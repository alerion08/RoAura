# ROQuestie — Stage 14: unresolved wiki/source transitions

**Goal:** investigate all **40** stage-13 unresolved visit-to-visit
transitions using the pinned source-code NPCs from stage-12 review.
**Mode:** static, source-cited, non-executing research only. Does **not**
produce a working dialogue agent or confirm implementation on any private RO
server.

## Evidence and boundaries

- Stage 12 approved the *identity of implementation files* for 19
  rAthena/Hercules source variants; 6 rejected or conflicting
  source proposals were excluded.
- Stage 13 confirmed **4** strict source write→guard→destination write
  correspondences, two per Hugel Memory variant. Its other **40** wiki
  visit transitions remain unsupported as executable state progress.
- Stage 14 classifies those 40 without automatically promoting any of them:
  **23** no shared written variable, **9** shared write with no proven
  guarded next-state write, **8** revisits to the same NPC.
- **7** further *weaker* literal value→destination guard-read clues appear
  in the gap review: Einbroch Murder (Scientist revisit; rAthena/Hercules),
  Broken Diamond (Jhonnita→Kimeunbang and Rogue Investigator→Investigator;
  both projects), and Continental Guard (Official→Balrog; Hercules).
  In each case, the code contains the matching literal comparison, but
  execution and the branch-to-write connection remain unproven.
- **26** gaps touch some item-check/grant/consume source facts.
  **8** gaps include **20** candidate pairs of the **same literal Item ID**
  checked and consumed inside the same named NPC source block. These include
  Einbroch Murder (factory NPC/item IDs), Unlucky Emerald, Successor to the
  Throne, Snowysnow and archived Halloween source variants. A source match
  does not say that the item is mandatory, which branch is taken, nor that
  it is consumed in a single visit.
- **0** source-item grant→next-NPC check/consume handoff candidates were
  found among these 40 gaps with the narrowly defined literal-ID rule.
  This negative does not prove no item transfer exists; items may come
  from drops, other NPCs, dynamic IDs or scripts outside the matched sample.
- **38** gaps have at least one menu option in source; **18** include
  unresolved menu targets. Stage 13 found 34 unresolved option occurrences
  across the source-supported implementation set.

## Completion evidence review — 19 source variants

The new \`wiki_terminal_reviews\` separately records source \`setquest\`,
\`completequest\` and \`erasequest\` for the NPC blocks actually matched to
each guide, preserving literal Quest IDs, command lines, guards and source
arguments. Results:

- **2** variants (Successor to the Throne, rAthena/Hercules) contain a
  *literal-ID* \`setquest\` + \`completequest\` pair for **Quest ID 10004**.
  This is source-level pairing within named NPCs, **not** proof of a full
  end-to-end quest path, nor that 10004 identifies the whole wiki quest.
- **5** variants contain \`completequest\` in named NPCs but no matched
  literal \`setquest\` ID in the same examined NPC set.
- **1** variant has only \`erasequest\` of these terminal operations;
  \`erasequest\` is not treated as proof of quest completion.
- **11** variants have no explicit \`completequest\` or \`erasequest\`
  in the named NPC set. They may use other source NPCs, legacy variables,
  or alternative completion mechanisms.

Every terminal command's attached guard (when represented in the lexical
source analysis) is included as the **raw expression**, not evaluated.

## Outputs and use by RoAura

- Source report: [\`output/source_transition_gap_review.json\`](output/source_transition_gap_review.json).
  It supplies per-step NPC names, original code lines, raw write values,
  guard expressions, menu options/target labels, item operations and
  possible source-terminal command correlations.
- Research tables: \`wiki_gap_reviews\` (one row per unresolved wiki
  NPC pair) and \`wiki_terminal_reviews\` (one per source-supported variant)
  retain detailed JSON evidence; research SQLite schema **v13**.
- ROQuestie **consumer SQLite remains schema v5**, intentionally no
  in-game routing/control changes. All edges keep
  \`transition_verified=false\`, \`item_requirements_proven=false\`,
  \`safe_for_automatic_navigation=false\`.
- Source code and test suite: \`gap_review.py\`, \`test_gap_review.py\`.
  CI includes tests, complete rebuild and SQLite integrity/foreign-key checks.
  No \`main\` merge or release; no live game validation.

## Next manual verification gates

For each unsupported wiki step, reconstruct *one particular*
source-variant path across all relevant NPC blocks, explicit dialog choices,
condition branches, inventory checks and eventual terminal states. Treat
a source operation as a prerequisite only when the branch semantics
establish it (AND/OR, alternate requirements and optional rewards included).
Identify whether completion is represented by Quest ID, character variable,
dialogue state, instance timer or another NPC. Finally confirm the chosen
server implements that source variant before driving overlays or recommending
automatic answers.
