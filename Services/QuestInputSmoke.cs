using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using RoAura.Models;

namespace RoAura.Services;

internal static class QuestInputSmoke
{
    internal static async Task Run(QuestOverlayHost host,QuestWorkspace workspace,Window settingsWindow,Action<string> setProcesses)
    {
        var directory=Path.Combine(AppEnvironment.LocalDirectory,"QuestInputProbe");Directory.CreateDirectory(directory);
        var exe=Path.Combine(directory,"RoAura-QuestProbe.exe");File.Copy(Environment.ProcessPath!,exe,true);
        var reportPath=Path.Combine(directory,"input.json");
        var start=new ProcessStartInfo(exe,"--smoke-test --quest-input-probe") {UseShellExecute=false};
        start.Environment["ROAURA_QUEST_PROBE_REPORT"]=reportPath;
        start.Environment.Remove("ROAURA_QUEST_UI_SNAPSHOTS");
        using var process=Process.Start(start) ?? throw new InvalidOperationException("Could not start isolated quest input probe.");
        var original=workspace.Settings.AllowedProcesses.ToList();
        var x=workspace.Settings.QuestOverlay.X;var y=workspace.Settings.QuestOverlay.Y;
        try
        {
            await Until(()=>Read(reportPath)?.Handle>0,"fake game startup");
            var game=new IntPtr(Read(reportPath)!.Handle);
            workspace.Settings.AllowedProcesses=["RoAura-QuestProbe"];setProcesses("RoAura-QuestProbe");
            QuestWindowNative.SetForegroundWindow(game);
            await Until(()=>QuestWindowNative.GetForegroundWindow()==game,"fake game activation");
            using var hook=new GlobalInputHook(workspace.Settings);
            hook.ToggleQuestBrowserPressed+=()=>settingsWindow.Dispatcher.BeginInvoke(host.ToggleBrowser);
            hook.ToggleQuestTrackerPressed+=()=>settingsWindow.Dispatcher.BeginInvoke(host.ToggleTracker);
            hook.ToggleOverlaysPressed+=()=>settingsWindow.Dispatcher.BeginInvoke(()=>host.SetGlobalVisible(!host.GlobalVisible));
            void KeyboardMode()=>hook.SetKeyboardBlocked(host.BrowserShowing);
            host.KeyboardModeChanged+=KeyboardMode;
            try
            {
                hook.Install();host.RefreshNow();
                await Until(()=>host.ActionWindows.Any(w=>w.IsVisible),"tracker buttons");
                await Pause();
                SaveDesktop(host.TextWindow,"tracker-native");
                var row=workspace.Active.First();var initial=row.Progress.CurrentStep;
                var strip=host.ActionWindows.First(w=>w.IsVisible);
                var buttons=((StackPanel)strip.Content).Children.OfType<Button>().ToArray();
                // Real OS mouse delivery, not RaiseEvent: buttons consume clicks without activation.
                var clicks=Read(reportPath)!.Clicks;
                Click(buttons[1].PointToScreen(new Point(buttons[1].ActualWidth/2,buttons[1].ActualHeight/2)));
                await Until(()=>row.Progress.CurrentStep==initial+1,"Next click");
                if(QuestWindowNative.GetForegroundWindow()!=game || Read(reportPath)!.Clicks!=clicks)
                    throw new InvalidOperationException("Quest button stole game focus or leaked its click.");
                await Pause();host.RefreshNow();strip=host.ActionWindows.First(w=>w.IsVisible);
                Click(strip.PointToScreen(new Point(0,0))); // fully transparent corner, a different process underneath
                await Until(()=>Read(reportPath)!.Clicks>clicks,"transparent button margin click-through");
                clicks=Read(reportPath)!.Clicks;
                Click(host.TextWindow.PointToScreen(new Point(x+15,y+10)));
                await Until(()=>Read(reportPath)!.Clicks>clicks,"text/world click-through");
                foreach(var vk in new ushort[]{0x10,0x11,0x12}) {Key(vk,true);await Pause(50);Key(vk,false);await Pause(50);}
                Key(0x41,true);Key(0x41,false);
                await Until(()=>Read(reportPath)!.Keys>0 && Read(reportPath)!.Modifiers.Count>=3,"game keyboard and modifiers");
                await Press(workspace.Settings.QuestTrackerHotkey);
                await Until(()=>!host.TrackerRequested,"independent tracker hotkey");
                if(host.BrowserRequested)throw new InvalidOperationException("Tracker hotkey opened the browser.");
                await Press(workspace.Settings.QuestTrackerHotkey);
                await Until(()=>host.TrackerRequested,"tracker restore hotkey");
                await Press(workspace.Settings.QuestBrowserHotkey);
                await Until(()=>host.BrowserShowing && host.Browser.IsActive,"browser hotkey/focus");
                if(!host.TrackerRequested)throw new InvalidOperationException("Browser toggle changed tracker preference.");
                host.Browser.Page.ShowGuide(row.Guide);await Pause();SaveDesktop(host.Browser,"browser-native");
                if(row.Guide is not null){host.Browser.Page.ShowFacts(row.Guide);await Pause();SaveDesktop(host.Browser,"facts-native");}
                var keys=Read(reportPath)!.Keys;Key(0x5A,true);Key(0x5A,false);await Pause();
                if(Read(reportPath)!.Keys!=keys)throw new InvalidOperationException("Quest search typing reached the game.");
                await Press(workspace.Settings.QuestBrowserHotkey);
                await Until(()=>!host.BrowserRequested && QuestWindowNative.GetForegroundWindow()==game,"browser close/restore correct client");
                await Press(workspace.Settings.OverlaysHotkey);
                await Until(()=>!host.GlobalVisible && !host.TextWindow.IsVisible,"global overlay hide");
                await Press(workspace.Settings.OverlaysHotkey);
                await Until(()=>host.GlobalVisible && host.TrackerRequested,"global overlay restore");
                settingsWindow.Show();settingsWindow.Activate();host.RefreshNow();
                if(host.ActionWindows.Any(w=>w.IsVisible && QuestWindowNative.Bounds(new WindowInteropHelper(w).Handle).IntersectsWith(QuestWindowNative.Bounds(new WindowInteropHelper(settingsWindow).Handle))))
                    throw new InvalidOperationException("Tracker actions overlap settings.");
                settingsWindow.Hide();QuestWindowNative.SetForegroundWindow(game);await Pause();
                SetWindowPos(game,IntPtr.Zero,20,20,340,240,0x14);await Pause();host.RefreshNow();
                if(workspace.Settings.QuestOverlay.X!=x || workspace.Settings.QuestOverlay.Y!=y)
                    throw new InvalidOperationException("Game resize rewrote tracker coordinates.");
                ShowWindow(game,6);await Pause();host.RefreshNow();
                if(host.TextWindow.IsVisible || host.BrowserShowing)throw new InvalidOperationException("Quest surfaces remained above a minimized game.");
                DebugLog.Write("QUEST INPUT SMOKE: cross-process clicks, transparent margins/text, keyboard/modifiers, independent keys, global mask, settings occlusion and resize passed.");
            }
            finally {host.KeyboardModeChanged-=KeyboardMode;hook.SetKeyboardBlocked(false);host.HideBrowser(false);}
        }
        finally
        {
            workspace.Settings.AllowedProcesses=original;setProcesses(string.Join(", ",original));
            if(!process.HasExited) {process.CloseMainWindow();if(!process.WaitForExit(2000)){process.Kill(true);process.WaitForExit();}}
        }
    }
    private static void SaveDesktop(Window window,string name)
    {
        var folder=Environment.GetEnvironmentVariable("ROAURA_QUEST_UI_SNAPSHOTS");if(string.IsNullOrWhiteSpace(folder))return;
        var rect=QuestWindowNative.Bounds(new WindowInteropHelper(window).Handle);if(rect.IsEmpty)return;
        var screen=GetDC(IntPtr.Zero);var dc=CreateCompatibleDC(screen);var bitmap=CreateCompatibleBitmap(screen,(int)rect.Width,(int)rect.Height);
        var old=SelectObject(dc,bitmap);
        try
        {
            if(!BitBlt(dc,0,0,(int)rect.Width,(int)rect.Height,screen,(int)rect.X,(int)rect.Y,0x40CC0020))throw new InvalidOperationException("Cannot capture isolated quest test desktop.");
            var image=System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(bitmap,IntPtr.Zero,Int32Rect.Empty,System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            Directory.CreateDirectory(folder);using var file=File.Create(Path.Combine(folder,name+".png"));encoder.Save(file);
        }
        finally{SelectObject(dc,old);DeleteObject(bitmap);DeleteDC(dc);ReleaseDC(IntPtr.Zero,screen);}
    }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd,IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc,int width,int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest,int x,int y,int width,int height,IntPtr source,int sx,int sy,uint flags);
    private static QuestInputProbe.Report? Read(string path)
    {try{using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);return JsonSerializer.Deserialize<QuestInputProbe.Report>(file);}catch(Exception ex) when(ex is IOException or JsonException){return null;}}
    private static async Task Until(Func<bool> condition,string name)
    {var end=DateTime.UtcNow.AddSeconds(6);while(DateTime.UtcNow<end){if(condition())return;await Pause(60);}throw new InvalidOperationException("Quest input smoke timeout: "+name);}
    private static Task Pause(int ms=180)=>Task.Delay(ms);
    private static async Task Press(string name)
    {var key=(System.Windows.Input.Key)Enum.Parse(typeof(System.Windows.Input.Key),name,true);var vk=(ushort)System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);Key(vk,true);await Pause(100);Key(vk,false);await Pause();}
    private static void Click(Point point)
    {SetCursorPos((int)point.X,(int)point.Y);Send(new Input {Type=0,Data=new Union {Mouse=new Mouse {Flags=2}}},new Input {Type=0,Data=new Union {Mouse=new Mouse {Flags=4}}});}
    private static void Key(ushort key,bool down)=>Send(new Input {Type=1,Data=new Union {Keyboard=new Keyboard {Vk=key,Flags=down?0u:2u}}});
    private static void Send(params Input[] inputs)
    {if(SendInput((uint)inputs.Length,inputs,Marshal.SizeOf<Input>())!=inputs.Length)throw new InvalidOperationException("CI desktop rejected real input injection; manual acceptance required.");}
    [StructLayout(LayoutKind.Sequential)] private struct Input {public uint Type;public Union Data;}
    [StructLayout(LayoutKind.Explicit)] private struct Union {[FieldOffset(0)] public Mouse Mouse;[FieldOffset(0)] public Keyboard Keyboard;}
    [StructLayout(LayoutKind.Sequential)] private struct Mouse {public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
    [StructLayout(LayoutKind.Sequential)] private struct Keyboard {public ushort Vk,Scan;public uint Flags,Time;public UIntPtr Extra;}
    [DllImport("user32.dll")] private static extern uint SendInput(uint count,Input[] inputs,int size);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window,int command);
}
