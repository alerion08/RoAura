using System.Windows.Controls;
using RoAura.Services;

namespace RoAura;

public partial class OverlayWindow
{
    private ObservedExperienceLevel _observedBaseLevel;
    private ObservedExperienceLevel _observedJobLevel;

    public void ObserveExperienceLevel(bool isJob, ushort level, DateTime nowUtc)
    {
        if (isJob) _observedJobLevel = _observedJobLevel.Observe(level, nowUtc);
        else _observedBaseLevel = _observedBaseLevel.Observe(level, nowUtc);
    }

    private void UpdateExperienceBars(DateTime nowUtc,
        ObservedExperienceLevel baseLevel, ulong baseExperience, ulong baseRequired, double baseRate,
        ObservedExperienceLevel jobLevel, ulong jobExperience, ulong jobRequired, double jobRate)
    {
        if (!_settings.ShowExperienceBars) return;
        SetBar(BaseExperienceBar, "Base", baseLevel, baseExperience, baseRequired, baseRate);
        if (_settings.ShowJobExperienceBar)
            SetBar(JobExperienceBar, "Job", jobLevel, jobExperience, jobRequired, jobRate);

        void SetBar(ContentControl target, string kind, ObservedExperienceLevel level, ulong current,
            ulong required, double rate)
        {
            var elapsed = level.ObservedUtc is { } observed && observed <= nowUtc
                ? FormatElapsed(nowUtc - observed) : "—";
            var levelText = level.Number > 0 ? level.Number.ToString() : "—";
            var hasProgress = required > 0 && current <= required;
            var progress = hasProgress ? (double)current / required : 0;
            var presentation = new ExperienceBarText(
                _settings.Language == "en" ? $"Time this level: {elapsed}" : $"На уровне: {elapsed}",
                $"{kind} EXP/h: {rate:N0}",
                $"{kind} Lv. {levelText}",
                hasProgress ? $"{current:N0} / {required:N0} ({required - current:N0})" : "—",
                hasProgress ? progress.ToString("P1") : "—",
                progress);
            if (!Equals(target.Content, presentation)) target.Content = presentation;
        }
    }

    private sealed record ExperienceBarText(string Time, string Rate, string Level,
        string Experience, string Percent, double Progress);
}
