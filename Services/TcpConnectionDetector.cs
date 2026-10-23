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
        int currentPort = 0)
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

        return candidates
            .OrderByDescending(connection => Score(connection, currentAddress, currentPort))
            .ThenBy(connection => connection.ProcessId)
            .ThenBy(connection => connection.RemotePort)
            .First();
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

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            result = GetExtendedTcpTable(buffer, ref size, true, AfInet,
                TcpTableClass.OwnerPidAll, 0);
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
                    new IPAddress(row.RemoteAddress).ToString(), remotePort));
            }
            return rows;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int PortFromNetworkOrder(uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        return (bytes[0] << 8) | bytes[1];
    }

    private readonly record struct TcpRow(int ProcessId, string RemoteAddress, int RemotePort);

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

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        int ipVersion,
        TcpTableClass tableClass,
        uint reserved);
}
