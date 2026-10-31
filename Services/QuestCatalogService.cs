using System.Runtime.InteropServices;
using RoAura.Models;

namespace RoAura.Services;

/// <summary>Isolated schema-v5 reader on the same Windows system SQLite API as ToC.
/// No writes, downloads, Python, packet capture or changes to ToC's payload reader.</summary>
public sealed class QuestCatalogService
{
    private readonly string _path;
    public QuestCatalogService(string? path = null) =>
        _path = path ?? Path.Combine(AppContext.BaseDirectory, "Data", "questie_catalog.sqlite");

    public Task<QuestCatalog> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Load(cancellationToken), cancellationToken);

    private QuestCatalog Load(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(_path)) throw new FileNotFoundException("Quest catalog is not installed.", _path);
        using var db = new Reader(_path, token);
        if (db.Query("PRAGMA user_version", r => r.Number(0)).Single() != QuestCatalog.SupportedSchema)
            throw new InvalidDataException("Unsupported quest catalog schema; expected v5.");
        var integrity = db.Query("PRAGMA integrity_check", r => r.Text(0));
        if (integrity.Count != 1 || integrity[0] != "ok" ||
            db.Query("PRAGMA foreign_key_check", _ => true).Count != 0)
            throw new InvalidDataException("Quest catalog integrity check failed.");

        var locations = db.Query("SELECT quest_key,step_no,map,x,y,hint_kind,raw_hint,assurance FROM quest_step_locations", r =>
            (Key: r.Text(0), Step: r.Number(1), Location: new QuestLocation(r.NullableText(2), r.Number(3), r.Number(4), r.Text(5), r.Text(6), r.Text(7))))
            .GroupBy(x => (x.Key, x.Step)).ToDictionary(g => g.Key, g => (IReadOnlyList<QuestLocation>)g.Select(x => x.Location).ToList().AsReadOnly());
        var knownMaps=db.Query("SELECT DISTINCT map FROM npcs WHERE map IS NOT NULL",r=>r.Text(0))
            .Concat(locations.Values.SelectMany(x=>x).Where(l=>l.Map is not null).Select(l=>l.Map!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = db.Query("SELECT quest_key,step_no,source_step,kind,section,instruction FROM quest_steps ORDER BY quest_key,step_no", r =>
        {
            var key=r.Text(0);var number=r.Number(1);var instruction=r.Text(5);
            var literal=locations.GetValueOrDefault((key,number))??Array.Empty<QuestLocation>();
            var all=literal.Concat(QuestWikiLocations.Parse(instruction,knownMaps))
                .DistinctBy(l=>(l.Map?.ToLowerInvariant(),l.X,l.Y,l.Kind,l.Assurance)).ToList().AsReadOnly();
            return (Key:key,Step:new QuestStep(number,r.NullableNumber(2),r.NullableText(3),r.NullableText(4),instruction,all));
        });
        if (rows.Any(x => x.Step.Number < 1) || rows.GroupBy(x => (x.Key, x.Step.Number)).Any(g => g.Count() != 1))
            throw new InvalidDataException("Invalid or duplicate quest steps.");
        var stepKeys = rows.Select(x => (x.Key, x.Step.Number)).ToHashSet();
        if (locations.Keys.Any(key => !stepKeys.Contains(key)))
            throw new InvalidDataException("Quest location refers to a missing step.");
        var steps = rows.GroupBy(x => x.Key).ToDictionary(g => g.Key, g => (IReadOnlyList<QuestStep>)g.Select(x => x.Step).ToList().AsReadOnly());
        var guides = db.Query("""
            SELECT q.quest_key,q.title,q.source_url,q.status,q.review_state,q.base_level,q.job_level,
                   q.requirements,q.rewards,q.content_sha,k.refresh_state,k.last_success_utc,k.last_error,
                   c.category,c.assurance,k.stored_steps
            FROM quests q LEFT JOIN quest_quality k USING(quest_key)
            LEFT JOIN quest_content_categories c USING(quest_key) ORDER BY q.quest_key
            """, r =>
        {
            var key = r.Text(0);
            var ordered = steps.GetValueOrDefault(key) ?? Array.Empty<QuestStep>();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(r.Text(1)) ||
                r.Number(15) != ordered.Count || r.NullableNumber(5) < 0 || r.NullableNumber(6) < 0)
                throw new InvalidDataException("Invalid quest identity, level or stored step count.");
            return new QuestGuide(key, r.Text(1), r.Text(2), r.Text(3), r.Text(4), r.NullableNumber(5), r.NullableNumber(6),
                r.NullableText(7), r.NullableText(8), r.NullableText(9), r.Text(10), r.NullableText(11), r.NullableText(12),
                r.NullableText(13), r.NullableText(14), ordered);
        });
        if (guides.GroupBy(q => q.Key).Any(g => g.Count() != 1) ||
            rows.Any(x => !guides.Any(q => q.Key == x.Key)))
            throw new InvalidDataException("Duplicate or missing quest identities.");
        return new QuestCatalog(guides);
    }

    private sealed class Reader : IDisposable
    {
        private IntPtr _db, _statement;
        private readonly CancellationToken _token;
        private readonly ProgressCallback _progress;
        private long _textBytes;

        public Reader(string path, CancellationToken token)
        {
            _token = token;
            _progress = () => token.IsCancellationRequested ? 1 : 0;
            try
            {
                if (sqlite3_open_v2(path, out _db, 1, IntPtr.Zero) != 0) Fail();
                sqlite3_progress_handler(_db, 1000, _progress, IntPtr.Zero);
            }
            catch { Dispose(); throw; }
        }

        public List<T> Query<T>(string sql, Func<Reader, T> read)
        {
            _token.ThrowIfCancellationRequested();
            try
            {
                if (sqlite3_prepare_v2(_db, sql, -1, out _statement, IntPtr.Zero) != 0) Fail();
                var result = new List<T>();
                int rc;
                while ((rc = sqlite3_step(_statement)) == 100)
                {
                    _token.ThrowIfCancellationRequested();
                    if (result.Count >= 100_000) throw new InvalidDataException("Quest catalog row limit exceeded.");
                    result.Add(read(this));
                }
                if (rc != 101) Fail();
                return result;
            }
            finally
            {
                if (_statement != IntPtr.Zero) sqlite3_finalize(_statement);
                _statement = IntPtr.Zero;
            }
        }

        public string? NullableText(int column)
        {
            if (sqlite3_column_type(_statement, column) == 5) return null;
            var bytes = sqlite3_column_bytes(_statement, column);
            _textBytes += bytes;
            if (bytes > 1_048_576 || _textBytes > 64 * 1024 * 1024)
                throw new InvalidDataException("Quest catalog text limit exceeded.");
            return Marshal.PtrToStringUTF8(sqlite3_column_text(_statement, column), bytes) ?? "";
        }
        public string Text(int column) => NullableText(column) ?? throw new InvalidDataException("Missing quest catalog field.");
        public int? NullableNumber(int column) => sqlite3_column_type(_statement, column) == 5 ? null : Number(column);
        public int Number(int column)
        {
            if (sqlite3_column_type(_statement, column) != 1) throw new InvalidDataException("Invalid quest catalog integer.");
            var value = sqlite3_column_int64(_statement, column);
            if (value < int.MinValue || value > int.MaxValue) throw new InvalidDataException("Quest integer out of range.");
            return (int)value;
        }
        private void Fail()
        {
            _token.ThrowIfCancellationRequested();
            throw new InvalidDataException("Cannot read quest catalog or required schema fields.");
        }
        public void Dispose()
        {
            if (_statement != IntPtr.Zero) sqlite3_finalize(_statement);
            if (_db != IntPtr.Zero) sqlite3_close(_db);
            _statement = _db = IntPtr.Zero;
            GC.KeepAlive(_progress);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ProgressCallback();
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db, int flags, IntPtr vfs);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int n, out IntPtr statement, IntPtr tail);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr statement);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_type(IntPtr statement, int column);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_bytes(IntPtr statement, int column);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern long sqlite3_column_int64(IntPtr statement, int column);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void sqlite3_progress_handler(IntPtr db, int instructions, ProgressCallback callback, IntPtr argument);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr statement);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
}
