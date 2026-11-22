using RoAura.Models;

namespace RoAura.Services;

internal static class MvpTimerRoute
{
    internal static void Refresh(IEnumerable<MvpTimer> timers, DateTime now, AppSettings settings)
    {
        var all = timers.ToArray();
        var route = all.Where(timer => timer.Enabled && timer.EndsAtUtc > now)
            .Select(timer => (Timer: timer, Start: timer.HasRespawnWindow
                ? timer.RespawnWindowOpensAtUtc!.Value : timer.EndsAtUtc!.Value, End: timer.EndsAtUtc!.Value))
            .OrderBy(row => row.Start).ToArray();
        var overlapping = new HashSet<MvpTimer>();
        for (var index = 0; index < route.Length; index++)
        {
            var left = route[index];
            if (left.Start == left.End) continue;
            for (var next = index + 1; next < route.Length && route[next].Start < left.End; next++)
            {
                var right = route[next];
                if (right.Start == right.End) continue;
                overlapping.Add(left.Timer);
                overlapping.Add(right.Timer);
            }
        }

        foreach (var timer in all)
        {
            timer.HasOverlappingRespawn = overlapping.Contains(timer);
            timer.SecondsDisplayThresholdMinutes = settings.TimerSecondsThresholdMinutes;
        }

        var until = now;
        var waiting = TimeSpan.Zero;
        var nextWindows = new List<string>();
        foreach (var row in route)
        {
            var gap = row.Start > until ? row.Start - until : TimeSpan.Zero;
            waiting += gap;
            if (row.End > until) until = row.End;
            if (nextWindows.Count >= 4) continue;
            var opens = row.Start <= now ? LocalizationService.Get("TimerRouteNow") :
                row.Start.ToLocalTime().ToString("g");
            var period = row.Start == row.End ? opens : $"{opens} → {row.End.ToLocalTime():g}";
            var place = string.IsNullOrWhiteSpace(row.Timer.LocationLabel) ? "" : $" · {row.Timer.LocationLabel}";
            nextWindows.Add($"{row.Timer.Name}{place} · {period} · +{FormatWait(gap)}");
        }

        settings.TimerRouteSummary = route.Length == 0 ? "" :
            string.Format(LocalizationService.Get("TimerRouteWait"), FormatWait(waiting)) +
            "\n" + string.Join("\n", nextWindows) +
            (route.Length > nextWindows.Count ? $"\n+{route.Length - nextWindows.Count}" : "");
    }

    private static string FormatWait(TimeSpan duration)
    {
        var minutes = (long)Math.Ceiling(duration.TotalMinutes);
        return $"{minutes / 60:00}:{minutes % 60:00}";
    }
}
