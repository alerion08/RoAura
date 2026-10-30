using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
namespace RoAura.Services;

internal static class QuestWindowNative
{
    internal const int MouseActivate=0x21, NoActivateResult=3;
    [StructLayout(LayoutKind.Sequential)] internal struct RectNative { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct PointNative { public int X,Y; }
    internal static void Configure(Window window,bool passThrough,bool noActivate)
    {
        var hwnd=new WindowInteropHelper(window).EnsureHandle();
        var style=GetWindowLong(hwnd,-20)|0x80;
        style=passThrough?style|0x20:style&~0x20;
        style=noActivate?style|0x08000000:style&~0x08000000;
        SetWindowLong(hwnd,-20,style);
    }
    internal static void Place(Window window,Rect pixels,bool topmost=true)
    {
        var hwnd=new WindowInteropHelper(window).EnsureHandle();
        SetWindowPos(hwnd,topmost?new IntPtr(-1):new IntPtr(-2),(int)Math.Round(pixels.X),(int)Math.Round(pixels.Y),
            Math.Max(1,(int)Math.Round(pixels.Width)),Math.Max(1,(int)Math.Round(pixels.Height)),0x10);
    }
    internal static Rect Bounds(IntPtr hwnd) => GetWindowRect(hwnd,out var r) ? new Rect(r.Left,r.Top,Math.Max(0,r.Right-r.Left),Math.Max(0,r.Bottom-r.Top)) : Rect.Empty;
    internal static bool IsOwnedBy(IntPtr window,IntPtr owner)
    { for(var i=0;i<12 && window!=IntPtr.Zero;i++) {window=GetWindow(window,4);if(window==owner && owner!=IntPtr.Zero)return true;}return false; }
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd,uint command);
    internal static Point Cursor() { GetCursorPos(out var p);return new Point(p.X,p.Y); }
    internal static double Scale(IntPtr hwnd) {var dpi=GetDpiForWindow(hwnd);return dpi>0?dpi/96d:1;}
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PointNative point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out RectNative rect);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd,int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd,int index,int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
}
