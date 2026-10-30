using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoAura.Models;

namespace RoAura.Services;

public sealed class SettingsStore
{
    private const string UiUpgradeBackupMarker = "ui-2.0-backup.completed";
    private const string FirstRunPendingMarker = "first-run.pending";
    private const string FirstRunCompletedMarker = "first-run.completed";
    private const int CurrentSettingsVersion = 14;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _rootDirectory;
    private bool _newerVersionCopied;
    public string SettingsPath => Path.Combine(_rootDirectory, "settings.json");
    public string ProfilesDirectory => Path.Combine(_rootDirectory, "Profiles");
    public string ProfileBackupsDirectory => Path.Combine(ProfilesDirectory, "Backups");
    public string RecoveryDirectory => Path.Combine(_rootDirectory, "Recovery");
    public string ActiveProfilePath => Path.Combine(_rootDirectory, "active-profile.txt");
    public string ActiveProfileName { get; private set; } = "Default";
    public bool IsFirstRun { get; private set; }

    /// <summary>
    /// True after settings.json was replaced from a preset snapshot. The
    /// application must not autosave the in-memory settings on exit, otherwise
    /// the restart would start from the old (pre-preset) working copy.
    /// </summary>
    public bool ExitSaveSuppressed { get; private set; }

    /// <summary>
    /// True when <see cref="Load"/> could not read the stored configuration and
    /// returned defaults. While set, every save is refused so the damaged file
    /// is never overwritten until <see cref="AcceptDefaults"/> is called.
    /// </summary>
    public bool LoadFailed { get; private set; }
    public string? LoadError { get; private set; }
    public string? LoadRecoveryPath { get; private set; }
    public string? LastSaveError { get; private set; }

    public SettingsStore() : this(AppEnvironment.RoamingDirectory)
    {
    }

    public SettingsStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Settings directory must not be empty.", nameof(rootDirectory));
        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    public AppSettings Load()
    {
        LoadFailed = false;
        LoadError = null;
        LoadRecoveryPath = null;
        var sourcePath = SettingsPath;
        try
        {
            PrepareFirstRunState();
            EnsureUiUpgradeBackup();
            ActiveProfileName = ReadActiveProfileName();
            // settings.json is the mutable working copy. Preset files are
            // explicit snapshots and must never be changed by autosave.
            if (File.Exists(SettingsPath))
                return LoadFromPath(SettingsPath, checkNewerVersion: true);

            var activeProfilePath = string.IsNullOrWhiteSpace(ActiveProfileName)
                ? null
                : ProfilePath(ActiveProfileName);
            if (activeProfilePath is not null && File.Exists(activeProfilePath))
            {
                sourcePath = activeProfilePath;
                var profile = LoadFromPath(activeProfilePath, checkNewerVersion: true);
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
        catch (Exception ex)
        {
            // Never replace a damaged configuration silently: keep a copy for
            // manual recovery and refuse to autosave until the caller accepts
            // the defaults explicitly.
            LoadFailed = true;
            LoadError = ex.Message;
            DebugLog.Write($"SETTINGS LOAD ERROR ({sourcePath}): {ex}");
            try
            {
                if (File.Exists(sourcePath))
                {
                    Directory.CreateDirectory(RecoveryDirectory);
                    var recoveryPath = Path.Combine(RecoveryDirectory,
                        $"settings.corrupt.{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
                    File.Copy(sourcePath, recoveryPath, overwrite: true);
                    LoadRecoveryPath = recoveryPath;
                }
            }
            catch (Exception copyError)
            {
                DebugLog.Write($"SETTINGS RECOVERY COPY ERROR: {copyError}");
            }
            var fallback = new AppSettings();
            try { Migrate(fallback); }
            catch (Exception migrateError) { DebugLog.Write($"SETTINGS DEFAULTS MIGRATE ERROR: {migrateError}"); }
            return fallback;
        }
    }

    /// <summary>
    /// Accepts the default settings returned after a failed <see cref="Load"/>
    /// and re-enables saving.
    /// </summary>
    public void AcceptDefaults()
    {
        LoadFailed = false;
    }

    private void PrepareFirstRunState()
    {
        Directory.CreateDirectory(RecoveryDirectory);
        var pendingPath = Path.Combine(RecoveryDirectory, FirstRunPendingMarker);
        var completedPath = Path.Combine(RecoveryDirectory, FirstRunCompletedMarker);
        if (File.Exists(completedPath))
        {
            IsFirstRun = false;
            return;
        }
        if (File.Exists(pendingPath))
        {
            IsFirstRun = true;
            return;
        }

        var hasExistingConfiguration = File.Exists(SettingsPath) ||
                                       File.Exists(ActiveProfilePath) ||
                                       Directory.Exists(ProfilesDirectory);
        IsFirstRun = !hasExistingConfiguration;
        AtomicWrite(IsFirstRun ? pendingPath : completedPath, DateTime.UtcNow.ToString("O"));
    }

    public void CompleteFirstRun()
    {
        Directory.CreateDirectory(RecoveryDirectory);
        var pendingPath = Path.Combine(RecoveryDirectory, FirstRunPendingMarker);
        var completedPath = Path.Combine(RecoveryDirectory, FirstRunCompletedMarker);
        AtomicWrite(completedPath, DateTime.UtcNow.ToString("O"));
        if (File.Exists(pendingPath)) File.Delete(pendingPath);
        IsFirstRun = false;
    }

    private void EnsureUiUpgradeBackup()
    {
        try
        {
            Directory.CreateDirectory(RecoveryDirectory);
            var markerPath = Path.Combine(RecoveryDirectory, UiUpgradeBackupMarker);
            if (File.Exists(markerPath)) return;

            var hasExistingConfiguration = File.Exists(SettingsPath) ||
                                           File.Exists(ActiveProfilePath) ||
                                           Directory.Exists(ProfilesDirectory);
            if (!hasExistingConfiguration)
            {
                AtomicWrite(markerPath, DateTime.UtcNow.ToString("O"));
                return;
            }

            var backupDirectory = Path.Combine(RecoveryDirectory,
                $"before-ui-2.0.{DateTime.UtcNow:yyyyMMdd-HHmmssfff}");
            Directory.CreateDirectory(backupDirectory);

            CopyIfExists(SettingsPath, Path.Combine(backupDirectory, "settings.json"));
            CopyIfExists(ActiveProfilePath, Path.Combine(backupDirectory, "active-profile.txt"));
            if (Directory.Exists(ProfilesDirectory))
            {
                foreach (var sourcePath in Directory.EnumerateFiles(ProfilesDirectory, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(ProfilesDirectory, sourcePath);
                    var destinationPath = Path.Combine(backupDirectory, "Profiles", relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    File.Copy(sourcePath, destinationPath, overwrite: false);
                }
            }

            AtomicWrite(markerPath, backupDirectory);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"UI 2.0 BACKUP ERROR: {ex}");
        }
    }

    private static void CopyIfExists(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, overwrite: false);
    }

    public void Save(AppSettings settings)
    {
        if (LoadFailed)
        {
            DebugLog.Write("SETTINGS SAVE SKIPPED: the stored configuration failed to load and defaults were not accepted.");
            return;
        }
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
        if (!AtomicWrite(profilePath, JsonSerializer.Serialize(settings, JsonOptions)))
            throw new IOException($"Could not write preset '{name}': {LastSaveError}");
        WriteActiveProfileName(name);
    }

    public void UpdateProfile(string name, AppSettings settings)
    {
        name = NormalizeProfileName(name);
        var profilePath = ProfilePath(name);
        if (!File.Exists(profilePath))
            throw new FileNotFoundException($"Preset '{name}' does not exist.");
        BackupProfile(name, profilePath);
        if (!AtomicWrite(profilePath, JsonSerializer.Serialize(settings, JsonOptions)))
            throw new IOException($"Could not write preset '{name}': {LastSaveError}");
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
        if (!AtomicWrite(SettingsPath, File.ReadAllText(profilePath), keepBackup: true))
            throw new IOException($"Could not apply preset '{name}': {LastSaveError}");
        WriteActiveProfileName(name);
        // The working copy now holds the preset. The in-memory settings of
        // the running instance are stale and must not be saved on exit.
        ExitSaveSuppressed = true;
    }

    public void ApplyProfileTo(string name, AppSettings target)
    {
        // Deserialize/migrate before touching the live settings. All subsystems keep
        // the same settings and collection references, including global hotkeys.
        var snapshot = LoadFromPath(ProfilePath(name));
        ApplyProfile(name);
        SettingsSnapshot.Apply(snapshot, target);
        ExitSaveSuppressed = false;
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

    private AppSettings LoadFromPath(string path, bool checkNewerVersion = false)
    {
        var result = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions)
                     ?? new AppSettings();
        EnsureCollections(result);
        if (checkNewerVersion) PreserveNewerVersionCopy(path, result.SettingsVersion);
        Migrate(result);
        return result;
    }

    // A hand-edited "Skills": null must not crash Migrate or the UI bindings.
    private static void EnsureCollections(AppSettings settings)
    {
        settings.AllowedProcesses ??= new();
        settings.Skills ??= new();
        settings.CooldownGroups ??= new();
        settings.Effects ??= new();
        settings.Reminders ??= new();
        settings.Equipment ??= new();
        settings.MvpTimers ??= new();
        settings.TocMvpTimers ??= new();
        settings.Characters ??= new();
        settings.QuestOverlay ??= new();
        settings.NavigationSections ??= new();
        settings.EnsureNavigationSections();
        foreach (var skill in settings.Skills) { skill.CharacterScope ??= new(); skill.CharacterScope.Characters ??= new(); }
        foreach (var effect in settings.Effects) { effect.CharacterScope ??= new(); effect.CharacterScope.Characters ??= new(); }
        foreach (var reminder in settings.Reminders) { reminder.CharacterScope ??= new(); reminder.CharacterScope.Characters ??= new(); }
        foreach (var equipment in settings.Equipment) { equipment.CharacterScope ??= new(); equipment.CharacterScope.Characters ??= new(); }
        settings.TocGoldGoblinPriceRules ??= new();
        settings.BuffPanelGroups ??= new();
        foreach (var group in settings.BuffPanelGroups) { group.CharacterScope ??= new(); group.CharacterScope.Characters ??= new(); }

        settings.RewardChannels ??= new();
        settings.RewardGroups ??= new();
        settings.RateIndicators ??= new();
        settings.GoldGoblinPriceRules ??= new();
        settings.MarketWatchRules ??= new();
        settings.MarketWatchNotifications ??= new();
        // Keep one rule per SpringRO item and discard malformed IDs without touching other settings.
        settings.MarketWatchRules = new(settings.MarketWatchRules.Where(r => r is not null && r.ItemId > 0 &&
            (r.WithoutPrice || r.MaximumPrice >= 0)).GroupBy(r => r.ItemId).Select(g => g.Last()));
    }

    // Migrate rewrites SettingsVersion to the current constant. Keep a copy of
    // a file written by a newer RoAura so a downgrade never loses settings.
    private void PreserveNewerVersionCopy(string path, int loadedVersion)
    {
        if (loadedVersion <= CurrentSettingsVersion || _newerVersionCopied) return;
        _newerVersionCopied = true;
        try
        {
            Directory.CreateDirectory(RecoveryDirectory);
            var copyPath = Path.Combine(RecoveryDirectory,
                $"settings.newer.{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            File.Copy(path, copyPath, overwrite: true);
            DebugLog.Write($"SETTINGS VERSION {loadedVersion} is newer than {CurrentSettingsVersion}; copy saved to {copyPath}");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"SETTINGS NEWER VERSION COPY ERROR: {ex}");
        }
    }

    private void SaveWorkingCopy(AppSettings settings)
    {
        if (LoadFailed)
        {
            DebugLog.Write("SETTINGS SAVE SKIPPED: the stored configuration failed to load and defaults were not accepted.");
            return;
        }

        string content;
        try
        {
            content = JsonSerializer.Serialize(settings, JsonOptions);
        }
        catch (Exception ex)
        {
            LastSaveError = ex.Message;
            DebugLog.Write($"SETTINGS SERIALIZE ERROR: {ex}");
            return;
        }
        if (AtomicWrite(SettingsPath, content, keepBackup: true)) LastSaveError = null;
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

    /// <summary>
    /// Writes <paramref name="content"/> durably: the temporary file is flushed
    /// to disk before it replaces the target, and the previous target is kept
    /// as "<c>&lt;name&gt;.bak</c>". Never throws; returns false on failure and
    /// records the error in <see cref="LastSaveError"/>.
    /// </summary>
    private bool AtomicWrite(string path, string content, bool keepBackup = false)
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                AtomicWriteCore(path, content, keepBackup);
                return true;
            }
            catch (IOException ex) when (attempt == 1)
            {
                // Antivirus or a sync client may hold the file for a moment.
                DebugLog.Write($"SETTINGS WRITE RETRY ({path}): {ex.Message}");
                Thread.Sleep(100);
            }
            catch (Exception ex)
            {
                LastSaveError = ex.Message;
                DebugLog.Write($"SETTINGS WRITE ERROR ({path}): {ex}");
                return false;
            }
        }
        return false;
    }

    private static void AtomicWriteCore(string path, string content, bool keepBackup)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            if (keepBackup && File.Exists(path))
            {
                try
                {
                    File.Replace(temporaryPath, path, path + ".bak", ignoreMetadataErrors: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
                {
                    if (!File.Exists(temporaryPath)) throw;
                    File.Move(temporaryPath, path, overwrite: true);
                }
            }
            else
            {
                File.Move(temporaryPath, path, overwrite: true);
            }
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

        foreach (var timer in settings.MvpTimers.Concat(settings.TocMvpTimers))
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
        settings.SettingsVersion = CurrentSettingsVersion;
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
