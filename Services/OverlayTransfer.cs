using System.Text.Json;
using RoAura.Models;

namespace RoAura.Services;

internal enum OverlayTransferKind { Cooldown, Effect, Equipment, Reminder }

internal sealed class OverlayTransfer
{
    private const int Version = 1;
    private const int MaxBytes = 64 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public int FormatVersion { get; set; }
    public string Module { get; set; } = "";
    public SkillAura[] Skills { get; set; } = [];
    public TrackedEffect[] Effects { get; set; } = [];
    public EquipmentTracker[] Equipment { get; set; } = [];
    public ReminderItem[] Reminders { get; set; } = [];
    public CooldownGroupSettings[] CooldownGroups { get; set; } = [];
    public BuffPanelGroup[] BuffGroups { get; set; } = [];
    public Dictionary<int, EquipmentGroupLayout> EquipmentGroups { get; set; } = [];
    public Dictionary<string, OverlayPanelPreference> Panels { get; set; } = [];

    internal static void Write(string file, AppSettings settings, OverlayTransferKind kind)
    {
        var prefix = Prefix(kind) + ":";
        var package = new OverlayTransfer
        {
            FormatVersion = Version, Module = kind.ToString(),
            Panels = settings.OverlayPanels.Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal) ||
                (kind == OverlayTransferKind.Effect && pair.Key.StartsWith("buff:", StringComparison.Ordinal)))
                .ToDictionary(pair => pair.Key, pair => pair.Value)
        };
        switch (kind)
        {
            case OverlayTransferKind.Cooldown:
                package.Skills = settings.Skills.ToArray();
                package.CooldownGroups = settings.CooldownGroups.ToArray();
                break;
            case OverlayTransferKind.Effect:
                package.Effects = settings.Effects.ToArray();
                package.BuffGroups = settings.BuffPanelGroups.ToArray();
                break;
            case OverlayTransferKind.Equipment:
                package.Equipment = settings.Equipment.ToArray();
                package.EquipmentGroups = new(settings.EquipmentGroupLayouts);
                break;
            case OverlayTransferKind.Reminder:
                package.Reminders = settings.Reminders.ToArray();
                break;
        }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(package, Json);
        if (bytes.Length > MaxBytes) throw new InvalidDataException();
        File.WriteAllBytes(file, bytes);
    }

    internal static int Read(string file, AppSettings settings, OverlayTransferKind kind)
    {
        using var stream = File.OpenRead(file);
        if (stream.Length > MaxBytes) throw new InvalidDataException();
        var package = JsonSerializer.Deserialize<OverlayTransfer>(stream, Json);
        if (package is null || package.FormatVersion != Version || package.Module != kind.ToString())
            throw new InvalidDataException();

        package.Validate(kind);
        return kind switch
        {
            OverlayTransferKind.Cooldown => package.Add(settings.Skills, row => row.GroupNumber, (row, n) =>
            {
                row.Id = Guid.NewGuid(); row.GroupNumber = n;
                row.IconPath = SafeLocalPath(row.IconPath);
            }, settings.CooldownGroups.Select(group => group.GroupNumber),
                package.CooldownGroups.Select(group => group.GroupNumber), map =>
                {
                    foreach (var group in package.CooldownGroups)
                    {
                        group.GroupNumber = map[group.GroupNumber];
                        settings.CooldownGroups.Add(group);
                    }
                }, settings, kind, package.Skills),
            OverlayTransferKind.Effect => package.Add(settings.Effects, row => row.GroupNumber, (row, n) =>
            {
                row.Id = Guid.NewGuid(); row.GroupNumber = n;
                row.SoundPath = SafeLocalPath(row.SoundPath);
                row.AppearedSoundPath = SafeLocalPath(row.AppearedSoundPath);
                row.DisappearedSoundPath = SafeLocalPath(row.DisappearedSoundPath);
            }, settings.BuffPanelGroups.Select(group => group.GroupNumber),
                package.BuffGroups.Select(group => group.GroupNumber), map =>
                {
                    foreach (var group in package.BuffGroups)
                    {
                        group.GroupNumber = map[group.GroupNumber];
                        settings.BuffPanelGroups.Add(group);
                    }
                }, settings, kind, package.Effects),
            OverlayTransferKind.Equipment => package.Add(settings.Equipment, row => row.GroupNumber, (row, n) =>
            {
                row.Id = Guid.NewGuid(); row.GroupNumber = n;
            }, settings.EquipmentGroupLayouts.Keys, package.EquipmentGroups.Keys, map =>
                {
                    foreach (var (number, layout) in package.EquipmentGroups)
                        settings.EquipmentGroupLayouts.Add(map[number], layout with
                        {
                            IconSize = Math.Clamp(layout.IconSize, 20, 180), Gap = Math.Clamp(layout.Gap, 0, 100)
                        });
                }, settings, kind, package.Equipment),
            _ => package.Add(settings.Reminders, row => row.GroupNumber, (row, n) =>
            {
                row.Id = Guid.NewGuid(); row.GroupNumber = n;
            }, [], [], _ => { }, settings, kind, package.Reminders)
        };
    }

    private void Validate(OverlayTransferKind kind)
    {
        if (Panels is null || Panels.Values.Any(value => value is null)) throw new InvalidDataException();

        bool InvalidScope(CharacterScope? scope) => scope?.Characters is null;
        var valid = kind switch
        {
            OverlayTransferKind.Cooldown => Skills is not null && Skills.All(row =>
                row is not null && row.Name is not null && !InvalidScope(row.CharacterScope)) &&
                CooldownGroups is not null && CooldownGroups.All(row => row is not null) &&
                CooldownGroups.Select(row => row.GroupNumber).Distinct().Count() == CooldownGroups.Length,
            OverlayTransferKind.Effect => Effects is not null && Effects.All(row =>
                row is not null && row.Name is not null && !InvalidScope(row.CharacterScope)) &&
                BuffGroups is not null && BuffGroups.All(row => row is not null && !InvalidScope(row.CharacterScope)) &&
                BuffGroups.Select(row => row.GroupNumber).Distinct().Count() == BuffGroups.Length,
            OverlayTransferKind.Equipment => Equipment is not null && Equipment.All(row =>
                row is not null && row.Name is not null && row.Text is not null && !InvalidScope(row.CharacterScope)) &&
                EquipmentGroups is not null && EquipmentGroups.All(pair =>
                    pair.Key is >= 1 and <= 999 && pair.Value is not null),
            _ => Reminders is not null && Reminders.All(row =>
                row is not null && row.Name is not null && row.Text is not null && !InvalidScope(row.CharacterScope))
        };
        if (!valid) throw new InvalidDataException();
    }

    private int Add<T>(ICollection<T> target, Func<T, int> groupOf, Action<T, int> remap,
        IEnumerable<int> existingGroups, IEnumerable<int> incomingGroups, Action<Dictionary<int, int>> addGroups,
        AppSettings settings, OverlayTransferKind kind, T[] rows) where T : class
    {
        if (rows.Length > 2000) throw new InvalidDataException();

        var incoming = incomingGroups.Concat(rows.Select(groupOf)).Where(number => number != 0).Distinct().ToArray();
        if (incoming.Any(number => number is < 1 or > 999)) throw new InvalidDataException();
        var prefix = Prefix(kind);
        var used = existingGroups.Concat(target.Select(groupOf)).ToHashSet();
        foreach (var key in settings.OverlayPanels.Keys)
        {
            var parts = key.Split(':');
            if (parts.Length == 2 && (parts[0] == prefix || (kind == OverlayTransferKind.Effect && parts[0] == "buff")) &&
                int.TryParse(parts[1], out var reserved))
                used.Add(reserved);
        }
        var map = new Dictionary<int, int> { [0] = 0 };
        foreach (var number in incoming.Where(number => !used.Contains(number)))
            map[number] = number;
        used.UnionWith(map.Values);
        foreach (var number in incoming.Where(number => !map.ContainsKey(number)))
        {
            var available = Enumerable.Range(1, 999).FirstOrDefault(candidate => !used.Contains(candidate));
            if (available == 0) throw new InvalidDataException();
            map[number] = available;
            used.Add(available);
        }

        foreach (var row in rows) remap(row, map[groupOf(row)]);
        addGroups(map);
        foreach (var (key, preference) in Panels)
        {
            var parts = key.Split(':');
            if (parts.Length == 2 && (parts[0] == prefix || (kind == OverlayTransferKind.Effect && parts[0] == "buff")) &&
                int.TryParse(parts[1], out var number) && number > 0 && map.TryGetValue(number, out var remapped))
                settings.OverlayPanels[$"{parts[0]}:{remapped}"] = preference;
        }
        foreach (var row in rows) target.Add(row);
        return rows.Length;
    }

    private static string Prefix(OverlayTransferKind kind) => kind.ToString().ToLowerInvariant();

    private static string SafeLocalPath(string? value) =>
        value is { Length: >= 3 } && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] == '\\'
            ? value : "";
}
