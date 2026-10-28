using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Concurrent;
using RoAura.Models;

namespace RoAura.Services;

/// <summary>
/// Read-only Npcap listener. It never sends, blocks or modifies network data.
/// Wireshark installs the same Npcap driver and native wpcap library used here.
/// </summary>
public sealed class NpcapPacketCapture : IDisposable
{
    private readonly object _lifecycleLock = new();
    // DEV only: diagnostics are completely separate from XP, loot and status routing.
    private readonly CharacterSessionManager _characterSessions = new();
    private readonly PlayerAidObserver _playerAidObserver = new();

    /// <summary>Self actor ID for effect/cooldown tracking only; not CID attribution.</summary>
    public uint VerifiedPlayerAid
    {
        get
        {
            var session = _characterSessions.Current;
            if (session.Phase == CharacterSessionPhase.ActiveObserved &&
                _characterSessions.CurrentFlow is { } flow)
            {
                var evidence = _playerAidObserver.SourceForFlow(flow);
                // A conflicting actor identity cannot safely be attributed.
                if (evidence == "conflict") return 0;
                var observed = _playerAidObserver.AidForFlow(flow);
                if (observed != 0) return observed;
                return session.PlayerAid;
            }
            return _playerAidObserver.Aid;
        }
    }

    public string PlayerAidIdentitySource
    {
        get
        {
            var session = _characterSessions.Current;
            if (session.Phase == CharacterSessionPhase.ActiveObserved &&
                _characterSessions.CurrentFlow is { } flow)
            {
                var source = _playerAidObserver.SourceForFlow(flow);
                if (source.Length > 0) return source;
                if (session.PlayerAid != 0) return "map-login";
            }
            return _playerAidObserver.Source;
        }
    }
    public CharacterSessionSnapshot CharacterSession => _characterSessions.Current;
    public event Action<CharacterSessionSnapshot>? CharacterSessionChanged;
    public bool IsCurrentCharacterSession(CharacterSessionSnapshot snapshot) =>
        AppEnvironment.IsDevelopment && _characterSessions.IsCurrent(snapshot);

    public NpcapPacketCapture()
    {
        if (AppEnvironment.IsDevelopment)
            _characterSessions.StateChanged += snapshot =>
            {
                DebugLog.Write($"CHARACTER SESSION SHADOW: phase={snapshot.Phase}; " +
                               $"server={snapshot.ServerKey}; cid={snapshot.CharacterId}; " +
                               $"clientPort={snapshot.ClientPort}; mapPort={snapshot.MapPort}");
                CharacterSessionChanged?.Invoke(snapshot);
            };
    }
    private CancellationTokenSource? _cancellation;
    private int _generation;
    private int _activeDevices;
    private int _rejectedOptionDiagnosticCount;
    private int _activeGeneration;
    private string _lastState = "";
    private string _language = "ru";
    private ConcurrentDictionary<ushort, RoEquipmentPacket> _equipmentIdentityCache = new();
    private ConcurrentDictionary<ushort, RoEquipmentPacket> _pendingEquipmentEvents = new();

    public event Action<RoStatusPacket>? StatusChanged;
    public event Action<RoOptionPacket>? OptionChanged;
    public event Action<RoSkillPacket>? SkillUsed;
    public event Action<RoSkillCooldownPacket>? SkillCooldown;
    public event Action<RoEquipmentPacket>? EquipmentChanged;
    public event Action<RoItemAddedPacket>? ItemAdded;
    public event Action<RoExperiencePacket>? ExperienceChanged;
    public event Action<RoProgressPacket>? ProgressChanged;
    public event Action<RoZenyPacket>? ZenyChanged;
    // DEV-only: immutable source character session accompanies each reward packet.
    public event Action<CharacterSessionPacket<RoItemAddedPacket>>? CharacterItemAdded;
    public event Action<CharacterSessionPacket<RoExperiencePacket>>? CharacterExperienceChanged;
    public event Action<CharacterSessionPacket<RoProgressPacket>>? CharacterProgressChanged;
    public event Action<CharacterSessionPacket<RoZenyPacket>>? CharacterZenyChanged;
    public event Action<string>? StateChanged;

    public static bool IsNpcapInstalled() => NpcapNative.FindLibraryFolder() is not null;
    public bool IsActive => Volatile.Read(ref _activeDevices) > 0;

    public void ApplySettings(AppSettings settings)
    {
        _language = settings.Language;
        CancellationTokenSource? oldCancellation;
        CancellationTokenSource? newCancellation = null;
        ConcurrentDictionary<ushort, RoEquipmentPacket> equipmentCache;
        ConcurrentDictionary<ushort, RoEquipmentPacket> pendingEquipmentEvents;
        ConcurrentDictionary<TcpFlowKey, TcpFlowState> tcpFlows;
        CharacterLoginObserver? loginObserver;
        int generation;
        lock (_lifecycleLock)
        {
            oldCancellation = _cancellation;
            oldCancellation?.Cancel();
            _cancellation = null;
            generation = ++_generation;
            _activeGeneration = generation;
            _activeDevices = 0;
            _rejectedOptionDiagnosticCount = 0;
            // Replace rather than clear: workers from the cancelled generation
            // may finish parsing one last frame and must not repopulate the new
            // connection's identity table.
            _equipmentIdentityCache = new();
            _pendingEquipmentEvents = new();
            equipmentCache = _equipmentIdentityCache;
            pendingEquipmentEvents = _pendingEquipmentEvents;
            tcpFlows = new();
            _playerAidObserver.Reset();
            if (AppEnvironment.IsDevelopment) _characterSessions.OnCaptureInterrupted();
            loginObserver = AppEnvironment.IsDevelopment ? new CharacterLoginObserver() : null;
            if (loginObserver is not null)
            {
                var serverKey = settings.ServerProfile;
                loginObserver.CandidateDetected += (flow, candidate) =>
                {
                    if (generation != Volatile.Read(ref _generation)) return;
                    _characterSessions.OnCandidate(serverKey,
                        CharacterSessionFlow.FromOutbound(flow), candidate.CharacterId,
                        candidate.PlayerAid);
                };
            }

            if (!settings.PacketCaptureEnabled)
            {
                SetState(generation, L("Пакеты: отключены", "Packets: disabled"));
                return;
            }

            newCancellation = new CancellationTokenSource();
            _cancellation = newCancellation;
        }

        var address = settings.PacketServerAddress;
        var port = settings.PacketServerPort;
        var cancellation = newCancellation!;
        SetState(generation, L("Пакеты: подключение к Npcap…", "Packets: connecting to Npcap…"));
        _ = Task.Run(() => StartCaptureAsync(address, port, generation, equipmentCache,
            pendingEquipmentEvents, tcpFlows, loginObserver, cancellation), CancellationToken.None);
    }

    private enum SessionResult
    {
        OpenFailed,
        ReadEnded
    }

    /// <summary>
    /// Per-generation adapter bookkeeping so StartCaptureAsync can report an
    /// aggregated result instead of the last worker's state.
    /// </summary>
    private sealed class GenerationStats
    {
        public int Opened;
        public int FailedFirstOpen;
    }

    private async Task StartCaptureAsync(string address, int port, int generation,
        ConcurrentDictionary<ushort, RoEquipmentPacket> equipmentCache,
        ConcurrentDictionary<ushort, RoEquipmentPacket> pendingEquipmentEvents,
        ConcurrentDictionary<TcpFlowKey, TcpFlowState> tcpFlows,
        CharacterLoginObserver? loginObserver,
        CancellationTokenSource cancellation)
    {
        var cancellationToken = cancellation.Token;
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                SetState(generation, L("Пакеты: доступны только в Windows", "Packets: available on Windows only"));
                return;
            }

            NpcapNative.EnsureLoaded();
            var devices = NpcapNative.GetDevices();
            if (devices.Count == 0)
            {
                SetState(generation, L("Пакеты: Npcap не нашёл сетевые интерфейсы", "Packets: Npcap found no network adapters"));
                return;
            }

            var filter = BuildFilter(address, port);
            var stats = new GenerationStats();
            var workers = devices.Select(device => Task.Run(
                () => CaptureDevice(device, filter, port, generation, stats, equipmentCache,
                    pendingEquipmentEvents, tcpFlows, loginObserver, cancellationToken),
                CancellationToken.None)).ToArray();

            // Report the aggregated first-open result once every adapter has
            // either opened or failed its first attempt; workers keep retrying
            // failed adapters with backoff in the background.
            var firstOpenReport = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested &&
                       generation == Volatile.Read(ref _generation))
                {
                    var opened = Volatile.Read(ref stats.Opened);
                    var failed = Volatile.Read(ref stats.FailedFirstOpen);
                    if (opened + failed >= devices.Count)
                    {
                        if (opened == 0)
                            SetState(generation, L(
                                "Пакеты: интерфейс не удалось открыть — попробуйте запуск от администратора",
                                "Packets: could not open an adapter — try running as administrator"));
                        return;
                    }
                    try { await Task.Delay(250, cancellationToken); }
                    catch (OperationCanceledException) { return; }
                }
            }, CancellationToken.None);

            await Task.WhenAll(workers);
            await firstOpenReport;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (DllNotFoundException)
        {
            SetState(generation, L("Пакеты: Npcap не установлен", "Packets: Npcap is not installed"));
        }
        catch (BadImageFormatException)
        {
            SetState(generation, L("Пакеты: разрядность Npcap не совпадает с RoAura", "Packets: Npcap architecture does not match RoAura"));
        }
        catch (Exception ex)
        {
            SetState(generation, L($"Пакеты: ошибка запуска — {ex.Message}", $"Packets: startup error — {ex.Message}"));
            DebugLog.Write($"PACKET CAPTURE START ERROR: {ex}");
        }
        finally
        {
            lock (_lifecycleLock)
            {
                if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private async Task CaptureDevice(PcapDeviceInfo device, string filter, int port, int generation,
        GenerationStats stats,
        ConcurrentDictionary<ushort, RoEquipmentPacket> equipmentCache,
        ConcurrentDictionary<ushort, RoEquipmentPacket> pendingEquipmentEvents,
        ConcurrentDictionary<TcpFlowKey, TcpFlowState> tcpFlows,
        CharacterLoginObserver? loginObserver,
        CancellationToken cancellationToken)
    {
        const int MinimumBackoffMs = 2000;
        const int MaximumBackoffMs = 60_000;
        const long FailureLogIntervalMs = 60_000;
        var attempts = 0;
        var backoffMs = MinimumBackoffMs;
        long lastFailureLogTick = 0;

        while (!cancellationToken.IsCancellationRequested &&
               generation == Volatile.Read(ref _generation))
        {
            attempts++;
            var logFailure = attempts == 1 ||
                             Environment.TickCount64 - lastFailureLogTick >= FailureLogIntervalMs;
            var result = CaptureDeviceSession(device, filter, port, generation, stats, equipmentCache,
                pendingEquipmentEvents, tcpFlows, loginObserver, cancellationToken, logFailure, attempts == 1);
            if (cancellationToken.IsCancellationRequested ||
                generation != Volatile.Read(ref _generation))
                break;

            int delayMs;
            if (result == SessionResult.OpenFailed)
            {
                if (attempts == 1) Interlocked.Increment(ref stats.FailedFirstOpen);
                if (logFailure) lastFailureLogTick = Environment.TickCount64;
                // An adapter that cannot be opened (no privileges, disabled
                // interface, VPN tunnel without Npcap binding) must not spam the
                // log every two seconds: back off exponentially up to a minute.
                delayMs = backoffMs;
                backoffMs = Math.Min(backoffMs * 2, MaximumBackoffMs);
            }
            else
            {
                // Npcap handles can be invalidated when a VPN/accelerator changes
                // routes or an adapter is briefly reset. A dead worker used to stay
                // dead until the user reset AID and relogged. Reopen it in place so
                // packet tracking recovers without touching the current settings.
                backoffMs = MinimumBackoffMs;
                delayMs = MinimumBackoffMs;
                DebugLog.Write($"PCAP REOPEN {device.Description}: retry in 2 seconds");
            }

            try
            {
                await Task.Delay(delayMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private SessionResult CaptureDeviceSession(PcapDeviceInfo device, string filter, int port, int generation,
        GenerationStats stats,
        ConcurrentDictionary<ushort, RoEquipmentPacket> equipmentCache,
        ConcurrentDictionary<ushort, RoEquipmentPacket> pendingEquipmentEvents,
        ConcurrentDictionary<TcpFlowKey, TcpFlowState> tcpFlows,
        CharacterLoginObserver? loginObserver,
        CancellationToken cancellationToken, bool logFailure, bool firstAttempt)
    {
        IntPtr handle = IntPtr.Zero;
        var countedAsActive = false;
        var opened = false;
        // pcap_breakloop wakes a blocked pcap_next_ex on cancellation. The
        // registration is disposed before pcap_close so it can never touch a
        // freed handle; the lock covers a cancellation racing with the close.
        var handleLock = new object();
        CancellationTokenRegistration breakRegistration = default;
        try
        {
            var error = new StringBuilder(NpcapNative.ErrorBufferSize);
            handle = NpcapNative.pcap_create(device.Name, error);
            if (handle == IntPtr.Zero)
            {
                if (logFailure) DebugLog.Write($"PCAP CREATE FAILED {device.Description}: {error}");
                return SessionResult.OpenFailed;
            }

            // Immediate mode delivers each frame as soon as the driver sees it
            // instead of holding it until the kernel buffer fills or the read
            // timeout elapses; pcap_open_live cannot request this.
            NpcapNative.pcap_set_snaplen(handle, 65535);
            NpcapNative.pcap_set_promisc(handle, 0);
            NpcapNative.pcap_set_timeout(handle, 500);
            NpcapNative.pcap_set_immediate_mode(handle, 1);
            var activation = NpcapNative.pcap_activate(handle);
            if (activation < 0)
            {
                if (logFailure)
                    DebugLog.Write($"PCAP OPEN FAILED {device.Description}: " +
                                   $"status={activation}; {NpcapNative.GetError(handle)}");
                return SessionResult.OpenFailed;
            }
            if (activation > 0)
                DebugLog.Write($"PCAP OPEN WARNING {device.Description}: " +
                               $"status={activation}; {NpcapNative.GetError(handle)}");

            if (NpcapNative.pcap_compile(handle, out var program, filter, 1, uint.MaxValue) != 0)
            {
                if (logFailure)
                    DebugLog.Write($"PCAP FILTER COMPILE FAILED {device.Description}: " +
                                   NpcapNative.GetError(handle));
                return SessionResult.OpenFailed;
            }

            try
            {
                if (NpcapNative.pcap_setfilter(handle, ref program) != 0)
                {
                    if (logFailure)
                        DebugLog.Write($"PCAP FILTER SET FAILED {device.Description}: " +
                                       NpcapNative.GetError(handle));
                    return SessionResult.OpenFailed;
                }
            }
            finally
            {
                NpcapNative.pcap_freecode(ref program);
            }

            opened = true;
            if (firstAttempt) Interlocked.Increment(ref stats.Opened);

            var linkType = NpcapNative.pcap_datalink(handle);
            var active = IncrementActive(generation);
            countedAsActive = true;
            SetState(generation, L($"Пакеты: слушаю {active} интерфейс(а), фильтр {filter}",
                $"Packets: listening on {active} adapter(s), filter {filter}"));
            DebugLog.Write($"PCAP LISTEN {device.Description}: dlt={linkType}; filter={filter}");

            breakRegistration = cancellationToken.Register(() =>
            {
                lock (handleLock)
                {
                    if (handle != IntPtr.Zero) NpcapNative.pcap_breakloop(handle);
                }
            });

            while (!cancellationToken.IsCancellationRequested &&
                   generation == Volatile.Read(ref _generation))
            {
                var result = NpcapNative.pcap_next_ex(handle, out var headerPointer,
                    out var dataPointer);
                if (result == 0) continue;
                if (result < 0)
                {
                    // -2 means pcap_breakloop was called (cancellation).
                    if (result != -2 && !cancellationToken.IsCancellationRequested)
                        DebugLog.Write($"PCAP READ STOP {device.Description}: " +
                                       NpcapNative.GetError(handle));
                    break;
                }

                var header = Marshal.PtrToStructure<PcapPacketHeader>(headerPointer);
                if (header.CapturedLength == 0 || header.CapturedLength > 1_048_576)
                    continue;

                var frame = new byte[(int)header.CapturedLength];
                Marshal.Copy(dataPointer, frame, 0, frame.Length);
                if (NetworkFrameParser.TryGetIpv4TcpPayload(frame, linkType,
                        out var payload, out var flowKey, out var sequence, out var tcpFlags))
                {
                    var closing = (tcpFlags & 0x05) != 0; // FIN or RST
                    // DEV only: client-to-server first-packet inspection runs in
                    // a separate bounded observer. NEVER feed outbound bytes
                    // into the existing server-to-client packet parser.
                    if (loginObserver is not null &&
                        flowKey.DestinationPort == port && flowKey.SourcePort != port)
                    {
                        loginObserver.Observe(flowKey, sequence, payload, closing);
                        if (closing && generation == Volatile.Read(ref _generation))
                        {
                            _characterSessions.OnConnectionClosed(CharacterSessionFlow.FromOutbound(flowKey));
                            _playerAidObserver.OnClosed(CharacterSessionFlow.FromOutbound(flowKey));
                        }
                        continue;
                    }
                    if (flowKey.SourcePort != port || flowKey.DestinationPort == port)
                        continue;

                    if (!tcpFlows.TryGetValue(flowKey, out var flow))
                    {
                        if (payload.IsEmpty) continue; // Bare FIN/RST for an unknown flow.
                        PruneIdleFlows(tcpFlows);
                        flow = tcpFlows.GetOrAdd(flowKey, _ => new TcpFlowState(
                            CreateScanner(generation, equipmentCache, pendingEquipmentEvents,
                                CharacterSessionFlow.FromInbound(flowKey))));
                    }
                    if (AppEnvironment.IsDevelopment && generation == Volatile.Read(ref _generation) &&
                        !payload.IsEmpty)
                        _characterSessions.OnServerPayload(CharacterSessionFlow.FromInbound(flowKey));
                    flow.Process(sequence, payload);
                    if (AppEnvironment.IsDevelopment && generation == Volatile.Read(ref _generation) && closing)
                    {
                        _characterSessions.OnConnectionClosed(CharacterSessionFlow.FromInbound(flowKey));
                        _playerAidObserver.OnClosed(CharacterSessionFlow.FromInbound(flowKey));
                    }
                    // The server side is done with this stream: drop it so the
                    // flow table cannot grow with every reconnect.
                    if (closing) tcpFlows.TryRemove(flowKey, out _);
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"PCAP DEVICE ERROR {device.Description}: {ex}");
        }
        finally
        {
            breakRegistration.Dispose();
            lock (handleLock)
            {
                if (handle != IntPtr.Zero) NpcapNative.pcap_close(handle);
                handle = IntPtr.Zero;
            }
            if (countedAsActive) DecrementActive(generation);
        }
        return opened ? SessionResult.ReadEnded : SessionResult.OpenFailed;
    }

    private const long FlowIdleLimitMs = 10 * 60 * 1000;

    private static void PruneIdleFlows(ConcurrentDictionary<TcpFlowKey, TcpFlowState> tcpFlows)
    {
        if (tcpFlows.IsEmpty) return;
        var now = Environment.TickCount64;
        foreach (var pair in tcpFlows)
        {
            if (now - pair.Value.LastSeenTick > FlowIdleLimitMs)
                tcpFlows.TryRemove(pair.Key, out _);
        }
    }

    private int IncrementActive(int generation)
    {
        lock (_lifecycleLock)
        {
            if (_activeGeneration != generation)
            {
                // A newer generation already started or an older one is still
                // winding down: counts belong to the newest generation only.
                if (generation < _activeGeneration) return 0;
                _activeGeneration = generation;
                _activeDevices = 0;
            }
            return ++_activeDevices;
        }
    }

    private void DecrementActive(int generation)
    {
        lock (_lifecycleLock)
        {
            if (_activeGeneration == generation && _activeDevices > 0) _activeDevices--;
        }
    }

    private RoPacketStreamScanner CreateScanner(int generation,
        ConcurrentDictionary<ushort, RoEquipmentPacket> equipmentCache,
        ConcurrentDictionary<ushort, RoEquipmentPacket> pendingEquipmentEvents,
        CharacterSessionFlow packetFlow)
    {
        var scanner = new RoPacketStreamScanner(equipmentCache);
        scanner.StatusChanged += packet => Forward(generation, () => StatusChanged?.Invoke(packet));
        if (AppEnvironment.IsDevelopment)
            scanner.OptionCandidateRejected += opcode => Forward(generation, () =>
            {
                if (Interlocked.Increment(ref _rejectedOptionDiagnosticCount) <= 20)
                    DebugLog.Write($"PACKET OPTION CANDIDATE REJECTED opcode=0x{opcode:X4}; " +
                                   "possible packetver/layout mismatch or embedded signature");
            });
        if (AppEnvironment.IsDevelopment)
            scanner.SelfAidObserved += packet => Forward(generation, () =>
                ObservePlayerAid(packetFlow, packet.Aid, "ZC_AID"));
        // Actor visibility (not always a skill ack): TF_HIDING is confirmed
        // by OPTION_HIDE, while EFST_HIDING is a separate status icon.
        bool? lastHidden = null;
        scanner.OptionChanged += packet => Forward(generation, () =>
        {
            OptionChanged?.Invoke(packet);
            // EFST_HIDING = 4, TF_HIDING = 51 in standard RO.
            // Synthetic status carries no guessed duration.
            StatusChanged?.Invoke(new RoStatusPacket(packet.PacketId, 4, packet.Aid,
                packet.IsHidden, 0, 0));
            if (packet.IsHidden && lastHidden != true)
                SkillUsed?.Invoke(new RoSkillPacket(51, packet.Aid, packet.Aid, 0));
            lastHidden = packet.IsHidden;
        });
        scanner.SkillUsed += packet => Forward(generation, () => SkillUsed?.Invoke(packet));
        scanner.SkillCooldown += packet => Forward(generation, () => SkillCooldown?.Invoke(packet));
        scanner.EquipmentChanged += packet => HandleEquipmentPacket(generation, packet,
            equipmentCache, pendingEquipmentEvents);
        scanner.ItemAdded += packet => Forward(generation, () =>
        {
            ItemAdded?.Invoke(packet);
            ForwardCharacterPacket(packetFlow, packet, CharacterItemAdded);
        });
        scanner.ExperienceChanged += packet => Forward(generation, () =>
        {
            if (AppEnvironment.IsDevelopment)
                ObservePlayerAid(packetFlow, packet.Aid, "EXP");
            ExperienceChanged?.Invoke(packet);
            ForwardCharacterPacket(packetFlow, packet, CharacterExperienceChanged);
        });
        scanner.ProgressChanged += packet => Forward(generation, () =>
        {
            ProgressChanged?.Invoke(packet);
            ForwardCharacterPacket(packetFlow, packet, CharacterProgressChanged);
        });
        scanner.ZenyChanged += packet => Forward(generation, () =>
        {
            ZenyChanged?.Invoke(packet);
            ForwardCharacterPacket(packetFlow, packet, CharacterZenyChanged);
        });
        return scanner;
    }

    private void ObservePlayerAid(CharacterSessionFlow flow, uint aid, string source)
    {
        if (_playerAidObserver.Observe(flow, aid, source))
        {
            var current = _characterSessions.Current;
            var sameFlow = _characterSessions.CurrentFlow == flow;
            var comparison = sameFlow && current.PlayerAid != 0
                ? (current.PlayerAid == aid ? "agree" : "disagree")
                : "unavailable";
            DebugLog.Write($"PLAYER SELF AID VERIFIED source={source}; " +
                           $"mapLoginComparison={comparison}; " +
                           $"clientPort={flow.ClientPort}; mapPort={flow.ServerPort}");
        }
    }

    private void ForwardCharacterPacket<T>(CharacterSessionFlow flow, T packet,
        Action<CharacterSessionPacket<T>>? callback)
    {
        if (!AppEnvironment.IsDevelopment || callback is null ||
            !_characterSessions.TryResolve(flow, out var session)) return;
        callback(new CharacterSessionPacket<T>(session, packet));
    }

    private void Forward(int generation, Action action)
    {
        if (generation == Volatile.Read(ref _generation)) action();
    }

    private void HandleEquipmentPacket(int generation, RoEquipmentPacket packet,
        ConcurrentDictionary<ushort, RoEquipmentPacket> equipmentCache,
        ConcurrentDictionary<ushort, RoEquipmentPacket> pendingEquipmentEvents)
    {
        if (generation != Volatile.Read(ref _generation)) return;

        if (packet.ItemId > 0)
        {
            equipmentCache[packet.InventoryIndex] = packet;
            EquipmentChanged?.Invoke(packet);

            // An equip/unequip acknowledgement can arrive on one adapter before
            // the inventory list is observed on another. Replay it as soon as
            // the complete identity becomes available so swaps are never lost.
            if (pendingEquipmentEvents.TryRemove(packet.InventoryIndex, out var pending))
            {
                var resolved = CopyEquipmentIdentity(pending, packet);
                DebugLog.Write($"PACKET EQUIPMENT RESOLVED: packet=0x{resolved.PacketId:X4}; " +
                               $"index={resolved.InventoryIndex}; item={resolved.ItemId}; " +
                               $"variant={resolved.VariantKey}");
                EquipmentChanged?.Invoke(resolved);
            }
            return;
        }

        if (equipmentCache.TryGetValue(packet.InventoryIndex, out var identity))
        {
            EquipmentChanged?.Invoke(CopyEquipmentIdentity(packet, identity));
            return;
        }

        // Do not forward an unidentified acknowledgement: it cannot be matched
        // reliably and could incorrectly clear another item in the same slot.
        // Keep the latest state until the inventory identity arrives.
        pendingEquipmentEvents[packet.InventoryIndex] = packet;
        DebugLog.Write($"PACKET EQUIPMENT PENDING: packet=0x{packet.PacketId:X4}; " +
                       $"index={packet.InventoryIndex}; location=0x{packet.WearLocation:X}; " +
                       $"equipped={packet.Equipped}");
    }

    private static RoEquipmentPacket CopyEquipmentIdentity(RoEquipmentPacket target,
        RoEquipmentPacket identity) => target with
    {
        ItemId = identity.ItemId,
        RefineLevel = identity.RefineLevel,
        Grade = identity.Grade,
        Card1 = identity.Card1,
        Card2 = identity.Card2,
        Card3 = identity.Card3,
        Card4 = identity.Card4,
        OptionsSignature = identity.OptionsSignature
    };

    private static string BuildFilter(string address, int port)
    {
        if (IPAddress.TryParse(address, out var parsed) &&
            parsed.AddressFamily == AddressFamily.InterNetwork)
            return AppEnvironment.IsDevelopment
                ? $"host {parsed} and tcp port {port}"
                : $"src host {parsed} and tcp src port {port}";
        return AppEnvironment.IsDevelopment ? $"tcp port {port}" : $"tcp src port {port}";
    }

    private void SetState(int generation, string state)
    {
        // A worker from a cancelled generation must never overwrite the
        // status of the generation that replaced it.
        if (generation != Volatile.Read(ref _generation)) return;
        lock (_lifecycleLock)
        {
            if (generation != _generation) return;
            if (string.Equals(_lastState, state, StringComparison.Ordinal)) return;
            _lastState = state;
        }
        DebugLog.Write(state.ToUpperInvariant());
        StateChanged?.Invoke(state);
    }

    private string L(string ru, string en) => _language == "en" ? en : ru;

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            _generation++;
            // The capture task owning this source disposes it when it finishes.
            _cancellation?.Cancel();
            _cancellation = null;
        }
    }
}

internal readonly record struct PcapDeviceInfo(string Name, string Description);

[StructLayout(LayoutKind.Sequential)]
internal struct PcapPacketHeader
{
    public int Seconds;
    public int Microseconds;
    public uint CapturedLength;
    public uint OriginalLength;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BpfProgram
{
    public uint InstructionCount;
    public IntPtr Instructions;
}

internal static class NpcapNative
{
    internal const int ErrorBufferSize = 256;
    private const string LibraryName = "wpcap";
    private static readonly object LoadLock = new();
    private static bool _loaded;
    private static IntPtr _wpcapHandle;
    private static IntPtr _packetHandle;

    internal static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (LoadLock)
        {
            if (_loaded) return;

            var folder = FindLibraryFolder();
            if (folder is not null)
            {
                var wpcapPath = Path.Combine(folder, "wpcap.dll");
                SetDllDirectory(folder);
                var packetPath = Path.Combine(folder, "Packet.dll");
                if (File.Exists(packetPath))
                    _packetHandle = NativeLibrary.Load(packetPath);
                _wpcapHandle = NativeLibrary.Load(wpcapPath);
                NativeLibrary.SetDllImportResolver(typeof(NpcapNative).Assembly,
                    (name, _, _) => string.Equals(name, LibraryName,
                        StringComparison.OrdinalIgnoreCase) ? _wpcapHandle : IntPtr.Zero);
                _loaded = true;
                return;
            }

            throw new DllNotFoundException("wpcap.dll");
        }
    }

    internal static string? FindLibraryFolder()
    {
        if (!OperatingSystem.IsWindows()) return null;

        var folders = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Npcap"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Npcap"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Wireshark"),
            AppContext.BaseDirectory
        }.Distinct(StringComparer.OrdinalIgnoreCase);

        return folders.FirstOrDefault(folder =>
            File.Exists(Path.Combine(folder, "wpcap.dll")) &&
            File.Exists(Path.Combine(folder, "Packet.dll")));
    }

    internal static List<PcapDeviceInfo> GetDevices()
    {
        var error = new StringBuilder(ErrorBufferSize);
        if (pcap_findalldevs(out var first, error) != 0)
            throw new InvalidOperationException(error.ToString());

        var result = new List<PcapDeviceInfo>();
        try
        {
            var current = first;
            while (current != IntPtr.Zero)
            {
                var native = Marshal.PtrToStructure<PcapInterface>(current);
                var name = Marshal.PtrToStringAnsi(native.Name);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var description = Marshal.PtrToStringAnsi(native.Description) ?? name;
                    result.Add(new PcapDeviceInfo(name, description));
                }
                current = native.Next;
            }
        }
        finally
        {
            if (first != IntPtr.Zero) pcap_freealldevs(first);
        }
        return result;
    }

    internal static string GetError(IntPtr handle) =>
        Marshal.PtrToStringAnsi(pcap_geterr(handle)) ?? "неизвестная ошибка Npcap";

    [StructLayout(LayoutKind.Sequential)]
    private struct PcapInterface
    {
        public IntPtr Next;
        public IntPtr Name;
        public IntPtr Description;
        public IntPtr Addresses;
        public uint Flags;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDllDirectory(string path);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Ansi)]
    internal static extern int pcap_findalldevs(out IntPtr devices, StringBuilder errorBuffer);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void pcap_freealldevs(IntPtr devices);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Ansi)]
    internal static extern IntPtr pcap_create(string device, StringBuilder errorBuffer);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int pcap_set_snaplen(IntPtr handle, int snapshotLength);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int pcap_set_promisc(IntPtr handle, int promiscuous);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int pcap_set_timeout(IntPtr handle, int readTimeoutMs);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int pcap_set_immediate_mode(IntPtr handle, int immediate);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int pcap_activate(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void pcap_breakloop(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Ansi)]
    internal static extern int pcap_compile(IntPtr handle, out BpfProgram program,
        string filter, int optimize, uint netmask);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int pcap_setfilter(IntPtr handle, ref BpfProgram program);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void pcap_freecode(ref BpfProgram program);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int pcap_datalink(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int pcap_next_ex(IntPtr handle, out IntPtr header,
        out IntPtr data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr pcap_geterr(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void pcap_close(IntPtr handle);
}

internal readonly record struct TcpFlowKey(
    uint SourceAddress,
    ushort SourcePort,
    uint DestinationAddress,
    ushort DestinationPort);

/// <summary>
/// Tracks one server-to-client TCP byte stream. Npcap may expose the same segment
/// on Ethernet, loopback and a VPN adapter; sequence numbers discard duplicates.
/// A capture gap resets only the incomplete packet tail and resumes immediately,
/// so one missed segment can never freeze every packet-driven feature.
/// </summary>
internal sealed class TcpFlowState
{
    private const uint MaximumRetransmissionWindow = 1_048_576;
    private readonly object _sync = new();
    private readonly RoPacketStreamScanner _scanner;
    private bool _initialized;
    private uint _nextSequence;
    private long _lastSeenTick = Environment.TickCount64;

    internal TcpFlowState(RoPacketStreamScanner scanner) => _scanner = scanner;

    internal long LastSeenTick => Volatile.Read(ref _lastSeenTick);

    internal void Process(uint sequence, ReadOnlySpan<byte> payload)
    {
        Volatile.Write(ref _lastSeenTick, Environment.TickCount64);
        if (payload.IsEmpty) return;
        lock (_sync)
        {
            if (!_initialized)
            {
                _initialized = true;
                ProcessContiguous(sequence, payload);
                return;
            }

            var delta = unchecked((int)(sequence - _nextSequence));
            if (delta < 0)
            {
                var backwardDistance = unchecked(_nextSequence - sequence);
                if (backwardDistance > MaximumRetransmissionWindow)
                {
                    // The same four-tuple was reused by a new TCP session and
                    // sequence numbers restarted. Treat it as a fresh stream;
                    // otherwise every new packet would look like an ancient
                    // retransmission and capture would remain frozen forever.
                    DebugLog.Write($"TCP SESSION RESET: previous={_nextSequence}; actual={sequence}");
                    _scanner.ResetStream();
                    ProcessContiguous(sequence, payload);
                    return;
                }

                var overlap = (int)backwardDistance;
                if (overlap >= payload.Length) return; // Full retransmission.
                sequence = _nextSequence;
                payload = payload[overlap..];
                delta = 0;
            }

            if (delta == 0)
            {
                ProcessContiguous(sequence, payload);
                return;
            }

            // A capture gap must never block all future buffs, skills and gear.
            // Drop only the incomplete scanner tail and continue immediately at
            // the next observed TCP segment.
            DebugLog.Write($"TCP STREAM GAP: expected={_nextSequence}; actual={sequence}; " +
                           $"missing={unchecked((uint)(sequence - _nextSequence))}");
            _scanner.ResetStream();
            ProcessContiguous(sequence, payload);
        }
    }

    private void ProcessContiguous(uint sequence, ReadOnlySpan<byte> payload)
    {
        _scanner.Process(payload);
        _nextSequence = unchecked(sequence + (uint)payload.Length);
    }

}

internal static class NetworkFrameParser
{
    internal static bool TryGetIpv4TcpPayload(byte[] frame, int linkType,
        out ReadOnlySpan<byte> payload, out TcpFlowKey flowKey, out uint sequence,
        out byte tcpFlags)
    {
        payload = default;
        flowKey = default;
        sequence = 0;
        tcpFlags = 0;
        var ipOffset = GetIpv4Offset(frame, linkType);
        if (ipOffset < 0 || frame.Length < ipOffset + 20 ||
            (frame[ipOffset] >> 4) != 4)
            return false;

        var ipHeaderLength = (frame[ipOffset] & 0x0F) * 4;
        if (ipHeaderLength < 20 || frame.Length < ipOffset + ipHeaderLength ||
            frame[ipOffset + 9] != 6)
            return false;

        var fragment = (ushort)((frame[ipOffset + 6] << 8) | frame[ipOffset + 7]);
        if ((fragment & 0x1FFF) != 0) return false;

        // Offloaded (TSO/LRO) frames report a zero or oversized IP length;
        // fall back to the captured frame length in both cases.
        var totalLength = (frame[ipOffset + 2] << 8) | frame[ipOffset + 3];
        var ipEnd = totalLength == 0 || ipOffset + totalLength > frame.Length
            ? frame.Length
            : ipOffset + totalLength;
        var tcpOffset = ipOffset + ipHeaderLength;
        if (ipEnd < tcpOffset + 20) return false;

        var tcpHeaderLength = (frame[tcpOffset + 12] >> 4) * 4;
        tcpFlags = frame[tcpOffset + 13];
        var payloadOffset = tcpOffset + tcpHeaderLength;
        if (tcpHeaderLength < 20) return false;
        // A pure FIN/RST segment carries no payload but must still close the
        // flow, so report it with an empty payload.
        if (payloadOffset >= ipEnd)
        {
            if ((tcpFlags & 0x05) == 0) return false;
            flowKey = new TcpFlowKey(
                BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(ipOffset + 12, 4)),
                BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(tcpOffset, 2)),
                BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(ipOffset + 16, 4)),
                BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(tcpOffset + 2, 2)));
            sequence = BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(tcpOffset + 4, 4));
            payload = ReadOnlySpan<byte>.Empty;
            return true;
        }

        flowKey = new TcpFlowKey(
            BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(ipOffset + 12, 4)),
            BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(tcpOffset, 2)),
            BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(ipOffset + 16, 4)),
            BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(tcpOffset + 2, 2)));
        sequence = BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(tcpOffset + 4, 4));
        payload = frame.AsSpan(payloadOffset, ipEnd - payloadOffset);
        return true;
    }

    private static int GetIpv4Offset(byte[] frame, int linkType)
    {
        switch (linkType)
        {
            case 1: // Ethernet
            {
                if (frame.Length < 14) return -1;
                var offset = 14;
                var etherType = (frame[12] << 8) | frame[13];
                while ((etherType == 0x8100 || etherType == 0x88A8) &&
                       frame.Length >= offset + 4)
                {
                    etherType = (frame[offset + 2] << 8) | frame[offset + 3];
                    offset += 4;
                }
                return etherType == 0x0800 ? offset : -1;
            }
            case 0: // BSD loopback / Npcap loopback
                return frame.Length >= 5 && (frame[4] >> 4) == 4 ? 4 : -1;
            case 12: // Raw IP
            case 228: // DLT_IPV4
                return frame.Length >= 1 && (frame[0] >> 4) == 4 ? 0 : -1;
            case 113: // Linux cooked capture v1
                return frame.Length >= 16 && frame[14] == 0x08 && frame[15] == 0x00 ? 16 : -1;
            case 276: // Linux cooked capture v2
                return frame.Length >= 20 && frame[0] == 0x08 && frame[1] == 0x00 ? 20 : -1;
            default:
                return -1;
        }
    }
}
