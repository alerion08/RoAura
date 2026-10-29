# Cooldowns / Effects / Reminders — common packet regression audit

Status: **fixes under test in DEV; user has NOT verified them in-game**.
User confirmed all three features fail **also in stable v3.0.0**, so this is
NOT caused by Character Sessions. Keep stable `main` unchanged.

## Earlier findings (subsequent corrections below)

1. The `0x0983` status packet was decoded with `Left` at byte 9 and
   `Total` at byte 13. rAthena's `packet_status_change` contains `Total`
   at +9 then `Left` at +13. `0x043F` has only `Left` at +9. Duration,
   expiry and notifications were therefore unreliable.
2. Cooldown skill auto-learning listened only for a `0x07FB` skill ACK.
   Standard RO servers also send `0x011A` / `0x09CB` non-damaging skill
   usage, `0x0114` / `0x01DE` damaging usage, `0x013E` cast ACK and
   `0x043D` post-delay. The DEV parser recognizes these known layouts;
   the training handler also accepts the skill ID in post-delay events.
3. The first status that arrived during learning could be assigned to the
   configured effect/reminder, including a status on another player or mob.
   On DEV the character-select/map-login handshake supplies the player's
   account/actor AID *in memory only*, and learning now restricts source
   packets to that AID and an active observed session.
4. When the first status packet was used for learning, the effect ID was
   recorded only after the ordinary handler had ignored it. The typed packet
   is now reprocessed after learning for the newly tracked status.
5. On finite effect expiry the overlay cleared the countdown but never
   emitted the "disappeared" transition. DEV now notifies the App when the
   finite duration ends, allowing reminders and disappearance notifications
   to transition. Explicit server removal still takes precedence.

## Security / compatibility

- No unredacted raw packet dumps, account identifiers, credentials or auth
  tokens are written to diagnostic output.
- User has expressly deferred release: **no main merge, tag, installer or
  GitHub Release**.
- Both SpringRO and RTM may use packetver-specific layouts, shuffled opcodes
  or custom status behavior. Synthetic tests cannot validate those live
  protocols; any unconfirmed variant must be investigated with safe,
  metadata-only diagnostics rather than guessed.

## Manual acceptance in a DEV build

1. Launch before character login, choose SpringRO and let the auto identity
   reach `ActiveObserved`. Verify EXP / Zeny / Gold Goblin still function.
2. Set a cooldown to "Learn Skill ID", cast its selected active skill,
   confirm nonzero ID and that the icon counts down after recasts.
   Test both a damaging and a self-buff skill.
3. Learn a finite effect. Confirm the tracked Status ID corresponds to the
   intended buff (not a nearby actor), its Remaining/Total duration is
   plausible and notification appears. Wait for expiry: disappearance
   notification and Reminder "missing" must appear.
4. Learn the same effect as a Reminder. Confirm it hides while active and
   appears again on expiration/removal. Test reapplication and repeats.
5. Character Select A → B → A, then repeat both tracked effects and
   skill cooldowns. Verify AID reinitializes with the selected flow.
6. Repeat on RTM. If a packet is not detected, record relevant
   `debug.log` metadata around the attempted cast or status toggle,
   excluding raw capture and sensitive data.

Relevant rAthena sources:
- https://github.com/rathena/rathena/blob/master/src/map/packets_struct.hpp
- https://github.com/rathena/rathena/wiki/packets

## Follow-up: SpringRO Hiding and missing status events

In-game DEV #59: Envenom, Back Slide and successful Steal trigger cooldowns; Hiding never triggers. Equipment still works. All ordinary effects/reminders remain unresponsive.

- Hiding is a toggle skill (TF_HIDING = 51), not necessarily accompanied by a successful-skill packet or any server cooldown. rAthena's `ZC_STATE_CHANGE` actor-option packets `0x0119` (13 bytes) and `0x0229` (15 bytes) expose `OPTION_HIDE = 0x2`. DEV converts a transition to hidden into a confirmed successful Hiding action, optionally starting the *user-configured* cooldown. It does not invent a server cooldown duration.
- The same packets convey the actor's hidden flag. DEV maps it to the client status icon `EFST_HIDING = 4` on appearance and disappearance, with unknown duration (0), so Effects and Reminders can react when configured for this Status ID. This is **not** the same number as `SC_HIDING = 24`.
- All decoded status and option packets generate bounded metadata-only diagnostic lines `PACKET STATUS OBSERVED` / `PACKET OPTION OBSERVED` with opcode, state, optional duration and `matchesPlayer` boolean, never authentication codes, raw frame bytes or account IDs. This lets us distinguish no packet vs. rejected actor vs. UI transition. If neither appears, SpringRO likely uses a different packet/layout and further source-specific verification is needed.
- Legacy equipment, skill effects, EXP, Gold Goblin and per-character statistics must continue functioning without changes.

Sources: https://github.com/rathena/rathena/wiki/packets ; https://github.com/brAthena/Client-Side/blob/master/Lua%20Files/2014/luafiles514/lua%20files/stateicon/efstids.lub

## Evidence from uploaded SpringRO debug.log — 2026-10-28 18:34–18:35

- Npcap observed 12 adapters with `host 127.0.0.1 and tcp port 57143`.
- The first outbound map-server opcode was repeatedly `0x0317`, not
  one of the observer's recognized `0x0436` / `0x0072` login packets.
  There is no `CHARACTER LOGIN CANDIDATE` or `ActiveObserved` session.
  This is an **unidentified connection**, not proof that the game uses
  a particular new handshake or that all traffic is unreadable.
- A packet was actually decoded: `0x0983`, status `11287`,
  active, total and remaining `180000` ms, `matchesPlayer=False`.
  The old diagnostic computed that boolean against the **map-login AID
  only**, so it is also false when that identity is unavailable. It
  did not establish that the status belonged to another actor.
- DEV's effect-learning handler required a known CID-backed
  `ActiveObserved` session. On this trace, it would discard every
  status event, regardless of whether the packet was really for the
  player's actor.

### Scoped correction

- Add explicit self-actor identity signal `ZC_AID` (`0x0283`, 6 bytes)
  and reuse the actor ID of own-character `0x0ACC` EXP packets. These
  are keyed to the TCP map flow in a separate `PlayerAidObserver`, reset
  on retarget and disconnect, fail closed on conflicting AIDs.
- DEV Effects/Reminders/skill learning can accept **verified self AID**
  independent of identifying the CID. An unknown AID never falls back
  to the first random matching status.
- The existing per-character ledger still requires CID/flow evidence.
  Missing CID must never assign EXP/Zeny/Gold Goblin to a guessed
  character.
- Diagnostic output now distinguishes `identityKnown`,
  `identitySource` and `matchesPlayer`. A newly recognized self-AID
  logs `PLAYER SELF AID VERIFIED` without writing the AID value.
- The test should run with RoAura DEV started before entering a
  character. If late-attached, the user can trigger an EXP event to
  give a second server-derived self-AID signal. If neither source
  appears, the actor remains deliberately unknown and a further
  protocol-specific investigation is required.

RTM Refuge reports no equivalent packet issue during this trial;
its overlay placement discrepancy is deferred by user request.

## Comprehensive follow-up — 2026-10-28

### Confirmed defects corrected

1. **Hiding non-damage ACK was rejected.** The field called `level` in the
   wire struct is the heal/amount parameter. rAthena's Hiding implementation
   sends `-1`, which is 65535 / 4294967295 on the wire. A 1–100 validation
   excluded Hiding, zero-amount skills and large heals. Successful results
   now pass; unsuccessful non-damage results do not trigger or teach a skill.
2. **Option karma was treated as a Boolean.** `clif_changeoption_target`
   assigns `sd->status.karma` to `isPKModeON`. Its name does not justify
   rejecting values above 1. Nonzero actor and complete-frame checks remain.
   Earlier logs saying `pk-mode-not-boolean` are not proof of a malformed
   SpringRO packet. The body/health mask correction remains in place.
3. **One Hiding bit was shared by all actors.** Another actor could suppress
   our activation or make a repeated packet look like a new activation.
   A bounded per-actor tracker now drives the Skill 51 bridge. Status 4
   remains an independent EFST state; no cooldown or buff duration is invented.
4. **The 64-KiB carry rescanned consumed bytes.** Only incomplete data is
   retained now. Known fixed/variable frames are consumed whole; supported
   chat/list bodies are opaque, even when fragmented. Coalesced 0x0283 can be
   read at a known boundary instead of only at a TCP-segment start. Existing
   equipment variants, loot, EXP, Zeny and status frames have mixed-stream
   fragmentation regression coverage.
5. **Missing standard layouts and wrong actor offsets.** 0x08FF/0x0984
   status-on-enter frames place AID before EFST; 0x0983/0x043F place EFST first.
   Both families now have separate decoders. 0x0117 and 0x0B1A skill layouts
   are recognized, and ground casts may have zero target AID. The original
   cooldown-on-cast-ACK behavior is retained: a cast ACK alone is not claimed
   to prove completion of an interruptible cast.
6. **UI dispatch lost source provenance.** Each event now carries an
   in-memory TCP lifetime token, original receipt time and same-flow self
   evidence. Other-flow and stale/closed-source events cannot update or teach
   the current overlays. Self identity remains separate from CID/statistics.
   A bounded two-minute pending status buffer can replay the latest own
   status after explicit identity arrives, without extending its deadline or
   resurrecting a status already removed. Old generations cannot register
   a new live source; scanner creation is serialized across Npcap adapters.
7. **Reminder expiry depended on an enabled effect row.** A shared status
   controller handles both collections and emits appearance/disappearance
   once per configured effect, retaining repeat protection. Standalone
   reminders expire; refresh moves the deadline; explicit removal wins;
   unknown duration stays active until removal. Learning replay is idempotent,
   and a row created by a wizard sees the cached state and original deadline.

### Detection versus attribution

Decoding is independent of AID. During learning, an observed Skill/Status ID
with unknown or mismatching self identity is shown with an explanation in
RU/EN, instead of being silently presented as "nothing detected". Automatic
assignment of actor-scoped events still requires same-flow self evidence in
DEV. The client-only 0x043D post-delay packet has no AID and no longer depends
on that check. Configured cooldowns retain their prior fallback when identity
is unavailable; a matching cast does not establish self identity for statuses
or a guessed character for statistics.

This does **not** establish what the historical status candidates 11287,
17519, 17578 or 15832 meant. No new raw SpringRO capture was supplied in this
iteration. Unknown/custom packet families still use validated scanning, and
neither encryption nor a specific shuffled protocol is assumed. The changes
cover reproducible standard-protocol and application defects; SpringRO live
verification remains necessary.

### Focused acceptance for the next DEV portable

- Start DEV before login. Learn/recast Envenom, Back Slide, Steal (success
  and failure) and Hiding. Toggle Hiding on/off repeatedly, including near
  another player doing the same. Verify Skill ID 51 and EFST 4 separately.
- Learn a finite buff as an Effect and as a Reminder. Check appearance,
  refresh, remaining/total, explicit removal and expiry. Repeat with only a
  Reminder configured, and with the matching Effect disabled/deleted.
- Create rows through both wizards after the first learning packet; wait
  before completing the wizard and check that the original deadline remains.
- Launch late without AID: an observation must be distinguished from a
  confirmed own event. Gain EXP within two minutes and check buffered own
  status restoration, with no other actor's status applied. Retry learning
  if its 20-second window expired; self-only post-delay needs no AID.
- Hide/show with F9 during a countdown; verify reminders, separate sounds,
  notification groups, saved positions and modifier keys. Test A → B → A,
  connection retarget, multiple adapters and restart with named presets.
- Recheck equipment/variants, EXP/Zeny/Gold Goblin and RTM. Do not use a
  successful synthetic test as a substitute for this game-client check.

### Verification and primary sources

68 focused scenarios pass in the local headless test harness; application
and test sources cross-compile for Windows with .NET 8. Full WPF tests and
isolated DEV packaging run in the branch workflow. No main/version/release
changes are included.

Source snapshot inspected: rAthena `d4b8e7b8f16061cc2496d377ac8f777ce72a4f39`:
- [Hiding implementation](https://github.com/rathena/rathena/blob/d4b8e7b8f16061cc2496d377ac8f777ce72a4f39/src/map/skills/thief/hiding.cpp)
- [clif_skill_nodamage, clif_changeoption_target and status delivery](https://github.com/rathena/rathena/blob/d4b8e7b8f16061cc2496d377ac8f777ce72a4f39/src/map/clif.cpp)
- [Wire structures](https://github.com/rathena/rathena/blob/d4b8e7b8f16061cc2496d377ac8f777ce72a4f39/src/map/packets_struct.hpp)

## Failed field acceptance after DEV 92 — diagnostic follow-up

`debug(3).log`, latest session 20:30–20:34 (user local clock):
- 0x0983 reports differing status numbers, including 17594, 9034, 13222,
  13097, 2687 and 8121 for 240000 ms, all mismatching self. This is evidence
  against claiming successful status learning, not proof of any encryption.
- 0x0229 matches self but its Boolean Hiding interpretation alternates.
  The log omitted the mask, body/health fields and karma; the true state
  cannot be reconstructed. EXP eventually agrees with map-login self AID.
- Effect notification logs are not a state trace: repeat protection can
  suppress a notification even though the model changes. A stale opposite
  notification is now removed independently of whether a new alert is emitted.

DEV scalar probes run before semantic filtering, including status IDs above
20000 that previous logs silently omitted. Routing and validation remain
unchanged. `boundary=True` means the scanner expected a boundary; it is not
proof of a complete SpringRO protocol profile. `alternate` is a diagnostic
actor-shaped field at the other standard layout offset (or a skill target),
never identity evidence. Window-local aN/fN labels allow equality checks
without persisting actual actor IDs, IP addresses or payload dumps. Each
window is capped at 120 seconds / 256 candidates, and Learn rearms it even
if initial startup diagnostics were exhausted. No private sample is committed.

Required next sample: start Learn, toggle Hiding on for 5–10 seconds then off,
repeat twice, then start Learn again and apply the same finite buff twice.
Record the real on/off order/time. Supply the new log; the new binary has
`packetLayoutDiagnostics=1` in its START line. Do not infer state from odd/even
cast count or accept a mismatching actor to make learning appear successful.

## 2026-10-28 21:07 field diagnostics — actual layout probes

Read-only analysis of the user's `debug.1.log` excerpt from a DEV run. The earlier script incorrectly reported no diagnostic build because it searched only the current `debug.log` after the 5-MB rotation; `PACKET LAYOUT` entries were present in `debug.1.log`. This is **field evidence**, not an accepted fix or a new decoder. The currently running executable may differ from the historical process that produced this rotated segment.

- Same TCP lifetime (`flow=f1`) and verified own actor (`self=a1`), with continuous scanner offsets:
  - 21:07:33: `0x0229` @12357 (actor=self, `hideBit=True`) → `0x0983` @12372 (status candidate 11561, actor=a2, alternate=a3; neither self, duration 240000/240000 ms) → `0x09CB` @12401 (Skill 51, actor=self, result=1, amount 4294967295).
  - 21:07:39: `0x0196` @12471 (status candidate 41769; neither actor interpretation self) → `0x0229` @12480 (actor=self, `hideBit=False`) → `0x09CB` @12495 (Skill 51, actor=self, result=1).
  - 21:07:47: `0x0229` @12599 (actor=self, **`hideBit=False`**) → `0x0983` @12614 (status candidate 31582, neither actor interpretation self, 240000/240000 ms) → `0x09CB` @12643 (Skill 51, actor=self, result=1).
  - 21:07:50: `0x0196` @12695 (status candidate 5975, actor not self) → `0x0229` @12704 (actor=self, `hideBit=False`) → `0x09CB` @12719 (Skill 51, actor=self, result=1).
  - 21:07:53: `0x0983` @12811 (status candidate 33575, duration 180000/180000 ms; neither actor interpretation self) → `0x09CB` @12840 (Skill 138, actor=self, result=1, amount=3).

**Result:** the actor-first +2/+6 hypothesis in the separate Claude audit is **not confirmed and is contradicted for these observed 0x0983 candidates**: `alternate` is never `self`. Merely swapping the actor/status field offsets is not a justified fix. Conversely, the repeated contiguous `0x0983/0x0196` + `0x0229` + successful own Skill 51 sequences are strong *temporal evidence* of a Hiding transition-related packet family. The exact ON/OFF assignment is provisional until matched to the user's action timestamps. High/variable option masks (e.g. 0xAC7F545F, 0x4FEF1CAD, 0x04CF6E3C) and the contradictory `hideBit=False` at 21:07:47 show the current bit-2 option interpretation cannot yet be trusted for SpringRO. `boundary=True` only means the scanner predicted a boundary; it is not independent protocol proof.

**Next safe investigation:** confirm real Hiding ON/OFF timestamps for these samples; examine verified-self Skill 51 and its immediately preceding 0x0983-vs-0x0196 shapes, along with 0x0229 scalars. Prefer metadata-only, flow-local correlation diagnostics before changing status decoding or automatically assigning IDs. Do not assume custom encryption, invent a Status ID, or treat a timed candidate as an own buff without corroboration. Preserve RTM, equipment, statistics and release behavior.

## 2026-10-28 22:48 field test: Hiding and Enchant Poison

The user confirmed that the 180000-ms activity is **Enchant Poison**, not an unidentified generic buff. Their `RoAura-SpringRO-diagnostic-v2.txt` (sanitized rotated logs) includes verified-self AID from EXP, `flow=f1`, and contiguous scanner offsets:

- 22:48:44–22:49:14: **seven** repetitions of `0x0983 (240000 ms) → 0x09CB Skill 51` and **seven** repetitions of `0x0196 → 0x0229 → 0x09CB Skill 51`. Every skill acknowledgment belongs to verified self, but `hideBit` is inconsistent, including `hideBit=True` on some apparent exits and `False` on some apparent entries. All candidate-status actor offsets (+2 and +4) fail own-AID matching.
- 22:49:21–22:49:43: **eight** repetitions of `0x0983 (180000 ms) → 0x09CB Skill 138` (Enchant Poison) with the same issues: variable candidate Status IDs and nonmatching actor interpretations, despite verified own Skill 138 and a plausible duration.
- The earlier 21:07 field capture showed the same signatures. A direct +2/+4 field swap is not supported by observations.

**Interpretation:** Skill IDs 51 and 138 are reliably identified in these samples, while their status attribution/EFST remains unknown. This does **not** prove all candidate status boundaries are authoritative; `boundary=True` is the scanner's current estimate. Do not auto-assign a random Status ID, equate Skill ID with EFST, or treat observed 180000/240000 ms as a definitive server-provided timer without full layout validation.

**DEV-only scoped instrumentation:** `SpringRoStatusSkillCorrelationDiagnostics` watches contiguous `0x0983/0x0196 → [0x0229] → 0x09CB` for confirmed own Skill 51/138, records 16-bit candidate *stability by offset* across repeated casts and checks whether the full verified 32-bit own AID appears at any offset in the candidate status frame. It logs no raw payload, personal AID, unverified numeric fields, or IP address. This is strictly diagnostic and is gated by DEV + SpringRO profile. RTM and released code remain unchanged. Actual decoding/learning and Hiding state behavior **are not yet fixed**; obtain Windows tests and a new live SpringRO trace of `SPRINGRO STATUS-SKILL PAIR` before implementing an evidence-based decoder.

## 2026-10-28 23:18 field test v3: repeated Hiding/Enchant Poison pairs and masking hypothesis

The user's `RoAura-SpringRO-diagnostic-v3.txt` contains **20 live metadata-only status/skill correlations** from one identified flow: 8 Hiding start candidates (`0x0983, 240000 ms → own Skill 51`), 8 Hiding end candidates (`0x0196 → own 0x0229 → own Skill 51`), and 4 Enchant Poison starts (`0x0983, 180000 ms → own Skill 138`). All reported `selfAidU32Offsets=none`. Candidate statuses at +2 are variable; the diagnostic stability comparison shows U16 at **+6 and +7 stable** within all three repetition series. The status +17..+27 tail is also stable in timed starts, consistent with largely unused/padding fields. No real EFST or stable actor ownership has been proven. The report also has **256 boundary=False initial guesses in ~3 seconds**, many implausible, before self identity is established; exclude these from any learning logic, and investigate scanner framing separately.

**New narrowly scoped hypothesis:** if status(+2) and actor-low16(+4) are scrambled by the same per-packet XOR mask, then `statusWord XOR actorLow16 XOR verifiedOwnAidLow16` should be stable for repeated known skills. A similar addition-derived value is a control hypothesis. The DEV-only SpringRO correlation logger now records *only* `actorHigh16MatchesSelf`, persistent high-half match across a series, and the 3+-sample stability (with a cautiously named candidate value when stable) of XOR/add-derived status. No packet bytes, full AID, addresses, or raw candidate actor numbers are logged. Neither derived candidate is used for auto-learning or gameplay; synthetic tests demonstrate the intended signal and a negative control, but a Windows .NET test run and a new live trace are still required. Even a stable candidate would require corroboration by Hiding ON/OFF, Enchant Poison, multiple flows and known status mappings before accepting a decoder.

## 2026-10-29 01:13 SpringRO v3(1) field results and Hiding-only DEV correction

Fourth metadata-only capture: 19 confirmed own-skill adjacent pairs, 4 Hiding starts, 4 Hiding ends, 11 Enchant Poison starts. Every paired 0x0983/0x0196 status candidate has `actorHigh16MatchesSelf=True`; none contains full verified AID. After 3+ repetitions, both shared-mask hypotheses `xorDerivedStatus` and `addDerivedStatus` are **unstable** for both skills and Hiding ends. Do not implement either mask. `0x0983` 180000ms tracks Enchant Poison/Skill 138, 240000ms tracks Hiding/Skill 51; the Status ID and low actor fields still cannot be decoded.

In Hiding sequences, actual `0x0229 hideBit` sometimes contradicts both `0x0983 → own successful Skill 51` and `0x0196 → own 0x0229 → own successful Skill 51`. This explains intermittent `PACKET EFFECT TRANSITION status=4` even when a full self-skill pair was observed.

**Scoped experimental correction (DEV + SpringRO profile only):** `SpringRoCorrelatedHidingTracker` now accepts state transitions only from a contiguous frame sequence on one verified-self flow, with correct 0x0983/0x0196 state byte, stable own AID high-half, 240000ms on start, and own successful Skill 51 ack. It emits the already-standard `EFST_HIDING=4` synthetic status, no invented timer or decoded Status ID. In this mode raw `0x0119/0x0229 OPTION_HIDE` never mutates Hiding state or creates fake Skill 51 usage; option packet observations remain as metadata diagnostics. Other server profiles and stable builds retain their prior behavior. Tests cover repeated ON/OFF, deliberately wrong option bit, foreign actor, missing identity, bad duration/state, stream reset, and Effects/Reminders synchronized state. This fixes *only Hiding state transitions* pending real Windows validation; **Enchant Poison / arbitrary Status ID learning are not fixed**. Preserve the distinction between successful skill use and proven EFST identity.

## 2026-10-29 01:35 SpringRO live acceptance — Hiding packet state (DEV 9724305e)

User's *sanitized* `RoAura-SpringRO-diagnostic-v4.txt` (185 lines; no raw packet capture) confirms the SpringRO-only correlated-Hiding implementation is functioning at the packet-to-effect transition boundary. Over 01:35:38–01:36:10 it logs **7 verified own Hiding ON** and **7 verified own Hiding OFF**, on the same scanner flow, each followed by:
- `PACKET STATUS OBSERVED status=4; matchesPlayer=True`, and
- `PACKET EFFECT TRANSITION status=4; active=True/False` matching the verified pair in sequence.

Example: at 01:35:44.752, `0x0196 → own 0x0229 → own successful Skill 51` resulted in `SPRINGRO HIDING VERIFIED: active=False` and `PACKET EFFECT TRANSITION status=4; active=False` **despite** the misleading `0x0229 hideBit=True`. Similar contradictory option bits occur on other transitions without corrupting the timeline. This is live confirmation of the **Hiding protocol-to-model fix** (not yet independent confirmation of visual UI notifications, overlay layering, relog, or other server profiles).

The same v4 trace contains **7 Enchant Poison** `0x0983 (180000ms) → own Skill 138` pairs. Existing decoder still logs `PACKET STATUS NOT APPLIED ... reason=not-own-actor`; actual EFST remains unknown. `xorDerivedStatus=unstable` and `addDerivedStatus=unstable` in all 3+-sample series; do not claim either transformation valid or assign a fabricated Status ID. `actorHigh16MatchesSelf=True` for paired packet family suggests a partially recognized actor, but the true low 16 bits and Status ID cannot yet be recovered. Offsets +6 and +7 in the stability report overlap: +7 includes the state byte +8 and is **not an independent 16-bit actor field**. False-boundary initial scans continue and must not contribute to learning.

**Release gate:** ask the user only whether the actual Hiding effect/reminder was visually correct. Do not mark the overall SpringRO Status ID learning work complete; an additional generalized fallback should be represented honestly as *Skill ID activation tracking*, distinct from true EFST decoding, with explicit UX/semantics and early-expiry limitations. No gameplay parser changes are justified by the current v4 sanitized evidence.

### User confirmation — Hiding overlay

The user confirmed after the seven Hiding ON/OFF cycles in the v4 DEV log that **the on-screen Hiding overlay displayed correctly**. This complements the 14 matching `PACKET EFFECT TRANSITION status=4` events and resolves the outstanding visual-acceptance question for this exact test. Do not request another identical Hiding ON/OFF test without a new regression. This is not proof of relog, expired/early-cleared effects outside the tested sequence, or all reminder permutations.

Enchantment/other buff learning remains a **separate open problem**; there is still no verified EFST decoding of the observed SpringRO `0x0983` payload for Enchant Poison.


## Lossless evidence gate for general Status ID decoding (2026-10-29)

The v4 report itself was read, not just the project summary. Its 7 Enchant
Poison pairs and confirmed Hiding transitions agree with the recorded findings.
The missing information is precise: bytes +2..+5 of each candidate status frame
and their relationship to the verified own actor are not preserved together.
The repeated `unstable` output is a verdict on two formulas, not data from which
a new formula can be tested. Standard rAthena/OpenKore layouts alone cannot
explain these observed values. Do not spend another live test on one more
hypothetical mask. A client replay might provide a second view, but no SpringRO
replay was supplied and it is not established whether its data would be decoded.

### One-time opted-in capture and offline replay

1. Close the running DEV instance, start the new portable from PowerShell:
   `.\RoAura.exe --springro-status-samples`. The flag is deliberately not a
   saved setting and does nothing for stable builds or another server profile.
2. With SpringRO selected and the character logged in, start **Learn Status ID**
   for Enchant Poison, then apply it 3–5 times. No repeat Hiding test is required.
   Learning may still fail: this is evidence collection, not an EFST fix.
3. The normal `%APPDATA%\RoAura-Dev\debug.log` (and rotated `debug.1.log`, etc.)
   contains `SPRINGRO WIRE SAMPLE { ... }`. These lines contain the exact selected
   status/option/skill bytes and own actor number. They contain no login, names,
   chat, IP address or full network stream. Do not use the old sanitized v4
   exporter: its whitelist may omit the new records. Supply the sample lines
   or logs privately, never in a public issue/commit.
4. Developer replay command:
   `python tools/inspect_springro_status_samples.py debug.log debug.1.log`.
   The inspector rejects incomplete, foreign, failed or inconsistent chains;
   a zero-sample input fails rather than claiming diagnostic success. It does
   not discover a decoder. Exact bytes permit the same future decoder to be
   tested repeatedly offline without another build per arithmetic hypothesis.

Collection is limited to the active observation source, contiguous expected
lengths and known boundary estimates, nonzero frozen self identity, matching
status actor high half, correct state byte, successful own self-targeted
Skill 51/138 and an ACK within two seconds. The high-half check is a sample
selection filter, not proof of true status ownership. Window cap: 16 pairs /
120 seconds, pending sources: 8. Capture resets with connection settings;
closed/replaced source tokens cannot finish an old pair. Raw data is retained
only in explicit capture mode and is written through the existing nonblocking
log queue. It never feeds learning, Hiding, effects, reminders or statistics.

Acceptance for any future decoder: one hypothesis must recover consistent
EFST and full actor identity on the original bytes across multiple known
statuses and both start/end packets, with a negative control; only then add
an anonymized structural regression fixture. Do not check in a private sample.

## 2026-10-29 — raw samples and static client analysis

The opted-in capture succeeded: 16 Enchant Poison status/Skill 138 pairs.
Across the 29-byte 0x0983 frames only offsets +2..+5 vary; all 17-byte
acknowledgments are identical. The full own actor ID is absent from the status
frames. This is not evidence for another field swap or a particular cipher.
Private samples and the client executable remain outside git.

Read-only analysis of the supplied PE32 client (SHA-256
`591e3776e40ac3661a1c60576a145e34e69a61423a890fda292c955d237f91c2`,
preferred image base `0x00400000`) located actual packet dispatch, rather than
guessing handlers from opcode constants. Addresses below are preferred VAs,
not file offsets or guaranteed live addresses.

| Site | Verified static behavior |
| --- | --- |
| `0x00C9E29C` / table `0x00CAA2E0` | Dispatch index is signed packet opcode minus `0x73`. |
| 0x0983 case `0x00CA8ACE` | Calls `0x00CF8810`. |
| `0x00CF8810` | Reads signed status word +2, actor DWORD +4, state byte +8, DWORDs +9/+13/+17/+21/+25; calls `0x00CE9A60`. |
| 0x0196 case `0x00CA634C` | Calls `0x00D039B0`, which reads actor +4, status +2 and state +8. |
| 0x09CB case `0x00CA3A16` | Reads skill +2, amount +4, actors +8/+12 and result +16. |
| `0x00C14530` | Calls receive pointer stored at `0x015C5A18`, then appends returned bytes through `0x00C16640`. |
| `0x00C147D0` | Frames packets using the packet-length lookup, then consumes buffered bytes through `0x00C16570`. |
| `0x00C16640` / `0x00C16570` | Append/consume use the imported `memcpy` thunk `0x00E07D4B`; no status-field transformation is present in these inspected routines. |
| `0x00C14B10` | Resolves `recv` with `LoadLibraryA("ws2_32.dll")` and `GetProcAddress`, with an IAT fallback. |

The visible status handlers expect the standard layout already used by RoAura.
This independently contradicts fixing the observed values by swapping fields.
It does **not** explain the wire-to-handler transformation: the actual runtime
receive target, loaded modules and possible runtime patches are unknown.
WinLicense/Themida sections and strings exist, but do not establish that those
components transform status packets. Raw address matches in `.winlice` around
`0x01D9DE23` / `0x01DE46D3` belong to dense address tables, not demonstrated
calls to the receive/status handlers. Do not treat arbitrary linear disassembly
of these data tables as a decoder.

Next evidence needed is the receive implementation actually loaded by this
installation, starting with whether a local `ws2_32.dll` exists beside the game
EXE. A directory/module inventory can distinguish that from the system DLL;
the EXE alone cannot. No additional same-buff capture or speculative DEV build
is justified. No decoder or general Status ID fix was implemented in this
analysis. Preserve the accepted correlated Hiding path.

### Supplied DLLs — follow-up

The installation screenshot has no local `ws2_32.dll` among the displayed
files, but does include `gepard.dll`, `spring.dll` and `dinput.dll`. All three
were supplied and inspected statically, without executing or modifying them.
Absence of a Gepard import in the EXE did not establish absence of Gepard from
the installation. Runtime loading remains unverified.

- `dinput.dll`: PE32, export `DirectInputCreateA`, internal name `ROExt.dll`.
  Inspected startup reads ROExt configuration via `GetPrivateProfileIntA`;
  imports cover input/window/DirectDraw functions. No direct Winsock imports.
  This is not evidence that it decodes status packets.
- `spring.dll`: PE32, exports `compress` and `gawbfxkiab`, internal export
  name `Fury.dll`. Embedded source path identifies GRF Editor client encryption
  and `Fury/zlib.cpp`. The exported path contains a byte-permutation/XOR loop
  at preferred VA `0x10004720` followed by a call to `0x10004D90`, whose visible
  branches handle compressed input (including a `0x78` header). No direct
  Winsock imports. Do not transplant this resource-processing transformation
  into the status parser: no network connection has been established.
- `gepard.dll`: identifies itself as Gepard Shield 3.0; includes Winsock `send`
  import and messages about incoming unknown packet IDs, previous packet IDs,
  init acknowledgments and packet logs. References to those network diagnostics
  occur in `.vmp0` (e.g. `0x10D34FFD` references the incoming-ID message and
  `0x1094A58E` references the corrupted-init-ack message). `.vmp0/.vmp1/.vmp2`
  and the fragmented inspected code prevent treating ordinary linear
  disassembly as an identified receive decoder. This makes Gepard relevant
  to the investigation, but does **not** prove it transforms bytes +2..+5.

SHA-256 fingerprints, for reproducibility without redistributing binaries:

| File | SHA-256 |
| --- | --- |
| dinput.dll | `3d8d3f03f96cd859f587afe6bcb7beb7915b1a35044a58be324c779c1e42e878` |
| spring.dll | `44f17591c39bd9b0281357b744ca942c99d032e5549809528fc3cb0f7d208b79` |
| gepard.dll | `7dd1805651dd556a68e28e220841dcc94b9750b9dfcd0de9c0d556d04c474b01` |

No executable decoder has been recovered or validated on the 16 samples.
The unresolved task is establishing the actual runtime receive path and its
field transformation, or obtaining that protocol information from the server
maintainer. Another identical gameplay trace does not resolve this gap. No
runtime patch, injection, protection change, speculative parser edit or DEV
build was made. Hiding remains unchanged.

### Next experiment: client-side comparison without modifying the client

The visible client dispatch calls `0x00C147D0` with a global packet buffer at
preferred VA `0x015E8198` (RVA `0x011E8198`) and subsequently passes that
buffer to the 0x0983/0x0196 handlers. `tools/Capture-SpringRoClientStatus.ps1`
polls *only* this 29-byte buffer through Windows `ReadProcessMemory` for up to
45 seconds by default. It checks the executable SHA-256, accounts for image relocation,
logs only 0x0983/0x0196 frames locally, and aborts on access denial. It does
not request debug privileges, attach a debugger, inject, patch or execute
game code. It may miss short-lived frames; Gepard may deny access. Treat a
zero-event run as inconclusive. A successful run provides the client-side
packet values needed to test the wire-to-handler hypothesis, not automatically
a reusable decoder.

First Windows run at 13:53 local time read the buffer 5,695 times over 90
seconds (~63 reads/second) and saw zero 0x0983/0x0196 frames. Access was
granted; the PowerShell `Start-Sleep -Milliseconds 1` loop effectively sampled
only once per ~16 ms. This negative result says nothing about the bytes at
dispatch time and must not be mistaken for absence of the status packet. The
probe now performs the bounded read loop in compiled C# code, reports read
count during collection, and checks a second read before saving a candidate.
A further zero-result run at higher frequency would still be inconclusive if
the client overwrites its global buffer too quickly. Do not keep repeating
that experiment without a different method.

Run one SpringRO client and the existing DEV RoAura with
`--springro-status-samples`; start Learn Status ID. In a separate Windows
PowerShell window from the repository root, execute
`powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Capture-SpringRoClientStatus.ps1`
and apply Enchant Poison three times while it is running. Collect the local
`%LOCALAPPDATA%\RoAura-Dev\Diagnostics\SpringRoClientStatus-*.jsonl` and the
matching RoAura DEV debug log privately. Do not publish either: both contain
actor-related values. Repeat for a second distinct status and its removal
only if the first comparison demonstrates readable, matching frames. The
prior 16 samples remain useful controls, but cannot substitute for same-run
client/wire alignment. Do not attempt to bypass an access denial.

### Client buffer observation, 2026-10-29 14:00 local

The user's second, compiled-loop run captured seven complete client-buffer
0x0983 frames over ~5 seconds. All seven have status ID **6**, actor
`0x001E95FA` (same independently verified self AID as earlier wire samples),
state 1 and total/remaining 180000 ms. The [client EFST table](https://github.com/scriptord3/Ragnarok-Client-Scripts/blob/master/data/luafiles514/lua%20files/stateicon/efstids.lua)
associates 6 with Enchant Poison. These are actual decoded values at the client's packet
dispatch buffer, not a guess based on duration. Earlier 16 Npcap wire frames
for Enchant Poison had different values in bytes +2..+5 every time while the
other 25 bytes matched. Together these observations establish a difference
between the network and client-dispatch views, but they come from different
runs, so no exact per-frame wire/decrypted pair or reusable transform exists
yet. The single preceding client-buffer 0x0196 frame has status ID 1689 and
the same actor; it must not be treated as Enchant Poison removal.

The next required artifact is the RoAura DEV `debug.log` (and rotated file if
needed) **from that same ~14:00 run**. The user need not cast again. Align
`SPRINGRO WIRE SAMPLE` timestamps and packet tails with the seven client
events, then examine how long the decoded frame remains readable and whether
short, bounded client-memory observation could safely feed the existing
status event pipeline. A continuous high-frequency read loop has not been
validated for application use. Do not publish the user's client-buffer JSONL
or the raw RoAura log.

### Same-run wire/client alignment

The supplied `debug(6).log` contains seven wire samples at 14:00:17–22.
`tools/compare_springro_status_views.py` matches all seven against the
client-buffer JSONL by opcode, complete bytes from offset +6 onward, unique
timestamp within 50 ms and packet length. Client observations follow Npcap
timestamps by 0.969–7.286 ms. In every pair, wire bytes +2..+5 differ and
the client has `06 00 FA 95`, status 6 and own actor `0x001E95FA` (the high
two actor bytes at +6..+7 remain `1E 00` in both views). The wire +2..+5
values are `65C8F8A8`, `FD065EB3`, `E6EA10F2`, `F7950080`,
`D95D07CF`, `446E49D0`, `64DFBF3C`. The remaining 25 bytes match.

This establishes a per-frame transformation of those four bytes before the
client dispatches `0x0983`. It explains why the raw parser rejects the
status as belonging to another actor. Seven distinct inputs with the same
output do not identify a general decoder or its changing state. Do not
hardcode status 6 from duration, skill ID or these samples; that would fail
other statuses and removals. The preceding client `0x0196` status 1689
has no corresponding captured wire sample and is still unclassified.

Next implementation decision requires either identifying the actual receive
transformation and its state, or validating bounded client-buffer observation
for a second distinct status, removal, relog and multiple clients. The
current probe is a diagnostic only: continuous high-frequency cross-process
reads have not been measured for CPU, frame retention or compatibility.

### Second status and removal, 14:15 local

The next client-buffer sample contains three `0x0983` starts with status 5,
own actor `0x001E95FA`, state 1 and both durations 9999 ms, and five
`0x0196` removals of status 5 for that same actor. Each of the eight events
has a unique same-opcode Npcap `PACKET LAYOUT` observation 2.425–5.255 ms
earlier. Npcap's parsed IDs vary across starts (10880, 51022, 50681) and
removals (40343, 54632, 37175, 2305, 844), and actor checks fail. These
are observational event matches, not exact byte matches: the existing
`SPRINGRO WIRE SAMPLE` logger emits only status/skill-138 pairs, whereas
this run contains skill 135. The client capture itself independently
establishes status 5 activation and removal; it does not establish that all
other effects or relog/multiple-client scenarios work.

The comparison tool now reports both exact wire matches and timestamp-only
layout matches, clearly distinguished. No further repeated cast of these
two skills is needed. Engineering work should address generic access to the
decoded receive path and its lifetime, not infer a per-skill status mapping.

### Experimental DEV status bridge

`--springro-client-status` enables read-only, bounded observation after each
SpringRO status packet. It requires one SpringRO.exe with the exact analyzed
SHA-256, opens `PROCESS_VM_READ | PROCESS_QUERY_LIMITED_INFORMATION`, polls
for at most 15 ms and compares a stable double-read to the wire packet's
opcode and bytes +6 onward. Only the independently verified own AID can
produce a status event. The existing `StatusChanged` pipeline then drives
learning, effects and reminders. The raw transformed `0x0983`/`0x0196`
events are suppressed in this opt-in mode; unrelated packet paths remain.
No client process is modified. Multiple simultaneous clients are declined.

This candidate must be run on Windows with the actual game to measure hit
rate, timing and CPU, including removal and reconnect. A 15 ms timeout is
an empirical starting bound, not proof that every status frame can be read.
Run the DEV build with `--springro-client-status` and inspect `SPRINGRO CLIENT
STATUS matched` lines in the private DEV debug log. Synthetic tests cannot
establish gameplay correctness.
