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
