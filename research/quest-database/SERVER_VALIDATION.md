# ROQuestie — server-neutral quest verification protocol

**Primary scope:** build a comprehensive, cited catalog of the main quests
shared across the Ragnarok Online ecosystem (Classic / Pre-Renewal / Renewal),
with explicit optional differences for individual servers. This research is not
limited to one server. No quest is *guaranteed* to be present on every server.

## Evidence and trust levels

- `wiki_documented`: documented by a named wiki guide, with URL, source
  content hash and independently recorded refresh status.
- `emulator_source_derived`: exact rAthena/Hercules script file, repository
  revision/blob SHA, original NPC script line, coordinates and literal state.
- `candidate_link`: heuristic alignment of guide with source NPC script(s).
  Similar names, coordinates and scores are clues, not proof.
- `server_authoritative`: evidence that a particular server and revision
  enables a known quest implementation, preferably authoritative manifests
  or published NPC scripts.
- `server_gameplay_observed`: reproducible interaction/quest-state evidence
  on a named server, with version, NPC map/cell, character state and date.

Never automatically upgrade wiki/emulator evidence to server-authoritative.

## Main research order

1. Permanent story chains and multi-stage quests.
2. Dungeon access and region unlock quests.
3. Job advancement, class-related skills and rebirth quests.
4. Repeatable or regional quests where a useful walkthrough exists.
5. Historical/seasonal events and archived iRO-specific activities.

These are **research priorities**, not claims of server availability. The
`roquestie_core_worklist.json` provides a complete, server-neutral queue;
`critical_quest_triage.json` retains detailed explanations for severe
coverage gaps. Even an old event remains in the source archive.

## Verification record for a concrete server variant

For a quest/server/revision pair, capture: canonical quest key, source
revision, NPC map/coordinates, start conditions, sequential and branching
steps, item requirements and rewards, Quest ID or quest journal absence,
dialogue options, completion checkpoints, provenance and review date.
Store different implementations separately, including Pre-Renewal and
Renewal; server-specific changes are optional overlays, never silent
replacements of the shared quest catalog.

Not every classic quest has a journal Quest ID. Do not invent one. The
`quest_id_candidates` view only establishes that a literal ID appears in
the **candidate** emulator script, not that it belongs uniquely to the guide
or is active on a server.

## Runtime and safety requirements

- The catalog is read-only and contains no account/user state; per-character
  progress is separate and survives catalog updates.
- The consumer must not auto-select a dialogue option based on an ambiguous
  candidate. Show unverified steps as references with provenance.
- Do not bypass server protections or assume packet identifiers without
  verified evidence.
- A fully automatic Questie guide for an individual server/revision requires
  trustworthy NPC positions, branches, prerequisites, progress and completion
  signals. Missing evidence means a read-only walkthrough, not a claimed
  completed or available quest.
