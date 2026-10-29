using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace RoAura.Services;

// Experimental DEV-only bridge: observes the client's already decoded dispatch
// buffer for a short period after a matching inbound status frame. Never writes
// to the game process. A failed or ambiguous match produces no status event.
public sealed class SpringRoClientStatusBridge : IDisposable
{
    public const string Argument = "--springro-client-status";
    private const string ExpectedHash = "591e3776e40ac3661a1c60576a145e34e69a61423a890fda292c955d237f91c2";
    private readonly object _sync = new();
    private IntPtr _handle;
    private IntPtr _address;
    private int _pid;
    private bool _unavailable;
    private bool _disposed;

    public void Prepare()
    {
        if (!OperatingSystem.IsWindows()) return;
        lock (_sync) EnsureProcess();
    }

    public RoStatusPacket? Observe(RoPacketLayoutProbe probe, uint self)
    {
        if (!OperatingSystem.IsWindows() || self == 0 || !probe.Boundary ||
            probe.Opcode is not (0x0983 or 0x0196) ||
            probe.WireBytes.Length != (probe.Opcode == 0x0983 ? 29 : 9)) return null;

        lock (_sync)
        {
            if (!EnsureProcess()) return null;
            var games = Process.GetProcessesByName("SpringRO");
            try
            {
                if (games.Length != 1 || games[0].Id != _pid) return null;
            }
            finally { foreach (var game in games) game.Dispose(); }
            var raw = probe.WireBytes.Span;
            var first = new byte[29];
            var second = new byte[29];
            var watch = Stopwatch.StartNew();
            var readSucceeded = false;
            do
            {
                var read = ReadProcessMemory(_handle, _address, first, (UIntPtr)29, out var length) &&
                    length.ToUInt32() == 29;
                readSucceeded |= read;
                if (read &&
                    ReadProcessMemory(_handle, _address, second, (UIntPtr)29, out length) &&
                    length.ToUInt32() == 29 && first.AsSpan().SequenceEqual(second) &&
                    DecodeMatchedFrame(raw.ToArray(), first, self) is { } decoded)
                {
                    return decoded;
                }
                // Yield occasionally to avoid monopolizing a capture thread.
                Thread.Yield();
            } while (watch.ElapsedMilliseconds < 15);
            if (!readSucceeded) DisposeHandle(); // Process exited or buffer became unreadable.
            return null;
        }
    }

    public static RoStatusPacket? DecodeMatchedFrame(byte[] rawBytes,
        byte[] frameBytes, uint self)
    {
        ReadOnlySpan<byte> raw = rawBytes;
        ReadOnlySpan<byte> frame = frameBytes;
        if (raw.Length is not (9 or 29) || frame.Length < raw.Length || self == 0 ||
            !frame[..2].SequenceEqual(raw[..2]) ||
            frame[8] != (raw.Length == 29 ? 1 : 0) ||
            !frame.Slice(6, raw.Length - 6).SequenceEqual(raw[6..]) ||
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(4)) != self)
            return null;
        var opcode = BinaryPrimitives.ReadUInt16LittleEndian(raw);
        if (opcode != (raw.Length == 29 ? 0x0983 : 0x0196)) return null;
        return new RoStatusPacket(opcode,
            BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(2)), self, frame[8] == 1,
            raw.Length == 29 ? BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(13)) : 0,
            raw.Length == 29 ? BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(9)) : 0);
    }

    private bool EnsureProcess()
    {
        if (_unavailable || _disposed) return false;
        if (_handle != IntPtr.Zero) return true;
        var games = Process.GetProcessesByName("SpringRO");
        try
        {
            if (games.Length != 1) return false; // Never attribute another client.
            var game = games[0];
            var file = game.MainModule?.FileName;
            if (file is null || !string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))),
                    ExpectedHash, StringComparison.OrdinalIgnoreCase))
            {
                _unavailable = true;
                DebugLog.Write("SPRINGRO CLIENT STATUS unavailable: executable hash mismatch");
                return false;
            }
            _address = IntPtr.Add(game.MainModule!.BaseAddress, 0x011E8198);
            _handle = OpenProcess(0x1010, false, game.Id);
            if (_handle == IntPtr.Zero)
            {
                _unavailable = true;
                DebugLog.Write("SPRINGRO CLIENT STATUS unavailable: read access denied");
                return false;
            }
            _pid = game.Id;
            DebugLog.Write($"SPRINGRO CLIENT STATUS observing PID {_pid}; read-only; opt-in=true");
            return true;
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            DebugLog.Write($"SPRINGRO CLIENT STATUS unavailable: {ex.GetType().Name}");
            return false;
        }
        finally { foreach (var game in games) game.Dispose(); }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            DisposeHandle();
        }
    }

    private void DisposeHandle()
    {
        if (_handle != IntPtr.Zero) CloseHandle(_handle);
        _handle = IntPtr.Zero;
        _address = IntPtr.Zero;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address,
        [Out] byte[] buffer, UIntPtr size, out UIntPtr bytesRead);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
