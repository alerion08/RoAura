using System.Windows;
namespace RoAura.Services;

public static class ClipboardService
{
    public static bool TryCopy(string text)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try { Clipboard.SetText(text); return true; }
            catch (Exception ex)
            {
                if (attempt == 3) { DebugLog.Write($"CLIPBOARD ERROR: {ex}"); return false; }
                Thread.Sleep(50);
            }
        }
        return false;
    }
}
