using System.Windows;
using System.Windows.Interop;
using RoAura.Services;
namespace RoAura;

/// <summary>Layered, nonactivating surface. Text uses WS_EX_TRANSPARENT; separate tiny
/// action surfaces are mouse-interactive only over their painted controls.</summary>
public sealed class QuestPassiveWindow : Window
{
    public bool IsTargetChooser {get;init;}
    public QuestPassiveWindow(bool passThrough)
    {
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;
        Background=null;ShowInTaskbar=false;ShowActivated=false;Focusable=false;
        Width=1;Height=1;
        SourceInitialized+=(_,_)=>
        {
            QuestWindowNative.Configure(this,passThrough,true);
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(NoActivate);
        };
    }
    private static IntPtr NoActivate(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message==QuestWindowNative.MouseActivate) {handled=true;return new IntPtr(QuestWindowNative.NoActivateResult);}
        return IntPtr.Zero;
    }
}
