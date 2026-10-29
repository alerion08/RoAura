using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using RoAura.Models;

namespace RoAura.Services;

/// <summary>Read-only adapter over the bundled snapshot. Uses Windows' SQLite; no install/download.</summary>
public static class TocDatabaseService
{
    private static readonly Lazy<GameDatabase> Snapshot = new(Load);
    public static GameDatabase Database => Snapshot.Value;
    private static string Text(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "";
    private static int Int(JsonElement e, string key) => int.TryParse(Text(e, key), out var n) ? n : 0;
    private static bool Flag(JsonElement e, string key) => Text(e, key).Equals("True", StringComparison.OrdinalIgnoreCase);
    public static string Plain(string html) => WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase), "<[^>]+>", ""));
    private static string Rate(JsonElement e, string key) => (Int(e, key) / 100m).ToString("0.####", CultureInfo.InvariantCulture) + "%";

    private static GameDatabase Load()
    {
        var result = new GameDatabase { Source = GameServerProfile.Toc };
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "database.toc.sqlite");
        foreach (var doc in Read(path, "items"))
        {
            using (doc)
            {
                var raw = doc.RootElement.GetProperty("item");
                var name = Text(raw, "display_name");
                var slots = Int(raw, "slots");
                var item = new DatabaseItem { Id = Int(raw, "id"), Name = name + (slots > 0 ? $" [{slots}]" : ""),
                    AegisName = Text(raw, "name_aegis"), Type = Text(raw, "type"),
                    Description = Plain(Text(raw, "display_desc")), NpcSellPrice = Int(raw, "price_sell"),
                    ImageUrl = $"https://tidesofcomodo.com/db/collection-img/{Int(raw, "id")}.png" };
                foreach (var p in raw.EnumerateObject().Where(p => p.Name is not ("desc" or "display_desc")))
                    item.Stats[p.Name] = p.Value.ToString();
                item.Stats.Remove("weight");
                item.Stats["Weight"] = (Int(raw, "weight") / 10m).ToString("0.####", CultureInfo.InvariantCulture);
                result.Items.Add(item);
            }
        }
        var items = result.Items.ToDictionary(i => i.Id);
        var aegis = result.Items.GroupBy(i => i.AegisName).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First());
        var maps = new Dictionary<string, DatabaseMap>();
        foreach (var doc in Read(path, "monsters"))
        {
            using (doc)
            {
                var root = doc.RootElement;
                var raw = root.GetProperty("monster");
                var m = new DatabaseMonster { Id = Int(raw, "id"), Name = Text(raw, "display_name"), IsMvp = Flag(raw, "is_mvp"),
                    IsBoss = Text(raw, "class") == "Boss", IsAggressive = raw.GetProperty("ai_modes").EnumerateArray().Any(v => v.GetString() == "Aggressive"),
                    Level = Text(raw, "level"), Hp = Text(raw, "hp"), Race = Text(raw, "race"), Size = Text(raw, "size"),
                    Element = $"{Text(raw, "element")} {Text(raw, "element_level")}", Attack = $"{Text(raw, "attack")}–{Text(raw, "attack2")}",
                    Defense = Text(raw, "def"), MagicDefense = Text(raw, "mdef"), BaseExp = Text(root, "base_exp"), JobExp = Text(root, "job_exp"),
                    ImageUrl = $"https://tidesofcomodo.com/db/mob-img/{Int(raw, "id")}.gif" };
                foreach (var p in raw.EnumerateObject().Where(p => p.Name is not ("drops" or "mvp_drops" or "base_exp" or "job_exp" or "mvp_exp"))) m.Stats[p.Name] = p.Value.ToString();
                m.Stats["Base EXP (base)"] = Text(raw, "base_exp");
                m.Stats["Job EXP (base)"] = Text(raw, "job_exp");
                m.Stats["MVP EXP (base)"] = Text(raw, "mvp_exp");
                m.Stats["Base EXP"] = m.BaseExp; m.Stats["Job EXP"] = m.JobExp; m.Stats["MVP EXP"] = Text(root, "mvp_exp");
                foreach (var drop in root.GetProperty("drops").EnumerateArray())
                    AddDrop(m, items.GetValueOrDefault(Int(drop, "item_id")), Rate(drop, "real_rate"), false);
                foreach (var drop in raw.GetProperty("mvp_drops").EnumerateArray())
                    AddDrop(m, aegis.GetValueOrDefault(Text(drop, "item")), Rate(drop, "rate"), true);
                foreach (var spawn in root.GetProperty("spawns").EnumerateArray())
                {
                    var s = new DatabaseSpawn { Code = Text(spawn, "map_name"), Name = Text(spawn, "map_display_name"), Count = Text(spawn, "amount"), Respawn = "—" };
                    m.Spawns.Add(s);
                    if (!maps.TryGetValue(s.Code, out var map)) maps[s.Code] = map = new DatabaseMap { Code = s.Code, Name = s.Name };
                    map.Monsters.Add(new DatabaseMapMonster { MonsterId = m.Id, Name = m.Name, Count = s.Count, Respawn = s.Respawn });
                }
                foreach (var skill in root.GetProperty("skills").EnumerateArray())
                    m.Skills.Add(new DatabaseMonsterSkill { Name = Text(skill, "name"), Level = Text(skill, "level"),
                        Type = Text(skill, "rules"), Description = Plain(Text(skill, "tooltip")) });
                result.Monsters.Add(m);
            }
        }
        result.Maps = maps.Values.OrderBy(m => m.Code).ToList();
        return result;
    }

    private static void AddDrop(DatabaseMonster monster, DatabaseItem? item, string rate, bool mvp)
    {
        if (item is null) return;
        monster.Drops.Add(new DatabaseDrop { ItemId = item.Id, Name = item.Name, Rate = rate, IsMvp = mvp });
        item.DroppedBy.Add(new DatabaseItemSource { MonsterId = monster.Id, Name = monster.Name, Rate = rate, IsMvp = mvp });
    }

    private static IEnumerable<JsonDocument> Read(string path, string table)
    {
        IntPtr db = IntPtr.Zero, statement = IntPtr.Zero;
        try
        {
            if (sqlite3_open_v2(path, out db, 1, IntPtr.Zero) != 0) throw new IOException("Cannot open ToC catalog (read-only).");
            if (sqlite3_prepare_v2(db, $"SELECT payload FROM {table} ORDER BY id", -1, out statement, IntPtr.Zero) != 0)
                throw new InvalidDataException("Invalid ToC catalog schema.");
            int rc;
            while ((rc = sqlite3_step(statement)) == 100)
            {
                var count = sqlite3_column_bytes(statement, 0);
                if (count <= 0 || count > 16 * 1024 * 1024) throw new InvalidDataException("Invalid ToC record size.");
                var data = new byte[count];
                Marshal.Copy(sqlite3_column_blob(statement, 0), data, 0, count);
                using var compressed = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
                yield return JsonDocument.Parse(compressed);
            }
            if (rc != 101) throw new InvalidDataException("Incomplete ToC catalog read.");
        }
        finally
        {
            if (statement != IntPtr.Zero) sqlite3_finalize(statement);
            if (db != IntPtr.Zero) sqlite3_close(db);
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db, int flags, IntPtr vfs);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int n, out IntPtr statement, IntPtr tail);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr statement);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_bytes(IntPtr statement, int i);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_blob(IntPtr statement, int i);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr statement);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
}
