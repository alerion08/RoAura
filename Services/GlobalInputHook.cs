using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
using RoAura.Models;

namespace RoAura.Services;

/// <summary>Observes configurable keyboard chords without registering or reserving Windows keys.</summary>
public sealed class GlobalInputHook : IDisposable
{
    private const int WhKeyboardLl=13, WmKeyDown=0x100, WmKeyUp=0x101, WmSysKeyDown=0x104, WmSysKeyUp=0x105;
    private const uint ProcessQueryLimitedInformation=0x1000;
    private readonly AppSettings _settings;
    private readonly LowLevelKeyboardProc _keyboardProc;
    private IntPtr _keyboardHook,_cachedWindow;
    private uint _cachedProcessId;
    private bool _cachedAllowed,_screenshotChordHeld;
    private Timer? _timer;
    private int _polling;
    private volatile bool _keyboardBlocked,_finishMoveEnabled;
    private Entry[] _entries=[];
    private sealed class Entry(HotkeyChord chord,Action action,bool finish)
    {
        public readonly HotkeyChord Chord=chord;
        public readonly Action Action=action;
        public readonly bool Finish=finish;
        public int Held;
    }
    public event Action? ToggleSettingsPressed,ToggleOverlaysPressed,EscapePressed,ToggleTimersPressed,
        PauseSessionPressed,AddMvpTimerPressed,ToggleDatabasePressed,ToggleQuestBrowserPressed,ToggleQuestTrackerPressed;
    public GlobalInputHook(AppSettings settings)
    {
        _settings=settings;_keyboardProc=KeyboardHookCallback;
        RefreshVirtualKeys();settings.PropertyChanged+=SettingsChanged;
    }
    public bool IsHotkeyEnabled(string value) => HotkeyChord.TryParse(value,out var chord) &&
        Volatile.Read(ref _entries).Any(entry=>entry.Chord==chord);
    public void Install()
    {
        if(_timer is not null)return;
        _timer=new Timer(_=>PollKeys(),null,TimeSpan.Zero,TimeSpan.FromMilliseconds(35));
        _keyboardHook=SetWindowsHookEx(WhKeyboardLl,_keyboardProc,IntPtr.Zero,0);
        if(_keyboardHook==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public bool SetEscapeEnabled(bool enabled){_finishMoveEnabled=enabled;return true;}
    public void SetKeyboardBlocked(bool blocked){_keyboardBlocked=blocked;if(!blocked)_screenshotChordHeld=false;}
    private void SettingsChanged(object? sender,PropertyChangedEventArgs e)
    {if(string.IsNullOrEmpty(e.PropertyName) || e.PropertyName.EndsWith("Hotkey",StringComparison.Ordinal))RefreshVirtualKeys();}
    private void RefreshVirtualKeys()
    {
        var entries=new List<Entry>();
        var used=new HashSet<HotkeyChord>();
        void Add(string value,Action action,bool finish=false)
        {
            if(HotkeyChord.TryParse(value,out var chord) && used.Add(chord))entries.Add(new(chord,action,finish) { Held=Down(chord.VirtualKey)?1:0 });
        }
        Add(_settings.SettingsHotkey,()=>ToggleSettingsPressed?.Invoke());
        Add(_settings.OverlaysHotkey,()=>ToggleOverlaysPressed?.Invoke());
        Add(_settings.FinishMoveHotkey,()=>EscapePressed?.Invoke(),true);
        Add(_settings.TimersHotkey,()=>ToggleTimersPressed?.Invoke());
        Add(_settings.DatabaseHotkey,()=>ToggleDatabasePressed?.Invoke());
        var quests=QuestHotkeys.Enabled(_settings);
        if(quests.Browser)Add(_settings.QuestBrowserHotkey,()=>ToggleQuestBrowserPressed?.Invoke());
        if(quests.Tracker)Add(_settings.QuestTrackerHotkey,()=>ToggleQuestTrackerPressed?.Invoke());
        if(FarmingHotkeys.Enabled(_settings,_settings.PauseSessionHotkey))Add(_settings.PauseSessionHotkey,()=>PauseSessionPressed?.Invoke());
        if(FarmingHotkeys.Enabled(_settings,_settings.AddMvpTimerHotkey))Add(_settings.AddMvpTimerHotkey,()=>AddMvpTimerPressed?.Invoke());
        Volatile.Write(ref _entries,entries.ToArray());
    }
    private static bool Down(int key)=>(GetAsyncKeyState(key)&0x8000)!=0;
    private static ModifierKeys Modifiers() =>
        (Down(0x10)?ModifierKeys.Shift:ModifierKeys.None) |
        (Down(0x11)?ModifierKeys.Control:ModifierKeys.None) |
        (Down(0x12)?ModifierKeys.Alt:ModifierKeys.None) |
        (Down(0x5B)||Down(0x5C)?ModifierKeys.Windows:ModifierKeys.None);
    private void PollKeys()
    {
        if(Interlocked.Exchange(ref _polling,1)!=0)return;
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(),out var process);
            var suppressed=HotkeyCaptureGate.Suppressed(Down);
            // Active game/application input is delivered by the hook, including short taps.
            // Polling remains a fallback and retains legacy global hotkeys outside those windows.
            if(_keyboardHook!=IntPtr.Zero && (process==(uint)Environment.ProcessId || IsForegroundWindowAllowed()))return;
            var allowed=!_keyboardBlocked || process==(uint)Environment.ProcessId;
            var modifiers=Modifiers();
            foreach(var entry in Volatile.Read(ref _entries))
            {
                var down=Down(entry.Chord.VirtualKey);
                var wasHeld=Interlocked.Exchange(ref entry.Held,down?1:0)!=0;
                if(allowed && !suppressed && down && !wasHeld && (!entry.Finish || _finishMoveEnabled) &&
                    entry.Chord.Matches(entry.Chord.VirtualKey,modifiers))entry.Action();
            }
        }
        catch(Exception ex){DebugLog.Write($"HOTKEY POLL ERROR: {ex}");}
        finally{Interlocked.Exchange(ref _polling,0);}
    }
    private IntPtr KeyboardHookCallback(int code,IntPtr message,IntPtr data)
    {
        try
        {
            if(code<0)return CallNextHookEx(_keyboardHook,code,message,data);
            var gameForeground=IsForegroundWindowAllowed();
            GetWindowThreadProcessId(GetForegroundWindow(),out var process);
            if(!gameForeground && process!=(uint)Environment.ProcessId)
                return CallNextHookEx(_keyboardHook,code,message,data);
            var isDown=message==new IntPtr(WmKeyDown)||message==new IntPtr(WmSysKeyDown);
            var isUp=message==new IntPtr(WmKeyUp)||message==new IntPtr(WmSysKeyUp);
            if(!isDown&&!isUp)return CallNextHookEx(_keyboardHook,code,message,data);
            var key=Marshal.ReadInt32(data);
            var modifiers=Modifiers();
            var suppressed=HotkeyCaptureGate.IsCapturing || HotkeyCaptureGate.AwaitingReleaseKey>0;
            foreach(var entry in Volatile.Read(ref _entries).Where(entry=>entry.Chord.VirtualKey==key))
            {
                var wasHeld=Interlocked.Exchange(ref entry.Held,isDown?1:0)!=0;
                if(!suppressed && isDown && !wasHeld && (!entry.Finish||_finishMoveEnabled) && entry.Chord.Matches(key,modifiers))entry.Action();
            }
            if(!_keyboardBlocked || !gameForeground || suppressed || IsAlwaysForwarded(key))
                return CallNextHookEx(_keyboardHook,code,message,data);
            if(key==0x53 && (_screenshotChordHeld || IsScreenshotShortcut(key,
                modifiers.HasFlag(ModifierKeys.Windows),modifiers.HasFlag(ModifierKeys.Shift))))
            {_screenshotChordHeld=isDown;return CallNextHookEx(_keyboardHook,code,message,data);}
            return new IntPtr(1);
        }
        catch(Exception ex){DebugLog.Write($"KEYBOARD HOOK ERROR: {ex}");return CallNextHookEx(_keyboardHook,code,message,data);}
    }
    public static bool IsScreenshotShortcut(int key,bool windows,bool shift)=>key==0x53&&windows&&shift;
    public static bool IsAlwaysForwarded(int key)=>key==0x2C||HotkeyChord.IsModifier(key);
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


    public void Dispose()
    {
        _keyboardBlocked=false;_settings.PropertyChanged-=SettingsChanged;
        if(_keyboardHook!=IntPtr.Zero)UnhookWindowsHookEx(_keyboardHook);
        _keyboardHook=IntPtr.Zero;_timer?.Dispose();_timer=null;
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
