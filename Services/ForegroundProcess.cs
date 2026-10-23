using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RoAura.Services;

public static class ForegroundProcess
{
    private const int SwRestore = 9;

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
    private static extern bool ShowWindowAsync(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public static string? GetName()
    {
        try
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero) return null;
            GetWindowThreadProcessId(window, out var processId);
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            return null;
        }
    }

    public static WindowBounds? FindWindowBounds(IEnumerable<string> processNames)
    {
        return TryFindWindow(processNames, out _, out var bounds) ? bounds : null;
    }

    public static bool TryFindWindow(IEnumerable<string> processNames, out IntPtr handle, out WindowBounds bounds)
    {
        handle = IntPtr.Zero;
        bounds = default;
        foreach (var requestedName in processNames)
        {
            var normalized = Path.GetFileNameWithoutExtension(requestedName.Trim());
            if (string.IsNullOrWhiteSpace(normalized)) continue;

            try
            {
                foreach (var process in Process.GetProcessesByName(normalized))
                {
                    using (process)
                    {
                        if (process.MainWindowHandle == IntPtr.Zero ||
                            !TryGetClientScreenBounds(process.MainWindowHandle, out bounds))
                            continue;

                        handle = process.MainWindowHandle;
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

    public static bool IsForegroundAllowed(IEnumerable<string> processNames)
    {
        var foregroundName = GetName();
        if (string.IsNullOrWhiteSpace(foregroundName)) return false;

        return processNames.Any(name => string.Equals(
            Path.GetFileNameWithoutExtension(name.Trim()),
            foregroundName,
            StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsWindowAllowed(IntPtr window, IEnumerable<string> processNames)
    {
        if (window == IntPtr.Zero) return false;
        try
        {
            GetWindowThreadProcessId(window, out var processId);
            using var process = Process.GetProcessById((int)processId);
            return processNames.Any(name => string.Equals(
                Path.GetFileNameWithoutExtension(name.Trim()), process.ProcessName,
                StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    public static bool TryGetForegroundAllowedBounds(IEnumerable<string> processNames, out WindowBounds bounds)
    {
        bounds = default;
        try
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero) return false;
            GetWindowThreadProcessId(window, out var processId);
            using var process = Process.GetProcessById((int)processId);
            var allowed = processNames.Any(name => string.Equals(
                Path.GetFileNameWithoutExtension(name.Trim()), process.ProcessName,
                StringComparison.OrdinalIgnoreCase));
            if (!allowed || !TryGetClientScreenBounds(window, out bounds))
                return false;
            return true;
        }
        catch { return false; }
    }

    public static bool TryActivateWindow(IEnumerable<string> processNames, out WindowBounds bounds)
    {
        bounds = default;
        foreach (var requestedName in processNames)
        {
            var normalized = Path.GetFileNameWithoutExtension(requestedName.Trim());
            if (string.IsNullOrWhiteSpace(normalized)) continue;

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
