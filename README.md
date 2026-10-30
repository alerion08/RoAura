# RoAura 3.2.0

Windows x64 overlay for Ragnarok Online. It tracks cooldowns and statuses from incoming RO packets via Npcap and displays configurable overlays.

Gold Goblin calculates the value of the current SpringRO loot session using NPC sell prices, Overcharge, current Vending offers or manual price rules. The calculated value can be included in loot notifications and Zeny-per-hour statistics.

Version 3.0 introduces the redesigned Ragnarok-inspired interface, shared rounded controls, color palettes, categorized navigation and full-settings preset switching without restarting. Ordinary overlays remain visible while editing settings, with only the area beneath RoAura excluded.

Version 3.2.0 adds ToC offline data, header Settings, character-targeted overlays and a global
packet-reading switch. ToC Gold Goblin uses NPC/manual prices; Market remains SpringRO-only.
Published map respawns appear in Database/timer hints. See [release notes](docs/releases/3.2.0.md)
for migration details and the remaining manual ToC acceptance checks.

## Features

- cooldown icons with per-state opacity, size, grouping and placement;
- configurable icon-to-background spacing for every icon overlay, including zero-gap full fill;
- buff/debuff appeared/disappeared notifications with independent visuals, sounds and per-group vertical queues;
- grouped Buff Panel rows, laid out left-to-right with active/inactive states;
- server-duration countdown and remaining-time fill on active buff/debuff icons;
- missing-status Reminder icons with centered, configurable icon/text presentation;
- learned equipment tracking by packet, inventory index, Item ID and wear slot;
- searchable server-specific MVP timers with restart/reset, movable list and dedicated hotkey;
- packet-based loot, Zeny and Base/Job EXP notifications with aggregation, independent colors, numbered groups, movable positions and per-group line limits;
- separate RTM, SpringRO and ToC item databases with Equip/Card/Loot/Consumable classification;
- separate RTM, SpringRO and ToC MVP catalogs;
- named full-settings presets with instant switching for different characters, classes and clients;
- SpringRO item watchlists with price conditions, customizable notifications and persistent local shop history;
- independently configurable XP/hour, time-to-level and Zeny/hour overlay indicators;
- individual and group dragging, with a configurable completion key;
- configurable global settings, overlay and placement hotkeys without Windows key reservation;
- Russian and English interface;
- self-contained single-file Windows build;
- Npcap detection with an official download link.
- automatic server IP/port discovery from the active RO process, with periodic reconnection when the endpoint changes;
- exact equipment variant learning from RTM inventory/pickup packets, including refine, grade, cards and random options.

Npcap is the only external runtime requirement: https://npcap.com/#download

## Build

Install the .NET 8 SDK and run:

```powershell
pwsh -ExecutionPolicy Bypass -File .\build-release.ps1
```

PowerShell 7+ (`pwsh`) is recommended; Windows PowerShell 5.1 also works because the archive is written with `System.IO.Compression.ZipFile`, not `Compress-Archive`. The script does not stop a running `RoAura.exe` unless you pass `-KillRunning`. The version is defined once in `RoAura.csproj` (`<Version>`); the portable archive name, the installer and the About window all derive from it (only `app.manifest` must be updated by hand, and the script checks it).

For an installer, install Inno Setup 6 and run `build-installer.ps1`; it forwards the csproj version to `installer.iss` via `/DMyAppVersion` and prints SHA-256 hashes of the outputs.

Read `GUIDE_RU.md` or `GUIDE_EN.md` for setup instructions.

## Development

Before changing code, read [AGENTS.md](AGENTS.md). It defines scope, branch, testing and release rules for AI-assisted work.

Use the compact project documents instead of reconstructing decisions from chat history:

- [Current project state](docs/PROJECT_STATE.md)
- [UI and overlay specification](docs/UI_SPEC.md)
- [Regression and release checklist](docs/REGRESSION_CHECKLIST.md)

Keep these documents current when implementation, branch status or release blockers change.

## Links

- Twitch: https://twitch.tv/im_alerion
- Discord: http://discord.gg/aleriontv
- Donate: https://www.donationalerts.com/r/im_alerion
- RTM Refuge: https://rtm-refuge.online
- Personal Discord: `alerionvm`
