using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using RoAura.Models;
using RoAura.Services;

namespace RoAura;

public partial class MainWindow : Window
{
    private readonly SettingsStore _store;
    private readonly OverlayWindow _overlay;
    private readonly NpcapPacketCapture _packetCapture;
    private readonly GoldGoblinService _goldGoblin;
    private bool _shutdownStarted;
    private bool _languageReady;
    private bool _serverProfileReady;
    private bool _changingMoveMode;
    private TrackedEffect? _learningEffect;
    private SkillAura? _learningSkill;
    private ReminderItem? _learningReminder;
    private EquipmentTracker? _learningEquipment;
    private DateTime _learningUntil;
    private int _learningGeneration;
    private readonly ObservableCollection<MonsterEntry> _monsters;
    private IReadOnlyDictionary<int, ItemEntry> _items;
    private readonly DispatcherTimer _connectionDetectionTimer;
    private bool _appliedPacketCaptureEnabled;
    private string _appliedPacketServerAddress = "";
    private int _appliedPacketServerPort;

    public ICollectionView MvpMonsterView { get; }
    public ObservableCollection<GoldGoblinLootRow> GoldGoblinRows => _goldGoblin.Rows;
    public ObservableCollection<ItemEntry> GoldGoblinItemSuggestions { get; } = [];

    public AppSettings Settings { get; }

    public MainWindow(AppSettings settings, SettingsStore store, OverlayWindow overlay,
        NpcapPacketCapture packetCapture, GoldGoblinService goldGoblin)
    {
        _goldGoblin = goldGoblin;
        _monsters = new ObservableCollection<MonsterEntry>(MonsterCatalogService.Load(settings.MvpDatabaseVersion));
        _items = ItemCatalogService.Load(settings.LootDatabaseVersion);
        RefreshGoldGoblinItemSuggestions();
        MvpMonsterView = CollectionViewSource.GetDefaultView(_monsters);
        InitializeComponent();
        SourceInitialized += MainWindow_SourceInitialized;
        Settings = settings;
        _store = store;
        _overlay = overlay;
        _packetCapture = packetCapture;
        _goldGoblin.Changed += GoldGoblin_Changed;
        _packetCapture.StatusChanged += OnLearningStatusObserved;
        _packetCapture.SkillUsed += OnLearningSkillObserved;
        _packetCapture.EquipmentChanged += OnLearningEquipmentObserved;
        DataContext = settings;
        Settings.SyncBuffPanelGroups();
        Settings.SyncRewardGroups();
        Settings.SyncRateIndicators();
        LootColorsGroup.DataContext = Settings.RewardChannels.First(channel => channel.Kind == RewardKind.Loot);

        EnabledBox.IsChecked = settings.Enabled;
        ProcessesBox.Text = string.Join(", ", settings.AllowedProcesses);
        if (settings.Skills.Count > 0) SkillsGrid.SelectedIndex = 0;
        if (settings.Effects.Count > 0) EffectsGrid.SelectedIndex = 0;
        if (settings.Reminders.Count > 0) RemindersGrid.SelectedIndex = 0;
        if (settings.Equipment.Count > 0) EquipmentGrid.SelectedIndex = 0;
        if (settings.MvpTimers.Count > 0) MvpTimersGrid.SelectedIndex = 0;
        if (settings.BuffPanelGroups.Count > 0) BuffPanelGroupBox.SelectedIndex = 0;
        if (settings.RewardChannels.Count > 0) RewardChannelsGrid.SelectedIndex = 0;
        if (settings.RewardGroups.Count > 0) RewardGroupBox.SelectedIndex = 0;
        if (settings.RateIndicators.Count > 0) RateIndicatorsGrid.SelectedIndex = 0;
        if (settings.GoldGoblinPriceRules.Count > 0) GoldGoblinRulesGrid.SelectedIndex = 0;
        LanguageBox.SelectedIndex = settings.Language == "en" ? 1 : 0;
        UpdateServerProfileLabels();
        RefreshPresetList(_store.ActiveProfileName);
        CooldownNavButton.IsChecked = true;
        _languageReady = true;
        ServerProfileBox.SelectedIndex = settings.ServerProfile switch
        {
            GameServerProfile.SpringRo => 1,
            GameServerProfile.Other => 2,
            _ => 0
        };
        _serverProfileReady = true;
        ApplyServerProfile(save: false);

        DetectConnection(userInitiated: false, restartCapture: false);
        RememberPacketCaptureConfiguration();
        _connectionDetectionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _connectionDetectionTimer.Tick += (_, _) =>
        {
            if (Settings.AutoDetectConnection)
                DetectConnection(userInitiated: false, restartCapture: true);
        };
        _connectionDetectionTimer.Start();
    }

    private void Navigation_Checked(object sender, RoutedEventArgs e)
    {
        if (MainTabs is null || sender is not RadioButton { Tag: string tag }) return;
        if (int.TryParse(tag, out var index) && index >= 0 && index < MainTabs.Items.Count)
            MainTabs.SelectedIndex = index;
    }

    private void ServerProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_serverProfileReady || ServerProfileBox.SelectedItem is not ComboBoxItem { Tag: string profile })
            return;
        Settings.ServerProfile = profile;
        ApplyServerProfile(save: true);
    }

    private void UpdateServerProfileLabels()
    {
        if (ServerProfileBox.Items.Count > 2 && ServerProfileBox.Items[2] is ComboBoxItem other)
            other.Content = Settings.Language == "en" ? "Other" : "Другой";
    }

    private void ApplyServerProfile(bool save)
    {
        var profile = GameServerProfile.Normalize(Settings.ServerProfile);
        var supportsDatabases = GameServerProfile.SupportsServerDatabases(profile);
        var supportsOverlayDatabase = GameServerProfile.SupportsDatabaseOverlay(profile);
        MvpNavButton.Visibility = supportsDatabases ? Visibility.Visible : Visibility.Collapsed;
        LootNavButton.Visibility = supportsDatabases ? Visibility.Visible : Visibility.Collapsed;
        MvpTab.Visibility = supportsDatabases ? Visibility.Visible : Visibility.Collapsed;
        LootTab.Visibility = supportsDatabases ? Visibility.Visible : Visibility.Collapsed;
        var supportsGoldGoblin = profile == GameServerProfile.SpringRo;
        GoldGoblinNavButton.Visibility = supportsGoldGoblin ? Visibility.Visible : Visibility.Collapsed;
        GoldGoblinTab.Visibility = supportsGoldGoblin ? Visibility.Visible : Visibility.Collapsed;
        DatabaseNavButton.Visibility = supportsOverlayDatabase ? Visibility.Visible : Visibility.Collapsed;
        DatabaseTab.Visibility = supportsOverlayDatabase ? Visibility.Visible : Visibility.Collapsed;
        DatabaseHotkeyControls.Visibility = supportsOverlayDatabase ? Visibility.Visible : Visibility.Collapsed;

        if (!supportsDatabases && MainTabs.SelectedIndex >= 4)
            CooldownNavButton.IsChecked = true;

        if (supportsDatabases)
        {
            var version = profile == GameServerProfile.SpringRo
                ? GameDatabaseVersion.SpringRo
                : GameDatabaseVersion.Rtm;
            Settings.MvpDatabaseVersion = version;
            Settings.LootDatabaseVersion = version;
            _monsters.Clear();
            foreach (var monster in MonsterCatalogService.Load(version)) _monsters.Add(monster);
            MvpMonsterView.Refresh();
            _items = ItemCatalogService.Load(version);
            RefreshGoldGoblinItemSuggestions();
            if (Application.Current is App app) app.ReloadItemCatalog(version);
        }

        if (!supportsOverlayDatabase && Application.Current is App currentApp)
            currentApp.CloseDatabaseOverlay();
        _overlay.RefreshServerProfile();
        if (save) _store.Save(Settings);
    }

    private string T(string ru, string en) => Settings.Language == "en" ? en : ru;

    private void GoldGoblin_Changed()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(GoldGoblin_Changed); return; }
        if (GoldGoblinTotalText is not null)
            GoldGoblinTotalText.Text = $"{_goldGoblin.SessionTotal:N0} Z";
    }

    private void AddGoldGoblinRule_Click(object sender, RoutedEventArgs e)
    {
        var item = ResolveGoldGoblinItem(GoldGoblinRuleItemBox);
        if (item is null)
        {
            SetStatus(T("Предмет не найден в базе SpringRO.", "Item was not found in the SpringRO database."));
            return;
        }
        var existing = Settings.GoldGoblinPriceRules.FirstOrDefault(rule => rule.ItemId == item.Id);
        if (existing is null)
        {
            existing = new GoldGoblinPriceRule { ItemId = item.Id, Name = item.Name };
            Settings.GoldGoblinPriceRules.Add(existing);
        }
        GoldGoblinRulesGrid.SelectedItem = existing;
        _goldGoblin.Reprice();
    }

    private void DeleteGoldGoblinRule_Click(object sender, RoutedEventArgs e)
    {
        if (GoldGoblinRulesGrid.SelectedItem is not GoldGoblinPriceRule rule) return;
        Settings.GoldGoblinPriceRules.Remove(rule);
        _goldGoblin.Reprice();
    }

    private void ResetGoldGoblin_Click(object sender, RoutedEventArgs e) => _goldGoblin.Reset();

    private void AddGoldGoblinLoot_Click(object sender, RoutedEventArgs e)
    {
        var item = ResolveGoldGoblinItem(GoldGoblinLootItemBox);
        if (item is null)
        {
            SetStatus(T("Выберите предмет из предложенного списка.", "Select an item from the suggestion list."));
            return;
        }
        _goldGoblin.AddManualItem(item.Id, item.Name);
        GoldGoblinLootGrid.SelectedItem = GoldGoblinRows.First(row => row.ItemId == item.Id);
    }

    private void DeleteGoldGoblinLoot_Click(object sender, RoutedEventArgs e)
    {
        if (GoldGoblinLootGrid.SelectedItem is GoldGoblinLootRow row) _goldGoblin.Remove(row);
    }

    private void GoldGoblinItemBox_KeyUp(object sender, KeyEventArgs e)
    {
        if (sender is not ComboBox box || e.Key == Key.Escape) return;
        var query = box.Text.Trim();
        box.ItemsSource = GoldGoblinItemSuggestions
            .Where(item => query.Length == 0 || item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Take(100).ToList();
        box.IsDropDownOpen = query.Length > 0;
    }

    private ItemEntry? ResolveGoldGoblinItem(ComboBox box)
    {
        if (box.SelectedItem is ItemEntry selected) return selected;
        var query = box.Text.Trim();
        return _items.Values.FirstOrDefault(item =>
            item.Name.Equals(query, StringComparison.CurrentCultureIgnoreCase));
    }

    private void RefreshGoldGoblinItemSuggestions()
    {
        GoldGoblinItemSuggestions.Clear();
        foreach (var item in _items.Values.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            GoldGoblinItemSuggestions.Add(item);
    }

    private void MoveRateIndicatorUp_Click(object sender, RoutedEventArgs e) => MoveRateIndicator(-1);
    private void MoveRateIndicatorDown_Click(object sender, RoutedEventArgs e) => MoveRateIndicator(1);

    private void MoveRateIndicator(int offset)
    {
        RateIndicatorsGrid.CommitEdit();
        if (RateIndicatorsGrid.SelectedItem is not RateIndicatorSettings selected) return;
        var oldIndex = Settings.RateIndicators.IndexOf(selected);
        var newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= Settings.RateIndicators.Count) return;
        var other = Settings.RateIndicators[newIndex];
        (selected.X, other.X) = (other.X, selected.X);
        (selected.Y, other.Y) = (other.Y, selected.Y);
        Settings.RateIndicators.Move(oldIndex, newIndex);
        RateIndicatorsGrid.SelectedItem = selected;
        _store.Save(Settings);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            UpdateMaximizeButton();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void ToggleMaximizeWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateMaximizeButton();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        UpdateMaximizeButton();
    }

    private void UpdateMaximizeButton()
    {
        if (MaximizeWindowButton is null) return;
        var maximized = WindowState == WindowState.Maximized;
        MaximizeWindowButton.Content = maximized ? "❐" : "□";
        MaximizeWindowButton.ToolTip = maximized
            ? (Settings.Language == "en" ? "Restore" : "Восстановить")
            : (Settings.Language == "en" ? "Maximize" : "Развернуть");
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(MainWindowMessageHook);
    }

    private static IntPtr MainWindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmGetMinMaxInfo = 0x0024;
        if (message != wmGetMinMaxInfo) return IntPtr.Zero;

        var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var monitor = MonitorFromWindow(hwnd, 0x00000002);
        if (monitor != IntPtr.Zero)
        {
            var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                var work = monitorInfo.WorkArea;
                var screen = monitorInfo.MonitorArea;
                info.MaxPosition.X = Math.Abs(work.Left - screen.Left);
                info.MaxPosition.Y = Math.Abs(work.Top - screen.Top);
                info.MaxSize.X = Math.Abs(work.Right - work.Left);
                info.MaxSize.Y = Math.Abs(work.Bottom - work.Top);
                Marshal.StructureToPtr(info, lParam, true);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    public void ReportPacketSkillTrigger(SkillAura skill, ushort skillId, uint serverDelayMs) =>
        SetStatus(T($"{skill.Name}: Skill ID {skillId}, кулдаун {skill.CooldownSeconds:0.##} сек.",
            $"{skill.Name}: Skill ID {skillId}, cooldown {skill.CooldownSeconds:0.##} sec."));

    public void ReportPacketEffectTransition(TrackedEffect effect, RoStatusPacket packet) =>
        SetStatus(packet.Active
            ? T($"{effect.Name}: появился · Status ID {packet.StatusId} · {packet.RemainingMs / 1000d:0.#} сек.",
                $"{effect.Name}: appeared · Status ID {packet.StatusId} · {packet.RemainingMs / 1000d:0.#} sec.")
            : T($"{effect.Name}: исчез · Status ID {packet.StatusId}.",
                $"{effect.Name}: disappeared · Status ID {packet.StatusId}."));

    public void ReportOverlayVisibility(bool visible) => SetStatus(visible
        ? T($"Оверлеи включены ({Settings.OverlaysHotkey}).", $"Overlays enabled ({Settings.OverlaysHotkey}).")
        : T($"Оверлеи скрыты ({Settings.OverlaysHotkey}); отслеживание продолжается.",
            $"Overlays hidden ({Settings.OverlaysHotkey}); tracking continues."));

    public void ReportTimersVisibility(bool visible) => SetStatus(visible
        ? T($"MVP-таймеры показаны ({Settings.TimersHotkey}).", $"MVP timers shown ({Settings.TimersHotkey}).")
        : T($"MVP-таймеры скрыты ({Settings.TimersHotkey}).", $"MVP timers hidden ({Settings.TimersHotkey})."));

    public void ReportPacketEquipment(RoEquipmentPacket packet) => SetStatus(T(
        $"Экипировка: пакет 0x{packet.PacketId:X4}, индекс {packet.InventoryIndex}, Item ID {packet.ItemId}, слот 0x{packet.WearLocation:X}.",
        $"Equipment: packet 0x{packet.PacketId:X4}, index {packet.InventoryIndex}, Item ID {packet.ItemId}, slot 0x{packet.WearLocation:X}."));

    public void HideFromHotkey()
    {
        EndOverlayEditModes();
        SaveSettings();
        Hide();
    }

    public void EndOverlayEditModes()
    {
        _changingMoveMode = true;
        MoveModeButton.IsChecked = false;
        GroupMoveModeButton.IsChecked = false;
        NotificationMoveModeButton.IsChecked = false;
        NotificationGroupMoveModeButton.IsChecked = false;
        ReminderMoveModeButton.IsChecked = false;
        ReminderGroupMoveModeButton.IsChecked = false;
        BuffPanelMoveModeButton.IsChecked = false;
        EquipmentMoveModeButton.IsChecked = false;
        EquipmentGroupMoveModeButton.IsChecked = false;
        TimerMoveModeButton.IsChecked = false;
        RewardMoveModeButton.IsChecked = false;
        RateIndicatorMoveModeButton.IsChecked = false;
        RateIndicatorGroupMoveModeButton.IsChecked = false;
        _changingMoveMode = false;
        _overlay.EndAllEditModes();
        if (Application.Current is App app) app.SetEscapeHotkeyEnabled(false);
        SaveSettings();
    }

    private void SaveSettings(bool persist = true, bool applyPacketCapture = true)
    {
        SkillsGrid.CommitEdit();
        EffectsGrid.CommitEdit();
        RemindersGrid.CommitEdit();
        EquipmentGrid.CommitEdit();
        MvpTimersGrid.CommitEdit();
        RewardChannelsGrid.CommitEdit();
        RateIndicatorsGrid.CommitEdit();
        GoldGoblinRulesGrid.CommitEdit();
        Settings.SyncBuffPanelGroups();
        Settings.SyncRewardGroups();
        Settings.SyncRateIndicators();
        if (BuffPanelGroupBox.SelectedItem is null && Settings.BuffPanelGroups.Count > 0)
            BuffPanelGroupBox.SelectedIndex = 0;
        if (RewardGroupBox.SelectedItem is null && Settings.RewardGroups.Count > 0)
            RewardGroupBox.SelectedIndex = 0;
        Settings.Enabled = EnabledBox.IsChecked == true;
        Settings.AllowedProcesses = ProcessesBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (Settings.AutoDetectConnection)
            DetectConnection(userInitiated: false, restartCapture: false);
        if (persist) _store.Save(Settings);
        if (applyPacketCapture) ApplyPacketCaptureIfChanged();
    }

    private void LootDatabase_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not AppSettings) return;
        _items = ItemCatalogService.Load(Settings.LootDatabaseVersion);
        if (Application.Current is App app) app.ReloadItemCatalog(Settings.LootDatabaseVersion);
        SetStatus(T($"База предметов: {Settings.LootDatabaseVersion} ({_items.Count} записей).",
            $"Item database: {Settings.LootDatabaseVersion} ({_items.Count} entries)."));
    }

    private void MvpDatabase_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not AppSettings) return;
        _monsters.Clear();
        foreach (var monster in MonsterCatalogService.Load(Settings.MvpDatabaseVersion))
            _monsters.Add(monster);
        MvpMonsterView.Refresh();
        SetStatus(T($"База MVP: {Settings.MvpDatabaseVersion} ({_monsters.Count} MVP).",
            $"MVP database: {Settings.MvpDatabaseVersion} ({_monsters.Count} MVPs)."));
    }

    private void CreatePreset_Click(object sender, RoutedEventArgs e)
    {
        var name = NewPresetNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, T("Введите имя нового пресета.", "Enter a name for the new preset."),
                "RoAura", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SaveSettings();
        try
        {
            _store.CreateProfile(name, Settings);
            NewPresetNameBox.Clear();
            RefreshPresetList(_store.ActiveProfileName);
            SetStatus(T($"Создан пресет «{_store.ActiveProfileName}».",
                $"Preset '{_store.ActiveProfileName}' created."));
        }
        catch (InvalidOperationException)
        {
            MessageBox.Show(this,
                T("Пресет с таким именем уже существует. Выберите его и нажмите «Обновить выбранный».",
                    "A preset with this name already exists. Select it and click 'Update selected'."),
                "RoAura", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        if (PresetBox.SelectedItem is not string name)
        {
            MessageBox.Show(this, T("Выберите существующий пресет.", "Select an existing preset."),
                "RoAura", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SaveSettings();
        _store.ApplyProfile(name);
        RestartApplication();
    }

    private void UpdatePreset_Click(object sender, RoutedEventArgs e)
    {
        if (PresetBox.SelectedItem is not string name)
        {
            MessageBox.Show(this, T("Выберите пресет для обновления.", "Select a preset to update."),
                "RoAura", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this,
                T($"Заменить сохранённый пресет «{name}» текущими настройками? Перед заменой будет создана резервная копия.",
                    $"Replace preset '{name}' with the current settings? A backup will be created first."),
                "RoAura", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        SaveSettings();
        _store.UpdateProfile(name, Settings);
        RefreshPresetList(name);
        SetStatus(T($"Пресет «{name}» обновлён.", $"Preset '{name}' updated."));
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (PresetBox.SelectedItem is not string name) return;
        if (MessageBox.Show(this,
                T($"Удалить пресет «{name}»?", $"Delete preset '{name}'?"), "RoAura",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _store.DeleteProfile(name);
        RefreshPresetList();
        SetStatus(T($"Пресет «{name}» удалён. Рабочие настройки не изменены.",
            $"Preset '{name}' deleted. Working settings were not changed."));
    }

    private void RefreshPresetList(string? selectedName = null)
    {
        var profiles = _store.ListProfiles();
        PresetBox.ItemsSource = profiles;
        PresetBox.SelectedItem = profiles.FirstOrDefault(name =>
            string.Equals(name, selectedName, StringComparison.OrdinalIgnoreCase)) ?? profiles.FirstOrDefault();
    }

    private void RestartApplication()
    {
        var executable = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executable))
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
        _shutdownStarted = true;
        Application.Current.Shutdown();
    }

    private void RememberPacketCaptureConfiguration()
    {
        _appliedPacketCaptureEnabled = Settings.PacketCaptureEnabled;
        _appliedPacketServerAddress = Settings.PacketServerAddress;
        _appliedPacketServerPort = Settings.PacketServerPort;
    }

    private bool ApplyPacketCaptureIfChanged(bool force = false)
    {
        var changed = force ||
                      _appliedPacketCaptureEnabled != Settings.PacketCaptureEnabled ||
                      !string.Equals(_appliedPacketServerAddress, Settings.PacketServerAddress,
                          StringComparison.OrdinalIgnoreCase) ||
                      _appliedPacketServerPort != Settings.PacketServerPort;
        if (!changed) return false;

        if (Settings.PacketCaptureEnabled)
        {
            // A new capture generation represents a new packet session. Do not
            // retain an AID or active equipment/effect state learned from the
            // previous TCP connection.
            Settings.PacketPlayerAid = 0;
            if (Application.Current is App app)
                app.ResetPacketSession(resetTrackedState: true);
            _store.Save(Settings);
        }
        RememberPacketCaptureConfiguration();
        _packetCapture.ApplySettings(Settings);
        return true;
    }

    private void AutoDetectConnection_Click(object sender, RoutedEventArgs e)
    {
        Settings.AutoDetectConnection = true;
        DetectConnection(userInitiated: true, restartCapture: true);
    }

    private bool DetectConnection(bool userInitiated, bool restartCapture)
    {
        var processNames = ProcessesBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (processNames.Count == 0) processNames = Settings.AllowedProcesses;

        var connection = TcpConnectionDetector.Detect(processNames,
            Settings.PacketServerAddress, Settings.PacketServerPort);
        if (!connection.HasValue)
        {
            if (userInitiated)
                SetStatus(T("Активное соединение процесса RO не найдено. Запустите игру и войдите персонажем.",
                    "No active RO process connection was found. Start the game and log in to a character."));
            return false;
        }

        var detected = connection.Value;
        var changed = !string.Equals(Settings.PacketServerAddress, detected.RemoteAddress,
                          StringComparison.OrdinalIgnoreCase) ||
                      Settings.PacketServerPort != detected.RemotePort;
        Settings.PacketServerAddress = detected.RemoteAddress;
        Settings.PacketServerPort = detected.RemotePort;

        if (changed)
        {
            DebugLog.Write($"AUTO CONNECTION: process={detected.ProcessName}({detected.ProcessId}); " +
                           $"server={detected.RemoteAddress}:{detected.RemotePort}");
            _store.Save(Settings);
            if (restartCapture) ApplyPacketCaptureIfChanged();
        }

        if (userInitiated)
            SetStatus(T(
                $"Соединение определено: {detected.ProcessName} → {detected.RemoteAddress}:{detected.RemotePort}.",
                $"Connection detected: {detected.ProcessName} → {detected.RemoteAddress}:{detected.RemotePort}."));
        return true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        SetStatus(T("Настройки сохранены.", "Settings saved."));
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var skill = new SkillAura { X = 100 + Settings.Skills.Count * 80, Y = 100 };
        Settings.Skills.Add(skill);
        SkillsGrid.SelectedItem = skill;
        SkillsGrid.ScrollIntoView(skill);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (SkillsGrid.SelectedItem is SkillAura skill) Settings.Skills.Remove(skill);
    }

    private void PickIcon_Click(object sender, RoutedEventArgs e)
    {
        if (SkillsGrid.SelectedItem is not SkillAura skill) { SelectFirst("skill"); return; }
        if (!TryPickImage(out var path)) return;
        ImportImage(path, value => skill.IconImageBase64 = value, value => skill.IconFileName = value);
    }

    private void Test_Click(object sender, RoutedEventArgs e)
    {
        if (SkillsGrid.SelectedItem is not SkillAura skill) { SelectFirst("skill"); return; }
        SkillsGrid.CommitEdit();
        _overlay.TriggerSkill(skill, true);
        SetStatus(T($"Тест кулдауна «{skill.Name}» запущен.", $"Cooldown test for “{skill.Name}” started."));
    }

    private void LearnSkillPacket_Click(object sender, RoutedEventArgs e)
    {
        if (SkillsGrid.SelectedItem is not SkillAura skill) { SelectFirst("skill"); return; }
        if (!EnsureNpcap()) return;
        SkillsGrid.CommitEdit();
        StartLearning();
        _learningSkill = skill;
        SetStatus(T($"Примените «{skill.Name}» в RO в течение 20 секунд.",
            $"Use “{skill.Name}” in RO within 20 seconds."));
        _ = ExpireLearningAsync(skill.Id, "skill", _learningGeneration);
    }

    private void AddEffect_Click(object sender, RoutedEventArgs e)
    {
        var effect = new TrackedEffect();
        Settings.Effects.Add(effect);
        EffectsGrid.SelectedItem = effect;
        EffectsGrid.ScrollIntoView(effect);
    }

    private void DeleteEffect_Click(object sender, RoutedEventArgs e)
    {
        if (EffectsGrid.SelectedItem is TrackedEffect effect)
        {
            Settings.Effects.Remove(effect);
            Settings.SyncBuffPanelGroups();
        }
    }

    private void PickEffectIcon_Click(object sender, RoutedEventArgs e)
    {
        if (EffectsGrid.SelectedItem is not TrackedEffect effect) { SelectFirst("effect"); return; }
        if (!TryPickImage(out var path)) return;
        ImportImage(path, value => effect.TemplateImageBase64 = value, value => effect.TemplateFileName = value);
    }

    private void PickAppearedSound_Click(object sender, RoutedEventArgs e) => PickEffectSound(true);
    private void PickDisappearedSound_Click(object sender, RoutedEventArgs e) => PickEffectSound(false);

    private void PickEffectSound(bool appeared)
    {
        if (EffectsGrid.SelectedItem is not TrackedEffect effect) { SelectFirst("effect"); return; }
        var picker = new OpenFileDialog
        {
            Title = appeared ? T("Выберите звук появления", "Choose appeared sound") : T("Выберите звук исчезновения", "Choose disappeared sound"),
            Filter = T("Аудиофайлы|*.wav;*.mp3;*.wma|Все файлы|*.*", "Audio files|*.wav;*.mp3;*.wma|All files|*.*")
        };
        if (picker.ShowDialog(this) != true) return;
        if (appeared) { effect.AppearedSoundPath = picker.FileName; effect.AppearedSoundEnabled = true; }
        else { effect.DisappearedSoundPath = picker.FileName; effect.DisappearedSoundEnabled = true; }
        SaveSettings();
    }

    private void TestEffectAppeared_Click(object sender, RoutedEventArgs e) => TestEffect(true);
    private void TestEffectDisappeared_Click(object sender, RoutedEventArgs e) => TestEffect(false);

    private void TestEffect(bool appeared)
    {
        if (EffectsGrid.SelectedItem is not TrackedEffect effect) { SelectFirst("effect"); return; }
        EffectsGrid.CommitEdit();
        _overlay.ShowEffectNotification(effect, appeared);
    }

    private void LearnEffectPacket_Click(object sender, RoutedEventArgs e)
    {
        if (EffectsGrid.SelectedItem is not TrackedEffect effect) { SelectFirst("effect"); return; }
        if (!EnsureNpcap()) return;
        EffectsGrid.CommitEdit();
        StartLearning();
        _learningEffect = effect;
        SetStatus(T($"Наложите эффект «{effect.Name}» в RO в течение 20 секунд.",
            $"Apply “{effect.Name}” in RO within 20 seconds."));
        _ = ExpireLearningAsync(effect.Id, "effect", _learningGeneration);
    }

    private void AddReminder_Click(object sender, RoutedEventArgs e)
    {
        var reminder = new ReminderItem { X = 900 + Settings.Reminders.Count * 75, Y = 500 };
        Settings.Reminders.Add(reminder);
        RemindersGrid.SelectedItem = reminder;
        RemindersGrid.ScrollIntoView(reminder);
    }

    private void DeleteReminder_Click(object sender, RoutedEventArgs e)
    {
        if (RemindersGrid.SelectedItem is ReminderItem reminder) Settings.Reminders.Remove(reminder);
    }

    private void PickReminderIcon_Click(object sender, RoutedEventArgs e)
    {
        if (RemindersGrid.SelectedItem is not ReminderItem reminder) { SelectFirst("reminder"); return; }
        if (!TryPickImage(out var path)) return;
        ImportImage(path, value => reminder.IconImageBase64 = value, value => reminder.IconFileName = value);
    }

    private void LearnReminderPacket_Click(object sender, RoutedEventArgs e)
    {
        if (RemindersGrid.SelectedItem is not ReminderItem reminder) { SelectFirst("reminder"); return; }
        if (!EnsureNpcap()) return;
        RemindersGrid.CommitEdit();
        StartLearning();
        _learningReminder = reminder;
        SetStatus(T($"Активируйте статус для «{reminder.Name}» в RO в течение 20 секунд.",
            $"Activate the status for “{reminder.Name}” in RO within 20 seconds."));
        _ = ExpireLearningAsync(reminder.Id, "reminder", _learningGeneration);
    }

    private async void TestReminder_Click(object sender, RoutedEventArgs e)
    {
        if (RemindersGrid.SelectedItem is not ReminderItem reminder) { SelectFirst("reminder"); return; }
        reminder.IsPlacementPreview = true;
        await Task.Delay(2500);
        if (!_overlay.IsBusyEditing) reminder.IsPlacementPreview = false;
    }

    private void StartLearning()
    {
        _learningGeneration++;
        var captureWasEnabled = Settings.PacketCaptureEnabled;
        Settings.PacketCaptureEnabled = true;
        Settings.PacketPlayerAid = 0;
        _learningSkill = null;
        _learningEffect = null;
        _learningReminder = null;
        _learningEquipment = null;
        _learningUntil = DateTime.UtcNow.AddSeconds(20);
        // Learning only observes the already-running packet stream. Restarting
        // every Npcap adapter here caused an unnecessary load spike exactly when
        // the user returned to the game to equip an item.
        if (!captureWasEnabled || !_packetCapture.IsActive)
            ApplyPacketCaptureIfChanged(force: true);
    }

    private void OnLearningStatusObserved(RoStatusPacket packet)
    {
        if (!packet.Active) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (DateTime.UtcNow > _learningUntil) return;
            if (_learningEffect is { } effect)
            {
                effect.StatusId = packet.StatusId;
                effect.IsActive = true;
                effect.CurrentStateText = T("ID определён", "ID learned");
                _learningEffect = null;
                FinishStatusLearning(effect.Name, packet);
            }
            else if (_learningReminder is { } reminder)
            {
                reminder.StatusId = packet.StatusId;
                reminder.IsStatusPresent = true;
                _learningReminder = null;
                FinishStatusLearning(reminder.Name, packet);
            }
        });
    }

    private void FinishStatusLearning(string name, RoStatusPacket packet)
    {
        Settings.PacketPlayerAid = packet.Aid;
        SaveSettings();
        SetStatus(T($"«{name}»: найден Status ID {packet.StatusId}, AID {packet.Aid}.",
            $"“{name}”: learned Status ID {packet.StatusId}, AID {packet.Aid}."));
    }

    private void OnLearningSkillObserved(RoSkillPacket packet)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var skill = _learningSkill;
            if (skill is null || DateTime.UtcNow > _learningUntil) return;
            skill.PacketSkillId = packet.SkillId;
            Settings.PacketPlayerAid = packet.SourceAid;
            _learningSkill = null;
            SaveSettings();
            SetStatus(T($"«{skill.Name}»: найден Skill ID {packet.SkillId}, AID {packet.SourceAid}.",
                $"“{skill.Name}”: learned Skill ID {packet.SkillId}, AID {packet.SourceAid}."));
        });
    }

    private void OnLearningEquipmentObserved(RoEquipmentPacket packet)
    {
        if (!packet.Success || !packet.Equipped) return;
        Dispatcher.BeginInvoke(() =>
        {
            // Inventory snapshots are emitted when the character enters the game.
            // They are useful for restoring overlay state, but they are not an
            // explicit equip action and must never complete the learning wizard.
            if (packet.PacketId is 0x00A4 or 0x0B39 or 0x0B41)
                return;

            var item = _learningEquipment;
            if (item is null || DateTime.UtcNow > _learningUntil) return;

            if (packet.ItemId <= 0)
            {
                // Stop the current attempt before asking for a relog. Previously
                // learning remained armed for the rest of its 20-second window,
                // so the first already-equipped item received during login could
                // overwrite the selected tracker (often with a weapon).
                _learningEquipment = null;
                SetStatus(T(
                    $"«{item.Name}»: Item ID ещё нет в кэше. Обучение остановлено: перезайдите персонажем с запущенной RoAura, дождитесь полной загрузки, затем снова нажмите «Обучить» и наденьте нужную вещь.",
                    $"“{item.Name}”: Item ID is not cached yet. Learning was stopped: relog with RoAura running, wait until the character is fully loaded, then click Learn again and equip the intended item."));
                return;
            }

            // Commit the learned identity atomically only after the inventory
            // cache supplied the complete item variant.
            item.PacketId = packet.PacketId;
            item.InventoryIndex = packet.InventoryIndex;
            item.WearLocation = (int)packet.WearLocation;
            item.IsEquipped = true;
            item.ItemId = packet.ItemId;
            item.VariantKey = packet.VariantKey;
            item.DetectedItemName = EquipmentIdentityFormatter.Describe(packet, _items);
            _learningEquipment = null;
            SaveSettings();
            SetStatus(T(
                $"«{item.Name}»: распознано {item.DetectedItemName}; индекс {packet.InventoryIndex}, слот 0x{packet.WearLocation:X}.",
                $"“{item.Name}”: detected {item.DetectedItemName}; index {packet.InventoryIndex}, slot 0x{packet.WearLocation:X}."));
        });
    }

    private async Task ExpireLearningAsync(Guid id, string kind, int generation)
    {
        await Task.Delay(TimeSpan.FromSeconds(20));
        await Dispatcher.BeginInvoke(() =>
        {
            // A previous 20-second timer must not cancel a newer retry for the
            // same row (a common path after relogging to populate item cache).
            if (generation != _learningGeneration) return;
            var expired = false;
            if (kind == "skill" && _learningSkill?.Id == id) { _learningSkill = null; expired = true; }
            else if (kind == "effect" && _learningEffect?.Id == id) { _learningEffect = null; expired = true; }
            else if (kind == "reminder" && _learningReminder?.Id == id) { _learningReminder = null; expired = true; }
            else if (kind == "equipment" && _learningEquipment?.Id == id) { _learningEquipment = null; expired = true; }
            if (expired) SetStatus(T("ID не обнаружен. Попробуйте обучение ещё раз.",
                "ID was not detected. Try learning again."));
        });
    }

    private void MoveMode_Changed(object sender, RoutedEventArgs e) => HandleMoveToggle((ToggleButton)sender, "skill", false);
    private void GroupMoveMode_Changed(object sender, RoutedEventArgs e) => HandleMoveToggle((ToggleButton)sender, "skill", true);
    private void NotificationMoveMode_Changed(object sender, RoutedEventArgs e) => HandleMoveToggle((ToggleButton)sender, "effect", false);
    private void NotificationGroupMoveMode_Changed(object sender, RoutedEventArgs e) => HandleMoveToggle((ToggleButton)sender, "effect", true);
    private void ReminderMoveMode_Changed(object sender, RoutedEventArgs e) => HandleMoveToggle((ToggleButton)sender, "reminder", false);
    private void ReminderGroupMoveMode_Changed(object sender, RoutedEventArgs e) => HandleMoveToggle((ToggleButton)sender, "reminder", true);
    private void EquipmentMoveMode_Changed(object sender, RoutedEventArgs e) => HandleMoveToggle((ToggleButton)sender, "equipment", false);
    private void EquipmentGroupMoveMode_Changed(object sender, RoutedEventArgs e) => HandleMoveToggle((ToggleButton)sender, "equipment", true);
    private void BuffPanelMoveMode_Changed(object sender, RoutedEventArgs e)
    {
        var button = (ToggleButton)sender;
        if (_changingMoveMode) return;
        if (button.IsChecked != true)
        {
            EndOverlayEditModes();
            return;
        }

        EffectsGrid.CommitEdit();
        Settings.SyncBuffPanelGroups();
        if (BuffPanelGroupBox.SelectedItem is not BuffPanelGroup group)
        {
            _changingMoveMode = true;
            button.IsChecked = false;
            _changingMoveMode = false;
            SetStatus(T("Выберите группу панели эффектов.", "Select a buff panel group."));
            return;
        }

        _changingMoveMode = true;
        foreach (var toggle in AllMoveButtons()) toggle.IsChecked = ReferenceEquals(toggle, button);
        _changingMoveMode = false;
        _overlay.EndAllEditModes();
        _overlay.SetBuffPanelEditMode(group, true);
        if (Application.Current is App app) app.SetEscapeHotkeyEnabled(true);
        SaveSettings();
        Hide();
    }

    private void HandleMoveToggle(ToggleButton button, string kind, bool group)
    {
        if (_changingMoveMode) return;
        if (button.IsChecked != true)
        {
            EndOverlayEditModes();
            return;
        }

        object? selected = kind switch
        {
            "skill" => SkillsGrid.SelectedItem as SkillAura,
            "effect" => EffectsGrid.SelectedItem as TrackedEffect,
            "reminder" => RemindersGrid.SelectedItem as ReminderItem,
            _ => EquipmentGrid.SelectedItem as EquipmentTracker
        };
        if (kind != "skill" && selected is null)
        {
            _changingMoveMode = true;
            button.IsChecked = false;
            _changingMoveMode = false;
            SelectFirst(kind);
            return;
        }

        _changingMoveMode = true;
        foreach (var toggle in AllMoveButtons()) toggle.IsChecked = ReferenceEquals(toggle, button);
        _changingMoveMode = false;
        _overlay.EndAllEditModes();
        if (kind == "skill") _overlay.SetSkillEditMode(true, group);
        else if (kind == "effect") _overlay.SetNotificationEditMode((TrackedEffect)selected!, true, group);
        else if (kind == "reminder") _overlay.SetReminderEditMode((ReminderItem)selected!, true, group);
        else _overlay.SetEquipmentEditMode((EquipmentTracker)selected!, true, group);

        if (Application.Current is App app) app.SetEscapeHotkeyEnabled(true);

        SaveSettings();
        Hide();
    }

    private ToggleButton[] AllMoveButtons() =>
    [
        MoveModeButton, GroupMoveModeButton, NotificationMoveModeButton,
        NotificationGroupMoveModeButton, ReminderMoveModeButton, ReminderGroupMoveModeButton,
        BuffPanelMoveModeButton, EquipmentMoveModeButton, EquipmentGroupMoveModeButton,
        TimerMoveModeButton, RewardMoveModeButton, RateIndicatorMoveModeButton,
        RateIndicatorGroupMoveModeButton
    ];

    private void AddEquipment_Click(object sender, RoutedEventArgs e)
    {
        var item = new EquipmentTracker { X = 1050 + Settings.Equipment.Count * 70, Y = 420 };
        Settings.Equipment.Add(item);
        EquipmentGrid.SelectedItem = item;
        EquipmentGrid.ScrollIntoView(item);
    }

    private void DeleteEquipment_Click(object sender, RoutedEventArgs e)
    {
        if (EquipmentGrid.SelectedItem is EquipmentTracker item) Settings.Equipment.Remove(item);
    }

    private void PickEquipmentIcon_Click(object sender, RoutedEventArgs e)
    {
        if (EquipmentGrid.SelectedItem is not EquipmentTracker item) { SelectFirst("equipment"); return; }
        if (!TryPickImage(out var path)) return;
        ImportImage(path, value => item.IconImageBase64 = value, value => item.IconFileName = value);
    }

    private void LearnEquipment_Click(object sender, RoutedEventArgs e)
    {
        if (EquipmentGrid.SelectedItem is not EquipmentTracker item) { SelectFirst("equipment"); return; }
        if (!EnsureNpcap()) return;
        EquipmentGrid.CommitEdit();
        StartLearning();
        _learningEquipment = item;
        SetStatus(T($"Наденьте «{item.Name}» в RO в течение 20 секунд.",
            $"Equip “{item.Name}” in RO within 20 seconds."));
        _ = ExpireLearningAsync(item.Id, "equipment", _learningGeneration);
    }

    private async void TestEquipment_Click(object sender, RoutedEventArgs e)
    {
        if (EquipmentGrid.SelectedItem is not EquipmentTracker item) { SelectFirst("equipment"); return; }
        item.IsPlacementPreview = true;
        await Task.Delay(2500);
        if (!_overlay.IsBusyEditing) item.IsPlacementPreview = false;
    }

    private void AddMvpTimer_Click(object sender, RoutedEventArgs e)
    {
        var timer = new MvpTimer();
        Settings.MvpTimers.Add(timer);
        MvpTimersGrid.SelectedItem = timer;
        MvpTimersGrid.ScrollIntoView(timer);
    }

    private void DeleteMvpTimer_Click(object sender, RoutedEventArgs e)
    {
        if (MvpTimersGrid.SelectedItem is MvpTimer timer) Settings.MvpTimers.Remove(timer);
    }

    private void RestartMvpTimer_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MvpTimer rowTimer) rowTimer.Restart();
        else if (MvpTimersGrid.SelectedItem is MvpTimer timer) timer.Restart();
        SaveSettings();
    }

    private void ResetMvpTimer_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MvpTimer rowTimer) rowTimer.Reset();
        else if (MvpTimersGrid.SelectedItem is MvpTimer timer) timer.Reset();
        SaveSettings();
    }

    private void PickMvpTimerSound_Click(object sender, RoutedEventArgs e)
    {
        if (MvpTimersGrid.SelectedItem is not MvpTimer timer) { SelectFirst("timer"); return; }
        var picker = new OpenFileDialog
        {
            Title = T("Выберите звук завершения MVP-таймера", "Choose MVP timer completion sound"),
            Filter = T("Аудиофайлы|*.wav;*.mp3;*.wma|Все файлы|*.*", "Audio files|*.wav;*.mp3;*.wma|All files|*.*")
        };
        if (picker.ShowDialog(this) != true) return;
        timer.CompletionSoundPath = picker.FileName;
        timer.CompletionSoundEnabled = true;
        SaveSettings();
    }

    private void TestMvpTimerSound_Click(object sender, RoutedEventArgs e)
    {
        if (MvpTimersGrid.SelectedItem is not MvpTimer timer) { SelectFirst("timer"); return; }
        MvpTimersGrid.CommitEdit();
        _overlay.PlayMvpTimerSound(timer);
    }

    private void MvpMonster_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MvpTimersGrid.SelectedItem is not MvpTimer timer || (sender as ComboBox)?.SelectedItem is not MonsterEntry monster) return;
        timer.MonsterId = monster.Id;
        timer.Name = monster.Name;
    }

    private void MvpMonster_KeyUp(object sender, KeyEventArgs e)
    {
        if (sender is not ComboBox box || e.Key is Key.Up or Key.Down or Key.Enter or Key.Escape or Key.Tab)
            return;

        if (!box.IsDropDownOpen) box.IsDropDownOpen = true;
        var query = box.Text.Trim();
        MvpMonsterView.Filter = value =>
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            if (value is not MonsterEntry monster) return false;
            return monster.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                   monster.NameRu.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                   monster.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);
        };
        MvpMonsterView.Refresh();
    }

    private void MvpMonster_DropDownClosed(object sender, EventArgs e)
    {
        MvpMonsterView.Filter = null;
        MvpMonsterView.Refresh();
    }

    private void TimerMoveMode_Changed(object sender, RoutedEventArgs e)
    {
        var button = (ToggleButton)sender;
        if (_changingMoveMode) return;
        if (button.IsChecked != true) { EndOverlayEditModes(); return; }
        _changingMoveMode = true;
        foreach (var toggle in AllMoveButtons()) toggle.IsChecked = ReferenceEquals(toggle, button);
        _changingMoveMode = false;
        _overlay.EndAllEditModes();
        _overlay.SetTimerPanelEditMode(true);
        if (Application.Current is App app) app.SetEscapeHotkeyEnabled(true);
        SaveSettings();
        Hide();
    }

    private void RewardMoveMode_Changed(object sender, RoutedEventArgs e)
    {
        var button = (ToggleButton)sender;
        if (_changingMoveMode) return;
        if (button.IsChecked != true) { EndOverlayEditModes(); return; }
        RewardChannelsGrid.CommitEdit();
        Settings.SyncRewardGroups();
        if (RewardGroupBox.SelectedItem is not RewardGroupSettings group)
        {
            _changingMoveMode = true;
            button.IsChecked = false;
            _changingMoveMode = false;
            SetStatus(T("Выберите группу уведомлений о наградах.", "Select a reward notification group."));
            return;
        }

        _changingMoveMode = true;
        foreach (var toggle in AllMoveButtons()) toggle.IsChecked = ReferenceEquals(toggle, button);
        _changingMoveMode = false;
        _overlay.EndAllEditModes();
        _overlay.SetRewardGroupEditMode(group, true);
        if (Application.Current is App app) app.SetEscapeHotkeyEnabled(true);
        SaveSettings();
        Hide();
    }

    private void TestRewards_Click(object sender, RoutedEventArgs e)
    {
        RewardChannelsGrid.CommitEdit();
        Settings.SyncRewardGroups();
        if (RewardGroupBox.SelectedItem is not RewardGroupSettings group)
        {
            SetStatus(T("Выберите группу уведомлений о наградах.", "Select a reward notification group."));
            return;
        }
        _overlay.ShowRewardPreview(group);
        SaveSettings();
    }

    private void RateIndicatorMoveMode_Changed(object sender, RoutedEventArgs e) =>
        HandleRateIndicatorMoveToggle((ToggleButton)sender, false);

    private void RateIndicatorGroupMoveMode_Changed(object sender, RoutedEventArgs e) =>
        HandleRateIndicatorMoveToggle((ToggleButton)sender, true);

    private void ResetStatisticsSession_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        _overlay.ResetRateSession();
        SetStatus(T("Сессия статистики сброшена. Новый расчёт начнётся со следующего получения опыта или Zeny.",
            "Statistics session reset. A new calculation starts with the next EXP or Zeny gain."));
    }

    private void HandleRateIndicatorMoveToggle(ToggleButton button, bool groupMode)
    {
        if (_changingMoveMode) return;
        if (button.IsChecked != true) { EndOverlayEditModes(); return; }
        RateIndicatorsGrid.CommitEdit();
        if (RateIndicatorsGrid.SelectedItem is not RateIndicatorSettings indicator)
        {
            _changingMoveMode = true;
            button.IsChecked = false;
            _changingMoveMode = false;
            SetStatus(T("Выберите индикатор статистики.", "Select a statistics indicator."));
            return;
        }

        _changingMoveMode = true;
        foreach (var toggle in AllMoveButtons()) toggle.IsChecked = ReferenceEquals(toggle, button);
        _changingMoveMode = false;
        _overlay.EndAllEditModes();
        _overlay.SetRateIndicatorEditMode(indicator, true, groupMode);
        if (Application.Current is App app) app.SetEscapeHotkeyEnabled(true);
        SaveSettings();
        Hide();
    }

    private void CheckNpcap_Click(object sender, RoutedEventArgs e)
    {
        if (NpcapPacketCapture.IsNpcapInstalled())
        {
            Settings.PacketCaptureStateText = T("Npcap установлен · перезапускаю захват…", "Npcap installed · restarting capture…");
            ApplyPacketCaptureIfChanged(force: true);
            return;
        }
        var dialog = new NpcapRequiredWindow { Owner = this };
        dialog.ShowDialog();
        if (NpcapPacketCapture.IsNpcapInstalled()) ApplyPacketCaptureIfChanged(force: true);
    }

    private bool EnsureNpcap()
    {
        if (NpcapPacketCapture.IsNpcapInstalled()) return true;
        CheckNpcap_Click(this, new RoutedEventArgs());
        return NpcapPacketCapture.IsNpcapInstalled();
    }

    private void ResetPacketAid_Click(object sender, RoutedEventArgs e)
    {
        Settings.PacketPlayerAid = 0;
        if (Application.Current is App app) app.ResetPacketSession();
        SaveSettings();
        SetStatus(T("AID персонажа сброшен.", "Character AID reset."));
    }

    private bool TryPickImage(out string path)
    {
        var picker = new OpenFileDialog
        {
            Title = T("Выберите изображение", "Choose an image"),
            Filter = T("Изображения|*.png;*.bmp;*.jpg;*.jpeg|Все файлы|*.*", "Images|*.png;*.bmp;*.jpg;*.jpeg|All files|*.*")
        };
        path = "";
        if (picker.ShowDialog(this) != true) return false;
        path = picker.FileName;
        return true;
    }

    private void ImportImage(string path, Action<string> setData, Action<string> setName)
    {
        try
        {
            setData(Convert.ToBase64String(File.ReadAllBytes(path)));
            setName(Path.GetFileName(path));
            SaveSettings();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "RoAura", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SelectFirst(string kind) => SetStatus(kind switch
    {
        "skill" => T("Сначала выберите скилл.", "Select a skill first."),
        "effect" => T("Сначала выберите эффект.", "Select an effect first."),
        "reminder" => T("Сначала выберите Reminder.", "Select a Reminder first."),
        "timer" => T("Сначала выберите MVP-таймер.", "Select an MVP timer first."),
        _ => T("Сначала выберите предмет экипировки.", "Select an equipment item first.")
    });

    private void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_languageReady || LanguageBox.SelectedItem is not ComboBoxItem { Tag: string language }) return;
        Settings.Language = language;
        LocalizationService.ApplyLanguage(language);
        UpdateServerProfileLabels();
        SkillsGrid.Items.Refresh();
        EffectsGrid.Items.Refresh();
        RemindersGrid.Items.Refresh();
        EquipmentGrid.Items.Refresh();
        MvpTimersGrid.Items.Refresh();
        SaveSettings();
    }

    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

    private void OpenDatabase_Click(object sender, RoutedEventArgs e) =>
        (Application.Current as App)?.ToggleDatabase();

    private void SocialLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url }) OpenUrl(url);
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "RoAura", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => HideFromHotkey();

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        _shutdownStarted = true;
        Application.Current.Shutdown();
    }

    private void SetStatus(string text) => StatusText.Text = $"{DateTime.Now:HH:mm:ss} · {text}";

    protected override void OnClosing(CancelEventArgs e)
    {
        _connectionDetectionTimer.Stop();
        if (!_shutdownStarted)
        {
            _shutdownStarted = true;
            EndOverlayEditModes();
            SaveSettings();
            Dispatcher.BeginInvoke(() => Application.Current.Shutdown());
        }
        base.OnClosing(e);
    }
}
