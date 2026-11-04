using System.Windows;
namespace RoAura.Services;

public static class MvpMapPlacement
{
    public static Rect Find(Rect panel, Size popup, Rect client, double gap = 8)
    {
        var positions = new[]
        {
            new Rect(panel.Right + gap, Math.Clamp(panel.Top, client.Top, Math.Max(client.Top, client.Bottom-popup.Height)), popup.Width,popup.Height),
            new Rect(panel.Left-popup.Width-gap, Math.Clamp(panel.Top, client.Top, Math.Max(client.Top, client.Bottom-popup.Height)), popup.Width,popup.Height),
            new Rect(Math.Clamp(panel.Left,client.Left,Math.Max(client.Left,client.Right-popup.Width)),panel.Bottom+gap,popup.Width,popup.Height),
            new Rect(Math.Clamp(panel.Left,client.Left,Math.Max(client.Left,client.Right-popup.Width)),panel.Top-popup.Height-gap,popup.Width,popup.Height)
        };
        return positions.FirstOrDefault(rect => client.Contains(rect) && !rect.IntersectsWith(panel), Rect.Empty);
    }
}
