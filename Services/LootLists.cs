using RoAura.Models;

namespace RoAura.Services;

internal static class LootLists
{
    internal static IEnumerable<LootList> ForServer(AppSettings settings, string server)
    {
        var profile = GameServerProfile.Normalize(server);
        return settings.LootLists.Where(list => GameServerProfile.Normalize(list.Server) == profile);
    }

    internal static bool AddItem(LootList list, int itemId, string name)
    {
        if (itemId <= 0 || list.Items.Any(row => row.ItemId == itemId)) return false;
        list.Items.Add(new LootListItem { ItemId = itemId, Name = name });
        return true;
    }

    internal static bool MoveItem(LootList source, LootList destination, LootListItem item)
    {
        if (source == destination || !source.Items.Contains(item) ||
            GameServerProfile.Normalize(source.Server) != GameServerProfile.Normalize(destination.Server) ||
            destination.Items.Any(row => row.ItemId == item.ItemId)) return false;
        source.Items.Remove(item);
        destination.Items.Add(item);
        return true;
    }

    internal static bool RecordPickup(AppSettings settings, string server, int itemId, int count)
    {
        if (count <= 0) return false;
        var changed = false;
        foreach (var item in ForServer(settings, server).SelectMany(list => list.Items)
                     .Where(item => item.Active && item.ItemId == itemId && item.Acquired < int.MaxValue))
        {
            item.Acquired += count;
            changed = true;
        }
        return changed;
    }

    internal static bool ContainsActiveItem(AppSettings settings, string server, int itemId) =>
        ForServer(settings, server).Any(list => list.Items.Any(item => item.Active && item.ItemId == itemId));

    internal static void ImportWantedLoot(AppSettings settings)
    {
        if (settings.LootListsMigrated || !settings.WantedLootByServer.Values.Any(ids => ids is { Count: > 0 })) return;
        settings.LootListsMigrated = true;
        foreach (var (server, ids) in settings.WantedLootByServer)
        {
            if (ids is null || ids.Count == 0) continue;
            var list = new LootList { Server = GameServerProfile.Normalize(server),
                Name = settings.Language == "en" ? "Wanted loot" : "Желаемый лут" };
            foreach (var id in ids.OrderBy(id => id)) AddItem(list, id, $"Item #{id}");
            if (list.Items.Count > 0) settings.LootLists.Add(list);
        }
        settings.WantedLootByServer.Clear();
    }
}
