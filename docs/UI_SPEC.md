# RoAura UI specification

This document records current product-level UI behavior. It is intentionally compact; implementation details remain in XAML and code.

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
- Restart/reset behavior, dedicated hotkey and movable list remain available.
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
reading and remain available where the server supports them. SpringRO HTTP Market
is also independent. Re-enabling restores navigation; verified character statistics
need a fresh observed login rather than reusing the previous CID.

## Settings and character targeting (2026-10-30)

Settings has General, Hotkeys, Character Manager and Navigation tabs. General binds
the same model as Home. All five hotkeys are edited in Settings. Other is last.
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
