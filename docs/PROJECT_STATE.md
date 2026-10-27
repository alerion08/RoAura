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
- Market refresh follows the configured 1–1440 minute interval (default 10); the former ten-minute service floor is removed. The UI warns about server blocking. In-flight crawls do not overlap, and server-directed back-off remains in effect.
- A single RoAura instance is the default. Developer/test multi-instance behavior is not currently implemented.

## Numeric layout input fix (2026-10-27)

The first attempt changed 44 layout/opacity bindings to `LostFocus` and passed 54 Windows tests, but the user reported the same min/max jumps in their running app. Those tests exercised isolated bindings and did not establish the cause of the remaining runtime report. Do not count the issue as manually verified.

All 44 editors now use `Controls/NumericTextBox`: Text is an unbound draft; only Enter or focus loss writes Value to the model. Source notifications cannot replace a draft. Escape restores the current model value. Existing bounds/settings formats stay unchanged. Empty, invalid and non-finite input retains the valid model value with validation; dot and comma decimals work. Closing the main window commits its focused numeric editor before saving.

Regression coverage includes the actual numeric controls/bindings, source updates during typing, readback at unchanged bounds, and a visible WPF window with Enter, Escape and focus transfer. Local .NET tests remain unavailable (no SDK); use Windows CI for tests and portable packaging. About now displays the short source revision from assembly metadata to distinguish integration builds sharing version 2.14.0. Because only one instance runs, fully close the previous RoAura before launching a new test package; a second launch otherwise activates the old process.

Manual input/persistence checks and the existing game-client blockers remain pending.

## Layout reset report and fix (2026-10-27)

After build `8238353f`, the user reported all overlays at (0,0). Inspection found that `SyncOverlayToGameWindow` called `ClampPositionsToGameArea` on client size changes, overwrote every overlay model coordinate and queued an autosave. This existing path could destroy saved positions on transient small bounds or a switch to a smaller client. The user's settings/logs are not available, so it is not yet proven that this path caused their specific reset.

Removed resize-driven model clamping and autosave. Window size/DPI/client changes now update only the host geometry; saved positions may lie outside a temporarily smaller client and return when the original bounds return. Explicit dragging/coordinate edits and one-time legacy coordinate migration remain unchanged. Regression tests cover every overlay category through small/invalid bounds, client switches, DPI changes, restore and save/reload, plus numeric-coordinate binding initialization/selection changes.

Already overwritten positions cannot be reconstructed from zeros. Recovery candidates are an explicitly saved preset in `%APPDATA%\RoAura\Profiles`, `settings.json.bak` (rolling backup, may already contain the reset), `Recovery/before-ui-2.0.*`, `Recovery/working.*.json`, and `Profiles/Backups`. Preserve a copy of the current RoAura data folder before recovery; do not auto-apply guessed positions or replace unrelated settings. Manual resize/client-switch checks and investigation of the user's actual stored values remain pending.

## Database watchlist (2026-10-27)

Active block: SpringRO Market item watchlist and notification settings. Item search rows and detail headers have synchronized stars (off by default). Adding requires confirming a maximum whole-Zeny price or Any price; removing turns tracking off. Manual name/ID search uses a separate suggestion list that never selects or rewrites the typing draft automatically.

Database settings now contain General, Watchlist and Notifications tabs. Rules and notification appearance/sound persist in settings and named presets. Notifications support `%name`/`%price`, an embedded custom icon or item icon, sizes, text/background colors with alpha, padding, spacing, X/Y and display duration. They share the ordinary overlay host, F9 and client-relative geometry.

Only complete successful live SELL snapshots are evaluated by item ID. The first fresh snapshot can notify about existing qualifying offers. New sellers/variants/prices can notify once per item per refresh (cheapest new match); quantity-only changes and unchanged offers are silent. A successful absence rearms notification for a later return. Failed/partial responses preserve the previous cache and matching state. Notification history is session-only and is not stored as personal market data in settings. Watches are SpringRO-specific.

Added tests cover price boundaries, Any price, large amounts, deduplication, disappearance/reappearance, rule changes, settings/presets, consecutive refreshes, error preservation and autocomplete draft behavior. Local execution is blocked by the missing .NET SDK. The first Windows run compiled the app and passed 70 tests; a malformed HTML fixture in the new crawl test was corrected. Final automated results are attached to the integration commit’s Windows Actions run; manual game checks remain required. No release approval is implied.

## Next recommended step

Test the current PR #5 artifact against `docs/REGRESSION_CHECKLIST.md`, record each result, and fix only confirmed regressions. Do not merge PR #5 or publish 2.14.0 until the release-blocking manual checks pass.
