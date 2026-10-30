using System.Text.Json;
using System.Windows;
using System.Windows.Input;
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
        window.PreviewKeyDown+=(_,e)=>
        {
            var key=e.Key==Key.System?e.SystemKey:e.Key;
            if(key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt)
            {if(!report.Modifiers.Contains(key.ToString()))report.Modifiers.Add(key.ToString());}
            else report.Keys++;
            Write();
        };
        window.Closed+=(_,_)=>app.Shutdown(0);
        window.Show();window.Activate();report.Handle=new WindowInteropHelper(window).Handle.ToInt64();Write();
        return true;
    }
}
