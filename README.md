# RoAura 2.12.2

Windows x64 overlay for Ragnarok Online. It tracks cooldowns and statuses from incoming RO packets via Npcap and displays configurable overlays.

Version 2.3 introduces a Ragnarok-inspired light blue interface with persistent left navigation, wider settings cards and no separate preview panel.

## Features

- cooldown icons with per-state opacity, size, grouping and placement;
- configurable icon-to-background spacing for every icon overlay, including zero-gap full fill;
- buff/debuff appeared/disappeared notifications with independent visuals, sounds and per-group vertical queues;
- grouped Buff Panel rows, laid out left-to-right with active/inactive states;
- server-duration countdown and remaining-time fill on active buff/debuff icons;
- missing-status Reminder icons with centered, configurable icon/text presentation;
- learned equipment tracking by packet, inventory index, Item ID and wear slot;
- searchable JSON-backed MVP timers with restart/reset, movable list and dedicated hotkey;
- packet-based loot, Zeny and Base/Job EXP notifications with aggregation, independent colors, numbered groups, movable positions and per-group line limits;
- separate RTM and SpringRO item databases with Equip/Card/Loot/Consumable classification;
- separate RTM and SpringRO MVP catalogs;
- named full-settings presets for different servers and clients;
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
powershell -ExecutionPolicy Bypass -File .\build-release.ps1
```

For an installer, install Inno Setup 6 and run `build-installer.ps1`.

Read `GUIDE_RU.md` or `GUIDE_EN.md` for setup instructions.

## Links

- Twitch: https://twitch.tv/im_alerion
- Discord: http://discord.gg/aleriontv
- Donate: https://www.donationalerts.com/r/im_alerion
- RTM Refuge: https://rtm-refuge.online
- Personal Discord: `alerionvm`
