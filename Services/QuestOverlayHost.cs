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
    private StackPanel _cards=new();
    private readonly DispatcherTimer _timer=new() {Interval=TimeSpan.FromMilliseconds(120)};
    private readonly List<(QuestPassiveWindow Window,double Y)> _actions=[];
    private readonly List<QuestPassiveWindow> _actionPool=[];
    private int _actionSlot;
    private QuestPassiveWindow? _choices;
    private QuestProgressEntry? _choiceEntry;
    private int _choiceStep;
    private bool _browserRequested,_trackerRequested=true,_global=true,_dirty=true,_disposed;
    private IntPtr _game;
    private Rect _gamePixels;
    private double _dpi=1;
    private UIElement? _dragElement;
    private Point _dragCursor,_dragOrigin;
    private QuestProgressEntry? _copiedEntry;
    private int _copiedStep;
    private DateTime _copiedUntil;
    public QuestBrowserWindow Browser {get;}
    public bool IsDragging=>_dragElement is not null;
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
        Browser.IsVisibleChanged+=(_,_)=>
        {
            if(Browser.IsVisible && (!_browserRequested || !_global))Browser.Hide();
            KeyboardModeChanged?.Invoke();
        };
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
        if(_dragElement is not null)
        {
            if(!QuestWindowNative.LeftMouseDown){EndDrag();_dirty=true;}
            else{var delta=QuestWindowNative.Cursor()-_dragCursor;Options.X=_dragOrigin.X+delta.X/_dpi;Options.Y=_dragOrigin.Y+delta.Y/_dpi;}
        }
        if(_copiedEntry is not null && DateTime.UtcNow>=_copiedUntil){_copiedEntry=null;_dirty=true;}
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
            .Concat(controls.Where(w=>w is DatabaseOverlayWindow or DatabaseDetailWindow).Select(w=>QuestWindowNative.Bounds(new WindowInteropHelper(w).Handle)))
            .Concat(Browser.IsVisible?[QuestWindowNative.Bounds(new WindowInteropHelper(Browser).Handle)]:[])
            .Where(r=>!r.IsEmpty).ToArray();
        if(!_text.IsVisible)_text.Show();QuestWindowNative.Place(_text,_gamePixels,promote);
        _canvas.UpdateLayout();_canvas.Clip=QuestWindowNative.ExclusionClip(_canvas,new Size(_canvas.Width,_canvas.Height),excluded);
        var scale=Options.Scale*_dpi;
        foreach(var (window,y) in _actions)
        {
            var rect=new Rect(_gamePixels.X+Options.X*_dpi,_gamePixels.Y+(Options.Y+y*Options.Scale)*_dpi,Options.Width*scale,24*scale);
            var inside=_gamePixels.Contains(rect);
            // Clip only the covered pixels. A wide transparent strip can intersect
            // the browser even when all its painted buttons are still to the left.
            if(!inside || !Options.ShowButtons) {window.Hide();continue;}
            if(!window.IsVisible)window.Show();QuestWindowNative.Place(window,rect,promote);
            if(window.Content is FrameworkElement content)
            {content.UpdateLayout();content.Clip=QuestWindowNative.ExclusionClip(content,new Size(Options.Width,24),excluded);}
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
    private QuestPassiveWindow ActionSurface()
    {
        if(_actionSlot==_actionPool.Count)_actionPool.Add(new QuestPassiveWindow(false));
        var window=_actionPool[_actionSlot++];window.Opacity=Options.OpacityPercent/100;return window;
    }
    private void Rebuild()
    {
        // Do not destroy a captured drag HWND while the mouse is held.
        if(_dragElement is not null)return;
        _actionSlot=0;_actions.Clear();var oldCards=_cards;_cards=new StackPanel();
        _cards.Width=Options.Width;_cards.LayoutTransform=new ScaleTransform(Options.Scale,Options.Scale);
        _cards.Opacity=Options.OpacityPercent/100;
        Brush color;try {color=(Brush)new BrushConverter().ConvertFromString(Options.TextColor)!;}catch{color=Brushes.White;}
        var headerStrip=ActionSurface();
        var header=new StackPanel {Orientation=Orientation.Horizontal,LayoutTransform=new ScaleTransform(Options.Scale,Options.Scale)};
        var heading=Label("ROQuestie",color,true);heading.Width=Math.Max(40,Options.Width-32);header.Children.Add(heading);
                var drag=ActionButton("⋮⋮",()=>{});drag.Width=24;drag.ToolTip=T("QMoveTracker");
                drag.PreviewMouseLeftButtonDown+=(_,e)=>
                {
                    _dragElement=drag;_dragCursor=QuestWindowNative.Cursor();_dragOrigin=new(Options.X,Options.Y);drag.CaptureMouse();_dragMode(true);e.Handled=true;
                };
                drag.PreviewMouseMove+=(_,e)=>
                {if(_dragElement!=drag)return;var delta=QuestWindowNative.Cursor()-_dragCursor;Options.X=_dragOrigin.X+delta.X/_dpi;Options.Y=_dragOrigin.Y+delta.Y/_dpi;Tick();};
                drag.PreviewMouseLeftButtonUp+=(_,e)=>{EndDrag();_dirty=true;e.Handled=true;};
                header.Children.Add(drag);headerStrip.Content=header;
        _actions.Add((headerStrip,0));_cards.Children.Add(new Border {Height=28});
        var y=28d;var count=0;
        var visibleRows=_workspace.Active.Where(q=>q.Progress.IsVisibleInOverlay).ToList();
        foreach(var row in visibleRows)
        {
            if(count>=Options.MaxTrackedQuests)break;
            // Only rows that fit the client are exposed. The final More action opens the full list.
            if(count>0 && Options.Y+(y+(row.Progress.IsCollapsed?30:90))*Options.Scale>_gamePixels.Height/_dpi)break;
            var panel=new StackPanel();
            var title=Label(row.Title+" "+row.Position,color,true);title.HorizontalAlignment=HorizontalAlignment.Left;title.Width=Math.Max(40,Options.Width-32);title.MaxHeight=(Options.FontSize+3);title.TextTrimming=TextTrimming.CharacterEllipsis;panel.Children.Add(title);
            var foldStrip=ActionSurface();
            var foldButtons=new StackPanel {Orientation=Orientation.Horizontal,LayoutTransform=new ScaleTransform(Options.Scale,Options.Scale)};
            foldButtons.Children.Add(new Border {Width=Options.Width-30,Height=22});
            var fold=ActionButton(row.Progress.IsCollapsed?"▸":"▾",()=>_workspace.ToggleCollapsed(row));fold.Width=24;fold.IsEnabled=row.Editable;
            fold.ToolTip=T(row.Progress.IsCollapsed?"QExpandQuest":"QCollapseQuest");foldButtons.Children.Add(fold);foldStrip.Content=foldButtons;
            _actions.Add((foldStrip,y));
            if(row.Progress.IsCollapsed)
            {
                _cards.Children.Add(panel);_cards.Children.Add(new Border {Height=Options.CardGap});
                y+=Math.Max(24,Options.FontSize+6)+Options.CardGap;panel.MinHeight=Math.Max(24,Options.FontSize+6);count++;continue;
            }
            var instruction=Label(row.Progress.IsMissingFromCatalog?T("QMissing"):row.Progress.NeedsStepReview?T("QReviewStep"):row.Instruction,color);
            if(!row.Progress.IsMissingFromCatalog && !row.Progress.NeedsStepReview)QuestInstructionText.Apply(instruction,row.Instruction,true);
            instruction.MaxHeight=(Options.FontSize+3)*Options.DescriptionLines;instruction.TextTrimming=TextTrimming.CharacterEllipsis;panel.Children.Add(instruction);
            if(Options.ShowCoordinates)
            {
                var targets=QuestNavigation.Targets(row);
                if(targets.Count>0){var coordinates=Label("",color);coordinates.Inlines.Add(new System.Windows.Documents.Run("\uE707 ") {FontFamily=new FontFamily("Segoe MDL2 Assets")});coordinates.Inlines.Add(new System.Windows.Documents.Run(string.Join(" · ",targets)));coordinates.ToolTip=T("QWikiCoordinates");coordinates.MaxHeight=(Options.FontSize+3)*2;coordinates.TextTrimming=TextTrimming.CharacterEllipsis;panel.Children.Add(coordinates);}
            }
            panel.Measure(new Size(Options.Width,double.PositiveInfinity));
            var textHeight=panel.DesiredSize.Height;
            if(count>0 && Options.Y+(y+textHeight+(Options.ShowButtons?24:0)+Options.CardGap)*Options.Scale>_gamePixels.Height/_dpi){_actions.RemoveAt(_actions.Count-1);foldStrip.Hide();_actionSlot--;break;}
            _cards.Children.Add(panel);
            if(Options.ShowButtons)
            {
                var strip=ActionSurface();var buttons=new StackPanel {Orientation=Orientation.Horizontal,Background=null,LayoutTransform=new ScaleTransform(Options.Scale,Options.Scale)};
                var prev=ActionButton("Prev",()=>_workspace.Move(row,-1));prev.IsEnabled=row.CanPrev;buttons.Children.Add(prev);
                var next=ActionButton("Next",()=>_workspace.Move(row,1));next.IsEnabled=row.CanNext;buttons.Children.Add(next);
                buttons.Children.Add(ActionButton("More",async()=>await OpenBrowserAsync(row.Guide)));
                var targets=QuestNavigation.Targets(row);
                var copied=ReferenceEquals(_copiedEntry,row.Progress) && _copiedStep==row.Progress.CurrentStep && DateTime.UtcNow<_copiedUntil;
                var navi=ActionButton("navi",()=>Navigate(row,strip,targets));navi.Width=64;
                if(copied)QuestCopyFeedback.Success(navi,false);navi.IsEnabled=targets.Count>0 && _workspace.Settings.ServerProfile!=GameServerProfile.Other;
                navi.ToolTip=T(targets.Count==0?"QNoNavigation":"QWikiCoordinates");buttons.Children.Add(navi);
                var step=row.Progress.CurrentStep;
                var done=ActionButton("Done",()=>_workspace.Done(row,step));done.IsEnabled=row.Editable && !row.Progress.NeedsStepReview && !row.Progress.IsMissingFromCatalog;done.ToolTip=T("QDoneHint");buttons.Children.Add(done);
                foreach(var answer in QuestAnswers.Extract(row.Instruction))
                {
                    var copy=ActionButton("Copy",()=>{});copy.ToolTip=T("QCopyAnswer")+": "+answer;
                    copy.Click+=(_,_)=>{if(ClipboardService.TryCopy(answer)){QuestCopyFeedback.Success(copy);}else copy.ToolTip=T("QCopyFailed");};
                    buttons.Children.Add(copy);
                }
                strip.Content=buttons;
                _actions.Add((strip,y+textHeight));
                _cards.Children.Add(new Border {Height=24});
            }
            _cards.Children.Add(new Border {Height=Options.CardGap});y+=textHeight+(Options.ShowButtons?24:0)+Options.CardGap;count++;
        }
        _canvas.Children.Remove(oldCards);_canvas.Children.Add(_cards);
        for(var i=_actionSlot;i<_actionPool.Count;i++)_actionPool[i].Hide();
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
        if(targets.Count==1){Copy(row,targets[0]);return;}
        CloseChoices();_choices=new QuestPassiveWindow(false) {IsTargetChooser=true};_choiceEntry=row.Progress;_choiceStep=row.Progress.CurrentStep;
        var list=new StackPanel();
        foreach(var target in targets)list.Children.Add(ActionButton(target.ToString(),()=>Copy(row,target)));
        var layout=new Grid {Margin=new Thickness(10)};
        layout.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        layout.Children.Add(new TextBlock {Text=T("QChooseNavigation"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8)});
        var scroll=new ScrollViewer {MaxHeight=220,Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Focusable=false};Grid.SetRow(scroll,1);layout.Children.Add(scroll);
        var cancel=ActionButton("×",CloseChoices);Grid.SetRow(cancel,2);layout.Children.Add(cancel);
        _choices.Content=new Border {Child=layout,CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),BorderBrush=(Brush)Browser.FindResource("RoSoftBorderBrush"),Background=(Brush)Browser.FindResource("RoCardBrush")};var anchor=QuestWindowNative.Bounds(new WindowInteropHelper(strip).Handle);
        var width=Math.Min(340*_dpi,_gamePixels.Width);var height=Math.Min((Math.Min(targets.Count*26,220)+100)*_dpi,_gamePixels.Height);
        var x=Math.Clamp(anchor.Left,_gamePixels.Left,Math.Max(_gamePixels.Left,_gamePixels.Right-width));
        var top=Math.Clamp(anchor.Bottom,_gamePixels.Top,Math.Max(_gamePixels.Top,_gamePixels.Bottom-height));
        _choices.Show();QuestWindowNative.Place(_choices,new(x,top,width,height));
    }
    private void Copy(QuestListRow row,QuestNavigationTarget target)
    {
        if(ClipboardService.TryCopy(target.Command))
        {_copiedEntry=row.Progress;_copiedStep=row.Progress.CurrentStep;_copiedUntil=DateTime.UtcNow.AddSeconds(4);}
        else if(_choices is not null)_choices.ToolTip=T("QCopyFailed");
        CloseChoices();_dirty=true;
    }
    private void CloseChoices(){_choices?.Close();_choices=null;}
    private void HideTracker()
    {EndDrag();_text.Hide();foreach(var a in _actions)a.Window.Hide();CloseChoices();}
    private void HideSurfaces(){Browser.Hide();HideTracker();KeyboardModeChanged?.Invoke();}
    internal void RefreshNow()=>Tick();
    internal IReadOnlyList<QuestPassiveWindow> ActionWindows=>_actions.Select(a=>a.Window).ToList();
    internal QuestPassiveWindow TextWindow=>_text;
    internal QuestPassiveWindow? TargetChooser=>_choices;
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;_timer.Stop();EndDrag();_workspace.Changed-=Changed;Options.PropertyChanged-=VisualChanged;
        CloseChoices();foreach(var window in _actionPool)window.Close();_actionPool.Clear();_actions.Clear();_text.Close();
        Browser.ClosingPermanently=true;Browser.Close();KeyboardModeChanged?.Invoke();
    }
}
