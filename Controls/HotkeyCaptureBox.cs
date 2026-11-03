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
    public HotkeyCaptureBox()
    {
        MinWidth=100;Padding=new Thickness(9,4,9,4);
        SetResourceReference(StyleProperty,typeof(Button));
        SetResourceReference(ForegroundProperty,"RoTextBrush");
        Click+=(_,_)=>{_recording=!_recording;HotkeyCaptureGate.IsCapturing=_recording;UpdateCaption();};
        PreviewKeyDown+=Capture;
        LostKeyboardFocus+=(_,_)=>Cancel();
        Unloaded+=(_,_)=>Cancel();
        var clear=new MenuItem();clear.SetResourceReference(HeaderedItemsControl.HeaderProperty,"ClearHotkey");
        clear.Click+=(_,_)=>{Value="None";Cancel();};
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
        Value=chord.ToString();
        Cancel();
    }
    private void UpdateCaption()
    {
        Content=_recording?LocalizationService.Get("PressHotkey"):
            HotkeyChord.TryParse(Value,out var chord)?chord.ToString():LocalizationService.Get("NoHotkey");
        ToolTip=LocalizationService.Get("CaptureHotkeyHint");
    }
}
