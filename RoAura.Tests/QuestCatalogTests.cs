using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public class QuestCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "RoAura-QuestCatalog-" + Guid.NewGuid().ToString("N"));
    private string CatalogPath => Path.Combine(_root, "catalog.sqlite");
    private const string Fixture = """
        PRAGMA user_version=5;
        CREATE TABLE quests(quest_key TEXT,title TEXT,source_url TEXT,status TEXT,review_state TEXT,base_level INTEGER,job_level INTEGER,requirements TEXT,rewards TEXT,content_sha TEXT);
        CREATE TABLE quest_steps(quest_key TEXT,step_no INTEGER,source_step INTEGER,kind TEXT,section TEXT,instruction TEXT);
        CREATE TABLE quest_step_locations(quest_key TEXT,step_no INTEGER,map TEXT,x INTEGER,y INTEGER,hint_kind TEXT,raw_hint TEXT,assurance TEXT);
        CREATE TABLE quest_quality(quest_key TEXT,refresh_state TEXT,last_success_utc TEXT,last_error TEXT,stored_steps INTEGER);
        CREATE TABLE quest_content_categories(quest_key TEXT,category TEXT,assurance TEXT);
        INSERT INTO quests VALUES('lost','Lost Child Quest','https://example.test/lost','walkthrough_parsed','cross_source_variant',60,NULL,'Original requirements','Original rewards','abc');
        INSERT INTO quests VALUES('friend','Friendship Quest','https://example.test/friend','walkthrough_parsed','candidate',NULL,NULL,NULL,NULL,'def');
        INSERT INTO quest_steps VALUES('lost',1,1,'numbered','Walkthrough','Talk to NPC'),('lost',2,NULL,'narrative',NULL,'Unnumbered original paragraph'),('friend',1,1,'numbered',NULL,'Another instruction');
        INSERT INTO quest_quality VALUES('lost','fresh','2026-10-29',NULL,2),('friend','cached_http_error',NULL,'timeout',1);
        INSERT INTO quest_step_locations VALUES('lost',1,'ra_in01',372,200,'map_coord','ra_in01:372:200','wiki_literal_not_npc_verified'),('lost',2,NULL,10,0,'bare_coord','10:0','wiki_literal_not_npc_verified');
        INSERT INTO quest_content_categories VALUES('lost','storyline_candidate','provisional');
        """;

    private void Create(string extra = "")
    {
        Directory.CreateDirectory(_root);
        IntPtr db = IntPtr.Zero;
        try
        {
            Assert.Equal(0, sqlite3_open_v2(CatalogPath, out db, 6, IntPtr.Zero));
            var result = sqlite3_exec(db, Fixture + extra, IntPtr.Zero, IntPtr.Zero, out var error);
            var message = error == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(error);
            if (error != IntPtr.Zero) sqlite3_free(error);
            Assert.True(result == 0, message);
        }
        finally { if (db != IntPtr.Zero) sqlite3_close(db); }
    }

    [Fact]
    public async Task ReadsSourcesNullsNarrativeAndLiteralLocationsWithoutChangingCatalog()
    {
        Create(); var before = SHA256.HashData(File.ReadAllBytes(CatalogPath));
        var catalog = await new QuestCatalogService(CatalogPath).LoadAsync();
        var guide = catalog.Guides["lost"];
        Assert.Equal("Original requirements", guide.Requirements); Assert.Equal("Original rewards", guide.Rewards);
        Assert.Null(guide.JobLevel); Assert.Equal("storyline_candidate", guide.Category);
        Assert.Equal(new[] { 1, 2 }, guide.Steps.Select(s => s.Number));
        Assert.Equal("narrative", guide.Steps[1].Kind); Assert.Null(guide.Steps[1].SourceNumber);
        Assert.Null(guide.Steps[1].Locations.Single().Map);
        Assert.Equal("wiki_literal_not_npc_verified", guide.Steps[0].Locations.Single().Assurance);
        Assert.Null(catalog.Guides["friend"].Requirements);
        Assert.Equal("cached_http_error", catalog.Guides["friend"].RefreshState);
        Assert.Equal("timeout", catalog.Guides["friend"].LastError);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(CatalogPath)));
        Assert.Single(catalog.Search("cHiLd")); Assert.Empty(catalog.Search("' OR 1=1 --"));
        Assert.Single(catalog.Search(category: "storyline_candidate"));
        Assert.Single(catalog.Search(level: 50)); // unknown level is not invented as zero
    }

    [Theory]
    [InlineData("PRAGMA user_version=4;")]
    [InlineData("PRAGMA user_version=6;")]
    [InlineData("DROP TABLE quest_quality;")]
    [InlineData("INSERT INTO quests SELECT * FROM quests WHERE quest_key='lost';")]
    [InlineData("INSERT INTO quest_steps SELECT * FROM quest_steps WHERE quest_key='friend';")]
    [InlineData("UPDATE quest_quality SET stored_steps=99;")]
    [InlineData("UPDATE quests SET base_level=-1;")]
    [InlineData("UPDATE quest_steps SET step_no=0;")]
    [InlineData("UPDATE quest_step_locations SET step_no=999;")]
    public async Task RejectsUnsupportedSchemasAndContradictoryCatalogs(string mutation)
    {
        Create(mutation);
        await Assert.ThrowsAsync<InvalidDataException>(() => new QuestCatalogService(CatalogPath).LoadAsync());
    }

    [Fact]
    public async Task MissingAndCorruptCatalogErrorsAreIsolatedAndDoNotCreateAFile()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => new QuestCatalogService(CatalogPath).LoadAsync());
        Assert.False(File.Exists(CatalogPath));
        Directory.CreateDirectory(_root); File.WriteAllText(CatalogPath, "broken sqlite");
        await Assert.ThrowsAsync<InvalidDataException>(() => new QuestCatalogService(CatalogPath).LoadAsync());
        Assert.Equal("broken sqlite", File.ReadAllText(CatalogPath));
        // A fresh independent reader still works after another catalog failed.
        File.Delete(CatalogPath); Create();
        Assert.Equal(2, (await new QuestCatalogService(CatalogPath).LoadAsync()).Guides.Count);
    }

    [Fact]
    public async Task PreservesLongUnicodeInstructionsAndSupportsCancellation()
    {
        Create("UPDATE quest_steps SET instruction='Длинный исходный текст ' || replace(hex(zeroblob(10000)),'0','я') WHERE quest_key='friend';");
        var catalog = await new QuestCatalogService(CatalogPath).LoadAsync();
        Assert.True(catalog.Guides["friend"].Steps.Single().Instruction.Length > 20000);
        using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new QuestCatalogService(CatalogPath).LoadAsync(source.Token));
    }

    [QuestSnapshotFact]
    public async Task PinnedResearchSnapshotPassesActualWindowsReaderAndSampleChecks()
    {
        var path = Environment.GetEnvironmentVariable("ROAURA_QUEST_CATALOG_TEST_PATH")!;
        Assert.Equal("7C89954C66B90603467EE4B645979FE4BAD46FF0003EBAF9073E32C18D748745",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        var catalog = await new QuestCatalogService(path).LoadAsync();
        Assert.NotEmpty(catalog.Guides); Assert.DoesNotContain(catalog.Guides.Values,q=>q.Category=="historical_or_seasonal"); Assert.NotEmpty(catalog.ArchivedKeys);
        var lost = catalog.Guides["irowiki-classic:Lost_Child_Quest"];
        var friend = catalog.Guides["irowiki-classic:Friendship_Quest"];
        Assert.Equal(13, lost.Steps.Count); Assert.Equal(14, friend.Steps.Count);
        Assert.Equal(2, lost.Steps[0].Locations.Count);
        Assert.Equal("ra_in01", lost.Steps[2].Locations.Single().Map);
        Assert.Null(lost.Steps[11].Locations.Single().Map);
        var state = QuestProgressDocument.Create(new("SpringRO", 123, null));
        var first = QuestProgress.Add(state, lost); var second = QuestProgress.Add(state, friend);
        QuestProgress.SelectStep(first, lost, 3); QuestProgress.Move(first, lost, 1); QuestProgress.Move(first, lost, -1);
        Assert.Equal(3, first.CurrentStep); Assert.Equal(1, second.CurrentStep);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr arg, out IntPtr error);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void sqlite3_free(IntPtr pointer);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
}

public sealed class QuestSnapshotFactAttribute : FactAttribute
{
    public QuestSnapshotFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ROAURA_QUEST_CATALOG_TEST_PATH")))
            Skip = "Set ROAURA_QUEST_CATALOG_TEST_PATH to the pinned schema-v5 research snapshot (enabled in feature CI).";
    }
}
