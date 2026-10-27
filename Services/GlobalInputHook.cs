using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
using RoAura.Models;

namespace RoAura.Services;

/// <summary>
/// Polls configurable keys without reserving them as Windows system hotkeys.
/// This is the proven input implementation from RoAura 2.8.1.
/// </summary>
public sealed class GlobalInputHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLShift = 0xA0;
    private const int VkRShift = 0xA1;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int VkLMenu = 0xA4;
    private const int VkRMenu = 0xA5;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private readonly AppSettings _settings;
    private Timer? _timer;
    private bool _settingsHeld;
    private bool _overlaysHeld;
    private bool _finishMoveHeld;
    private bool _timersHeld;
    private bool _databaseHeld;
    private bool _databaseHookHeld;
    private bool _settingsHookHeld;
    private volatile bool _finishMoveEnabled;
    private volatile bool _keyboardBlocked;
    private IntPtr _keyboardHook;
    private readonly LowLevelKeyboardProc _keyboardProc;
    private int _polling;
    private volatile int _settingsKey;
    private volatile int _overlaysKey;
    private volatile int _finishMoveKey;
    private volatile int _timersKey;
    private volatile int _databaseKey;
    // Tiny one-entry cache for the hook callback: the foreground window
    // rarely changes between keystrokes, so resolving its process name once
    // keeps the low-level hook well inside the system's callback budget.
    private IntPtr _cachedWindow;
    private uint _cachedProcessId;
    private bool _cachedAllowed;

    public event Action? ToggleSettingsPressed;
    public event Action? ToggleOverlaysPressed;
    public event Action? EscapePressed;
    public event Action? ToggleTimersPressed;
    public event Action? ToggleDatabasePressed;

    public GlobalInputHook(AppSettings settings)
    {
        _settings = settings;
        _keyboardProc = KeyboardHookCallback;
        RefreshVirtualKeys();
        _settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    public void Install()
    {
        if (_timer is not null) return;
        _timer = new Timer(_ => PollKeys(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(35));
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, IntPtr.Zero, 0);
        // Polling keeps working without the hook; the caller reports the failure.
        if (_keyboardHook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public bool SetEscapeEnabled(bool enabled)
    {
        _finishMoveEnabled = enabled;
        if (!enabled) _finishMoveHeld = false;
        return true;
    }

    public void SetKeyboardBlocked(bool blocked) => _keyboardBlocked = blocked;

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case null:
            case "":
            case nameof(AppSettings.SettingsHotkey):
            case nameof(AppSettings.OverlaysHotkey):
            case nameof(AppSettings.FinishMoveHotkey):
            case nameof(AppSettings.TimersHotkey):
            case nameof(AppSettings.DatabaseHotkey):
                RefreshVirtualKeys();
                break;
        }
    }

    private void RefreshVirtualKeys()
    {
        _settingsKey = ParseVirtualKey(_settings.SettingsHotkey);
        _overlaysKey = ParseVirtualKey(_settings.OverlaysHotkey);
        _finishMoveKey = ParseVirtualKey(_settings.FinishMoveHotkey);
        _timersKey = ParseVirtualKey(_settings.TimersHotkey);
        _databaseKey = ParseVirtualKey(_settings.DatabaseHotkey);
    }

    private IntPtr KeyboardHookCallback(int code, IntPtr message, IntPtr data)
    {
        try
        {
            if (code < 0 || !_keyboardBlocked)
                return CallNextHookEx(_keyboardHook, code, message, data);

            var isDown = message == new IntPtr(WmKeyDown) || message == new IntPtr(WmSysKeyDown);
            var isUp = message == new IntPtr(WmKeyUp) || message == new IntPtr(WmSysKeyUp);
            if (!isDown && !isUp)
                return CallNextHookEx(_keyboardHook, code, message, data);

            if (!IsForegroundWindowAllowed())
                return CallNextHookEx(_keyboardHook, code, message, data);

            var virtualKey = Marshal.ReadInt32(data);
            if (IsModifier(virtualKey))
                return CallNextHookEx(_keyboardHook, code, message, data);
            if (virtualKey == _databaseKey)
            {
                if (isDown && !_databaseHookHeld) ToggleDatabasePressed?.Invoke();
                _databaseHookHeld = isDown;
            }
            else if (virtualKey == _settingsKey)
            {
                if (isDown && !_settingsHookHeld) ToggleSettingsPressed?.Invoke();
                _settingsHookHeld = isDown;
            }
            return new IntPtr(1);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"KEYBOARD HOOK ERROR: {ex}");
            return CallNextHookEx(_keyboardHook, code, message, data);
        }
    }

    private bool IsForegroundWindowAllowed()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return false;
        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0) return false;
        if (window == _cachedWindow && processId == _cachedProcessId)
            return _cachedAllowed;

        var allowed = false;
        var processName = GetProcessName(processId);
        if (processName is not null)
        {
            var allowedProcesses = _settings.AllowedProcesses;
            foreach (var name in allowedProcesses)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (string.Equals(Path.GetFileNameWithoutExtension(name.Trim()), processName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    allowed = true;
                    break;
                }
            }
        }

        _cachedWindow = window;
        _cachedProcessId = processId;
        _cachedAllowed = allowed;
        return allowed;
    }

    private static string? GetProcessName(uint processId)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            if (!QueryFullProcessImageName(process, 0, buffer, ref size) || size == 0)
                return null;
            return Path.GetFileNameWithoutExtension(buffer.ToString());
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static bool IsModifier(int virtualKey) => virtualKey is
        VkShift or VkControl or VkMenu or VkLShift or VkRShift or
        VkLControl or VkRControl or VkLMenu or VkRMenu;

    private void PollKeys()
    {
        if (Interlocked.Exchange(ref _polling, 1) != 0) return;
        try
        {
            var foreground = GetForegroundWindow();
            GetWindowThreadProcessId(foreground, out var foregroundProcessId);
            if (!_keyboardBlocked || foregroundProcessId == (uint)Environment.ProcessId)
                PollKey(_settingsKey, ref _settingsHeld, ToggleSettingsPressed);
            else
                _settingsHeld = false;
            PollKey(_overlaysKey, ref _overlaysHeld, ToggleOverlaysPressed);
            PollKey(_timersKey, ref _timersHeld, ToggleTimersPressed);
            if (!_keyboardBlocked || foregroundProcessId == (uint)Environment.ProcessId)
                PollKey(_databaseKey, ref _databaseHeld, ToggleDatabasePressed);
            else
                _databaseHeld = false;

            if (_finishMoveEnabled)
                PollKey(_finishMoveKey, ref _finishMoveHeld, EscapePressed);
            else
                _finishMoveHeld = false;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"HOTKEY POLL ERROR: {ex}");
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    private static void PollKey(int virtualKey, ref bool held, Action? action)
    {
        if (virtualKey <= 0)
        {
            held = false;
            return;
        }

        var down = (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        if (down && !held) action?.Invoke();
        held = down;
    }

    private static int ParseVirtualKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var normalized = value.Trim();
        if (normalized.Equals("Esc", StringComparison.OrdinalIgnoreCase))
            normalized = nameof(Key.Escape);
        if (!Enum.TryParse<Key>(normalized, true, out var key)) return 0;
        return KeyInterop.VirtualKeyFromKey(key);
    }

    public void Dispose()
    {
        _keyboardBlocked = false;
        _settings.PropertyChanged -= OnSettingsPropertyChanged;
        if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
        _keyboardHook = IntPtr.Zero;
        _timer?.Dispose();
        _timer = null;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookId, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true,
        EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags,
        StringBuilder exeName, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
