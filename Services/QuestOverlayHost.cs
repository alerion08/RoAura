using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using RoAura.Models;

namespace RoAura.Services;

/// <summary>Two independent game surfaces; ordinary OverlayWindow is never made interactive.
/// World/text HWND is entirely click-through. Only small painted action HWNDs accept a mouse.</summary>
public sealed class QuestOverlayHost : IDisposable
{
    private readonly QuestWorkspace _workspace;
    private readonly Window _settings;
    private readonly Func<bool> _otherEditing;
    private readonly Action _saveVisuals;
    private readonly Action<bool> _dragMode;
    private readonly QuestPassiveWindow _text=new(true);
    private readonly Canvas _canvas=new() { ClipToBounds=true,Background=null };
    private readonly StackPanel _cards=new();
    private readonly DispatcherTimer _timer=new() {Interval=TimeSpan.FromMilliseconds(120)};
    private readonly List<(QuestPassiveWindow Window,double Y)> _actions=[];
    private QuestPassiveWindow? _choices;
    private QuestProgressEntry? _choiceEntry;
    private int _choiceStep;
    private bool _browserRequested,_trackerRequested=true,_global=true,_dirty=true,_disposed;
    private IntPtr _game;
    private Rect _gamePixels;
    private double _dpi=1;
    private UIElement? _dragElement;
    private Point _dragCursor,_dragOrigin;
    private string _toast="";
    private DateTime _toastUntil;
    public QuestBrowserWindow Browser {get;}
    public bool BrowserShowing=>Browser.IsVisible;
    public bool BrowserRequested=>_browserRequested;
    public bool TrackerRequested=>_trackerRequested;
    public bool GlobalVisible=>_global;
    public IntPtr GameHandle=>_game;
    public event Action? KeyboardModeChanged;
    public event Action<IntPtr,IntPtr>? TargetChanged;
    private QuestOverlaySettings Options=>_workspace.Settings.QuestOverlay;
    public QuestOverlayHost(QuestWorkspace workspace,Window settings,Func<CharacterSessionSnapshot> observed,
        Func<bool> otherEditing,Action saveVisuals,Action<bool> dragMode)
    {
        _workspace=workspace;_settings=settings;_otherEditing=otherEditing;_saveVisuals=saveVisuals;_dragMode=dragMode;
        Browser=new(workspace,observed);
        Browser.HideRequested+=()=>HideBrowser(true);
        Browser.UserGeometryChanged+=SaveBrowserGeometry;
        Browser.IsVisibleChanged+=(_,_)=>KeyboardModeChanged?.Invoke();
        _text.Content=_canvas;_canvas.Children.Add(_cards);
        _workspace.Changed+=Changed;Options.PropertyChanged+=VisualChanged;
        _timer.Tick+=(_,_)=>Tick();_timer.Start();
    }
    public void ToggleTracker() { _trackerRequested=!_trackerRequested;EndDrag();Tick(); }
    public async void ToggleBrowser()
    {
        if(_browserRequested) {HideBrowser(true);return;}
        await OpenBrowserAsync();
    }
    public async Task<bool> OpenBrowserAsync(QuestGuide? guide=null)
    {
        if(!ResolveGame())return false;
        (Application.Current as App)?.CloseDatabaseOverlay();
        _browserRequested=true;ActivateGame();Tick();
        if(Browser.IsVisible) {Browser.Activate();KeyboardModeChanged?.Invoke();}
        await Browser.LoadGuideAsync(guide);
        return true;
    }
    public void HideBrowser(bool restoreFocus)
    {
        var wasActive=Browser.IsActive || Browser.OwnedWindows.Cast<Window>().Any(w=>w.IsActive);
        _browserRequested=false;Browser.Hide();KeyboardModeChanged?.Invoke();
        if(restoreFocus && wasActive) ActivateGame();
    }
    public void SetGlobalVisible(bool visible)
    {
        var wasActive=Browser.IsActive;_global=visible;EndDrag();CloseChoices();Tick();
        if(!visible && wasActive)ActivateGame();
    }
    public bool PrepareMove()
    {
        if(!ResolveGame())return false;
        _trackerRequested=true;_global=true;ActivateGame();Tick();return true;
    }
    public void EndDrag()
    {
        if(_dragElement is null)return;
        _dragElement.ReleaseMouseCapture();_dragElement=null;_dragMode(false);_saveVisuals();
    }
    private bool ResolveGame()
    {
        if(!ForegroundProcess.TryFindWindow(_workspace.Settings.AllowedProcesses,out var handle,out var bounds))return false;
        if(_game!=handle)
        {
            var previous=_game;_game=handle;TargetChanged?.Invoke(previous,handle);_dirty=true;CloseChoices();
        }
        _gamePixels=new Rect(bounds.Left,bounds.Top,Math.Max(1,bounds.Right-bounds.Left),Math.Max(1,bounds.Bottom-bounds.Top));
        var dpi=QuestWindowNative.Scale(handle);if(dpi!=_dpi){_dpi=dpi;_dirty=true;}
        return true;
    }
    private void ActivateGame()
    {if(_game!=IntPtr.Zero && ForegroundProcess.IsWindowAllowed(_game,_workspace.Settings.AllowedProcesses))QuestWindowNative.SetForegroundWindow(_game);}
    private void Changed()
    {
        _dirty=true;
        if(_choices is not null && !_workspace.Active.Any(r=>ReferenceEquals(r.Progress,_choiceEntry) && r.Progress.CurrentStep==_choiceStep))CloseChoices();
    }
    private void VisualChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName is not (nameof(QuestOverlaySettings.X) or nameof(QuestOverlaySettings.Y) or nameof(QuestOverlaySettings.BrowserX) or nameof(QuestOverlaySettings.BrowserY) or nameof(QuestOverlaySettings.BrowserWidth) or nameof(QuestOverlaySettings.BrowserHeight)))
            _dirty=true;
    }
    private void Tick()
    {
        if(_disposed)return;
        if(_toast.Length>0 && DateTime.UtcNow>=_toastUntil){_toast="";_dirty=true;}
        if(!_global || !ResolveGame()) {HideSurfaces();return;}
        var foreground=QuestWindowNative.GetForegroundWindow();
        var gameActive=foreground==_game;
        var browserActive=Browser.IsActive || Browser.OwnedWindows.Cast<Window>().Any(w=>w.IsActive) ||
            QuestWindowNative.IsOwnedBy(foreground,new WindowInteropHelper(Browser).Handle);
        var controls=Application.Current.Windows.OfType<Window>().Where(w=>w is not QuestPassiveWindow && w is not OverlayWindow &&
            w.IsVisible && w.WindowState!=WindowState.Minimized).ToArray();
        var controlsActive=controls.Any(w=>new WindowInteropHelper(w).Handle==foreground);
        var occludedByOther=!gameActive && !controlsActive && ForegroundProcess.DoesForegroundWindowCover(
            new((int)_gamePixels.Left,(int)_gamePixels.Top,(int)_gamePixels.Right,(int)_gamePixels.Bottom));
        if(occludedByOther) {HideSurfaces();return;}
        var promote=gameActive || controlsActive;
        if(_browserRequested && (gameActive || browserActive) && _gamePixels.Width/_dpi>=760 && _gamePixels.Height/_dpi>=480)
        {
            PlaceBrowser(promote);if(!Browser.IsVisible)Browser.Show();
        }
        else if(Browser.IsVisible)Browser.Hide();
        if(_otherEditing() || !_trackerRequested || !Options.Enabled || _workspace.Active.All(q=>!q.Progress.IsVisibleInOverlay))
        {HideTracker();return;}
        if(_dirty)Rebuild();
        _canvas.Width=_gamePixels.Width/_dpi;_canvas.Height=_gamePixels.Height/_dpi;
        Canvas.SetLeft(_cards,Options.X);Canvas.SetTop(_cards,Options.Y);
        var excluded=(controlsActive?controls.Select(w=>QuestWindowNative.Bounds(new WindowInteropHelper(w).Handle)):Enumerable.Empty<Rect>())
            .Concat(Browser.IsVisible?[QuestWindowNative.Bounds(new WindowInteropHelper(Browser).Handle)]:[])
            .Where(r=>!r.IsEmpty).ToArray();
        _canvas.Clip=OverlayOcclusion.CreateClip(new Size(_canvas.Width,_canvas.Height),excluded.Select(r=>
            new Rect((r.X-_gamePixels.X)/_dpi,(r.Y-_gamePixels.Y)/_dpi,r.Width/_dpi,r.Height/_dpi)));
        if(!_text.IsVisible)_text.Show();QuestWindowNative.Place(_text,_gamePixels,promote);
        var scale=Options.Scale*_dpi;
        foreach(var (window,y) in _actions)
        {
            var rect=new Rect(_gamePixels.X+Options.X*_dpi,_gamePixels.Y+(Options.Y+y*Options.Scale)*_dpi,Options.Width*scale,24*scale);
            var inside=_gamePixels.Contains(rect);
            var masked=excluded.Any(r=>r.IntersectsWith(rect));
            if(!inside || masked || !Options.ShowButtons) {window.Hide();continue;}
            if(!window.IsVisible)window.Show();QuestWindowNative.Place(window,rect,promote);
        }
        if(_choices is not null)
        {
            if(!gameActive && !browserActive)CloseChoices();
            else QuestWindowNative.Place(_choices,QuestWindowNative.Bounds(new WindowInteropHelper(_choices).Handle),promote);
        }
    }
    private void PlaceBrowser(bool promote)
    {
        if(Browser.IsUserMoving)return;
        var width=Math.Min(Options.BrowserWidth*_dpi,_gamePixels.Width);
        var height=Math.Min(Options.BrowserHeight*_dpi,_gamePixels.Height);
        var x=Math.Clamp(Options.BrowserX*_dpi,0,Math.Max(0,_gamePixels.Width-width));
        var y=Math.Clamp(Options.BrowserY*_dpi,0,Math.Max(0,_gamePixels.Height-height));
        QuestWindowNative.Place(Browser,new(_gamePixels.X+x,_gamePixels.Y+y,width,height),promote);
    }
    private void SaveBrowserGeometry()
    {
        if(_game==IntPtr.Zero || !Browser.IsVisible)return;
        var rect=QuestWindowNative.Bounds(new WindowInteropHelper(Browser).Handle);
        if(rect.IsEmpty)return;
        Options.BrowserX=(rect.X-_gamePixels.X)/_dpi;Options.BrowserY=(rect.Y-_gamePixels.Y)/_dpi;
        Options.BrowserWidth=rect.Width/_dpi;Options.BrowserHeight=rect.Height/_dpi;_saveVisuals();
    }
    private static string T(string key)=>LocalizationService.Get(key);
    private void Rebuild()
    {
        // Do not destroy a captured drag HWND while the mouse is held.
        if(_dragElement is not null)return;
        foreach(var a in _actions)a.Window.Close();_actions.Clear();_cards.Children.Clear();
        _cards.Width=Options.Width;_cards.LayoutTransform=new ScaleTransform(Options.Scale,Options.Scale);
        _cards.Opacity=Options.OpacityPercent/100;
        Brush color;try {color=(Brush)new BrushConverter().ConvertFromString(Options.TextColor)!;}catch{color=Brushes.White;}
        var y=0d;var count=0;
        var visibleRows=_workspace.Active.Where(q=>q.Progress.IsVisibleInOverlay).ToList();
        foreach(var row in visibleRows)
        {
            // Only rows that fit the client are exposed. The final More action opens the full list.
            if(count>0 && Options.Y+(y+90)*Options.Scale>_gamePixels.Height/_dpi)break;
            var panel=new StackPanel();
            var title=Label(row.Title+" "+row.Position,color,true);title.MaxHeight=(Options.FontSize+3)*2;title.TextTrimming=TextTrimming.CharacterEllipsis;panel.Children.Add(title);
            var instruction=Label(row.Progress.IsMissingFromCatalog?T("QMissing"):row.Progress.NeedsStepReview?T("QReviewStep"):row.Instruction,color);
            instruction.MaxHeight=(Options.FontSize+3)*3;instruction.TextTrimming=TextTrimming.CharacterEllipsis;panel.Children.Add(instruction);
            if(Options.ShowCoordinates)
            {
                var targets=QuestNavigation.Targets(row);
                if(targets.Count>0){var coordinates=Label(T("QWikiShort")+": "+string.Join(" · ",targets),color);coordinates.MaxHeight=(Options.FontSize+3)*2;coordinates.TextTrimming=TextTrimming.CharacterEllipsis;panel.Children.Add(coordinates);}
            }
            if(DateTime.UtcNow<_toastUntil)panel.Children.Add(Label(_toast,Brushes.Gold));
            panel.Measure(new Size(Options.Width,double.PositiveInfinity));
            var textHeight=panel.DesiredSize.Height;
            if(count>0 && Options.Y+(y+textHeight+(Options.ShowButtons?24:0)+Options.CardGap)*Options.Scale>_gamePixels.Height/_dpi)break;
            _cards.Children.Add(panel);
            if(Options.ShowButtons)
            {
                var strip=new QuestPassiveWindow(false) {Opacity=Options.OpacityPercent/100};var buttons=new StackPanel {Orientation=Orientation.Horizontal,Background=null,LayoutTransform=new ScaleTransform(Options.Scale,Options.Scale)};
                var prev=ActionButton("Prev",()=>_workspace.Move(row,-1));prev.IsEnabled=row.CanPrev;buttons.Children.Add(prev);
                var next=ActionButton("Next",()=>_workspace.Move(row,1));next.IsEnabled=row.CanNext;buttons.Children.Add(next);
                buttons.Children.Add(ActionButton("More",async()=>await OpenBrowserAsync(row.Guide)));
                var targets=QuestNavigation.Targets(row);
                var navi=ActionButton("navi",()=>Navigate(row,strip,targets));navi.IsEnabled=targets.Count>0 && _workspace.Settings.ServerProfile!=GameServerProfile.Other;
                navi.ToolTip=T(targets.Count==0?"QNoNavigation":"QWikiCoordinates");buttons.Children.Add(navi);
                var drag=ActionButton("⋮⋮",()=>{});drag.Width=24;drag.ToolTip=T("QMoveTracker");
                drag.PreviewMouseLeftButtonDown+=(_,e)=>
                {
                    _dragElement=drag;_dragCursor=QuestWindowNative.Cursor();_dragOrigin=new(Options.X,Options.Y);drag.CaptureMouse();_dragMode(true);e.Handled=true;
                };
                drag.PreviewMouseMove+=(_,e)=>
                {if(_dragElement!=drag)return;var delta=QuestWindowNative.Cursor()-_dragCursor;Options.X=_dragOrigin.X+delta.X/_dpi;Options.Y=_dragOrigin.Y+delta.Y/_dpi;Tick();};
                drag.PreviewMouseLeftButtonUp+=(_,e)=>{EndDrag();_dirty=true;e.Handled=true;};
                buttons.Children.Add(drag);strip.Content=buttons;
                _actions.Add((strip,y+textHeight));
                _cards.Children.Add(new Border {Height=24});
            }
            _cards.Children.Add(new Border {Height=Options.CardGap});y+=textHeight+(Options.ShowButtons?24:0)+Options.CardGap;count++;
        }
        if(count<visibleRows.Count)_cards.Children.Add(Label(T("QMoreQuests")+" "+(visibleRows.Count-count)+" · "+_workspace.Settings.QuestBrowserHotkey,color));
        _dirty=false;
    }
    private TextBlock Label(string value,Brush color,bool bold=false)=>new()
    {
        Text=value,TextWrapping=TextWrapping.Wrap,FontSize=Options.FontSize,LineHeight=Options.FontSize+3,Foreground=color,
        FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,Margin=new Thickness(0,1,0,2),
        Effect=new DropShadowEffect {Color=Colors.Black,BlurRadius=2,ShadowDepth=1,Opacity=.9}
    };
    private static Button ActionButton(string caption,Action action)
    {
        var button=new Button {Content=caption,Focusable=false,IsTabStop=false,MinHeight=22,Height=22,FontSize=11,Padding=new Thickness(5,0,5,0),Margin=new Thickness(1,1,2,1)};
        button.Click+=(_,_)=>action();return button;
    }
    private void Navigate(QuestListRow row,QuestPassiveWindow strip,IReadOnlyList<QuestNavigationTarget> targets)
    {
        if(targets.Count==0)return;
        if(targets.Count==1){Copy(targets[0]);return;}
        CloseChoices();_choices=new QuestPassiveWindow(false) {IsTargetChooser=true};_choiceEntry=row.Progress;_choiceStep=row.Progress.CurrentStep;
        var list=new StackPanel();
        foreach(var target in targets)list.Children.Add(ActionButton(target.ToString(),()=>Copy(target)));
        var layout=new Grid {Background=(Brush)Browser.FindResource("RoCardBrush")};
        layout.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition());layout.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        layout.Children.Add(new TextBlock {Text=T("QChooseNavigation"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8)});
        var scroll=new ScrollViewer {Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Focusable=false};Grid.SetRow(scroll,1);layout.Children.Add(scroll);
        var cancel=ActionButton("×",CloseChoices);Grid.SetRow(cancel,2);layout.Children.Add(cancel);
        _choices.Content=layout;var anchor=QuestWindowNative.Bounds(new WindowInteropHelper(strip).Handle);
        var width=Math.Min(340*_dpi,_gamePixels.Width);var height=Math.Min((targets.Count*26+90)*_dpi,_gamePixels.Height);
        var x=Math.Clamp(anchor.Left,_gamePixels.Left,Math.Max(_gamePixels.Left,_gamePixels.Right-width));
        var top=Math.Clamp(anchor.Bottom,_gamePixels.Top,Math.Max(_gamePixels.Top,_gamePixels.Bottom-height));
        _choices.Show();QuestWindowNative.Place(_choices,new(x,top,width,height));
    }
    private void Copy(QuestNavigationTarget target)
    {
        _toast=T(ClipboardService.TryCopy(target.Command)?"QCommandCopied":"QCopyFailed");_toastUntil=DateTime.UtcNow.AddSeconds(4);
        CloseChoices();_dirty=true;
    }
    private void CloseChoices(){_choices?.Close();_choices=null;}
    private void HideTracker()
    {EndDrag();_text.Hide();foreach(var a in _actions)a.Window.Hide();CloseChoices();}
    private void HideSurfaces(){Browser.Hide();HideTracker();KeyboardModeChanged?.Invoke();}
    internal void RefreshNow()=>Tick();
    internal IReadOnlyList<QuestPassiveWindow> ActionWindows=>_actions.Select(a=>a.Window).ToList();
    internal QuestPassiveWindow TextWindow=>_text;
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;_timer.Stop();EndDrag();_workspace.Changed-=Changed;Options.PropertyChanged-=VisualChanged;
        CloseChoices();foreach(var a in _actions)a.Window.Close();_actions.Clear();_text.Close();
        Browser.ClosingPermanently=true;Browser.Close();KeyboardModeChanged?.Invoke();
    }
}
