using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using RoAura.Models;
using RoAura.Services;
namespace RoAura;
public partial class QuestBrowserWindow : Window
{
    private readonly QuestWorkspace _workspace;
    public event Action? HideRequested,UserGeometryChanged;
    public bool IsUserMoving {get;private set;}
    public bool ClosingPermanently {get;set;}
    public QuestBrowserWindow(QuestWorkspace workspace,Func<CharacterSessionSnapshot> observed)
    {
        _workspace=workspace;InitializeComponent();
        Page.Initialize(workspace,observed,()=>{});
        SourceInitialized+=(_,_)=>
        {
            QuestWindowNative.Configure(this,false,false);
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowHook);
        };
        Closing+=(_,e)=> { if(!ClosingPermanently){e.Cancel=true;HideRequested?.Invoke();}else Page.Detach(); };
    }
    public async Task LoadGuideAsync(QuestGuide? guide=null,bool activate=true)
    {
        if(_workspace.Settings.QuestOverlay.AutoLoadCatalog && _workspace.Catalog is null && !_workspace.IsLoadingCatalog) await _workspace.LoadCatalogAsync();
        if(guide is not null) Page.ShowGuide(guide);
        if(activate && IsVisible) {Activate();if(guide is null) Page.FocusSearch();}
    }
    private void Title_Down(object sender,MouseButtonEventArgs e)
    {if(e.ChangedButton==MouseButton.Left){DragMove();UserGeometryChanged?.Invoke();}}
    private void Close_Click(object sender,RoutedEventArgs e)=>HideRequested?.Invoke();
    private IntPtr WindowHook(IntPtr hwnd,int message,IntPtr wp,IntPtr lp,ref bool handled)
    {if(message==0x231)IsUserMoving=true;if(message==0x232){IsUserMoving=false;UserGeometryChanged?.Invoke();}return IntPtr.Zero;}
}
