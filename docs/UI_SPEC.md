# RoAura UI specification

## Timers for MVPs and regular monsters — 2026-11-05

**Timers** is the navigation and page title. Both creation entry points open the same form. Category **MVP** searches only MVPs and **Monsters** searches only regular entries by name/ID. A category change clears the previous selection; the location list and local sprite/map follow the selected monster. The first known map is preselected, and the confirmed map code and marker are saved per timer. Ordinary and MVP timers share the list, numbers and restart shortcuts.

The overlay row shows an outlined map code such as `pay_fild04` when a location is selected. Hover anywhere on the row to see its minimap and marker. The **In-game panel** tab groups position/text, background (enable/color/opacity) and border (enable/color/thickness), with a live preview. RTM and generic profiles use a reference catalog where server-specific ordinary entries are unavailable.


Current published version: **3.4.0**, [release](https://github.com/alerion08/RoAura/releases/tag/v3.4.0). Dated earlier validation notes are historical; current publication evidence is at the end and in PROJECT_STATE.md.

## Release 3.3.0 completion — 2026-11-03 documentation update

Published v3.3.0 on 2026-11-02 from main commit `7d70b173b7c10cc4ab7ae3f268ff89cb9474e19e` (PR #10).
Final release run https://github.com/alerion08/RoAura/actions/runs/41441524384 completed successfully.
Windows tests, eight ToC checks, native input/startup checks, footer centering in both themes, portable/installer packaging, silent installation and installed database hash verification passed.
Release assets: portable ZIP, Setup.exe and SHA256SUMS.txt.
Earlier “pending CI”, “preparation” and “no release” entries are historical and superseded by this completion record.
Real SpringRO, mixed-DPI, fullscreen, multi-client and long-session acceptance remain manual; they are not marked passed by CI.


This document records current product-level UI behavior. It is intentionally compact; implementation details remain in XAML and code.

## MuhRO packet profile — development branch

MuhRO is selectable in the header/server settings and character manager. It enables cooldowns, effects, reminders and equipment with the Home packet switch. `Muh` is added to the permitted game processes without replacing user entries. Statistics and its reward/rate overlays are available for every server profile when packet reading is on. Manual MVP timers are available independently of packet/server support. MuhRO does not expose another server's database/market as its own. With an empty item cache, equipment learning stops with the existing RU/EN relog-then-retry explanation; login snapshots do not assign a tracker. Other server selections and existing settings remain unchanged.

## General interface

- Desktop application: Windows x64, WPF, .NET 8.
- Visual direction: Ragnarok-inspired light-blue interface, not a literal copy of the game client.
- Main navigation stays on the left: Home; Overlays (Cooldowns, Effects, Reminder, Equipment); Services (MVP Timers, Statistics, Gold Goblin, Database). The sidebar scrolls at minimum height. Unavailable destinations remain visible, disabled, gray (icon and title) and at 60% opacity; a tooltip explains server support or the Home packet switch. A disabled page cannot remain selected.
- Header wordmark has 80% opacity. Windows/About use the supplied crystal icon; Home uses the supplied house. Gold Goblin and Database share other sections’ header-card style, with tabs below the header.
- Settings are organized as readable cards with consistent spacing.
- There is no separate permanent preview panel.
- Russian and English layouts must expose the same functionality and localization keys.
- Text must remain readable at supported Windows scaling values; controls must not clip at 125%, 150% or on mixed-DPI monitors.
- User-facing version text comes from assembly/project metadata, not a separate hard-coded value.
- Closing the main window exits RoAura cleanly unless a deliberately documented tray mode is introduced later.

## Common interaction rules

- Controls must not move or resize unexpectedly when data arrives.
- Empty sections must not reserve unexplained blank space.
- A missing selection must be handled safely; buttons should be disabled or no-op rather than crash.
- Destructive actions require clear intent and must not silently erase settings or presets.
- Long network or file operations must not freeze the UI.
- Errors should be understandable to the user and detailed in the diagnostic log.
- User-entered names take precedence over database fallback names unless the user explicitly replaces them.
- Positions, scale, opacity, colors and enabled states persist through settings/presets where supported.
- Pixel sizes, padding, spacing, coordinates and opacity editors use `NumericTextBox`: the editable text is a local draft, separate from the numeric model binding. Enter or leaving the field (Tab/click) commits the complete number; Escape discards the draft. Source notifications must not replace a draft or move its caret. Existing model limits apply only at commit; the displayed result is read back even when clamping raises no change event. Empty/invalid/non-finite input keeps the last valid model value and shows WPF validation. Both decimal separators are accepted.

## Shared controls and presets

- Dropdown arrows remain blue with no separate border, fill or checked/hover highlight. Popups open below their owning field.
- TextBoxBase forwards Padding to the content host; the rounded template must not duplicate it as a host margin. Compact editor viewports must fit the complete text line. Database watch/history text columns use explicit vertical centering.
- Text, numeric and color editors share a 5-DIP rounded template. Field styles inherit the common template. Slider tracks use blue progress, a pale remainder and a round outlined thumb throughout the app.
- Color fields open an in-app palette with RGBA sliders, preview and a HEX draft accepting #RRGGBB or #AARRGGBB. Apply commits; Cancel preserves the original value and alpha.
- Selecting a named preset applies all serialized settings without restarting the process. Existing model/collection references remain valid for running services and hotkeys. Editors detach during the copy; learning, transient notifications and session statistics reset. Settings, layout and exact equipment variants are retained by each snapshot.
- Named files are `%APPDATA%/RoAura/Profiles/<preset name>.json`. Create/update are explicit snapshots; autosave only changes settings.json. Switching backs up the working copy. Market history remains separate from presets.
- MVP selection synchronization never opens search or queues an old text draft when switching timer rows. Search runs only while typing in the focused editor; no empty popup is opened.

## Overlay window behavior

- Overlays are non-activating and must not steal keyboard focus from Ragnarok.
- They must not block or reserve Alt, Ctrl, Shift or the Windows key.
- Do not make the Ragnarok process the owner of an overlay window through cross-process `GWL_HWNDPARENT`.
- While the active Ragnarok client is eligible, overlays remain visually above it, including protected/fullscreen clients that may reclaim Z-order.
- When Ragnarok is not found, applicable game overlays are hidden.
- While RoAura settings or their dialogs are foreground, ordinary overlays remain visible above the game except beneath those windows. Only the shared overlay rendering clip changes; coordinates and focus remain intact. A settings window on another monitor leaves the full game overlay visible. Returning to the game clears this exclusion.
- Unrelated foreground applications retain the existing overlap-based hide/demotion behavior.
- Switching between multiple allowed clients must update the target window.
- Hidden launcher/helper windows must not be chosen as the game client.
- Coordinates are relative to the selected game client and are converted correctly for per-monitor DPI.
- Resizing, DPI changes and switching clients must never rewrite or autosave model coordinates. Positions outside a temporarily smaller client return when its original size is restored; dragging or editing coordinates remains an explicit user action.
- Moving an overlay saves coordinates with a short debounce rather than writing on every mouse event.
- F9 toggles all overlays unless the user changes that binding.
- Individual drag and group drag must both remain available where currently supported.

## Cooldowns

- The PNG fills the icon tile.
- Border is neutral, not a permanent green state.
- Optional short skill name and `READY` text are rendered inside the icon.
- During cooldown, a large bold remaining-time value is centered.
- Cooldown progress fill is visible and configurable.
- Cooldown state may reduce icon opacity.
- Size, opacity, position and group movement remain configurable.
- Packet detection is authoritative; image confirmation is not required.

## Effects and Buff/Debuff notifications

- Status IDs are detected from packets.
- Appearance and disappearance can have separate text and separate sounds.
- Position and size can be configured globally for the notification group.
- Simultaneous notifications stack with consistent spacing and do not overlap.
- Notifications can be dragged to establish their position.
- Image-search confirmation is not part of the packet-based mode.

## Buff Panel

- Effects appear in a single horizontal line/grouped rows according to current settings.
- Each effect can be enabled or disabled independently.
- Active effects are colored; inactive effects are grayscale.
- Remaining duration/fill is shown for active statuses when duration data is available.
- Panel scale and position are independent from notification scale and position.

## Reminder

- Missing-status reminders use centered, configurable icon/text presentation.
- Existing reminder logic and packet-derived state must remain unchanged during visual work.
- Reminder placement and visibility participate in the global overlay behavior.

## Equipment

- Learned equipment is identified from packets using inventory index, Item ID and wear slot.
- Exact variants may include refine, grade, cards and random options.
- UI changes must not discard learned equipment mappings or preset data.
- A visual redesign must preserve all edit, learn, clear and display actions currently available.

## MVP Timers

- Timers use the searchable catalog for the selected server; ToC uses its compact SQLite snapshot. ToC intervals are not supplied by the source and must be entered manually. ToC timers are saved separately from legacy server timers.
- Restart/reset behavior, dedicated hotkey and movable list remain available. Manual timers and their overlay are available for all profiles, including MuhRO and Other, independent of packet reading.
- List/overlay numbers follow the full active collection order, including disabled/stopped rows. Numbers do not shift on completion or hiding; deleting/reordering a row changes subsequent list positions. More than twelve timers remain allowed.
- Settings → Hotkeys → MVP Timers includes an expandable set of twelve optional restart shortcuts (None by default). A numbered shortcut opens the existing form prefilled from that timer, with a new exact keypress kill time and no old marker. Place the new marker and confirm Restart to update the same timer; Cancel/repeated shortcut commits nothing. Unassigned/empty slots do nothing; conflicts preserve older shortcuts and disable duplicate indexed bindings.
- Custom user names must not be replaced by database names after refresh.
- Empty or unselected rows must be handled safely.

## Loot, experience and farming overlays

- Loot, Zeny, Base EXP and Job EXP notifications preserve separate colors and grouping.
- Notifications stack without overlap and respect per-group line limits.
- XP/hour, time-to-level, Zeny/hour and Zeny Total remain independently configurable.
- Indicator order remains user-configurable where supported.
- Gold Goblin valuation may be included in loot notifications and Zeny/hour when enabled.

## Database panels

- RTM, SpringRO and ToC data remain separate. ToC items/monsters/maps are read offline; only images use remote URLs. SpringRO HTTP item details and Market are never substituted for ToC data.
- Item, monster and Market panels open over the currently selected Ragnarok client.
- They must not first appear over another application and then jump to the game.
- First open and first click inside the game must behave the same as later opens.
- Panels account for mixed-DPI coordinates.
- Database windows must not steal focus or reintroduce modifier-key blocking.
- Loading, empty and error states must be visible and must not erase previously cached usable Market data.

### Market watchlist

- The watch table has exactly three columns with a filling name column and a centered Enabled checkbox. Below it, the selected item's history shows local detection time, stall name, map/coordinates and price, newest first. Without a selection it shows all stored entries.
- General exposes history retention in whole days (1–3650, default 30). Cleanup runs at startup, when changing retention, during successful evaluation, and hourly even if the network is unavailable. Expired entries are deleted and may notify again on a later scan.
- Every newly discovered qualifying SELL offer is recorded, while notifications remain grouped to the cheapest new match per item per refresh. Disk writes are atomic and happen before alerts are presented. A failed read preserves the file; read/write errors show a warning because cross-restart deduplication then cannot be guaranteed.
- Sound volume is an integer percentage; slider steps are whole percentages and the choose/listen buttons have equal height.

- A star on item results/details toggles tracking. Inactive stars are semi-transparent; active stars are dark blue.
- Adding opens a borderless confirmation dialog centered in the gap between the search and detail panels, regardless of which star was clicked. Positions use the panels’ DIP coordinates and follow client repositioning. Maximum whole-Zeny price or Any price is required; Cancel changes nothing.
- The Watchlist tab uses a left-hand table and a Gold Goblin-style editor card on the right. Add/edit/delete actions stay at the bottom of that card; selecting a row loads its rule into the editor.
- Both the inline editor and the star dialog share a compact 32-DIP field editor. The refresh interval field is 30 DIP high; the star dialog is 400 DIP wide (20% narrower than the original).
- Search suggestions appear in a popup with wrapping full names, slot suffixes and IDs. Opening/updating the popup must not select the first result, overwrite input, steal typing focus or select text. Only a click or keyboard confirmation writes a chosen name. The popup closes on outside click, focus loss or leaving the tab.
- The third Watchlist column toggles each rule’s Enabled state without removing the item or price. Paused rules do not match snapshots; resuming checks the next live snapshot against retained history. Paused stars fade and can resume the saved rule. Editing a paused rule preserves its state. Old settings default to Enabled.
- Notifications starts with a permanent live preview using the same `MarketWatchNotificationView` as the game overlay. Compact rows use narrow numeric fields; X/Y share one line. Show icon hides the icon and its margin. Sound has a listen action and a 0–100% volume slider/editor; both alerts and audition use that volume.
- Move in game starts the existing overlay edit lifecycle with a persistent sample, client-relative drag and the configured finish hotkey (Escape by default). Finishing removes only placement samples, saves coordinates and restores click-through behavior. F9/settings shortcuts use the existing end-edit flow.
- The Notifications tab retains `%name`/`%price`, custom/default icon, sizes, colors/alpha, padding, spacing, duration, sound and settings/preset compatibility.
- Successful fresh SpringRO SELL snapshots trigger new qualifying offers only; existing cached data and failed refreshes do not trigger/rearm notifications. The first successful live snapshot may notify about already-present offers. Unchanged offers/quantity-only changes remain silent, including after absence/return or restart while their history is retained. New seller/price/equipment variants are distinct offers.
- Notifications stack in the existing overlay host and follow its visibility and F9 controls. Rules, appearance and retention duration are stored in settings/presets. Offer history is stored separately under `%LOCALAPPDATA%/RoAura/market-watch-history.json`, never inside a preset or portable package.

- Market notification settings keep preview, enable/icon actions and template above the informational help card; Appearance and Sound tabs follow that card. Equipment learning help uses the same information-card styling as Effects.

## Gold Goblin and Market

- Gold Goblin shows session quantity, unit price and total value.
- NPC price, Overcharge, Vending price and manual rule behavior remain distinct.
- Overcharge levels are 0–10.
- Market search should return cached/local results immediately and refresh in the background.
- The configured refresh interval is 1–1440 minutes (default 10), without a hidden ten-minute minimum. Warn that frequent refreshes can result in a server-side block. Keep non-overlap and server-directed back-off.
- Network failures preserve the last usable cache and report the failure without freezing the interface.

## Accessibility and layout checks

For any changed screen, verify:

- Russian and English text;
- 100%, 125% and 150% Windows scaling when feasible;
- narrow window/minimum supported size;
- empty, loading, error and populated states;
- keyboard focus and modifier keys;
- long names and large numeric values;
- no content hidden behind scrollbars or collapsed panels.

## Home packet-reading control

A RU/EN warning explains that active Npcap packet reading may violate the selected
Ragnarok server's rules. The toggle immediately persists the existing packet
preference, stops/starts workers, cancels learning and invalidates old flow state.
It does not delete configured overlays, rules, timers, saved history or positions.
Cooldowns, Effects/Buff Panel, Reminder, Equipment, Statistics and Gold Goblin
become unavailable while off. Database and manual MVP timers do not require packet
reading. Manual MVP timers are available in every profile; database availability remains server-specific. SpringRO HTTP Market
is also independent. Re-enabling restores navigation; verified character statistics
need a fresh observed login rather than reusing the previous CID.

## Settings and character targeting (2026-10-30)

Settings tabs start with Basic and Appearance, followed by Hotkeys, Character Manager and Navigation. Basic binds
the same model as Home. Hotkeys are grouped by General, Statistics/Gold Goblin, MVP Timers, Database and ROQuestie. Other is last.
Optional navigation visibility only affects the left menu; unsupported or packet-disabled
features remain gray/disabled. Home cannot be hidden. Settings is opened by the header button replacing the old
hotkey legend; it has no sidebar entry.

Every overlay editor offers a collapsed character selector; no checked characters means
all, several checked characters means any matching observed server/CID. Unknown session
hides targeted overlays. Aliases and assigned server are metadata; history/CID is immutable.
Character metadata survives preset changes. Buff panel groups also have independent scope.

Cooldowns detect their draft ID in step two before creation. Empty overlay list/editor
areas use the same centered card, title/hint typography, padding and alignment.
Database statistics use paired label/value cells; technical extras and skill descriptions
are collapsed. ToC MVP respawn labels use official map detail data when published.

Overlay list help cards share the equipment info-card template, padding, icon and
text color. Empty-state titles and field labels use RoTextBrush explicitly.
The About icon always uses its original full-color source.

## ROQuestie stage 2 (feature branch, 2026-10-30)

Quests follows Database in Services and remains available on every server with
packets off. Settings stays at tab/enum 9 and opens from the header; Quests is
appended at 10. Existing enum values and destinations are preserved through an
explicit destination mapping. Shared sidebar rows are 44 DIP with 2-DIP vertical
margins and 30-DIP icons. The supplied 48px scroll asset is a WPF resource with
NearestNeighbor; About behavior is untouched.

The light-blue section has the shared header card above Catalog / Active quests /
History / Overlay settings. Catalog search debounces typing, with provisional
category and optional character Base Lv. filters; unknown levels remain visible.
Original names/text stay in their source language. Details show every guide row,
requirements/rewards (unknown means Not specified), literal wiki coordinates,
freshness/source URL/hash and assurance. No candidate script becomes a gameplay
confirmation. More selects the same full detail pane; a game-side More window
belongs to stage 3.

An automatic observed CID or an explicit manual saved CID/local profile is
required to edit progress. Manual mode is visibly labeled. Immutable detected
server identity, not an editable server alias, identifies saved profiles. Unknown
sessions do not inherit the last CID; changing server clears manual selection.
Backup-only records can be selected read-only. Active quests have independent
Prev/Next, visibility, ordering, pause, manual completion confirmation and removal.
History/restoration retains dates and explicitly manual evidence.

Quest progress autosaves after 400ms, flushes before scope switch and normal exit,
and remains outside presets. Background saves use isolated document copies and
stale-writer protection. Failed writes retain pending changes and prevent a scope
switch or normal exit from silently discarding them. Settings schema 14 adds only
QuestOverlay visual options, preserving schema-13 layout. Named presets copy the
nested visual model while retaining its observed reference and external progress.

Overlay settings use NumericTextBox and the standard color palette. The in-app
preview has smaller 22-DIP buttons and honors width/text/opacity/spacing/scale,
coordinates and button visibility. Stage 2 does not create a game overlay or send
navi; the UI explicitly says these are later stages. No duplicate hotkey editor.
CI startup smoke exercises all-server packet-off navigation, the real catalog,
two manual guides and RU/EN render captures at 100/125/150% equivalent pixel
densities. These captures are not real mixed-monitor OS DPI/game acceptance.

## ROQuestie game-first direction (supersedes stage 2)

See `ROQUESTIE_PRODUCT_DIRECTION.md` for the owner's explicit priority override.
The main application now contains quest settings only. F6 opens/closes a large
in-game catalog/active/history/walkthrough window; F5 toggles the independent edge
tracker. Defaults participate in the existing hotkey service/presets and global
F9 mask. Conflicts disable new quest assignments only, with a localized hint.
Settings schema 15 adds hotkeys and independent client-relative browser geometry.

The browser reuses the proven activating Database interaction pattern for typing,
but does not make the game its owner. Walkthrough is a full-width tab, with current
instruction border/background, jump-to-current and direct manual selection. More
from any tracker row opens that quest here. Requirements/rewards share a structured
source-labeled tab; original text is retained. Known field labels may be localized;
names, conditions, quantities, variants and unsupported text are never guessed.

Tracker text is transparent, shadowed and compact. Its full-client text HWND is
WS_EX_TRANSPARENT/NOACTIVATE. Separate tiny layered action windows use NOACTIVATE
and WM_MOUSEACTIVATE=MA_NOACTIVATE, with nonfocusable controls and transparent
margins. No HTTRANSPARENT cross-process assumption, global mouse hook or new
capture technology. Visible buttons handle only their painted area; text/world
clicks pass through. Drag the small handle; mouse-up/finish key ends placement.

Both window types follow the selected eligible client in physical screen bounds,
with per-window DPI and model coordinates in DIP. Browser moves/resizes persist
only on explicit user geometry changes. Tiny/minimized/missing clients hide
surfaces without rewriting layouts. Existing occlusion excludes passive quest
HWNDs from control masks and treats the browser as a real control window.

Navi filters only same-step literal wiki map/cells; bare/candidate/invalid points
stay ineligible. Multiple targets require explicit choice. It shares the existing
bounded clipboard helper with Database; copied is not navigation-started. The
source/server uncertainty remains visible. Other stays reference-only.

Optional CI smoke uses a separate fake-game executable and real OS input to test
button focus/clicks, transparent text/margins, keyboard/modifiers, independent keys,
F9, settings exclusion and resize/minimize. This does not establish protected
SpringRO/fullscreen/mixed-DPI acceptance. Real-game manual gates remain mandatory.

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

- Optional Total B.XP / Total J.XP are initially disabled and use existing indicator style/placement/presets.
- Statistics and Gold Goblin share character-session pause (default Pause key). Paused elapsed time/EXP/loot/income freeze; balance baselines continue. Session time shows a pause marker.
- Print Screen and modifiers always pass the hook, including keyboard suppression. Screenshot keys cannot be consuming hotkeys.
- Each reward extends all still-aggregating rows of its group; a quiet gap starts a new burst. Groups remain independent.
- Exclude price rules remove value and counted quantity even with a manual override; raw history retains excluded acquisitions.
- The shared market editor offers silent current-offer baseline, established by a requested successful live scan and retained with the rule.
- Gold Goblin History contains the session list and pickup details directly in its tab; selection shows timestamped pickup prices. Direct Zeny and loot value are separate. Confirmed deletion removes the file and cancels queued recreation.
- MVP Location selects a map and marker. Changing the map clears its marker. Passive hover preview is nonactivating and mouse-transparent. Add MVP in game defaults F4, follows the exact client, uses the game theme and restores focus. New conflicting bindings disable themselves.

The MVP game form and passive map preview occlude lower HUD/tracker pixels even when game focus is restored; quest-browser visibility behavior remains unchanged. Identical pickup price buckets group quantities and show their first/last receipt times.

Validation SUCCESS on code commit `bf6f97113a7e2c14c3e531e21caa94ca9f4dfdcb`: [development run 41619866259](https://github.com/alerion08/RoAura/actions/runs/41619866259) — 352 tests, zero failures/skips, eight ToC checks, native startup/input including Print Screen, MVP hotkey/create/cancel/focus and HUD occlusion, resource/privacy checks and portable packaging. [PR build 41619876280](https://github.com/alerion08/RoAura/actions/runs/41619876280) also succeeded, including installer checks.
Real Ragnarok/mixed-monitor/long-session acceptance remains manual.

## Feedback iteration — 2026-11-03

Respawn locations are clickable cards containing the original name/code/interval, selected with theme colors. In the compact creation wizard they sit below Start immediately, and the right card shows the map. Repeated Add MVP hotkey closes the form. Map previews occupy a disjoint rectangle beside the complete timer panel.

Market history can show all items while preserving the selected watch editor. Session history uses one list with Open/double-click details in another window; details omit receipt dates/times. Gold Goblin has four actions and no CSV button.

Hotkey fields record a key or chord on keyboard press, expose Clear through their context menu, and report duplicate assignments. Modifier combinations use exact matching and one action per physical press. Recorded input does not invoke a newly assigned action before release. Character removal preserves local histories and cannot silently broaden character-scoped overlays. Reminder mode cards use live palette brushes.

Verified code `88a43a460958d2b3769def8fd5b0ef355f485105`: [development run 41626955122](https://github.com/alerion08/RoAura/actions/runs/41626955122) passed 362 tests (zero failures/skips), eight ToC checks, portable/native input smoke, RU/EN resources and clean-payload checks. Native input verifies keyboard capture of Ctrl+Shift+F4, no activation during assignment, exact modifiers, repeated-key close, create/cancel and exact-client focus restore. Dark MVP/location-card and Reminder rasters were inspected.
Real client and mixed-DPI checks remain manual.

## MVP form refinement — 2026-11-03

The MVP form has rounded client corners and a small drag grip. Explicit drag position persists in game-relative DIP without resize/DPI rewriting it. Content clicks open the searchable MVP dropdown, preserve draft/caret and never select entered characters automatically. A newly selected MVP defaults to its first map; an existing explicit map selection remains unchanged.

Absent/loading map content shows a bordered help card in the picture area. A loaded map has no permanent bottom instruction or Clear marker action. Timer placement keeps its edit surface above the game and uses a temporary visual offset for off-screen saved positions, without overwriting them before a drag.

Hotkey fields expose a visible × clear action that sets None, retaining the source binding. The session-summary Item column from build 110 is superseded by the tracking-history correction below.

Validated on `cf4e87e145de8bb293ccd11dafc8938e86f50f82` by [Windows development 110](https://github.com/alerion08/RoAura/actions/runs/41636326233): 364 tests plus native keyboard/mouse checks for clear/reassignment, dropdown typing and form/timer movement. Empty/loaded dark MVP and unassigned-hotkey rasters inspected. Real game and mixed-DPI acceptance remains manual.

## MVP map and tracking-history correction — 2026-11-03

MVP source details initialize the first map for new/restored timers with no location. Bundled maps display offline; downloaded maps share a persistent local cache across editor and hover views. The map region shows a loading state while loading, an unavailable/retry state on failure, and the location hint only when no location exists. Switching selection or closing/reopening must not show a stale map.

Restart always starts a complete interval from the current time. Exact kill-time creation remains available in the wizard; an existing timer's clock draft is applied through its separate Apply time button.

Database tracking history shows Item names when All history is enabled or no item is selected; full names are available on hover. Selecting one item hides the redundant name column. Gold Goblin session summaries retain their original columns, with item names in the separate loot detail window.

Validation: `6e51e05e7588f42471913b8fb6c0b14fd486bbf1` / [development 113](https://github.com/alerion08/RoAura/actions/runs/41662661959) passed 368 tests and native startup/input checks, including offline decoding of every bundled map, first-map painting with no location click and restarting from an invalid previous clock draft. All 53 packaged images match the manifest and decode independently. Real client/DPI acceptance remains manual.

## Application MVP location layout — 2026-11-04

In the application's timer editor, the Location block has two columns: narrower selectable respawn cards on the left, marker minimap on the right. The map is at most 250 DIP wide and scales down at the minimum window width. Location names wrap and long lists scroll vertically. The in-game Add timer form keeps its existing layout.

Validation on `7be636a5ea1e9ac4cab38bee5be053da97cf2a2c`: 368 tests and portable native/startup checks passed in [development 116](https://github.com/alerion08/RoAura/actions/runs/41793270258). Light/dark Location-block rasters with selected location and marker inspected on the constrained CI desktop. Captures use programmatic selection; real main-window resizing/clicks and mixed-DPI acceptance remain manual.


## Shared session controls and MVP magnifier — 2026-11-04

Both Statistics and Gold Goblin use the same Start/Reset actions. Both clear session base/job EXP, dropped items and editable Gold Goblin rows, earned Zeny, elapsed active time, pause and reward notification groups. Old receipt-price history, lifetime aggregates, character level progress and price rules remain. Start starts the clock immediately; Reset leaves it stopped until the next experience gain. The Start hotkey defaults to None; its configured press uses the same application operation and preserves the completed history.

Gold Goblin calculation settings include Ignore direct Zeny income. This excludes all positive balance changes from displayed Zeny Total/h independently of the optional loot valuation; raw balance baselines, reward notifications and receipt history remain factual. Trades and NPC sales are not distinguished by absolute balance packets.

All table cells and headers center vertically through the shared theme; text/right/left horizontal alignment remains per column. Basic is the first Settings tab, Appearance second. Hotkeys are grouped by function; None is not a conflict. A magnifier remains immediately after each MVP name. Hovering the entire timer row (name, magnifier, time and gaps) opens the minimap; leaving the row closes it. This supersedes the initial magnifier-only behavior. The preview retains its separate passive HWND and placement outside the timer panel.

Verified on `f680a488b7a6e6b9b7051c6cd6faf01937fc1c90`: [Windows development 119](https://github.com/alerion08/RoAura/actions/runs/41802211067) passed 376 tests, eight ToC checks, native input/startup and clean portable checks. Native probe verifies magnifier-only hover/close and retained game focus, plus exact Start chord/None dispatch; actual row/map rasters inspected. [Installer attempt 2](https://github.com/alerion08/RoAura/actions/runs/41802217842/attempts/2) passed after the known unrelated ROQuestie toast-expiry timeout on attempt 1. Real gameplay/mixed-DPI acceptance remains manual.


## Interface layout mode — 2026-11-04

Settings → Hotkeys → General records Interface layout mode (None by default). The configured chord toggles one global group editor. Repeating it or pressing the configured Finish movement key saves and exits with game focus retained; F8 exits and opens Settings, and F9 exits/hides overlays.

Every available group receives a passive section/group label and a small ⋮⋮ grip like the quest tracker. Cooldowns, effect notifications, buff panels, reminders, equipment, rate indicators and reward notifications have separate group targets. Market notifications, MVP timers and ROQuestie move by section. Group 0 is a single ungrouped section bucket; this mode has no individual-item movement. All allowed members of a group, including disabled members, receive the same delta; character-scoped objects from other characters remain untouched. Cooldown group labels retain custom names.

Only the grip is interactive; labels and ordinary overlay HWNDs remain mouse-transparent/nonactivating. Temporary notification/MVP/empty-quest previews make quiet panels placeable without simulating packet events, sounds, cooldowns or session gains. Existing requested visibility and serialized appearance/enable preferences survive exit. Merely entering the editor or clamping an off-screen grip does not rewrite model coordinates; the first deliberate drag brings the group into bounds while preserving spacing. Movement converts physical mouse deltas using the selected game client's DPI. Alt-Tab/minimize hides grips and releases mouse capture; changing client, character identity, server, packet gate, preset or closing the application finishes the mode and cleans previews. Settings opened from the app end the mode.

The MVP minimap uses the whole timer row as its hover target again, including its retained magnifier. Map placement beside the complete panel and passive focus behavior remain unchanged; minimap hover is suspended while layout mode is active.

Verified on `ba6b96986eb4a2ab3d11ff36c2cd84dbcce3dba1`: [development 121](https://github.com/alerion08/RoAura/actions/runs/41809493268) passed 382 tests, eight ToC checks, native input/startup and clean portable verification. Native probes exercise full-row MVP hover/close/focus, exact layout chord, passive label clicks, whole-group movement with disabled members, unrelated group preservation, repeated-key/Finish exit and cleanup. Geometry tests cover 100/125/150% scaling; actual layout/map rasters inspected. [Installer validation](https://github.com/alerion08/RoAura/actions/runs/41809501092) passed packaging, silent installation and payload checks. Actual Ragnarok/fullscreen, OS mixed-DPI transitions and live client/character changes remain manual acceptance.


## Published 3.4.0 — 2026-11-04

The owner approved publication after accepting development 121. PR #12 is merged; [v3.4.0](https://github.com/alerion08/RoAura/releases/tag/v3.4.0) resolves to `8bceb8c81c6530af4a634d09d53321b9b4953e22`. Shared session/history/market/MVP/layout/hotkey behavior above is shipped: minimap hover covers the complete row, and the global editor moves whole groups/sections through passive labels and separate grips.

[Publication workflow 41827714651](https://github.com/alerion08/RoAura/actions/runs/41827714651) passed 382 tests, eight ToC checks, native input/startup, portable/installer and silent-install/payload checks. Both public packages and SHA256SUMS.txt were verified against public asset digests; primary PE versions and all packaged document headers match 3.4.0 and all 53 maps are present. Current published hashes are in PROJECT_STATE.md. Earlier candidate-run notes are historical evidence, not the published binaries.

Release/development identities remain separate; no automatic RoAura-Dev import occurs. General owner acceptance does not establish physical mixed-DPI, protected/fullscreen, live client switching or long-session coverage. This documentation-only main update does not rebuild or replace the release.
