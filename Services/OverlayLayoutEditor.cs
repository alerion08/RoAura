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
    private sealed record Handle(OverlayLayoutTarget Target, QuestPassiveWindow Label, QuestPassiveWindow Grip, Button Button);
    private readonly AppSettings _settings;
    private readonly OverlayWindow _overlay;
    private readonly QuestOverlayHost _quests;
    private readonly Action _save;
    private readonly Action<bool> _activeChanged;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly List<Handle> _handles = [];
    private QuestPassiveWindow? _banner;
    private Handle? _moving;
    private OverlayLayoutDrag? _drag;
    private Point _cursor;
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
        IsActive = true;
        try
        {
            _overlay.SetOverlaysVisible(true); _quests.SetGlobalVisible(true);
            _overlay.SetLayoutPreview(true, targets); _quests.SetLayoutEditing(targets.Any(target => target.Key == "quest"));
            foreach (var target in targets) AddHandle(target);
            _banner = Label(LocalizationService.Get("InterfaceLayoutMode") + " · " +
                _settings.InterfaceLayoutHotkey + " / " + _settings.FinishMoveHotkey);
            _activeChanged(true); _timer.Start(); Tick();
            return true;
        }
        catch { Stop(); throw; }
    }
    public void Stop()
    {
        if (!IsActive) return;
        IsActive = false; _timer.Stop(); EndDrag();
        foreach (var handle in _handles) { handle.Grip.Close(); handle.Label.Close(); }
        _handles.Clear(); _banner?.Close(); _banner = null;
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
        var grip = new QuestPassiveWindow(false) { Content = button };
        AppearanceService.ApplyPalette(grip, AppearanceService.Resolve(_settings.ApplicationTheme, _settings.GameOverlayTheme, true));
        var handle = new Handle(target, label, grip, button); _handles.Add(handle);
        button.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!IsActive) return;
            EndDrag(); _moving = handle; _cursor = QuestWindowNative.Cursor(); _dragMoved = false;
            var rect = QuestWindowNative.Bounds(_game);
            _drag = target.BeginDrag(new Size(rect.Width / _dpi, rect.Height / _dpi));
            button.CaptureMouse(); e.Handled = true;
        };
        button.PreviewMouseMove += (_, e) => { if (_moving == handle) Move(); };
        button.PreviewMouseLeftButtonUp += (_, e) => { if (_moving == handle) { Move(); EndDrag(); e.Handled = true; } };
        button.LostMouseCapture += (_, _) => { if (_moving == handle) EndDrag(); };
    }
    private void Move()
    {
        if (_drag is null) return;
        var cursor = QuestWindowNative.Cursor();
        if (!_dragMoved && (cursor - _cursor).Length < 3) return;
        _dragMoved = true;
        _drag.MoveScreen(_cursor, cursor, _dpi);
        _overlay.RefreshLayoutPreviewPositions();
        _quests.RefreshNow();
    }
    private void EndDrag()
    {
        if (_moving is null) return;
        var button = _moving.Button; _moving = null; _drag = null;
        button.ReleaseMouseCapture(); _save();
    }
    private void HideHandles()
    {
        EndDrag();
        foreach (var handle in _handles) { handle.Grip.Hide(); handle.Label.Hide(); }
        _banner?.Hide();
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
        var placed = new List<Rect>();
        foreach (var handle in _handles)
        {
            var size = new Size(Math.Min(260, viewport.Width), 26);
            var origin = handle.Target.Bounds;
            var x = Math.Clamp(origin.Left, 0, Math.Max(0, viewport.Width - size.Width));
            var y = origin.Top >= 30 ? origin.Top - 30 : origin.Bottom + 4;
            y = Math.Clamp(y, 0, Math.Max(0, viewport.Height - size.Height));
            var rect = new Rect(new Point(x, y), size);
            // Separate coincident groups without changing model coordinates. Keep the captured grip stable.
            if (_moving != handle)
                for (var attempt = 0; attempt < _handles.Count && placed.Any(previous => previous.IntersectsWith(rect)); attempt++)
                {
                    y = y + 30 <= viewport.Height - 26 ? y + 30 : Math.Max(0, y - 60);
                    rect.Y = y;
                }
            placed.Add(rect);
            Place(handle.Label, new Rect(rect.X, rect.Y, Math.Max(1, rect.Width - 30), 26), bounds);
            Place(handle.Grip, new Rect(rect.Right - 26, rect.Y, 26, 26), bounds);
        }
        if (_banner is not null)
            Place(_banner, new Rect(Math.Max(0, (viewport.Width - 460) / 2), 2, Math.Min(460, viewport.Width), 26), bounds);
    }
    private void Place(Window window, Rect dip, ForegroundProcess.WindowBounds bounds)
    {
        if (!window.IsVisible) window.Show();
        QuestWindowNative.Place(window, new Rect(bounds.Left + dip.X * _dpi, bounds.Top + dip.Y * _dpi,
            dip.Width * _dpi, dip.Height * _dpi));
    }
    public void Dispose() { Stop(); _settings.PropertyChanged -= SettingsChanged; }
}
