# Project state

Last updated: 2026-10-27

## Current product

RoAura is a Windows x64 WPF overlay for Ragnarok Online. It reads incoming game packets through Npcap and provides configurable overlays and supporting tools.

Current release candidate: **3.0.0**

Stable branch: `main`  
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

Complete the 3.0.0 release pipeline through PR #5. After successful publication, continue confirmed bug fixes as 3.0.x patches using the regression checklist. Preserve the accepted 3.0 functionality and user settings.

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
