# Cooldowns / Effects / Reminders — common packet regression audit

Status: **fixes under test in DEV; user has NOT verified them in-game**.
User confirmed all three features fail **also in stable v3.0.0**, so this is
NOT caused by Character Sessions. Keep stable `main` unchanged.

## Source findings

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
