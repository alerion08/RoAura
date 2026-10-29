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
    private const string ExpectedHash = "591e3776e40ac3661a1c60576a145e34e69a61423a890fda292c955d237f91c2";
    private readonly object _sync = new();
    private readonly object _framesSync = new();
    private readonly Queue<(DateTime At, byte[] Bytes)> _frames = new();
    private IntPtr _handle;
    private IntPtr _address;
    private int _pid;
    private bool _unavailable;
    private volatile bool _disposed;
    private int _missesLogged;

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
            var rawBytes = raw.ToArray();
            var first = new byte[29];
            var second = new byte[29];
            var watch = Stopwatch.StartNew();
            var readSucceeded = false;
            do
            {
                if (FindRecent(rawBytes, self) is { } captured) return captured;
                var read = ReadProcessMemory(_handle, _address, first, (UIntPtr)29, out var length) &&
                    length.ToUInt32() == 29;
                readSucceeded |= read;
                if (read &&
                    ReadProcessMemory(_handle, _address, second, (UIntPtr)29, out length) &&
                    length.ToUInt32() == 29 && first.AsSpan().SequenceEqual(second) &&
                    DecodeMatchedFrame(rawBytes, first, self) is { } decoded)
                {
                    return decoded;
                }
                // Yield occasionally to avoid monopolizing a capture thread.
                Thread.Yield();
            } while (watch.ElapsedMilliseconds < 15);
            if (FindRecent(rawBytes, self) is { } late) return late;
            if (!readSucceeded) DisposeHandle(); // Process exited or buffer became unreadable.
            if (_missesLogged++ < 20)
                DebugLog.Write($"SPRINGRO CLIENT STATUS missed opcode=0x{probe.Opcode:X4}; " +
                               $"readable={readSucceeded}; elapsedMs={watch.ElapsedMilliseconds}; " +
                               DescribeRecent(rawBytes, self));
            return null;
        }
    }

    private string DescribeRecent(byte[] raw, uint self)
    {
        lock (_framesSync)
        {
            var cutoff = DateTime.UtcNow.AddMilliseconds(-100);
            var opcode = 0;
            var tail = 0;
            var actor = 0;
            foreach (var frame in _frames)
            {
                if (frame.At < cutoff || !frame.Bytes.AsSpan(0, 2).SequenceEqual(raw.AsSpan(0, 2)))
                    continue;
                opcode++;
                if (!frame.Bytes.AsSpan(6, raw.Length - 6).SequenceEqual(raw.AsSpan(6))) continue;
                tail++;
                if (BinaryPrimitives.ReadUInt32LittleEndian(frame.Bytes.AsSpan(4)) == self) actor++;
            }
            return $"recentOpcode={opcode}; matchingTail={tail}; matchingActor={actor}";
        }
    }

    private RoStatusPacket? FindRecent(byte[] raw, uint self)
    {
        lock (_framesSync)
        {
            var cutoff = DateTime.UtcNow.AddMilliseconds(-100);
            while (_frames.TryPeek(out var head) && head.At < cutoff) _frames.Dequeue();
            foreach (var frame in _frames)
                if (DecodeMatchedFrame(raw, frame.Bytes, self) is { } decoded)
                    return decoded;
        }
        return null;
    }

    // The client dispatch buffer can be overwritten before Npcap's managed
    // callback runs. Observe it independently and retain only a brief window.
    private void CaptureLoop(IntPtr handle, IntPtr address)
    {
        var first = new byte[29];
        var second = new byte[29];
        var previousWasStatus = false;
        byte[]? previous = null;
        long reads = 0;
        long statusCandidates = 0;
        long stableCandidates = 0;
        var report = Stopwatch.StartNew();
        while (!_disposed)
        {
            if (!ReadProcessMemory(handle, address, first, (UIntPtr)29, out var length) ||
                length.ToUInt32() != 29) break;
            reads++;
            var opcode = BinaryPrimitives.ReadUInt16LittleEndian(first);
            if (opcode is 0x0983 or 0x0196)
            {
                statusCandidates++;
                var size = opcode == 0x0983 ? 29 : 9;
                if ((!previousWasStatus || previous is null || !first.AsSpan(0, size).SequenceEqual(previous)) &&
                    ReadProcessMemory(handle, address, second, (UIntPtr)29, out length) &&
                    length.ToUInt32() == 29 && first.AsSpan().SequenceEqual(second))
                {
                    stableCandidates++;
                    var frame = first.AsSpan(0, size).ToArray();
                    lock (_framesSync)
                    {
                        _frames.Enqueue((DateTime.UtcNow, frame));
                        while (_frames.Count > 128) _frames.Dequeue();
                    }
                    previous = frame;
                }
                previousWasStatus = true;
            }
            else
            {
                previousWasStatus = false;
                previous = null;
            }
            if ((reads & 255) == 0) Thread.Sleep(0);
            if (report.Elapsed >= TimeSpan.FromSeconds(15))
            {
                DebugLog.Write($"SPRINGRO CLIENT STATUS sampler readsPerSecond={reads / report.Elapsed.TotalSeconds:0}; " +
                               $"statusCandidates={statusCandidates}; stableCandidates={stableCandidates}; windowMs=100");
                reads = 0;
                statusCandidates = 0;
                stableCandidates = 0;
                report.Restart();
            }
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
            DebugLog.Write($"SPRINGRO CLIENT STATUS observing PID {_pid}; read-only; sampler=continuous");
            var handle = _handle;
            var address = _address;
            _ = Task.Run(() => CaptureLoop(handle, address));
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
            lock (_framesSync) _frames.Clear();
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
