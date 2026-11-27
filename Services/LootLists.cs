using RoAura.Models;

namespace RoAura.Services;

internal static class LootLists
{
    internal static IEnumerable<LootList> ForServer(AppSettings settings, string server)
    {
        var profile = GameServerProfile.Normalize(server);
        return settings.LootLists.Where(list => GameServerProfile.Normalize(list.Server) == profile);
    }

    internal static bool IsShownInTracker(LootList list, LootListItem item) =>
        item.ShowInTracker ?? list.ShowInTracker;

    internal static IEnumerable<LootListItem> TrackerItems(LootList list) =>
        list.Items.Where(item => item.Active && IsShownInTracker(list, item));

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
        item.ShowInTracker ??= IsShownInTracker(source, item);
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

    internal static bool IsPickupPaused(AppSettings settings, string server, int itemId)
    {
        var foundInactive = false;
        foreach (var item in ForServer(settings, server).SelectMany(list => list.Items))
        {
            if (item.ItemId != itemId) continue;
            if (item.Active) return false;
            foundInactive = true;
        }
        return foundInactive;
    }

    internal static bool ContainsActiveItem(AppSettings settings, string server, int itemId) =>
        ForServer(settings, server).Any(list => list.Items.Any(item => item.Active && item.ItemId == itemId));

    internal static int ImportQuest(AppSettings settings, string server, string questKey, string name,
        IEnumerable<(int Id, string Name, long Target)> materials)
    {
        var rows = materials.Where(item => item.Id > 0).GroupBy(item => item.Id).ToArray();
        if (rows.Length == 0) return 0;
        var lists = ForServer(settings, server).ToArray();
        var list = lists.FirstOrDefault(item => item.SourceQuestKey == questKey) ??
            lists.FirstOrDefault(item => item.SourceQuestKey is null &&
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (list is null)
        {
            var unique = name;
            for (var suffix = 2; lists.Any(item => string.Equals(item.Name, unique, StringComparison.OrdinalIgnoreCase)); suffix++)
                unique = $"{name} ({suffix})";
            list = new LootList { Name = unique, Server = GameServerProfile.Normalize(server) };
            settings.LootLists.Add(list);
        }
        list.SourceQuestKey = questKey;
        list.ShowInTracker = true;
        var added = 0;
        foreach (var group in rows)
        {
            if (!AddItem(list, group.Key, group.First().Name)) continue;
            list.Items[^1].Target = group.Max(item => item.Target);
            added++;
        }
        return added;
    }
}
