using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class App : Application
{
    private SettingsStore? _settingsStore;
    private GlobalInputHook? _inputHook;
    private OverlayWindow? _overlay;
    private MainWindow? _settingsWindow;
    private NpcapPacketCapture? _packetCapture;
    private DatabaseOverlayWindow? _databaseOverlay;
    private readonly Dictionary<Guid, EffectPacketState> _effectStates = [];
    private IReadOnlyDictionary<int, ItemEntry> _itemCatalog = new Dictionary<int, ItemEntry>();
    private ulong _nextBaseExperience;
    private ulong _nextJobExperience;
    private ulong _previousNextBaseExperience;
    private ulong _previousNextJobExperience;
    private DateTime _baseRequirementChangedUtc;
    private DateTime _jobRequirementChangedUtc;
    private long? _lastZeny;
    private DateTime _lastDatabaseToggleUtc = DateTime.MinValue;
    private readonly GitHubUpdateService _updateService = new();
    private SpringRoMarketService? _marketService;
    private GoldGoblinService? _goldGoblin;
    private bool _updatePromptOpen;

    public App()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            DebugLog.Write($"UNHANDLED UI ERROR: {args.Exception}");
            MessageBox.Show(
                $"RoAura encountered an error and must close.\n\n{args.Exception.Message}\n\nLog: {DebugLog.PathName}",
                "RoAura", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(-1);
        };
    }

    public void ResetPacketSession(bool resetTrackedState = false)
    {
        _lastZeny = null;
        _nextBaseExperience = 0;
        _nextJobExperience = 0;
        _previousNextBaseExperience = 0;
        _previousNextJobExperience = 0;
        _overlay?.ResetRateStatistics();

        if (!resetTrackedState || _settingsWindow is null) return;
        _effectStates.Clear();
        foreach (var effect in _settingsWindow.Settings.Effects)
        {
            effect.IsActive = false;
            effect.ApplyDuration(0, 0);
        }
        foreach (var reminder in _settingsWindow.Settings.Reminders)
            reminder.IsStatusPresent = false;
        foreach (var equipment in _settingsWindow.Settings.Equipment)
            equipment.IsEquipped = false;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Ragnarok clients and wrappers such as dgVoodoo use the legacy Direct3D
        // device aggressively. Keeping WPF on the software compositor prevents
        // the transparent overlay from sharing GPU resources with the game and
        // avoids corrupted sprites/textures while equipment windows are updated.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        base.OnStartup(e);

        _settingsStore = new SettingsStore();
        var settings = _settingsStore.Load();
        _itemCatalog = ItemCatalogService.Load(settings.LootDatabaseVersion);
        LocalizationService.ApplyLanguage(settings.Language);

        _packetCapture = new NpcapPacketCapture();
        _packetCapture.StatusChanged += OnPacketStatusChanged;
        _packetCapture.SkillUsed += OnPacketSkillUsed;
        _packetCapture.SkillCooldown += OnPacketSkillCooldown;
        _packetCapture.EquipmentChanged += OnPacketEquipmentChanged;
        _packetCapture.ItemAdded += OnPacketItemAdded;
        _packetCapture.ExperienceChanged += OnPacketExperienceChanged;
        _packetCapture.ProgressChanged += OnPacketProgressChanged;
        _packetCapture.ZenyChanged += OnPacketZenyChanged;
        _packetCapture.StateChanged += state => Dispatcher.BeginInvoke(() =>
            settings.PacketCaptureStateText = state);

        _marketService = new SpringRoMarketService();
        _goldGoblin = new GoldGoblinService(settings, _marketService);
        _overlay = new OverlayWindow(settings, _settingsStore, _goldGoblin);
        _databaseOverlay = new DatabaseOverlayWindow(settings, new GameDatabaseService(), _marketService);
        _databaseOverlay.HideRequested += HideDatabase;
        _settingsWindow = new MainWindow(settings, _settingsStore, _overlay, _packetCapture, _goldGoblin);
        _settingsWindow.Show();
        _overlay.Show();
        _databaseOverlay.PrepareForFirstUse();

        try
        {
            _inputHook = new GlobalInputHook(settings);
            _inputHook.ToggleSettingsPressed += ToggleSettings;
            _inputHook.ToggleOverlaysPressed += ToggleOverlays;
            _inputHook.EscapePressed += ExitOverlayEditMode;
            _inputHook.ToggleTimersPressed += ToggleTimers;
            _inputHook.ToggleDatabasePressed += ToggleDatabase;
            _inputHook.Install();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"F8/F9 HOOK ERROR: {ex}");
            MessageBox.Show(_settingsWindow,
                settings.Language == "en"
                    ? "The hotkey service could not be started."
                    : "Не удалось запустить службу горячих клавиш.",
                "RoAura", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        DebugLog.Write("START RoAura 2.13.1 · Gold Goblin layout and editing update");

        if (settings.PacketCaptureEnabled && !NpcapPacketCapture.IsNpcapInstalled())
        {
            settings.PacketCaptureStateText = settings.Language == "en" ? "Npcap is not installed" : "Npcap не установлен";
            var dialog = new NpcapRequiredWindow { Owner = _settingsWindow };
            dialog.ShowDialog();
        }

        _packetCapture.ApplySettings(settings);
        Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            await CheckForUpdatesAsync(false, _settingsWindow);
        });
    }

    public async Task CheckForUpdatesAsync(bool showUpToDate, Window? owner = null)
    {
        if (_updatePromptOpen) return;
        var english = _settingsWindow?.Settings.Language == "en";
        try
        {
            var update = await _updateService.CheckAsync();
            if (update is null)
            {
                if (showUpToDate)
                    MessageBox.Show(owner ?? _settingsWindow,
                        english ? "You are using the latest version of RoAura." : "У вас установлена актуальная версия RoAura.",
                        "RoAura", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _updatePromptOpen = true;
            try
            {
                var window = new UpdateWindow(update, _updateService, english) { Owner = owner ?? _settingsWindow };
                window.ShowDialog();
            }
            finally { _updatePromptOpen = false; }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"UPDATE CHECK ERROR: {ex}");
            if (showUpToDate)
                MessageBox.Show(owner ?? _settingsWindow,
                    (english ? "Could not check for updates.\n\n" : "Не удалось проверить обновления.\n\n") + ex.Message,
                    "RoAura", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void ReloadItemCatalog(string gameVersion)
    {
        _itemCatalog = ItemCatalogService.Load(gameVersion);
        DebugLog.Write($"ITEM CATALOG switched to {GameDatabaseVersion.Normalize(gameVersion)}; entries={_itemCatalog.Count}");
    }

    private void OnPacketStatusChanged(RoStatusPacket packet) =>
        Dispatcher.BeginInvoke(() => HandlePacketStatusChanged(packet));

    private void HandlePacketStatusChanged(RoStatusPacket packet)
    {
        if (_settingsWindow is null || _overlay is null || !_settingsWindow.Settings.Enabled)
            return;

        var settings = _settingsWindow.Settings;
        var matchingEffects = settings.Effects.Where(effect =>
            effect.Enabled && effect.StatusId > 0 && effect.StatusId == packet.StatusId).ToList();
        var matchingReminders = settings.Reminders.Where(reminder =>
            reminder.Enabled && reminder.StatusId > 0 && reminder.StatusId == packet.StatusId).ToList();
        if (matchingEffects.Count == 0 && matchingReminders.Count == 0) return;

        if (settings.PacketPlayerAid == 0)
        {
            settings.PacketPlayerAid = packet.Aid;
            _settingsStore?.Save(settings);
            DebugLog.Write($"PACKET PLAYER AID LEARNED: {packet.Aid}");
        }
        if (settings.PacketPlayerAid != packet.Aid) return;

        foreach (var reminder in matchingReminders)
            reminder.IsStatusPresent = packet.Active;

        foreach (var effect in matchingEffects)
        {
            if (!_effectStates.TryGetValue(effect.Id, out var state))
            {
                state = new EffectPacketState();
                _effectStates[effect.Id] = state;
            }

            var previous = state.Present;
            effect.IsActive = packet.Active;
            if (packet.Active) effect.ApplyDuration(packet.RemainingMs, packet.TotalMs);
            else effect.ApplyDuration(0, 0);
            if (previous == packet.Active) continue;
            state.Present = packet.Active;

            var durationText = packet.Active && packet.RemainingMs is > 0 and < uint.MaxValue
                ? $" · {packet.RemainingMs / 1000d:0.#} {(settings.Language == "en" ? "sec" : "с")}"
                : "";
            effect.CurrentStateText = packet.Active
                ? (settings.Language == "en" ? $"active{durationText}" : $"активен{durationText}")
                : (settings.Language == "en" ? "missing" : "отсутствует");

            var now = DateTime.UtcNow;
            if (packet.Active && effect.NotifyOnAppeared &&
                now - state.LastAppeared >= TimeSpan.FromSeconds(effect.RepeatProtectionSeconds))
            {
                state.LastAppeared = now;
                _overlay.ShowEffectNotification(effect, true);
            }
            else if (!packet.Active && previous == true && effect.NotifyOnDisappeared &&
                     now - state.LastDisappeared >= TimeSpan.FromSeconds(effect.RepeatProtectionSeconds))
            {
                state.LastDisappeared = now;
                _overlay.ShowEffectNotification(effect, false);
            }

            _settingsWindow.ReportPacketEffectTransition(effect, packet);
            DebugLog.Write($"PACKET EFFECT {effect.Name}: packet=0x{packet.PacketId:X4}; " +
                           $"status={packet.StatusId}; aid={packet.Aid}; active={packet.Active}; " +
                           $"remaining={packet.RemainingMs}");
        }
    }

    private void OnPacketSkillUsed(RoSkillPacket packet) =>
        Dispatcher.BeginInvoke(() => HandlePacketSkill(packet.SkillId, packet.SourceAid,
            packet.DelayMs, "0x07FB"));

    private void OnPacketSkillCooldown(RoSkillCooldownPacket packet) =>
        Dispatcher.BeginInvoke(() => HandlePacketSkill(packet.SkillId, null,
            packet.DelayMs, "0x043D"));

    private void OnPacketEquipmentChanged(RoEquipmentPacket packet) =>
        Dispatcher.BeginInvoke(() => HandlePacketEquipment(packet));

    private void OnPacketItemAdded(RoItemAddedPacket packet) => Dispatcher.BeginInvoke(() =>
    {
        if (_settingsWindow is null || _overlay is null || !_settingsWindow.Settings.Enabled) return;
        var item = _itemCatalog.GetValueOrDefault(packet.ItemId) ?? new ItemEntry
        {
            Id = packet.ItemId,
            Name = $"Item #{packet.ItemId}",
            Type = "Loot"
        };
        _overlay.ShowLootReward(packet.ItemId, item.Name, item.Type, packet.Count);
        if (_settingsWindow.Settings.ServerProfile == GameServerProfile.SpringRo)
            _goldGoblin?.AddLoot(packet.ItemId, item.Name, packet.Count);
        DebugLog.Write($"PACKET LOOT item={packet.ItemId}; name={item.Name}; type={item.Type}; count={packet.Count}");
    });

    private void OnPacketProgressChanged(RoProgressPacket packet) => Dispatcher.BeginInvoke(() =>
    {
        if (packet.Parameter == 22)
        {
            if (_nextBaseExperience > 0 && _nextBaseExperience != packet.Value)
            {
                _previousNextBaseExperience = _nextBaseExperience;
                _baseRequirementChangedUtc = DateTime.UtcNow;
            }
            _nextBaseExperience = packet.Value;
            _overlay?.SetNextBaseExperience(packet.Value);
        }
        else if (packet.Parameter == 23)
        {
            if (_nextJobExperience > 0 && _nextJobExperience != packet.Value)
            {
                _previousNextJobExperience = _nextJobExperience;
                _jobRequirementChangedUtc = DateTime.UtcNow;
            }
            _nextJobExperience = packet.Value;
            _overlay?.SetNextJobExperience(packet.Value);
        }
        else if (packet.Parameter == 1)
        {
            _overlay?.SetCurrentBaseExperience(packet.Value);
        }
        else if (packet.Parameter == 2)
        {
            _overlay?.SetCurrentJobExperience(packet.Value);
        }
    });

    private void OnPacketExperienceChanged(RoExperiencePacket packet) => Dispatcher.BeginInvoke(() =>
    {
        if (_settingsWindow is null || _overlay is null || !_settingsWindow.Settings.Enabled || packet.Amount <= 0)
            return;
        var settings = _settingsWindow.Settings;
        if (settings.PacketPlayerAid != 0 && settings.PacketPlayerAid != packet.Aid) return;
        if (settings.PacketPlayerAid == 0)
        {
            settings.PacketPlayerAid = packet.Aid;
            _settingsStore?.Save(settings);
        }

        var isJob = packet.ExperienceType == 2;
        if (isJob) _overlay.RecordJobExperience(packet.Amount);
        else _overlay.RecordBaseExperience(packet.Amount);
        if (isJob && !settings.ShowJobExperience) return;
        var now = DateTime.UtcNow;
        var usePreviousJobRequirement = isJob && _previousNextJobExperience > 0 &&
                                        now - _jobRequirementChangedUtc < TimeSpan.FromSeconds(2);
        var usePreviousBaseRequirement = !isJob && _previousNextBaseExperience > 0 &&
                                         now - _baseRequirementChangedUtc < TimeSpan.FromSeconds(2);
        var required = isJob
            ? (usePreviousJobRequirement ? _previousNextJobExperience : _nextJobExperience)
            : (usePreviousBaseRequirement ? _previousNextBaseExperience : _nextBaseExperience);
        if (usePreviousJobRequirement) _previousNextJobExperience = 0;
        if (usePreviousBaseRequirement) _previousNextBaseExperience = 0;
        var percentage = required > 0 ? packet.Amount / (double)required * 100d : -1d;
        _overlay.ShowExperienceReward(packet.Amount, percentage, isJob);
        DebugLog.Write($"PACKET EXP aid={packet.Aid}; type={packet.ExperienceType}; " +
                       $"amount={packet.Amount}; required={required}; percent={(percentage >= 0 ? percentage.ToString("0.####") : "unknown")}");
    });

    private void OnPacketZenyChanged(RoZenyPacket packet) => Dispatcher.BeginInvoke(() =>
    {
        if (_settingsWindow is null || _overlay is null || !_settingsWindow.Settings.Enabled) return;
        if (!_lastZeny.HasValue)
        {
            _lastZeny = packet.Total;
            return;
        }

        var gained = packet.Total - _lastZeny.Value;
        _lastZeny = packet.Total;
        if (gained <= 0) return;
        _overlay.ShowZenyReward(gained);
        _overlay.RecordZeny(gained);
        DebugLog.Write($"PACKET ZENY gained={gained}; total={packet.Total}");
    });

    private void HandlePacketEquipment(RoEquipmentPacket packet)
    {
        if (_settingsWindow is null || !_settingsWindow.Settings.Enabled || !packet.Success) return;

        // Equip acknowledgements do not contain Item ID. If the inventory cache
        // could not resolve the index, treating the packet as authoritative can
        // hide another item in the same slot or match a false byte-stream
        // candidate. Wait for a fully identified event instead.
        if ((packet.PacketId is 0x00AA or 0x00AC or 0x0999 or 0x099A) && packet.ItemId <= 0)
        {
            DebugLog.Write($"PACKET EQUIPMENT IGNORED: unresolved packet=0x{packet.PacketId:X4}; " +
                           $"index={packet.InventoryIndex}; location=0x{packet.WearLocation:X}");
            return;
        }
        var settings = _settingsWindow.Settings;

        var matchedItems = FindMatchingEquipmentItems(settings.Equipment, packet);

        if (packet.Equipped && packet.WearLocation != 0)
        {
            // Clear the previous tracked item only after the incoming item has
            // been fully identified. A tracked target is selected by the
            // priority matcher above; a genuinely untracked equipped item still
            // correctly leaves the tracked slot empty.
            foreach (var other in settings.Equipment.Where(item => item.Enabled && item.IsEquipped &&
                         ((uint)item.WearLocation & packet.WearLocation) != 0 &&
                         !matchedItems.Contains(item)))
                other.IsEquipped = false;
        }

        foreach (var item in matchedItems)
        {
            item.IsEquipped = packet.Equipped;
            if (packet.ItemId > 0)
            {
                item.ItemId = packet.ItemId;
                if (string.IsNullOrWhiteSpace(item.VariantKey))
                    item.VariantKey = packet.VariantKey;
                item.DetectedItemName = EquipmentIdentityFormatter.Describe(packet, _itemCatalog);
            }
            item.InventoryIndex = packet.InventoryIndex;
            item.WearLocation = (int)packet.WearLocation;
            item.PacketId = packet.PacketId;
        }

        if (matchedItems.Count == 0)
        {
            DebugLog.Write($"PACKET EQUIPMENT UNMATCHED: packet=0x{packet.PacketId:X4}; " +
                           $"index={packet.InventoryIndex}; item={packet.ItemId}; " +
                           $"variant={packet.VariantKey}; location=0x{packet.WearLocation:X}; " +
                           $"equipped={packet.Equipped}");
            return;
        }
        _settingsWindow.ReportPacketEquipment(packet);
        DebugLog.Write($"PACKET EQUIPMENT packet=0x{packet.PacketId:X4}; index={packet.InventoryIndex}; " +
                       $"item={packet.ItemId}; refine={packet.RefineLevel}; grade={packet.Grade}; " +
                       $"cards={packet.Card1},{packet.Card2},{packet.Card3},{packet.Card4}; " +
                       $"location=0x{packet.WearLocation:X}; equipped={packet.Equipped}");
    }

    private static List<EquipmentTracker> FindMatchingEquipmentItems(
        IEnumerable<EquipmentTracker> equipment, RoEquipmentPacket packet)
    {
        var packetHasVariant = !string.IsNullOrWhiteSpace(packet.VariantKey);
        var enabled = equipment.Where(item => item.Enabled).ToList();

        // Exact fingerprint is authoritative across relogs and inventory-index
        // changes. It differentiates items with the same base ID but different
        // refinement, cards, grade or random options.
        if (packetHasVariant)
        {
            var exact = enabled.Where(item =>
                !string.IsNullOrWhiteSpace(item.VariantKey) &&
                string.Equals(item.VariantKey, packet.VariantKey,
                    StringComparison.Ordinal)).ToList();
            if (exact.Count > 0) return exact;
        }

        // During the current character session the inventory index uniquely
        // identifies the concrete item. Use it as a fallback when two packet
        // layouts encode the same fingerprint differently.
        var byIndex = enabled.Where(item =>
            item.InventoryIndex > 0 && item.InventoryIndex == packet.InventoryIndex &&
            (item.ItemId <= 0 || packet.ItemId <= 0 || item.ItemId == packet.ItemId)).ToList();
        if (byIndex.Count > 0) return byIndex;

        // Base Item ID is safe only for legacy/incomplete entries without a
        // learned fingerprint. Never merge two card/refine variants by Item ID.
        return enabled.Where(item =>
            string.IsNullOrWhiteSpace(item.VariantKey) && packet.ItemId > 0 &&
            item.ItemId > 0 && item.ItemId == packet.ItemId).ToList();
    }

    private void HandlePacketSkill(ushort skillId, uint? sourceAid, uint delayMs,
        string packetName)
    {
        if (_settingsWindow is null || _overlay is null || !_settingsWindow.Settings.Enabled)
            return;

        var settings = _settingsWindow.Settings;
        if (sourceAid.HasValue && settings.PacketPlayerAid != 0 &&
            settings.PacketPlayerAid != sourceAid.Value)
            return;

        var matchingSkills = settings.Skills.Where(skill =>
            skill.Enabled && skill.PacketSkillId > 0 && skill.PacketSkillId == skillId).ToList();
        if (matchingSkills.Count == 0) return;

        if (sourceAid.HasValue && settings.PacketPlayerAid == 0)
        {
            settings.PacketPlayerAid = sourceAid.Value;
            _settingsStore?.Save(settings);
        }

        foreach (var skill in matchingSkills)
        {
            if (!_overlay.TriggerSkill(skill)) continue;
            _settingsWindow.ReportPacketSkillTrigger(skill, skillId, delayMs);
            DebugLog.Write($"PACKET COOLDOWN {skill.Name}: packet={packetName}; " +
                           $"skill={skillId}; aid={sourceAid}; serverDelay={delayMs}");
        }
    }

    private void ToggleSettings()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_settingsWindow is null) return;
            if (_overlay?.IsBusyEditing == true)
            {
                ExitOverlayEditMode();
                return;
            }
            if (_settingsWindow.IsVisible)
                _settingsWindow.HideFromHotkey();
            else
            {
                _settingsWindow.Show();
                _settingsWindow.Activate();
            }
        });
    }

    public bool SetEscapeHotkeyEnabled(bool enabled) =>
        _inputHook?.SetEscapeEnabled(enabled) ?? false;

    private void ExitOverlayEditMode()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_settingsWindow is null) return;
            _settingsWindow.EndOverlayEditModes();
            _settingsWindow.Show();
            _settingsWindow.Activate();
        });
    }

    private void ToggleOverlays()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_overlay is null) return;
            if (_overlay.IsBusyEditing)
            {
                ExitOverlayEditMode();
                return;
            }
            _settingsWindow?.EndOverlayEditModes();
            var visible = !_overlay.AreOverlaysVisible;
            _overlay.SetOverlaysVisible(visible);
            _settingsWindow?.ReportOverlayVisibility(visible);
        });
    }

    private void ToggleTimers()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_settingsWindow is null) return;
            var settings = _settingsWindow.Settings;
            if (!GameServerProfile.SupportsServerDatabases(settings.ServerProfile)) return;
            settings.TimersOverlayVisible = !settings.TimersOverlayVisible;
            _settingsStore?.Save(settings);
            _settingsWindow.ReportTimersVisibility(settings.TimersOverlayVisible);
        });
    }

    public void ToggleDatabase()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_databaseOverlay is null) return;
            var now = DateTime.UtcNow;
            if (now - _lastDatabaseToggleUtc < TimeSpan.FromMilliseconds(450)) return;
            _lastDatabaseToggleUtc = now;
            if (_settingsWindow is null ||
                !GameServerProfile.SupportsDatabaseOverlay(_settingsWindow.Settings.ServerProfile))
                return;
            if (_databaseOverlay.IsDatabaseOpen)
            {
                HideDatabase();
            }
            else
            {
                _inputHook?.SetKeyboardBlocked(true);
                _databaseOverlay.ShowForGame();
            }
        });
    }

    private void HideDatabase()
    {
        _databaseOverlay?.HideDatabase();
        _inputHook?.SetKeyboardBlocked(false);
        if (ForegroundProcess.TryActivateWindow(
                _settingsWindow?.Settings.AllowedProcesses ?? [], out _)) { }
    }

    public void CloseDatabaseOverlay()
    {
        if (_databaseOverlay?.IsDatabaseOpen == true) HideDatabase();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _packetCapture?.Dispose();
        _inputHook?.SetKeyboardBlocked(false);
        _inputHook?.Dispose();
        _settingsStore?.Save(_settingsWindow?.Settings ?? new AppSettings());
        base.OnExit(e);
    }

    private sealed class EffectPacketState
    {
        public bool? Present { get; set; }
        public DateTime LastAppeared { get; set; }
        public DateTime LastDisappeared { get; set; }
    }
}
