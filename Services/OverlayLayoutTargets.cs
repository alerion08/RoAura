using System.Windows;
using RoAura.Models;

namespace RoAura.Services;

/// <summary>Model positions only: opening the editor never rewrites saved coordinates.</summary>
public sealed record OverlayLayoutSize(string Label, Func<double> Read, Action<double> Write, double Step = 2);

public sealed record OverlayLayoutItem(Func<Rect> ReadBounds, Action<Point> SetPosition, OverlayLayoutSize? QuickSize = null);

public sealed class OverlayLayoutTarget(string key, string section, int? group, IReadOnlyList<OverlayLayoutItem> items, string? groupName = null)
{
    public string Key { get; } = key;
    public string Section { get; } = section;
    public int? Group { get; } = group;
    public IReadOnlyList<OverlayLayoutItem> Items { get; } = items;
    public string Caption => LocalizationService.Get(Section) + (Group is int number
        ? " · " + (!string.IsNullOrWhiteSpace(groupName) ? groupName : number == 0 ? LocalizationService.Get("LayoutUngrouped") :
            string.Format(LocalizationService.Get("LayoutGroup"), number)) : "");
    public string? QuickSizeLabel => Items.FirstOrDefault()?.QuickSize?.Label;

    public Action? AdjustSize(int direction)
    {
        var sizes = Items.Select(item => item.QuickSize).OfType<OverlayLayoutSize>()
            .Select(size => (Size: size, Original: size.Read())).ToArray();
        foreach (var (size, original) in sizes)
            size.Write(Math.Round(original + direction * size.Step, 2));
        if (!sizes.Any(entry => entry.Size.Read() != entry.Original)) return null;
        return () =>
        {
            foreach (var (size, original) in sizes) size.Write(original);
        };
    }

    public Rect Bounds => Items.Select(item => item.ReadBounds()).Aggregate(Rect.Empty, Union);
    private static Rect Union(Rect first, Rect next) { first.Union(next); return first; }
    public OverlayLayoutDrag BeginDrag(Size viewport) => new(this, viewport);
}

/// <summary>One common delta keeps every member's spacing, including disabled members.</summary>
public sealed class OverlayLayoutDrag
{
    private readonly OverlayLayoutTarget _target;
    private readonly Rect[] _origins;
    private readonly Rect _bounds;
    private readonly Size _viewport;
    internal OverlayLayoutDrag(OverlayLayoutTarget target, Size viewport)
    {
        _target = target; _viewport = viewport;
        _origins = target.Items.Select(item => item.ReadBounds()).ToArray();
        _bounds = Rect.Empty;
        foreach (var origin in _origins) _bounds.Union(origin);
    }
    public bool HasChanged => _target.Items.Where((item, index) =>
        item.ReadBounds().Location != _origins[index].Location).Any();

    public void Restore()
    {
        for (var index = 0; index < _origins.Length; index++)
            _target.Items[index].SetPosition(_origins[index].Location);
    }

    public void Move(Vector requested)
    {
        if (_bounds.IsEmpty || !double.IsFinite(requested.X) || !double.IsFinite(requested.Y)) return;
        var dx = Math.Clamp(requested.X, -_bounds.Left, Math.Max(-_bounds.Left, _viewport.Width - _bounds.Right));
        var dy = Math.Clamp(requested.Y, -_bounds.Top, Math.Max(-_bounds.Top, _viewport.Height - _bounds.Bottom));
        for (var index = 0; index < _origins.Length; index++)
            _target.Items[index].SetPosition(new Point(_origins[index].X + dx, _origins[index].Y + dy));
    }
    public const double GridStep = 20;

    public void MoveScreen(Point origin, Point current, double scale, bool snap = false)
    {
        if (!double.IsFinite(scale) || scale <= 0 || _bounds.IsEmpty) return;
        var delta = (current - origin) / scale;
        if (snap)
            delta = new Vector(Snap(_bounds.Left + delta.X) - _bounds.Left,
                Snap(_bounds.Top + delta.Y) - _bounds.Top);
        Move(delta);
    }

    private static double Snap(double position) =>
        Math.Round(position / GridStep, MidpointRounding.AwayFromZero) * GridStep;

    public void Align(double? horizontal, double? vertical)
    {
        if (_bounds.IsEmpty) return;
        Move(new Vector(
            horizontal.HasValue ? (_viewport.Width - _bounds.Width) * horizontal.Value - _bounds.Left : 0,
            vertical.HasValue ? (_viewport.Height - _bounds.Height) * vertical.Value - _bounds.Top : 0));
    }
}

public static class OverlayLayoutTargets
{
    private static Rect Rectangle(double x, double y, double width, double height) => new(
        double.IsFinite(x) ? x : 0, double.IsFinite(y) ? y : 0,
        double.IsFinite(width) ? Math.Max(1, width) : 1, double.IsFinite(height) ? Math.Max(1, height) : 1);
    private static OverlayLayoutItem Item(Func<double> x, Func<double> y, Action<double> setX,
        Action<double> setY, Func<Size> size, OverlayLayoutSize quickSize) => new(() =>
        { var s = size(); return Rectangle(x(), y(), s.Width, s.Height); },
        point => { setX(point.X); setY(point.Y); }, quickSize);

    public static IReadOnlyList<OverlayLayoutTarget> Build(AppSettings settings)
    {
        var result = new List<OverlayLayoutTarget>();
        bool Can(AppFeature feature) => FeatureAvailability.CanUse(settings.ServerProfile,
            settings.Enabled && settings.PacketCaptureEnabled, feature);
        void Groups<T>(IEnumerable<T> rows, Func<T, int> number, Func<T, bool> enabled,
            string key, string section, Func<T, OverlayLayoutItem> item)
        {
            foreach (var group in rows.GroupBy(number).Where(group => group.Any(enabled)).OrderBy(group => group.Key))
                result.Add(new($"{key}:{group.Key}", section, group.Key, group.Select(item).ToArray(),
                    key == "cooldown" ? settings.CooldownGroups.FirstOrDefault(row => row.GroupNumber == group.Key)?.Name : null));
        }
        void Single(string key, string section, OverlayLayoutItem item, int? group = null) =>
            result.Add(new(key, section, group, [item]));
        if (Can(AppFeature.Cooldowns))
            Groups(settings.Skills.Where(row => row.CharacterScope.IsAllowed), row => row.GroupNumber, row => row.Enabled,
                "cooldown", "Cooldowns", row => Item(() => row.X, () => row.Y, x => row.X = x, y => row.Y = y,
                    () => new(row.IndividualIconSize, row.IndividualIconSize),
                    new("IconSize", () => row.IndividualIconSize, value => row.IndividualIconSize = value)));
        if (Can(AppFeature.Effects))
        {
            Groups(settings.Effects.Where(row => row.CharacterScope.IsAllowed), row => row.GroupNumber, row => row.Enabled,
                "effect", "Effects", row => Item(() => row.NotificationX, () => row.NotificationY,
                    x => row.NotificationX = x, y => row.NotificationY = y,
                    () => new(Math.Max(row.NotificationIconSize, 220), Math.Max(row.NotificationIconSize, row.NotificationTextSize * 1.5) + 20),
                    new("TextSize", () => row.NotificationTextSize, value => row.NotificationTextSize = value)));
            foreach (var group in settings.BuffPanelGroups.Where(group => group.CharacterScope.IsAllowed &&
                         group.Effects.Any(effect => effect.Enabled && effect.CharacterScope.IsAllowed)))
                Single($"buff:{group.GroupNumber}", "LayoutBuffPanel", Item(() => group.X, () => group.Y,
                    x => group.X = x, y => group.Y = y,
                    () => group.Direction == "Vertical"
                        ? new(group.IconSize, Math.Max(1, group.Effects.Count) * (group.IconSize + group.Gap))
                        : new(Math.Max(1, group.Effects.Count) * (group.IconSize + group.Gap), group.IconSize),
                    new("IconSize", () => group.IconSize, value => group.IconSize = value)), group.GroupNumber);
        }
        if (Can(AppFeature.Reminders))
            Groups(settings.Reminders.Where(row => row.CharacterScope.IsAllowed), row => row.GroupNumber, row => row.Enabled,
                "reminder", "Reminders", row => Item(() => row.X, () => row.Y, x => row.X = x, y => row.Y = y,
                    () => new(Math.Max(row.IconSize, 220), Math.Max(row.IconSize, row.TextSize * 1.5)),
                    new("TextSize", () => row.TextSize, value => row.TextSize = value)));
        if (Can(AppFeature.Equipment))
        {
            Groups(settings.Equipment.Where(row => row.CharacterScope.IsAllowed &&
                    !settings.EquipmentGroupLayouts.ContainsKey(row.GroupNumber)), row => row.GroupNumber, row => row.Enabled,
                "equipment", "Equipment", row => Item(() => row.X, () => row.Y, x => row.X = x, y => row.Y = y,
                    () => new(Math.Max(row.IconSize, 220), Math.Max(row.IconSize, row.TextSize * 1.5)),
                    new("TextSize", () => row.TextSize, value => row.TextSize = value)));
            foreach (var group in settings.Equipment.GroupBy(row => row.GroupNumber))
            {
                if (!settings.EquipmentGroupLayouts.TryGetValue(group.Key, out var layout)) continue;
                var visible = group.Where(row => row.Enabled && row.CharacterScope.IsAllowed).ToArray();
                if (visible.Length == 0) continue;
                var anchor = group.First();
                var height = layout.IconSize + visible.Max(row => row.ShowText ? row.TextSize * 1.5 + 4 : 0);
                var width = Math.Max(layout.IconSize, visible.Max(row => row.ShowText ? row.Text.Length * row.TextSize * .65 : 0));
                var total = visible.Length - 1;
                var bounds = new Size(layout.Vertical ? width : width * visible.Length + total * layout.Gap,
                    layout.Vertical ? height * visible.Length + total * layout.Gap : height);
                Single($"equipment:{group.Key}", "Equipment", Item(() => anchor.X, () => anchor.Y,
                    x => anchor.X = x, y => anchor.Y = y, () => bounds,
                    new("IconSize", () => settings.EquipmentGroupLayouts[group.Key].IconSize,
                        value => settings.EquipmentGroupLayouts[group.Key] = settings.EquipmentGroupLayouts[group.Key]
                            with { IconSize = Math.Clamp(value, 20, 180) })), group.Key);
            }
        }
        if (Can(AppFeature.Statistics))
        {
            Groups(settings.RateIndicators, row => row.GroupNumber, row => row.Enabled,
                "statistics", "LayoutStatistics", row => Item(() => row.X, () => row.Y,
                    x => row.X = x, y => row.Y = y, () => new(240, row.TextSize * 1.5),
                    new("TextSize", () => row.TextSize, value => row.TextSize = value)));
            if (settings.ShowExperienceBars)
                Single("experience-bars", "ExperienceBars", Item(() => settings.ExperienceBarsX, () => settings.ExperienceBarsY,
                    x => settings.ExperienceBarsX = x, y => settings.ExperienceBarsY = y,
                    () => new(settings.ExperienceBarWidth, (settings.ExperienceBarHeight + 18) *
                        (settings.ShowJobExperienceBar ? 2 : 1) + (settings.ShowJobExperienceBar ? 2 : 0)),
                    new("ExperienceBarHeight", () => settings.ExperienceBarHeight, value => settings.ExperienceBarHeight = value)));
            if (settings.RewardsEnabled)
                foreach (var group in settings.RewardGroups.Where(group => settings.RewardChannels.Any(
                             channel => channel.Enabled && channel.GroupNumber == group.GroupNumber)))
                    Single($"reward:{group.GroupNumber}", "LayoutRewards", Item(() => group.X, () => group.Y,
                        x => group.X = x, y => group.Y = y, () => new(260, group.TextSize * 2),
                        new("TextSize", () => group.TextSize, value => group.TextSize = value)), group.GroupNumber);
        }
        if (Can(AppFeature.MvpTimers))
            Single("mvp", "MvpTimers", Item(() => settings.TimersX, () => settings.TimersY,
                x => settings.TimersX = x, y => settings.TimersY = y, () => new(260, settings.TimersTextSize * 1.5 + 20),
                new("TextSize", () => settings.TimersTextSize, value => settings.TimersTextSize = value)));
        if (GameServerProfile.SupportsMarketWatch(settings.ServerProfile) && settings.MarketWatchNotifications.Enabled)
        {
            var market = settings.MarketWatchNotifications;
            Single("market", "Database", Item(() => market.X, () => market.Y,
                x => market.X = x, y => market.Y = y, () => new(280, Math.Max(market.IconSize, market.TextSize * 1.5) + 2 * market.Padding),
                new("TextSize", () => market.TextSize, value => market.TextSize = value)));
        }
        if (settings.QuestOverlay.Enabled)
        {
            var quest = settings.QuestOverlay;
            Single("quest", "Quests", Item(() => quest.X, () => quest.Y,
                x => quest.X = x, y => quest.Y = y, () => new(quest.Width * quest.Scale, 60 * quest.Scale),
                new("QScale", () => quest.Scale, value => quest.Scale = value, .05)));
        }
        return result;
    }
}
