using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RoAura.Services;

namespace RoAura.Controls;

public sealed class HotkeyCaptureBox : Button
{
    public static readonly DependencyProperty ValueProperty=DependencyProperty.Register(nameof(Value),typeof(string),typeof(HotkeyCaptureBox),
        new FrameworkPropertyMetadata("None",FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,Changed));
    public string Value {get=>(string)GetValue(ValueProperty);set=>SetValue(ValueProperty,value);}
    private bool _recording;
    private readonly TextBlock _caption=new() {VerticalAlignment=VerticalAlignment.Center};
    private readonly Button _clear=new() {Content="×",Width=22,Height=22,MinWidth=22,MinHeight=22,Padding=new Thickness(0),Margin=new Thickness(6,0,0,0)};
    public HotkeyCaptureBox()
    {
        MinWidth=100;Padding=new Thickness(9,4,9,4);
        SetResourceReference(StyleProperty,typeof(Button));
        SetResourceReference(ForegroundProperty,"RoTextBrush");
        var content=new Grid();
        content.ColumnDefinitions.Add(new() {Width=new GridLength(1,GridUnitType.Star)});
        content.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
        Grid.SetColumn(_clear,1);content.Children.Add(_caption);content.Children.Add(_clear);Content=content;
        _clear.SetResourceReference(ToolTipProperty,"NoHotkey");
        _clear.Click+=(_,e)=>{SetCurrentValue(ValueProperty,"None");Cancel();UpdateCaption();e.Handled=true;};
        Click+=(_,e)=>{if(e.OriginalSource!=this)return;_recording=!_recording;HotkeyCaptureGate.IsCapturing=_recording;UpdateCaption();};
        PreviewKeyDown+=Capture;
        LostKeyboardFocus+=(_,_)=>Cancel();
        Unloaded+=(_,_)=>Cancel();
        var clear=new MenuItem();clear.SetResourceReference(HeaderedItemsControl.HeaderProperty,"ClearHotkey");
        clear.Click+=(_,_)=>{SetCurrentValue(ValueProperty,"None");Cancel();UpdateCaption();};
        ContextMenu=new ContextMenu();ContextMenu.Items.Add(clear);
        UpdateCaption();
    }
    private static void Changed(DependencyObject target,DependencyPropertyChangedEventArgs e)=>((HotkeyCaptureBox)target).UpdateCaption();
    private void Cancel(){if(!_recording)return;_recording=false;HotkeyCaptureGate.IsCapturing=false;UpdateCaption();}
    private void Capture(object sender,KeyEventArgs e)
    {
        if(!_recording)return;
        e.Handled=true;
        var key=e.Key==Key.System?e.SystemKey:e.Key;
        var vk=KeyInterop.VirtualKeyFromKey(key);
        if(vk<=0 || HotkeyChord.IsModifier(vk))return;
        var chord=new HotkeyChord(vk,Keyboard.Modifiers);
        HotkeyCaptureGate.AwaitingReleaseKey=vk;
        SetCurrentValue(ValueProperty,chord.ToString());
        Cancel();
    }
    private void UpdateCaption()
    {
        _caption.Text=_recording?LocalizationService.Get("PressHotkey"):
            HotkeyChord.TryParse(Value,out var chord)?chord.ToString():LocalizationService.Get("NoHotkey");
        _clear.IsEnabled=HotkeyChord.TryParse(Value,out _);
        ToolTip=LocalizationService.Get("CaptureHotkeyHint");
    }
}
