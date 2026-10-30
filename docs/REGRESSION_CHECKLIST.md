# Regression checklist

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
