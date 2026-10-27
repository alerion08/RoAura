# RoAura UI specification

This document records current product-level UI behavior. It is intentionally compact; implementation details remain in XAML and code.

## General interface

- Desktop application: Windows x64, WPF, .NET 8.
- Visual direction: Ragnarok-inspired light-blue interface, not a literal copy of the game client.
- Main navigation stays on the left.
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
- With `OnlyWhenGameIsActive`, overlays hide when the user works in another application and return when the game becomes active.
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

- Timers use the searchable JSON-backed catalog.
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

- RTM and SpringRO data remain separate.
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
