using System.Text.Json;
using RoAura.Models;

namespace RoAura.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _rootDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RoAura");
    public string SettingsPath => Path.Combine(_rootDirectory, "settings.json");
    public string ProfilesDirectory => Path.Combine(_rootDirectory, "Profiles");
    public string ProfileBackupsDirectory => Path.Combine(ProfilesDirectory, "Backups");
    public string RecoveryDirectory => Path.Combine(_rootDirectory, "Recovery");
    public string ActiveProfilePath => Path.Combine(_rootDirectory, "active-profile.txt");
    public string ActiveProfileName { get; private set; } = "Default";

    public AppSettings Load()
    {
        try
        {
            ActiveProfileName = ReadActiveProfileName();
            // settings.json is the mutable working copy. Preset files are
            // explicit snapshots and must never be changed by autosave.
            if (File.Exists(SettingsPath))
                return LoadFromPath(SettingsPath);

            var activeProfilePath = string.IsNullOrWhiteSpace(ActiveProfileName)
                ? null
                : ProfilePath(ActiveProfileName);
            if (activeProfilePath is not null && File.Exists(activeProfilePath))
            {
                var profile = LoadFromPath(activeProfilePath);
                SaveWorkingCopy(profile);
                return profile;
            }

            var defaults = new AppSettings();
            Migrate(defaults);
            SaveWorkingCopy(defaults);
            CreateProfile("Default", defaults);
            WriteActiveProfileName("Default");
            return defaults;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        SaveWorkingCopy(settings);
    }

    public IReadOnlyList<string> ListProfiles()
    {
        Directory.CreateDirectory(ProfilesDirectory);
        var names = Directory.EnumerateFiles(ProfilesDirectory, "*.json")
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return names;
    }

    public void CreateProfile(string name, AppSettings settings)
    {
        name = NormalizeProfileName(name);
        Directory.CreateDirectory(ProfilesDirectory);
        var profilePath = ProfilePath(name);
        if (File.Exists(profilePath))
            throw new InvalidOperationException($"Preset '{name}' already exists.");
        AtomicWrite(profilePath, JsonSerializer.Serialize(settings, JsonOptions));
        WriteActiveProfileName(name);
    }

    public void UpdateProfile(string name, AppSettings settings)
    {
        name = NormalizeProfileName(name);
        var profilePath = ProfilePath(name);
        if (!File.Exists(profilePath))
            throw new FileNotFoundException($"Preset '{name}' does not exist.");
        BackupProfile(name, profilePath);
        AtomicWrite(profilePath, JsonSerializer.Serialize(settings, JsonOptions));
        WriteActiveProfileName(name);
    }

    public void ApplyProfile(string name)
    {
        name = NormalizeProfileName(name);
        var profilePath = ProfilePath(name);
        if (!File.Exists(profilePath))
            throw new FileNotFoundException($"Preset '{name}' does not exist.");

        // Validate the complete snapshot before touching the working settings.
        _ = LoadFromPath(profilePath);
        BackupWorkingCopy();
        AtomicWrite(SettingsPath, File.ReadAllText(profilePath));
        WriteActiveProfileName(name);
    }

    public bool DeleteProfile(string name)
    {
        name = NormalizeProfileName(name);
        var path = ProfilePath(name);
        if (!File.Exists(path)) return false;
        BackupProfile(name, path);
        File.Delete(path);
        if (string.Equals(ActiveProfileName, name, StringComparison.OrdinalIgnoreCase))
            WriteActiveProfileName("");
        return true;
    }

    private AppSettings LoadFromPath(string path)
    {
        var result = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions)
                     ?? new AppSettings();
        Migrate(result);
        return result;
    }

    private void SaveWorkingCopy(AppSettings settings)
    {
        AtomicWrite(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private void BackupProfile(string name, string profilePath)
    {
        Directory.CreateDirectory(ProfileBackupsDirectory);
        var backupPath = Path.Combine(ProfileBackupsDirectory,
            $"{NormalizeProfileName(name)}.{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.json");
        File.Copy(profilePath, backupPath, overwrite: false);
        foreach (var oldBackup in Directory.EnumerateFiles(ProfileBackupsDirectory,
                     $"{NormalizeProfileName(name)}.*.json")
                 .OrderByDescending(File.GetLastWriteTimeUtc).Skip(10))
            File.Delete(oldBackup);
    }

    private void BackupWorkingCopy()
    {
        if (!File.Exists(SettingsPath)) return;
        Directory.CreateDirectory(RecoveryDirectory);
        var backupPath = Path.Combine(RecoveryDirectory,
            $"working.{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.json");
        File.Copy(SettingsPath, backupPath, overwrite: false);
        foreach (var oldBackup in Directory.EnumerateFiles(RecoveryDirectory, "working.*.json")
                     .OrderByDescending(File.GetLastWriteTimeUtc).Skip(20))
            File.Delete(oldBackup);
    }

    private void WriteActiveProfileName(string name)
    {
        ActiveProfileName = name;
        AtomicWrite(ActiveProfilePath, name);
    }

    private static void AtomicWrite(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, content);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private string ReadActiveProfileName()
    {
        try
        {
            return File.Exists(ActiveProfilePath)
                ? NormalizeOptionalProfileName(File.ReadAllText(ActiveProfilePath))
                : "";
        }
        catch { return ""; }
    }

    private string ProfilePath(string name) => Path.Combine(ProfilesDirectory, $"{NormalizeProfileName(name)}.json");

    private static string NormalizeProfileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Preset name is required.", nameof(name));
        var normalized = name.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars()) normalized = normalized.Replace(invalid, '_');
        return normalized.Length > 60 ? normalized[..60] : normalized;
    }

    private static string NormalizeOptionalProfileName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "" : NormalizeProfileName(name);

    private static void Migrate(AppSettings settings)
    {
        if (settings.SettingsVersion < 2)
        {
            var firstEffect = settings.Effects.FirstOrDefault();
            if (firstEffect is not null)
            {
                settings.NotificationX = firstEffect.NotificationX;
                settings.NotificationY = firstEffect.NotificationY;
                settings.NotificationSize = firstEffect.NotificationSize;
            }

            foreach (var effect in settings.Effects)
            {
                effect.AppearedSoundEnabled = effect.SoundEnabled;
                effect.AppearedSoundPath = effect.SoundPath;
                effect.DisappearedSoundEnabled = effect.SoundEnabled;
                effect.DisappearedSoundPath = effect.SoundPath;
            }
        }

        if (settings.SettingsVersion < 3)
        {
            foreach (var skill in settings.Skills)
            {
                skill.IndividualIconSize = settings.IconSize;
                skill.ReadyOpacityPercent = settings.IconOpacity * 100;
                skill.CooldownOpacityPercent = settings.IconOpacity * 68;
                skill.GroupNumber = 1;
            }

            var index = 0;
            foreach (var effect in settings.Effects)
            {
                effect.NotificationX = settings.NotificationX;
                effect.NotificationY = settings.NotificationY + index * (settings.NotificationSize + 30);
                effect.NotificationIconSize = settings.NotificationSize;
                effect.NotificationTextSize = Math.Max(16, settings.NotificationSize * 0.42);
                effect.NotificationOpacityPercent = 100;
                effect.GroupNumber = 1;
                effect.ShowNotificationIcon = true;
                index++;
            }
        }

        if (settings.SettingsVersion < 4)
        {
            foreach (var effect in settings.Effects)
                effect.ShowOnBuffPanel = true;
        }

        if (settings.SettingsVersion < 5)
        {
            foreach (var effect in settings.Effects)
            {
                effect.ShowDurationText = true;
                effect.ShowDurationFill = true;
            }
        }

        foreach (var timer in settings.MvpTimers)
        {
            timer.RemainingSeconds = timer.EndsAtUtc.HasValue
                ? Math.Max(0, (timer.EndsAtUtc.Value - DateTime.UtcNow).TotalSeconds)
                : 0;
            if (timer.RemainingSeconds <= 0) timer.EndsAtUtc = null;
        }

        if (settings.SettingsVersion < 11 &&
            (settings.LootDatabaseVersion == GameDatabaseVersion.SpringRo ||
             settings.MvpDatabaseVersion == GameDatabaseVersion.SpringRo))
            settings.ServerProfile = GameServerProfile.SpringRo;

        settings.SyncBuffPanelGroups();
        settings.SyncRewardGroups();
        settings.SyncRateIndicators();
        settings.SettingsVersion = 11;
        settings.ServerProfile = GameServerProfile.Normalize(settings.ServerProfile);
        if (GameServerProfile.SupportsServerDatabases(settings.ServerProfile))
        {
            var version = settings.ServerProfile == GameServerProfile.SpringRo
                ? GameDatabaseVersion.SpringRo
                : GameDatabaseVersion.Rtm;
            settings.LootDatabaseVersion = version;
            settings.MvpDatabaseVersion = version;
        }
        settings.LootDatabaseVersion = GameDatabaseVersion.Normalize(settings.LootDatabaseVersion);
        settings.MvpDatabaseVersion = GameDatabaseVersion.Normalize(settings.MvpDatabaseVersion);

        // Keep known mappings when upgrading from pre-release settings.
        var harvestEffect = settings.Effects.FirstOrDefault(effect =>
            string.Equals(effect.Name?.Trim(), "Harvest", StringComparison.OrdinalIgnoreCase));
        if (harvestEffect is not null && harvestEffect.StatusId == 0)
            harvestEffect.StatusId = 208;

        var harvestSkill = settings.Skills.FirstOrDefault(skill =>
            string.Equals(skill.Name?.Trim(), "Harvest", StringComparison.OrdinalIgnoreCase));
        if (harvestSkill is not null && harvestSkill.PacketSkillId == 0)
            harvestSkill.PacketSkillId = 543;

        // Import old path-based skill icons into settings.json so RoAura no
        // longer depends on the original PNG after the first 1.0 launch.
        foreach (var skill in settings.Skills.Where(skill =>
                     string.IsNullOrWhiteSpace(skill.IconImageBase64) &&
                     !string.IsNullOrWhiteSpace(skill.IconPath) && File.Exists(skill.IconPath)))
        {
            try
            {
                skill.IconImageBase64 = Convert.ToBase64String(File.ReadAllBytes(skill.IconPath));
                skill.IconFileName = Path.GetFileName(skill.IconPath);
                skill.IconPath = "";
            }
            catch
            {
                // A missing or locked cosmetic file must not prevent startup.
            }
        }
    }
}
