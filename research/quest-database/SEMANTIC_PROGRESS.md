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
generator **semantic-5.7.0**. All four description fields are populated for
**988 steps across 80 curated guides**, up from the previous 76-guide/974-step
checkpoint. **143 conditional branches** also have independent RU/EN short and
full descriptions. Twenty-one quest routes represent nine exclusive expedition/lyric/material paths
and twelve conditional/optional activities. **1,762 steps in 136 remaining catalog entries still lack
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
| Muff's Loan Quest | 14 | Requested versus consumed gems; incomplete/full material check; restored machine state/color table; literal theory answer |
| Broken Diamond Quest | 13 | Mandatory Muff link; distinct investigators; ordered switches; intermediate Pearl and final two diamonds |
| Z-Gang Quest | 18 | Input-stage passwords; distinct notes/decoders; gambling rule; planned versus actual combat waves |
| Rogue Guild Investigation Quest | 17 | Distinct city agents; two Alcohol installments; one retained Mask; food level versus count |
| Crow of Destiny Quest | 21 | Alternative title/author search; story scenes; album versus displayed photos; torn-note uncertainty |
| Footprint Quest | 7 | Per-attempt bait versus rental fee; nine footprint triggers and exact rescue tile; nine EXP bands |
| Tribe Investigation | 5 | One horn set; contextual return actors; no invented cooldown or giant kills |
| Laphine Craftsman | 2 | Started/access prerequisite; character-bound material; separate entrance/interior; 24-hour repeat |
| Bradium Collection | 2 | Fragments/Refined material conflict; loot source versus kill quota; pinned shared coin ID |
| Collecting Draco Eggs | 3 | One ten-egg total across two floors; no kill objective; both coin rewards |
| Manuk's Monster Suppression | 3 | One Rata and one Duneyrr; unqualified prerequisite; no borrowed daily timer |
| Snowysnow Quest | 11 | Conditional Bard house; unknown blue text/child identities; salt scope; possible gift ranges |
| Piano Keys Quest | 13 | Two exclusive source routes; six keys per path; distinct unnamed tomb triggers; prompt-conditioned lyrics |
| Message Delivery Quest | 5 | Unknown rose count; exact wrong answer; summoning-dependent rewards; source HP uncertainty |
| Juperos Ruins History Quest | 9 | Four distinct plate colors versus one example ID; homonymous books; uncertain per-plate EXP and box variant |
| Finding The Moving Island Quest | 15 | Separate repair/crafting sets; manual ship kills; literal PST windows; optional mastery; three reward outcomes |
| Eye of Hellion Quest | 22 | Actual received code; one reused gem; four tablets; optional cave; already-held-eye exception; source actor uncertainty |
| Geffen Bard Quest | 13 | Optional author discovery; exclusive book/letter paths; nine letter-only EXP bands; uncertain Magnifier consumption |
| Bard Job Change Guide | 4 | Eight alternative flowers; Snowysnow corequisite; live song copying; five optional trunk/instrument offers |
| Bard Skill Quest | 6 | Geffen completion gate at Riott; positional answers; five-egg total and ordered pursuit cycle; unknown item IDs |
| Archer Job Change Guide | 3 | Restored weighted trunk table; mixed 25-point minimum; optional Willow leveling tip; unknown reward counts |
| Archer Skill Quest | 2 | Independent level-30/35 activities; restored headers/unlocks; euRO material/location alternatives |
| Hunter Job Change Guide | 20 | Ten exact personality answers; seven restored random sets; four kills versus six targets; shared timer and preparation context |
| Hunter Skill Quest | 2 | One 5/5/30 material set; two participants; only Arpesto has printed coordinates |
| Dancer Job Change Guide | 29 | Three retained material sets; restored 30-answer quiz; ordered dance/optional shortcuts; one final target and exclusive instrument rewards |
| Dancer Skill Quest | 3 | One taken mirror; separate Aelle material set; uncertain teleport destination; Canell return without a second cost |
| Acolyte Job Change Guide | 2 | Three assigned ascetic options; changed save point; restored Ymir restriction; explicit map links and missing third coordinate |
| Acolyte Skill Quest | 1 | One Holy Light material set; Rosary delivery distinct from helper possession |
| Priest Job Change Guide | 9 | Three visits with Job-50 exemption; optional unequipped Rosary helper; Exorcism versus pass rooms; temptation NPCs and exclusive books |
| Priest Skill Quest | 2 | Restored Resurrection prerequisite; initial/extra stock distinct from one-plus-one random teaching-attempt costs |

A filled localized row does **not** certify complete quest mechanics or server
availability. Quest review states deliberately remain `source_reviewed_partial`.
The source-derived evidence tier is distinct from human review and gameplay
verification; there are zero server-verified records.

## Implemented in this checkpoint

- Four Acolyte/Priest guides preserve all 14 original stages. Coverage is now
  988 stages / 80 guides, 143 localized branches and twenty-one routes. All prior
  974 localized descriptions, 137 branches and original step IDs remain unchanged.
  Existing schema 5 / semantic-5.7.0 supports this data-only iteration.
- Acolyte has one assigned ascetic branch, not three required visits. Field map
  codes come from the guide's own links; Father Rubalkabara has no printed point.
  Talking to the Ascetic changes the save point, so death/Butterfly Wing returns
  there, not to the church/previous point. Travel hazards and escape suggestions
  are not kill quotas or mandatory costs. The omitted Ymir-only-Father-Yosuke
  note is restored as a source-specific restriction, not universal availability.
- Holy Light delivers Rosary 2608, Opal 727 and Crystal Blue 991 once. Cleric is
  not merged with the unnamed Acolyte church Priest or Bishop Paul. Rosary has
  a different role in the optional Priest job-change assistance branch.
- Priest requires all three ascetic visits unless Job Level 50 permits a skip.
  The conditional route is not a free skip below 50, and the next room-entry
  description repeats this context. The referenced Acolyte guide is not a newly
  asserted full-quest completion dependency. Optional assisting Priest holds
  Rosary in inventory, not equipped/consumed or charged to the applicant.
  Repeated Peter S. Alberto clicks are intentional, with no fixed count.
- Exorcism clears all actual Zombie spawns with unknown totals. Later Zombie/
  Mummies rooms need no full clear. Deviruchi, Doppleganger, Dark Lord and Baphomet
  are ordered temptation NPCs, not boss kills. Rejection and Sister Cecelia
  answer labels are absent, so no literal choices are invented. Bible 1551 and
  Book 1550 rewards are exclusive by Job Level, with the job change shared.
- Redemptio keeps initial 30-water/20-gem stock, ten recommended extra gems and
  per-attempt one-water/one-gem consumption distinct. Header consumed summaries
  do not create another upfront fee. Success chance, number of attempts and
  total consumption remain unknown. Resurrection Level 1 is restored from the
  header; skill ID and Sister Linus coordinates are not guessed.

Earlier source-review checkpoints:

- Two Dancer guides preserved all 32 original stages. Coverage was 974
  stages / 76 guides, 137 localized branches and twenty routes. All prior 942
  localized descriptions, 124 branches and original step IDs remain unchanged.
  Existing schema 5 / semantic-5.7.0 supports this data-only iteration.
- Dancer registration has three exclusive source-step routes. The item-retention
  note follows the entire list in the pinned HTML DOM, so all requested materials
  are shown, not consumed, despite its legacy placement after the third set.
  Set 3 records 10000 Zeny possession; the source does not establish currency
  deduction. Do not apply that balance condition to the other two routes.
- The omitted quiz table restores thirty question/answer pairs in three reference
  branches. Match current questions; wording may differ and random assignment is
  not established. Each option cites its own literal HTML question/answer row.
  Elder Willow is an example obvious non-Jazz answer; Comodo Beach is the offered
  answer even though the source names Kokomo beach as the geographical spelling.
  Quiz-only entity references do not become travel, item or combat objectives.
- Dance instructions are ordered tile actions, not map coordinates or menu
  answers. Pose holds position, and Improve Concentration is explicitly optional.
  Source step 19 corrects live Front, Right wording to a single Right move.
  Three long sequences have exclusive full/first-last branches, with later
  source-step-28 context supporting the optional shortcuts. Repeated identical
  source steps 23/24 retain distinct occurrence-based identities.
- One final poring must be killed with a bow, using one of the two listed attack
  skills. Bow/arrows are equipment categories with NULL IDs/counts; the exam
  actor ID is not borrowed from a regular monster database. Line 1953 is the
  Job Level 50 reward; Rope 1950 is the exclusive below-50 reward. No unprinted
  base level, exact map point, equipment fee or gender eligibility is inferred.
- Charming Wink consumes one Crystal Mirror at Canell, then one separate
  five-material set goes to Aelle. The final Canell return has no second cost.
  Later Aelle conversations may send the player to apparently random town
  points, not a mandatory retry objective. Aelle is not aliased to Aile from
  Dancer registration; bare town coordinates do not establish map codes.

Earlier source-review checkpoints:

- Four Archer/Hunter guides preserved all 27 original stages. Coverage was
  942 stages / 74 guides, 124 localized branches and seventeen routes. All prior
  915 localized descriptions, 114 branches and original step IDs remain unchanged.
  This is data curation using the existing schema 5 / semantic-5.7.0 generator.
- Archer Job Change restores the omitted four-row trunk point table from pinned
  HTML. Any mix must total at least 25 points; values are 1/2/3/5 per eligible
  item. Four eligible types are not four compulsory quotas. Willow is a suggested
  loot source, and the 10/8–12/10 leveling tip is not another eligibility gate.
  Bow/Arrows reward IDs follow this guide's links; their quantities remain NULL.
- Archer Skill has two independent optional routes. Arrow Crafting needs Job
  Level 30; Arrow Repel needs 35, recovered from its own omitted header. Each
  unlock and header is restored with pinned additions. Four common crafting
  materials are shared by two server variants: 41 Tough Scalelike Stem (7012)
  or euRO Pointed Scales (906), never both. Roberto's two points are alternative
  locations; Jason's map code remains unknown despite a printed town/point.
- Hunter's ten personality answers are exact choices, not movement/heal tasks;
  question stems are absent. Its omitted table has seven exclusive random
  three-material sets. Branch narratives are authored from the unchanged
  assignment paragraph and the pinned table; each material fact cites its
  literal HTML row. Do not require all sets or independently mix their items.
- The Hunter test requires any four labelled targets, followed by switch and
  exit, within one shared three-minute limit. Six listed targets are population,
  not a kill quota. Regular source-linked species IDs do not verify special
  exam actors, whose IDs remain NULL. Waiting-chat preparation is linked to
  stage 15 with later stage-18 evidence and a literal addition, so it is not an
  instruction to restart after success. Silver Arrows are a 200-item grant;
  Necklace of Wisdom is one received/returned proof. Falcon rental remains
  an optional post-job branch, with an unnamed receptionist kept distinct.
- Hunter Skill requests then accepts one 5/5/30 material set, not a separate
  set per NPC or a doubled total. Arpesto has the only printed point; Reidin
  Corse's coordinate and the skill's numeric ID remain unknown.

Earlier source-review checkpoints:

- Three Bard-related guides preserved all 23 original stages. Coverage was
  915 stages / 70 guides, 114 localized branches and fifteen routes. All prior
  892 localized descriptions, 92 branches and original step IDs remain unchanged.
  Semantic schema stays 5; generator is semantic-5.7.0.
- Geffen separates optional author discovery from two exclusive completion paths.
  The nine level EXP bands belong to the letter path; the book payout stays NULL.
  Hand/back marks are not fabricated inventory items. Exact typed inputs/menu
  answers retain punctuation; the displayed book lyric is not a proven answer.
  Magnifier header consumption conflicts with the possible inventory-drop note,
  so it is a source-cost summary rather than a shared fixed charge.
- Bard Job Change requires one of eight flowers, not all eight. Snowysnow has a
  literal `source_corequisite` relation, not an inferred reversed prerequisite.
  Header mucus/ink costs are summaries, not another hand-in to Lalo. Copy the
  live song: the source prints no static answer. Five optional 60-trunk offers
  have separate item identities; the Job Level 50 Fine Trunk offer uses linked
  Harp ID 1910 and does not stack with the general Lute offer.
- Pang Voice has its Geffen completion gate at Riott's stage 3, permitting the
  earlier drink conversations. Positional choices remain second then first.
  Five eggs is one collection goal, not a kill quota or per-attempt yield. Five
  pursuit points retain the printed cycle with unknown starting point; final
  egg removal, item IDs, price, map codes and skill ID are not invented.
- Alternative routes can use `choice_evidence` plus `source_step` for a literal
  same-guide choice proof. This is distinct from optional `source_evidence`;
  conflicting proofs and wrong route kinds fail without replacing a valid export.
  A single corequisite must exactly match its source header and target title;
  corrupted stored relations also fail validation.

Earlier source-review checkpoints:

- Three access/exploration guides preserved all 46 original stages. Coverage was
  892 stages / 67 guides, with 92 localized branches and twelve routes.
  Earlier 846 localized descriptions, 87 branches and all original step IDs
  remain unchanged. Semantic schema remains 5; generator is semantic-5.6.0.
- Juperos has four distinct Book identities at their printed points. The plate
  category requires four colors but keeps NULL ID; source link 7355 is only a
  separately recorded example, not proof all colors share one ID. Four Dimik
  types are not four kills. Gift Box uses the literal linked variant 666, not
  substituted 644. Header reward summaries are non-additive. Per-plate 100000
  EXP is reported with explicit uncertainty beyond two plates, so a guaranteed
  400000 total or unlimited repeated rewards is not invented.
- Moving Island separates one repair set from one Gusli crafting set; the
  second voyage does not add another repair. Follow the latest live steering
  command and kill all actual spawns: five-minute despawn is not success.
  Possible species/level labels do not define fixed quotas or player gates.
  Ship-only death, EXP and loot rules keep their scope. The first Stranger
  visit has three talks, while optional mastery has a random/unknown count.
  Three performance rewards are exclusive. Gusli is retained and equipped;
  literal PST windows, the dock trigger and conflicting any-time prose stay
  explicit without a UTC/DST conversion or invented unlimited access hours.
- Eye keeps alternate machine codes tied to the actually received value;
  input modes that the prose does not specify remain unspecified. A single
  Blue Gemstone is delivered then used by Welshyun, not charged twice. Four
  named tablets stay distinct, as do their combined Tablet and narrative Jewel.
  Cave steps 19/20 form one optional route proved by the later stage-22 skip
  clause. Higher payout has an already-held-eye exception and does not prove
  this character killed the boss. Final item counts and boss ID remain unknown.
  Shawn is not silently merged with Welshyun; source spellings stay literal.
- Optional routes can now cite `source_step` plus a nonempty literal
  `source_evidence` from the same guide, including later explanatory text.
  This permits a reviewed multistep optional activity without inventing source
  headings. Evidence method is source_optional_activity_context; unsupported
  proof, missing step or alternative-kind misuse fails without replacing a
  valid export. Route membership remains explanatory, not executable gameplay.

- Three permanent regional guides preserve all 29 original stages, including
  repeated identical Piano Keys instructions with distinct occurrence IDs.
  Coverage is 846 stages / 64 guides, with 87 localized branches and ten routes.
  Earlier 817 descriptions and all 84 earlier branches remain intact. Semantic
  schema 5 and generator semantic-5.5.0 are unchanged; source additions stay 13.
- Snowysnow has a conditional Bard Job Change house trigger; it is not a
  universal extra visit. Only Snowysnow? has printed exact answer text; later
  blue choices retain position/color instructions without fabricated labels.
  One Ink/Mucus pair is delivered once. Salt ID/count, inventory implementation
  and removal stay unverified. Charu Charu/Marcell are not guessed into boy/girl
  identities; boy-before-girl order is explicit. Four possible gift ranges are
  possible_item rows, not four guaranteed payouts or invented probabilities.
  Christmas scenery alone does not reclassify this Bard-related quest as retired.
- Piano Part 2a and Part 2b are exclusive source-prompt routes. Each gives five
  keys plus shared Alreg's key, for six; eleven is not the quest requirement.
  Distinct Tombstone references identify two unnamed proximity triggers, not
  named NPCs. Two passes yield two keys in total; talking does nothing. Four
  exact song lines are prompt-conditioned candidates, not an unconditional
  four-input sequence. Header consumption has no proven removal stage. Witch's
  every-talk warp does not make the entire quest repeatable. The original Sign
  Part 1 header reference is not a completed Sign quest or verified unlock.
- Message Delivery keeps Eriuous/Erious as source spellings of one return actor.
  Rose quantity/removal timing are unknown; ten consumed leaves are separate
  from ten awarded leaves. Sorry is a printed wrong option with no invented
  correct alternative. Either final answer is allowed. Two reward branches
  depend on Feylin's summoning result, not a fabricated final response. Failure
  HP loss has no stated amount/percentage or success probability.

- Five New World side guides preserve all 15 original stages and add complete
  RU/EN descriptions and typed facts. Coverage is now 817 stages / 61 guides;
  earlier 802 descriptions/IDs and all 84 branches remain intact. Semantic
  schema stays 5; generator is now semantic-5.5.0.
- Tribe Investigation returns one 30-horn set to Worker and then one Manuk Coin
  at Bazett. Rest wording is not a timer; giants are not invented kill targets.
  Laphine and Draco require Started Two Tribes with Yggdrasil Dungeon access,
  not full completion. Laphine has one 15+15 set and an independent character-
  binding flag. Its literal spl_in code is retained even though absent from
  the sampled NPC-map reference; the entrance precedes the interior NPC.
- Laphine, Bradium and Draco each have a pinned 24-hour repeat notice. Tribe
  Investigation and Monster Suppression have no such notice; no daily timer
  is borrowed. Reset clock/origin is unspecified. Draco has one ten-egg total
  across two floors; egg collection is not a Draco monster kill requirement.
- Bradium requests Bradium Fragments with no ID link but delivers Refined
  Bradium 6090. This unresolved source-label conflict is explicit; two labels
  are not two additive sets. Bradium Golems 2024 are loot sources, not a fixed
  kill quota. Monster Suppression instead requires exactly one Rata 2017 and
  one Duneyrr 2018, then grants ten coins and one EXP payout.
- Explicit cross-guide ID proofs require a donor page/hash pin, exact same
  canonical name/type/ID and matching database URL. Donor HTML is retained as
  the ID evidence source, while the owning guide supplies the item role.
  Bradium's Manuk Coin and Two Tribes' two coin labels now share verified
  catalog identities (6080/6081) with the other Classic guides. Other unknown
  Two Tribes items stay unknown. Stale/missing/mismatched proofs fail closed;
  same spelling alone never establishes a game ID or NPC identity.

- Three permanent story/regional guides preserve all 45 original stages and
  add nine localized Footprint reward branches. No schema/generator change or
  source addition is needed; the original archive and earlier 757 descriptions
  remain intact.
- Rogue Guild Investigation continues the mandatory Z-Gang prerequisite. City
  agents and two source notes have separate identities; the final return shares
  the original guild actor. Five dungeon materials form one requested/delivered
  set. Two Alcohol are delivered separately, one each. Red Herbs is a retracted
  request. Two requested Masks are separate from the actual one retained Mask;
  one random level-4 food and one final Gift Box are not duplicated from headers.
- Crow keeps two alternative exact catalog inputs. Vision characters and the
  crow are not kill objectives; Female Researcher is not silently merged with
  Eva. Two shown photos are distinct from the album grant. The box is at stage
  19 and 900000 Base/Job EXP each at 21. Source stage 20 tears a note, while 21
  still mentions a note: the final referent stays unresolved rather than adding
  a replacement inventory grant. Unknown document and interior map IDs stay NULL.
- Footprint requires full Ayothaya Dungeon Entrance completion at stage 4,
  including its Old Blue Box receipt. That box is not another Footprint reward.
  The 50-Zeny tool rental and one Monster Feed per attempt have different scopes;
  no retry total or repeated rental is guessed. Nine triggers comprise four on
  ayo_dun01 and five on ayo_dun02; listed order is not asserted compulsory.
  The last footprint at 89,262 differs from the exact rescue tile at 91,262.
  Ten random potions belong to the intermediate trigger; final EXP selects one
  of nine level bands. Holy/Holier Threads are retained with unknown quantities.

- Three loan/jewel/gang guides preserve all 45 original stages. Three new
  source additions restore the Muff machine table and put both Z-Gang
  passwords at their actual input steps; total additions are now thirteen.
  Schema 5 and generator semantic-5.4.0 are unchanged.
- Muff distinguishes the ten-Red-Gemstone request from five actually consumed.
  The source does not establish whether ten-held checking also exists, so no
  executable holding threshold is asserted. The incomplete-then-complete
  conversation is not a zero-inventory rule. Four state/color mappings are
  live-condition answers, not a guaranteed fixed button order. The literal
  theory input includes its leading 3. Wallet and Debt Note retain unknown
  IDs/counts; final rewards are not duplicated from the header.
- Broken Diamond has a single mandatory Muff prerequisite. The shared Muff
  actor is source-qualified; guild and Comodo investigators stay distinct.
  Materials and the 10000-Zeny payment each have one delivery/payment plus a
  non-additive summary. Two switches have explicit order. Pearl is awarded
  at stage 3; final rewards are one box and two 3carat Diamonds, with carat
  notation not a quantity. The investigated gem's narrative labels have one
  quest-scoped referent, no verified game ID or asserted universal synonym.
- Z-Gang keeps the exact Marybell password at stage 4 and three ordered,
  separate lines at 14 with pinned HTML evidence. Stage 11's original text
  remains unchanged. Attack and invisible-ink notes are distinct, without
  invented player inventory grants. Moonho and Gooho Ahn are separate actors;
  gambling requires two wins in three rounds, with no invented winning input.
  Only the first wave is sent; the announced second wave is replaced by the
  reveal event. No cloak item, extra kill quota or extra EXP is invented.


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

Validated locally: **276 tests passed**; complete offline enrichment passed;
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

New regression cases cover cross-guide ID pins and URL/name/type mismatches,
failed-build preservation, coin canonicalization, started/access prerequisites,
material conflicts, literal non-reference maps, character binding, repeat
cooldown scope, collection floors and loot-source versus kill quotas.

New regression cases cover the conditional Bard trigger, exact versus
color-only answers, child-label uncertainty, possible gift ranges, shared
material accounting, exclusive key-route totals, duplicate instruction IDs,
proximity triggers, prompt-conditioned lyrics and summoning-dependent payouts.

New regressions cover plate categories/example IDs, book identities, box
variants, uncertain per-turn-in EXP, ship/crafting accounting, despawn scope,
learning/performance outcomes, PST windows, exact inputs, NPC-held gem reuse,
optional cave evidence and failed-build preservation under corrupt proofs.

## Next work, in order

1. Review the remaining 136 catalog candidates, separating reference pages,
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
