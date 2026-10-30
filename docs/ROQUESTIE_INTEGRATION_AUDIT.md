# ROQuestie — stage 0 integration audit (2026-10-30)

Status: audit complete; runtime behavior unchanged. Implementation stages 1–5 remain pending.
User-approved specification: ROQuestie_Technical_Specification.md, revision 1.0,
2026-10-30. This report does not replace that specification or redesign its UX.

## Pinned baseline

- Stable main: 10adb870f760f080711fb29d9d77fd32d1ff120c; RoAura.csproj/app.manifest 3.2.0.
- Created feature/roquestie-mvp at that exact main commit; branch did not previously exist.
- Research branch: 41bfc483311fa0e24ee84d1b1edcb0b4558914bd.
- Both branch heads match the specification. Research-only DB is now schema 13;
  consumer catalog remains schema 5. These are different databases, not a runtime migration.
- Consumer source: research/quest-database/output/questie_catalog.sqlite.
- Size: 73,289,728 bytes.
- SHA-256: 7c89954c66b90603467ee4b645979fe4bad46ff0003ebaf9073e32c18d748745.
- Read-only SQLite verification: user_version=5, integrity_check=ok,
  foreign_key_check=0 rows, duplicate quest_key groups=0.
- Counts: 286 quests, 3,420 guide rows, 1,254 wiki locations
  (878 map_coord, 376 bare_coord with NULL map). Quality: 285 fresh, 1 fresh_empty.
  “Fresh” concerns source retrieval, never server validation.
- Review queue: all 286 unverified by server; 117 without candidate scripts,
  32 with mixed scopes. Source-supported file mappings are not gameplay confirmations.

## Sources inspected

AGENTS.md; docs/PROJECT_STATE.md; docs/UI_SPEC.md;
docs/REGRESSION_CHECKLIST.md; README.md; GUIDE_RU.md; GUIDE_EN.md;
RoAura.csproj; relevant runtime and tests. Research README.md,
INTEGRATION_CONTRACT.md, SERVER_VALIDATION.md, coverage.json and
quest_review_queue.json, plus the actual consumer SQLite schema/sample records.

PROJECT_STATE contains historical 3.1.1 and earlier branch statements below
the new 3.2.0 publication entry; code and the top dated entry take precedence.
The guides still have a historical 2.14.0 output archive example; that is not
the current product version. No unrelated cleanup is part of this audit.

## Integration map and planned touch points

| Area | Existing implementation | Planned boundary |
| --- | --- | --- |
| SQLite | Services/TocDatabaseService.cs, private native winsqlite3.dll API, System32 search, read-only open flag 1; ToC compressed payload reader | Separate Services/QuestCatalogService.cs and quest models; same system provider, typed schema-v5 SQL. Do not reuse the ToC payload query or refactor ToC loading. Async loading/cancellation, bounded input and isolated errors. |
| Catalog packaging | RoAura.csproj already copies Data/*.sqlite to output/publish | Pin Data/questie_catalog.sqlite and provenance/license notices at packaging stage; do not ship research SQLite/scripts or player data. Verify build-release.ps1 and installer payload separately. |
| Navigation | MainWindow.xaml NavigationButtonStyle, NavIconStyle, numeric RadioButton tags; MainWindow.xaml.cs Navigation_Checked/UpdateNavigationAvailability; Models/FeatureAvailability.cs | Append Quests without shifting persisted enum values or Settings index 9. Availability currently relies on enum/index order and must become explicit for the added destination. Update AppSettings navigation preferences, Settings navigation editor/localization as needed. Quests stays offline/reference-capable for all servers and packet-off. |
| Menu style/icon | Common margin 7,4, height 48 DIP; icon 32 DIP/NearestNeighbor | Shared compact spacing only (start 44 DIP, vertical margin 2, icon 30); supplied original scroll icon as WPF resource under Assets/Menu. Verify actual RU/EN DPI layout. |
| Theme/editors | Themes/RagnarokTheme.xaml, Controls/NumericTextBox.cs, Resources language dictionaries | New QuestsPage and More window reuse theme, header then tabs, existing numeric draft commit behavior and RU/EN parity. Header Settings and colored About remain intact. |
| CID | Services/CharacterSessionManager.cs Current/StateChanged/Revision/ActiveObserved; App.CurrentCharacterSession; NpcapPacketCapture session events | Observe existing server-qualified CID snapshots. Connecting/Unknown/Suspended never inherit last CID. AID is not identity. Do not change capture/parser/ledger. |
| Character profiles | Models/CharacterProfile.cs, Services/CharacterScopeService.cs, MainWindow.Settings.cs | Explicit manual profile selection for offline mode; distinguish manually selected from observed. Keep server+CID or server+local-profile keys separate. Existing mutable assigned-server aliases must not silently move quest history. |
| Progress | Services/CharacterStatisticsStore.cs and CharacterStatisticsKey are patterns for validation, backup and atomic writes; AppEnvironment.LocalDirectory | New QuestProgressStore under LocalDirectory/ROQuestie, versioned per identity; active/history separate from AppSettings and catalog. Stable quest_key, step_no, text anchor/content hash; preserve missing/changed quests and require review for ambiguous remapping. Read-only backup recovery must not overwrite corrupt/newer primaries. |
| Settings/presets | SettingsStore schema 13; SettingsSnapshot.Apply copies serialized model properties while preserving live references, registry is specially merged | Only visual QuestOverlaySettings in AppSettings/presets via normal migration. Active quests/history remain outside presets. No changes to other coordinates; flush progress on scope change/close, debounce saves. |
| Ordinary overlays | OverlayWindow.xaml.cs SyncOverlayToGameWindow/ApplyGameWindowBounds/UpdateHostVisibility/UpdateSettingsOcclusion, ForegroundProcess, OverlayOcclusion | Separate quest rendering/interaction adapter, preserve client-relative DIP, DPI transforms, no game/client switches writing X/Y. Reuse visibility/occlusion policy without rewriting shared host. |
| Input/focus | Ordinary host sets WS_EX_TRANSPARENT + WS_EX_NOACTIVATE outside edit mode; edit mode can Activate(); DatabaseOverlayWindow.ShowForGame deliberately Activate()s and focuses search | Neither path directly satisfies clickable nonactivating quest buttons. Dedicated interaction spike and Windows click tests required: only visible buttons catch clicks, transparent world passes through, no focus theft/modifier blocking. No cross-process owner/GWL_HWNDPARENT. |
| F9 | GlobalInputHook.ToggleOverlaysPressed -> App.ToggleOverlays -> OverlayWindow.SetOverlaysVisible; user-configurable OverlaysHotkey | Share the existing visibility decision with quests; no second hook or hard-coded F9. Coordinate busy-edit/finish behavior and More lifecycle. |
| Navi | Models/DatabaseModels.cs MarketListing.NavigationCommand; DatabaseDetailWindow.ShowMarketListing -> private TryCopyToClipboard (bounded retries, nonfatal failure) | Existing feature COPIES only; no verified command-send path found. Reuse clipboard behavior through a small helper when implementing stage 4. State “Command copied”, never “navigation started”. Typed validation; explicit wiki target selection if multiple, no candidate-NPC auto-promotion. |
| Tests/CI | RoAura.Tests; .github/workflows/validation.yml | Add focused reader/store, scope/preset, navigation and input tests. Current push filter does not include feature/roquestie-mvp; add this branch or dispatch validation explicitly after a coherent code block. Never rely on a push here automatically testing it. |

Planned new components: quest models, read-only catalog reader, progress store/controller,
QuestsPage, QuestDetailsWindow, QuestOverlayWindow/adapter, visual settings and focused
RoAura.Tests cases. Existing touch points above are a plan, not files already changed.
No new capture technology, runtime installation dependency or automatic quest detector.

## Sample walkthrough / navigation findings

- Lost Child: exact key irowiki-classic:Lost_Child_Quest, 13 numbered rows.
  Step 1 has ra_in01 384/246 AND rachel 100/240; requires explicit target choice.
  Step 3 has ra_in01 372/200. Step 12 has only bare 10/0: ineligible for navi,
  do not infer map or “repair” the source hint.
- Friendship: exact key irowiki-classic:Friendship_Quest, 14 numbered rows.
  Step 1 has two lighthalzen points; step 2 has three targets across two maps.
  Bare 200/0 and 400/0 hints must not become coordinates with an invented map.
- All these points have assurance wiki_literal_not_npc_verified. They are wiki
  references, not tested SpringRO NPC targets. Narrative rows elsewhere retain
  kind/source_step and source order; Next never proves in-game completion.
- Preserve literal requirements/rewards and NULL levels as unknown.
  Do not derive guaranteed rewards/requirements from script getitem/countitem.

## Risks and validation gates

1. Highest risk: selective clicks, nonactivation, fullscreen Z-order, More focus,
   settings occlusion and mixed-DPI. Existing Database is intentionally activating;
   copying it would violate the small overlay requirements.
2. Session state currently reaches the App statistics callback behind statistics
   health guards. Quest identity observation must not depend on statistics store
   success. A separate subscriber/controller must validate current revision.
3. Multiple clients and late launch cannot safely infer CID. An explicit offline
   profile is required; never reuse last CID after packet-off/logout.
4. Enum/index coupling and persisted navigation preferences can misroute Settings
   if Quests is inserted naively. Preserve current numeric enum values.
5. Presets reflection-copy visual options; storing progress there would reset it.
   Catalog refresh must not silently clamp/reassign saved steps.
6. ToC adapter has no reusable generic provider class; avoid an unrelated SQLite
   refactor. Unsupported schema/corrupt files must fail only the quest module.
7. Catalog adds ~73 MB uncompressed. Check real portable/installer payload,
   provenance/license distribution and no personal data at stage 5.
8. Current docs retain pending ToC, CID/relog, multi-client and DPI manual gates.
   Existing CI success must not be restated as full in-game acceptance.
9. CI does not currently auto-run this feature branch. Windows testing must be
   wired explicitly before claiming the completed implementation block tested.

## Actual validation for stage 0

- GitHub connector verified remote main/research heads and created the feature branch.
- Actual SQLite opened mode=ro; schema, integrity, foreign keys, key uniqueness,
  counts, freshness, sample steps and locations inspected with Python sqlite3.
- Runtime sources unchanged. No app tests or portable/installer build performed:
  this host has no dotnet executable and cannot perform Windows/WPF/game checks.
- No test archive produced. No main/research/release updates, merges, tags,
  product version changes or public releases.
- Next authorized work: stage 1 reader/progress and focused tests, followed by UI,
  interaction, navi and packaging gates in specification order.
