namespace RoAura.Services;

/// <summary>
/// Independent accumulator for character-specific stats. Not connected to
/// incoming gameplay events or legacy overlays during the storage milestone.
/// All operations use explicit UTC timestamps for deterministic tests.
/// Call Flush periodically and Suspend on exit once it is integrated.
/// </summary>
public sealed class CharacterStatisticsLedger
{
    private readonly object _sync = new();
    private readonly CharacterStatisticsStore _store;
    private readonly Dictionary<CharacterStatisticsKey, CharacterStatisticsDocument> _documents = [];
    private readonly ZenyBalanceTracker _zeny = new();
    private CharacterStatisticsKey? _active;
    private DateTime? _sinceUtc;

    public CharacterStatisticsLedger(CharacterStatisticsStore store) => _store = store;

    public CharacterStatisticsKey? Active
    {
        get { lock (_sync) return _active; }
    }

    public void SwitchTo(CharacterStatisticsKey key, DateTime nowUtc)
    {
        key = CharacterStatisticsKey.Create(key.ServerKey, key.CharacterId);
        CheckUtc(nowUtc);
        lock (_sync)
        {
            if (_active == key && _sinceUtc.HasValue) return;
            // Load first: a corrupt destination must not destroy the old session.
            var next = GetOrCreate(key, nowUtc);
            PauseCurrent(nowUtc);
            _active = key;
            _sinceUtc = nowUtc;
            // Login does not begin the shared timer.
            _zeny.Reset(); // First balance of EVERY login is a baseline.
        }
    }

    public void Suspend(DateTime nowUtc)
    {
        CheckUtc(nowUtc);
        lock (_sync) PauseCurrent(nowUtc);
    }

    public void Flush(DateTime nowUtc)
    {
        CheckUtc(nowUtc);
        lock (_sync)
        {
            Checkpoint(nowUtc);
            if (_active is { } key) _store.Save(_documents[key].Copy());
        }
    }

    public CharacterStatisticsDocument? Snapshot(CharacterStatisticsKey key, DateTime nowUtc)
    {
        key = CharacterStatisticsKey.Create(key.ServerKey, key.CharacterId);
        CheckUtc(nowUtc);
        lock (_sync)
        {
            if (!_documents.TryGetValue(key, out var document))
            {
                var loaded = _store.Load(key);
                if (loaded is null) return null;
                _documents[key] = document = loaded;
            }
            var result = document.Copy();
            if (_active == key && _sinceUtc.HasValue && result.SessionTimerStarted)
            {
                var ticks = ElapsedTicks(_sinceUtc.Value, nowUtc);
                result.SessionActiveTicks = checked(result.SessionActiveTicks + ticks);
                result.LifetimeActiveTicks = checked(result.LifetimeActiveTicks + ticks);
            }
            return result;
        }
    }

    public TimeSpan ActiveSessionElapsed(DateTime nowUtc)
    {
        CheckUtc(nowUtc);
        lock (_sync)
        {
            if (_active is not { } key || !_documents[key].SessionTimerStarted)
                return TimeSpan.Zero;
            var ticks = _documents[key].SessionActiveTicks +
                (_sinceUtc.HasValue ? ElapsedTicks(_sinceUtc.Value, nowUtc) : 0);
            return TimeSpan.FromTicks(ticks);
        }
    }

    public bool AddExperience(long amount, bool isJob, DateTime nowUtc)
    {
        CheckUtc(nowUtc);
        if (amount <= 0) return false;
        lock (_sync)
        {
            if (!TryGetActive(out var state)) return false;
            if (!state.SessionTimerStarted)
            {
                state.SessionTimerStarted = true;
                state.SessionStartedUtc = nowUtc;
                _sinceUtc = nowUtc;
            }
            if (isJob)
            {
                state.SessionJobExperience = checked(state.SessionJobExperience + amount);
                state.LifetimeJobExperience = checked(state.LifetimeJobExperience + amount);
            }
            else
            {
                state.SessionBaseExperience = checked(state.SessionBaseExperience + amount);
                state.LifetimeBaseExperience = checked(state.LifetimeBaseExperience + amount);
            }
            state.LastUpdatedUtc = nowUtc;
            return true;
        }
    }

    public bool AddLoot(int itemId, string name, long count, DateTime nowUtc)
    {
        CheckUtc(nowUtc);
        if (itemId <= 0 || count <= 0) return false;
        lock (_sync)
        {
            if (!TryGetActive(out var state)) return false;
            IncreaseLoot(state.SessionLoot, itemId, name, count);
            IncreaseLoot(state.LifetimeLoot, itemId, name, count);
            state.LastUpdatedUtc = nowUtc;
            return true;
        }
    }

    /// <summary>Returns trusted positive income only; no income from the first balance.</summary>
    public long ObserveZeny(long total, DateTime nowUtc)
    {
        CheckUtc(nowUtc);
        lock (_sync)
        {
            if (!TryGetActive(out var state)) return 0;
            var sample = _zeny.Observe(total);
            if (!sample.Accepted || sample.Gained <= 0) return 0;
            state.SessionZenyGained = checked(state.SessionZenyGained + sample.Gained);
            state.LifetimeZenyGained = checked(state.LifetimeZenyGained + sample.Gained);
            state.LastUpdatedUtc = nowUtc;
            return sample.Gained;
        }
    }

    public void SetProgress(bool isJob, ulong current, ulong required, DateTime nowUtc)
    {
        CheckUtc(nowUtc);
        lock (_sync)
        {
            if (!TryGetActive(out var state)) return;
            if (isJob)
            {
                state.CurrentJobExperience = current;
                state.NextJobExperience = required;
            }
            else
            {
                state.CurrentBaseExperience = current;
                state.NextBaseExperience = required;
            }
            state.LastUpdatedUtc = nowUtc;
        }
    }

    /// <summary>Persist Gold Goblin manual row counts/prices independently of drop history.</summary>
    public void ReplaceGoldGoblinRows(IReadOnlyCollection<CharacterLootCount> rows, DateTime nowUtc)
    {
        CheckUtc(nowUtc);
        lock (_sync)
        {
            if (!TryGetActive(out var state)) return;
            var next = new Dictionary<int, CharacterLootCount>();
            foreach (var row in rows)
            {
                if (row.ItemId <= 0 || row.Count < 0 || row.ManualUnitCost < 0 ||
                    row.Name.Length > 500 || !next.TryAdd(row.ItemId, new CharacterLootCount
                    {
                        ItemId = row.ItemId, Name = row.Name, Count = row.Count,
                        HasManualCost = row.HasManualCost, ManualUnitCost = row.ManualUnitCost
                    }))
                    throw new InvalidDataException("Invalid Gold Goblin row.");
            }
            state.GoldGoblinRows = next;
            state.LastUpdatedUtc = nowUtc;
        }
    }

    public void StartNewSession(DateTime nowUtc) => ResetCurrentSession(nowUtc, true);

    public void ResetCurrentSession(DateTime nowUtc, bool startClock = false)
    {
        CheckUtc(nowUtc);
        lock (_sync)
        {
            if (!TryGetActive(out var document)) return;
            Checkpoint(nowUtc);
            document.SessionBaseExperience = 0;
            document.SessionJobExperience = 0;
            document.SessionZenyGained = 0;
            document.SessionActiveTicks = 0;
            document.SessionLoot.Clear();
            document.GoldGoblinRows.Clear();
            document.SessionStartedUtc = startClock ? nowUtc : default;
            document.SessionTimerStarted = startClock;
            _sinceUtc = nowUtc;
            document.LastUpdatedUtc = nowUtc;
            _zeny.Reset();
            _store.Save(document.Copy());
        }
    }

    private CharacterStatisticsDocument GetOrCreate(CharacterStatisticsKey key, DateTime nowUtc)
    {
        if (_documents.TryGetValue(key, out var existing)) return existing;
        var document = _store.Load(key) ?? new CharacterStatisticsDocument
        {
            ServerKey = key.ServerKey,
            CharacterId = key.CharacterId,
            CreatedUtc = nowUtc,
            LastUpdatedUtc = nowUtc,
            SessionStartedUtc = default,
            SessionTimerStarted = false
        };
        _documents.Add(key, document);
        return document;
    }

    private void PauseCurrent(DateTime nowUtc)
    {
        Checkpoint(nowUtc);
        if (_active is { } key) _store.Save(_documents[key].Copy());
        _active = null;
        _sinceUtc = null;
        _zeny.Reset();
    }

    private void Checkpoint(DateTime nowUtc)
    {
        if (_active is not { } key || !_sinceUtc.HasValue) return;
        var state = _documents[key];
        if (state.SessionTimerStarted)
        {
            var elapsed = ElapsedTicks(_sinceUtc.Value, nowUtc);
            state.SessionActiveTicks = checked(state.SessionActiveTicks + elapsed);
            state.LifetimeActiveTicks = checked(state.LifetimeActiveTicks + elapsed);
        }
        state.LastUpdatedUtc = nowUtc;
        _sinceUtc = nowUtc;
    }

    private bool TryGetActive(out CharacterStatisticsDocument document)
    {
        if (_active is { } key && _sinceUtc.HasValue)
        {
            document = _documents[key];
            return true;
        }
        document = null!;
        return false;
    }

    private static void IncreaseLoot(Dictionary<int, CharacterLootCount> loot, int itemId, string name, long count)
    {
        if (!loot.TryGetValue(itemId, out var entry))
        {
            entry = new CharacterLootCount { ItemId = itemId, Name = name ?? "" };
            loot.Add(itemId, entry);
        }
        entry.Count = checked(entry.Count + count);
        if (!string.IsNullOrWhiteSpace(name)) entry.Name = name;
    }

    private static void CheckUtc(DateTime time)
    {
        if (time.Kind != DateTimeKind.Utc) throw new ArgumentException("Expected UTC timestamp.");
    }

    private static long ElapsedTicks(DateTime start, DateTime end) =>
        end > start ? (end - start).Ticks : 0;
}
