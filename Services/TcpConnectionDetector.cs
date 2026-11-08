using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace RoAura.Services;

public readonly record struct DetectedTcpConnection(
    string ProcessName,
    int ProcessId,
    string RemoteAddress,
    int RemotePort);

public static class TcpConnectionDetector
{
    private const int AfInet = 2;
    private const uint ErrorInsufficientBuffer = 122;
    private const uint MibTcpStateEstablished = 5;

    public static DetectedTcpConnection? Detect(
        IEnumerable<string> processNames,
        string? currentAddress = null,
        int currentPort = 0,
        int preferredProcessId = 0)
    {
        if (!OperatingSystem.IsWindows()) return null;

        var processes = new Dictionary<int, string>();
        foreach (var name in processNames.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            var normalized = Path.GetFileNameWithoutExtension(name.Trim());
            try
            {
                foreach (var process in Process.GetProcessesByName(normalized))
                {
                    using (process)
                        processes[process.Id] = process.ProcessName;
                }
            }
            catch
            {
                // A process may exit between enumeration and inspection.
            }
        }

        if (processes.Count == 0) return null;
        var candidates = ReadEstablishedConnections()
            .Where(row => processes.ContainsKey(row.ProcessId))
            .Select(row => new DetectedTcpConnection(
                processes[row.ProcessId], row.ProcessId, row.RemoteAddress, row.RemotePort))
            .Distinct()
            .ToList();
        if (candidates.Count == 0) return null;

        return Select(candidates, currentAddress, currentPort, preferredProcessId);
    }

    // HTTP connections belong to patchers, news and account services. Automatic
    // discovery waits for a game connection; manual endpoint settings can still
    // use any port. Loopback is valid for local RO proxies.
    public static DetectedTcpConnection? Select(IEnumerable<DetectedTcpConnection> connections,
        string? currentAddress = null, int currentPort = 0, int preferredProcessId = 0)
    {
        var candidates = connections.Where(connection => connection.RemotePort is > 0 and <= 65535 &&
            connection.RemotePort is not (80 or 443)).ToList();
        if (preferredProcessId != 0)
            candidates = candidates.Where(connection => connection.ProcessId == preferredProcessId).ToList();
        if (candidates.Count == 0) return null;
        return candidates
            .OrderByDescending(connection => Score(connection, currentAddress, currentPort))
            .ThenBy(connection => connection.ProcessId)
            .ThenBy(connection => connection.RemotePort)
            .First();
    }

    internal static int? OwnedGameProcess(TcpFlowKey flow, IReadOnlySet<string> allowedProcesses)
    {
        if (!OperatingSystem.IsWindows() || allowedProcesses.Count == 0) return null;
        foreach (var row in ReadEstablishedConnections())
        {
            if (row.LocalAddress != flow.SourceAddress || row.LocalPort != flow.SourcePort ||
                row.RemotePort != flow.DestinationPort || row.RemoteAddressValue != flow.DestinationAddress)
                continue;
            try
            {
                using var process = Process.GetProcessById(row.ProcessId);
                return allowedProcesses.Contains(process.ProcessName) ? row.ProcessId : null;
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return null;
            }
        }
        return null;
    }

    private static int Score(DetectedTcpConnection connection, string? currentAddress, int currentPort)
    {
        var score = 0;
        if (connection.RemotePort == currentPort) score += 300;
        if (string.Equals(connection.RemoteAddress, currentAddress, StringComparison.OrdinalIgnoreCase)) score += 200;
        if (connection.RemotePort is not (80 or 443)) score += 80;
        if (connection.RemotePort >= 1024) score += 20;
        if (IPAddress.TryParse(connection.RemoteAddress, out var address) && !IPAddress.IsLoopback(address)) score += 10;
        return score;
    }

    private static IReadOnlyList<TcpRow> ReadEstablishedConnections()
    {
        var size = 0;
        var result = GetExtendedTcpTable(IntPtr.Zero, ref size, true, AfInet,
            TcpTableClass.OwnerPidAll, 0);
        if (result != ErrorInsufficientBuffer || size <= 0) return [];

        var buffer = IntPtr.Zero;
        try
        {
            // The table can grow between the size query and the read; retry a
            // few times with the size the API reports back.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                buffer = Marshal.AllocHGlobal(size);
                result = GetExtendedTcpTable(buffer, ref size, true, AfInet,
                    TcpTableClass.OwnerPidAll, 0);
                if (result != ErrorInsufficientBuffer || size <= 0) break;
            }
            if (result != 0) return [];

            var count = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            var rows = new List<TcpRow>(Math.Max(0, count));
            var rowPointer = IntPtr.Add(buffer, sizeof(int));
            for (var index = 0; index < count; index++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPointer);
                rowPointer = IntPtr.Add(rowPointer, rowSize);
                if (row.State != MibTcpStateEstablished || row.RemoteAddress == 0 || row.OwningPid == 0)
                    continue;

                var remotePort = PortFromNetworkOrder(row.RemotePort);
                if (remotePort <= 0) continue;
                rows.Add(new TcpRow((int)row.OwningPid,
                    new IPAddress(row.RemoteAddress).ToString(), remotePort,
                    BinaryPrimitives.ReverseEndianness(row.LocalAddress), PortFromNetworkOrder(row.LocalPort),
                    BinaryPrimitives.ReverseEndianness(row.RemoteAddress)));
            }
            return rows;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }

    private static int PortFromNetworkOrder(uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        return (bytes[0] << 8) | bytes[1];
    }

    private readonly record struct TcpRow(int ProcessId, string RemoteAddress, int RemotePort,
        uint LocalAddress, int LocalPort, uint RemoteAddressValue);

    private enum TcpTableClass
    {
        OwnerPidAll = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        int ipVersion,
        TcpTableClass tableClass,
        uint reserved);
}
