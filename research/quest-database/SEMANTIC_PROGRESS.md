# Semantic database progress — 2026-11-02

Status: **partial research dataset, not ready for application integration**.
Scope: database, sources, parsers, curations, exports and research CI only.
No WPF/application/overlay/settings/release files or `main` changed.

## Verified data scope

The original archive retains 939 NPC scripts, 286 wiki records and 3,420
extracted rows. Active selection retains **216 catalog candidates and 2,750
original rows**; 59 source-confirmed historical event records are excluded
and 10 ambiguous event records are quarantined. One source-confirmed reference
index (`Access Quests`, 3 extracted notes) is excluded from active catalogs. Quarantine is not a claim
that an event is permanently retired. The active catalog still contains
reference pages and unreviewed candidates; 216 is not a count of independently
verified permanent quests.

The normalized `output/quest_semantic.sqlite` uses semantic schema **5** and
generator **semantic-5.4.0**. All four description fields are populated for
**712 steps across 50 curated guides**, up from the previous 46-guide/670-step
checkpoint. **75 conditional branches** also have independent RU/EN short and
full descriptions. Eight quest routes represent two exclusive expedition paths
and six conditional/optional activities. **2,038 steps in 166 remaining catalog entries still lack
complete localization/context analysis.** `complete` remains false.

| Curated guide | Preserved steps | Specific source issue covered |
| --- | ---: | --- |
| Juice Quest | 3 | Showing Meat versus consuming it; conflicting header |
| Factory Quest | 5 | Contextual material delivery; reward level conflict |
| Hugel Memory Quest | 11 | Dialogue sequence and intermediate context |
| Gemstone Exchanging | 1 | Optional exchanges and separate inputs/outputs |
| Dropped Knife Quest | 3 | Quest item and EXP outcomes |
| Prontera Culverts Entrance Quest | 2 | Reward items are not requirements |
| Amatsu Dungeon Entrance Quest | 7 | Nine Tails creature/item homonym; own-character permit |
| Ayothaya Dungeon Entrance Quest | 10 | Two crafting stages; box retained without a second grant |
| Turtle Island Entrance Quest | 2 | Exact answer and fee per journey |
| Abyss Lake Entrance Guide | 4 | Materials omitted by old extractor; timed portal; separate Columns |
| Lost Child Quest | 13 | Two Idle Merchants; target Kid versus pond Kid; conditional prerequisite |
| Rachel Sanctuary Quest | 15 | Server donation target; two EXP installments; exact/wrong answers |
| Biolabs Entrance Quest | 22 | Alternate puzzle relocated from exit prose; player-specific codes; uncertain shape mapping |
| Veins Siblings Quest | 20 | Random material consumption; Steel is not consumed; contextual NPCs |
| Nameless Island Entrance Quest | 20 | House-only death penalty exception; prerequisite gate; EXP installments |
| Thor Volcano Base Quest | 22 | Literal oath; five mutually exclusive item rewards; unknown quiver quantities |
| Thor Volcano Level 2 Access Quest | 6 | Optional iRO/premium shortcut; reusable warp versus one-time quest |
| Curse of Gaebolg | 14 | Three final routes; EXP forfeiture order; exact rhyme; lossless source split |
| Kiel Hyre Quest | 75 | Restored sword puzzle; optional advance cards; retry costs; ambiguous keys; approximate rewards |
| Ice Necklace Quest | 7 | Optional summon and sales; weapon slots are not counts; merchant unlock without boss kill |
| Cursed Spirit Quest | 22 | Ordered triggers; separate Representatives; disguise not consumed; four level reward bands |
| Odin's Temple Excavation Quest | 21 | Exclusive Alex/Laura paths; one material set/box; alternative warp points |
| Onward to the New World Quest | 17 | Separate staff/manager identities; ordered gem deliveries; two EXP installments; Classic warning |
| New Surroundings | 22 | Shared two-post material total; repair action reuse; 20 pies across 12 grants; letter identities |
| Attitude to the New World | 16 | Concurrent quest conflict; item versus monster hunt; extra stage required for Report prerequisite |
| Report from the New World | 13 | Minimum two-of-four prerequisite group; alternative scouts; variant timers/failure losses; distinct volumes |
| Pursuing Rayan Moore | 26 | Unknown timed combat count/duration; two EXP installments; optional wrap-up materials and reward |
| Tripatriate Union's Feud | 21 | One invitation with two signatures; unknown bug count; separate repeatable food recipes |
| Finding a Fairy | 4 | Two EXP installments; source prerequisite for the language chain |
| Ring of the Wise King | 14 | Started Report requirement; exact source/non-iRO language inputs; separate Gem actors; one-hour wait |
| Two Tribes | 25 | Equipped ring spelling; three granted plus three gathered berries; all-held removal warning; Guardian start milestone |
| Guardian of Yggdrasil | 10 | Finished versus started prerequisites; independent tribe choices; exclusive rewards; source map typo and iRO warning |
| Einbroch Murder Quest | 8 | Seven exact password pairs; unknown apple total; four exclusive EXP bands; conflicting factory map |
| President Quest | 38 | Step-22 completion gate; Pacific-time appointments; distinct guards; PIN versus card number; source/iRO search case |
| Rebellion Quest | 16 | Random material set; class-dependent drink prices; unnamed bottle seller; optional food exchange before final delivery |
| How the Airship Works Quest | 6 | International/domestic routes; optional random attack; cleanup timer versus spawn interval |
| Heart Fragment Quest | 9 | Completion gate; potion installments; partial password candidates; random distinct-digit code |
| Airship Ticket Quest | 8 | Undeliverable package returned; exact choices; repair service versus final tickets |
| Medicine Quest | 12 | Proximity triggers; multi-step repetitions; one White Potion; unknown later medicine quantities |
| Friendship Quest | 14 | Exact six-line relay at its delivery step; unknown return message; two EXP installments; overlapping level 75 bands |
| Biological Weapon Quest | 9 | One requested/delivered eleven-material set; unknown medicine split; two exact EXP installments |
| Bruspetti Quest | 18 | Ordered exact topics; interactable Book versus later diary; one three-slot Knife consumed; header/stage level-75 conflict |
| Peace for Arunafeltz Quest | 21 | Distinct same-name merchants; original/forged documents; equivalent choices; unknown split of total EXP; source gem spelling conflict |
| Shadow Quest (Rekenber Job Quest) | 17 | Alternative guard sequences; wrong return answer; work versus quest-log gate; two attack counts; final flashback after reward |
| Lovers Quest | 9 | Two independent level-band payouts; 40 Coal in four batches; player-specific name input; unknown random ore category |
| Juno Remedy Quest | 12 | Five Bingo participants without winning; shared captain identity; contextual scene location; one rune set and header-evidenced final EXP |
| Continental Guard Quest | 8 | Classic availability warning; restored boss tables; accessory choice; skin eligibility outside killing party |
| Nidhoggur's Nest | 8 | Source Geffen entry; collective guardian count; per-visit side rooms; uncertain HP effect; distinct timers |
| Sealed Shrine Quest | 12 | Leader/returner roles; population versus kills; seal timing; optional crafting route |
| Orc Memory Dungeon | 14 | Literal instance maps; ordered braziers; lookalikes versus objectives; conditional spawns and cleanup |

A filled localized row does **not** certify complete quest mechanics or server
availability. Quest review states deliberately remain `source_reviewed_partial`.
The source-derived evidence tier is distinct from human review and gameplay
verification; there are zero server-verified records.

## Implemented in this checkpoint

- Writer conflict handling ignores duplicate keys only; CHECK/NOT NULL errors
  cannot silently discard facts. Curated item/monster operations are checked
  against schema-supported values. Context roles such as monster_population,
  monster_spawn and item_disabled use typed requirements alongside supported
  mention/loot-source/delivery relations. The old Shadow cargo item relation
  now uses mention while its inspect action preserves the exact role.

- Four access/instance guides retain all 42 archived stages and add eleven
  localized branches. Semantic schema remains 5; generator is semantic-5.4.0.
  Literal instance map codes (1@orcs/2@orcs) preserve their floor and spelling,
  with false reference-known status when absent from the sampled scripts.
  Email suffixes and missing separators do not create navigation targets.
- `non_entity_mentions` is a reviewed per-entry surface exclusion, with a
  mandatory reason. It prevents Continental Guard as an organization/quest
  label from becoming a false NPC span, while keeping the actual NPC and
  longer item labels typed. Unknown, unused or conflicting exclusions fail.
- Continental Guard retains its pinned unimplemented/Classic-replacement
  notice. The notice is not a universal archived-event classification or a
  prerequisite imported into this guide. Seven combat tables omitted by the
  old extractor are restored as ordered bilingual source additions. Boss HP
  phases, skill levels and accessory bonuses are not player/item requirements.
  One accessory choice and one EXP payout are distinct; any map occupant may
  collect the skin even outside the killing party. Injured boss ID is unknown.
- Nidhoggur's Nest keeps its source-specific Geffen/Matt Roy Shka entrance
  and None prerequisite header. Twelve guardians are a collective objective;
  three Ryncho/two Phylla are per side-room visit, with no fixed fight total.
  Four room branches are selected per visit. Northeast HP-25%(?) stays
  uncertain. Instance duration, approximate room interval, status duration and
  three-day re-entry cooldown retain separate typed facts.
- Sealed Shrine separates leader-only pendant/fire tasks, role-conditional
  tokens, respawning population (not kill quotas), entry/return/task/instance
  timers and creation cooldown. The source permits joining another eligible
  leader. Seal rotation and independent area-reset timing are explicit. The
  undated center-seal note is not universal server verification. Rewards
  stages 11–12 are an optional crafting route; 990000 Zeny and materials are
  not entry costs. Linked Baphomet Horns and the unlinked Giant Majestic Goat
  output retain their source names; no guaranteed doll drop or output ID is
  invented.
- Orc Memory retains four ordered first-floor targets and three four-brazier
  sequences with context-backed map inheritance. Lookalike Orc Warriors are
  not the unverified-ID Enchanted Orc. Full clears/extra spawns are conditional
  and thresholds unknown. Two-hour duration and repeat cooldown are distinct;
  the latter is restored from the pinned notice. Depraved Orc Hero is not
  silently aliased to the cleanup exception Orc Hero. No payout is invented.


- Three further Schwartzvald/airship story guides preserve all 38 original
  stages and add fourteen localized branches. Shadow's guard test offers
  one immediate-leave response OR a business-then-leave sequence; they are
  alternatives, not a combined list. Sure is explicitly a wrong answer to
  Kazien on return. The retry wait is unknown; the separate 5–10-minute
  missing-Lyozien advice is not a quest cooldown or guaranteed respawn.
- Shadow permits the quest-log entry before the level-70 work gate. Two
  attacks require two and three Thugs (source-linked 1592). Three EXP grants
  sum to 1,400,000; one potion is awarded after cargo inspection. The final
  flashback at stage 17 remains after the stage-16 payout. Kazien/Kaizen
  spellings are preserved without inventing a verified universal alias.
- Lovers has six independent level alternatives at each of stages 4 and 9.
  Header totals summarize two same-band grants, not a third reward or a rule
  that the player's level stays fixed. Satra takes one 40-Coal total in four
  batches of 10. One purchased Tuxedo is handed over once; Flower is requested,
  not explicitly granted. Own-character-name inputs have no fixed copyable
  option, while Klitzer is the exact sender answer. Random Level 4 Weapon Ore
  retains an unknown item/count category rather than an invented named ore.
- Juno Remedy needs five Bingo participants total (four others plus player),
  not victory. Airship Captain shares the Classic airplane 236,163 identity
  already used by Ferlock, retaining this guide's literal display label.
  Stage 8's second map point inherits its map from the same source scene with
  contextual evidence; stage 9 identifies the appearing man as Thierry.
  One five-rune set (7511) produces a remedy of unknown ID/count. Final
  900,000 Base and 600,000 Job EXP are cited once from the reward header.

- Three related permanent story guides preserve all 48 original stages and
  add three localized level-reward branches. Their single mandatory header
  prerequisites link to the already curated Medicine, Friendship and Thor
  Volcano Base guides without inferring executable quest flags.
- Biological Weapon requests one eleven-material set at stage 3 and delivers
  it at 4; header consumed totals summarize that delivery. Red Plant Stem
  Powder and medicine retain NULL IDs/counts and quest-scoped identities.
  The medicine is distributed to Morriphen and Siria without inventing a
  numerical split. Exact 500,000 and 700,000 EXP installments are not
  supplemented by another header reward.
- Bruspetti retains exact ordered topic strings and distinguishes explicit
  typed answers from unspecified topic interactions. Book at stage 10 is an
  interactable, while Bruspetti's Diary is acquired at 15. One Knife [3] is
  removed at 17; the slot notation does not mean three knives, and this page
  supplies no verified item ID. Approximate remaining HP is not a minimum
  requirement. Stage 18 says 75–90 while the header says 76–90: both literal
  sources remain, with an explicit unresolved overlap at level 75.
- Peace's two Foreign Merchants have separate source-coordinate identities;
  High Priest Zhed shares the reviewed identity from the Rachel chain.
  Zhed's personal note, Niren's original File and the returned forged File
  are independently addressable, with explicit per-stage mention bindings.
  Truth/Lies both continue. The 2,500,000 EXP header is a total, while the
  two actual installments have NULL amounts. One Sardonyx is supported by
  its header/link (725); stage-15 Sardonx remains an unverified source spelling,
  not an invented second gem. Rachel Field 3 has no inferred map code.

- Five regional/airship guides preserve all 49 original stages and add six
  localized reward branches. International and domestic routes retain their
  distinct captains; Ferlock is shared between two guides by the same source
  map/coordinates. The airship attack is an optional material source: four
  minutes is its cleanup timer, while spawning is random and may take days.
- Heart Fragment requires How the Airship Works completion at stage 2. Two
  potion grants sum to the header two. Its partial password list retains
  twelve candidates with unknown correctness/mapping; strings differ from
  Einbroch Murder and are never substituted. The five-attempt three-digit
  puzzle uses distinct digits 1–9, with no invented fixed solution.
- Ticket returns an undeliverable package to Kain, rather than delivering to
  absent Defra. Free Item Repair is a service unlock; four tickets are one
  final item grant. Unknown journals, notes and unnamed contacts remain explicit.
  The unverified Journal identity is quest-scoped to avoid merging it with
  Pursuing Rayan Moore's unrelated same-label document.
- `action_reuse` accepts a legacy single reference or a list of distinct
  earlier steps. Medicine stage 8 reuses both 6 and 7; later stages reuse
  their preceding sequence. These are explanatory links, not executable
  progression loops. Duplicate, future or unsupported targets fail closed.
  Proximity is distinct from clicking; later medicine/CPR repetitions do not
  multiply the sole known White Potion cost or invent a flash timer.
- Friendship's six exact Digotz lines move from source step 4 to delivery
  descriptions/options at step 5 using lossless cross-step primary fragments.
  Both original instructions and stable IDs remain unchanged. Maku's return
  answer is not quoted by the source and remains unknown. Two EXP installments
  retain the source overlap at level 75; no bound is silently corrected and
  no header bonus or enforced same-level completion is invented.

- Three connected Schwartzvald guides preserve all 62 original stages and
  add twelve localized branches. `completion_required_at_step` links President
  stage 22 to Einbroch Murder completion. The general prerequisite header
  does not invent an earlier executable completion gate. Rebellion explicitly
  requires all four header quests completed at its own stage 4.
- Einbroch retains seven literal challenge/answer pairs with exact case and
  punctuation. Two different challenges share one answer. The initial 100
  apples purchase is distinct from repeated handovers with unknown total.
  Four level reward bands are exclusive, not cumulative grants. The lab
  scientist and Drunken Old Man are distinct actors despite a shared surface.
- President has two source appointment windows; Pacific Time applies only to
  the source iRO variant. No timezone conversion or DST rule is invented.
  The initial PIN 738495 is distinct from the unspecified number on the card;
  repeated three-minute descriptions are one intrusion window. Main search
  text Shinokas and the explicit iRO lowercase shinokas note are separate
  branches. Disguise items are equipped/retained rather than consumed.
- Rebellion uses one random five-item set. Drinks have source normal/assassin
  prices of 1000/800 each; the 3000 header summary is not another charge,
  and does not prove a bottle price. An owned Marine Sphere Bottle avoids
  buying another. Two Remover are required, with no guessed monster ID.
- The repeatable 100-Handcuff food exchange unlocks at Rebellion stage 15,
  before final document delivery at 16. It has its own optional branch, not
  a main material cost or a guaranteed named food. Source NPC/item/map
  spelling conflicts and unspecified IDs remain review items.

- Four connected New World guides preserve all 53 source stages and add eight
  localized branches. `prerequisite_clauses` adds reviewed header components
  with typed `required_quest`, `required_started_quest` and
  `required_completed_quest` links. Unqualified clauses do not assert a
  specific flag phase. Exact source clauses and complete header coverage are
  validated; a Started requirement cannot be promoted to Finished.
- `stage_required_to_start` links Two Tribes stage 7 to Guardian. This is
  distinct from completing Two Tribes; Report must explicitly be finished
  for Guardian, but only started for Ring of the Wise King.
- Ring language input is losslessly partitioned into source-default and
  explicit non-iRO branches, preserving all four exact strings. The two Gem
  objects have separate identities. Ring 2782 is locally source-linked; the
  differently spelled Two Tribes ring remains unlinked rather than silently
  aliased. Exact final EXP comes from the reward header, with its evidence,
  while the stage prose itself only says some EXP.
- Two Tribes grants three berries and separately collects three. Snorren
  explicitly takes all held berries if more than six are carried; removal
  quantity remains unknown, not capped at six. Bare coordinates inherit a
  map only through explicit preceding-step witnesses.
- Guardian has independent investigation, assistance and final reward choices.
  One completion gives one EXP/coin reward branch, not both. No literal
  password, token ID or instance party size is invented. Its undated iRO
  completion warning and `spl_fld01`/`spl_fild01` discrepancy stay visible;
  server availability remains unknown. NPC spelling conflicts remain separate.

- Additive schema migration 4 → 5 introduces `quest_prerequisite_groups`
  and `quest_prerequisite_members`. Report has one reviewed minimum-two-of-four
  group, not four mandatory prerequisites. Source header order, membership,
  threshold and evidence are validated together; unsupported group changes
  fail without replacing a valid export. Generic prerequisite mentions remain
  distinct from this reviewed threshold. Attitude's separate stage-16 relation
  still governs its eligibility toward Report. No runtime quest flags are inferred.
- Report's four scouts are alternative contacts, with a separate collective
  textual label rather than an arbitrary individual identity. Default/iRO
  search intervals (30/15 minutes) and failure losses (10/5 pages) are four
  localized branches. Ten pages per attempt does not prove success or a
  forty-page total. Four distinct volumes are needed; completed volumes are
  stored to avoid duplicate-in-inventory failures. Page/volume IDs, delivery
  copy counts and probabilities remain unknown.
- Pursuing Rayan Moore has 200,000+1,200,000 base and 10,000+200,000 job EXP
  installments. Its optional wrap-up (20 Potatoes, 20 logs → 5 Sweet Potatoes)
  is a separate route. Dandelion Members' IDs/count and battle duration remain
  unknown. Generic journals and tracking information are not silently assigned
  inventory IDs or exact dialogue answers.
- Tripatriate's final archived row is losslessly partitioned into main
  completion and three optional repeatable food recipes. Recipe choice is per
  exchange, not a permanent exclusion of other recipes. Main food/kit rewards
  are not duplicated from headers. One invitation is presented for two
  signatures; no second invitation or fixed Thief Bug count is invented.
- Schema-1 through schema-4 migration paths preserve existing human data;
  failed threshold migration rolls back to schema 4. Human-reviewed groups
  cannot be silently regenerated.

- Three additional source-pinned New World guides preserve all 55 original
  stages and four localized fields. Promotional Staff and Alliance Manager
  homonyms remain distinct; Diego is shared across two guides with a
  source-qualified identity. Marian/Marien and Rumis/Lumis spellings remain
  unresolved source conflicts rather than invented aliases.
- New Surroundings uses one shared 20-Branch/20-Vine material total for two
  posts. `repeat_actions_from` links step 10 to the repair sequence in step 9;
  per-click consumption stays unknown. Twelve Chocolate Pie grants sum to 20,
  and the reward header does not create an extra grant.
- Attitude records the Camp Guard conflict with New Surroundings separately
  from ordinary prerequisites. Its 1,500,000 EXP stage is distinct from the
  additional Horn of Tendrilion stage (1,000,000 EXP), which is required when
  counting Attitude toward Report from the New World. The extra stage has a
  conditional route and a quest relation with exact stage-16 evidence.
- Two reviewed single-quest prerequisite headers establish mandatory Onward
  relations. Unreviewed/compound headers retain unspecified Boolean semantics;
  Report's reviewed threshold is stored separately in the schema-5 groups above.
- Onward's undated pinned Classic warning remains visible in review metadata.
  Server-neutral inclusion does not turn it into a verified active Classic quest.
  No game/server availability was promoted.

- Additive schema migration 3 → 4 introduces `quest_routes` and `step_routes`
  for alternatives that span several archived steps and independent optional
  activities. Odin source steps 4–7 (Alex) and 8–12 (Laura) belong to one
  exclusive choice group; step 13 onward is shared. Their rune deliveries
  and boxes must not be added together for one completion.
- Ice Necklace's optional Ktullanux summon and postquest Glacial Hearts sales
  are independent activities, not compulsory steps before the main reward.
  Hammer [2] is slot notation for one linked weapon. Merchant unlock depends
  on steps 1–5, not a boss kill; 375 Zeny is a per-item sale rate.
- Cursed Spirit's EXP uses four exclusive level-band branches. Disguise gear
  is worn and retained; Handcuffs remain held until the evidence stage. The
  exact removal stage is unknown despite the consumed-item header.
- `step_locations.location_order` and `condition_text` distinguish a reviewed
  visit sequence from alternative triggers. Cursed Spirit's building entrance
  precedes its interior trigger; Odin's five possible warp points have no
  compulsory order. Older/unreviewed order values remain NULL.
- `selection_decisions.json` records a source-pinned review of Access Quests:
  its own introduction says it is a reference table. This removes one index
  and three notes from both usable catalogs and the worklist, while retaining
  archive rows and HTML. Changed source hashes or missing proof fail closed;
  similarly titled quests are not automatically excluded.
- Tested migrations preserve human descriptions, source additions and location
  rows. Human-reviewed routes are protected from silent regeneration.

- Additive schema migration 2 → 3 introduces `step_source_additions`. These
  records attach literal text from pinned HTML and its Russian translation
  to an existing step. The archive and stable step IDs remain unchanged.
  English full descriptions are validated as original/partition text plus
  ordered additions; fabricated or missing additions fail the build.
- Kiel Hyre step 30's four sword/snake pairings were omitted by the old
  extractor because the solution uses `<pre>` blocks. The full descriptions
  now include the source-backed solution; four item-use/action records retain
  its order and targets. Future HTML extraction also retains these blocks.
- Curse of Gaebolg step 14 is losslessly partitioned into its introduction
  and three route branches. Historian disclosure can forfeit all EXP if Bonnie
  is visited before Biscuss pays; that route's numeric EXP amount is unknown.
  The two routes that explicitly state 1,600,000 keep separate conditional grants.
- Kiel Hyre records one mandatory initial Black Keycard, three optional
  advance cards, door uses and three retry cards after a party wipe. Per-attempt
  monsters and optional material substitutions are not flattened into quest totals.
  The source's approximate 700,000 installment, unknown final EXP and 1,700,000
  header total remain distinct; no exact remainder is invented.
- Kiehl NPC/combat mentions use separate typed spans. Uncertain Kiel identities,
  Old Bronze/Copper Key, Rosimmer/Rosimer names and Ellisia/Allysia ring names
  retain source spellings; key/ring conflicts have explicit review notes. Literal answer whitespace
  is preserved and marked unverified where the source may contain formatting spaces.
- Schema-1 and schema-2 upgrades preserve human descriptions and branch rows;
  failed migrations roll back. Human-reviewed additions are also protected from
  silent replacement during regeneration.

- Additive schema migration 1 → 2 preserves existing IDs, facts and human
  descriptions; failed migration rolls back schema and version changes.
- `step_branches`, `branch_descriptions` and `branch_mentions` retain branch
  conditions, source scope, four localized fields and typed spans. Actions,
  items, monsters, requirements, rewards and dialogues have optional branch IDs.
  A choice group denotes alternatives, not sequential instructions or a
  default selection. Unqualified NPC/location links remain step context.
- `source_fragments` maps immutable source ranges to their semantic target.
  Biolabs original step 22 is partitioned without gaps or overlaps: the exit
  remains step 22, the non-iRO two-digit puzzle belongs to a branch at step 16.
  Example numbers never become universal correct answers. Source order remains
  article order, not executable gameplay flow.
- Future HTML extraction preserves notes after new headings as `section_note`
  records in document order instead of appending them to the preceding step.
  This does not rewrite the original 3,420 archived rows.
- Five additional pinned HTML pages are now covered by curation (Biolabs was
  already captured; four new snapshots were added). iRO/premium category
  evidence for the Thor shortcut does not establish availability elsewhere.

- Normalized entities, locations, actions, items, monsters, requirements,
  rewards, dialogues/options, evidence, review queue and description spans.
- Evidence-based event selection also filters the older research consumer
  projection and core worklist; archived data remain in the separate archive.
- All curated sources are pinned to both complete extracted guide metadata
  and original HTML SHA-256. Changed requirements, reward headers, steps or
  HTML require explicit re-review. Extra/missing steps and duplicate pinned
  step IDs fail the build instead of silently dropping data.
- Exact game names and dialogue choices are preserved in both full locales;
  numeric and entity-presence parity is checked for short and full pairs.
- Same-spelling NPCs have separate explicit curation references. Per-occurrence
  span overrides distinguish a monster from an item and a target from an NPC
  the player should avoid. High Priest Zhed is shared across the Rachel, Nameless Island and Thor
  guides with source-qualified identity; Karyn is shared across the two Veins
  story appearances; different NPCs are never merged just
  because their display names match.
- Source-section facts retain evidence from requirements, rewards or original
  HTML. Abyss Lake's three materials come from the pinned article introduction,
  which the legacy numbered-step extractor omitted.
- Fixed legacy coordinate evidence that silently lowercased `Hugel` to
  `hugel`, inventing map certainty and invalidating the source adjudication
  worklist. Literal spellings are now preserved in both matching paths, and
  offline rebuilds refresh unverified coordinate hints from unchanged source
  prose. All existing source decision gates remain strict.
- Builds use per-quest cached extraction plans and replace semantic SQLite
  only after validation. Failed review gates close SQLite connections before
  retrying, preventing a stale temporary journal from causing an I/O failure.
- Human-reviewed database rows are protected from silent replacement. Export
  corrections to the source-pinned curation ledger before rebuilding.

## Reproduction and checks

Run from the repository root with Python 3 and `requests`, `beautifulsoup4`,
`pyyaml` installed:

```bash
python research/quest-database/semantic.py
(cd research/quest-database && python -m unittest discover -p 'test_*.py' -q)
python research/quest-database/build.py --out research/quest-database/output --enrich-only
```

`semantic.py` needs no network and opens the source archive read-only. Full
offline enrichment regenerates derived research tables and both independent
research projections; it does not update the application's runtime database.
`output/semantic_report.json` is authoritative for current counts and integrity.
`output/semantic_review_queue.json` records unresolved facts with source links.
`output/semantic_examples.json` contains all curated guides and their relations.

Validated locally: **182 tests passed**; complete offline enrichment passed;
the exact SQLite validation script from `quest-research.yml` passed for the
archive and both projections. All integrity checks are `ok`, with zero
foreign-key violations. Original script bytes, guide metadata and original
step text equal the previous committed archive (939 / 286 / 3,420).

The regression suite includes repeated-build byte identity/cache reuse,
unchanged original rows, event selection, context witnesses, original names,
exact answers, source pinning, failed-build preservation, homonyms, source HTML
requirements, per-trip cost, server-wide donations, split reward totals, lossless source
partitions, branch corruption, additive/failed migrations, human-reviewed
branch protection, optional rewards and repeatability/death-penalty scope,
source additions, sword order, Gaebolg route rewards, keycard retry quantities
and NPC/combat homonyms. Schema-1, schema-2 and schema-3 migration paths are tested, including rollback
and preserved human additions/locations. Additional checks cover reference-index
selection pins, route membership, optional activities, level rewards and location
order versus alternative points, shared repair totals, repeated action references,
New World reward accounting, item-versus-kill goals, and evidence-backed
prerequisite/concurrency relations. Corrupted links and unsupported header
prerequisites fail without replacing a valid export. Further cases cover
threshold group corruption and migration rollback, source-default/iRO scouting
variants, distinct volumes, optional wrap-up and postquest food recipes. New
checks cover phase-qualified prerequisites, exact language inputs, distinct
gem actors, all-held berry loss, independent tribe choices and source map
spelling conflicts. Additional checks cover literal password pairs, gambling
retry totals, source appointment windows, stage-specific completion gates,
case-sensitive file search, random requests, drink prices and early exchange
unlock. New checks cover random airship timing, shared captain identity,
partial password candidates, returned packages, multi-target action reuse,
proximity actions, cross-step message provenance and overlapping EXP bands.
Further checks cover the regional prerequisite links, single material-set
accounting, unknown medicine quantities, exact topic input modes, slot counts,
Book/diary distinction, conflicting reward headers, merchant/document identity
bindings, unknown EXP splits and unqualified field coordinates.
Additional checks cover alternative guard dialogue paths, wrong answers,
level-versus-log gates, attack counts, the final trigger after payout,
independent level installments, four-batch accounting, dynamic character names,
Bingo participation, shared captain identity and context-qualified map points.

Local-host reproduction note: file-backed construction of the large legacy
catalog has intermittently returned SQLite read-only/malformed-image errors
in this hosted workspace. Local full validation builds that projection in an
in-memory connection, then uses SQLite backup into the final file before the
normal integrity/FK and exact CI assertions. The source archive and semantic
export use their normal builders. Isolated CI runs the unmodified offline
command above; partial files from failed local attempts are never published.

## Next work, in order

1. Review the remaining 166 catalog candidates, separating reference pages,
   duplicate guides and genuine quests without deleting original source rows.
   Enrich full quest chains, not isolated easy sentences.
2. Continue remaining permanent access/story chains and related New World
   requirements in whole-quest context. Keep early start milestones separate
   from full quest completion.
   Audit structured entity coverage
   of curated guides. Nameless Island now has a curated Curse of Gaebolg
   prerequisite, but no executable or universally applicable prerequisite graph
   is claimed.
3. Audit remaining generic item labels with unknown IDs, including earlier
   File Folder roles, before treating shared names as verified game identity.
   Resolve source spelling conflicts (Boonthon/Boomthon, Dusit/Dustit,
   Phoebe/Phobe, Nema/Nemma, Jackknif e/Jackknife, Laboratory Pass/Permit).
   Biolabs shape-menu/input mapping and the single-digit Yellow Key receipt
   remain review items, not silently invented facts.
4. Normalize optional donation lottery rules separately from guaranteed
   Rachel rewards. Review image permissions and attach sourced route diagrams
   for Ayothaya/Rachel; no images are fabricated or automatically licensed.
5. Review the 10 quarantined event statuses and remaining metadata/variant
   conflicts. Keep source applicability distinct from server verification.

No in-game check was performed. No quest is promised to work on SpringRO or
any other server solely because it appears in wiki/emulator sources.
