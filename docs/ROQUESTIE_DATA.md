# ROQuestie data layer (stage 1)

Baseline: stable RoAura 3.2.0 / 10adb870. Runtime catalog contract: v5,
research ref 41bfc483311fa0e24ee84d1b1edcb0b4558914bd,
SHA-256 7c89954c66b90603467ee4b645979fe4bad46ff0003ebaf9073e32c18d748745.
The separate research DB schema 13 is not a supported runtime catalog.

## Catalog API

`QuestCatalogService(path).LoadAsync(token)` opens read-only through system
winsqlite3.dll, checks user_version before reading, checks integrity/foreign
keys and loads the small guide projection into independent read-only collections.
No source scripts, dialogue candidates or live character state are loaded.
The default future distribution path is `Data/questie_catalog.sqlite`; stage 1
does not copy the 73 MB catalog to the application's startup/distribution path.

`QuestCatalog.Search(text, category, level)` searches original names locally,
case-insensitively, with literal input rather than interpolated SQL. Unknown
levels remain unknown and visible. Classifications are provisional source labels.
The UI stage owns search debounce and discarding superseded load/search results.

Errors are task failures isolated to this service (missing file, invalid schema/
data, cancellation or OS I/O/native availability); there is no static failed
singleton and a subsequent reader can load a healthy catalog. UI integration
must catch/report these in Quests only, leaving existing services operational.

The reader preserves requirements/rewards as source text, source URLs/hashes,
NULL levels/map, original numbering/kind, freshness and assurance. Wiki text and
source-reviewed mappings are never server support or live quest-completion proof.
Step locations are evidence, not validated navigation targets. Navigation target
selection/validation and clipboard interaction belong to stage 4.

## Progress API and ownership

`QuestProfileKey(server, cid, null)` or `(server, null, localGuid)` explicitly
selects one identity. `FromObserved` returns null for packet-off, wrong server,
non-ActiveObserved state or unknown CID. It never uses PlayerAid. Local-profile
selection is manual and does not alter capture identity or character registry.
The UI/controller must clear observed selection on session invalidation and never
fall back to the last observed identity. Do not key history on mutable aliases.

`QuestProgressStore.Load(key)` returns a session owning its document and disk
revision. Normal storage is `%LOCALAPPDATA%/RoAura/ROQuestie/<SERVER>/cid-N.json`
or `local-GUID.json` (the standard isolated DEV/smoke AppEnvironment roots apply).
`Save(session)` atomically replaces the primary, retaining `.bak`, with durable
flush, an exclusive writer lock and optimistic disk-hash checks. A stale writer
must reload; a failed save must remain visible to the caller. Temporary files
are cleaned after errors. Read-only recovery never repairs/overwrites originals.
Future schemas, invalid JSON/identities, duplicate quests and contradictory rows
are rejected. There is no predecessor quest-state format to migrate; v0/future
versions must not silently become v1. Any future migration must add tests.

Active quests/history are outside AppSettings/SettingsSnapshot/preset files and
outside the source SQLite. Applying presets must not replace them. Stage 2/3
will connect explicit store saves through debounced controller changes, flush on
scope/client switch and close, and surface read-only/error states. There is no
per-frame writer or new background worker in stage 1.

`QuestProgress.Add` is idempotent per canonical quest key. Existing history is
restored only through an explicit state change. `SelectStep` is a manual choice;
`Move` stays within that quest's ordered rows and never sets Completed. `SetState`
requires explicit confirmation for Completed and records UTC/manual history.
Order/visibility/state are independent fields per quest. Removed/Paused entries
retain all instructions position/history metadata rather than being deleted.

Before displaying progress against a refreshed catalog, call `Reconcile`.
A unique identical kind+instruction SHA can move to a new row. Unchanged catalog
and instruction keep an explicitly chosen repeated row. Ambiguous/changed/missing
instructions keep the old row/hash/anchor and set NeedsStepReview; `Move` then
refuses to advance until explicit `SelectStep`. Missing guides remain saved.
None of these manual flags is evidence that the server performed an action.

## Validation

New tests: `QuestCatalogTests.cs`, `QuestProgressTests.cs` (31 cases).
Synthetic fixtures cover malformed/duplicate/missing schema fields, NULL values,
long Unicode, literal search, cancellation and file preservation. The exact real
catalog test covers 286 quests/3420 rows, Lost Child 13 steps, Friendship 14,
multiple/NULL wiki locations and independent manual progression.

For local Windows snapshot testing, set `ROAURA_QUEST_CATALOG_TEST_PATH` to the
exact pinned catalog. If absent, only that actual-snapshot test is explicitly
skipped; synthetic Windows SQLite tests still run. Feature CI sets the path and
hash-checks a snapshot extracted outside the distributed payload. The ordinary
full test/build/package checks remain unchanged apart from this branch trigger
and preparation step. No publication action was added.

Local Linux: 31 cases passed using actual C# production/test files, actual xUnit
assertions and a small reflection harness; only winsqlite3 was resolved to system
libsqlite3 and capture tuple shell was provided for isolated session types.
The complete app and test project cross-compile for Windows with .NET 8.0.415.
These checks do not prove native Windows/WPF/game behavior. Full Windows suite,
portable startup/payload validation and its exact commit CI result are required.
No interactive in-game/DPI/manual regression or release readiness is claimed.

## Stage-2 integration

QuestWorkspace is the UI-thread controller; MainWindow.Quests dispatches existing
CID snapshots independently of statistics, debounces saves and flushes before
scope changes/normal close. The page's automatic and manual choices are explicit.
ListKnownProfiles enumerates safe per-server CID/local filenames (including backup
only), never using an alias to rewrite detected identity. Read-only recovery stays
read-only. Save failures preserve the old pending document until retry.

Feature CI now extracts the pinned consumer to Data/questie_catalog.sqlite for
the test portable and real-app smoke; .gitignore avoids duplicating the research
binary. On a full clone, provision it locally without changing research:

```python
import pathlib, subprocess
revision = '41bfc483311fa0e24ee84d1b1edcb0b4558914bd'
subprocess.run(['git', 'fetch', 'origin', revision], check=True)
payload = subprocess.check_output(['git', 'show', revision + ':research/quest-database/output/questie_catalog.sqlite'])
pathlib.Path('Data/questie_catalog.sqlite').write_bytes(payload)
```

The source tree retains the reader's nonfatal missing-catalog state. Notices ship
next to the snapshot. Full packaging/license review remains stage 5. Schema 14
visual settings participate in presets; external quest state never does.
