using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using RoAura.Models;

namespace RoAura.Services;

internal static class QuestInputSmoke
{
    internal static async Task Run(QuestOverlayHost host,QuestWorkspace workspace,Window settingsWindow,Action<string> setProcesses)
    {
        var directory=Path.Combine(AppEnvironment.LocalDirectory,"QuestInputProbe");Directory.CreateDirectory(directory);
        settingsWindow.Hide();
        DebugLog.Write("QUEST INPUT: copying isolated probe executable");
        var exe=Path.Combine(directory,"RoAura-QuestProbe.exe");File.Copy(Environment.ProcessPath!,exe,true);
        DebugLog.Write("QUEST INPUT: launching isolated probe");
        var reportPath=Path.Combine(directory,"input.json");
        var start=new ProcessStartInfo(exe,"--smoke-test --quest-input-probe") {UseShellExecute=false};
        start.Environment["ROAURA_QUEST_PROBE_REPORT"]=reportPath;
        start.Environment["ROAURA_SMOKE_DATA"]=Path.Combine(directory,"ChildData");
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
            hook.EscapePressed+=()=>((App)Application.Current).ExitOverlayEditMode();
            hook.ToggleOverlaysPressed+=()=>settingsWindow.Dispatcher.BeginInvoke(()=>host.SetGlobalVisible(!host.GlobalVisible));
            void KeyboardMode()=>hook.SetKeyboardBlocked(host.BrowserRequested && host.BrowserShowing);
            host.KeyboardModeChanged+=KeyboardMode;
            try
            {
                hook.Install();host.RefreshNow();
                await Until(()=>host.ActionWindows.Any(w=>w.IsVisible),"tracker buttons");
                await Pause();
                SaveDesktop(host.TextWindow,"tracker-native");
                var row=workspace.Active.First();var initial=row.Progress.CurrentStep;
                var strip=host.ActionWindows.First(w=>w.IsVisible && ((StackPanel)w.Content).Children.OfType<Button>().Any(b=>Equals(b.Content,"Next")));
                var buttons=((StackPanel)strip.Content).Children.OfType<Button>().ToArray();
                // Real OS mouse delivery, not RaiseEvent: buttons consume clicks without activation.
                var originalStripHandle=new WindowInteropHelper(strip).Handle;
                var clicks=Read(reportPath)!.Clicks;
                Click(buttons[1].PointToScreen(new Point(buttons[1].ActualWidth/2,buttons[1].ActualHeight/2)));
                await Until(()=>row.Progress.CurrentStep==initial+1,"Next click");
                if(QuestWindowNative.GetForegroundWindow()!=game || Read(reportPath)!.Clicks!=clicks)
                    throw new InvalidOperationException("Quest button stole game focus or leaked its click.");
                await Pause();host.RefreshNow();strip=host.ActionWindows.First(w=>w.IsVisible && ((StackPanel)w.Content).Children.OfType<Button>().Any(b=>Equals(b.Content,"Next")));
                if(new WindowInteropHelper(strip).Handle!=originalStripHandle)throw new InvalidOperationException("Tracker action HWND was recreated after Next.");
                Click(strip.PointToScreen(new Point(0,0))); // fully transparent corner, a different process underneath
                await Until(()=>Read(reportPath)!.Clicks>clicks,"transparent button margin click-through");
                clicks=Read(reportPath)!.Clicks;
                Click(host.TextWindow.PointToScreen(new Point(x+15,y+40)));
                await Until(()=>Read(reportPath)!.Clicks>clicks,"text/world click-through");
                var doneStep=row.Progress.CurrentStep;
                var doneStrip=host.ActionWindows.First(w=>w.IsVisible && ((StackPanel)w.Content).Children.OfType<Button>().Any(b=>Equals(b.Content,"Done")));
                var done=((StackPanel)doneStrip.Content).Children.OfType<Button>().Single(b=>Equals(b.Content,"Done"));
                Click(done.PointToScreen(new Point(done.ActualWidth/2,done.ActualHeight/2)));
                await Until(()=>row.Progress.CurrentStep==doneStep+1,"Done confirms and advances current step");
                if(QuestWindowNative.GetForegroundWindow()!=game)throw new InvalidOperationException("Done stole game focus.");
                await Pause();host.RefreshNow();
                var multi=workspace.Active.FirstOrDefault(r=>QuestNavigation.Targets(r).Count>1);
                if(multi is not null)
                {
                    var index=workspace.Active.Where(r=>r.Progress.IsVisibleInOverlay).ToList().FindIndex(r=>ReferenceEquals(r.Progress,multi.Progress));
                    var naviStrip=host.ActionWindows.Where(w=>w.IsVisible && ((StackPanel)w.Content).Children.OfType<Button>().Any(b=>Equals(b.Content,"navi"))).ElementAt(index);
                    var navi=((StackPanel)naviStrip.Content).Children.OfType<Button>().Single(b=>Equals(b.Content,"navi"));
                    Click(navi.PointToScreen(new Point(navi.ActualWidth/2,navi.ActualHeight/2)));
                    await Until(()=>host.TargetChooser?.IsVisible==true,"rounded navigation chooser");
                    await Pause();SaveDesktop(host.TargetChooser!,"navigation-native");
                    var border=(Border)host.TargetChooser!.Content;var grid=(Grid)border.Child;
                    var target=((StackPanel)grid.Children.OfType<ScrollViewer>().Single().Content).Children.OfType<Button>().First();
                    Click(target.PointToScreen(new Point(target.ActualWidth/2,target.ActualHeight/2)));
                    await Until(()=>host.TargetChooser is null && host.ActionWindows.Any(w=>((StackPanel)w.Content).Children.OfType<Button>().Any(b=>Equals(b.Content,"copied") && b.Foreground==System.Windows.Media.Brushes.White)),"navigation copied checkmark");
                    await Pause();SaveDesktop(host.TextWindow,"tracker-copied-native");
                }
                var foldWindow=host.ActionWindows.First(w=>w.IsVisible && ((StackPanel)w.Content).Children.OfType<Button>().Any(b=>Equals(b.Content,"▾")));
                var fold=((StackPanel)foldWindow.Content).Children.OfType<Button>().Single();
                Click(fold.PointToScreen(new Point(fold.ActualWidth/2,fold.ActualHeight/2)));
                await Until(()=>row.Progress.IsCollapsed,"collapse tracker quest");await Pause();host.RefreshNow();
                var expandWindow=host.ActionWindows.First(w=>w.IsVisible && ((StackPanel)w.Content).Children.OfType<Button>().Any(b=>Equals(b.Content,"▸")));
                var expand=((StackPanel)expandWindow.Content).Children.OfType<Button>().Single();
                Click(expand.PointToScreen(new Point(expand.ActualWidth/2,expand.ActualHeight/2)));
                await Until(()=>!row.Progress.IsCollapsed,"expand tracker quest");await Pause();host.RefreshNow();
                // Reopen a guide previously left on Facts: More must force Walkthrough/current step.
                host.Browser.Page.ShowFacts(row.Guide!);
                var moreStrip=host.ActionWindows.First(w=>w.IsVisible && ((StackPanel)w.Content).Children.OfType<Button>().Any(b=>Equals(b.Content,"More")));
                var more=((StackPanel)moreStrip.Content).Children.OfType<Button>().Single(b=>Equals(b.Content,"More"));
                Click(more.PointToScreen(new Point(more.ActualWidth/2,more.ActualHeight/2)));
                await Until(()=>host.BrowserShowing && host.Browser.Page.CurrentStepInView,"More opens current instruction from Facts");
                var oldBrowserWidth=workspace.Settings.QuestOverlay.BrowserWidth;var oldBrowserX=workspace.Settings.QuestOverlay.BrowserX;
                workspace.Settings.QuestOverlay.BrowserWidth=760;workspace.Settings.QuestOverlay.BrowserX=100;
                var transformedCanvas=(Canvas)host.TextWindow.Content;
                transformedCanvas.RenderTransform=new System.Windows.Media.ScaleTransform(1.25,1.25);
                host.RefreshNow();await Pause();host.RefreshNow();
                var browserBounds=QuestWindowNative.Bounds(new WindowInteropHelper(host.Browser).Handle);
                var inside=new Point(browserBounds.Left+4,browserBounds.Top+80);var outside=new Point(browserBounds.Left-12,browserBounds.Top+80);
                if(transformedCanvas.Clip?.FillContains(transformedCanvas.PointFromScreen(inside))!=false ||
                    (transformedCanvas.Clip?.FillContains(transformedCanvas.PointFromScreen(outside))??true)!=true)
                    throw new InvalidOperationException("Tracker clip does not follow its actual WPF transform.");
                transformedCanvas.RenderTransform=System.Windows.Media.Transform.Identity;
                workspace.Settings.QuestOverlay.BrowserWidth=oldBrowserWidth;workspace.Settings.QuestOverlay.BrowserX=oldBrowserX;host.RefreshNow();
                host.HideBrowser(true);await Pause();
                foreach(var vk in new ushort[]{0x10,0x11,0x12}) {Key(vk,true);await Pause(50);Key(vk,false);await Pause(50);}
                Key(0x41,true);Key(0x41,false);
                var delivered=Read(reportPath)!;DebugLog.Write("QUEST INPUT DELIVERY: keys="+delivered.Keys+"; modifiers="+string.Join(",",delivered.Modifiers)+"; gameForeground="+(QuestWindowNative.GetForegroundWindow()==game)+"; browserVisible="+host.BrowserShowing+"; browserRequested="+host.BrowserRequested+"; blocked="+typeof(GlobalInputHook).GetField("_keyboardBlocked",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(hook));
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
                var pointButton=host.Browser.Page.CurrentNavigationButton ?? throw new InvalidOperationException("Current navigation button missing.");
                var coordinateCaption=pointButton.Content;
                Click(pointButton.PointToScreen(new Point(pointButton.ActualWidth/2,pointButton.ActualHeight/2)));
                await Until(()=>Equals(pointButton.Content,"copied"),"detail coordinate copied feedback");
                SaveDesktop(host.Browser,"detail-copied-native");
                await Until(()=>Equals(pointButton.Content,coordinateCaption),"detail coordinate label restored after four seconds");
                if(row.Guide is not null){host.Browser.Page.ShowFacts(row.Guide);await Pause();SaveDesktop(host.Browser,"facts-native");}
                var keys=Read(reportPath)!.Keys;Key(0x5A,true);Key(0x5A,false);await Pause();
                if(Read(reportPath)!.Keys!=keys)throw new InvalidOperationException("Quest search typing reached the game.");
                await Press(workspace.Settings.QuestBrowserHotkey);
                await Until(()=>!host.BrowserRequested && QuestWindowNative.GetForegroundWindow()==game,"browser close/restore correct client");
                await Press(workspace.Settings.OverlaysHotkey);
                await Until(()=>!host.GlobalVisible && !host.TextWindow.IsVisible,"global overlay hide");
                await Press(workspace.Settings.OverlaysHotkey);
                await Until(()=>host.GlobalVisible && host.TrackerRequested,"global overlay restore");
                await Pause();host.RefreshNow();
                var moveStrip=host.ActionWindows.First(w=>w.IsVisible && ((StackPanel)w.Content).Children.OfType<Button>().Any(b=>Equals(b.Content,"⋮⋮")));var handle=((StackPanel)moveStrip.Content).Children.OfType<Button>().Last();
                var grab=handle.PointToScreen(new Point(handle.ActualWidth/2,handle.ActualHeight/2));
                SetCursorPos((int)grab.X,(int)grab.Y);Send(new Input {Type=0,Data=new Union {Mouse=new Mouse {Flags=2}}});
                await Until(()=>host.IsDragging,"tracker drag start");
                hook.SetEscapeEnabled(true);SetCursorPos((int)grab.X+25,(int)grab.Y+35);
                await Until(()=>workspace.Settings.QuestOverlay.Y!=y,"nonactivating drag outside handle");
                await Press(workspace.Settings.FinishMoveHotkey);await Until(()=>!host.IsDragging,"finish tracker drag");
                Send(new Input {Type=0,Data=new Union {Mouse=new Mouse {Flags=4}}});hook.SetEscapeEnabled(false);
                if(settingsWindow.IsVisible)throw new InvalidOperationException("Finishing tracker drag opened main settings.");
                x=workspace.Settings.QuestOverlay.X;y=workspace.Settings.QuestOverlay.Y;
                settingsWindow.Show();settingsWindow.Activate();host.RefreshNow();
                var settingsBounds=QuestWindowNative.Bounds(new WindowInteropHelper(settingsWindow).Handle);
                foreach(var action in host.ActionWindows.Where(w=>w.IsVisible))
                {
                    var content=(StackPanel)action.Content;
                    foreach(var button in content.Children.OfType<Button>())
                    {
                        var center=new Point(button.ActualWidth/2,button.ActualHeight/2);
                        if(settingsBounds.Contains(button.PointToScreen(center)) && (content.Clip?.FillContains(button.TranslatePoint(center,content))??true))
                            throw new InvalidOperationException("Painted tracker button overlaps settings without a clip.");
                    }
                }
                settingsWindow.Hide();QuestWindowNative.SetForegroundWindow(game);await Pause();
                var oldLimit=workspace.Settings.QuestOverlay.MaxTrackedQuests;
                workspace.Settings.QuestOverlay.MaxTrackedQuests=1;host.RefreshNow();
                if(host.ActionWindows.Count(w=>((StackPanel)w.Content).Children.OfType<Button>().Any(b=>Equals(b.Content,"More")))!=1)
                    throw new InvalidOperationException("Tracker quest limit was not applied.");
                workspace.Settings.QuestOverlay.MaxTrackedQuests=oldLimit;host.RefreshNow();
                ((App)Application.Current).ToggleDatabase();
                var database=Application.Current.Windows.OfType<DatabaseOverlayWindow>().Single();
                await Until(()=>database.IsDatabaseOpen && database.IsVisible,"database priority open");
                QuestWindowNative.SetForegroundWindow(game);await Pause(350);host.RefreshNow();
                var databaseBounds=QuestWindowNative.Bounds(new WindowInteropHelper(database).Handle);
                var dbCenter=new Point(databaseBounds.X+databaseBounds.Width/2,databaseBounds.Y+databaseBounds.Height/2);
                var questCanvas=(Canvas)host.TextWindow.Content;
                if(questCanvas.Clip?.FillContains(host.TextWindow.PointFromScreen(dbCenter))!=false)
                    throw new InvalidOperationException("Quest tracker paints above the database.");
                var ordinary=Application.Current.Windows.OfType<OverlayWindow>().Single();
                var surface=(FrameworkElement)ordinary.FindName("OverlaySurface");
                if(surface.Clip?.FillContains(ordinary.PointFromScreen(dbCenter))!=false)
                    throw new InvalidOperationException("Statistics/ordinary overlays paint above the database.");
                SaveDesktop(database,"database-priority-native");
                ((App)Application.Current).CloseDatabaseOverlay();await Pause();
                SetWindowPos(game,IntPtr.Zero,20,20,340,240,0x14);await Pause();host.RefreshNow();
                if(workspace.Settings.QuestOverlay.X!=x || workspace.Settings.QuestOverlay.Y!=y)
                    throw new InvalidOperationException("Game resize rewrote tracker coordinates.");
                ShowWindow(game,6);await Pause();host.RefreshNow();
                if(host.TextWindow.IsVisible || host.BrowserShowing)throw new InvalidOperationException("Quest surfaces remained above a minimized game.");
                DebugLog.Write("QUEST INPUT SMOKE: cross-process clicks, transparent margins/text, keyboard/modifiers, independent keys, global mask, settings occlusion and resize passed.");
            }
            finally {host.KeyboardModeChanged-=KeyboardMode;hook.SetKeyboardBlocked(false);host.HideBrowser(false);}
        }
        catch(Exception ex) {DebugLog.Write("QUEST INPUT FAILURE: "+ex);DebugLog.Flush();throw;}
        finally
        {
            workspace.Settings.AllowedProcesses=original;setProcesses(string.Join(", ",original));
            if(!process.HasExited) {process.CloseMainWindow();if(!process.WaitForExit(2000)){process.Kill(true);process.WaitForExit();}}
        }
    }
    private static void SaveDesktop(Window window,string name)
    {
        DebugLog.Write("QUEST INPUT CAPTURE: "+name);
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
    {DebugLog.Write("QUEST INPUT WAIT: "+name);var end=DateTime.UtcNow.AddSeconds(6);while(DateTime.UtcNow<end){if(condition()){DebugLog.Write("QUEST INPUT PASS: "+name);return;}await Pause(60);}throw new InvalidOperationException("Quest input smoke timeout: "+name);}
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
