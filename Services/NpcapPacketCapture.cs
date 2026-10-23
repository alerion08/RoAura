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
    private CancellationTokenSource? _cancellation;
    private int _generation;
    private int _activeDevices;
    private string _lastState = "";
    private string _language = "ru";
    private ConcurrentDictionary<ushort, RoEquipmentPacket> _equipmentIdentityCache = new();
    private ConcurrentDictionary<ushort, RoEquipmentPacket> _pendingEquipmentEvents = new();

    public event Action<RoStatusPacket>? StatusChanged;
    public event Action<RoSkillPacket>? SkillUsed;
    public event Action<RoSkillCooldownPacket>? SkillCooldown;
    public event Action<RoEquipmentPacket>? EquipmentChanged;
    public event Action<RoItemAddedPacket>? ItemAdded;
    public event Action<RoExperiencePacket>? ExperienceChanged;
    public event Action<RoProgressPacket>? ProgressChanged;
    public event Action<RoZenyPacket>? ZenyChanged;
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
        int generation;
        lock (_lifecycleLock)
        {
            oldCancellation = _cancellation;
            oldCancellation?.Cancel();
            _cancellation = null;
            generation = ++_generation;
            // Replace rather than clear: workers from the cancelled generation
            // may finish parsing one last frame and must not repopulate the new
            // connection's identity table.
            _equipmentIdentityCache = new();
            _pendingEquipmentEvents = new();
            equipmentCache = _equipmentIdentityCache;
            pendingEquipmentEvents = _pendingEquipmentEvents;
            tcpFlows = new();

            if (!settings.PacketCaptureEnabled)
            {
                SetState(L("Пакеты: отключены", "Packets: disabled"));
                return;
            }

            newCancellation = new CancellationTokenSource();
            _cancellation = newCancellation;
        }

        var address = settings.PacketServerAddress;
        var port = settings.PacketServerPort;
        var token = newCancellation!.Token;
        SetState(L("Пакеты: подключение к Npcap…", "Packets: connecting to Npcap…"));
        _ = Task.Run(() => StartCaptureAsync(address, port, generation, equipmentCache,
            pendingEquipmentEvents, tcpFlows, token), token);
    }

    private async Task StartCaptureAsync(string address, int port, int generation,
        ConcurrentDictionary<ushort, RoEquipmentPacket> equipmentCache,
        ConcurrentDictionary<ushort, RoEquipmentPacket> pendingEquipmentEvents,
        ConcurrentDictionary<TcpFlowKey, TcpFlowState> tcpFlows,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                SetState(L("Пакеты: доступны только в Windows", "Packets: available on Windows only"));
                return;
            }

            NpcapNative.EnsureLoaded();
            var devices = NpcapNative.GetDevices();
            if (devices.Count == 0)
            {
                SetState(L("Пакеты: Npcap не нашёл сетевые интерфейсы", "Packets: Npcap found no network adapters"));
                return;
            }

            var filter = BuildFilter(address, port);
            var workers = devices.Select(device => Task.Run(
                () => CaptureDevice(device, filter, generation, equipmentCache,
                    pendingEquipmentEvents, tcpFlows, cancellationToken),
                cancellationToken)).ToArray();
            await Task.WhenAll(workers);

            if (!cancellationToken.IsCancellationRequested && generation == _generation)
                SetState(L("Пакеты: интерфейс не удалось открыть — попробуйте запуск от администратора",
                    "Packets: could not open an adapter — try running as administrator"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (DllNotFoundException)
        {
            SetState(L("Пакеты: Npcap не установлен", "Packets: Npcap is not installed"));
        }
        catch (BadImageFormatException)
        {
            SetState(L("Пакеты: разрядность Npcap не совпадает с RoAura", "Packets: Npcap architecture does not match RoAura"));
        }
        catch (Exception ex)
        {
            SetState(L($"Пакеты: ошибка запуска — {ex.Message}", $"Packets: startup error — {ex.Message}"));
            DebugLog.Write($"PACKET CAPTURE START ERROR: {ex}");
        }
    }

    private void CaptureDevice(PcapDeviceInfo device, string filter, int generation,
        ConcurrentDictionary<ushort, RoEquipmentPacket> equipmentCache,
        ConcurrentDictionary<ushort, RoEquipmentPacket> pendingEquipmentEvents,
        ConcurrentDictionary<TcpFlowKey, TcpFlowState> tcpFlows,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && generation == _generation)
        {
            CaptureDeviceSession(device, filter, generation, equipmentCache,
                pendingEquipmentEvents, tcpFlows, cancellationToken);
            if (cancellationToken.IsCancellationRequested || generation != _generation)
                break;

            // Npcap handles can be invalidated when a VPN/accelerator changes
            // routes or an adapter is briefly reset. A dead worker used to stay
            // dead until the user reset AID and relogged. Reopen it in place so
            // packet tracking recovers without touching the current settings.
            DebugLog.Write($"PCAP REOPEN {device.Description}: retry in 2 seconds");
            if (cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(2)))
                break;
        }
    }

    private void CaptureDeviceSession(PcapDeviceInfo device, string filter, int generation,
        ConcurrentDictionary<ushort, RoEquipmentPacket> equipmentCache,
        ConcurrentDictionary<ushort, RoEquipmentPacket> pendingEquipmentEvents,
        ConcurrentDictionary<TcpFlowKey, TcpFlowState> tcpFlows,
        CancellationToken cancellationToken)
    {
        IntPtr handle = IntPtr.Zero;
        var countedAsActive = false;
        try
        {
            var error = new StringBuilder(NpcapNative.ErrorBufferSize);
            handle = NpcapNative.pcap_open_live(device.Name, 65535, 0, 500, error);
            if (handle == IntPtr.Zero)
            {
                DebugLog.Write($"PCAP OPEN FAILED {device.Description}: {error}");
                return;
            }

            if (NpcapNative.pcap_compile(handle, out var program, filter, 1, uint.MaxValue) != 0)
            {
                DebugLog.Write($"PCAP FILTER COMPILE FAILED {device.Description}: " +
                               NpcapNative.GetError(handle));
                return;
            }

            try
            {
                if (NpcapNative.pcap_setfilter(handle, ref program) != 0)
                {
                    DebugLog.Write($"PCAP FILTER SET FAILED {device.Description}: " +
                                   NpcapNative.GetError(handle));
                    return;
                }
            }
            finally
            {
                NpcapNative.pcap_freecode(ref program);
            }

            var linkType = NpcapNative.pcap_datalink(handle);
            var active = Interlocked.Increment(ref _activeDevices);
            countedAsActive = true;
            SetState(L($"Пакеты: слушаю {active} интерфейс(а), фильтр {filter}",
                $"Packets: listening on {active} adapter(s), filter {filter}"));
            DebugLog.Write($"PCAP LISTEN {device.Description}: dlt={linkType}; filter={filter}");

            while (!cancellationToken.IsCancellationRequested && generation == _generation)
            {
                var result = NpcapNative.pcap_next_ex(handle, out var headerPointer,
                    out var dataPointer);
                if (result == 0) continue;
                if (result < 0)
                {
                    if (!cancellationToken.IsCancellationRequested)
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
                        out var payload, out var flowKey, out var sequence))
                {
                    var flow = tcpFlows.GetOrAdd(flowKey, _ => new TcpFlowState(
                        CreateScanner(generation, equipmentCache, pendingEquipmentEvents)));
                    flow.Process(sequence, payload);
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"PCAP DEVICE ERROR {device.Description}: {ex}");
        }
        finally
        {
            if (handle != IntPtr.Zero) NpcapNative.pcap_close(handle);
            if (countedAsActive) Interlocked.Decrement(ref _activeDevices);
        }
    }

    private RoPacketStreamScanner CreateScanner(int generation,
        ConcurrentDictionary<ushort, RoEquipmentPacket> equipmentCache,
        ConcurrentDictionary<ushort, RoEquipmentPacket> pendingEquipmentEvents)
    {
        var scanner = new RoPacketStreamScanner(equipmentCache);
        scanner.StatusChanged += packet => Forward(generation, () => StatusChanged?.Invoke(packet));
        scanner.SkillUsed += packet => Forward(generation, () => SkillUsed?.Invoke(packet));
        scanner.SkillCooldown += packet => Forward(generation, () => SkillCooldown?.Invoke(packet));
        scanner.EquipmentChanged += packet => HandleEquipmentPacket(generation, packet,
            equipmentCache, pendingEquipmentEvents);
        scanner.ItemAdded += packet => Forward(generation, () => ItemAdded?.Invoke(packet));
        scanner.ExperienceChanged += packet => Forward(generation, () => ExperienceChanged?.Invoke(packet));
        scanner.ProgressChanged += packet => Forward(generation, () => ProgressChanged?.Invoke(packet));
        scanner.ZenyChanged += packet => Forward(generation, () => ZenyChanged?.Invoke(packet));
        return scanner;
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
            return $"src host {parsed} and tcp src port {port}";
        return $"tcp src port {port}";
    }

    private void SetState(string state)
    {
        if (string.Equals(_lastState, state, StringComparison.Ordinal)) return;
        _lastState = state;
        DebugLog.Write(state.ToUpperInvariant());
        StateChanged?.Invoke(state);
    }

    private string L(string ru, string en) => _language == "en" ? en : ru;

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            _generation++;
            _cancellation?.Cancel();
            _cancellation?.Dispose();
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
    internal static extern IntPtr pcap_open_live(string device, int snapshotLength,
        int promiscuous, int readTimeoutMs, StringBuilder errorBuffer);

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

    internal TcpFlowState(RoPacketStreamScanner scanner) => _scanner = scanner;

    internal void Process(uint sequence, ReadOnlySpan<byte> payload)
    {
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
        out ReadOnlySpan<byte> payload, out TcpFlowKey flowKey, out uint sequence)
    {
        payload = default;
        flowKey = default;
        sequence = 0;
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

        var totalLength = (frame[ipOffset + 2] << 8) | frame[ipOffset + 3];
        var ipEnd = Math.Min(frame.Length, ipOffset + totalLength);
        var tcpOffset = ipOffset + ipHeaderLength;
        if (ipEnd < tcpOffset + 20) return false;

        var tcpHeaderLength = (frame[tcpOffset + 12] >> 4) * 4;
        var payloadOffset = tcpOffset + tcpHeaderLength;
        if (tcpHeaderLength < 20 || payloadOffset >= ipEnd) return false;

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
