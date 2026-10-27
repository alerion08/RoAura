# Regression checklist

Use the smallest relevant section for normal changes. Complete all **Release blockers** before merging a release candidate. Record failures with the build/commit, Windows version, display mode, DPI, RO executable and whether ExitLag/VPN was active.

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
