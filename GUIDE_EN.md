# RoAura 3.4.0 — user guide

## Gold Goblin

This section is available for SpringRO and ToC; Vending prices apply to SpringRO only. Received loot is automatically added to the `Item / Count / Cost / Total` table. The default cost is the official NPC sell value. Select an Overcharge level to apply the NPC selling bonus. Special price rules can use the lowest current Vending sell price and/or a manual price; when Vending is enabled, the manual value is used if the item is not listed. Start, Pause and complete Reset are shared with Statistics; completed history and price rules remain.

In “Loot / Zeny / EXP”, `Zeny Price` appends the calculated value to loot notifications, while `Gold Goblin` includes loot value in Zeny per hour.

## In-game database

1. Open **Database** and select a hotkey (`F7` by default).
2. Switch to Ragnarok Online and press the hotkey. Search and results open at the left edge, while the detail card opens at the right edge. The center remains unobstructed.
3. Select **Monsters**, **Items**, **Maps**, or **Market**, then enter a name or numeric ID.
4. Monster pages link to drops and maps; item pages link to drop sources; map pages link to all monsters on that map.
5. In **Market**, choose Selling or Buying. Search reads current SpringRO listings by item name only. The **Navi** button copies a command such as `/navi prontera 183/213`.
6. Press the hotkey again to close the database and return focus to the game.

While the database is open, keyboard input stays in its search UI and does not reach RO. The database panel accepts mouse input; clicks outside the panel remain available to the game.

RoAura is a self-contained Windows overlay for Ragnarok Online cooldowns, buff/debuff events and missing-status reminders. It reads incoming `Skill ID` and `Status ID` packets; it does not press keys or modify the game client.

## Installation and first launch

Windows 10/11 x64 is required; packet features need the Npcap system driver. .NET, Wireshark and AutoHotkey are not required.

1. Download Npcap only from https://npcap.com/#download and install it with default options.
2. Run `RoAura.exe` before character login, then enter the game. Relog if RoAura was started after login, so CID and the initial balance can be observed.
3. Keep `Tracking enabled` and `Capture packets via Npcap` enabled.
4. Keep `Detect IP and port automatically` enabled. RoAura finds the active `PRM` connection and applies its current endpoint, including an ExitLag loopback endpoint. Use `Detect now` for an immediate retry.
5. Click `Save`.

If Npcap cannot open an adapter, run RoAura as administrator. After switching characters, use `Reset AID` if events stop matching.

## Hotkeys

Open Settings → Hotkeys, click an assignment field and press a supported key/chord. × sets Unassigned. Conflicts are highlighted; keep assignments unique. Fields are grouped by feature.

- `F8` — Settings.
- `F9` — global overlay visibility; tracking continues.
- `F10` — MVP panel visibility without stopping timers.
- `F7` — in-game database.
- `F6` — ROQuestie browser; `F5` — separate tracker.
- `F4` — in-game Add MVP form; repeat closes it.
- `Pause` — shared farming-session pause.
- `Escape` — finish placement; global layout mode exits while keeping game focus.
- Start session and Interface layout mode are initially unassigned.

Print Screen and Windows screenshot shortcuts remain forwarded.

## Interface layout mode

Assign its hotkey under Settings → Hotkeys → General → Interface layout mode. Press it in game to show section/group labels and ⋮⋮ grips. Drag a grip to move the entire group, retaining member spacing, including disabled members. MVP, quest tracker and market notifications use section targets. Group 0 is one Ungrouped bucket, without individual grips.

Quiet notifications and empty panels have temporary previews. Repeat the hotkey or use Finish placement to save positions and remove the editor. F8 exits and opens Settings; F9 exits and hides overlays. Labels are click-through; only grips accept mouse input. Entering does not rewrite coordinates or change cooldown/session state. Existing individual placement controls remain available in their sections.

## Shared sessions and Gold Goblin history

For an identified character, Start in Statistics/Gold Goblin or the assigned hotkey clears the current session and starts its clock immediately. Reset on either page clears session EXP, items, Zeny, active time, pause and transient rewards; the clock then starts on the next EXP gain. Pause excludes time and gains while updating the balance baseline. Level progress, lifetime totals, price rules and completed history remain.

Select a session and use Open or double-click for separate item details. Prices are captured at acquisition rather than recomputed from today's market. Delete session removes its file from `%LOCALAPPDATA%\RoAura\GoldGoblinSessions`. Character statistics remain separately under `%LOCALAPPDATA%\RoAura\CharacterStatistics`.

Excluded price rules omit an item from valuation. Ignore direct Zeny income excludes all positive balance changes from Total/h; the packet cannot distinguish player trades from NPC sales. Raw balance/history remain factual. Optional loot valuation is controlled independently.

## Cooldowns

1. Add a skill, enter its name and cooldown, and choose a PNG.
2. Click `Learn Skill ID (20 sec)` and successfully use only that skill.
3. Check it with `Test cooldown`.
4. Every row has its own group, size, icon-to-background gap, X/Y and two 0–100% opacity values: ready and on cooldown. Set the gap to `0` for the tightest full-fill image.
5. `Move selected` moves icons individually. `Move group` moves every icon with the dragged icon's group number; group `0` remains individual. Drag the overlay, then press ESC.

The selected row is also shown in structured cards below the table, where main values, appearance and the loaded icon preview are easier to edit. Table columns remain available for quickly comparing multiple skills.

`Short names` and `READY label` apply globally.

## Buffs / Debuffs

1. Add an effect, name it and choose a PNG.
2. Click `Learn Status ID (20 sec)` and receive the effect in-game.
3. Configure separate appeared/disappeared events and text.
4. Set duration, repeat protection, group, X/Y, opacity, separate icon/text sizes and icon-to-background gap.
5. Icon, background and border can be disabled independently. Text/background colors use `#AARRGGBB`.
6. Appeared/disappeared sounds and 0–100% volumes are independent.

Notifications from the same group stack downward when triggered close together. Different groups have independent queues. Move notifications individually or by numbered group and finish with the configured placement key.

### Buff Panel

Enable `Panel` for effects that should remain visible. Effects with the same group number form an independent horizontal row, ordered left to right. Active effects stay colored; missing effects are gray and translucent.

Select a group under `Active effects panel` to set X/Y, icon size, icon-to-background gap and active/inactive opacity. `Move group panel` hides settings and lets you drag that entire row.

For effects whose packet includes a duration, `Show effect time as numbers` and `Fill icon by remaining time` can be enabled independently or together. Infinite/simple statuses remain visible without a countdown.

## Reminder

A Reminder shows its icon and text while its `Status ID` is missing. It hides while the status is active and returns when it ends.

1. Add a Reminder, enter its name/text and choose a PNG.
2. Click `Learn Status ID (20 sec)` and activate the required status.
3. Configure icon/text size, icon-to-background gap, opacity, text/background/border colors and visibility.
4. Icon and text are centered on X/Y. Move one Reminder or its whole group, then use the configured placement key.

## Equipment

Use this tab to see which tracked item is currently equipped during weapon, armor, or element swaps.

1. Add an entry, set its name/text, and select a PNG.
2. For the first learning attempt, start RoAura before logging the character in, or relog once while RoAura is already running. This lets the app cache the inventory-index-to-Item-ID mapping. Wait until the character is fully loaded.
3. Only then click `Learn equipment (20 sec)` and equip the item in game. RoAura stores the packet ID, inventory index, Item ID, wear slot, refine level, grade, four cards, and random options.
4. Configure icon/text size, icon-to-background gap, opacity, colors, background, border, and placement.
5. The entry is visible only while equipped; use the test button for appearance checks.
6. Move one entry or an entire numbered group.

RoAura also refreshes the equipment cache when a new item arrives through packet `0x0B41`. If the app reports that Item ID is not cached, the current learning attempt has already been stopped: relog with RoAura running, wait until the character is fully loaded, and only then click `Learn` again. If Item ID is still unresolved, send `%APPDATA%\RoAura\roaura.log` for diagnosis.

`Detected item` shows a readable description such as `+7 Word of Misery · Bloody Murderer Card ×3`. Matching uses the exact item fingerprint rather than only the base Item ID, so variants with different cards, refinement, or options are tracked independently. You can enter the familiar client name, for example `+7 Triple Kill Word of Misery`, in the editable `Name` field.

## MVP Timers

1. Choose RTM, SpringRO or ToC and search by MVP name/ID. Supported catalog records include source locations and respawn intervals.
2. The first available location is selected automatically. Click another location card to switch maps, and click the minimap to mark the kill point.
3. Restart replaces the old countdown with a full interval from now. Apply time uses a historical kill time separately. Reset stops the timer.
4. Hover the name, magnifier, time or gaps in the game timer row to show its map/marker; leave the row to close it.
5. F4 opens the compact in-game Add timer form by default; repeat closes it. Dragged form placement persists.
6. Panel X/Y, text size/color, section placement, completion sound and volume remain configurable. F10 toggles visibility without stopping countdowns. With no active timers the panel is hidden except during layout editing.

All 53 supported catalog minimaps are bundled. An unavailable custom map shows an explicit error/retry state instead of substituting another map.

## Loot / Zeny / EXP

This tab displays incoming server events without reading game-client memory:

- loot uses `Jellopy x5`; repeated packets for the same Item ID are merged into one line;
- positive balance changes are aggregated as `Zeny +N`;
- Base EXP uses `XP +N (Y%)`, where the percentage is calculated against the current level requirement; Job EXP is optional.

To capture the initial Zeny balance and EXP requirement, start RoAura before character login or relog once while it is running. If the level requirement has not been received yet, RoAura shows the XP amount without a misleading percentage.

Loot, Zeny and Experience each have an enable flag, text color, group number and order. Matching group numbers share one vertical list; a lower order value appears higher. Different numbers create independent groups with their own X/Y, text size, display duration, aggregation window, gap and maximum line count.

`Aggregation window` controls how long repeated events continue adding to one line. For a large monster pack, try 1000–2000 ms. `Maximum lines` discards the oldest excess rows. Use `Move group` to drag a group and finish with the configured placement key.

Choose the `RTM` or `SpringRO` item database on the Loot / Zeny / EXP tab. Names and types are loaded only from the selected server catalog; the global Item ID 12333 → Black Heart override has been removed. Equip, Card, Loot and Consumables have four independent colors. The same inventory-add packet is used for monster drops and other item gains, so the notification represents inventory additions rather than a guaranteed drop source. Likewise, the Zeny balance packet does not include a transaction reason, so RoAura aggregates every positive balance change.

Aggregation is a sliding quiet interval: each incoming EXP, item or Zeny event renews its group window. A longer quiet gap starts a new burst.

## Presets

Enter a separate name and click `Create new`. The list contains only saved snapshots. Selecting a preset immediately applies the complete snapshot without restarting: cooldowns, effects, reminders, equipment, timers, panel layouts, statistics, Gold Goblin and database settings. `Load` reapplies the selected snapshot. The previous working configuration is preserved in `Recovery`. Transient notifications clear; local histories are stored separately from presets. Files are stored locally at `%APPDATA%\RoAura\Profiles\<preset name>.json`. `Update selected` is the only operation that can replace an existing preset; it requires confirmation and creates a backup first. `Delete` backs up and removes the snapshot without changing the current working settings. Autosave writes only to `settings.json` and never to `Profiles`.

Statistics provides XP/h, Base/Job ETA, Zeny/h, Zeny Total, session time and optional Total B.XP/Total J.XP (initially disabled). Each indicator has group, X/Y, size, color, opacity and enable controls. Refresh ranges from 1–60 seconds, default 5. ETA requires current EXP and the level requirement from the server.

Start, Pause and full Reset use the shared Gold Goblin session operation described above. Reset retains level progress, AID and capture settings; the first Zeny gain after the boundary is counted.

The `Short names` and `READY label` toggles are located inside Cooldowns because they affect cooldown icons only.

## Language, saving and closing

Select Russian or English in the upper-right corner; Russian is the default. `Save` applies settings, `Hide (F8)` keeps background tracking active, while `Exit` and the window close button fully terminate the process. `About` contains version, year, website and author links.

Packet capture state is shown directly below the network settings. `Last action` is fixed above the footer and reports the latest test, learned ID, overlay toggle or save. Settings cards scroll inside their own tab and no longer cover this log.

Settings: `%APPDATA%\RoAura\settings.json`  
Log: `%APPDATA%\RoAura\debug.log`

## Troubleshooting

- No cooldown: click `Detect now`, then verify Skill ID, `On`, Npcap and AID.
- No effect event: learn Status ID again without receiving unrelated new statuses.
- No sound: check event toggle, volume, file path and Windows mixer.
- Missing overlay: press the configured overlay hotkey and verify X/Y after monitor changes.
- Hotkey fails: choose another listed key, keep all assignments different and click `Save`.

## Building from source

Install .NET 8 SDK and run:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-release.ps1
```

Output: `artifacts\RoAura-3.4.0-portable.zip`. Install Inno Setup 6 and run `build-installer.ps1` for Setup.

Links: https://twitch.tv/im_alerion · http://discord.gg/aleriontv · https://www.donationalerts.com/r/im_alerion · https://rtm-refuge.online

### SpringRO Market tracking

When adding a rule, Ignore current offers establishes a silent baseline on the first successful full scan, then alerts on new offers. All history shows item names across the watchlist while preserving the selected rule editor.

Click ★ on an item result or its details, enter a maximum whole-Zeny price or select Any price, then click Add. A semi-transparent star means tracking is off; a dark-blue star means it is on. Click again to stop tracking.

Database → Watchlist lets you add items by name/ID, edit rules and remove watches. Suggestions never replace your typing automatically; select the intended item. Notifications configures text (%name for item name, %price for price), icon, sizes, background/text colors, padding, spacing, X/Y, duration and sound. Use Test notification to preview it on the overlay; F9 controls visibility.

Only successful fresh selling-shop snapshots are evaluated. The first refresh may notify about offers already present. Known offers remain silent after restart or absence/return while their history is retained; a new seller or price can notify again. Multiple new matches for one item use the lowest qualifying price. Download failures do not count as an absence. Offer history is stored locally and shown below the watchlist: time, shop, coordinates and price. General configures retention in whole days, default 30. Once an entry expires, its offer can notify again.

Refresh interval: 1–1440 minutes, default 10. **Refreshing too frequently may result in a server-side block.** There is no hidden ten-minute floor; crawls do not overlap and server-directed back-off remains active.

Pause a watch with Enabled in the third column: its item and price rule are retained. Re-enable it to check the next fresh Market update. The live preview is always at the top; Show icon affects both it and the game overlay. Move in game hides settings and lets you drag a persistent sample; finish with the configured hotkey (Esc by default). Listen auditions the chosen sound at the selected volume; 0% is silent.

## New in 3.2.0

Open Settings using the header button for General, all hotkeys, detected CID aliases/server
assignments and navigation visibility. Overlay Advanced settings can target multiple characters;
no selection means all characters. Start RoAura before map login to observe CID.
ToC provides an offline catalog, MVP data and NPC/manual Gold Goblin valuation; Market is
SpringRO-only. The Home packet switch stops capture and disables dependent sections while
leaving offline Database and manual timers available. Packet reading may conflict with server rules.


## ROQuestie

F6 opens the quest browser; F5 toggles the compact tracker. Catalog entries require fully prepared RU/EN descriptions. Select step chooses the current instruction; checkboxes confirm completion. Coordinate and answer buttons copy text without sending game input. The tracker displays at most five quests. Select a character/local profile to save progress. Quest settings include optional automatic catalog loading and tracker appearance.

## Light and dark appearance

Settings → Appearance independently selects the application theme and the game-window theme (Follow application / Light / Dark). Changes apply immediately. Custom transparent HUD colors, opacity and positions are preserved.


Release builds use `%APPDATA%\RoAura` and `%LOCALAPPDATA%\RoAura`; development builds use separate `RoAura-Dev` folders. Test configuration/history is not automatically migrated into release storage. Close the previous process before updating; packages contain no personal data.
