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
