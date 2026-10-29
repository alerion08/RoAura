# Character Sessions development

Branch: `feature/character-statistics`, based on the released 3.0.0 main commit.
No merge, tag, installer or public release is authorized.

## Stage gates
1. Isolate development runtime data, logs, IPC and updater. Build and test a DEV portable package.
2. Observe character-select/map-login packets without changing current game statistics.
   Confirm Character ID on SpringRO and RTM; packet IDs and offsets require live validation.
3. Implement per-TCP-flow session identity and transitions Unknown → Connecting → Active → Suspended.
   Do not use PacketPlayerAid as a verified Character ID.
4. Separate per-character Base/Job EXP, Zeny, Gold Goblin, time and persistent sessions.
5. Reuse existing overlay visuals and settings; switch only recorded/displayed statistics.
6. Add RU/EN identity status and safe manual fallback; never switch presets automatically.
7. Verify multiple Character Select cycles, late launch, relog, VPN, corrupt data and full regressions.

## Rules
Only confirmed character sessions may receive character-specific statistics.
No previous-character attribution after disconnect or uncertain identity.
No logging of raw packet payloads, account credentials or authentication tokens.
Keep release settings/presets unmodified; use RoAura-Dev for experimental data.
Zeny balance is baselined afresh at each new login; do not record the baseline as earnings.
Store session and lifetime figures separately; inactive-character time is excluded from rate denominators.
Multi-client support is deferred until flow-to-process association can be verified.


## Stage 2 implementation snapshot (2026-10-28)

- Dev-only BPF captures both directions on the selected game endpoint. Incoming
  server-to-client packets still use the original `RoPacketStreamScanner` and
  `TcpFlowState`. Outgoing bytes are routed ONLY to
  `CharacterLoginObserver`; no existing state or rewards are changed.
- A bounded flow-first-payload probe recognizes standard map-login `0x0072`
  and `0x0436`, including a 23-byte variant. It parses CID at byte offset 6.
  Retransmissions, overlapping frames, short TCP fragments, capture gaps,
  invalid fields and unsupported packet IDs are handled fail-closed.
- Debug log markers:
  `CHARACTER LOGIN CANDIDATE (unconfirmed): opcode=...; cid=...; ...`
  and capped `CHARACTER MAP LOGIN NOT IDENTIFIED: firstOpcode=...`.
  No raw frames, credentials, auth tokens or account ID are written.
- **Crucial:** map login candidate is NOT authenticated/confirmed. Character
  Select transitions, multiple client binding, late startup and variant
  packetver handling still need real in-game verification.
- The probe sees outbound traffic only for the currently configured game
  server address and port. It cannot promise to detect a new map endpoint
  before auto-discovery retargets the Npcap filter; this is an explicit field
  test risk. It does not monitor the separate character-server connection.

## Required hands-on packet checks before stage 3

1. Launch DEV RoAura before logging in; check `%APPDATA%\RoAura-Dev\debug.log`.
2. On SpringRO, login Knight → Character Select → Novice → Character Select →
   Knight (without closing the client). Record only the diagnostic lines and
   whether CID changes as expected. Do not share full packet captures.
3. Repeat with the RTM profile and the user's normal ExitLag/VPN configuration.
4. Launch RoAura after already logging in. This case is permitted to remain
   unidentified; record that instead of assuming a previous character.
5. Compare exact CID/packet IDs across reconnects. Confirm whether any
   observed packets are shuffled or missing due to endpoint changes.
6. Do not advance to active per-character statistic routing until successful
   in-game identification is demonstrated.

## Stage 2 field evidence / Stage 3 shadow-mode implementation (2026-10-28)

- Confirmed in one real Ragnarok client: Character Select without closing the game
  produced CID 154386 → CID 155665 → CID 154386. Each map login had opcode
  `0x0436`, length 23, map port 57143 and a distinct client TCP port.
- Observed duplicate candidate logs earlier: the DEV observer now suppresses
  near-identical diagnostics. No assertion about causes without packet evidence.
- Stage 3 introduces `CharacterSessionManager` and a read-only shadow state
  machine. `Connecting` means map-login CID candidate was seen;
  `ActiveObserved` means server payload on the matching reverse TCP flow.
  This is NOT authenticated character identity and is NEVER used to route XP,
  loot, Gold Goblin or Zeny yet.
- On a new candidate, the previous flow becomes suspended. FIN/RST from an old
  flow cannot suspend the newer flow. Capture retarget/disconnect suspends the
  current flow; late startup leaves identity Unknown. No persistent statistics.
- DEV log prefix: `CHARACTER SESSION SHADOW:`. No auth data or IP addresses.
- Still awaiting RTM, VPN/ExitLag and late-start field tests. Only one
  connection may be considered current in the initial design; multi-client
  requires process-level attribution before statistics integration.

## Stage 4 — isolated per-character storage foundation (2026-10-28)

The SpringRO live test established shadow state transitions:
`154386 Connecting → ActiveObserved → Suspended`,
`155665 Connecting → ActiveObserved → Suspended`,
`154386 Connecting → ActiveObserved`. Same map-server port and different
client TCP ports. That verifies the observed Character Select cycle but does
not establish a separate authentication acknowledgement.

The following components are added only in the DEV branch:
- `CharacterStatisticsDocument`: versioned individual and per-session EXP,
  Job EXP, trusted Zeny gains, loot counts and elapsed active time. Item pricing
  remains independent from counts, and settings/presets are NOT serialized here.
- `CharacterStatisticsStore`: per-server/CID JSON in
  `%LOCALAPPDATA%/RoAura-Dev/CharacterStatistics/` when built with the DEV flag;
  atomic replacement with `.bak`, validation, and refusal to overwrite damaged
  or newer-version files. Corrupt primary can be read from backup without
  silently deleting the damaged file.
- `CharacterStatisticsLedger`: separate in-memory entries per server+CID.
  Switch/pause/flush checkpoint elapsed time; switching resets Zeny's *initial*
  balance; session reset retains lifetime totals and other characters.
  No per-packet disk writes. UI rate denominator uses active-character time.
- Synthetic tests verify A→B→A, baseline Zeny, reset, corruption, serialization,
  profile isolation and rejection of rewards while inactive.

**Integration remains intentionally disabled.** The stage-4 ledger is not fed
by Npcap packets and does not change existing Gold Goblin, reward notifications
or on-screen rate indicators. Before enabling this in stage 5, validate reliable
packet-to-flow association (current packet events do not include their TCP flow),
confirm character acceptance, and test reconnection/late-start separately.

## Stage 5 experimental integration (2026-10-28)

- Packet events from the inbound RO scanner now additionally carry the
  corresponding TCP flow and immutable character session revision in DEV.
  Old queued messages are dropped if the character changes or capture restarts.
  Standard 3.0.0 packet handlers still run in normal builds.
- DEV App subscribes only to identity-bound EXP / job EXP, Zeny, progress
  and item events; status/skill/equipment/market handling is left unchanged.
  Only a matching currently ActiveObserved flow can update the ledger.
- Overlay rates and session time are sourced from the active character ledger,
  with inactive-character time excluded; the existing overlay layouts and
  settings are not changed. Rates are zeroed while identity is unknown.
- Gold Goblin rows (including manual quantity/price edits) are separately
  snapshotted per character; common price rules remain shared. Drop history
  records only packet-observed loot, independent of manually edited rows.
- The existing "reset statistics session" action resets only the current
  character and retains lifetime data.
- Stats checkpoints are saved upon character switch, capture suspension,
  periodic flush (30 s) and application exit. A persistence error fails
  closed and is logged, never silently assigning gains to another character.
- This still needs manual field regression for switching, repeated incoming
  packets, market prices, normal buffs/equipment, late start and RTM.
  ActiveObserved is network evidence, not cryptographic proof of login.

## Stage 6 — character browser and one shared session clock (2026-10-28)

User accepted Stage 5 on SpringRO, with two corrections:
- Gold Goblin and the overlay read the same per-character timer. A session
  starts on the FIRST positive Base/Job EXP packet, or when the user presses
  Gold Goblin "Start Session". Neither loot nor Zeny starts the timer.
- The session-time overlay updates on its own WPF tick, irrespective of
  "Refresh every N seconds" for rates; Gold Goblin time reads the same
  ledger. Time for a character is paused during other characters and relogs.
- Manual "Start Session" resets current character's session metrics and Gold
  Goblin rows together, but retains lifetime history. "Reset statistics"
  clears the active session and leaves the timer at zero until next EXP.
- Existing DEV files without SessionTimerStarted are loaded compatibly.
  Earlier recorded active time is preserved rather than silently deleted.
- Stage 6 UI adds active server/CID and a manual, READ-ONLY selector of saved
  character histories, in Russian/English. This selector NEVER changes
  packet ownership, presets or overlays; unsupported login or late-start
  packet attribution still needs a separately verified safe manual binding.
- Last stage remains full regression: SpringRO/RTM, rapid Character Select,
  restart, missing Npcap, VPN/ExitLag and all existing overlay features.
