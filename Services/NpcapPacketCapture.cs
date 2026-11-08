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
    // Character identity is bound to the observed TCP flow.
    private readonly CharacterSessionManager _characterSessions = new();
    private readonly PlayerAidObserver _playerAidObserver = new();
    private readonly ConcurrentDictionary<CharacterSessionFlow, RoPacketSource> _trackingSources = new();

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
        AppEnvironment.CharacterSessionsEnabled && _characterSessions.IsCurrent(snapshot);

    public NpcapPacketCapture()
    {
        if (AppEnvironment.CharacterSessionsEnabled)
            _characterSessions.StateChanged += snapshot =>
            {
                DebugLog.Write($"CHARACTER SESSION SHADOW: phase={snapshot.Phase}; " +
                               $"server={snapshot.ServerKey}; cid={snapshot.CharacterId}; " +
                               $"clientPort={snapshot.ClientPort}; mapPort={snapshot.MapPort}");
                CharacterSessionChanged?.Invoke(snapshot);
            };
    }
    private readonly MapLoginHandoff _mapLoginHandoff = new();
    private readonly Dictionary<(int ProcessId, uint CharacterId), (CharacterRosterLevel Levels, long SeenAt)> _rosterLevels = new();
    private const int RosterLevelLifetimeMs = 120_000;
    private string? _discoveryServer;
    private CancellationTokenSource? _cancellation;
    private int _generation;
    private int _activeDevices;
    private int _rejectedOptionDiagnosticCount;
    private int _activeGeneration;
    private string _lastState = "";
    private string _language = "ru";

    private readonly PacketLayoutDiagnostics _layoutDiagnostics = new();
    private volatile bool _springRoStatusCorrelationEnabled;
    private readonly SpringRoStatusWireCapture _statusWireCapture = new();
    private SpringRoClientStatusBridge? _clientStatusBridge;
    private static bool WireSamplesRequested => AppEnvironment.IsDevelopment &&
        Environment.GetCommandLineArgs().Contains(SpringRoStatusWireCapture.Argument, StringComparer.Ordinal);

    public void BeginLearningDiagnostics()
    {
        if (!AppEnvironment.IsDevelopment) return;
        _layoutDiagnostics.Begin(DateTime.UtcNow);
        if (_springRoStatusCorrelationEnabled && WireSamplesRequested)
        {
            _statusWireCapture.Begin(DateTime.UtcNow);
            DebugLog.Write("SPRINGRO WIRE WINDOW started; maxSamples=16; durationSeconds=120; " +
                "contains=exact-status-option-skill-bytes-and-self-AID; optIn=true");
        }
        DebugLog.Write("PACKET LAYOUT WINDOW started; durationSeconds=120; maxCandidates=256; actorLabels=window-local");
    }

    public event Action<RoPacketObservation<RoStatusPacket>>? StatusChanged;
    public event Action<RoPacketObservation<RoOptionPacket>>? OptionChanged;
    public event Action<RoPacketObservation<RoSkillPacket>>? SkillUsed;
    public event Action<RoPacketObservation<RoSkillCooldownPacket>>? SkillCooldown;
    public event Action<RoPacketSource>? TrackingSourceClosed;
    public event Action<RoPacketObservation<RoEquipmentPacket>>? EquipmentChanged;
    public event Action<RoPacketObservation<RoEquipmentPacket>>? EquipmentIdentityMissing;
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
        ConcurrentDictionary<TcpFlowKey, TcpFlowState> tcpFlows;
        CharacterLoginObserver? loginObserver;
        var target = new CaptureTarget(settings.PacketServerAddress, settings.PacketServerPort,
            AppEnvironment.CharacterSessionsEnabled && settings.AutoDetectConnection && settings.PacketCaptureEnabled
                ? settings.AllowedProcesses.Select(name => Path.GetFileNameWithoutExtension(name.Trim()))
                    .Where(name => !string.IsNullOrEmpty(name)).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : null);
        int generation;
        lock (_lifecycleLock)
        {
            oldCancellation = _cancellation;
            oldCancellation?.Cancel();
            _cancellation = null;
            generation = ++_generation;
            foreach (var source in _trackingSources.Values) CloseTrackingSource(source.Flow);
            _activeGeneration = generation;
            _activeDevices = 0;
            _rejectedOptionDiagnosticCount = 0;
            _statusWireCapture.Reset();
            _clientStatusBridge?.Dispose();
            _clientStatusBridge = null;
            _springRoStatusCorrelationEnabled =
                settings.PacketCaptureEnabled && !AppEnvironment.IsSmokeTest && GameServerProfile.UsesSpringRoClientStatus(settings.ServerProfile);
            if (_springRoStatusCorrelationEnabled)
            {
                _clientStatusBridge = new SpringRoClientStatusBridge();
                var bridge = _clientStatusBridge;
                DebugLog.Write("SPRINGRO CLIENT STATUS enabled; mode=read-only; " +
                               "requires=single-matching-client");
                _ = Task.Run(bridge.Prepare);
            }
            if (AppEnvironment.IsDevelopment) _layoutDiagnostics.Begin(DateTime.UtcNow);
            tcpFlows = new();
            _playerAidObserver.Reset();
            if (AppEnvironment.CharacterSessionsEnabled) _characterSessions.OnCaptureInterrupted();
            var discoveryServer = target.AutoDiscovery ? settings.ServerProfile : null;
            if (discoveryServer is null || _discoveryServer != discoveryServer)
            {
                _mapLoginHandoff.Clear();
                _rosterLevels.Clear();
            }
            _discoveryServer = discoveryServer;
            if (discoveryServer is not null &&
                _mapLoginHandoff.Take(target.Matches, target.IsOwnedGameConnection, Environment.TickCount64) is { } handoff)
            {
                _characterSessions.OnCandidate(discoveryServer, CharacterSessionFlow.FromOutbound(handoff.Flow),
                    handoff.Login.CharacterId, handoff.Login.PlayerAid);
                DebugLog.Write($"CHARACTER LOGIN RETAINED ACROSS ENDPOINT CHANGE: mapPort={handoff.Flow.DestinationPort}");
            }
            loginObserver = AppEnvironment.CharacterSessionsEnabled ? new CharacterLoginObserver() : null;
            if (loginObserver is not null)
            {
                var serverKey = settings.ServerProfile;
                loginObserver.CandidateDetected += (flow, candidate) =>
                {
                    lock (_lifecycleLock)
                    {
                        if (generation != _generation) return;
                        var selected = CharacterSessionFlow.FromOutbound(flow);
                        foreach (var source in _trackingSources.Values.Where(s => s.Flow != selected))
                            CloseTrackingSource(source.Flow, source);
                        _characterSessions.OnCandidate(serverKey,
                            selected, candidate.CharacterId, candidate.PlayerAid);
                    }
                };
            }

            if (!settings.PacketCaptureEnabled || AppEnvironment.IsSmokeTest)
            {
                SetState(generation, L("Пакеты: отключены", "Packets: disabled"));
                return;
            }

            newCancellation = new CancellationTokenSource();
            _cancellation = newCancellation;
        }

        var cancellation = newCancellation!;
        SetState(generation, L("Пакеты: подключение к Npcap…", "Packets: connecting to Npcap…"));
        _ = Task.Run(() => StartCaptureAsync(target, generation, tcpFlows, loginObserver, cancellation), CancellationToken.None);
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

    private async Task StartCaptureAsync(CaptureTarget target, int generation,
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

            var filter = BuildFilter(target.Address, target.Port);
            if (target.AutoDiscovery)
            {
                // Other connections contribute only SYN metadata and matching
                // login or roster headers. Their gameplay bytes stay filtered.
                const string opcode = "tcp[((tcp[12] & 0xf0) >> 2):2]";
                filter = $"({filter}) or (ip and tcp and ((tcp[13] & 0x12) = 0x02 or " +
                         $"{opcode} = 0x7200 or {opcode} = 0x3604 or " +
                         $"{opcode} = 0x6b00 or {opcode} = 0x9d09 or {opcode} = 0x720b))";
            }
            var stats = new GenerationStats();
            var workers = devices.Select(device => Task.Run(
                () => CaptureDevice(device, filter, target, generation, stats, tcpFlows, loginObserver, cancellationToken),
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
            SetState(generation, L("Пакеты: не удалось запустить захват", "Packets: could not start capture"));
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

    private async Task CaptureDevice(PcapDeviceInfo device, string filter, CaptureTarget target, int generation,
        GenerationStats stats,
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
            var result = CaptureDeviceSession(device, filter, target, generation, stats, tcpFlows, loginObserver, cancellationToken, logFailure, attempts == 1);
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

    private SessionResult CaptureDeviceSession(PcapDeviceInfo device, string filter, CaptureTarget target, int generation,
        GenerationStats stats,
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
            IncrementActive(generation);
            countedAsActive = true;
            SetState(generation, L("Пакеты: захват работает", "Packets: capture is active"));
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
                    var incoming = target.Matches(flowKey.SourceAddress, flowKey.SourcePort);
                    var outgoing = target.Matches(flowKey.DestinationAddress, flowKey.DestinationPort);
                    if (!incoming && !outgoing && target.AutoDiscovery)
                    {
                        var roster = CharacterRosterLevels.Read(payload);
                        if (roster.Length > 0)
                            RememberRosterLevels(generation, target, flowKey, roster);
                        lock (_lifecycleLock)
                        {
                            if (generation == _generation)
                                _mapLoginHandoff.Observe(flowKey, sequence, tcpFlags, payload,
                                    target.IsOwnedGameConnection, Environment.TickCount64);
                        }
                        continue;
                    }

                    // Client-to-server first-packet inspection runs in
                    // a separate bounded observer. NEVER feed outbound bytes
                    // into the existing server-to-client packet parser.
                    if (loginObserver is not null &&
                        outgoing && !incoming)
                    {
                        loginObserver.Observe(flowKey, sequence, payload, closing);
                        if (closing && generation == Volatile.Read(ref _generation))
                        {
                            _characterSessions.OnConnectionClosed(CharacterSessionFlow.FromOutbound(flowKey));
                            _playerAidObserver.OnClosed(CharacterSessionFlow.FromOutbound(flowKey));
                            CloseTrackingSource(CharacterSessionFlow.FromOutbound(flowKey));
                        }
                        continue;
                    }
                    if (!incoming || outgoing)
                        continue;

                    if (!tcpFlows.TryGetValue(flowKey, out var flow))
                    {
                        if (payload.IsEmpty) continue; // Bare FIN/RST for an unknown flow.
                        // GetOrAdd factories can run more than once across
                        // adapters. Only the retained scanner may register the
                        // live source identity for this flow.
                        lock (tcpFlows)
                        {
                            if (!tcpFlows.TryGetValue(flowKey, out flow))
                            {
                                PruneIdleFlows(tcpFlows, generation);
                                var scanner = CreateScanner(generation, CharacterSessionFlow.FromInbound(flowKey));
                                if (target.AutoDiscovery)
                                    scanner.CharacterRosterObserved += roster => RememberRosterLevels(generation, target, flowKey, roster);
                                flow = new TcpFlowState(scanner);
                                tcpFlows[flowKey] = flow;
                            }
                        }
                    }
                    if (AppEnvironment.CharacterSessionsEnabled && generation == Volatile.Read(ref _generation) &&
                        !payload.IsEmpty)
                    {
                        var sessionFlow = CharacterSessionFlow.FromInbound(flowKey);
                        if (_characterSessions.Current.Phase == CharacterSessionPhase.Connecting)
                        {
                            lock (_lifecycleLock)
                            {
                                if (generation == _generation &&
                                    _characterSessions.Current.Phase == CharacterSessionPhase.Connecting &&
                                    _characterSessions.CurrentFlow == sessionFlow)
                                {
                                    _characterSessions.OnServerPayload(sessionFlow);
                                    if (target.AutoDiscovery)
                                        ApplyRosterLevels(generation, target, flowKey, sessionFlow);
                                }
                            }
                        }
                    }
                    flow.Process(sequence, payload);
                    if (AppEnvironment.CharacterSessionsEnabled && generation == Volatile.Read(ref _generation) && closing)
                    {
                        loginObserver?.Observe(new TcpFlowKey(flowKey.DestinationAddress, flowKey.DestinationPort,
                            flowKey.SourceAddress, flowKey.SourcePort), 0, ReadOnlySpan<byte>.Empty, closing: true);
                        _characterSessions.OnConnectionClosed(CharacterSessionFlow.FromInbound(flowKey));
                        _playerAidObserver.OnClosed(CharacterSessionFlow.FromInbound(flowKey));
                        CloseTrackingSource(CharacterSessionFlow.FromInbound(flowKey));
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

    private void PruneIdleFlows(ConcurrentDictionary<TcpFlowKey, TcpFlowState> tcpFlows, int generation)
    {
        if (tcpFlows.IsEmpty) return;
        var now = Environment.TickCount64;
        foreach (var pair in tcpFlows)
        {
            if (now - pair.Value.LastSeenTick > FlowIdleLimitMs && tcpFlows.TryRemove(pair.Key, out _))
            {
                var key = CharacterSessionFlow.FromInbound(pair.Key);
                if (_trackingSources.TryGetValue(key, out var source) && source.Generation == generation)
                    CloseTrackingSource(key, source);
            }
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
        CharacterSessionFlow packetFlow)
    {
        // All adapters share the retained TcpFlowState/scanner, but different
        // TCP lifetimes must never resolve the same inventory index together.
        var equipmentCache = new ConcurrentDictionary<ushort, RoEquipmentPacket>();
        var pendingEquipmentEvents = new ConcurrentDictionary<ushort, RoPacketObservation<RoEquipmentPacket>>();
        var scanner = new RoPacketStreamScanner(equipmentCache);
        var clientStatusBridge = _clientStatusBridge;
        scanner.CaptureStatusWireBytes = _springRoStatusCorrelationEnabled &&
            (WireSamplesRequested || clientStatusBridge is not null);
        var source = RegisterTrackingSource(generation, packetFlow);
        var statusSkillCorrelation = _springRoStatusCorrelationEnabled
            ? new SpringRoStatusSkillCorrelationDiagnostics() : null;
        var correlatedHiding = statusSkillCorrelation is null
            ? null : new SpringRoCorrelatedHidingTracker();
        var hiding = new HidingStateTracker();
        var pendingStatuses = new PendingStatusObservations();
        bool? decodedHidingBeforeSkill = null;
        if (_springRoStatusCorrelationEnabled)
            scanner.LayoutProbed += probe => Forward(generation, () =>
            {
                var observation = Observe(probe, source);
                if (!IsCurrentObservation(observation)) return;
                if (clientStatusBridge?.Observe(probe, observation.SelfAid) is { } decoded)
                {
                    if (decoded.StatusId == 4)
                        decodedHidingBeforeSkill = decoded.Active;
                    DebugLog.Write($"SPRINGRO CLIENT STATUS matched opcode=0x{probe.Opcode:X4}; " +
                                   $"status={decoded.StatusId}; active={decoded.Active}; " +
                                   $"totalMs={decoded.TotalMs}; leftMs={decoded.RemainingMs}");
                    PublishStatus(decoded);
                }
                if (AppEnvironment.IsDevelopment &&
                    _layoutDiagnostics.Format(probe, source, observation.SelfAid,
                        observation.IdentitySource, observation.ObservedAtUtc) is { } line)
                    DebugLog.Write(line);
                if (WireSamplesRequested && scanner.CaptureStatusWireBytes && _statusWireCapture.Observe(probe, source,
                        observation.SelfAid, observation.ObservedAtUtc) is { } sample)
                    DebugLog.Write(sample);
                if (statusSkillCorrelation?.Observe(probe, observation.SelfAid) is { } pair)
                    DebugLog.Write(pair);
                if (correlatedHiding?.Observe(probe, observation.SelfAid) is { } hidden)
                {
                    var alreadyDecoded = clientStatusBridge is not null && decodedHidingBeforeSkill == hidden;
                    decodedHidingBeforeSkill = null;
                    if (!alreadyDecoded)
                    {
                        DebugLog.Write($"SPRINGRO HIDING VERIFIED: active={hidden}; skill=51; " +
                                       $"statusOpcode=0x{(hidden ? 0x0983 : 0x0196):X4}; " +
                                       $"source=correlated-own-skill");
                        // Fallback only when the generic decoded frame was missed.
                        PublishStatus(new RoStatusPacket(
                            hidden ? (ushort)0x0983 : (ushort)0x0196,
                            4, observation.SelfAid, hidden, 0, 0));
                    }
                }
            });
        void PublishStatus(RoStatusPacket packet)
        {
            var observation = Observe(packet, source);
            if (observation.SelfAid == 0) pendingStatuses.Remember(observation);
            StatusChanged?.Invoke(observation);
        }
        void ReplayPendingStatuses()
        {
            var identity = Observe(default(RoStatusPacket), source);
            foreach (var pending in pendingStatuses.Resolve(source, identity.SelfAid,
                         identity.IdentitySource, DateTime.UtcNow))
                StatusChanged?.Invoke(pending);
        }
        scanner.StreamReset += () =>
        {
            if (!source.IsActive || generation != Volatile.Read(ref _generation)) return;
            CloseTrackingSource(packetFlow, source);
            hiding.Reset();
            statusSkillCorrelation?.Reset();
            correlatedHiding?.Reset();
            decodedHidingBeforeSkill = null;
            pendingStatuses.Reset();
            equipmentCache.Clear();
            pendingEquipmentEvents.Clear();
            source = RegisterTrackingSource(generation, packetFlow);
        };
        scanner.StatusChanged += packet => Forward(generation, () =>
        {
            // Raw SpringRO status/actor fields are transformed per frame. With
            // the client bridge, only a matched decoded frame may drive UI.
            if (clientStatusBridge is null || packet.PacketId is not (0x0983 or 0x0196))
                PublishStatus(packet);
        });
        if (AppEnvironment.IsDevelopment)
            scanner.OptionCandidateRejectedDetailed += (opcode, reason) => Forward(generation, () =>
            {
                if (Interlocked.Increment(ref _rejectedOptionDiagnosticCount) <= 20)
                    DebugLog.Write($"PACKET OPTION CANDIDATE REJECTED opcode=0x{opcode:X4}; " +
                                   $"reason={reason}");
            });
        if (AppEnvironment.CharacterSessionsEnabled)
            scanner.SelfAidObserved += packet => Forward(generation, () =>
            {
                ObservePlayerAid(packetFlow, packet.Aid, "ZC_AID");
                ReplayPendingStatuses();
            });
        // Actor visibility (not always a skill ack): TF_HIDING is confirmed
        // by OPTION_HIDE, while EFST_HIDING is a separate status icon.
        scanner.OptionChanged += packet => Forward(generation, () =>
        {
            OptionChanged?.Invoke(Observe(packet, source));
            // SpringRO's OPTION_HIDE bit fluctuates even within
            // confirmed own Hiding transitions. Only the corroborated
            // 0x0983/0x0196 -> own Skill 51 path may drive status=4.
            // RTM and Other retain their existing option handling.
            if (correlatedHiding is not null) return;
            // EFST_HIDING = 4, TF_HIDING = 51 in standard RO.
            // Synthetic status carries no guessed duration.
            PublishStatus(new RoStatusPacket(packet.PacketId, 4, packet.Aid, packet.IsHidden, 0, 0));
            if (hiding.Observe(packet))
                SkillUsed?.Invoke(Observe(new RoSkillPacket(51, packet.Aid, packet.Aid, 0, packet.PacketId), source));
        });
        scanner.SkillUsed += packet => Forward(generation, () => SkillUsed?.Invoke(Observe(packet, source)));
        scanner.SkillCooldown += packet => Forward(generation, () => SkillCooldown?.Invoke(Observe(packet, source)));
        scanner.EquipmentChanged += packet => HandleEquipmentPacket(Observe(packet, source),
            equipmentCache, pendingEquipmentEvents);
        scanner.ItemAdded += packet => Forward(generation, () =>
        {
            ItemAdded?.Invoke(packet);
            ForwardCharacterPacket(packetFlow, packet, CharacterItemAdded);
        });
        scanner.ExperienceChanged += packet => Forward(generation, () =>
        {
            if (AppEnvironment.CharacterSessionsEnabled)
            {
                ObservePlayerAid(packetFlow, packet.Aid, "EXP");
                ReplayPendingStatuses();
            }
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

    private RoPacketObservation<T> Observe<T>(T packet, RoPacketSource source)
    {
        var evidence = _playerAidObserver.SourceForFlow(source.Flow);
        var aid = _playerAidObserver.AidForFlow(source.Flow);
        if (evidence.Length == 0 && _characterSessions.TryResolve(source.Flow, out var session))
        {
            aid = session.PlayerAid;
            evidence = aid == 0 ? "" : "map-login";
        }
        return new(packet, source, DateTime.UtcNow, aid, evidence);
    }

    public bool IsCurrentObservation<T>(RoPacketObservation<T> observation) =>
        observation.Source.IsActive && observation.Source.Generation == Volatile.Read(ref _generation) &&
        _trackingSources.TryGetValue(observation.Source.Flow, out var current) &&
        ReferenceEquals(current, observation.Source) &&
        _playerAidObserver.SourceForFlow(observation.Source.Flow) != "conflict" &&
        (_characterSessions.CurrentFlow is not { } selected || selected == observation.Source.Flow);

    private RoPacketSource RegisterTrackingSource(int generation, CharacterSessionFlow flow)
    {
        var source = new RoPacketSource(generation, flow);
        lock (_lifecycleLock)
        {
            if (generation != _generation) source.Close();
            else
            {
                CloseTrackingSource(flow);
                _trackingSources[flow] = source;
            }
        }
        return source;
    }

    private void CloseTrackingSource(CharacterSessionFlow flow, RoPacketSource? expected = null)
    {
        lock (_lifecycleLock)
        {
            if (!_trackingSources.TryGetValue(flow, out var source) ||
                (expected is not null && !ReferenceEquals(source, expected))) return;
            _trackingSources.TryRemove(flow, out _);
            source.Close();
            _playerAidObserver.OnClosed(flow);
            TrackingSourceClosed?.Invoke(source);
        }
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

    private void RememberRosterLevels(int generation, CaptureTarget target, TcpFlowKey inbound,
        CharacterRosterLevel[] roster)
    {
        if (target.OwnedGameProcess(Reverse(inbound)) is not { } processId) return;
        lock (_lifecycleLock)
        {
            if (generation != _generation) return;
            var now = Environment.TickCount64;
            foreach (var (key, entry) in _rosterLevels.ToArray())
                if (now - entry.SeenAt > RosterLevelLifetimeMs) _rosterLevels.Remove(key);
            if (_rosterLevels.Count + roster.Length > 128) _rosterLevels.Clear();
            foreach (var level in roster)
                _rosterLevels[(processId, level.CharacterId)] = (level, now);
        }
    }

    private void ApplyRosterLevels(int generation, CaptureTarget target, TcpFlowKey inbound,
        CharacterSessionFlow flow)
    {
        if (!_characterSessions.TryResolve(flow, out var session) || session.CharacterId is not { } cid ||
            target.OwnedGameProcess(Reverse(inbound)) is not { } processId)
            return;
        lock (_lifecycleLock)
        {
            if (generation != _generation || !_characterSessions.IsCurrent(session) ||
                !_rosterLevels.Remove((processId, cid), out var entry) ||
                Environment.TickCount64 - entry.SeenAt > RosterLevelLifetimeMs)
                return;

            ForwardCharacterPacket(flow, new RoProgressPacket(11, entry.Levels.BaseLevel), CharacterProgressChanged);
            ForwardCharacterPacket(flow, new RoProgressPacket(55, entry.Levels.JobLevel), CharacterProgressChanged);
        }
    }

    private static TcpFlowKey Reverse(TcpFlowKey flow) =>
        new(flow.DestinationAddress, flow.DestinationPort, flow.SourceAddress, flow.SourcePort);

    private void ForwardCharacterPacket<T>(CharacterSessionFlow flow, T packet,
        Action<CharacterSessionPacket<T>>? callback)
    {
        if (!AppEnvironment.CharacterSessionsEnabled || callback is null ||
            !_characterSessions.TryResolve(flow, out var session)) return;
        callback(new CharacterSessionPacket<T>(session, packet));
    }

    private void Forward(int generation, Action action)
    {
        if (generation == Volatile.Read(ref _generation)) action();
    }

    private void HandleEquipmentPacket(RoPacketObservation<RoEquipmentPacket> observation,
        ConcurrentDictionary<ushort, RoEquipmentPacket> equipmentCache,
        ConcurrentDictionary<ushort, RoPacketObservation<RoEquipmentPacket>> pendingEquipmentEvents)
    {
        if (!IsCurrentObservation(observation)) return;
        var packet = observation.Packet;
        if (packet.ItemId > 0)
        {
            equipmentCache[packet.InventoryIndex] = packet;
            EquipmentChanged?.Invoke(observation);
            if (pendingEquipmentEvents.TryRemove(packet.InventoryIndex, out var pending) &&
                IsCurrentObservation(pending))
            {
                var resolved = CopyEquipmentIdentity(pending.Packet, packet);
                // A later snapshot is authoritative for current wear state.
                // Keep the original receipt time so it cannot complete a newly
                // armed learning window with an earlier equip action.
                if (packet.PacketId is 0x00A4 or 0x0B39)
                    resolved = resolved with { Equipped = packet.Equipped };
                DebugLog.Write($"PACKET EQUIPMENT RESOLVED: packet=0x{resolved.PacketId:X4}; " +
                               $"index={resolved.InventoryIndex}; item={resolved.ItemId}");
                EquipmentChanged?.Invoke(pending with { Packet = resolved });
            }
            return;
        }
        if (equipmentCache.TryGetValue(packet.InventoryIndex, out var identity))
        {
            EquipmentChanged?.Invoke(observation with { Packet = CopyEquipmentIdentity(packet, identity) });
            return;
        }
        if (pendingEquipmentEvents.Count >= 256 && !pendingEquipmentEvents.ContainsKey(packet.InventoryIndex))
            pendingEquipmentEvents.Clear();
        pendingEquipmentEvents[packet.InventoryIndex] = observation;
        // Diagnostic/UI event only: unidentified acknowledgements never change
        // a tracked item or clear another item in the same slot.
        EquipmentIdentityMissing?.Invoke(observation);
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

    private sealed class CaptureTarget(string address, int port, IReadOnlySet<string>? allowedProcesses)
    {
        private readonly uint? _addressValue = IPAddress.TryParse(address, out var parsed) &&
            parsed.AddressFamily == AddressFamily.InterNetwork
            ? BinaryPrimitives.ReadUInt32BigEndian(parsed.GetAddressBytes()) : null;

        public string Address => address;
        public int Port => port;
        public bool AutoDiscovery => allowedProcesses is { Count: > 0 };
        public bool Matches(uint ip, ushort candidatePort) =>
            port == candidatePort && (!_addressValue.HasValue || _addressValue == ip);

        public bool IsOwnedGameConnection(TcpFlowKey flow) => OwnedGameProcess(flow).HasValue;

        public int? OwnedGameProcess(TcpFlowKey flow) =>
            allowedProcesses is null ? null : TcpConnectionDetector.OwnedGameProcess(flow, allowedProcesses);
    }

    private static string BuildFilter(string address, int port)
    {
        if (IPAddress.TryParse(address, out var parsed) &&
            parsed.AddressFamily == AddressFamily.InterNetwork)
            return AppEnvironment.CharacterSessionsEnabled
                ? $"host {parsed} and tcp port {port}"
                : $"src host {parsed} and tcp src port {port}";
        return AppEnvironment.CharacterSessionsEnabled ? $"tcp port {port}" : $"tcp src port {port}";
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
            foreach (var source in _trackingSources.Values) CloseTrackingSource(source.Flow, source);
            // The capture task owning this source disposes it when it finishes.
            _cancellation?.Cancel();
            _cancellation = null;
            _clientStatusBridge?.Dispose();
            _clientStatusBridge = null;
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
        // Bare SYN, FIN and RST still carry flow-lifetime information.
        if (payloadOffset >= ipEnd)
        {
            if ((tcpFlags & 0x07) == 0) return false;
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
