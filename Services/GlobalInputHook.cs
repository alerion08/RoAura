using System.Runtime.InteropServices;
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

    public event Action? ToggleSettingsPressed;
    public event Action? ToggleOverlaysPressed;
    public event Action? EscapePressed;
    public event Action? ToggleTimersPressed;
    public event Action? ToggleDatabasePressed;

    public GlobalInputHook(AppSettings settings)
    {
        _settings = settings;
        _keyboardProc = KeyboardHookCallback;
    }

    public void Install()
    {
        if (_timer is not null) return;
        _timer = new Timer(_ => PollKeys(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(35));
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, IntPtr.Zero, 0);
    }

    public bool SetEscapeEnabled(bool enabled)
    {
        _finishMoveEnabled = enabled;
        if (!enabled) _finishMoveHeld = false;
        return true;
    }

    public void SetKeyboardBlocked(bool blocked) => _keyboardBlocked = blocked;

    private IntPtr KeyboardHookCallback(int code, IntPtr message, IntPtr data)
    {
        var foreground = GetForegroundWindow();
        var belongsToGame = ForegroundProcess.IsWindowAllowed(foreground, _settings.AllowedProcesses);
        if (code >= 0 && _keyboardBlocked && belongsToGame &&
            (message == new IntPtr(WmKeyDown) || message == new IntPtr(WmKeyUp) ||
             message == new IntPtr(WmSysKeyDown) || message == new IntPtr(WmSysKeyUp)))
        {
            var virtualKey = Marshal.ReadInt32(data);
            var isDown = message == new IntPtr(WmKeyDown) || message == new IntPtr(WmSysKeyDown);
            if (IsModifier(virtualKey))
                return CallNextHookEx(_keyboardHook, code, message, data);
            if (virtualKey == ParseVirtualKey(_settings.DatabaseHotkey))
            {
                if (isDown && !_databaseHookHeld) ToggleDatabasePressed?.Invoke();
                _databaseHookHeld = isDown;
            }
            else if (virtualKey == ParseVirtualKey(_settings.SettingsHotkey))
            {
                if (isDown && !_settingsHookHeld) ToggleSettingsPressed?.Invoke();
                _settingsHookHeld = isDown;
            }
            return new IntPtr(1);
        }
        return CallNextHookEx(_keyboardHook, code, message, data);
    }

    private static bool IsModifier(int virtualKey) => virtualKey is
        VkShift or VkControl or VkMenu or VkLShift or VkRShift or
        VkLControl or VkRControl or VkLMenu or VkRMenu;

    private void PollKeys()
    {
        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out var foregroundProcessId);
        if (!_keyboardBlocked || foregroundProcessId == (uint)Environment.ProcessId)
            PollKey(ParseVirtualKey(_settings.SettingsHotkey), ref _settingsHeld,
                ToggleSettingsPressed);
        else
            _settingsHeld = false;
        PollKey(ParseVirtualKey(_settings.OverlaysHotkey), ref _overlaysHeld,
            ToggleOverlaysPressed);
        PollKey(ParseVirtualKey(_settings.TimersHotkey), ref _timersHeld,
            ToggleTimersPressed);
        if (!_keyboardBlocked || foregroundProcessId == (uint)Environment.ProcessId)
            PollKey(ParseVirtualKey(_settings.DatabaseHotkey), ref _databaseHeld,
                ToggleDatabasePressed);
        else
            _databaseHeld = false;

        if (_finishMoveEnabled)
            PollKey(ParseVirtualKey(_settings.FinishMoveHotkey), ref _finishMoveHeld,
                EscapePressed);
        else
            _finishMoveHeld = false;
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
}
