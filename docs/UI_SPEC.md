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
- Temporary window resizing may clamp the displayed position, but must not silently destroy the user's saved layout.
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

## Gold Goblin and Market

- Gold Goblin shows session quantity, unit price and total value.
- NPC price, Overcharge, Vending price and manual rule behavior remain distinct.
- Overcharge levels are 0–10.
- Market search should return cached/local results immediately and refresh in the background.
- The UI must display the effective refresh interval; it must not promise a shorter interval than the implementation allows.
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
