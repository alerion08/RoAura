# Regression checklist

## Release 3.3.0 completion — 2026-11-03 documentation update

Published v3.3.0 on 2026-11-02 from main commit `7d70b173b7c10cc4ab7ae3f268ff89cb9474e19e` (PR #10).
Final release run https://github.com/alerion08/RoAura/actions/runs/41441524384 completed successfully.
Windows tests, eight ToC checks, native input/startup checks, footer centering in both themes, portable/installer packaging, silent installation and installed database hash verification passed.
Release assets: portable ZIP, Setup.exe and SHA256SUMS.txt.
Earlier “pending CI”, “preparation” and “no release” entries are historical and superseded by this completion record.
Real SpringRO, mixed-DPI, fullscreen, multi-client and long-session acceptance remain manual; they are not marked passed by CI.


Use the smallest relevant section for normal changes. Complete all **Release blockers** before merging a release candidate. Record failures with the build/commit, Windows version, display mode, DPI, RO executable and whether ExitLag/VPN was active.

## RoAura 3.0 acceptance record

On 2026-10-27 the user confirmed that tested build `28fa0d5` works well and accepted the next release as 3.0. Version-only candidate 3.0.0 is validated through the release PR for tests, startup, portable ZIP, installer, private-data exclusion and SHA-256 files. Detailed environment-matrix rows below remain a reusable checklist; do not infer individually recorded OS/DPI combinations from the general user acceptance.

## Automated baseline

- [ ] `dotnet test .\RoAura.Tests\RoAura.Tests.csproj -c Release`
- [ ] `.\build-release.ps1`
- [ ] Portable ZIP exists and launches.
- [ ] Portable contains no `settings.json`, `active-profile.txt`, Profiles, Recovery, Market caches or `debug.log`.
- [ ] RU and EN resource keys match.
- [ ] Version shown in About matches `RoAura.csproj`.
- [ ] No unrelated files or private data in the diff.

## Release blockers

### First launch and persistence

- [ ] Clean portable launch starts without personal/default developer entries.
- [ ] Missing Npcap produces a clear message and official download link.
- [ ] Existing settings load with positions, colors, sizes, hotkeys and enabled states intact.
- [ ] Existing named presets load and apply correctly.
- [ ] Applying a preset and closing the app does not overwrite it with the previous state.
- [ ] Corrupted settings are preserved in Recovery/quarantine and do not crash startup.
- [ ] Closing with the X button terminates the process.
- [ ] A second launch activates the existing RoAura window.

### Ragnarok window and overlays

- [ ] No game running: applicable overlays are hidden.
- [ ] Start game after RoAura: overlays appear on the correct client.
- [ ] Start RoAura after game: overlays appear on the correct client.
- [ ] Switch RoAura → Ragnarok → another app → Ragnarok.
- [ ] Ordinary overlays remain above the game after clicking inside Ragnarok.
- [ ] Database panel opens over the game on the first attempt without appearing over another window.
- [ ] Database panel remains visible after the first in-game click.
- [ ] Fullscreen, borderless and windowed modes behave correctly where supported.
- [ ] Resize/minimize/restore the game; saved layout is not permanently damaged.
- [ ] Test temporary tiny client bounds, a smaller second client and DPI changes; all feature X/Y values stay unchanged during resize and after save/restart.
- [ ] Numeric X/Y editors load existing coordinates and switch selected rows without writing (0,0).
- [ ] Move the game between monitors with different DPI/scaling.
- [ ] Launcher/helper window is not mistaken for the client.
- [ ] If multiple clients are open, activating another eligible client retargets the overlays.
- [ ] F9 hides and restores all overlays.
- [ ] Individual and group drag work and positions persist.
- [ ] Alt, Ctrl and Shift work normally with overlays and Database panels open.
- [ ] Overlays do not steal focus.

### Npcap and connection lifecycle

- [ ] Initial capture starts and packet-driven features respond.
- [ ] Relog to character selection and back without restarting RoAura.
- [ ] Disconnect/reconnect the network.
- [ ] Game endpoint/IP/port change is detected.
- [ ] Test the user's normal route.
- [ ] Test with ExitLag if used.
- [ ] Test with VPN/WireGuard if used.
- [ ] No duplicate notifications after reconnect.
- [ ] No permanent capture stall after FIN/RST.
- [ ] CPU and memory remain reasonable during a long session.

### Packet-driven features

- [ ] Cooldown starts, counts down and returns to READY.
- [ ] Effects show appearance and disappearance notifications.
- [ ] Separate appear/disappear sounds work.
- [ ] Buff Panel active/inactive state and duration update.
- [ ] Reminder reacts to missing/restored effects.
- [ ] Equipment learns and displays exact equipped variants.
- [ ] Equipment survives relog and preset/settings reload.
- [ ] MVP Timers detect/restart/reset correctly.
- [ ] Loot notifications aggregate correctly.
- [ ] Zeny notifications and Zeny/hour update.
- [ ] Base/Job EXP and XP/hour/time-to-level update.
- [ ] Gold Goblin receives loot and values it correctly.

## Database and Market

- [ ] RTM item and monster searches work.
- [ ] SpringRO item and monster searches work.
- [ ] Empty search and no-result state do not crash.
- [ ] Item/monster detail opens and closes safely.
- [ ] Market returns cached results immediately.
- [ ] Background Market refresh updates results without freezing the UI.
- [ ] Network timeout/error keeps previous usable cache.
- [ ] Duplicate IDs do not break catalog loading.
- [ ] Gold Goblin NPC, Overcharge 0–10, Vending and manual rules calculate correctly.
- [ ] Custom MVP names are not replaced by catalog names.
- [ ] Clipboard actions work or report a nonfatal error.

### Market tracking

- [ ] Under the three-column watch table, select an item and inspect detection time, shop, map/coordinates and price in its history; check centered Enabled controls and no unused fourth column.
- [ ] Detect several qualifying shops, restart, scan again: known offers remain silent and history stays visible. Removing/readding or pausing/resuming a watch does not forget its history. New prices/sellers still notify.
- [ ] Retention in General accepts whole days and persists in presets. Expired entries are removed on startup, retention changes and periodic cleanup; they can notify again. History stays separate from presets and portable packages.
- [ ] A history file read/write failure is nonfatal and shows a warning; unreadable data is not overwritten. Network errors do not erase history.
- [ ] Volume shows integer percentages and slider steps; Choose sound and Listen have equal height.

- [ ] Star dialog has no OS title bar and centers between both database panels from either star; move the game and test mixed/negative monitor coordinates while it is open.
- [ ] Third-column checkbox pauses/resumes without losing item/price; disabled watches stay silent, editing preserves their state, restart/presets retain it, and the star reflects/resumes the rule.
- [ ] Permanent preview updates live; Show icon removes the icon and its gap both there and in game. X/Y share a compact row; numeric drafts still commit only on Enter/blur.
- [ ] Sound audition uses chosen audio and volume (including silent 0%); alert uses the same volume. Media handles release after playback.
- [ ] Move in game shows a persistent draggable sample. Finish hotkey, F8 and F9 end editing and restore click-through; position persists after restart, resize and DPI/client changes.

- [ ] In RU and EN, inactive stars on search rows and item details are semi-transparent; confirmed watches turn both dark blue; Cancel changes nothing; a second click removes the watch.
- [ ] Watchlist uses a left table and a right editor card with bottom actions; refresh field is readable, item fields are compact, star dialog is 20% narrower. Add/edit/remove via Watchlist. Search by name/ID in the popup, type/paste/replace several characters without auto-selection or caret jumps; confirm by click or Down/Enter, dismiss by Escape/outside click/tab change. Jur [2]/[3] and other slot variants show their full names. Reject absent/ambiguous items, empty/negative/overflowing prices; Any price disables price input.
- [ ] Max price includes equality; above-threshold/BUY/other IDs do not notify. Any price accepts any valid sale price.
- [ ] First successful fresh snapshot can notify once. Unchanged offers and quantity changes do not repeat; a new seller/price notifies, while disappearance/return stays silent until the stored entry expires.
- [ ] Network errors/partial/invalid pages preserve cache and do not reset match history. A genuine empty snapshot removes stale offers.
- [ ] Set refresh to 1 minute; the service no longer skips for ten minutes. Warning is visible; crawls do not overlap and 429/5xx back-off still applies.
- [ ] Configure template tags, custom/default icon, icon/text size, colors/alpha, padding, spacing, X/Y, duration and sound; use Test notification. Numeric input keeps the full typing draft.
- [ ] Simultaneous matches stack. F9, game focus, resize and mixed DPI work without changing saved positions. Add dialog stays visible over Database without blocking modifiers.
- [ ] Save/restart and named presets retain rules/appearance. Switching away from SpringRO hides pending Market notifications.

## Numeric layout input

- [ ] In Cooldowns, Effects, Buff Panel, Reminder, Equipment, MVP Timers, reward groups and rate indicators: select the whole size value, clear it, type `64`/`24` digit by digit; no intermediate snap to a minimum or maximum.
- [ ] Enter or Tab/click another field commits the number and updates the preview/overlay; Escape restores the last valid value.
- [ ] A model/packet update while typing does not replace the draft or move its caret.
- [ ] Fully close the previous instance before testing; About shows the candidate commit revision.
- [ ] Padding/gap accept `0`; X/Y accept supported negative/fractional coordinates.
- [ ] Paste/replacement works; out-of-range values clamp only after leaving the field.
- [ ] Empty/non-numeric input leaves the last valid value intact, displays validation, and can be corrected.
- [ ] Edit a value, Save/apply a preset, switch selection/tab and restart: the committed value persists.
- [ ] Sliders still update their matching numeric fields.

## Shared controls and live presets

- [ ] Every dropdown keeps a plain blue arrow when hovered/open/selected; its list appears below the field with no empty popup after changing rows.
- [ ] Switch repeatedly among MVP timer rows while the dropdown/editor has focus; selected monster/name/duration/kill time remain associated with the correct timer. Search by name and ID; select by mouse/keyboard; custom names survive row selection.
- [ ] Text, numeric and color fields share the same rounded corners; all sliders match the timer sound slider. Sound button captions fit at 100/125/150% DPI in RU and EN.
- [ ] Click each color field: palette, HEX, RGB and alpha work; Cancel preserves the previous value; Apply persists through preset save/reload and restart.
- [ ] Create Knight/Wizard presets with different entries, enabled states, exact equipment variants, all panel positions, statistics, Gold Goblin rules and Database settings. Switch using the list: process stays running, every section/overlay changes, hotkeys follow the preset, no old alerts remain.
- [ ] Modify settings after switching, save and close/reopen: current working state persists. Named snapshots change only through Update selected; their local filenames match the displayed names. Corrupt/missing preset shows a warning without changing the working settings.

## Compact field text regression

- [ ] At 100/125/150% Windows DPI in RU/EN, inspect full glyph height in skill names, numeric coordinates/sizes, HEX, effect advanced fields and timer/statistics settings. Focus, select text, type and leave the field: no upper/lower clipping.
- [ ] Watchlist and offer-history names, prices, timestamps and coordinates are vertically centered in the 26-DIP rows, matching the centered Enabled checkbox.

## Section layout and active settings window

- [ ] Gold Goblin/Database header typography and cards match MVP/Statistics; Database header is above all tabs. RU/EN text and Gold Goblin actions fit at minimum window size.
- [ ] Market Notifications: template help has a blue info card and more top spacing; Appearance/Sound tabs preserve every editor, live preview and all bindings, including volume, sound choice/listen and placement. Equipment learning hints match Effects.
- [ ] Sidebar category order matches Home, Overlays and Services; every destination remains reachable at minimum height, and unsupported services leave no empty category. Wordmark is at 80% opacity; crystal icon works in Windows/About; Home shows the house.
- [ ] RoAura on a second monitor: focusing/typing/changing settings keeps ordinary game overlays visible. On the same monitor: only pixels beneath RoAura/its dialogs disappear, with no overlay drawing over the controls. Move/resize/maximize and close dialogs; masks follow without altering stored positions.
- [ ] Repeat with negative monitor origin and mixed DPI; activate game and verify all masks clear. F9, edit/finish, modifier keys, unrelated applications, minimized/missing client and Database search/detail windows keep working.

## UI and localization

- [ ] Changed screen checked in Russian.
- [ ] Changed screen checked in English.
- [ ] No missing localization keys or raw resource identifiers.
- [ ] 100% scaling checked.
- [ ] 125%/150% or mixed-DPI checked for window/overlay work.
- [ ] Minimum window size remains usable.
- [ ] Empty sections do not leave unexplained gaps.
- [ ] Long item/monster/user names do not overlap controls.
- [ ] Loading and error states remain readable.
- [ ] No selected row / deleted row actions are safe.
- [ ] Sounds release file handles after playback.

## Installer and updater

- [ ] Installer builds with the version from `RoAura.csproj`.
- [ ] Installer warns when Npcap is missing but can continue.
- [ ] Installer refuses unsupported Windows versions.
- [ ] Install preserves user data under `%APPDATA%\RoAura`.
- [ ] Automatic update displays progress.
- [ ] Cancel/close during download removes temporary data safely.
- [ ] Failed download gives a useful error and manual release-page option.
- [ ] Downloaded installer hash/digest is verified.
- [ ] Update does not publish or overwrite an existing release unintentionally.

## Release gate

Before merging to `main` or publishing:

- [ ] Every applicable Release blocker passed on the exact candidate commit.
- [ ] Known failures are documented and explicitly accepted by the user.
- [ ] `docs/PROJECT_STATE.md` reflects the final state.
- [ ] `RELEASE_NOTES.md` describes user-visible changes accurately.
- [ ] Version is correct in `RoAura.csproj` and `app.manifest`.
- [ ] Portable and installer were produced by successful Windows CI.
- [ ] SHA-256 checksums were generated.
- [ ] User explicitly approved merge/release.

## ToC / packet switch acceptance

- [ ] Select ToC: 61 MVPs, ToC item names/slots, descriptions/scripts, maps, server
  EXP and normal/MVP drops; no SpringRO item-detail or market calls/results.
- [ ] ToC NPC prices (Red Potion: 25 Zeny; OC10: 31), manual rules and loot values;
  SpringRO rules and ToC rules/timers remain separate across switches and presets.
- [ ] Unknown ToC respawns are shown as unknown; manual timer duration is retained.
- [ ] All navigation stays visible. Unsupported entries are gray/transparent and
  cannot be opened by clicking, Home quick actions or keyboard; disabled icon alpha
  is preserved. Switch servers while a now-unsupported page is selected.
- [ ] Home warning and toggle fit RU/EN at 100/125/150% DPI and minimum size.
- [ ] Turn packets off during capture/learning: Npcap and SpringRO client sampler
  stop, no queued reward/status is applied, packet overlays/notifications stop;
  Database, manual MVP timers and SpringRO HTTP Market remain usable.
- [ ] Turn packets on, relog, verify fresh CID/AID and correct server-specific
  EXP/loot/Gold Goblin session; no old server events or history are attributed.
- [ ] Restart and apply named presets with packets off: off remains off; settings,
  timers, rules, positions and saved character history remain intact.

## Settings / scopes / ToC cards

- [ ] Home and Settings share connection controls, packet switch and language/server.
- [ ] All five hotkeys edit/persist; Other is last; navigation preferences persist.
- [ ] Home/Settings cannot be hidden; packet-disabled features remain gray.
- [ ] CID aliases survive presets; same CID on different servers remains separate.
- [ ] All overlay types: empty scope, one/multiple characters, unknown CID, relog.
- [ ] Overlay coordinates and legacy settings survive schema 13 migration.
- [ ] Cooldown ID is visible before creation; retry/back/cancel leaves no overlay.
- [ ] Four empty overlay list/editor areas align in RU/EN and high DPI.
- [ ] Item/monster cards wrap text; skills begin collapsed; Baphomet 120–130 min.
- [ ] Existing user timer intervals stay unchanged when loading new respawn hints.

- [ ] Header Settings button replaces hotkey legend; no Settings sidebar entry. Return to each sidebar section works.
- [ ] Overlay help cards share Equipment's border/icon/padding/text color in RU and EN.
- [ ] About icon remains colored across server and packet-switch changes.

## ROQuestie stage 2

- [ ] Quests follows Database; compact sidebar retains every destination; Settings
      still opens from the header and About remains colored.
- [ ] RTM/SpringRO/ToC/Other, packets off: Quests is readable and enabled.
- [ ] Catalog missing/corrupt/unsupported: only Quests reports an error; retry works.
- [ ] Original-title search, provisional categories, unknown/known level filters;
      Lost Child shows 13 original rows and Friendship 14, including source details.
- [ ] Unknown CID needs explicit profile choice; manual mode is clearly labeled.
      Switch CID/server/relog; immutable detected server and aliases do not mix state.
- [ ] Prev/Next/More, reorder, visibility, pause/remove, confirmed manual completion,
      history and restore retain each quest's independent position through restart.
- [ ] Save failure/backup-only recovery: no silent overwrite, no unsaved scope/exit
      loss; explicit retry remains possible.
- [ ] Schema 13 -> 14 and presets preserve all old overlay coordinates, exact
      equipment data and external quest progress; only quest visuals follow presets.
- [ ] RU/EN empty/loading/error/populated states at minimum window size; actual
      Windows 100/125/150% and long guide names. CI PNGs are supporting evidence only.
- [ ] Settings preview remains inside RoAura; game overlay/drag/F9/navi acceptance
      stays pending for stages 3/4, without changing existing overlay behavior.

## ROQuestie two game windows (owner priority override)

- [ ] Main Quests section has settings only; no main-app quest search dependency.
- [ ] F6 browser and F5 tracker toggle independently; remapping/presets and new-key
      conflicts preserve legacy F7/F8/F9/F10/finish assignments.
- [ ] Browser catalog/active/history/full-width walkthrough work over the exact
      game client; More highlights that quest's current instruction.
- [ ] Combined requirements/rewards has clear source-labeled blocks/lists/values;
      unknown text, numeric separators, branches and original source remain intact.
- [ ] Tracker Prev/Next affect only that quest; no automatic completion. Navi copy
      identifies wiki uncertainty and requires a choice for multiple targets.
- [ ] Real OS clicks on tracker buttons do not activate it or reach the game;
      text, blank margins and transparent corners pass through to the game.
- [ ] Ctrl/Shift/Alt/Windows, movement, typing, F9 and finish-drag remain usable.
      Browser typing stays in browser; close returns to the exact game HWND.
- [ ] Settings, Database and native/modal dialogs are not covered by tracker
      buttons; ordinary overlays are not hidden by the passive full-client HWND.
- [ ] Resize/minimize/client switch, mixed/negative monitors and 100/125/150% DPI
      retain every saved coordinate. Dragging/resizing saves only explicit changes.
- [ ] Cross-client automatic CID stays suspended until a fresh observed session;
      manual profile stays explicit; logout/server switch never inherits old state.
- [ ] Run fake-game CI input checks and inspect native captures. Separately test
      actual SpringRO protected/fullscreen, 2K, multi-client/relog and paste navi.

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

- [ ] Real SpringRO: header-only drag; Done/last-step history; star toggle/restore; internal quest links; rounded navi chooser; settings-only catalog reload.

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

## ROQuestie copied feedback, collapsed tracker and step marks (2026-10-31)

Owner feedback supersedes earlier checkmarks/20-row tracker behavior. Navi has a
64-DIP width and green white-text copied state. Coordinate/text copy buttons restore
the exact original caption/template after four seconds; repeated clicks restart the
timer. Tracker feedback is scoped to its entry/step and survives card rebuilds.
Active-list reorder controls move to the top right; Prev/Next are removed there,
More shares the lower action row. Tracker Prev/Next remain independent.
MaxTrackedQuests clamps legacy values to 1–5 (default 5). Excess visible active
quests are reachable with a separate persistent nonactivating vertical scrollbar;
card rebuilds do not destroy its captured thumb. Screen-fit may expose fewer rows
on small clients, with overflow still reachable. Collapse state is per quest/profile;
a collapsed row shows title/position and expand control only. Header drag remains.
Step checkboxes persist explicit completion keys (instruction number + content
anchor), independently of current navigation; identical text at another step is
not automatically marked. Done marks the current step; checking the current step
advances/completes like Done. Other step marks do not advance, and unchecking does
not rewind or reopen a completed quest. Marked cards have green outlines.
These are manual user confirmations, never inferred game progress. Additive fields
retain external progress schema 1, with bounded/validated completion keys. Marks
remain stored when source changes; changed instructions no longer match a mark.
The details Active button is green; explicit restore-to-active is red. New guides
retain the ordinary style. Native smoke checks copied expiry, fold/unfold and
scroll overflow; focused tests check mark/collapse persistence and CID isolation.
Automated validation passed: 306 tests, eight ToC checks and Windows portable startup; real SpringRO/fullscreen/DPI acceptance remains open.

## ROQuestie stable tracker and catalog tags (2026-10-31)

Owner video revealed window destruction/recreation after each action and scrollbar
geometry derived from currently displayed card heights. Action HWNDs are now pooled
and reused; card text swaps in one UI batch. Unused surfaces hide and are reused,
not destroyed. Existing focus/click-through/occlusion behavior remains in place.
Scrollbar has a transparent track and fixed 36-DIP thumb. Track height and page
range depend on configured capacity, text options and available client space,
not which cards happen to be expanded/on screen. A native thumb-drag smoke checks
geometry invariance and game focus; Next checks action HWND identity retention.
Historical_or_seasonal guides are excluded from the working catalog, links and
saved-row presentation. The pinned research snapshot and saved progress are not
modified/deleted; archived keys remain known for safe suppression and recovery.
Catalog metadata is three compact outlined light-navy tags with 3x1-DIP padding:
category, base level and stage count. RU wording is now этапов, EN steps.
A generated edit of the exact selected screenshot is a styling reference only;
shipped tags are WPF controls and are validated in actual app captures.
Automated validation passed: 307 tests, eight ToC checks and Windows portable startup; real SpringRO/mixed-DPI acceptance remains open.

## ROQuestie actual-visual clipping, whitespace waypoints and no scrollbar (2026-10-31)

Owner supersedes scrolling: tracker shows at most the configured first 1–5 quests;
all others remain in the browser. Scroll HWND, template, state and input path are
removed. Pooled tracker surfaces remain to avoid destruction/recreation flicker.
Tracker masks now convert physical control-window rectangles through each actual
WPF visual's PointFromScreen (including its own DPI/layout/render transforms),
instead of dividing them by the game HWND DPI. This fixes premature clipping when
game and WPF scales differ. The native smoke includes a transformed visual mask
regression with points just inside/outside the browser edge.
Research head was checked: 48c9dd28b9c0986ae391f31939fc8961772fbdf7 only changes
README/enrich.py/test_enrich.py, not the shipped schema-v5 SQLite. The integration
contract is unchanged. The application supplements literal wiki locations at read
time using that parser's qualified map+X/Y rule, accepting comma or whitespace.
Map names are checked against sourced NPC/map identifiers or the parser's map-code
and navigation-context rule; numeric prose and bare cells never get guessed maps.
Source NPC map identifiers are a whitelist only, not NPC-position verification.
Bounds, deduplication and original assurance remain; no script candidates/progress
are promoted. The pinned catalog remains read-only and research is not modified.
Focused tests cover niflheim/nif_in and rejection cases plus catalog immutability.
Automated validation passed: 318 tests, eight ToC checks and Windows portable startup; real SpringRO/mixed-DPI acceptance remains open.

## Quest tracker layout and startup catalog — 2026-10-31

The previous clipping fix is not accepted in SpringRO: the user still observes
truncated text/actions. Keep WPF Width/Height synchronized with native layered
window placement; native smoke checks their agreement after dispatcher/layout.
Tracker buttons share template geometry and lowercase captions, including copied.
Quest settings preview sits left of controls and uses read-only examples when no
tracked quests exist. AutoLoadCatalog defaults true, persists with quest settings,
loads at application startup and gates browser/page implicit loading; manual reload
remains available. Check disabled startup, saved option, empty-profile preview,
button heights, monitor DPI, clipping after actions and database/browser priority.
Validation: [Windows run 41182802140](https://github.com/alerion08/RoAura/actions/runs/41182802140)
passed on `ed06d76e16da2be063f1d1f44411ba7a24abd414`: 318 tests, zero failed/skipped,
eight ToC validation/import checks, portable startup and package privacy/resource
checks. Native smoke includes post-layout WPF/HWND size agreement, equal tracker
button heights/lowercase captions, focus/click-through, copied expiry, collapse,
current-step navigation and database priority. The first run failed to compile due
to a probe variable-name collision, corrected before this successful run.
Artifact: [RoAura-development-81-ed06d76](https://github.com/alerion08/RoAura/actions/runs/41182802140/artifacts/13034954377).
Portable SHA256: `ae502ff9067fdca7fa14497bea00d50769ce82315d7bf2a2f3ab4c4f315cb02f`.
Real SpringRO clipping/mixed-DPI and settings preview acceptance remain manual.
No main/research writes, version changes, merge, tag or release.

## Semantic walkthrough redesign — 2026-11-01

Owner specification v1.1: docs/ROQUESTIE_WALKTHROUGH_SPEC.md and the user-attached
Ayothaya HTML are design references only. The production view reads exclusively
Data/quest_semantic.sqlite, semantic schema 5 (not the legacy schema-5 catalog).
Pinned research e3206beccbdd414c6d6f31d146d168247634c578 has 28 fully localized
quests / 420 ready steps. Research and main remain untouched; version stays 3.2.0.
The whole-quest gate compares distinct (quest_id,step_id) membership in ready_steps
and requires all four fields to contain non-whitespace text. A zero-step or
partially translated quest is absent; partial source review is not an exclusion.

Two panels separate selected/read step from current/manual progress. Left short
texts, right full texts, independent scroll, right-only detail/facts/source tabs,
RU/EN, compact coordinate copy, completion and tracking use the existing profile
and progress system. All 28 ready quests match original stage number/kind/text
with the legacy catalog: source anchors remain compatible. Mismatching source
revisions fail closed for editing rather than moving marks by row position.
Descriptions never fall back to old prose. The old catalog/active/history/tracker
are not filtered by semantic readiness. The new view has its own ready selector. Newly ready content absent from the old
progress catalog remains readable, with editing disabled until source alignment;
it never inherits the old selected quest's current/completed/tracking state.

Read-only SQLite uses the existing Windows SQLite reader, schema/integrity guards.
Typed entity spans convert Unicode code-point offsets to UTF-16 and verify exact
surfaces. Coordinates/NPCs/materials/dialogue options/facts/images come from typed
relations. Unknown maps stay unknown; no regular-expression extraction or guessed
NPC identity. Requirements and conditional/branch rewards are displayed as
separate facts, never summed across alternatives. Untranslated original fact
conditions remain verbatim. Server assurance is explicitly unknown for this data,
separate from RU/EN readiness. No game completion is detected automatically.

Reopening, file replacement/change notifications and an active two-second file
revision probe reload the semantic view;
newly ready quests appear without a whitelist, removed entries do not delete
progress. Source screenshots are step-specific; the snapshot currently has zero
images, so neutral placeholders are correct. User image uploads are not enabled.
Feature CI provisions SHA-pinned read-only semantic/legacy fixtures and notices.

Acceptance: readiness 0/1/10/9-of-10, whitespace/NULL, cross-quest membership,
duplicate IDs, newly ready membership, partial source review, Unicode spans,
SQLite immutability; native More/current, copy toast (coordinates retain caption),
focus/input, F9, database priority and snapshots.
Windows validation SUCCESS on `826a2b2ef842f6fe3086efabb484fb0ac3d022bb`:
[run 41320367177](https://github.com/alerion08/RoAura/actions/runs/41320367177),
329 tests, zero failed/skipped, eight ToC checks, startup/native input smoke and
clean portable/resource validation. Actual pinned semantic spans and all 28 legacy
stage mappings pass. Native smoke additionally verifies reading versus current,
right-tab retention and standalone newly-ready content without legacy progress.
RU/EN Ayothaya step-2 snapshots use the actual semantic source, not the HTML data.
All implementation iterations passed Windows CI; visual QA corrected the ready
selector caption, asynchronous snapshot/loading race, checkbox/segment states.
[Development artifact 85](https://github.com/alerion08/RoAura/actions/runs/41320367177/artifacts/13088629427).
Portable SHA256: `d014ac9e39ebe782ea8a256f88b0cbffd2779ecf1023512bbb055ed4b665fcca`.
Actual game/mixed-DPI/narrow-layout acceptance remains manual, not release-ready.

Manual acceptance: real SpringRO mixed-DPI, narrow layout, long/conditional quests,
RU/EN/selected/current/completed/profile isolation, live database replacement.

Manual acceptance: real SpringRO mixed-DPI, narrow layout, long/conditional quests,
RU/EN/selected/current/completed/profile isolation, live database replacement.


## Walkthrough feedback — 2026-11-02

Owner supersedes browser occlusion: opening QuestBrowserWindow must not exclude
pixels from the tracker or ordinary overlay surface. Settings and database
occlusion remain in force. Native smoke checks the browser intersection after
a WPF scale transform; actual SpringRO mixed-DPI acceptance remains manual.

Walkthrough metadata/quality lines and the instruction helper are hidden.
The action is “Выбрать этап” / “Select step”. Legend follows the screenshot.
Compact exact-answer buttons sit in a separate bottom row of the detail pane.
Requirements/rewards use two compact columns and per-step material rows below;
source conditions and alternative branches are preserved, never summed.
Explicit localized map/cell literals supplement missing step_locations without
writing the DB or guessing cells. Both panels share the same coordinate list;
map highlights include numeric cells. Whole-quest readiness and progress guards
remain unchanged. Focused tests cover all 11 Cursed Spirit positions, deduplication,
full numeric highlights, known maps without underscores and invalid cells.

Validation: pending Windows CI for this change; no release/version/main changes.


## Ready catalog and localized quest facts — 2026-11-02

Owner feedback supersedes the previous walkthrough-only readiness filter and
bottom answer footer. Catalog and walkthrough now share whole-quest semantic
readiness; missing/incomplete semantic data produces no legacy catalog fallback.
Active/history/progress storage remain intact. Newly ready IDs without legacy
progress are readable, with editing disabled until matching legacy data exists.
Catalog refresh follows semantic replacement while the catalog tab is open.

Answer options are compact single-button rows in their own card between
coordinates and materials below the screenshot. Stage actions have explicit zero
left margin and align with the instruction and coordinate panel. Quest selector
opens from its entire field; the shared ComboBox theme is unchanged.

Requirements sort level, prerequisites, then materials. Rewards sort Base/Job EXP,
access/unlocks, then items. Numeric alternative-branch experience uses a min/max
range, never sums; unconditional sequential or unknown grants remain separate.
A reviewed exact-source RU dictionary covers all 154 distinct requirement/reward
and step-material comments in the pinned 28-ready-quest snapshot. EN keeps the
original text; SQLite source/evidence is not rewritten. Future unknown comments
retain source text and require a dictionary update rather than guessed translation.
Raw branch IDs are not rendered as user-facing labels.

Checks: focused range/sequential reward, requirement order, ready catalog and
translation coverage tests; native content-area selector click; RU Cursed Spirit
facts and Biolabs answer-card snapshots. Windows CI pending for this block.
Main/research/version/release remain unchanged.


## Independent application and game themes — 2026-11-02

Feature work remains on feature/roquestie-mvp, without a version/release change.
Settings > Appearance adds Application Light/Dark and Game overlays
Follow application/Light/Dark with independent live previews. Additive properties
default to Light/Application for older settings; existing presets/snapshots
serialize them through the existing settings pipeline. No coordinates or custom
HUD colors are rewritten.

Window-scoped palettes and dynamic brush references cover main UI, dialogs,
settings, database search/detail, quest browser and passive quest action surfaces.
Owned dialogs inherit the owning interface's scope. Ordinary transparent HUD
windows retain configured colors. Programmatic quest highlights and database
statistics use the matching palette; cached database presentation redraws without
re-requesting item descriptions. Source game images/minimaps/color swatches are
not recolored. No window ownership, z-order, packet or input-hook changes.

Validation planned: palette key parity, normalization/defaults, independent
resolution, settings/preset and custom-color preservation; real native copy in
dark quests, live mixed scope checks, dark database native capture, RU/EN dark
walkthrough and facts plus Appearance preview PNGs. Full Windows CI pending.
Real SpringRO/mixed-monitor readability remains manual acceptance.


## Release 3.3.0 preparation — 2026-11-02

Owner explicitly requested footer centering and publication of the current feature
work. Twitch/Discord/Donate content, icon and label centers are explicit; startup
smoke checks their measured center alignment in both themes and captures the footer.
Version/manifest are 3.3.0. Release includes ROQuestie and independent themes from
the verified feature line, retaining the exact pinned catalogs used by test build 95.

Release CI now prepares hash-pinned quest data before reader tests and packaging;
installer includes Data/*.sqlite and attribution/license text, and CI silently
installs it into an isolated runner directory to compare installed data hashes.
Silent setup does not display the interactive Npcap notice or launch the app.
Both distributions retain source/license notices and full GFDL/GPL references;
per-page wiki/history and upstream script provenance remain available.
Full release Windows tests, native input and installer verification passed in run 41441524384.
PR #10 was merged; v3.3.0 and both distributions were published on 2026-11-02.
Remaining real-game/mixed-DPI acceptance is documented in release notes.

## Farming sessions and MVP tools — development 2026-11-03

- [ ] Optional Total B.XP / Total J.XP retain session totals across level changes/presets, reset correctly and isolate server/CID.
- [ ] Pause from both pages and hotkey freezes time/EXP/loot/Zeny; balance changes during pause are excluded on resume; relog/restart preserves pause.
- [ ] Real Print Screen works with RoAura, passive overlays, database and quests; modifier/screenshot shortcuts pass.
- [ ] EXP then loot then EXP within the interval extends the initial line; quiet gap separates bursts; groups and line limits remain independent.
- [ ] Exclude omits price/count even with manual prices; re-enable restores valuation; ToC/SpringRO remain separate.
- [ ] Market star/inline add: checked option seeds on a complete live scan, survives restart, ignores old offers and alerts new seller/price. Unchecked behavior unchanged; errors never seed.
- [ ] History shows date, duration, character, count, direct Zeny and loot value; same item at different pickup prices keeps both. Start/reset archives before clearing; deletion removes the file and cannot resurrect from queued writes.
- [ ] MVP map/marker persists in settings/presets. Missing images are explicit. Hover does not consume clicks/focus; verify F9, database/settings, minimized/switching clients and mixed/negative DPI.
- [ ] In-game Add MVP creates/cancels, returns exact-client focus and supports packet-off manual timers. Conflicts leave existing hotkeys intact.
