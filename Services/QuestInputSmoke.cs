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
        var bundle=Path.Combine(AppContext.BaseDirectory,"Data","Minimaps");
        var offlineMaps=new MvpMapImageCache(_=>throw new InvalidOperationException("Bundled maps must not use the network."),bundle,bundle);
        var mapFiles=Directory.GetFiles(bundle,"*.png");
        if(mapFiles.Length==0)throw new InvalidOperationException("MVP minimap bundle is empty.");
        foreach(var mapFile in mapFiles)
            if(await offlineMaps.GetAsync(Path.GetFileNameWithoutExtension(mapFile)) is null)
                throw new InvalidOperationException("Bundled MVP map cannot decode offline: "+mapFile);
        DebugLog.Write($"MVP MAP OFFLINE DECODE PASS: {mapFiles.Length} maps.");
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
            hook.AddMvpTimerPressed+=()=>settingsWindow.Dispatcher.BeginInvoke(()=>((MainWindow)settingsWindow).AddMvpTimerInGame());
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
                var strip=host.ActionWindows.First(w=>w.IsVisible && ((StackPanel)w.Content).Children.OfType<Button>().Any(b=>Equals(b.Content,"next")));
                var buttons=((StackPanel)strip.Content).Children.OfType<Button>().ToArray();
                // Validate after dispatcher/render processing, not only immediately after SetWindowPos.
                foreach(var actionSurface in host.ActionWindows.Where(w=>w.IsVisible))
                {
                    var bounds=QuestWindowNative.Bounds(new WindowInteropHelper(actionSurface).Handle);
                    var dpi=QuestWindowNative.Scale(new WindowInteropHelper(actionSurface).Handle);
                    if(Math.Abs(actionSurface.Width*dpi-bounds.Width)>2 || Math.Abs(actionSurface.Height*dpi-bounds.Height)>2)
                        throw new InvalidOperationException("WPF/native tracker size diverged after layout.");
                }
                if(buttons.Select(b=>b.ActualHeight).Distinct().Count()!=1 || buttons.Any(b=>b.Content is string t && char.IsUpper(t[0])))
                    throw new InvalidOperationException("Tracker buttons must have equal heights and lowercase labels.");
                // Real OS mouse delivery, not RaiseEvent: buttons consume clicks without activation.
                var originalStripHandle=new WindowInteropHelper(strip).Handle;
                var clicks=Read(reportPath)!.Clicks;
                Click(buttons[1].PointToScreen(new Point(buttons[1].ActualWidth/2,buttons[1].ActualHeight/2)));
                await Until(()=>row.Progress.CurrentStep==initial+1,"Next click");
                if(QuestWindowNative.GetForegroundWindow()!=game || Read(reportPath)!.Clicks!=clicks)
                    throw new InvalidOperationException("Quest button stole game focus or leaked its click.");
                await Pause();host.RefreshNow();strip=host.ActionWindows.First(w=>w.IsVisible && ((StackPanel)w.Content).Children.OfType<Button>().Any(b=>Equals(b.Content,"next")));
                if(new WindowInteropHelper(strip).Handle!=originalStripHandle)throw new InvalidOperationException("Tracker action HWND was recreated after Next.");
                Click(strip.PointToScreen(new Point(0,0))); // fully transparent corner, a different process underneath
                await Until(()=>Read(reportPath)!.Clicks>clicks,"transparent button margin click-through");
                clicks=Read(reportPath)!.Clicks;
                Click(host.TextWindow.PointToScreen(new Point(x+15,y+40)));
                await Until(()=>Read(reportPath)!.Clicks>clicks,"text/world click-through");
                await Pause();host.RefreshNow();
                var doneStep=row.Progress.CurrentStep;
                var doneStrip=host.ActionWindows.First(w=>w.IsVisible && ((StackPanel)w.Content).Children.OfType<Button>().Any(b=>Equals(b.Content,"done")));
                var done=((StackPanel)doneStrip.Content).Children.OfType<Button>().Single(b=>Equals(b.Content,"done"));
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
                var moreStrip=host.ActionWindows.First(w=>w.IsVisible && ((StackPanel)w.Content).Children.OfType<Button>().Any(b=>Equals(b.Content,"more")));
                var more=((StackPanel)moreStrip.Content).Children.OfType<Button>().Single(b=>Equals(b.Content,"more"));
                Click(more.PointToScreen(new Point(more.ActualWidth/2,more.ActualHeight/2)));
                await Until(()=>host.BrowserShowing && host.Browser.Page.CurrentStepInView,"More opens current instruction from Facts");
                var oldBrowserWidth=workspace.Settings.QuestOverlay.BrowserWidth;var oldBrowserX=workspace.Settings.QuestOverlay.BrowserX;
                workspace.Settings.QuestOverlay.BrowserWidth=760;workspace.Settings.QuestOverlay.BrowserX=100;
                var transformedCanvas=(Canvas)host.TextWindow.Content;
                transformedCanvas.RenderTransform=new System.Windows.Media.ScaleTransform(1.25,1.25);
                host.RefreshNow();await Pause();host.RefreshNow();
                var browserBounds=QuestWindowNative.Bounds(new WindowInteropHelper(host.Browser).Handle);
                var inside=new Point(browserBounds.Left+4,browserBounds.Top+80);var outside=new Point(browserBounds.Left-12,browserBounds.Top+80);
                if((transformedCanvas.Clip?.FillContains(transformedCanvas.PointFromScreen(inside))??true)!=true ||
                    (transformedCanvas.Clip?.FillContains(transformedCanvas.PointFromScreen(outside))??true)!=true)
                    throw new InvalidOperationException("Quest browser cut pixels out of the tracker.");
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
                var selector=host.Browser.Page.ReadyQuestChoice;
                Click(selector.PointToScreen(new Point(15,selector.ActualHeight/2)));
                await Until(()=>selector.IsDropDownOpen,"quest selector opens from its content area");
                selector.IsDropDownOpen=false;await Pause();
                var pointButton=host.Browser.Page.CurrentNavigationButton ?? throw new InvalidOperationException("Current navigation button missing.");
                var coordinateCaption=pointButton.Content;
                Click(pointButton.PointToScreen(new Point(pointButton.ActualWidth/2,pointButton.ActualHeight/2)));
                await Until(()=>host.Browser.Page.CopyToastVisible,"detail coordinate copy toast");
                SaveDesktop(host.Browser,"detail-copied-native");
                if(!Equals(pointButton.Content,coordinateCaption))throw new InvalidOperationException("Semantic coordinate caption changed after copy.");
                if(row.Guide is not null){host.Browser.Page.ShowFacts(row.Guide);await Pause();SaveDesktop(host.Browser,"facts-native");}
                var oldApplicationTheme=workspace.Settings.ApplicationTheme;var oldGameTheme=workspace.Settings.GameOverlayTheme;
                try
                {
                    workspace.Settings.ApplicationTheme="Dark";workspace.Settings.GameOverlayTheme="Dark";
                    host.Browser.Page.ShowGuide(row.Guide);await Pause();
                    await Until(()=>!host.Browser.Page.CopyToastVisible,"previous coordinate toast expiry before dark input");
                    var darkPoint=host.Browser.Page.CurrentNavigationButton??throw new InvalidOperationException("Dark coordinate button missing.");
                    Click(darkPoint.PointToScreen(new Point(darkPoint.ActualWidth/2,darkPoint.ActualHeight/2)));
                    await Until(()=>host.Browser.Page.CopyToastVisible,"dark theme coordinate copy");
                    SaveDesktop(host.Browser,"browser-dark-native");
                    workspace.Settings.GameOverlayTheme="Light";await Pause();
                    if(host.Browser.Resources["AppearancePaletteName"] as string!="Light" || settingsWindow.Resources["AppearancePaletteName"] as string!="Dark")
                        throw new InvalidOperationException("Mixed application/game themes leaked across windows.");
                    SaveDesktop(host.Browser,"browser-light-app-dark-native");
                }
                finally{workspace.Settings.ApplicationTheme=oldApplicationTheme;workspace.Settings.GameOverlayTheme=oldGameTheme;}
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
                if(host.ActionWindows.Count(w=>((StackPanel)w.Content).Children.OfType<Button>().Any(b=>Equals(b.Content,"more")))!=1)
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
                var databaseTheme=workspace.Settings.GameOverlayTheme;
                try
                {
                    workspace.Settings.GameOverlayTheme="Dark";await Pause();
                    if(database.Resources["AppearancePaletteName"] as string!="Dark")throw new InvalidOperationException("Database overlay did not apply its theme live.");
                    SaveDesktop(database,"database-dark-native");
                }
                finally{workspace.Settings.GameOverlayTheme=databaseTheme;}
                ((App)Application.Current).CloseDatabaseOverlay();await Pause();
                var previousServer = workspace.Settings.ServerProfile;
                var previousAddKey = workspace.Settings.AddMvpTimerHotkey;
                var previousGameTheme = workspace.Settings.GameOverlayTheme;
                try
                {
                    workspace.Settings.ServerProfile = GameServerProfile.SpringRo;
                    QuestWindowNative.SetForegroundWindow(game); await Pause();
                    workspace.Settings.GameOverlayTheme="Dark";
                    var capture=new RoAura.Controls.HotkeyCaptureBox {Margin=new Thickness(12)};
                    capture.SetBinding(RoAura.Controls.HotkeyCaptureBox.ValueProperty,new System.Windows.Data.Binding(nameof(AppSettings.AddMvpTimerHotkey)) {Source=workspace.Settings,Mode=System.Windows.Data.BindingMode.TwoWay});
                    var captureWindow=new Window {Width=350,Height=130,Content=capture,Topmost=true,WindowStartupLocation=WindowStartupLocation.CenterScreen};
                    captureWindow.Show();captureWindow.Activate();await Pause();
                    Click(capture.PointToScreen(new Point(capture.ActualWidth/2,capture.ActualHeight/2)));
                    await Press("Ctrl+Shift+F4");
                    if(HotkeyChord.Canonical(capture.Value)!="CTRL+SHIFT+F4")throw new InvalidOperationException($"Keyboard chord capture failed: value={capture.Value}; gate={HotkeyCaptureGate.IsCapturing}.");
                    if(HotkeyChord.Canonical(workspace.Settings.AddMvpTimerHotkey)!="CTRL+SHIFT+F4" ||
                        capture.GetBindingExpression(RoAura.Controls.HotkeyCaptureBox.ValueProperty) is null)
                        throw new InvalidOperationException("Hotkey capture detached its settings binding.");
                    var clear=AllButtons(capture).Single(button=>Equals(button.Content,"×"));
                    Click(clear.PointToScreen(new Point(clear.ActualWidth/2,clear.ActualHeight/2)));
                    await Pause();SaveDesktop(captureWindow,"hotkey-unassigned-native");
                    if(workspace.Settings.AddMvpTimerHotkey!="None" || hook.IsHotkeyEnabled("Ctrl+Shift+F4"))
                        throw new InvalidOperationException("Visible unassign action did not disable the shortcut.");
                    Click(capture.PointToScreen(new Point(capture.ActualWidth/2,capture.ActualHeight/2)));
                    await Press("Ctrl+Shift+F4");
                    if(HotkeyChord.Canonical(workspace.Settings.AddMvpTimerHotkey)!="CTRL+SHIFT+F4")
                        throw new InvalidOperationException("Reassignment after clearing failed.");
                    await Pause();
                    if(Application.Current.Windows.OfType<MvpTimerWizardWindow>().Any(w=>w.IsVisible))throw new InvalidOperationException("Assigning a chord invoked it.");
                    captureWindow.Close();QuestWindowNative.SetForegroundWindow(game);await Pause();
                    await Press("F4");
                    if(Application.Current.Windows.OfType<MvpTimerWizardWindow>().Any(w=>w.IsVisible))throw new InvalidOperationException("Chord fired without modifiers.");
                    var beforeTimers = workspace.Settings.ActiveMvpTimers.Count;
                    await Press(workspace.Settings.AddMvpTimerHotkey);
                    await Until(()=>Application.Current.Windows.OfType<MvpTimerWizardWindow>().Any(w=>w.IsVisible), "in-game MVP hotkey opens wizard");
                    var mvpWizard=Application.Current.Windows.OfType<MvpTimerWizardWindow>().Single(w=>w.IsVisible);
                    if(!AppearanceService.IsGameWindow(mvpWizard))throw new InvalidOperationException("MVP wizard lost its game theme scope.");
                    await Until(()=>mvpWizard.IsLoaded && mvpWizard.MonsterBox.IsLoaded && mvpWizard.MonsterBox.ActualWidth>100,"MVP form layout ready");
                    await Pause(250);
                    mvpWizard.MonsterBox.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,new System.Windows.Input.MouseButtonEventHandler((_,e)=>
                        DebugLog.Write($"MVP SEARCH CLICK: source={e.OriginalSource.GetType().Name}; handled={e.Handled}; size={mvpWizard.MonsterBox.ActualWidth}x{mvpWizard.MonsterBox.ActualHeight}; open={mvpWizard.MonsterBox.IsDropDownOpen}")),true);
                    SaveDesktop(mvpWizard,"mvp-empty-native");
                    Click(mvpWizard.MonsterBox.PointToScreen(new Point(18,mvpWizard.MonsterBox.ActualHeight/2)));
                    await Until(()=>mvpWizard.MonsterBox.IsDropDownOpen,"MVP search opens from content");
                    var editor=(TextBox)mvpWizard.MonsterBox.Template.FindName("PART_EditableTextBox",mvpWizard.MonsterBox);
                    await Press("D");await Press("R");
                    if(editor.Text.ToLowerInvariant()!="dr" || editor.SelectionLength!=0)
                        throw new InvalidOperationException("MVP typing lost characters or selected input.");
                    mvpWizard.MonsterBox.SelectedIndex=0;await Pause();mvpWizard.MonsterBox.IsDropDownOpen=false;await Pause();
                    if(mvpWizard.Draft.MonsterId<=0)throw new InvalidOperationException("In-game MVP selection failed.");
                    await Pause(); host.RefreshNow();
                    var mvpBounds=QuestWindowNative.Bounds(new WindowInteropHelper(mvpWizard).Handle);
                    var mvpCenter=new Point(mvpBounds.X+mvpBounds.Width/2,mvpBounds.Y+mvpBounds.Height/2);
                    if(((Canvas)host.TextWindow.Content).Clip?.FillContains(host.TextWindow.PointFromScreen(mvpCenter))!=false)
                        throw new InvalidOperationException("Quest tracker paints above the MVP timer form.");
                    if(mvpWizard.Draft.RespawnHints.FirstOrDefault() is { } spawn && mvpWizard.Draft.LocationCode!=spawn.Code)
                        throw new InvalidOperationException("The first MVP map was not selected automatically.");
                    var mapEditor=Descendants<RoAura.Controls.MvpMapEditor>(mvpWizard).Single();
                    await Until(()=>mapEditor.IsMapReady && mapEditor.DisplayedMapCode==mvpWizard.Draft.LocationCode,"first MVP map paints without a location click");
                    var formBefore=QuestWindowNative.Bounds(new WindowInteropHelper(mvpWizard).Handle);
                    var grip=mvpWizard.DragHandle.PointToScreen(new Point(mvpWizard.DragHandle.ActualWidth/2,8));
                    SetCursorPos((int)grip.X,(int)grip.Y);Send(new Input {Type=0,Data=new Union {Mouse=new Mouse {Flags=2}}});await Pause(80);
                    SetCursorPos((int)grip.X-45,(int)grip.Y-15);await Pause(160);
                    Send(new Input {Type=0,Data=new Union {Mouse=new Mouse {Flags=4}}});await Pause(400);
                    var formAfter=QuestWindowNative.Bounds(new WindowInteropHelper(mvpWizard).Handle);
                    if(Math.Abs(formAfter.X-formBefore.X)<10 || workspace.Settings.MvpAddWindowX is null)
                        throw new InvalidOperationException("MVP form drag was recentered or not persisted.");
                    await Pause();SaveDesktop(mvpWizard,"mvp-add-native");
                    var create=AllButtons(mvpWizard).Single(button=>Equals(button.Content,LocalizationService.Get("CreateMvpTimer")));
                    Click(create.PointToScreen(new Point(create.ActualWidth/2,create.ActualHeight/2)));
                    await Until(()=>workspace.Settings.ActiveMvpTimers.Count==beforeTimers+1 && QuestWindowNative.GetForegroundWindow()==game, "MVP creation and exact-game focus restore");
                    var restarted=workspace.Settings.ActiveMvpTimers.Last();
                    restarted.StartFromKillTime(DateTime.Now.AddMinutes(-25));
                    restarted.KillTimeInput="invalid old draft";
                    var restartAt=DateTime.UtcNow;
                    var restartHandler=typeof(MainWindow).GetMethod("RestartMvpTimer_Click",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
                    restartHandler.Invoke(settingsWindow,[new Button {DataContext=restarted},new RoutedEventArgs()]);
                    if(restarted.KilledAtUtc<restartAt || restarted.RemainingSeconds<restarted.DurationMinutes*60-2)
                        throw new InvalidOperationException("Restart reused an old/invalid kill-clock draft.");
                    DebugLog.Write("MVP RESTART PASS: existing kill-clock draft replaced with now.");
                    workspace.Settings.ActiveMvpTimers.RemoveAt(workspace.Settings.ActiveMvpTimers.Count-1);
                    await Press(workspace.Settings.AddMvpTimerHotkey);
                    await Until(()=>Application.Current.Windows.OfType<MvpTimerWizardWindow>().Any(w=>w.IsVisible), "MVP wizard reopens");
                    mvpWizard=Application.Current.Windows.OfType<MvpTimerWizardWindow>().Single(w=>w.IsVisible);
                    await Press(workspace.Settings.AddMvpTimerHotkey);
                    await Until(()=>!Application.Current.Windows.OfType<MvpTimerWizardWindow>().Any(w=>w.IsVisible),"repeated MVP hotkey closes wizard");
                    await Press(workspace.Settings.AddMvpTimerHotkey);
                    await Until(()=>Application.Current.Windows.OfType<MvpTimerWizardWindow>().Any(w=>w.IsVisible),"MVP wizard opens after hotkey close");
                    mvpWizard=Application.Current.Windows.OfType<MvpTimerWizardWindow>().Single(w=>w.IsVisible);
                    var cancel=AllButtons(mvpWizard).Single(button=>Equals(button.Content,LocalizationService.Get("Cancel")));
                    Click(cancel.PointToScreen(new Point(cancel.ActualWidth/2,cancel.ActualHeight/2)));
                    await Until(()=>!Application.Current.Windows.OfType<MvpTimerWizardWindow>().Any(w=>w.IsVisible) && QuestWindowNative.GetForegroundWindow()==game,"MVP cancel and exact-game focus restore");
                    if(workspace.Settings.ActiveMvpTimers.Count!=beforeTimers)throw new InvalidOperationException("Cancel created an MVP timer.");
                    var oldTimerX=workspace.Settings.TimersX;var oldTimerY=workspace.Settings.TimersY;
                    try
                    {
                        // Off-screen model coordinates must get a visible edit preview without being rewritten.
                        workspace.Settings.TimersX=5000;workspace.Settings.TimersY=5000;
                        ((System.Windows.Controls.Primitives.ToggleButton)settingsWindow.FindName("TimerMoveModeButton")).IsChecked=true;
                        hook.SetEscapeEnabled(true);await Pause(350);
                        var panel=(FrameworkElement)ordinary.FindName("MvpTimerPanel");
                        if(workspace.Settings.TimersX!=5000||workspace.Settings.TimersY!=5000)throw new InvalidOperationException("Preview rewrote timer coordinates.");
                        var timerGrab=panel.PointToScreen(new Point(panel.ActualWidth/2,panel.ActualHeight/2));
                        SetCursorPos((int)timerGrab.X,(int)timerGrab.Y);Send(new Input {Type=0,Data=new Union {Mouse=new Mouse {Flags=2}}});await Pause(80);
                        SetCursorPos((int)timerGrab.X-80,(int)timerGrab.Y-40);await Pause(150);
                        Send(new Input {Type=0,Data=new Union {Mouse=new Mouse {Flags=4}}});await Pause();
                        if(workspace.Settings.TimersX>=5000||workspace.Settings.TimersY>=5000)throw new InvalidOperationException("MVP timer panel did not move.");
                        await Press(workspace.Settings.FinishMoveHotkey);
                        await Until(()=>!ordinary.IsBusyEditing,"MVP timer placement finishes");
                        settingsWindow.Hide();QuestWindowNative.SetForegroundWindow(game);await Pause();
                    }
                    finally {ordinary.EndAllEditModes();hook.SetEscapeEnabled(false);workspace.Settings.TimersX=oldTimerX;workspace.Settings.TimersY=oldTimerY;}
                }
                finally { ((MainWindow)settingsWindow).CloseMvpWizard();workspace.Settings.ServerProfile=previousServer;workspace.Settings.AddMvpTimerHotkey=previousAddKey;workspace.Settings.GameOverlayTheme=previousGameTheme; }
                var previousAppTheme=workspace.Settings.ApplicationTheme;
                try
                {
                    workspace.Settings.ApplicationTheme="Dark";
                    var reminder=new ReminderWizardWindow(_=>{});
                    reminder.Show();reminder.Activate();await Pause();
                    Click(reminder.ManualIdCard.PointToScreen(new Point(reminder.ManualIdCard.ActualWidth/2,reminder.ManualIdCard.ActualHeight/2)));
                    await Pause();
                    if(!ReferenceEquals(reminder.ManualIdCard.Background,reminder.FindResource("RoSelectionBrush")))
                        throw new InvalidOperationException("Reminder cards lost their dark theme.");
                    SaveDesktop(reminder,"reminder-dark-native");
                    reminder.Close();
                }
                finally{workspace.Settings.ApplicationTheme=previousAppTheme;QuestWindowNative.SetForegroundWindow(game);}
                SetWindowPos(game,IntPtr.Zero,20,20,340,240,0x14);await Pause();host.RefreshNow();
                if(workspace.Settings.QuestOverlay.X!=x || workspace.Settings.QuestOverlay.Y!=y)
                    throw new InvalidOperationException("Game resize rewrote tracker coordinates.");
                // Screen capture must reach the game even with RoAura's keyboard suppression enabled.
                var screenshots = Read(reportPath)!.Screenshots;
                hook.SetKeyboardBlocked(true);
                Key(0x2C,true); Key(0x2C,false);
                await Until(()=>Read(reportPath)!.Screenshots > screenshots, "Print Screen bypasses blocked keyboard");
                hook.SetKeyboardBlocked(false);
                ShowWindow(game,6);await Pause();host.RefreshNow();
                if(host.TextWindow.IsVisible || host.BrowserShowing)throw new InvalidOperationException("Quest surfaces remained above a minimized game.");
                await CaptureMvpLocationLayout((MainWindow)settingsWindow,workspace.Settings);
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
    private static async Task CaptureMvpLocationLayout(MainWindow main,AppSettings settings)
    {
        var monster=MonsterCatalogService.Load(GameDatabaseVersion.SpringRo).First(monster=>monster.Id==1039);
        var timer=new MvpTimer {MonsterId=monster.Id,Name=monster.Name};
        timer.SetMonsterDetails(monster);
        var oldWidth=main.Width;var oldHeight=main.Height;var oldLeft=main.Left;var oldTop=main.Top;
        var oldTheme=settings.ApplicationTheme;var oldTab=main.MainTabs.SelectedItem;var oldTimer=main.MvpTimersGrid.SelectedItem;
        try
        {
            settings.ActiveMvpTimers.Add(timer);main.MvpNavButton.IsChecked=true;main.MainTabs.SelectedItem=main.MvpTab;main.MvpTimersGrid.SelectedItem=timer;
            main.WindowState=WindowState.Normal;main.Left=20;main.Top=20;main.Show();main.Activate();
            foreach(var size in new[]{(Width:1420d,Height:920d,Theme:"Light",Name:"mvp-locations-wide"),
                                      (Width:main.MinWidth,Height:main.MinHeight,Theme:"Dark",Name:"mvp-locations-minimum")})
            {
                settings.ApplicationTheme=size.Theme;main.Width=size.Width;main.Height=size.Height;
                await Pause();main.UpdateLayout();
                // Keep the timer detail pane on the CI desktop even when its width is below the application's minimum.
                main.Left=SystemParameters.WorkArea.Right-main.ActualWidth;
                main.Top=SystemParameters.WorkArea.Bottom-main.ActualHeight;
                main.MvpLocationPanel.BringIntoView();await Pause();
                await Until(()=>main.MvpLocationCards.IsVisible && main.MvpLocationCards.ActualWidth>0,"application MVP location panel visible");
                SaveElement(main.MvpLocationPanel,size.Name+"-before");
                var cards=main.MvpLocationCards;var map=main.MvpLocationMap;
                await Until(()=>map.IsMapReady,"application MVP map ready");
                if(cards.Items.Count>1)
                {
                    var card=(ListBoxItem)cards.ItemContainerGenerator.ContainerFromIndex(1);
                    var cardPoint=card.PointToScreen(new Point(card.ActualWidth/2,card.ActualHeight/2));
                    DebugLog.Write($"MVP LOCATION CLICK: desktop={SystemParameters.WorkArea}; point={cardPoint}; tab={main.MainTabs.SelectedIndex}");
                    Click(cardPoint);
                    await Until(()=>map.IsMapReady && map.DisplayedMapCode==timer.RespawnHints[1].Code,"application MVP location switch");
                }
                var leftEdge=cards.PointToScreen(new Point(cards.ActualWidth,0));
                var mapOrigin=map.PointToScreen(new Point());
                var mapEnd=map.PointToScreen(new Point(map.ActualWidth,map.ActualHeight));
                var panelEnd=main.MvpLocationPanel.PointToScreen(new Point(main.MvpLocationPanel.ActualWidth,main.MvpLocationPanel.ActualHeight));
                if(leftEdge.X>=mapOrigin.X || mapEnd.X>panelEnd.X+1)
                    throw new InvalidOperationException("MVP location cards overlap or clip the map.");
                timer.ClearMarker();
                Click(map.PointToScreen(new Point(map.ActualWidth/2,map.ActualHeight/2)));
                await Until(()=>timer.MarkerX.HasValue && timer.MarkerY.HasValue,"application MVP map marker click");
                await Pause();SaveElement(main.MvpLocationPanel,size.Name);
            }
        }
        finally
        {
            settings.ActiveMvpTimers.Remove(timer);main.MvpTimersGrid.SelectedItem=oldTimer;main.MainTabs.SelectedItem=oldTab;
            main.Width=oldWidth;main.Height=oldHeight;main.Left=oldLeft;main.Top=oldTop;settings.ApplicationTheme=oldTheme;main.Hide();
        }
    }
    private static void SaveElement(FrameworkElement element,string name)
    {
        var folder=Environment.GetEnvironmentVariable("ROAURA_QUEST_UI_SNAPSHOTS");
        if(string.IsNullOrWhiteSpace(folder))return;
        var width=Math.Max(1,(int)Math.Ceiling(element.ActualWidth));
        var height=Math.Max(1,(int)Math.Ceiling(element.ActualHeight));
        var drawing=new System.Windows.Media.DrawingVisual();
        using(var context=drawing.RenderOpen())
            context.DrawRectangle(new System.Windows.Media.VisualBrush(element),null,new Rect(0,0,width,height));
        var image=new System.Windows.Media.Imaging.RenderTargetBitmap(width,height,96,96,System.Windows.Media.PixelFormats.Pbgra32);
        image.Render(drawing);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        Directory.CreateDirectory(folder);using var file=File.Create(Path.Combine(folder,name+".png"));encoder.Save(file);
        DebugLog.Write("MVP LOCATION CAPTURE: "+name);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=System.Windows.Media.VisualTreeHelper.GetChild(root,i);
            if(child is T match)yield return match;
            foreach(var nested in Descendants<T>(child))yield return nested;
        }
    }
    private static IEnumerable<Button> AllButtons(DependencyObject root)
    {
        for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=System.Windows.Media.VisualTreeHelper.GetChild(root,i);
            if(child is Button button)yield return button;
            foreach(var nested in AllButtons(child))yield return nested;
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
    {
        if(!HotkeyChord.TryParse(name,out var chord))throw new InvalidOperationException("Invalid smoke hotkey: "+name);
        var modifiers=new List<ushort>();
        if(chord.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))modifiers.Add(0x11);
        if(chord.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt))modifiers.Add(0x12);
        if(chord.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift))modifiers.Add(0x10);
        if(chord.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Windows))modifiers.Add(0x5B);
        foreach(var modifier in modifiers){Key(modifier,true);await Pause(35);}
        Key((ushort)chord.VirtualKey,true);await Pause(100);Key((ushort)chord.VirtualKey,false);
        foreach(var modifier in modifiers.AsEnumerable().Reverse())Key(modifier,false);
        await Pause();
    }
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
