using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace RoAura.Services;

/// <summary>Explicit CI-only fake game process, never a normal app startup path.</summary>
internal static class QuestInputProbe
{
    internal sealed class Report
    {
        public long Handle {get;set;}
        public int Clicks {get;set;}
        public int Keys {get;set;}
        public List<string> Modifiers {get;set;}=[];
    }
    internal static bool TryStart(App app,StartupEventArgs args)
    {
        if(!AppEnvironment.IsSmokeTest || !args.Args.Contains("--quest-input-probe"))return false;
        var path=Environment.GetEnvironmentVariable("ROAURA_QUEST_PROBE_REPORT") ?? throw new InvalidOperationException("Missing isolated input-probe path.");
        var report=new Report();
        var window=new Window {Title="RoAura quest input probe",Width=1100,Height=780,Left=20,Top=20,Background=Brushes.DarkSlateGray};
        void Write()
        {
            var temporary=path+".tmp";File.WriteAllText(temporary,JsonSerializer.Serialize(report));File.Move(temporary,path,true);
        }
        window.PreviewMouseDown+=(_,_)=>{report.Clicks++;Write();};
        var input=new TextBox {Width=280,Height=34,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,0,35)};
        window.Content=input;
        window.Closed+=(_,_)=>app.Shutdown(0);
        window.Show();window.Activate();input.Focus();Keyboard.Focus(input);
        report.Handle=new WindowInteropHelper(window).Handle.ToInt64();
        IntPtr Observe(IntPtr hwnd,int message,IntPtr wp,IntPtr lp,ref bool handled)
        {
            if(message is 0x100 or 0x104)
            {
                var key=wp.ToInt32();
                var modifier=key switch {0x10 or 0xA0 or 0xA1=>"Shift",0x11 or 0xA2 or 0xA3=>"Control",0x12 or 0xA4 or 0xA5=>"Alt",_=>null};
                if(modifier is null)report.Keys++;else if(!report.Modifiers.Contains(modifier))report.Modifiers.Add(modifier);
                Write();
            }
            return IntPtr.Zero;
        }
        HwndSource.FromHwnd(new IntPtr(report.Handle))!.AddHook(Observe);
        Write();
        return true;
    }
}
