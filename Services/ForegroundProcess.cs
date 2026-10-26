using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RoAura.Services;

public static class ForegroundProcess
{
    private const int SwRestore = 9;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int DiscoveryIntervalMs = 2000;
    private const int NameCacheLifetimeMs = 30000;

    public readonly record struct WindowBounds(int Left, int Top, int Right, int Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out NativeRect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder exeName, ref uint size);

    // Cached game window. The per-tick callers (overlay timers) only validate the
    // handle with IsWindow/IsIconic and read its bounds; the expensive process
    // enumeration runs when the handle is gone or at most every two seconds.
    private static readonly object CacheLock = new();
    private static IntPtr _cachedGameWindow;
    private static int _cachedGamePid;
    private static string _cachedProcessKey = "";
    private static long _lastDiscoveryTicks = long.MinValue;

    // PID -> process name. Cleared periodically so that a reused PID cannot keep
    // a stale name for long.
    private static readonly Dictionary<int, string> ProcessNameCache = new();
    private static long _nameCacheClearedTicks;

    public static string? GetName()
    {
        try
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero) return null;
            GetWindowThreadProcessId(window, out var processId);
            return GetProcessName((int)processId);
        }
        catch
        {
            return null;
        }
    }

    public static bool TryFindWindow(IEnumerable<string> processNames, out IntPtr handle, out WindowBounds bounds)
    {
        handle = IntPtr.Zero;
        bounds = default;
        var names = NormalizeNames(processNames);
        if (names.Count == 0) return false;
        var key = string.Join("\n", names);

        lock (CacheLock)
        {
            if (!string.Equals(key, _cachedProcessKey, StringComparison.OrdinalIgnoreCase))
            {
                _cachedProcessKey = key;
                _cachedGameWindow = IntPtr.Zero;
                _cachedGamePid = 0;
                _lastDiscoveryTicks = long.MinValue;
            }

            var now = Environment.TickCount64;

            // The active allowed window is authoritative. Several Ragnarok
            // distributions run a launcher and the game (or multiple clients)
            // under the same executable name. Keeping the first enumerated HWND
            // in that case anchors every regular overlay to the wrong client.
            // DatabaseOverlayWindow later follows the foreground HWND, which is
            // why it used to appear elsewhere first and then jump into the game.
            if (TryGetAllowedForegroundWindow(names, out var foreground,
                    out var foregroundPid, out bounds))
            {
                _cachedGameWindow = foreground;
                _cachedGamePid = foregroundPid;
                _lastDiscoveryTicks = now;
                handle = foreground;
                return true;
            }

            var cachedValid = _cachedGameWindow != IntPtr.Zero && IsWindow(_cachedGameWindow);
            if (!cachedValid && _cachedGameWindow != IntPtr.Zero)
            {
                // The game window was destroyed: forget it and look again right away.
                _cachedGameWindow = IntPtr.Zero;
                _cachedGamePid = 0;
                _lastDiscoveryTicks = long.MinValue;
            }

            if (cachedValid)
            {
                // Minimized: the window still exists, nothing to discover.
                if (IsIconic(_cachedGameWindow)) return false;
                if (TryGetClientScreenBounds(_cachedGameWindow, out bounds))
                {
                    handle = _cachedGameWindow;
                    return true;
                }
            }

            if (now - _lastDiscoveryTicks < DiscoveryIntervalMs)
            {
                bounds = default;
                return false;
            }
            _lastDiscoveryTicks = now;

            if (DiscoverGameWindow(names, out var found, out var foundPid, out bounds))
            {
                _cachedGameWindow = found;
                _cachedGamePid = foundPid;
                handle = found;
                return true;
            }

            bounds = default;
            return false;
        }
    }

    private static bool DiscoverGameWindow(List<string> names, out IntPtr handle, out int processId, out WindowBounds bounds)
    {
        handle = IntPtr.Zero;
        processId = 0;
        bounds = default;
        long largestClientArea = 0;
        foreach (var normalized in names)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(normalized))
                {
                    using (process)
                    {
                        var window = process.MainWindowHandle;
                        if (window == IntPtr.Zero || IsIconic(window) || !IsWindowVisible(window) ||
                            !TryGetClientScreenBounds(window, out bounds))
                            continue;

                        var area = Math.Max(1L,
                            (long)(bounds.Right - bounds.Left) * (bounds.Bottom - bounds.Top));
                        if (area <= largestClientArea) continue;

                        largestClientArea = area;
                        handle = window;
                        processId = process.Id;
                    }
                }
            }
            catch
            {
                // Try the next configured process name.
            }
        }

        if (handle != IntPtr.Zero && TryGetClientScreenBounds(handle, out bounds))
            return true;

        bounds = default;
        return false;
    }

    private static bool TryGetAllowedForegroundWindow(List<string> names, out IntPtr handle,
        out int processId, out WindowBounds bounds)
    {
        handle = GetForegroundWindow();
        processId = 0;
        bounds = default;
        if (handle == IntPtr.Zero || !IsWindow(handle) || IsIconic(handle) || !IsWindowVisible(handle))
            return false;

        GetWindowThreadProcessId(handle, out var pid);
        if (pid == 0 || !NameMatches(GetProcessName((int)pid), names) ||
            !TryGetClientScreenBounds(handle, out bounds))
        {
            handle = IntPtr.Zero;
            bounds = default;
            return false;
        }

        processId = (int)pid;
        return true;
    }

    public static bool IsForegroundAllowed(IEnumerable<string> processNames)
    {
        try
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero) return false;
            return IsWindowAllowed(window, processNames);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsWindowAllowed(IntPtr window, IEnumerable<string> processNames)
    {
        if (window == IntPtr.Zero) return false;
        try
        {
            GetWindowThreadProcessId(window, out var processId);
            if (processId == 0) return false;
            if (IsCachedGameProcess((int)processId, processNames)) return true;
            return NameMatches(GetProcessName((int)processId), processNames);
        }
        catch { return false; }
    }

    public static bool IsForegroundWindow(IntPtr window) =>
        window != IntPtr.Zero && GetForegroundWindow() == window;

    public static bool TryGetForegroundAllowedBounds(IEnumerable<string> processNames, out WindowBounds bounds)
    {
        bounds = default;
        try
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero) return false;
            if (!IsWindowAllowed(window, processNames) || !TryGetClientScreenBounds(window, out bounds))
                return false;
            return true;
        }
        catch { return false; }
    }

    public static bool DoesForegroundWindowCover(WindowBounds gameBounds)
    {
        try
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || !IsWindowVisible(foreground)) return false;
            GetWindowThreadProcessId(foreground, out var processId);
            if (processId == (uint)Environment.ProcessId) return false;
            if (!GetWindowRect(foreground, out var window)) return false;

            var left = Math.Max(gameBounds.Left, window.Left);
            var top = Math.Max(gameBounds.Top, window.Top);
            var right = Math.Min(gameBounds.Right, window.Right);
            var bottom = Math.Min(gameBounds.Bottom, window.Bottom);
            if (right <= left || bottom <= top) return false;
            var gameArea = Math.Max(1L, (long)(gameBounds.Right - gameBounds.Left) * (gameBounds.Bottom - gameBounds.Top));
            var coveredArea = (long)(right - left) * (bottom - top);
            return coveredArea / (double)gameArea >= 0.15;
        }
        catch { return false; }
    }

    public static bool TryActivateWindow(IEnumerable<string> processNames, out WindowBounds bounds)
    {
        bounds = default;
        foreach (var normalized in NormalizeNames(processNames))
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(normalized))
                {
                    using (process)
                    {
                        var window = process.MainWindowHandle;
                        if (window == IntPtr.Zero ||
                            !TryGetClientScreenBounds(window, out bounds))
                            continue;

                        if (IsIconic(window)) ShowWindowAsync(window, SwRestore);
                        SetForegroundWindow(window);
                        return true;
                    }
                }
            }
            catch
            {
                // Try the next configured process name.
            }
        }

        return false;
    }

    private static bool IsCachedGameProcess(int processId, IEnumerable<string> processNames)
    {
        lock (CacheLock)
        {
            if (_cachedGamePid == 0 || _cachedGamePid != processId) return false;
            if (_cachedGameWindow == IntPtr.Zero || !IsWindow(_cachedGameWindow)) return false;
            var key = string.Join("\n", NormalizeNames(processNames));
            return string.Equals(key, _cachedProcessKey, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static List<string> NormalizeNames(IEnumerable<string> processNames)
    {
        var names = new List<string>();
        foreach (var requestedName in processNames)
        {
            if (string.IsNullOrWhiteSpace(requestedName)) continue;
            var normalized = Path.GetFileNameWithoutExtension(requestedName.Trim());
            if (!string.IsNullOrWhiteSpace(normalized)) names.Add(normalized);
        }
        return names;
    }

    /// <summary>Shared "does this process name match AllowedProcesses" check.</summary>
    private static bool NameMatches(string? processName, IEnumerable<string> processNames)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        foreach (var name in processNames)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (string.Equals(Path.GetFileNameWithoutExtension(name.Trim()), processName,
                    StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string? GetProcessName(int processId)
    {
        if (processId <= 0) return null;
        lock (CacheLock)
        {
            var now = Environment.TickCount64;
            if (now - _nameCacheClearedTicks > NameCacheLifetimeMs)
            {
                ProcessNameCache.Clear();
                _nameCacheClearedTicks = now;
            }
            if (ProcessNameCache.TryGetValue(processId, out var cached)) return cached;

            var name = QueryProcessName(processId);
            if (name is null)
            {
                // Fallback for processes that refuse even limited query access.
                try
                {
                    using var process = Process.GetProcessById(processId);
                    name = process.ProcessName;
                }
                catch
                {
                    name = null;
                }
            }

            if (name is not null) ProcessNameCache[processId] = name;
            return name;
        }
    }

    private static string? QueryProcessName(int processId)
    {
        var handle = IntPtr.Zero;
        try
        {
            handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)processId);
            if (handle == IntPtr.Zero) return null;
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            if (!QueryFullProcessImageNameW(handle, 0, buffer, ref size) || size == 0) return null;
            var path = buffer.ToString(0, (int)size);
            var name = Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (handle != IntPtr.Zero) CloseHandle(handle);
        }
    }

    private static bool TryGetClientScreenBounds(IntPtr window, out WindowBounds bounds)
    {
        bounds = default;
        if (!GetClientRect(window, out var client) ||
            client.Right <= client.Left || client.Bottom <= client.Top)
            return false;
        var topLeft = new NativePoint { X = client.Left, Y = client.Top };
        var bottomRight = new NativePoint { X = client.Right, Y = client.Bottom };
        if (!ClientToScreen(window, ref topLeft) || !ClientToScreen(window, ref bottomRight))
            return false;
        bounds = new WindowBounds(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
        return bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;
    }
}
