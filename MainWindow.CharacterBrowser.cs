using System.Windows;
using System.Windows.Controls;
using RoAura.Services;
using RoAura.Models;

namespace RoAura;

public partial class MainWindow
{
    private bool _characterBrowserInitialized;
    private bool _updatingCharacterBrowser;
    private string _characterBrowserSignature = "";
    private CharacterStatisticsKey? _historySelectedCharacter;
    private int _characterBrowserTick;

    private void InitializeCharacterBrowser()
    {
        if (!AppEnvironment.CharacterSessionsEnabled) return;
        CharacterStatsPanel.Visibility = Visibility.Visible;
        _characterBrowserInitialized = true;
        UpdateCharacterBrowser(force: true);
    }

    private void CharacterBrowseCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_characterBrowserInitialized || _updatingCharacterBrowser) return;
        _historySelectedCharacter = (CharacterBrowseCombo.SelectedItem as ComboBoxItem)?.Tag
            is CharacterStatisticsKey key ? key : null;
        UpdateCharacterBrowserDetails();
    }

    private void UpdateCharacterBrowser(bool force = false)
    {
        if (!_characterBrowserInitialized || Application.Current is not App app) return;
        var session = app.CurrentCharacterSession;
        CharacterPanelTitle.Text = T("Активный персонаж", "Active character");
        CharacterBrowseHintText.Text = T(
            "Ручной выбор — просмотр сохранённой истории. Без подтверждённого подключения игровые пакеты не записываются.",
            "Manual selection views saved history only. Without a verified connection no game packets are recorded.");
        var status = session.Phase switch
        {
            CharacterSessionPhase.ActiveObserved when session.CharacterId is > 0 =>
                T($"Подключён · {CharacterCaption(CharacterStatisticsKey.Create(session.ServerKey, session.CharacterId.Value))}",
                  $"Connected · {CharacterCaption(CharacterStatisticsKey.Create(session.ServerKey, session.CharacterId.Value))}"),
            CharacterSessionPhase.Connecting =>
                T("Подключение к игровому серверу…", "Connecting to the game server…"),
            CharacterSessionPhase.Suspended =>
                T("Персонаж отключён — статистика приостановлена",
                  "Character disconnected — statistics paused"),
            _ => T("Ожидание определения персонажа", "Waiting for character identification")
        };
        if (CharacterStateText.Text != status) CharacterStateText.Text = status;

        if (!force && ++_characterBrowserTick % 5 != 0)
        {
            UpdateCharacterBrowserDetails();
            return;
        }
        RefreshCharacterManager();
        var keys = app.ListSavedCharacters().Where(key => string.Equals(key.ServerKey, Settings.ServerProfile, StringComparison.OrdinalIgnoreCase)).ToList();
        if (_historySelectedCharacter is { } previous && !string.Equals(previous.ServerKey, Settings.ServerProfile, StringComparison.OrdinalIgnoreCase))
            _historySelectedCharacter = null;
        if (session.Phase == CharacterSessionPhase.ActiveObserved && session.CharacterId is > 0 && string.Equals(session.ServerKey, Settings.ServerProfile, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var active = CharacterStatisticsKey.Create(session.ServerKey, session.CharacterId.Value);
                if (!keys.Contains(active)) keys.Add(active);
            }
            catch (ArgumentException) { }
        }
        keys = keys.Distinct().OrderBy(x => x.ServerKey).ThenBy(x => x.CharacterId).ToList();
        var signature = Settings.Language + ":" + Settings.ServerProfile + ":" +
            string.Join(";", keys.Select(CharacterCaption));
        if (signature != _characterBrowserSignature)
        {
            _characterBrowserSignature = signature;
            _updatingCharacterBrowser = true;
            try
            {
                CharacterBrowseCombo.Items.Clear();
                CharacterBrowseCombo.Items.Add(new ComboBoxItem
                {
                    Content = T("Автоматически · активный персонаж", "Automatic · active character")
                });
                foreach (var key in keys)
                    CharacterBrowseCombo.Items.Add(new ComboBoxItem
                    {
                        Content = CharacterCaption(key),
                        Tag = key
                    });
                var found = CharacterBrowseCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
                    item.Tag is CharacterStatisticsKey key && key == _historySelectedCharacter);
                if (_historySelectedCharacter.HasValue && found is null)
                    _historySelectedCharacter = null;
                CharacterBrowseCombo.SelectedItem = found ?? CharacterBrowseCombo.Items[0];
            }
            finally { _updatingCharacterBrowser = false; }
        }
        UpdateCharacterBrowserDetails();
    }

    private void UpdateCharacterBrowserDetails()
    {
        if (!_characterBrowserInitialized || Application.Current is not App app) return;
        var session = app.CurrentCharacterSession;
        CharacterStatisticsKey? selected = _historySelectedCharacter;
        if (!selected.HasValue && session.Phase == CharacterSessionPhase.ActiveObserved &&
            session.CharacterId is > 0)
        {
            try { selected = CharacterStatisticsKey.Create(session.ServerKey, session.CharacterId.Value); }
            catch (ArgumentException) { }
        }
        if (!selected.HasValue)
        {
            CharacterBrowseDetailsText.Text = T("Нет выбранной статистики", "No statistics selected");
            return;
        }
        var snapshot = app.ReadSavedCharacter(selected.Value);
        if (snapshot is null)
        {
            CharacterBrowseDetailsText.Text = T("Статистика ещё не записана",
                "No statistics recorded yet");
            return;
        }
        var mode = _historySelectedCharacter.HasValue
            ? T("История · только просмотр", "History · read-only")
            : T("Автоматически", "Automatic");
        CharacterBrowseDetailsText.Text =
            $"{mode} · Base {snapshot.LifetimeBaseExperience:N0} · Job {snapshot.LifetimeJobExperience:N0} · " +
            $"Zeny {snapshot.LifetimeZenyGained:N0}";
    }
}
