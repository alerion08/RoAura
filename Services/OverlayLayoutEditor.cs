using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using RoAura.Models;

namespace RoAura.Services;

/// <summary>Only small grip HWNDs accept input. Labels and the ordinary overlay stay click-through.</summary>
public sealed class OverlayLayoutEditor : IDisposable
{
    private sealed record Handle(OverlayLayoutTarget Target, QuestPassiveWindow Label, QuestPassiveWindow Grip, Button Button, Button Eye, Button Lock);
    private readonly AppSettings _settings;
    private readonly OverlayWindow _overlay;
    private readonly QuestOverlayHost _quests;
    private readonly Action _save;
    private readonly Action<bool> _activeChanged;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly List<Handle> _handles = [];
    private QuestPassiveWindow? _banner;
    private QuestPassiveWindow? _gridWindow;
    private OverlayLayoutTarget? _selected;
    private readonly List<Button> _alignmentButtons = [];
    private readonly List<Button> _sizeButtons = [];
    private readonly List<Button> _layerButtons = [];
    private bool _snap;
    private string BannerText => LocalizationService.Get("InterfaceLayoutMode") + " · " +
        (_selected?.Caption ?? (_settings.InterfaceLayoutHotkey + " / " + _settings.FinishMoveHotkey));
    private QuestPassiveWindow? _undoWindow;
    private Button? _undoButton;
    private readonly Stack<Action> _undo = new();
    private Handle? _moving;
    private OverlayLayoutDrag? _drag;
    private Point _cursor;
    private Point? _lastDragCursor;
    private readonly Dictionary<Window, Rect> _placedPixels = [];
    private IntPtr _game;
    private string _identity = "";
    private bool _overlaysVisible, _questsVisible;
    private double _dpi = 1;
    private bool _dragMoved;
    private Rect _lastClient = Rect.Empty;
    public bool IsActive { get; private set; }
    internal IReadOnlyDictionary<string, Window> GripWindows => _handles.ToDictionary(handle => handle.Target.Key, handle => (Window)handle.Grip);
    internal IReadOnlyDictionary<string, Window> LabelWindows => _handles.ToDictionary(handle => handle.Target.Key, handle => (Window)handle.Label);
    private static string Identity()
    {
        var session = (Application.Current as App)?.CurrentCharacterSession;
        return session is null ? "" : $"{session.Value.ServerKey}:{session.Value.CharacterId}:{session.Value.Phase}";
    }
    public OverlayLayoutEditor(AppSettings settings, OverlayWindow overlay, QuestOverlayHost quests,
        Action save, Action<bool> activeChanged)
    {
        _settings = settings; _overlay = overlay; _quests = quests; _save = save; _activeChanged = activeChanged;
        _timer.Tick += (_, _) => Tick();
        settings.PropertyChanged += SettingsChanged;
    }
    public bool Start()
    {
        if (IsActive) return true;
        if (!ForegroundProcess.IsForegroundAllowed(_settings.AllowedProcesses) ||
            !ForegroundProcess.TryFindWindow(_settings.AllowedProcesses, out _game, out _)) return false;
        _identity = Identity();
        _overlaysVisible = _overlay.AreOverlaysVisible; _questsVisible = _quests.GlobalVisible;
        var targets = OverlayLayoutTargets.Build(_settings);
        DebugLog.Write($"INTERFACE LAYOUT START: targets={string.Join(",", targets.Select(target => target.Key))}");
        IsActive = true;
        try
        {
            _overlay.SetOverlaysVisible(true); _quests.SetGlobalVisible(true);
            _overlay.SetLayoutPreview(true, targets); _quests.SetLayoutEditing(targets.Any(target => target.Key == "quest"));
            foreach (var target in targets) AddHandle(target);
            _banner = Label(BannerText);
            _gridWindow = new QuestPassiveWindow(true) { Content = new LayoutGrid() };
            _undoButton = new Button { Content = LocalizationService.Get("LayoutUndo"),
                Focusable = false, IsEnabled = false, MinHeight = 0, Padding = new Thickness(4, 0, 4, 0) };
            _undoButton.Click += (_, _) => Undo();
            var actions = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
            foreach (var direction in new[] { -1, 1 })
            {
                var size = new Button { Content = direction < 0 ? "−" : "+",
                    Focusable = false, IsEnabled = false, MinHeight = 0, Padding = new Thickness(0) };
                size.Click += (_, _) => ChangeSize(direction);
                _sizeButtons.Add(size);
                actions.Children.Add(size);
            }
            var grid = new System.Windows.Controls.Primitives.ToggleButton { Content = "▦", Focusable = false,
                MinHeight = 0, Padding = new Thickness(0), ToolTip = LocalizationService.Get("LayoutGridSnap") };
            System.Windows.Automation.AutomationProperties.SetName(grid, LocalizationService.Get("LayoutGridSnap"));
            grid.Click += (_, _) => { _snap = grid.IsChecked == true; Tick(); };
            actions.Children.Add(grid);
            foreach (var (symbol, key, x, y) in new (string, string, double?, double?)[]
            {
                ("←", "LayoutAlignLeft", 0, null), ("→", "LayoutAlignRight", 1, null),
                ("↑", "LayoutAlignTop", null, 0), ("↓", "LayoutAlignBottom", null, 1),
                ("⊙", "LayoutAlignCenter", .5, .5)
            })
            {
                var caption = LocalizationService.Get(key);
                var align = new Button { Content = symbol, ToolTip = caption, Focusable = false, IsEnabled = false,
                    MinHeight = 0, Padding = new Thickness(0) };
                System.Windows.Automation.AutomationProperties.SetName(align, caption);
                align.Click += (_, _) => Align(x, y);
                _alignmentButtons.Add(align);
                actions.Children.Add(align);
            }
            foreach (var front in new[] { false, true })
            {
                var caption = LocalizationService.Get(front ? "LayoutBringFront" : "LayoutSendBack");
                var layer = new Button { Content = front ? "⇈" : "⇊", ToolTip = caption, Focusable = false,
                    IsEnabled = false, MinHeight = 0, Padding = new Thickness(0) };
                System.Windows.Automation.AutomationProperties.SetName(layer, caption);
                layer.Click += (_, _) => ChangeLayer(front);
                _layerButtons.Add(layer);
                actions.Children.Add(layer);
            }
            actions.Children.Add(_undoButton);
            _undoWindow = new QuestPassiveWindow(false) { Content = actions };
            AppearanceService.ApplyPalette(_undoWindow, AppearanceService.Resolve(_settings.ApplicationTheme, _settings.GameOverlayTheme, true));
            _activeChanged(true); _timer.Start(); Tick();
            return true;
        }
        catch { Stop(); throw; }
    }
    public void Stop()
    {
        if (!IsActive) return;
        IsActive = false; _timer.Stop(); EndDrag();
        DebugLog.Write("INTERFACE LAYOUT STOP");
        foreach (var handle in _handles) { handle.Grip.Close(); handle.Label.Close(); }
        _handles.Clear(); _banner?.Close(); _banner = null;
        _undoWindow?.Close(); _undoWindow = null; _undoButton = null; _undo.Clear();
        _gridWindow?.Close(); _gridWindow = null;
        _selected = null; _snap = false; _alignmentButtons.Clear(); _sizeButtons.Clear(); _layerButtons.Clear(); _placedPixels.Clear();
        _overlay.SetLayoutPreview(false, []); _quests.SetLayoutEditing(false);
        _overlay.SetOverlaysVisible(_overlaysVisible); _quests.SetGlobalVisible(_questsVisible);
        _activeChanged(false); _save();
    }
    private void SettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.ServerProfile) or nameof(AppSettings.PacketCaptureEnabled) or nameof(AppSettings.Enabled)
            or nameof(AppSettings.AllowedProcesses) or "") Stop();
    }
    private QuestPassiveWindow Label(string text)
    {
        var window = new QuestPassiveWindow(true);
        var label = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "RoTextBrush");
        var card = new Border { Child = label, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "RoCardBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "RoBorderBrush");
        window.Content = card;
        AppearanceService.ApplyPalette(window, AppearanceService.Resolve(_settings.ApplicationTheme, _settings.GameOverlayTheme, true));
        return window;
    }
    private void AddHandle(OverlayLayoutTarget target)
    {
        var label = Label(target.Caption);
        var button = new Button { Content = "⋮⋮", Width = 26, Height = 26, Padding = new Thickness(0),
            MinWidth = 0, MinHeight = 0, Margin = new Thickness(0), FontSize = 16, Focusable = false, Cursor = Cursors.SizeAll,
            ToolTip = target.Caption };
        var eye = new Button { Width = 26, Height = 26, MinWidth = 0, MinHeight = 0,
            Margin = new Thickness(0), Padding = new Thickness(0), Focusable = false };
        var panelLock = new Button { Width = 26, Height = 26, MinWidth = 0, MinHeight = 0,
            Margin = new Thickness(0), Padding = new Thickness(0), Focusable = false };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(button);
        buttons.Children.Add(eye);
        buttons.Children.Add(panelLock);
        var grip = new QuestPassiveWindow(false) { Content = buttons };
        AppearanceService.ApplyPalette(grip, AppearanceService.Resolve(_settings.ApplicationTheme, _settings.GameOverlayTheme, true));
        var handle = new Handle(target, label, grip, button, eye, panelLock); _handles.Add(handle);
        eye.Click += (_, _) =>
        {
            EndDrag();
            SelectTarget(target);
            var current = Preference(target);
            ChangePreference(target, current with { Hidden = !current.Hidden });
        };
        panelLock.Click += (_, _) =>
        {
            EndDrag();
            SelectTarget(target);
            var current = Preference(target);
            ChangePreference(target, current with { Locked = !current.Locked });
        };
        UpdateHandle(handle);
        button.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!IsActive) return;
            EndDrag();
            SelectTarget(target);
            e.Handled = true;
            if (Preference(target).Locked) return;
            _moving = handle; _cursor = QuestWindowNative.Cursor(); _lastDragCursor = null; _dragMoved = false;
            var rect = QuestWindowNative.Bounds(_game);
            _drag = target.BeginDrag(new Size(rect.Width / _dpi, rect.Height / _dpi));
            button.CaptureMouse(); e.Handled = true;
        };
        button.PreviewMouseMove += (_, e) => { if (_moving == handle) Move(); };
        button.PreviewMouseLeftButtonUp += (_, e) => { if (_moving == handle) { Move(); EndDrag(); e.Handled = true; } };
        button.LostMouseCapture += (_, _) => { if (_moving == handle) EndDrag(); };
    }

    private OverlayPanelPreference Preference(OverlayLayoutTarget target) =>
        _settings.OverlayPanels.GetValueOrDefault(target.Key) ?? new();

    private void SelectTarget(OverlayLayoutTarget target)
    {
        _selected = target;
        if (_banner?.Content is Border { Child: TextBlock labelText }) labelText.Text = BannerText;
        foreach (var align in _alignmentButtons) align.IsEnabled = !Preference(target).Locked;
        foreach (var layer in _layerButtons) layer.IsEnabled = true;
        for (var index = 0; index < _sizeButtons.Count; index++)
        {
            var button = _sizeButtons[index];
            button.IsEnabled = target.QuickSizeLabel is not null;
            if (target.QuickSizeLabel is not { } label) continue;
            var caption = string.Format(LocalizationService.Get(index == 0 ? "LayoutSizeDown" : "LayoutSizeUp"),
                LocalizationService.Get(label));
            button.ToolTip = caption;
            System.Windows.Automation.AutomationProperties.SetName(button, caption);
        }
    }

    private void ChangeLayer(bool front)
    {
        if (_selected is not { } target || _moving is not null) return;
        var ranks = _settings.OverlayPanels.Values.Select(value => value.Layer);
        var layer = front ? Math.Max(100, ranks.DefaultIfEmpty(0).Max() + 1)
                          : Math.Min(-100, ranks.DefaultIfEmpty(0).Min() - 1);
        ChangePreference(target, Preference(target) with { Layer = layer });
    }

    private void ChangePreference(OverlayLayoutTarget target, OverlayPanelPreference value)
    {
        var previous = Preference(target);
        if (previous == value) return;
        Remember(() => Apply(previous));
        Apply(value);
        _save();

        void Apply(OverlayPanelPreference preference)
        {
            _settings.OverlayPanels[target.Key] = preference;
            UpdateHandle(_handles.First(handle => handle.Target == target));
            if (_selected == target) SelectTarget(target);
            _overlay.RefreshPanelLayers();
            _quests.RefreshNow();
        }
    }

    private void UpdateHandle(Handle handle)
    {
        var preference = Preference(handle.Target);
        handle.Button.Cursor = preference.Locked ? Cursors.Arrow : Cursors.SizeAll;
        handle.Button.Opacity = preference.Locked ? .45 : 1;
        Configure(handle.Eye, preference.Hidden ? "⊘" : "👁", preference.Hidden ? "LayoutShowPanel" : "LayoutHidePanel");
        Configure(handle.Lock, preference.Locked ? "🔒" : "🔓", preference.Locked ? "LayoutUnlockPanel" : "LayoutLockPanel");

        static void Configure(Button button, string glyph, string key)
        {
            var caption = LocalizationService.Get(key);
            button.Content = glyph;
            button.ToolTip = caption;
            System.Windows.Automation.AutomationProperties.SetName(button, caption);
        }
    }

    private void ChangeSize(int direction)
    {
        if (!IsActive || _moving is not null || _selected?.AdjustSize(direction) is not { } undo) return;
        Remember(() =>
        {
            undo();
            RefreshSizedPreviews();
        });
        RefreshSizedPreviews();
        RefreshPositions();
        _save();
    }

    private void RefreshSizedPreviews() =>
        _overlay.SetLayoutPreview(true, _handles.Select(handle => handle.Target).ToArray());

    private void Move()
    {
        if (_drag is null) return;
        var cursor = QuestWindowNative.Cursor();
        if (!_dragMoved && (cursor - _cursor).Length < 3) return;
        if (_lastDragCursor == cursor) return;
        _lastDragCursor = cursor;
        _dragMoved = true;
        _drag.MoveScreen(_cursor, cursor, _dpi, _snap);
        RefreshPositions();
    }

    private void Align(double? x, double? y)
    {
        if (!IsActive || _moving is not null || _selected is null || Preference(_selected).Locked || _lastClient.IsEmpty) return;
        var drag = _selected.BeginDrag(new Size(_lastClient.Width / _dpi, _lastClient.Height / _dpi));
        drag.Align(x, y);
        if (!drag.HasChanged) return;
        Remember(drag.Restore);
        RefreshPositions();
        _save();
    }

    private void RefreshPositions()
    {
        _overlay.RefreshLayoutPreviewPositions();
        _quests.RefreshNow();
    }

    private void Remember(Action undo)
    {
        _undo.Push(undo);
        if (_undoButton is not null) _undoButton.IsEnabled = true;
    }

    private void Undo()
    {
        if (!IsActive || _moving is not null || !_undo.TryPop(out var undo)) return;
        undo();
        if (_undoButton is not null) _undoButton.IsEnabled = _undo.Count > 0;
        RefreshPositions();
        _save();
    }
    private void EndDrag()
    {
        if (_moving is null) return;
        var button = _moving.Button;
        var drag = _drag;
        _moving = null; _drag = null;
        button.ReleaseMouseCapture();
        if (drag is { HasChanged: true })
        {
            if (IsActive) Remember(drag.Restore);
            _save();
        }
    }
    private void HideHandles()
    {
        EndDrag();
        foreach (var handle in _handles) { handle.Grip.Hide(); handle.Label.Hide(); }
        _banner?.Hide(); _undoWindow?.Hide(); _gridWindow?.Hide();
    }
    private void Tick()
    {
        if (!IsActive) return;
        if (!QuestWindowNative.IsWindow(_game) || Identity() != _identity) { Stop(); return; }
        if (!ForegroundProcess.TryFindWindow(_settings.AllowedProcesses, out var game, out var bounds)) { HideHandles(); return; }
        if (game != _game) { Stop(); return; }
        if (QuestWindowNative.GetForegroundWindow() != _game) { HideHandles(); return; }
        var dpi = QuestWindowNative.Scale(_game);
        if (Math.Abs(dpi - _dpi) > .001) { EndDrag(); _dpi = dpi; }
        var client = new Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        if (client != _lastClient) { EndDrag(); _lastClient = client; }
        if (_moving is not null)
        {
            if (!QuestWindowNative.LeftMouseDown) EndDrag(); else Move();
        }
        var viewport = new Rect(0, 0, (bounds.Right - bounds.Left) / _dpi, (bounds.Bottom - bounds.Top) / _dpi);
        if (_gridWindow is not null)
        {
            if (_snap) Place(_gridWindow, viewport, bounds);
            else if (_gridWindow.IsVisible) _gridWindow.Hide();
        }
        var placed = new List<Rect>();
        var maxGripY = Math.Max(0, viewport.Height - 26);
        var minGripY = Math.Min(32, maxGripY);
        foreach (var handle in _handles)
        {
            var size = new Size(Math.Min(260, viewport.Width), 26);
            var origin = handle.Target.Bounds;
            var x = Math.Clamp(origin.Left, 0, Math.Max(0, viewport.Width - size.Width));
            var y = origin.Top >= 30 ? origin.Top - 30 : origin.Bottom + 4;
            y = Math.Clamp(y, minGripY, maxGripY);
            var rect = new Rect(new Point(x, y), size);
            // Separate coincident groups without changing model coordinates. Keep the captured grip stable.
            if (_moving != handle)
                for (var attempt = 0; attempt < _handles.Count && placed.Any(previous => previous.IntersectsWith(rect)); attempt++)
                {
                    y = y + 30 <= maxGripY ? y + 30 : Math.Max(minGripY, y - 60);
                    rect.Y = y;
                }
            placed.Add(rect);
            Place(handle.Label, new Rect(rect.X, rect.Y, Math.Max(1, rect.Width - 82), 26), bounds);
            Place(handle.Grip, new Rect(rect.Right - 78, rect.Y, 78, 26), bounds);
        }
        var toolbarWidth = Math.Min(750, viewport.Width);
        var toolbarX = (viewport.Width - toolbarWidth) / 2;
        var undoWidth = Math.Min(505, toolbarWidth * .74);
        var labelWidth = toolbarWidth - undoWidth;
        if (_banner is not null)
            Place(_banner, new Rect(toolbarX, 2, labelWidth, 26), bounds);
        if (_undoWindow is not null)
            Place(_undoWindow, new Rect(toolbarX + labelWidth, 2, undoWidth, 26), bounds);
    }
    private void Place(Window window, Rect dip, ForegroundProcess.WindowBounds bounds)
    {
        var pixels = new Rect(
            Math.Round(bounds.Left + dip.X * _dpi), Math.Round(bounds.Top + dip.Y * _dpi),
            Math.Max(1, Math.Round(dip.Width * _dpi)), Math.Max(1, Math.Round(dip.Height * _dpi)));
        var visible = window.IsVisible;
        if (visible && _placedPixels.TryGetValue(window, out var previous) && previous == pixels) return;
        if (!visible) window.Show();
        QuestWindowNative.Place(window, pixels, preserveZOrder: visible);
        _placedPixels[window] = pixels;
    }
    private sealed class LayoutGrid : FrameworkElement
    {
        private static readonly Pen Line = CreatePen();

        private static Pen CreatePen()
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(85, 160, 198, 255)), 1);
            pen.Freeze();
            return pen;
        }

        protected override void OnRender(DrawingContext drawing)
        {
            for (var x = OverlayLayoutDrag.GridStep; x < ActualWidth; x += OverlayLayoutDrag.GridStep)
                drawing.DrawLine(Line, new Point(x, 0), new Point(x, ActualHeight));
            for (var y = OverlayLayoutDrag.GridStep; y < ActualHeight; y += OverlayLayoutDrag.GridStep)
                drawing.DrawLine(Line, new Point(0, y), new Point(ActualWidth, y));
        }
    }

    public void Dispose() { Stop(); _settings.PropertyChanged -= SettingsChanged; }
}
