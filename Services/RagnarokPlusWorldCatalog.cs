using RoAura.Models;

namespace RoAura.Services;

internal static class RagnarokPlusWorldCatalog
{
    internal static void Normalize(GameDatabase catalog)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        catalog.Monsters = catalog.Monsters.GroupBy(m => m.Id).Select(group =>
        {
            var monster = group.OrderByDescending(m => m.IsMvp).ThenByDescending(m => m.RagnarokPlusDetailsCaptured).First();
            monster.Spawns = group.SelectMany(m => m.Spawns).ToList();
            monster.Drops = group.SelectMany(m => m.Drops).DistinctBy(d => (d.ItemId, d.IsMvp, d.Rate)).ToList();
            return monster;
        }).ToList();
        var monsters = catalog.Monsters.ToDictionary(m => m.Id);
        var maps = catalog.Maps.GroupBy(map => map.Code, comparer).ToDictionary(group => group.Key, group =>
        {
            var map = group.First();
            map.Monsters = group.SelectMany(m => m.Monsters).ToList();
            return map;
        }, comparer);
        var explicitSpawns = catalog.Monsters.ToDictionary(m => m.Id, m => m.Spawns
            .Where(s => IsMapCode(s.Code) && HasSpawn(s.Count, s.Respawn))
            .GroupBy(s => s.Code, comparer).Select(g => g.OrderByDescending(s => !string.IsNullOrWhiteSpace(s.Respawn)).First()).ToArray());
        var aliases = new Dictionary<string, string>(comparer);
        var matches = new List<(DatabaseMap Map, string Code, int Score)>();
        foreach (var map in maps.Values.Where(m => !IsMapCode(m.Code)))
        {
            var ranked = map.Monsters.Where(m => HasSpawn(m.Count, m.Respawn))
                .DistinctBy(m => m.MonsterId)
                .SelectMany(m => explicitSpawns.GetValueOrDefault(m.MonsterId) ?? [],
                    (mob, spawn) => (mob, spawn))
                .Where(pair => Count(pair.mob.Count) > 0 && Count(pair.mob.Count) == Count(pair.spawn.Count))
                .GroupBy(pair => pair.spawn.Code, comparer)
                .Select(g => (Code: g.Key, Score: g.Count()))
                .OrderByDescending(x => x.Score).Take(2).ToArray();
            if (ranked.Length > 0 && (ranked.Length == 1 || ranked[0].Score > ranked[1].Score))
                matches.Add((map, ranked[0].Code, ranked[0].Score));
        }

        foreach (var group in matches.GroupBy(m => m.Code, comparer))
        {
            var ranked = group.OrderByDescending(x => x.Score).Take(2).ToArray();
            if (ranked.Length > 1 && ranked[0].Score == ranked[1].Score) continue;
            var (map, code, _) = ranked[0];
            aliases[map.Code] = code;
            maps.Remove(map.Code);
            if (maps.TryGetValue(code, out var existing))
            {
                existing.Monsters.AddRange(map.Monsters);
                existing.LocalImagePng ??= map.LocalImagePng;
            }
            else
            {
                map.Code = code;
                maps.Add(code, map);
            }
        }

        foreach (var monster in catalog.Monsters)
        {
            foreach (var spawn in monster.Spawns.Where(s => HasSpawn(s.Count, s.Respawn)))
            {
                var code = aliases.GetValueOrDefault(spawn.Code, spawn.Code);
                if (string.IsNullOrWhiteSpace(code)) continue;
                if (!maps.TryGetValue(code, out var map))
                    maps.Add(code, map = new DatabaseMap { Code = code, Name = spawn.Name });
                var entry = map.Monsters.FirstOrDefault(m => m.MonsterId == monster.Id);
                if (entry is null)
                    map.Monsters.Add(new DatabaseMapMonster { MonsterId = monster.Id, Name = monster.Name,
                        Count = spawn.Count, Respawn = spawn.Respawn });
                else
                {
                    if (Count(entry.Count) == 0) entry.Count = spawn.Count;
                    if (string.IsNullOrWhiteSpace(entry.Respawn)) entry.Respawn = spawn.Respawn;
                }
            }
            monster.Spawns = [];
        }

        foreach (var map in maps.Values)
        {
            map.Monsters = map.Monsters.Where(m => monsters.ContainsKey(m.MonsterId) && HasSpawn(m.Count, m.Respawn))
                .GroupBy(m => m.MonsterId).Select(g => g.OrderByDescending(m => !string.IsNullOrWhiteSpace(m.Respawn)).First()).ToList();
            if (IsMapCode(map.Code)) map.Name = map.Code;
            foreach (var entry in map.Monsters)
                monsters[entry.MonsterId].Spawns.Add(new DatabaseSpawn { Code = map.Code, Name = map.Name,
                    Count = entry.Count, Respawn = entry.Respawn });
        }

        catalog.Maps = maps.Values.Where(m => m.Monsters.Count > 0).ToList();
        catalog.Monsters.RemoveAll(m => m.Drops.Count == 0 && m.Spawns.Count == 0);
        var retained = catalog.Monsters.Select(m => m.Id).ToHashSet();
        foreach (var item in catalog.Items)
            item.DroppedBy.RemoveAll(source => !retained.Contains(source.MonsterId));
    }

    private static bool IsMapCode(string code) =>
        code.Length > 0 && char.IsAsciiLetter(code[0]) && code.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    private static int Count(string count) => int.TryParse(count.Trim(), out var value) ? value : 0;

    private static bool HasSpawn(string count, string respawn) => Count(count) > 0 || !string.IsNullOrWhiteSpace(respawn);
}
