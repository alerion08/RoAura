using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RoAura.Models;
using RoAura.Services;

namespace RoAura.Controls;

/// <summary>Marker coordinates are fractions of the displayed minimap, independent of DPI.</summary>
public sealed class MvpMapEditor : UserControl
{
    private readonly Grid _picture = new() { Width = 250, Height = 250, ClipToBounds = true, Background = Brushes.Transparent };
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly Canvas _marks = new() { IsHitTestVisible = false };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,0) };
    private readonly Button _clear = new() { Margin = new Thickness(0,8,0,0), HorizontalAlignment = HorizontalAlignment.Left };
    private MvpTimer? _timer;
    private string _loadedMap = "";
    private bool _loading;
    public bool ReadOnly { get; set; }

    public MvpMapEditor()
    {
        var root = new StackPanel();
        _clear.SetResourceReference(ContentControl.ContentProperty, "ClearMapMarker");
        _picture.Children.Add(_image); _picture.Children.Add(_marks);
        root.Children.Add(_picture); root.Children.Add(_message); root.Children.Add(_clear);
        Content = root;
        _picture.MouseLeftButtonDown += Mark;
        _clear.Click += (_, _) => { if (_timer is not null) _timer.ClearMarker(); };
        _image.ImageFailed += (_, _) => { _message.Text = LocalizationService.Get("MapImageUnavailable"); _marks.Children.Clear(); };
        DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => { if (_timer is not null) _timer.PropertyChanged -= TimerChanged; _timer = null; };
    }
    private void Attach()
    {
        if (_timer is not null) _timer.PropertyChanged -= TimerChanged;
        _timer = DataContext as MvpTimer;
        if (_timer is not null) _timer.PropertyChanged += TimerChanged;
        _loadedMap = "";
        Refresh();
    }
    private void TimerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MvpTimer.RespawnHints) or nameof(MvpTimer.LocationCode) or
            nameof(MvpTimer.MarkerX) or nameof(MvpTimer.MarkerY)) Refresh();
    }
    private void Refresh()
    {
        _loading = true;
        try
        {
            _clear.Visibility = ReadOnly ? Visibility.Collapsed : Visibility.Visible;
            var code = _timer?.LocationCode ?? "";
            if (_loadedMap != code)
            {
                _loadedMap = code; _image.Source = null;
                if (code.Length > 0 && code.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_'))
                {
                    var image = new BitmapImage();
                    image.BeginInit(); image.UriSource = new Uri($"https://pub-36f18a13614d49f0997ef23bce857ec4.r2.dev/sprites/minimaps/{code}.png");
                    image.CacheOption = BitmapCacheOption.OnDemand; image.EndInit();
                    image.DownloadCompleted += (_, _) => DrawMarker();
                    _image.Source = image;
                }
            }
            _message.Text = ReadOnly ? code : LocalizationService.Get("MapMarkerHint");
            DrawMarker();
        }
        finally { _loading = false; }
    }
    private Rect ImageBounds()
    {
        if (_image.Source is not BitmapSource source || source.PixelWidth <= 0 || source.PixelHeight <= 0) return Rect.Empty;
        var ratio = Math.Min(250d / source.PixelWidth, 250d / source.PixelHeight);
        var width = source.PixelWidth * ratio; var height = source.PixelHeight * ratio;
        return new Rect((250-width)/2, (250-height)/2, width, height);
    }
    private void Mark(object sender, MouseButtonEventArgs e)
    {
        if (ReadOnly || _timer is null || string.IsNullOrWhiteSpace(_timer.LocationCode)) return;
        var bounds = ImageBounds(); var point = e.GetPosition(_picture);
        if (bounds.IsEmpty || !bounds.Contains(point)) return;
        _timer.MarkerX = (point.X - bounds.Left) / bounds.Width;
        _timer.MarkerY = (point.Y - bounds.Top) / bounds.Height;
        DrawMarker(); e.Handled = true;
    }
    private void DrawMarker()
    {
        _marks.Children.Clear();
        var bounds = ImageBounds();
        if (_timer?.MarkerX is not double x || _timer.MarkerY is not double y || bounds.IsEmpty) return;
        var mark = new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, Fill = Brushes.Red, Stroke = Brushes.White, StrokeThickness = 2 };
        Canvas.SetLeft(mark, bounds.Left + x * bounds.Width - 6); Canvas.SetTop(mark, bounds.Top + y * bounds.Height - 6);
        _marks.Children.Add(mark);
    }
}
