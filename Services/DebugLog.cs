using System.Text;
using System.Threading.Channels;

namespace RoAura.Services;

/// <summary>
/// Non-blocking diagnostics log. <see cref="Write"/> only enqueues; a single
/// background task owns the file, flushes about once per second, rotates the
/// file at 5 MB and collapses identical messages repeated within 5 seconds.
/// </summary>
public static class DebugLog
{
    private const long MaximumFileSize = 5L * 1024 * 1024;
    private const int RotatedFileCount = 3;
    private const int QueueCapacity = 10000;
    private const int RepeatWindowMs = 5000;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);
    private static readonly object Sync = new();
    private static readonly Channel<string> Queue = Channel.CreateBounded<string>(
        new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    private static readonly SemaphoreSlim FlushRequest = new(0, int.MaxValue);
    private static Task? _writer;
    private static StreamWriter? _stream;
    private static long _writtenBytes;
    private static string? _lastMessage;
    private static long _lastMessageTick;
    private static int _repeatCount;
    private static long _flushTicket;
    private static long _flushCompleted;

    public static string PathName { get; } = Path.Combine(AppEnvironment.RoamingDirectory, "debug.log");

    public static void Write(string message)
    {
        try
        {
            EnsureWriter();
            lock (Sync)
            {
                var now = Environment.TickCount64;
                if (_lastMessage is not null &&
                    string.Equals(_lastMessage, message, StringComparison.Ordinal) &&
                    now - _lastMessageTick <= RepeatWindowMs)
                {
                    _repeatCount++;
                    _lastMessageTick = now;
                    return;
                }

                EmitRepeatSummaryLocked();
                _lastMessage = message;
                _lastMessageTick = now;
                Queue.Writer.TryWrite(Stamp(message));
            }
        }
        catch
        {
            // Diagnostics must never stop the overlay.
        }
    }

    /// <summary>
    /// Pushes everything queued so far to disk. Safe to call from any thread;
    /// the wait is bounded so shutdown can never hang on the log.
    /// </summary>
    public static void Flush()
    {
        try
        {
            if (_writer is null) return;
            lock (Sync)
            {
                EmitRepeatSummaryLocked();
                _lastMessage = null;
            }

            var ticket = Interlocked.Increment(ref _flushTicket);
            FlushRequest.Release();
            var deadline = Environment.TickCount64 + 2000;
            while (Volatile.Read(ref _flushCompleted) < ticket &&
                   Environment.TickCount64 < deadline)
                Thread.Sleep(10);
        }
        catch
        {
            // Diagnostics must never stop the overlay.
        }
    }

    private static string Stamp(string message) =>
        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";

    private static void EmitRepeatSummaryLocked()
    {
        if (_repeatCount <= 0) return;
        Queue.Writer.TryWrite(Stamp($"(last message repeated {_repeatCount} times)"));
        _repeatCount = 0;
    }

    private static void EnsureWriter()
    {
        if (_writer is not null) return;
        lock (Sync)
        {
            if (_writer is not null) return;
            _writer = Task.Factory.StartNew(WriterLoop, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
        }
    }

    private static async Task WriterLoop()
    {
        var reader = Queue.Reader;
        Task<bool>? dataArrived = null;
        while (true)
        {
            try
            {
                var ticket = Volatile.Read(ref _flushTicket);
                var drained = false;
                while (reader.TryRead(out var line))
                {
                    drained = true;
                    AppendLine(line);
                }

                if (drained && _stream is not null)
                {
                    try { _stream.Flush(); }
                    catch { CloseStream(); }
                }
                Volatile.Write(ref _flushCompleted, ticket);

                // Sleep until new data arrives, a flush is requested or the
                // periodic interval elapses, whichever comes first.
                if (dataArrived is null || dataArrived.IsCompleted)
                    dataArrived = reader.WaitToReadAsync().AsTask();
                var flushOrTimeout = FlushRequest.WaitAsync(FlushInterval);
                await Task.WhenAny(dataArrived, flushOrTimeout).ConfigureAwait(false);
            }
            catch
            {
                CloseStream();
                try { await Task.Delay(FlushInterval).ConfigureAwait(false); }
                catch { }
            }
        }
    }

    private static void AppendLine(string line)
    {
        try
        {
            var stream = OpenStream();
            stream.WriteLine(line);
            _writtenBytes += Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
            if (_writtenBytes >= MaximumFileSize) Rotate();
        }
        catch
        {
            CloseStream();
        }
    }

    private static StreamWriter OpenStream()
    {
        if (_stream is not null) return _stream;
        var directory = Path.GetDirectoryName(PathName);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var file = new FileStream(PathName, FileMode.Append, FileAccess.Write,
            FileShare.ReadWrite);
        _writtenBytes = file.Length;
        _stream = new StreamWriter(file, new UTF8Encoding(false)) { AutoFlush = false };
        return _stream;
    }

    private static void CloseStream()
    {
        try { _stream?.Dispose(); }
        catch { }
        _stream = null;
    }

    private static void Rotate()
    {
        CloseStream();
        try
        {
            var directory = Path.GetDirectoryName(PathName) ?? "";
            var baseName = Path.GetFileNameWithoutExtension(PathName);
            var extension = Path.GetExtension(PathName);
            string Rotated(int index) => Path.Combine(directory, $"{baseName}.{index}{extension}");

            var oldest = Rotated(RotatedFileCount);
            if (File.Exists(oldest)) File.Delete(oldest);
            for (var index = RotatedFileCount - 1; index >= 1; index--)
            {
                var source = Rotated(index);
                if (File.Exists(source)) File.Move(source, Rotated(index + 1), true);
            }
            if (File.Exists(PathName)) File.Move(PathName, Rotated(1), true);
        }
        catch
        {
            // Rotation failure must not lose the log: keep appending to the
            // current file and try again at the next size check.
        }
        _writtenBytes = 0;
    }
}
