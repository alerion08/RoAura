using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "RoAura.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SeparateProfilesKeepIndependentEquipmentSnapshots()
    {
        var store = new SettingsStore(_root);
        var settings = store.Load();
        settings.Equipment.Add(new EquipmentTracker
        {
            Name = "+7 Triple Kill Word of Misery",
            ItemId = 1148,
            InventoryIndex = 10,
            VariantKey = "weapon-with-three-cards"
        });
        store.CreateProfile("Equipment", settings);

        settings.Equipment.Clear();
        store.Save(settings);
        store.CreateProfile("Empty", settings);

        store.ApplyProfile("Equipment");
        var restored = store.Load();

        var item = Assert.Single(restored.Equipment);
        Assert.Equal(1148, item.ItemId);
        Assert.Equal("weapon-with-three-cards", item.VariantKey);
        Assert.Equal("+7 Triple Kill Word of Misery", item.Name);
    }

    [Fact]
    public void SavingWorkingCopyDoesNotModifyStoredProfile()
    {
        var store = new SettingsStore(_root);
        var settings = store.Load();
        settings.PacketServerPort = 5121;
        store.CreateProfile("Server A", settings);

        settings.PacketServerPort = 64906;
        store.Save(settings);
        store.ApplyProfile("Server A");

        Assert.Equal(5121, store.Load().PacketServerPort);
    }

    [Fact]
    public void UpdatingProfileCreatesBackup()
    {
        var store = new SettingsStore(_root);
        var settings = store.Load();
        store.CreateProfile("Backup test", settings);
        settings.PacketServerPort = 6000;

        store.UpdateProfile("Backup test", settings);

        Assert.True(Directory.Exists(store.ProfileBackupsDirectory));
        Assert.NotEmpty(Directory.EnumerateFiles(store.ProfileBackupsDirectory, "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public void InvalidWorkingJsonIsNotOverwrittenDuringLoad()
    {
        Directory.CreateDirectory(_root);
        var settingsPath = Path.Combine(_root, "settings.json");
        const string invalidJson = "{ definitely not valid json";
        File.WriteAllText(settingsPath, invalidJson);

        _ = new SettingsStore(_root).Load();

        Assert.Equal(invalidJson, File.ReadAllText(settingsPath));
    }

    [Fact]
    public void FirstRunGuideIsOnlyPendingForNewInstallation()
    {
        var store = new SettingsStore(_root);
        _ = store.Load();
        Assert.True(store.IsFirstRun);

        store.CompleteFirstRun();
        Assert.False(store.IsFirstRun);

        var restarted = new SettingsStore(_root);
        _ = restarted.Load();
        Assert.False(restarted.IsFirstRun);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
