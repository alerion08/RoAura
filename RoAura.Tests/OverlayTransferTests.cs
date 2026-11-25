using RoAura.Models;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class OverlayTransferTests
{
    [Fact]
    public void EquipmentImportKeepsGroupLayoutAndDoesNotReplaceExistingOverlays()
    {
        var path = Path.GetTempFileName();
        try
        {
            var source = new AppSettings();
            var item = new EquipmentTracker { Name = "Shield", GroupNumber = 3, X = 125, IconImageBase64 = "YWJj" };
            source.Equipment.Add(item);
            source.EquipmentGroupLayouts.Add(3, new(68, 12, Vertical: true));
            source.OverlayPanels.Add("equipment:3", new(Locked: true));
            OverlayTransfer.Write(path, source, OverlayTransferKind.Equipment);

            var target = new AppSettings();
            var existing = new EquipmentTracker { Name = "Hat", GroupNumber = 3 };
            target.Equipment.Add(existing);
            Assert.Equal(1, OverlayTransfer.Read(path, target, OverlayTransferKind.Equipment));
            Assert.Same(existing, target.Equipment[0]);
            var imported = target.Equipment[1];
            Assert.NotEqual(item.Id, imported.Id);
            Assert.NotEqual(existing.GroupNumber, imported.GroupNumber);
            Assert.Equal(item.X, imported.X);
            Assert.Equal(item.IconImageBase64, imported.IconImageBase64);
            Assert.Equal(source.EquipmentGroupLayouts[3], target.EquipmentGroupLayouts[imported.GroupNumber]);
            Assert.True(target.OverlayPanels[$"equipment:{imported.GroupNumber}"].Locked);
            Assert.Throws<InvalidDataException>(() => OverlayTransfer.Read(path, target, OverlayTransferKind.Reminder));
            Assert.Empty(target.Reminders);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void InvalidPackageDoesNotImportAnyRows()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{"FormatVersion":1,"Module":"Effect","Effects":[{},null]}""");
            var settings = new AppSettings();
            Assert.Throws<InvalidDataException>(() => OverlayTransfer.Read(path, settings, OverlayTransferKind.Effect));
            Assert.Empty(settings.Effects);
            Assert.Empty(settings.BuffPanelGroups);
        }
        finally { File.Delete(path); }
    }
}
