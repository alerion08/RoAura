# Project state

Last updated: 2026-10-27

## Current product

RoAura is a Windows x64 WPF overlay for Ragnarok Online. It reads incoming game packets through Npcap and provides configurable overlays and supporting tools.

Current prepared version: **2.14.0**  
Stable branch: `main`  
Active redesign branch: `dev/roaura-2-ui`  
Audit integration branch: `integration/2.14.0-audit-fixes`  
Active review: [PR #5](https://github.com/alerion08/RoAura/pull/5)

## Branch status

### main

Stable public code. Do not use it as the source for unfinished RoAura 2 work and do not merge into it without explicit release approval.

### dev/roaura-2-ui

Baseline for the redesigned interface and existing RoAura 2 functionality.

### integration/2.14.0-audit-fixes

Contains the 2.14.0 audit/infrastructure patch applied over the redesign. PR #5 targets `dev/roaura-2-ui` and must remain draft until manual Ragnarok testing is complete.

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

## Known risks and required manual work

The following remain release blockers until verified in Ragnarok:

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
- The effective full Market refresh interval is currently limited to ten minutes; the UI should not imply a shorter effective interval.
- A single RoAura instance is the default. Developer/test multi-instance behavior is not currently implemented.

## Numeric layout input fix (2026-10-27)

The pixel/opacity TextBox bindings in all main-window feature editors now explicitly use `LostFocus`. Cooldown, Reminder and Equipment appearance editors previously pushed every keystroke into clamping model setters: typing `6` for `64` could immediately produce `20` and subsequent digits could hit `180`. Size, padding, spacing, X/Y and opacity inputs now allow the complete value before applying the existing limits. Effects, Buff Panel, MVP Timers, reward notifications and rate statistics were audited and use the same commit policy. Text/color editors and model/settings formats are unchanged.

WPF regression tests exercise the actual XAML bindings with incremental typing, replacement/paste, invalid/empty input, bounds, negative coordinates and fractional values. Local Windows tests/package creation are unavailable in the Linux editing environment (no .NET SDK); use the branch Windows validation workflow. Manual input checks, persistence and the existing game-client blockers remain required.

## Next recommended step

Test the current PR #5 artifact against `docs/REGRESSION_CHECKLIST.md`, record each result, and fix only confirmed regressions. Do not merge PR #5 or publish 2.14.0 until the release-blocking manual checks pass.
