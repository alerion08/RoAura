using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RoAura.Models;
using RoAura.Services;

namespace RoAura.Controls;

public sealed class MvpMapEditor : UserControl
{
    private readonly Grid _picture=new() {Width=250,Height=250,ClipToBounds=true,Background=Brushes.Transparent};
    private readonly Image _image=new() {Stretch=Stretch.Uniform};
    private readonly Canvas _marks=new() {IsHitTestVisible=false};
    private readonly TextBlock _hint=new() {TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Center};
    private readonly Border _empty=new() {Margin=new Thickness(8),Padding=new Thickness(16),CornerRadius=new CornerRadius(7),BorderThickness=new Thickness(1),IsHitTestVisible=false};
    private readonly MvpMapImageCache _cache;
    private MvpTimer? _timer;
    private string _loadedMap="";
    private int _loadVersion;
    private bool _imageFailed, _loading;
    public bool IsMapReady => _image.Source is BitmapSource;
    public string DisplayedMapCode => IsMapReady ? _loadedMap : "";
    public bool ReadOnly {get;set;}
    public MvpMapEditor() : this(MvpMapImageCache.Shared) { }
    public MvpMapEditor(MvpMapImageCache cache)
    {
        _cache=cache;
        _empty.SetResourceReference(Border.BackgroundProperty,"RoSelectionBrush");
        _empty.SetResourceReference(Border.BorderBrushProperty,"RoSoftBorderBrush");
        _hint.SetResourceReference(TextBlock.ForegroundProperty,"RoTextBrush");_empty.Child=_hint;
        _picture.Children.Add(_image);_picture.Children.Add(_marks);_picture.Children.Add(_empty);
        Content=_picture;_picture.MouseLeftButtonDown+=Mark;
        // A failed fetch can be retried without changing the selected location or its mark.
        _picture.MouseLeftButtonDown+=(_,_)=>{if(_imageFailed){_loadedMap="#retry";Refresh();}};
        DataContextChanged+=(_,_)=>Attach();Loaded+=(_,_)=>Attach();
        Unloaded+=(_,_)=>{if(_timer is not null)_timer.PropertyChanged-=Changed;_timer=null;++_loadVersion;};
    }
    private void Attach()
    {
        if(_timer is not null)_timer.PropertyChanged-=Changed;
        _timer=DataContext as MvpTimer;
        if(_timer is not null)_timer.PropertyChanged+=Changed;
        _loadedMap="#reload";Refresh();
    }
    private void Changed(object? sender,PropertyChangedEventArgs e)
    {if(e.PropertyName is nameof(MvpTimer.LocationCode) or nameof(MvpTimer.MarkerX) or nameof(MvpTimer.MarkerY))Refresh();}
    private void Refresh()
    {
        var code=MvpMapImageCache.Normalize(_timer?.LocationCode);
        if(code!=_loadedMap)
        {
            _loadedMap=code;
            var version=++_loadVersion;
            _image.Source=null;_imageFailed=false;_loading=code.Length>0;
            UpdatePlaceholder();
            if(_loading)LoadMap(code,version);
        }
        DrawMarker();
    }
    private async void LoadMap(string code,int version)
    {
        var image=await _cache.GetAsync(code);
        // A previous map completing later must never overwrite a newer choice or an unloaded control.
        if(version!=_loadVersion || code!=_loadedMap)return;
        _loading=false;_imageFailed=image is null;_image.Source=image;
        UpdatePlaceholder();DrawMarker();
    }
    private void UpdatePlaceholder()
    {
        _empty.Visibility=IsMapReady?Visibility.Collapsed:Visibility.Visible;
        _hint.Text=LocalizationService.Get(_loading?"MapImageLoading":_imageFailed?"MapImageUnavailable":"MapMarkerHint");
        if(!IsMapReady)_marks.Children.Clear();
    }
    private Rect ImageBounds()
    {
        if(_image.Source is not BitmapSource source || source.IsDownloading || source.PixelWidth<=0 || source.PixelHeight<=0 || _imageFailed)return Rect.Empty;
        var ratio=Math.Min(250d/source.PixelWidth,250d/source.PixelHeight);
        var width=source.PixelWidth*ratio;var height=source.PixelHeight*ratio;
        return new Rect((250-width)/2,(250-height)/2,width,height);
    }
    private void Mark(object sender,MouseButtonEventArgs e)
    {
        if(ReadOnly || _timer is null)return;
        var bounds=ImageBounds();var point=e.GetPosition(_picture);
        if(bounds.IsEmpty||!bounds.Contains(point))return;
        _timer.MarkerX=(point.X-bounds.Left)/bounds.Width;_timer.MarkerY=(point.Y-bounds.Top)/bounds.Height;
        DrawMarker();e.Handled=true;
    }
    private void DrawMarker()
    {
        _marks.Children.Clear();var bounds=ImageBounds();
        if(_timer?.MarkerX is not double x || _timer.MarkerY is not double y || bounds.IsEmpty)return;
        var dot=new System.Windows.Shapes.Ellipse {Width=12,Height=12,Fill=Brushes.Red,Stroke=Brushes.White,StrokeThickness=2};
        Canvas.SetLeft(dot,bounds.Left+x*bounds.Width-6);Canvas.SetTop(dot,bounds.Top+y*bounds.Height-6);_marks.Children.Add(dot);
    }
}
