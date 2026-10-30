# Project state

## ROQuestie game-first windows (2026-10-30)

Owner explicitly superseded conflicting stage-2/original UX: all normal quest
work belongs in a large game window, plus an independently toggled edge tracker.
Main RoAura contains settings only. See `ROQUESTIE_PRODUCT_DIRECTION.md`.

Development remains `feature/roquestie-mvp`, stable main `10adb870` / 3.2.0,
based on stage-2 documentation `2fddd08`. Product version/manifest unchanged.

Implemented in this block:
- Large activating game browser, full-width walkthrough/current-step highlight,
  catalog/active/history and structured combined requirements/rewards with raw
  source retention. Main-app quest settings are separated from the browser.
- Independent F6/F5 hotkeys in the existing service/Settings UI, collision-safe
  new bindings, and a shared F9 mask that retains independent requested states.
  Legacy hotkey values remain intact; Windows modifiers join the existing bypass.
- Transparent text tracker and tiny nonactivating mouse-action surfaces, independent
  per-quest controls and drag handle. DPI/physical client positioning, game lifecycle,
  settings/browser occlusion and exact-client focus restoration. No cross-process
  owner or ordinary-overlay interactivity. Passive quest HWNDs are excluded from
  existing overlay control masks to preserve other overlays.
- Same-step wiki-only navigation with explicit multi-target choice and shared bounded
  clipboard behavior; no SendInput command path. Other stays reference-only.
- Settings schema 15 adds hotkeys/browser geometry; external progress schema stays 1.
  Client switches invalidate stale automatic CID display until a fresh revision;
  manual selection remains labeled. Capture/parser/market/ToC behavior unchanged.
- Focused source-format/navigation/hotkey/preset tests; optional portable startup
  launches a separate explicit fake-game probe and uses real OS mouse/keyboard input.
  Native captures supplement browser RU/EN snapshots. Probe/injected input is gated
  by --smoke-test/explicit probe flag and never used for normal game interaction.

Local validation: Windows-targeted app/test cross-build, XAML XML and RU/EN
key/reference checks. Exact implementation Windows CI result is the authority
for full tests, portable startup and cross-process input. Real SpringRO 2K,
protected/fullscreen, relog/multiple clients, long-session focus and actual mixed-DPI
acceptance remain unverified. This is a test branch, not a release candidate.
No main/research changes, merges, tags, product version bump or public release.

Game-first verification: [Actions #41056398770](https://github.com/alerion08/RoAura/actions/runs/41056398770)
SUCCESS on `87b3d5c84b183fae985e880d7793e2e5473e1277`: 290 tests passed,
zero skipped/failed, plus eight ToC checks. `build-release.ps1`, portable startup,
RU/EN resources and clean payload passed. The startup includes real cross-process
mouse/keyboard delivery: Next without activation/click leakage, transparent
margins/text, Shift/Ctrl/Alt and ordinary keys, independent F5/F6, browser typing
isolation/exact-client focus restore, F9, drag beyond the handle, production Escape
handler, settings occlusion, resize-coordinate retention and minimized-client hiding.
The fake game's native Alt system menu is suppressed in the probe only; all game
input assertions remain enabled. The probe begins with main settings hidden.

Artifact: [RoAura-development-70-87b3d5c](https://github.com/alerion08/RoAura/actions/runs/41056398770/artifacts/12977186474),
one portable archive and 15 UI PNGs. Portable SHA256:
`83b9517226ef42de3d482b4da1d3a44f989f5f0c614d78b75daa7a8a4b4afa4b`.
Native browser/facts/tracker and Russian walkthrough rasters were inspected:
full-width steps/current highlight, structured facts and compact multiple quest
rows are present. The CI fake client exceeds its desktop width/height, so native
captures clip the right/bottom desktop edge; these do not establish real screen-fit
or mixed-DPI acceptance. Offscreen scale variants are rendering checks only.
Real SpringRO checks listed above remain open; no release readiness is claimed.

## ROQuestie stage 2 — application UI and persistence lifecycle (2026-10-30)

Branch `feature/roquestie-mvp`, based on stage 1 / audit documentation `7f7c2ce`;
stable base remains `10adb870` / 3.2.0. No product version/release change.

- Added the supplied scroll icon and Quests after Database in Services. Shared
  sidebar spacing is slightly tighter. Explicit destination mapping preserves
  Settings index 9 and all prior feature IDs; Quests is appended as 10 and works
  without packets on every server, including reference-only Other.
- Added four theme-consistent RU/EN tabs: local catalog search/provisional filters,
  active guides, manual history/restore and visual settings with an in-app preview.
  Full details preserve all source rows, original requirements/rewards, wiki
  coordinates, freshness/hash/URL and unverified-server labels. More currently
  uses the application's detail pane; game overlay/More/navi follow in stages 3/4.
- QuestWorkspace observes existing session snapshots independently of the statistics
  health gate. Explicit offline choices include saved CID records/backup-only files
  and one local profile per server. Mutable aliases do not move history. Unknown
  identity never inherits a CID; switching server clears manual profile selection.
- Debounced saves (400ms), scope flush and normal-exit flush use independent document
  copies for background writes. Failed saves preserve pending state and block silent
  scope/exit loss. Settings schema 14 adds only nested visual options; presets retain
  live references and do not replace quest history or other overlay coordinates.
- Feature CI provisions the pinned v5 snapshot to Data (not committed a second time)
  and includes provenance notices. Source builds can extract the same research blob;
  absent catalog remains a nonfatal Quests-only error. Source/license/package final
  review remains a stage-5 public-release gate.
- Added controller/scope/preset/migration regressions. Full app/test cross-build and
  XML/RU-EN key parity/reference validation pass locally. Windows full tests,
  portable startup and RU/EN UI captures are checked on the implementation commit.
  No actual in-game, mixed-monitor DPI or new overlay-input checks are claimed.

Only data/UI/persistence lifecycle changed. Capture/parser, Gold Goblin, ToC adapter,
ordinary overlay host, global input hook and hotkey implementations are untouched.
The existing startup smoke now tests offline quest navigation/catalog and exports
optional UI PNGs; it still runs in isolated data directories with real capture off.


Stage-2 verification: [Actions #41046358448](https://github.com/alerion08/RoAura/actions/runs/41046358448)
SUCCESS on `4323935aa818acc6f6e75f9f3df683a9ab147f5d`: 276 tests passed,
zero skipped/failed, plus eight ToC checks. Portable startup, clean payload and
real-app offline quest/catalog smoke passed. Artifact:
`RoAura-development-65-4323935aa818acc6f6e75f9f3df683a9ab147f5d`
([download](https://github.com/alerion08/RoAura/actions/runs/41046358448/artifacts/12972392809)).
Its single CI archive contains the portable and 12 optional RU/EN PNGs. Source
catalog is still the pinned schema-v5 snapshot, not a new research revision.

Visual review corrected technical record captions and default Windows tabs to
shared RoAura styles, collapsed empty errors and detached screenshot layout from
the larger parent window. Catalog/active/settings/history rasters were inspected.
Some offscreen History/Settings captures show top glyph baseline cropping after
mixed-density rendering; these are not accepted as real-window DPI verification.
Actual Windows minimum-size, 100/125/150% / mixed-monitor visual acceptance remains
open. No game overlay/focus/F9/navi acceptance or release readiness is claimed.

## ROQuestie stage 1 — catalog and manual progress (2026-10-30)

Branch: `feature/roquestie-mvp`, stable base `10adb870` / 3.2.0,
implementation starts after stage-0 audit `bd3d152`. Version metadata unchanged.

- Added an isolated asynchronous/cancellable consumer schema-v5 reader using
  Windows system SQLite. It validates schema/integrity/foreign keys, required
  columns, bounded rows/text, identities and stored step counts. Original wiki
  text, NULL values, narrative kinds, freshness and location assurance remain
  distinct. Search is local; no packet/network activity or new dependency.
- Added manual progress and history keyed by canonical server + CID or explicitly
  selected local-profile GUID. Observed identity requires ActiveObserved and
  matching server with packets enabled; unknown/logout/off never inherit CID/AID.
- New independent `LocalDirectory/ROQuestie` JSON schema 1 store uses atomic
  replacement, flushed writes, backups, stale-writer checks and read-only recovery.
  Invalid/future data cannot be overwritten. No old quest format exists: unknown
  versions are rejected, not implicitly migrated. Presets/settings are untouched.
- Stable quest keys and instruction anchors preserve missing quests/history.
  Unique text matches can remap changed catalog steps; missing/ambiguous matches
  retain the saved position and require explicit selection. Unchanged repeated
  text retains the chosen row. Next never completes a quest; completion requires
  an explicit manual confirmation and retains timestamped history.
- Added 31 focused C# cases, including real pinned schema-v5 catalog tests.
  Local execution passed all 31 using actual source/tests and xUnit assertions,
  with Linux system SQLite substituted only for native Windows SQLite. Full
  Windows-targeted app + test project cross-build succeeded (existing warnings).
- Development Windows workflow now includes this branch, extracts the exact
  research catalog at `41bfc483` to RUNNER_TEMP and verifies its SHA before the
  real Windows reader test. It runs the full suite and existing portable checks.
  The workflow result for the implementation commit is the Windows authority.

This is the data layer only: no navigation/page/overlay/input/capture/ToC edits,
no user-visible quest UI yet, no runtime catalog payload bundled in this stage.
UI/controller stages will schedule debounced saves and flush on scope/close;
store methods are explicit now and not attached to the application's lifecycle.
No gameplay, DPI, hotkey or focus acceptance is claimed. No merge/tag/release.
See `ROQUESTIE_DATA.md` for APIs and validation boundaries.

Stage-1 Windows verification: [Actions #41041421010](https://github.com/alerion08/RoAura/actions/runs/41041421010)
SUCCESS on `b6524ae3a7fbd50f220251a6806c9863221a74ca`: 264 app tests
passed, zero skipped/failed (31 new quest cases included); eight ToC Python
checks passed; actual pinned quest catalog read through Windows SQLite passed.
Portable startup smoke, build and clean-payload verification passed. One test
artifact: `RoAura-development-62-b6524ae3a7fbd50f220251a6806c9863221a74ca`.
This is a CI test portable with unchanged 3.2.0 metadata, not a new public release;
it has no quest UI/catalog distribution yet. In-game/manual checks remain pending.

## ROQuestie stage 0 audit (2026-10-30)

Active scoped branch: `feature/roquestie-mvp`, created from stable main
`10adb870f760f080711fb29d9d77fd32d1ff120c` (3.2.0). Research stays at
`41bfc483311fa0e24ee84d1b1edcb0b4558914bd`; consumer SQLite schema v5.
These match the approved specification. Read-only integrity and foreign-key
checks passed on the actual catalog (286 quests / 3,420 guide rows).
See [integration audit](ROQUESTIE_INTEGRATION_AUDIT.md) for the pinned catalog
hash, actual APIs, planned touch points, sample navigation ambiguities and risks.

No application behavior changes in this stage. Implementation stages 1–5 are
pending. Existing Navi is clipboard-only; no verified send-to-game API was
found. The ordinary overlay is click-through, while Database intentionally
takes focus: neither may be copied blindly for interactive quest buttons.
Progress must remain separate from presets and use server-qualified CID or an
explicit manual local profile. No automatic quest completion from research.

Current development workflow does not auto-trigger on this branch; wire or
dispatch Windows validation after a coherent implementation block. This host
has no dotnet executable; no app tests, portable or Windows/game validation
were performed for this documentation-only audit. No release/merge/tag/version
change is authorized. Stable main and the research branch are unchanged.

## RoAura 3.2.0 publication (2026-10-30)

User explicitly authorized preparation and publication of this release. Release branch:
`release/3.2.0`, based on `0f0468f` from `feature/settings-character-overlays`.
Main before publication: `4f75b78` / v3.1.1. Version metadata is now 3.2.0.
The release PR must pass full Windows tests, portable startup, installer compilation,
ToC importer/integrity validation, clean-payload checks and checksums before merging.
The main push then creates v3.2.0 and publishes portable/installer/checksums through release.yml.
Historical "no release authorized" notes below are superseded by this instruction.
New ToC gameplay, multi-client/relog scope behavior and DPI visual acceptance remain
unverified; publication notes explicitly retain these limitations. Prior feature CI passed
233 app tests, eight Python tests, startup and portable verification.

## Header settings and overlay visual corrections (2026-10-30)

Same development branch, based on tested `031591c` (233 Windows tests, eight Python
checks, package startup/portable passed). Settings now opens from the header button
instead of the hotkey legend and sidebar entry. Overlay helper cards share the equipment
info-card template; titles and field labels explicitly use the same theme text color.
About uses its full-color image directly, without feature-availability conversion.
Settings/model data and overlay coordinates are unchanged. New visual changes require
manual Windows/DPI review; in-game acceptance remains pending as recorded below.

## Settings, character scopes and ToC presentation (2026-10-30)

Active branch: `feature/settings-character-overlays`, based on `c1e2804`.
Stable main remains 3.1.1; no merge, tag or release is authorized.

- Settings now contains General (shared Home connection controls, server and language),
  all five hotkey editors, Character Manager and optional navigation visibility.
  Other is last in both server selectors. Home and Settings remain reachable.
- Detected CID metadata has stable identities; aliases and assigned servers do not
  rewrite captured identities/history. Presets retain the global character registry.
- All five overlay model types support multiple character targets. An empty selection
  retains legacy all-character behavior; assigned overlays require an observed matching
  server/CID, never AID. Existing coordinates and overlay options are retained.
- Cooldown wizard detects a draft Skill ID before creation and cancels pending learning
  on close/back; no half-created overlay is inserted.
- Uniform empty list/editor cards; structured item/monster statistics and collapsed
  skill descriptions. Existing headers and tabs retain their styling.
- ToC catalog schema 2 adds 53 spawn-location records from 48 official map pages,
  preserving exact source delay fields. Database and timer hints share these records.
  Missing published intervals remain unknown; saved timer durations are not overwritten.
- Settings schema 13 adds registry, scope and navigation preferences with legacy defaults.
- Local validation: .NET/WPF cross-build succeeds; eight Python tests and full SQLite
  integrity/count/hash validation pass. Windows regression/portable results are recorded
  in the branch Actions run. New behavior has NOT been verified in Ragnarok/ToC.
- Smoke startup uses isolated temporary data and disables real packet capture.

Manual acceptance required: relog/server switching and multiple clients; all-character
and multiple-character overlay targeting; global hotkeys and pre-creation learning;
RU/EN layouts and empty-state alignment at Windows DPI; ToC packet statistics/equipment.

## ToC integration and packet feature control (2026-10-29)

Active branch: `feature/toc-server-integration`, based on the completed ToC
catalog branch (`9783f9d`). Stable `main` remains 3.1.1; no version/tag/release change.
The prior catalog-only block below is historical; its Windows CI passed 218 app
tests, six Python tests and portable verification.

Implemented in this block:

- Explicit ToC profile, SQLite-backed database/items/MVPs, offline item descriptions,
  scripts, restrictions, monster skills/rules, normal/MVP drops and linked maps.
  The read-only adapter uses System32 `winsqlite3.dll`, already supplied by supported
  Windows, without a new installed dependency or a duplicate JSON catalog.
- ToC NPC prices and manual valuation in Gold Goblin; no SpringRO vending prices,
  item HTTP requests or market/watch UI for ToC. Unknown respawns stay unknown;
  ToC MVP timer intervals are entered manually.
- Separate persisted ToC timer and price-rule collections; transient Gold Goblin
  rows are retained per server during switches instead of being cleared. Legacy collections and
  layout settings are retained. Settings schema is 12 so older releases preserve a
  newer-version recovery copy. Packet preference remains the existing persisted
  `PacketCaptureEnabled` field and participates in presets.
- Persistent navigation entries: unavailable features are disabled, gray (including
  icon pixels, preserving alpha) and slightly transparent. Selecting a server or
  switching packets off returns from an unavailable page to Home.
- RU/EN Home warning and packet toggle. Off stops Npcap, disposes the SpringRO
  client reader, invalidates queued events and learning, and hides packet overlays.
  Offline Database, manual MVP timers and SpringRO HTTP Market remain independent.
- ToC character statistics reuse the existing verified packet/CID ledger with a
  distinct server key; the history selector follows the selected server. Server
  switches invalidate the previous capture even when address/port are unchanged.

Local validation: app and full test project cross-compile with .NET 8;
actual C# ToC adapter/catalogs and feature matrix pass seven headless checks
(using system SQLite on Linux in place of Windows SQLite only). Six Python
import tests, snapshot/hash checks, XAML XML and RU/EN key parity pass. Added
Windows regressions cover the actual snapshot, NPC prices/overcharge, rule/timer
isolation, disk/preset persistence, grayscale alpha and packet-off workers.
The branch's Windows Actions run is the authority for full tests, native Windows
SQLite and portable startup, including live navigation/toggle smoke assertions.

Manual acceptance is still required: ToC executable/process selection, real
Npcap/login/CID/EXP/loot/status events, relog/reconnect, switch-server history,
packet off/on during play, RU/EN layout and DPI. No ToC-specific wire decoder is
claimed; the existing standard parser is used. Do not call this release-ready.

## ToC catalog preparation (2026-10-29)

GitHub was checked directly: stable `main` is `4f75b78` (3.1.1), published
`v3.1.1` has portable/installer assets, and release workflow run `40877228467`
completed successfully. Older 3.0.0/current-branch statements below are historical
and do not describe the current stable release. Remote branches include
`release/3.1.1-character-identity`, `release/3.1.0-springro`,
`feature/character-statistics`, `fix/gold-goblin-zero-prices`,
`integration/2.14.0-audit-fixes` and `dev/roaura-2-ui`.

Active task: `feature/toc-local-database`, based on stable main. Prepare a compact
ToC item/monster/MVP catalog for later Database, MVP Timers, Statistics and Gold
Goblin integration. See `TOC_DATABASE.md` for schema, source, refresh and limits.
Snapshot completed: 9,894 items, 1,061 monsters, 61 MVPs, 4,946 normal-drop
relations and 156 MVP rewards in a 12,386,304-byte SQLite file. Indexed drop/reward
references all resolve. Six local Python regressions plus snapshot/payload/hash
checks pass. Full Windows application/package checks belong to this branch's
Actions run; local .NET SDK is unavailable.
No settings migration, packet/UI changes, version bump or release is included.

Verification boundary: user provisionally accepted SpringRO Skill ID, Status ID,
reminders, equipment and own AID behavior. Earlier character-statistics stages
have field feedback, but ordinary 3.1.1 CID/relog/Character Select and long-session
CPU/reconnect acceptance are not separately recorded. Successful release CI proves
automated build/package checks, not these gameplay checks. ToC runtime support
has not been verified in game.

## Release correction: 3.1.1 (2026-10-29)

The published 3.1.0 regular build omitted the DEV character-session and statistics path. This release candidate promotes map-login observation, flow-bound CID sessions, character-specific statistics storage, the active-character browser and shared Gold Goblin/session clock to the regular build. DEV-only raw diagnostics, updater isolation and the separate RoAura-Dev data directory remain gated. The normal build writes new character records to RoAura's local data directory; DEV history is not automatically migrated. A late launch after map login cannot infer CID. Windows CI and in-game relog/Character Select checks remain necessary before calling this correction verified.

## Release preparation: 3.1.0 (2026-10-29)

The user provisionally accepted the latest SpringRO DEV behavior: Skill ID, Status ID, reminders, equipment and own-character AID work in their game test. The normal build previously excluded the client status bridge and verified AID observations. Branch `release/3.1.0-springro` enables that path for SpringRO in normal builds, keeps lossless packet sampling DEV-only, and updates version and bilingual notes. This is a release candidate until Windows CI validates the normal portable and installer. The client bridge remains tied to one executable hash and uses a high-frequency read-only sampler; extended-session CPU and reconnect were not individually accepted. Do not claim those checks passed.

Last updated: 2026-10-29

Latest SpringRO investigation: 16 exact Enchant Poison samples were received.
Static analysis of the supplied client located the actual 0x0983/0x0196
handlers and confirmed the standard status/actor offsets (+2/+4). The visible
receive-buffer path contains no field decoder; the runtime-resolved `recv`
implementation remains unidentified. See the final section of
`PACKET_TRACKING_AUDIT.md` for reproducible addresses and limits. General Status
ID learning remains unresolved in the normal packet path; accepted Hiding
behavior is unchanged. An opt-in DEV bridge is now awaiting gameplay testing.

DLL follow-up: the supplied installation includes Gepard Shield 3.0. Its
network-diagnostic references lead into protected `.vmp0` code; this does not
yet prove a status-field transformation. `spring.dll` identifies GRF resource
encryption/compression, and `dinput.dll` identifies ROExt. No validated network
decoder was recovered from these files. DLL fingerprints and evidence limits
are recorded in the audit; no additional identical live capture is requested.

The next independent experiment is a read-only, one-shot client packet-buffer
probe (`tools/Capture-SpringRoClientStatus.ps1`) paired with the existing DEV
wire sampler. This captures a different observation point from prior repeated
wire tests. It does not implement a Status ID decoder; see the audit for
limits and instructions.

The first protected-client probe had access but returned zero frames in 5,695
reads/90 seconds (~63 Hz), a limitation of the PowerShell polling loop. It is
not evidence about decrypted status values. The updated probe uses a compiled
bounded loop. Its second field run captured seven identical decoded 0x0983
frames for Enchant Poison: EFST 6, verified own actor and 180000 ms. A separate
0x0196 status 1689 is not attributed to Enchant Poison. Same-run RoAura wire
log is needed to align individual frames before any generic application path.
The matching log has now arrived: all seven wire frames align uniquely with
client frames within 0.969–7.286 ms. Exactly bytes +2..+5 change before
dispatch; client Status ID is 6 and actor is the verified player. This proves
the present raw parser cannot identify ownership or Status ID for these
frames. It does not reveal the changing transform or establish a safe general
client-memory capture path; see the experimental bridge below.
Second client run confirms status 5 on three starts and five removals with
own actor; the corresponding wire layout events precede them by 2.425–5.255
ms but the wire sampler did not record their raw bytes because it is limited
to skill 138. Generic decoded event delivery remains the implementation task.

Experimental implementation: SpringRO DEV enables the client status bridge
automatically when SpringRO is selected; no command-line flag is needed.
For one running client matching the analyzed EXE hash, an inbound `0x0983` or
`0x0196` triggers up to 15 ms of read-only client-buffer observation. Only a
double-read stable frame with the same opcode, all unchanged wire bytes from
offset +6 onward and verified own actor reaches the existing status pipeline;
unmatched events are dropped for these two opcodes. Other servers and regular
builds retain their prior path, and correlated Hiding is unchanged. The
matching code passes focused headless tests; actual Windows gameplay, first
event latency, CPU impact and reconnect remain unverified. This is a DEV test
build candidate, not a general decoder or completed fix.
The first Windows feedback log (`debug(8).log`) showed DEV assembly
`435544ba-191f-467a-83d2-6244522fcb83`, no bridge lines and continued raw
status observations. The user confirms the flag was supplied. The log proves
the bridge was not active in the running process, but cannot distinguish a
different artifact from argument handling or settings timing. Automatic DEV
activation and explicit enabled/missed lines make the next test diagnostic;
in-game matching still requires confirmation.

Next Windows feedback (`debug(9).log`) confirms the bridge really activated
and decoded status 6 on eight starts. It missed seven other 0x0983 starts and
one 0x0196 removal during the Enchant Poison sequence; a timer continued
after an unconfirmed removal. The 15 ms packet-callback poll is therefore
insufficient. The bridge now samples the read-only client dispatch buffer on
an independent worker, keeps only the last 100 ms of status frames (bounded
to 128), and matches Npcap events against that cache before/in addition to
the short callback poll. This is still awaiting Windows gameplay and CPU
measurement; no effect-specific expiry is inferred from a missed removal.

Third Windows feedback (`debug(10).log`) confirms generic decoded status 6
starts/removals reach Effects, including early expiration. Decoded status 4
arrived with 240000 ms, then the old correlated Hiding fallback immediately
published the same active ID with zero duration, erasing the timer. The
fallback now publishes only if no matching decoded Hiding transition arrived.
Status 5 removals decoded but its starts were missed in the reported sequence;
the bridge now logs recent decoded-frame counts by opcode, unchanged packet
tail and own actor for missed starts, to locate the generic matching failure.
The continuous sampler reports about 0.94–1.0 million cross-process reads/s;
CPU cost has not been measured and remains a material DEV-only concern.

## Current product

RoAura is a Windows x64 WPF overlay for Ragnarok Online. It reads incoming game packets through Npcap and provides configurable overlays and supporting tools.

Current published release: **3.1.1** (verified on GitHub 2026-10-29)

Stable branch: `main` (unchanged by this work)

Active integration branch: `feature/toc-server-integration`

Previous SpringRO investigation branch: `feature/character-statistics`

Active redesign branch: `dev/roaura-2-ui`  
Audit integration branch: `integration/2.14.0-audit-fixes`  
Release review: [PR #5](https://github.com/alerion08/RoAura/pull/5)

## Branch status

### main

Stable public code. Do not use it as the source for unfinished RoAura 2 work and do not merge into it without explicit release approval.

### dev/roaura-2-ui

Baseline for the redesigned interface and existing RoAura 2 functionality.

### integration/2.14.0-audit-fixes

Contains the RoAura 3.0 redesign, audit/infrastructure work and user-tested follow-ups. PR #5 is being promoted from the development review to the 3.0.0 release review targeting `main`, following user acceptance on 2026-10-27.

The integration work includes:

- safer settings and preset persistence;
- recovery and diagnostic logging;
- Npcap reconnect and capture changes;
- overlay/window/DPI and Z-order changes;
- global input hook safeguards;
- Market caching, indexing, timeout and retry work;
- updater, installer and build hardening;
- centralized version metadata;
- regression tests and a non-release validation workflow;
- follow-up fixes for selecting the active Ragnarok window and restoring overlay Z-order.

## Implemented functional areas

- Cooldowns.
- Effects and Buff/Debuff notifications.
- Buff Panel.
- Reminder.
- Equipment tracking and learned variants.
- MVP Timers.
- Loot, Zeny, Base/Job EXP notifications and farming statistics.
- Gold Goblin session valuation.
- RTM and SpringRO item/monster databases.
- SpringRO Market search and cache.
- Full-settings presets.
- Global and feature hotkeys.
- Russian and English UI.
- Portable package, installer and automatic updater.

These areas already existed before the audit patch; the patch is intended to stabilize infrastructure rather than redesign them.

## Current verification state

Automated Windows validation has passed for the integration branch:

- compilation;
- logic and UI regression tests;
- portable package creation;
- clean-package checks.

Automated success does **not** prove correct behavior inside the game client.

## RoAura 3.0 release acceptance (2026-10-27)

The user tested build `28fa0d5`, reported that everything works well, and accepted the project for the next release, named 3.0. This supersedes the earlier pending manual-acceptance state of the development follow-ups below. The user did not enumerate a Windows/client/DPI test matrix; retain that context when diagnosing future reports rather than claiming specific combinations were individually tested.

The 3.0.0 candidate changes version metadata, release documentation and packaging checks; gameplay behavior matches the accepted build. PR #5 targets main for full Windows tests, portable startup, installer and clean-package/SHA-256 validation before merge/publication. Both the project and embedded manifest use 3.0.0. Existing settings schema remains unchanged.

The first 3.0.0 release PR run passed all 104 tests and built both packages. A simultaneous development run failed the existing visible watch-search test when another WPF test class could steal keyboard focus. Window/focus test classes now share a non-parallel xUnit collection, and search focus is established after the dialog's initial dispatcher work. Assertions remain intact; application behavior is unchanged. The final head must pass both release and development validation before merge.

## Historical regression areas

The following areas guided development testing and remain the reusable regression focus for future changes (current release acceptance is recorded above):

- all ordinary overlays are visible above the active client;
- Database panels open over the correct client without jumping between windows;
- overlays survive switching between RoAura, Ragnarok and other applications;
- fullscreen/windowed transitions and multiple monitors;
- multiple clients or launcher/client processes with the same executable name;
- Alt, Ctrl and Shift remain available;
- Npcap starts, tracks packets, survives relog and reconnects after endpoint changes;
- Cooldowns, Effects, Reminder, Equipment and MVP tracking still react to packets;
- Existing settings and presets load without data loss;
- clean first launch and update flow.

Two follow-up Z-order/window-selection fixes were made after real testing showed invisible overlays. They compile and pass CI but still require confirmation in the actual client.

## Sensitive design decisions

- Do not assign a Ragnarok window as the cross-process owner of an overlay through `GWL_HWNDPARENT`; this previously interfered with modifier keys.
- Active allowed Ragnarok windows have priority; fallback selection must ignore hidden/launcher windows and prefer a suitable visible game window.
- Protected/fullscreen clients may reclaim the topmost layer, so logical `Topmost` state alone is not proof of actual Z-order.
- Do not permanently overwrite saved layout coordinates merely because the game window is temporarily smaller.
- Settings from a newer schema and corrupted settings require preservation and recovery; avoid destructive automatic rewrites.
- Immediate-mode Npcap and multi-adapter capture must be checked for CPU usage during long sessions.
- Market refresh follows the configured 1–1440 minute interval (default 10); the former ten-minute service floor is removed. The UI warns about server blocking. In-flight crawls do not overlap, and server-directed back-off remains in effect.
- A single RoAura instance is the default. Developer/test multi-instance behavior is not currently implemented.

## Numeric layout input fix (2026-10-27)

The first attempt changed 44 layout/opacity bindings to `LostFocus` and passed 54 Windows tests, but the user reported the same min/max jumps in their running app. Those tests exercised isolated bindings and did not establish the cause of the remaining runtime report. Do not count the issue as manually verified.

All 44 editors now use `Controls/NumericTextBox`: Text is an unbound draft; only Enter or focus loss writes Value to the model. Source notifications cannot replace a draft. Escape restores the current model value. Existing bounds/settings formats stay unchanged. Empty, invalid and non-finite input retains the valid model value with validation; dot and comma decimals work. Closing the main window commits its focused numeric editor before saving.

Regression coverage includes the actual numeric controls/bindings, source updates during typing, readback at unchanged bounds, and a visible WPF window with Enter, Escape and focus transfer. Local .NET tests remain unavailable (no SDK); use Windows CI for tests and portable packaging. About now displays the short source revision from assembly metadata to distinguish integration builds sharing version 2.14.0. Because only one instance runs, fully close the previous RoAura before launching a new test package; a second launch otherwise activates the old process.

Manual input/persistence checks and the existing game-client blockers remain pending.

## Layout reset report and fix (2026-10-27)

After build `8238353f`, the user reported all overlays at (0,0). Inspection found that `SyncOverlayToGameWindow` called `ClampPositionsToGameArea` on client size changes, overwrote every overlay model coordinate and queued an autosave. This existing path could destroy saved positions on transient small bounds or a switch to a smaller client. The user's settings/logs are not available, so it is not yet proven that this path caused their specific reset.

Removed resize-driven model clamping and autosave. Window size/DPI/client changes now update only the host geometry; saved positions may lie outside a temporarily smaller client and return when the original bounds return. Explicit dragging/coordinate edits and one-time legacy coordinate migration remain unchanged. Regression tests cover every overlay category through small/invalid bounds, client switches, DPI changes, restore and save/reload, plus numeric-coordinate binding initialization/selection changes.

Already overwritten positions cannot be reconstructed from zeros. Recovery candidates are an explicitly saved preset in `%APPDATA%\RoAura\Profiles`, `settings.json.bak` (rolling backup, may already contain the reset), `Recovery/before-ui-2.0.*`, `Recovery/working.*.json`, and `Profiles/Backups`. Preserve a copy of the current RoAura data folder before recovery; do not auto-apply guessed positions or replace unrelated settings. Manual resize/client-switch checks and investigation of the user's actual stored values remain pending.

## Database watchlist (2026-10-27)

Active block: SpringRO Market item watchlist and notification settings. Item search rows and detail headers have synchronized stars (off by default). Adding requires confirming a maximum whole-Zeny price or Any price; removing turns tracking off. Manual name/ID search uses a separate suggestion list that never selects or rewrites the typing draft automatically.

Database settings now contain General, Watchlist and Notifications tabs. Rules and notification appearance/sound persist in settings and named presets. Notifications support `%name`/`%price`, an embedded custom icon or item icon, sizes, text/background colors with alpha, padding, spacing, X/Y and display duration. They share the ordinary overlay host, F9 and client-relative geometry.

Only complete successful live SELL snapshots are evaluated by item ID. The first fresh snapshot can notify about existing qualifying offers. New sellers/variants/prices can notify once per item per refresh (cheapest new match); quantity-only changes and unchanged offers are silent. Retained history now suppresses repeats after absence/return and restart until expiry; the original session-only rearm behavior has been superseded. Failed/partial responses preserve the previous cache and matching state. Offer history is local and persistent, stored separately from settings and presets. Watches are SpringRO-specific.

Added tests cover price boundaries, Any price, large amounts, deduplication, disappearance/reappearance, rule changes, settings/presets, consecutive refreshes, error preservation and autocomplete draft behavior. Local execution is blocked by the missing .NET SDK. The first Windows run compiled the app and passed 70 tests; a malformed HTML fixture in the new crawl test was corrected. Final automated results are attached to the integration commit’s Windows Actions run; manual game checks remain required. No release approval is implied.

## Watchlist layout follow-up (2026-10-27)

User screenshot feedback: increase the short refresh field; replace the full-width watch table/modal-only workflow with a left table/right inline editor resembling Gold Goblin; reduce oversized fields; use a real dropdown; show slot suffixes; narrow the star dialog by 20%.

Implemented shared `MarketWatchEditor` for inline and star-dialog entry, with 32-DIP fields and a separate popup that never binds back into the typing text. The right card keeps actions at its bottom. The star dialog is 400 DIP wide, and the refresh interval field is 30 DIP high. Database item search/detail names now reuse `ItemCatalogService.FullName`, including equipment slot variants; existing watch names are refreshed by ID at startup without changing price rules. The existing popup draft test now exercises a visible window and typing focus; a catalog regression distinguishes Jur [2]/[3]. Local .NET remains unavailable; automated results are recorded by this commit's Windows Actions run. Manual layout checks in RU/EN at 100–150% DPI and in-game modal checks remain pending.

## Notification controls and paused watches (2026-10-27)

Active Database follow-up: borderless star dialog centered between both database panels; permanent live notification preview; compact numeric rows and shared X/Y row; Show icon; sound audition and volume; game drag through the existing edit/finish lifecycle; per-rule pause checkbox in the third table column. Pausing preserves the item and price; resuming checks the next successful snapshot against retained offer history. Old rules default to enabled, and old appearance settings keep icons on and volume at 100%.

Preview and runtime now share a single notification view. Movement uses the ordinary overlay host and client-relative/DPI-aware mouse coordinates, with no resize-driven coordinate writes. Sound uses the existing MediaPlayer lifecycle. Regression coverage includes paused matching, older JSON defaults, settings/presets, centered gap geometry including a negative monitor origin, icon space removal and edit-mode cleanup/coordinate persistence. Local SDK remains unavailable; Windows Actions records automated validation for the commit. Manual RU/EN, mixed-DPI, dialog placement from both stars, drag/finish/focus and audio-volume checks are still pending.

## Persistent offer history (2026-10-27)

Latest Database task adds a history table beneath watched items: detection time, stall, map/coordinates and price, filtered by selected item. Every qualifying new shop is retained, while one notification per item still uses the cheapest new offer. Local persistence at `%LOCALAPPDATA%/RoAura/market-watch-history.json` prevents known offers from notifying again after restart, rule removal/readdition, pause/resume, or temporary absence. A different seller, price or equipment variant remains a new offer. General has whole-day retention (default 30, range 1–3650); cleanup runs at startup, hourly, on retention edits and on successful matching. History is separate from settings/presets and excluded from portable/source packages.

History saves atomically before alerts. A damaged/newer/unreadable file is preserved and cannot be overwritten by empty history; a visible warning reports persistence errors, and write failures keep in-memory deduplication with retry. New tests cover actual file restart, all shop metadata, retention/expiry, file preservation and write recovery. The watch table uses exactly three filling columns with centered checkboxes. Volume is rounded to integer percent and choose/listen buttons share a fixed height. Local SDK remains absent; Windows CI validates the final commit. Manual history display, restart suppression, day-setting behavior, checkbox layout and audio UI checks remain pending.

## Shared controls and live presets (2026-10-27)

Current UI block: shared rounded text/numeric/color fields, borderless blue dropdown arrows, application-wide slider style, reduced button caption padding and a common RGBA/HEX color palette. MVP main editor and wizard no longer queue stale search text; timer row changes close/filter-reset the popup under selection suppression.

Selecting a preset now copies the complete migrated snapshot into the live settings and existing observable collections. No process restart or second-instance race occurs. UI selection/bindings, server catalogs, hotkeys, watch subscriptions, language and capture configuration refresh. Transient notifications, learning and session counters reset; persisted positions and exact equipment variants come from the snapshot. Named JSON files and explicit create/update semantics remain compatible. Corrupt presets are validated before touching live settings. Offer history remains independent.

Local .NET 8 cross-build of the application and tests succeeds on Linux. WPF execution and portable smoke tests require Windows validation. Added coverage checks repeated full-profile transitions, every serialized field, collection identity, post-switch saving, immutable named snapshots, corrupt input, HEX/alpha, rounded templates and arrow/slider visuals. Manual RU/EN at 100/125/150% DPI, the reported MVP selection sequence, palette apply/cancel and A/B preset switches inside Ragnarok remain required. No merge/release approval is implied.

## Field clipping follow-up (2026-10-27)

User screenshots after a78cc4b show vertically clipped text in compact text/numeric/color fields. The shared rounded template added Padding as PART_ContentHost.Margin, while WPF TextBoxBase already forwards Padding to its ScrollViewer; this consumed the text viewport twice. Removed that duplicate margin, retaining 5-DIP corners, field sizes and input behavior. Database watch/history text columns now use an explicit vertically centered TextBlock style; checkbox centering remains intact. Header vertical alignment is explicit.

New Windows layout tests measure actual field viewport/line bounds with Russian/English names, numeric values and HEX at 100/125/150% layout scale, plus the compact table text's vertical position. The earlier style tests checked corners/arrows but did not detect text clipping. Manual verification on the user's Windows DPI remains pending; the commit's Actions run records tests and portable packaging.

## Section layout, branding and settings-window occlusion (2026-10-27)

Gold Goblin and Database now use the same header card/title/caption sizing as other workspaces; Database's header stays above its tabs. Market notification Appearance and Sound tabs follow the template help card. Equipment learning hints share the blue information-card style. Sidebar categories are Home, Overlays and Services, with scrolling at minimum height. The provided wordmark replaces header icon/title at 80% opacity; supplied crystal artwork updates the Windows ICO and About image, and the house artwork identifies Home.

While MainWindow or another visible RoAura settings/dialog window is foreground, ordinary game overlays stay visible/topmost but their shared rendering surface excludes the physical rectangles of those windows. Bounds are converted to the overlay monitor's DIPs. A separate-monitor settings window produces no exclusion; moving, resizing, maximizing, opening/closing a dialog and returning to the game refresh the clip. This changes render geometry only, never saved coordinates, ownership, focus, capture or input hooks. F9/no-client hiding and existing unrelated-application behavior remain unchanged. Database search/detail windows retain their existing visibility lifecycle.

Focused geometry tests cover separate monitors, partial/full coverage, owned dialogs, negative monitor origins and 100/125/150% DPI conversion. Windows tests and portable startup/packaging are recorded in the commit's Actions run. Manual same/two-monitor overlap, mixed DPI, game focus, modifiers, F9 and all supplied artwork/layout in RU/EN remain required before release.

## Next step

Continue the SpringRO Skill ID / Status ID / Hiding and Effects/Reminders audit on `feature/character-statistics`. Version 3.0.0 is already published; the older release-candidate notes above are historical. Preserve existing settings and functionality. Do not merge to main, change versions, create a tag, installer or release. RTM overlay placement remains deferred.

## Experimental Character Sessions (2026-10-28)

RoAura 3.0.0 is published and PR #5 is merged. Feature work is isolated on
`feature/character-statistics`, based on released main commit `e2bdab6`.
Stage 1 adds a compile-time DEV runtime identity, separate settings/market/log
folders, separate instance IPC, disabled official updater, a portable-only
build script and Windows CI. No character identification or per-character
statistics routing is yet implemented or field-verified. See
`docs/CHARACTER_SESSIONS_PLAN.md` for the subsequent experimental gates.


## Stage 2 passive character detection (2026-10-28)

Experimental branch `feature/character-statistics` now contains a separate
outbound map-login packet probe (known unshuffled `0x0072`/`0x0436`), bounded
sequence-aware per-flow inspection and metadata-only DEV diagnostic logging.
The development capture filter observes both TCP directions while the normal
server-to-client parser remains isolated. The change DOES NOT switch
characters or write per-character statistics yet. Identification is candidate
only and is awaiting SpringRO/RTM real-client verification, including Character
Select, late launch and endpoint changes. The initial development isolation
Windows CI run passed; later stage-2 CI must be evaluated per commit.

## Character Session shadow state (2026-10-28)
User verified a standard 23-byte 0x0436 map login and three Character Select
sessions with IDs A=154386, B=155665, A=154386 (same server port, distinct client
ports). Stage 3 adds passive per-flow shadow state and synthetic transition
tests on the development branch only. No statistics routing, persistence, or
confirmed server authentication yet; RTM/late-start testing remains necessary.

## Stage 4 per-character storage foundation (2026-10-28)

Added versioned `CharacterStatisticsDocument`, file-based
`CharacterStatisticsStore` and explicit-time `CharacterStatisticsLedger`
to the isolated feature branch. Storage is per logical server + CID and has
backup/validation controls. A→B→A, session reset, Zeny baselining, elapsed time,
loot separation and recovery scenarios have unit tests. None of these
components is connected to the live overlay/event route yet; existing
user-visible statistics are unchanged. Stage 5 requires flow-affine event
routing and character confirmation before it can safely update UI data.

## Stage 5 EXP/Zeny/Gold Goblin DEV wiring (2026-10-28)
The experimental branch now routes reward/statistics packets with immutable
per-connection character tokens and rejects stale queued events. DEV UI rates,
Gold Goblin inventory/manual overrides, user session reset and periodic local
persistence use the per-character ledger. The normal `main` branch is unchanged.
Automated Windows CI and real game regression remain required before calling
the feature ready; active session is inferred from map login plus inbound
network activity, not an explicit server-auth acceptance packet.

## Stage 6 character UI and shared timer (2026-10-28)

The user field-tested stage 5 on SpringRO and reported that everything worked,
apart from the independent overlay/Gold Goblin session clocks and the session
timer obeying the rate-refresh interval. DEV now uses one clock starting at
first EXP (Base or Job), or manually through Gold Goblin Start Session; loot
alone does not start time. Session-time overlay refreshes independently of
rate-statistics cadence. A compact RU/EN identity and historical record browser
is added to the Statistics section. Selecting a saved character is read-only:
it must not silently attribute network rewards to an unverified identity.
Manual field verification and regression are still required; no release/merge.

## Stage 7 audit started — character statistics recovery (2026-10-28)

The user authorized the regression audit but specifically postponed any release
or merge while they collect bugs. Read-only source review found a recovery gap:
`CharacterStatisticsStore.Load` did not fall back to the .bak file when
semantically invalid statistics caused `InvalidDataException`. The DEV branch
now supports read-only backup fallback in this case, while refusing to
overwrite the corrupt primary. The saved-character browser also lists a
backup-only character when the primary file is missing. Two focused Windows
regression tests cover these data-loss scenarios. Further manual checks
(SpringRO/RTM, VPN, overlays, named presets, multiple clients) remain open.
Do not merge, tag, build an installer or create a public release.

## Stage 7 packet-effect regression: Hiding and diagnostic instrument (2026-10-28)

After user verified some self/target Thief skills but no Hiding or Effect/Reminder transitions, DEV parser gained standard actor option packet decoding (0x0119 and 0x0229) and a Hiding-only bridge: OPTION_HIDE flag → Skill 51 upon activation and EFST Status 4 during on/off changes. New synthetic tests cover both layouts. Bounded diagnostic logs report decoded status/option event metadata and whether AID matches the current character (without logging the AID itself). Field test remains mandatory; unknown client-specific packet versions must not be guessed. No main merge/release.

## Stage 7 SpringRO self-AID attribution from uploaded debug.log

The uploaded 2026-10-28 18:34–18:35 log has 12 Npcap adapters filtering
127.0.0.1:57143, repeating outbound first opcode 0x0317, zero
map-login candidates/ActiveObserved sessions, and one decoded 0x0983
status 11287 for 180000 ms with matchesPlayer=false. In the prior
implementation that last boolean was calculated ONLY from an active
map-login AID, so it did not prove a different actor. DEV effect/
reminder learning was hard-gated on ActiveObserved, explaining its
failure on this capture. A separate self-AID observer now extracts
ZC_AID 0x0283 or own EXP 0x0ACC on the server flow, permitting secure
effects/skills attribution without CID; the EXP/Zeny/loot ledger
still requires a proper CID. Windows CI and real SpringRO verification
remain required. RTM overlay placement issue explicitly deferred.

## SpringRO effect-tracking follow-up (2026-10-28)
Second debug capture confirms SpringRO map login CID 154386, but 0x0983 status candidates 17519 and 17578 did not match the logged-in actor, and no valid Hiding option packet was observed. Skill 52 and EXP with player AID 2004474 did work. DEV fixes self-AID precedence to use same-TCP-flow ZC_AID/EXP evidence when present, adds a consistency flag in diagnostics, and reports bounded rejected Hiding option candidates. Real game retesting is still needed; not released or merged.

## 2026-10-28 SpringRO third debug and AID distinction

User's 19:17–19:18 DEV log: capture on local port 57143 across 12 adapters, outgoing first opcodes 0x0316/0x0317, no CID/ActiveObserved and no ZC_AID/EXP identity. Skill-use 0x07FB Skill 52 works. One candidate status 0x0983 index 15832, 240s, identityKnown=false. Several 0x0229 candidates are rejected. There is also a separate market HTTP connection timeout unrelated to effect packet detection. The user asks why AID is needed: it is for filtering other actors' events, NOT for detecting SKID/EFST identifiers or decoding durations. Source evidence identifies 0x0229 fields bodyState and healthState as full 16-bit bitmasks, which were incorrectly rejected above 4096. DEV now accepts all 16-bit masks, retains PK flag and nonzero actor checks, adds regression tests and reason-specific diagnostic logs. Status routing explicitly logs whether a configured effect was blocked for unknown own AID or an AID mismatch; it still refuses to ascribe foreign/unverified status to self. No fabricated fixed status/Hiding fix claim until real client verification. Branch only, no merge/release.

## SpringRO packet pipeline and shared status timeline (2026-10-28)

This iteration supersedes the earlier Boolean PK-field assumption and the
per-effect timeout implementation. It is an implementation under test, not a
claim of completed SpringRO live-client acceptance.

- Confirmed against upstream rAthena: non-damaging skill packets carry a
  heal/amount field, not a level limited to 1–100. Hiding sends -1. Both
  0x011A/0x09CB now accept that field and require a successful result. Option
  packets carry a karma byte which is not restricted to 0/1. Added standard
  status-on-enter 0x08FF/0x0984 and ground/modern cast layouts; diagnostics
  retain the real skill opcode.
- The stream scanner retains only incomplete data, consumes known frames
  once and skips supported opaque chat/list bodies. This prevents nested
  signatures and permits coalesced ZC_AID after another known packet.
  Unknown packet families still require validated scanning: no complete
  SpringRO packet-version table or custom decoder is being asserted.
- Effect/skill observations retain their TCP source lifetime, receive time
  and same-flow identity across dispatcher queues. Reconnect/retarget/reset
  invalidates old observations. Hiding transitions are per actor, and
  self-AID evidence is kept per flow instead of being overwritten by another
  connection. Unknown status observations can wait at most two minutes in a
  bounded memory-only buffer and replay only for a subsequently verified
  self actor, retaining the original duration deadline.
- Effects and Reminders share one status timeline. Standalone reminders,
  disabled effect rows, wizard creation after learning, refreshes, duplicate
  packets, explicit removal and timer expiry now use the same state. No
  settings/preset schema or saved positions change; hiding overlays still
  leaves tracking running.
- Learning distinguishes observed ID from confirmed self ownership and
  displays the reason when automatic assignment is withheld. Client-only
  0x043D can be learned without AID/CID. Existing configured cooldown fallback
  remains available when no self identity is known; it never establishes a
  verified self actor or a character-statistics identity.

Validation: the .NET 8 Windows-targeted application and tests cross-compile;
68 focused existing/new scenarios run successfully in a local headless
harness using the actual parser, capture routing and model sources (only OS
logging/localization/settings-shell dependencies substituted). The branch's
Windows workflow runs the full unmodified test project and builds one isolated
DEV portable; its exact commit/run result is the authority for that build.
No installer, version change or release preparation. Live SpringRO/RTM,
Npcap adapters, relog, F9 and modifier-key acceptance remain pending.
See `docs/PACKET_TRACKING_AUDIT.md` for the focused acceptance matrix.

## SpringRO field rejection of DEV 92 (2026-10-28)

User's new debug(3).log confirms that ID learning still fails and Hiding
state is unreliable. Do not describe the preceding iteration as working on
SpringRO. In the 20:30–20:34 session, map-login and later EXP agree on self,
but 0x0983 yields changing status candidates with actor mismatch and plausible
180/240-second timers. Own 0x0229 alternates its decoded Hiding bit. The log
does not contain the option mask or original status fields, so neither a
custom layout nor encoding nor correct live state can be established from it.

A reproducible independent overlay defect is fixed: a suppressed repeat or
disabled opposite notification no longer leaves an old opposite-state message
visible. Manual notification tests and placement previews retain their normal
lifetime. This does not correct a wrong wire-level Hiding bit.

DEV now records bounded scalar layout diagnostics for 120 seconds / 256
candidates after capture start and each Learn operation. This includes rejected
status candidates, full option masks, body/health/karma, standard field values,
stream offsets and boundary evidence. Actors and source lifetimes have only
window-local labels, not raw IDs/IPs/payloads. No new decoder or own-actor
fallback is guessed. Startup identifies the binary by assembly MVID. Next
step: field-test this diagnostic portable and inspect the new PACKET LAYOUT
lines alongside a short timed Hiding on/off and buff learning sequence.

Local targeted harness: 75 scenarios passed using actual source and xUnit
assertions. Standard MSBuild cannot initialize Process metadata in this host;
Windows CI is the full application/test/package validation authority. No main,
version, installer or release changes.

## SpringRO field evidence from rotated DEV log (2026-10-28 21:07)

The user's `debug.1.log` (not the current `debug.log`) contains working `PACKET LAYOUT` probes. Previous script output saying `Diagnostic build: NO` was a false negative caused by ignoring log rotation. Two Hiding-related sequences contain contiguous `0x0229 → 0x0983 → 0x09CB Skill 51` and `0x0196 → 0x0229 → 0x09CB Skill 51` frames, all on one flow; own Skill 51 succeeds and the `0x0229` actor matches self. At the second apparent activation, `0x0229` says `hideBit=False`, so the option bit is not a reliable live SpringRO state indicator. Observed `0x0983` status candidates have **neither** the existing actor interpretation nor Claude's proposed actor-first alternate equal to the verified self actor. A simple field swap is **not supported** by this real sample. The sequences suggest a relationship with Hiding but need the user's ON/OFF timestamps for confirmation. No parser, release, or identity behavior was changed; details and packet offsets are recorded in `docs/PACKET_TRACKING_AUDIT.md`.

## SpringRO Enchant Poison and Hiding status audit (2026-10-28 22:48)

User supplied a second sanitized log confirming Skill 138 = Enchant Poison (180 s); learning its Status ID failed. Verified-self `0x09CB` packets correlate directly with `0x0983` for eight applications. The same live capture contains seven repeated Hiding-related `0x0983` starts and `0x0196` ends adjacent to verified-self Skill 51 acknowledgments; option `hideBit` contradicts the sequence. Both +2/+4 interpretations of the status actor remain unmatched. Thus **skill learning works in these samples; Status ID decoding/attribution does not**. The issue is shared between Hiding and Enchant Poison; do not narrow the fix to Hiding alone.

New DEV-only SpringRO correlation diagnostics compare previously opaque status scalar candidate offsets across repetitions and test all possible 32-bit in-frame offsets against the verified self actor **without logging those raw values**. This does not modify status handling, tracking or user settings. New focused tests cover pair detection and rejection of unverified/misaligned data, but Windows .NET build/tests and manual SpringRO verification remain pending. See `docs/PACKET_TRACKING_AUDIT.md` for exact evidence and boundaries.

## SpringRO v3 packet investigation (2026-10-28 23:18)

Live v3 sanitized log confirmed 20 high-specificity skill-correlated sequences: 8 starts / 8 ends for Hiding (Skill 51) and 4 starts for Enchant Poison (Skill 138). Unlike the many unaligned initial candidates, these are in a verified-self flow with contiguous byte offsets. Status +2, actor +4 and alternate +2 do not yield own AID or stable Status ID. Candidate U16 at status-frame offsets +6 and +7 remain stable across repeats, suggesting a possible partially masked actor field or simply an unsupported layout. A new diagnostic-only (DEV + SpringRO gated) experiment checks whether a shared per-frame XOR or additive mask between the candidate Status ID and low AID word can explain the observed variation, and whether the high AID word matches verified self. Changes are in `SpringRoStatusSkillCorrelationDiagnostics` with focused synthetic tests; these are **not proof** until live sampling. Neither status decoding nor effect/reminder routing has been changed. Windows build/tests and gameplay validation still pending.

## Hiding state correction in isolated SpringRO DEV (2026-10-29)

Another real v3 trace confirmed 4 Hiding start pairs, 4 Hiding end pairs, 11 Enchant Poison pairs; full AID absent in status frames but actor high16 consistently matches verified self. The shared-XOR/shared-addition hypotheses are disproven for repeated field samples. New `SpringRoCorrelatedHidingTracker` consumes only verified-self contiguous `0x0983 -> Skill 51` (Hiding ON) and `0x0196 -> own 0x0229 -> Skill 51` (Hiding OFF) with strict structural checks, supplying synthetic EFST_HIDING=4 to the normal Effect/Reminder timeline. When gated DEV + SpringRO, misleading raw option masks are diagnostic-only and no longer overwrite Hiding state. This is an **experimental focused fix**, not a generalized EFST decoder. RTM/Other and main unchanged. Focused tests added; GitHub Windows CI build and real manual Hiding ON/OFF test required. Enchant Poison Status ID learning remains blocked on actual protocol decoding; do not claim fixed.

## SpringRO Hiding live packet-to-effect acceptance (2026-10-29 01:35)

User's v4 sanitized trace from the tested DEV `9724305e` reports 7 correlated own Hiding ON and 7 OFF, in alternating order; **all 14** resulted in a matching synthetic status=4 event and `PACKET EFFECT TRANSITION` from the existing effect timeline. Contradictory option-mask hide bits no longer corrupt observed Hiding transitions. This is stronger than synthetic-test success, but **screen-side UI, notification position and relog remain unverified** until the user's direct report. No further Hiding code modifications needed on the current evidence.

Seven Enchant Poison casts also correlated with own Skill 138 + 180000ms status-like packets. Their true Status ID and low actor bits remain undecoded; current learning rejects them as foreign. Repeated XOR and addition derived statuses are unstable, so neither decoding hypothesis is justified. Do not pretend that tracking a successful skill cast is the same as learning a server Status ID. A separate, clearly labeled Skill ID triggered effect/reminder mode could be evaluated as a useful fallback, but would be a feature with timer/early-dispel caveats and needs a scoped product decision. `main` and stable 3.0 remain unchanged.

## SpringRO Hiding: user-confirmed visual acceptance (2026-10-29)

After the v4 DEV test (commit `9724305e`), the user explicitly confirmed that **the Hiding overlay displayed correctly throughout the test**. This closes the previously open screen-side acceptance question for the seven ON/OFF cycles logged between 01:35:38 and 01:36:10. Together, the packet capture and user's visual confirmation support marking **the scoped SpringRO Hiding ON/OFF fix as verified for the tested workflow**. Do not imply that disconnect/relog, other server profiles, all reminders, or the stable main release were independently tested. Keep the validated correlation path unchanged unless a regression is observed.

**Active priority:** address general SpringRO Status ID identification, starting with Enchant Poison (Skill ID 138; repeated 0x0983 180000ms + own successful skill acknowledgment). Observed 16-bit Status ID candidates vary and the full AID does not match. XOR/add-derived hypotheses are unstable. Do not assign a fake EFST ID or silently equate Skill ID with Status ID. If an alternative skill-triggered tracking mode is implemented, identify it explicitly as a distinct mode with documented limitations for early cancellation and non-skill-origin statuses.

No release/merge requested. `main` and tested stable RoAura 3.0 remain untouched.


## Status ID investigation: reproducible evidence instead of mask guesses (2026-10-29)

Reviewed all documentation changes through `fb42b04`, current correlation code,
and the actual 184-line Library report `RoAura-SpringRO-diagnostic-v4.txt`.
Hiding visual acceptance remains valid; its implementation is unchanged.
General EFST decoding is **still blocked**. Existing sanitized reports retain
candidate status numbers but replace actor words with aliases and transformation
results with `unstable`; they cannot reconstruct even one original status frame.
Neither a field swap nor the rejected XOR/addition masks should be retried as
an alleged fix. A successful own Skill 138 does not establish EFST identity.

DEV adds an explicitly opted-in lossless sample path:
`RoAura.exe --springro-status-samples`, then Learn in the SpringRO profile.
For 120 seconds, at most 16 contiguous status/[option]/successful self-targeted
Skill 51 or 138 chains are written as `SPRINGRO WIRE SAMPLE` JSON log lines.
Only these selected frames plus verified self AID and offsets are included;
no full TCP dump, login/chat content, endpoint addresses or names. Default
launches retain metadata-only diagnostics; stable/RTM modes cannot enable this.
This intentionally differs from the old metadata-only policy: exact original
fields are needed for an offline reproducible test, and export requires the
explicit command-line switch. Samples remain local until the user supplies them
and must never be committed to Git. Reconnect/reset cancels pending capture.

`tools/inspect_springro_status_samples.py` validates and inspects these samples
without modifying application state or inventing a Status ID. It accepts rotated
logs too, and reports the standard-layout interpretation and a content digest
without displaying raw actors. Synthetic tests verify lossless round trip,
foreign/failed/unrelated events, identity changes, missing/expired boundaries,
closed source, count/time bounds and default-off behavior. 92 focused C# harness
scenarios and 3 Python inspector tests pass locally. Windows CI remains the
full application/portable check. No new game-facing decoder or skill-triggered
fallback, no main merge, version change or release.

## SpringRO fourth run: missing starts and reported Skill ID learning regression

In the latest log a `0x0983` start at 15:45:19 was missed with
`recentOpcode=0`; later status 4 and 6 starts/removals matched. The user
reports that Cloaking still does not appear and cooldown Skill ID learning
stopped working. The log does not show whether a skill event entered the
active UI learning window, so no cause is established yet.

DEV now logs learning-window activation, skill/post-delay arrival and existing
identity/lifetime decisions. The client sampler reports aggregate status
opcode and stable-frame counts every 15 seconds. No raw actors or memory
contents are added. The next Windows build and SpringRO test must identify
the missing step before changing the generic decoder or attribution rules.

## ROQuestie interaction refinements (2026-10-30, feature only)

Owner feedback: one ROQuestie tracker header owns the only drag handle. Quest rows
keep Prev/Next/More/navi and replace their drag handle with Done. Done is explicit
manual confirmation: advances one instruction or completes the last and moves to
history. Navigation alone still never completes. Missing/review/readonly/inactive
rows and stale step actions cannot confirm. Progress/history stays per server/CID.
Catalog stars toggle active tracking without deleting saved progress; reactivation
retains the instruction and restores tracker visibility. Active is dark blue,
inactive light gray/translucent. Exact unambiguous catalog titles in facts become
internal quest links; unknown/ambiguous names remain original text.
Catalog status/reload belongs in RoAura quest settings only. Game browser has
larger edge spacing; navi chooser has rounded border and internal padding.
Tests cover confirmation, history persistence/isolation, stars and literal links;
native input smoke now selects header/quest surfaces by action and checks Done
and the rounded multi-target chooser. CI fake client fits the desktop for captures.
Automated verification: 294 tests and eight ToC checks pass; real-game acceptance remains open.

Interaction verification: [Actions #41066780089](https://github.com/alerion08/RoAura/actions/runs/41066780089)
SUCCESS on `7fcce0062a4e866111190b2a11f742daa3245798`: 294 tests passed,
zero skipped/failed, eight ToC checks, portable startup, RU/EN and clean package.
Native input additionally passed Done/next instruction without focus loss,
multi-target chooser open/cancel, and the single header drag with production Escape.
The fake client now fits the desktop; tracker, navigation chooser, catalog star
and facts/link rasters were inspected. The native browser edge is no longer clipped
by the oversized test fixture. These are still not real SpringRO/mixed-DPI checks.
Artifact: [RoAura-development-71-7fcce00](https://github.com/alerion08/RoAura/actions/runs/41066780089/artifacts/12983925091),
one portable and 16 PNGs. Portable SHA256:
`a3d58e98484db5ede04dd9adac11ea4ebf4c286d904bfb4183090a49ef1b3aea`.
No main/research changes, version bump, merge, tag or release.

## ROQuestie feedback and current-step navigation (2026-10-30)

Feature-only owner feedback: the details Active button now toggles tracking via
the same progress-preserving action as a catalog star. Navi success is per entry
and current instruction: four seconds of green/white checkmark, including hover;
no repeated copied-command text in tracker cards. A map-pin glyph replaces Wiki
while the tooltip retains source uncertainty. DescriptionLines defaults to 3,
clamps to 1–20 and participates in settings/presets and the live preview.
Explicit quoted input or short literal capitalized names after "type in" receive
Copy text/Copy controls in walkthrough/tracker. Variable passwords, menu choices
and research candidate answers are not inferred or promoted to verified progress.
More always selects Walkthrough, updates layout and scrolls to the active row,
even after the same guide was left on Facts. Native smoke checks this path and
successful navi feedback. Automated validation passed on c6065b2 (302 tests, eight ToC checks, Windows portable startup);
real SpringRO/DPI/input acceptance remains manual.

Feedback verification: [Actions #41070762636](https://github.com/alerion08/RoAura/actions/runs/41070762636)
SUCCESS on `c6065b29ce5762e88c74f41dc2dede948a5799e4`: 302 tests,
zero skipped/failed, eight ToC checks, resources/package checks and portable startup.
Native input checked successful navi copy/checkmark and More returning from Facts
to a visible current instruction. Native rasters inspected: no repeated toast,
per-row green button with white checkmark, location glyph, active details toggle,
and walkthrough scrolled to the current highlighted step. The shared theme initially
overrode the checkmark color; its success template now sets the glyph foreground
explicitly, confirmed in the final native capture.
Answer-copy behavior is literal-source only; no guarantee of server-specific
dialogue compatibility or variable password inference. Real SpringRO acceptance
remains open. Linux build tooling intermittently failed in process metadata;
the final Windows CI compilation and checks are the validation authority.
Artifact: [RoAura-development-73-c6065b2](https://github.com/alerion08/RoAura/actions/runs/41070762636/artifacts/12985270376),
portable plus 17 UI PNGs. Portable SHA256:
`2274a41fbf6a21c20404cfa78dc603086c505da47bcc284797bf7449fc62ff9a`.
No release/version/main/research mutation.

## ROQuestie action occlusion and detail navigation (2026-10-30)

Owner-reported disappearing tracker buttons were caused by whole-strip hiding
when the browser/settings intersected only its transparent edge. Action content
now uses a render/input alpha clip for covered pixels only; uncovered buttons
remain visible. Existing game bounds, visibility toggles and passive focus remain.
Walkthrough literal usable map points are coordinate-labeled copy buttons using
the same filtered /navi command and success feedback; Other remains reference-only.
Coordinate warning prose is removed from point labels/tooltips/chooser. Original
catalog/source assurance is retained internally; no new server verification claim.
Regression smoke checks clipped painted button centers rather than demanding
nonintersecting transparent HWND rectangles. Windows validation passed: 303 tests, eight ToC checks and portable startup; real-game acceptance remains open.

Occlusion/navigation verification: [Actions #41073669338](https://github.com/alerion08/RoAura/actions/runs/41073669338)
SUCCESS on `aa0fd9b3581847ad05bcb59dd82587241afe3ece`: 303 tests,
zero failed/skipped, eight ToC checks, portable startup and clean package checks.
The partial-overlap regression checks uncovered/covered action points; native smoke
checks settings exclusion against painted button centers while preserving prior
focus/click-through/keyboard checks. Native walkthrough raster inspected: usable
map points are buttons and coordinate-warning lines are absent. The user-reported
SpringRO partial-window placement and actual coordinate paste still need manual
verification. General catalog availability/source metadata is unchanged.
Artifact: [RoAura-development-74-aa0fd9b](https://github.com/alerion08/RoAura/actions/runs/41073669338/artifacts/12986003102).
Portable SHA256: `b94bb96d5ec76efbfc6aff3e8f28eb12b8f0ea28a349feda7d4b79548aa1fb99`.
No main/research changes, product version bump, merge, tag or release.

## ROQuestie emphasis, compact actions and database priority (2026-10-30)

Owner priority: catalog is a general quest reference. Availability-on-server
labels are removed from catalog and details without rewriting source assurance.
Explicit answer literals use bold purple; exact bundled item names use bold green,
with source text preserved. Unknown names are not inferred. The tracker uses
lighter colors over the game; walkthrough uses darker colors on its light cards.
Step navigation buttons share a compact wrapping row (22 DIP, minimal padding).
Step selection/copy buttons share their own wrapping row. No category headings.
MaxTrackedQuests is 1–50 (default 20, preserving practical screen-fit behavior),
saved with visuals/presets; extra quests stay active and accessible in the browser.
Database search/details permanently occlude lower tracker and ordinary/statistics
pixels while visible, even with game focus. No capture/parser changes or window
ownership; shared clipping keeps other visible areas and input behavior intact.
Native smoke checks quest limit and database priority for tracker/statistics;
focused tests cover literal emphasis/source preservation and limit persistence.
Automated validation passed: 305 tests, eight ToC checks and Windows portable startup; actual SpringRO/mixed-DPI acceptance remains open.

Emphasis/priority verification: [Actions #41089927571](https://github.com/alerion08/RoAura/actions/runs/41089927571)
SUCCESS on `9cbdc3a754a354f4627b1349763dc04edc038fe6`: 305 tests,
zero skipped/failed, eight ToC checks, RU/EN/package validation and portable startup.
Native smoke applies the one-quest limit, opens the actual Database overlay, returns
focus to the fake game and verifies that both quest and ordinary/statistics surfaces
exclude the database center. Prior input/focus/drag tests continue to pass.
Native browser/database and RU catalog rasters inspected: compact point controls,
separate action row, no availability label and unobstructed database panel. Literal
highlight matching/source preservation and maximum-limit persistence are covered by
focused tests. Real SpringRO, mixed-DPI and broad item-name coverage remain manual;
unrecognized item names remain plain rather than guessed.
Artifact: [RoAura-development-75-9cbdc3a](https://github.com/alerion08/RoAura/actions/runs/41089927571/artifacts/12992839460).
Portable SHA256: `2fdd096f155e155e5f12c306adeaef45e6b3ea7593e61f25b7514edbece8a238`.
No main/research write, version bump, merge, tag or public release.
