# RoAura Quest research database

Isolated **research branch**. No changes to the RoAura WPF application, settings, updater, main, release files or database runtime.

## Current ROQuestie scope — shared quests across Ragnarok Online

**ROQuestie is server-neutral.** The main objective is a source-backed, nearly
complete catalog of the fundamental Ragnarok Online quests that can later
power a full walkthrough assistant in RoAura. We prioritize permanent
storylines, dungeon access, class changes/skills, repeatable regional quests
and explicit prerequisite chains. Old iRO events remain in the source archive
without displacing core research.

**Universal catalog does not mean universal availability:** official and
private servers can have different quest implementations, locations and IDs.
Classic, Pre-Renewal, Renewal and emulator variants are retained separately;
a server-specific compatibility overlay is optional and must be evidence-based.
Never silently treat a script from one source as active on every server.

Stage 8 also produces `roquestie_core_worklist.json`: a full catalogue of
source pages with provisional research category, useful main-quest research
priority, gaps/known source scopes and unknown server availability. Historical
seasonal events are still indexed. See `INTEGRATION_CONTRACT.md` and the
generic `SERVER_VALIDATION.md` for future optional compatibility checks.

## Long-term deliverable / completion contract

**The objective is a maximally comprehensive, sourced, compact, versioned Ragnarok Online quest dataset that can be integrated into RoAura as a full quest assistant with minimal application-specific transformation.** This research repository develops the *data product and its contract*, not the WPF UI or packet capture implementation.

Acceptance direction: per server/variant, stable quest IDs and steps, NPC map/coordinates and dialogue branches, prerequisite checks, item/kill requirements, variables and quest state transitions, consequences/rewards, traceable evidence, coverage flags, ambiguity review queue, and a read-only local runtime export with explicit schema version. Source-derived data, parser-derived candidates and in-game-verified facts must never be conflated. Do not silently promote emulator sources to verified iRO/ToC behavior.

Future RoAura integration must not require joining raw parser tables or parsing NPC script code at runtime. Preserve the richer research SQLite and ship an independent, stable, smaller runtime database when the contract has been validated.
## Stage 7 — quality, reproducibility and manual review queue

**Verified stage 7 build:** [GitHub Actions #40926491951](https://github.com/alerion08/RoAura/actions/runs/40926491951) SUCCESS; focused regression tests [#40926483207](https://github.com/alerion08/RoAura/actions/runs/40926483207) SUCCESS. Research SQLite v6 and runtime catalog v2 both passed integrity and foreign-key checks. This snapshot retains 939 script files, 286 wiki records, 3,420 guide steps, 574 quest-script candidates covering 169 pages and 1,298 step-to-NPC candidate links. `quest_review_queue.json` identifies 117 wiki pages without any script candidate, 32 with mixed gameplay variants, and 84 critical/105 high/97 medium review items. All 286 wiki-page freshness records remain historical/unknown, because this run was deliberately **offline-enrichment only**; this must not be reported as a fresh iRO crawl.



This stage addresses a real data-quality regression observed in the extended 939-script crawl: a partial wiki category response discovered fewer pages, and a transient wiki page fetch failed even though prior guide steps were retained. A passing SQLite integrity check does not prove freshness or source coverage.

- `quality.py`: the wiki fetcher retries transient HTTP/network failures; the importer merges the previously known wiki URLs when category discovery is incomplete; it does not overwrite previously parsed quest text or its content hash after fetch errors or a severe parser regression. The wiki page refresh state is recorded separately. For historical data with unknown per-page timestamps, `legacy_cached` explicitly means **not fresh-verified**.
- Research schema v6 stores `wiki_refresh_state`, with last attempt/success timestamps, last error, and stored step counts. `coverage.json` records the cache/freshness distribution and a no-regression check against previous source/page/step totals.
- Runtime Questie schema v2 adds `quest_quality` and `scripts.variant_scope` (`common`, `pre-re`, `renewal`). The latter prevents future RoAura readers from silently mixing gameplay branches. Applications must check `PRAGMA user_version` before opening a catalog and keep user progress separately.
- `audit.py` publishes `quest_review_queue.json` with a prioritized list of known quest data gaps (missing script, missing NPC location per stage, missing guide steps, stale source, competing game variants). Every report is explicitly marked as **not game/server validated**. This file is for development/curation, not user quest recommendations.
- `test_quality.py` and `test_audit.py` exercise transient errors, retained data, parser regression protection, variant classification, idempotency and prioritization. New CI assertions validate the runtime schema, review queue, source catalog and foreign keys.

**Limitations:** a cached guide is still old data until revalidated, and a source script is not proof it is active on ToC. The no-regression gate protects against accidental truncation but cannot guarantee quest accuracy. Use `output/coverage.json` and the per-quest review queue for the actual latest verified counts; the historical stage statistics below are not a current coverage claim.

## Stage 6 — expanded quest-script source discovery and corroboration

The collector now inventories shared NPC quest, job, city, other and event scripts **plus distinct rAthena/Hercules pre-renewal and renewal quest/job/guide/location directories**. It explicitly excludes custom scripts, mobs, warps and most non-quest stores by default. Source paths distinguish variants; raw bytes must match each GitHub tree blob SHA. Limits such as the 900 KiB per-file ceiling mean this is not an exhaustive server installation.

An additional evidence pass in `augment.py` supplements earlier filename matches with (a) an exact multiword quest title found in the lead script comments, (b) the same map and coordinates **plus a distinctive NPC name** in the same wiki step, or (c) **two distinct** exact map/coordinate matches in the same candidate script. A single coincidental coordinate does not create a new quest-script mapping. All matches remain `candidate`, with method and evidence preserved in SQLite.

On the prior 314-script snapshot, the augmented matching increased wiki pages with candidate scripts from **70 to 155**, added 206 script candidates, and added 426 quest-step-to-NPC candidate rows (443 and 1,146 respectively). These are **candidate coverage** metrics, not gameplay validation; refreshed source counts are recorded separately in `output/coverage.json`. New tests cover distinct script variants, exact-header matching, refusal of single-coordinate evidence, and repeatability.

## Stage 5 — quest-state evidence and consumer SQLite

**Mission:** build a maximally complete, sourced, compact and independently consumable Ragnarok Online quest database for future RoAura Questie-style help. Do not modify the RoAura application during research.

- `state.py` extracts literal source commands `setquest`, `completequest`, `erasequest`, `checkquest`, `getitem`, `delitem`, `countitem` and variable writes, retaining per-NPC source lines, arguments and enclosing explicit-brace `if` guards. These are code facts, not evaluated/verified player progression.
- `npc_state_facts` keeps actions, checks, possible object IDs, count expressions, condition line references and script source identity. `npc_quest_registry_refs` links literal Quest IDs to the pre-re registry where a matching ID exists.
- `runtime.py` generates `output/questie_catalog.sqlite` (consumer `PRAGMA user_version=1`) and `questie_catalog.sqlite.gz`. The smaller integration surface contains stable wiki/source/NPC keys, steps, source-qualified coordinates, dialogue text and choices, source conditions, literal state facts and candidate matches; no Python parser or compressed raw script dependency at runtime.
- The consumer schema and SQL examples are documented in [INTEGRATION_CONTRACT.md](./INTEGRATION_CONTRACT.md). Consumer views include `quest_navigation_candidates`, `quest_progress_evidence` and `quest_dialogue_candidates`.
- `build.py --enrich-only` refreshes parser-derived tables from an already stored source SQLite without another upstream crawl. The report records the source snapshot timestamp. For new upstream source material, run `build.py` without this flag or use the workflow's explicit refresh option.

**Validated full-source snapshot, isolated test run [#40917146417](https://github.com/alerion08/RoAura/actions/runs/40917146417):** 24,309 lexical quest/item/variable facts, 1,005 quest-state actions, 396 quest checks, 6,057 item grants/consumptions, 5,020 item checks, 11,831 variable writes and 1,374 registry-linked literal Quest ID references. Preview runtime catalog: 286 quests, 3,420 guide rows, 3,904 NPC locations, 298,131 dialogue lines and 14,081 menu options, with SQLite integrity = OK and zero foreign-key violations.

These counts do **not** prove all quests or game states are captured. 216 wiki pages still have no matching script; dynamic control flow, server-specific variants, and manual quest validation remain unresolved.
## Verified stage 3 dataset (2026-10-29)

**[Full GitHub Actions build: SUCCESS](https://github.com/alerion08/RoAura/actions/runs/40910651236)** — schema version 4, SQLite integrity_check = ok, 0 foreign-key violations, 0 source-fetch errors.

- 314 compressed original NPC scripts; 3,904 NPC position records; 406,386 extracted script events.
- 286 wiki pages, 3,420 guide rows and 2,879 pre-renewal Quest ID entries.
- 5,815 NPC choice points; 14,081 options; **9,295 explicitly linked choice destinations**, 24,646 unevaluated if-guards and 38 locally resolved goto statements.
- 286 quest review rows: **216 without a candidate**, **61 tied competing script candidates**, **9 tied source/emulator variants**. These categories are *not* confirmed matching scripts.
- 33 candidate matches between explicit guide text and literal NPC menu labels. No gameplay routing is verified.
- SQLite 31,698,944 bytes; compressed GZIP 13,110,334 bytes.

All isolated parser/graph/review tests and full-build checks passed. [Real-source QA](https://github.com/alerion08/RoAura/actions/runs/40910773274) inspected Bunny Band (6 of 6 option edges), Juice Maker (18 of 24), and Kiel Hyre (169 of 213). Dynamic menus remain unresolved.

## Historical stage 2 dataset (2026-10-29)

**[Successful full extractor build + regression tests](https://github.com/alerion08/RoAura/actions/runs/40904241602)**. Data was published to this research branch and the run provides a downloadable ZIP artifact.

- SQLite: **29,405,184 bytes**; compressed SQLite (.gz): **12,310,410 bytes**.
- Database checks: `PRAGMA integrity_check = ok`, **0 foreign-key violations**.
- Public NPC source files: **227/227 rAthena**, **87/87 Hercules**, **314 total**, with no download errors.
- NPC position records: **3,904**; script command events: **406,386**; pre-renewal Quest ID registry records: **2,879**.
- iRO Wiki Classic: **286/286 pages fetched**, **3,420 extracted guide rows** including **2,931 numbered steps** and **489 narrative-only paragraphs**.
- Cross-references: **237 candidate wiki-to-script file links** spanning **70 distinct wiki pages**, plus **720 candidate wiki-step-to-NPC links**. Evidence and score are stored per link; NONE are gameplay-verified.
- Live wiki parser probe checked **75** steps for Kiel Hyre, **3** for Juice, **6** for Swordman Job Change, and **5** for Factory. The full build checked source coverage and SQLite integrity.

**Caveats:** 286 fetched pages are a discovery catalog, not proof every page is a standalone quest. Unnumbered narrative paragraphs are not verified ordered steps. Most wiki pages have no script match yet. Cross-server variants, NPC branch conditions, complete dialogue trees and active ToC scripts remain unresolved. This is a sourced **research database**, not a complete Questie navigation engine.

## Guide parsing and NPC cross-links (research stage 2)

The wiki parser now covers ordinary HTML numbered paragraphs (`1. ...`),
ordered lists and unnumbered narrative paragraphs. It appends continuation
paragraphs and list notes to the preceding step, preserving original guide
step numbers separately in `wiki_step_sources`. Narrative-only pages use
`wiki_parse_quality.mode=narrative`; they are **not** treated as verified
ordered walkthroughs.

The script collector includes rAthena pre-renewal quest/job script directories.
For each wiki guide, `wiki_script_links` connects likely NPC script files
by distinctive filename tokens; `wiki_step_npc_links` records limited
step-to-NPC candidates supported by exact same-map coordinates and/or
explicit NPC names. Match confidence is a ranking heuristic (60–95),
**not a probability and not verification of server correctness**. Status
always remains `candidate`. Do not auto-navigate to these coordinates
or infer dialogue branches without subsequent in-game validation.

Query examples (SQLite):

    SELECT p.title, s.step_no, s.instruction, sn.source_step
    FROM wiki_pages p JOIN wiki_steps s ON p.id=s.page_id
    LEFT JOIN wiki_step_sources sn ON sn.page_id=s.page_id AND sn.step_no=s.step_no
    WHERE p.title='Kiel Hyre Quest' ORDER BY s.step_no;

    SELECT p.title, v.path, l.score, l.method, l.status
    FROM wiki_script_links l JOIN wiki_pages p ON p.id=l.page_id
    JOIN provenance v ON v.id=l.source_id
    WHERE p.title='Kiel Hyre Quest' ORDER BY l.score DESC;

The original `script_events` provides ordered NPC script commands including
`mes`, `select`, `menu`, `setquest` and `close`. Full original script
payloads remain in `provenance` as compressed bytes. We **have not yet**
built a control-flow graph that ties each NPC menu option to game state,
nor confirmed which server-specific scripts are active in ToC/SpringRO.

## Dialogue graph and ambiguity audit (stage 3)

The third research stage adds a **partial lexical graph** of NPC dialogues. It never executes game code or claims to know the player's quest state.

- **npc_flow_points:** per-NPC select/menu anchors with the original expression.
- **npc_flow_options:** option number, literal label (NULL for dynamic choices), explicit target line when resolvable and an evidence tag.
- **npc_flow_conditions:** literal if-conditions and matched brace-block end where available; conditions are not evaluated.
- **npc_flow_jumps:** goto targets resolved only to unique local labels.
- **npc_flow_edges:** zero-copy SQL view over resolved menu-option and goto edges, with original script line numbers.
- **wiki_script_reviews:** one row per wiki page identifying no candidates, tied best candidates, single filename candidates, or additional coordinate support.
- **wiki_answer_menu_matches:** exact, distinctive wiki answer to literal menu-label candidates. Repeated labels stay ambiguous; generic Yes/No are excluded.

Resolved edges mean *explicit source references*, **not** a correct in-game answer or a verified route. Dynamic menus, function calls, switch fallthrough, loops, else branches, and many one-line conditions remain unresolved. The graph is not a complete control-flow interpreter.

Example SQL queries:

    SELECT p.line_no,o.option_no,o.label,o.target_line,o.resolution
    FROM npc_flow_points p JOIN npc_flow_options o ON o.point_id=p.id
    WHERE p.source_id=1 ORDER BY p.line_no,o.option_no;

    SELECT q.title,r.review_state,r.candidates,r.best_score_ties
    FROM wiki_script_reviews r JOIN wiki_pages q ON q.id=r.page_id
    WHERE r.review_state='ambiguous_top';

Schema version 4. All original source files remain in provenance, so future graph analysis can improve without losing source lines.
## Download the database

After the GitHub Actions job **Quest research database (isolated)** finishes successfully, use:

- [quest_research.sqlite](./output/quest_research.sqlite) — regular SQLite 3 file.
- [quest_research.sqlite.gz](./output/quest_research.sqlite.gz) — compressed version; extract .gz before opening.
- [coverage.json](./output/coverage.json) — actually collected counts and failed sources.

The GitHub Action also provides a quest-research-database ZIP artifact. Do not interpret an empty folder, a CI failure, or a partial source import as complete coverage.

## What is collected

- iRO Wiki Classic Quests index and subcategories; article steps, source URL, level prerequisites when explicit, coordinate and choice excerpts from the guide.
- Complete publicly distributed rAthena NPC script file payloads in provenance.content_zlib, individually zlib-compressed; upstream Git blob hash and source URL retained.
- Hercules npc/quests scripts kept as a separate comparison source.
- Per-script NPC positions/labels and event commands (mes, select, menu, input, setquest, etc.).
- rAthena pre-re Quest ID registry with quest-objective metadata.

Scripts and wiki articles are separate evidence sources. Different servers and versions may disagree. Extracted dialogues and choices are not necessarily on the actual player's path. Full script text is preserved, but parsing all branches and mapping every guide to exact scripts requires additional research.

## Read the complete NPC source

Python 3 example:

    import sqlite3, zlib
    c = sqlite3.connect("quest_research.sqlite")
    row = c.execute("SELECT path,content_zlib FROM provenance WHERE path LIKE '%kiel_hyre_quest.txt' LIMIT 1").fetchone()
    if row: print(row[0], zlib.decompress(row[1]).decode("utf-8", errors="replace"))

Data layout:

- provenance: source project, path, SHA, URL and compressed complete source script.
- script_npcs: NPC map, x/y, direction, name, sprite and source lines.
- script_events: parsed commands/arguments and source line.
- quest_registry: pre-re quest IDs, titles and objectives.
- wiki_pages, wiki_steps, wiki_details: wiki catalog, instructions and source-specific hints.
- research_meta: schema and verification status.

## Rebuild

The push-triggered workflow runs build.py against public remote sources. Local setup:

    python -m pip install requests beautifulsoup4 pyyaml
    python research/quest-database/build.py --out research/quest-database/output

It respects wiki robots.txt, uses a request delay, never bypasses source access restrictions, and records errors in coverage.json.

## Licensing

Upstream files include their original notices, and provenance stores the source URL. Follow rAthena and Hercules GPL terms before redistribution or reuse. iRO Wiki content has separate licensing requirements. This research archive does not grant any additional rights.

**Final stage 5 publication:** [GitHub Actions #40916787665](https://github.com/alerion08/RoAura/actions/runs/40916787665) SUCCESS. Research SQLite schema 5 = 35,082,240 bytes; consumer SQLite schema 1 = 37,773,312 bytes (7,602,500 bytes gzip). Both integrity checks = OK, zero FK violations. Source snapshot is from 2026-10-29T16:55:45Z; this run safely reused it with the offline enrichment mode. The consumer artifact is available in this branch at `output/questie_catalog.sqlite` / `.gz`. All new state/export unit tests and end-to-end row-count assertions passed. A separate earlier full-source refresh completed successfully in run #40916329046.

## Verified stage 8 server-neutral ROQuestie dataset (2026-10-30)

**[Full build succeeded](https://github.com/alerion08/RoAura/actions/runs/40930800624)**. Source snapshot was reused from 2026-10-29T17:15:16Z; wiki freshness is **not** certified by this run.

- Research SQLite schema v7 (69,914,624 bytes), consumer Questie catalog v3 (72,753,152 bytes). Both integrity checks OK; 0 foreign key violations.
- 939 pinned source scripts, 286 indexed wiki records, 3,420 source-extracted steps, 574 source-script candidates across 169 wiki records, 1,298 step-to-NPC candidates.
- Explicit wiki quest prerequisite sections: **51**; exact known-page prerequisite edges: **57**, covering 45 pages; 6 sections have no matching indexed page title. Unspecified Boolean logic is **not** interpreted.
- Generic `roquestie_core_worklist.json` covers all 286 pages: 52 clearly tagged main-story/dungeon/job research candidates, 67 seasonal/historical archive candidates, 12 possible profession reference pages, 155 uncategorized candidates. Classifications are title-based **research hints**, not game facts.
- `critical_quest_triage.json` preserves all 84 critical records (42 iRO events/spotlights, 41 other quest candidates, 1 possible reference page). No quests are discarded or declared supported/unsupported across servers.
- `quest_id_candidates` view exposes Quest IDs found in emulator code alongside NPC, script line and source evidence. This does **not** prove a quest is active on any specific server.
- `SERVER_VALIDATION.md` describes generic evidence tiers and optional server overlays.

The shared ROQuestie database is **not yet complete** or available-for-all-servers verified. The iRO wiki source freshness remains legacy/unknown for 286 pages until the separate live refresh succeeds.

## Verified stage 9 — content evidence, grounded NPC leads and dependency paths

**[GitHub Actions #40936291908](https://github.com/alerion08/RoAura/actions/runs/40936291908) SUCCESS**, including focused unit tests, source DB/consumer SQLite integrity check, zero foreign-key violations and published artifacts. The rebuild reused the successfully refreshed 2026-10-29 iRO wiki snapshot. No game-server validation was performed.

ROQuestie remains a server-neutral knowledge base; source-derived categories and guessed links must **never** be advertised as quests or NPCs guaranteed to exist on every server.

- `content.py`: explicit walkthrough phrase and wiki-requirement signals with source step and matched text, exported in research tables `wiki_content_categories`/`wiki_content_signals`. 1,370 source text signals across 286 pages; **27 previously unsorted pages** gained an evidence-qualified category, reducing the unclassified queue from 155 to **128**. Many more labels remain provisional; unknown pages are not forced into a category.
- `name_links.py`: only explicit "talk/speak/meet/visit ... Name" guide mentions and distinctive unique NPC names in **strong existing script candidates**, preserving score 55 and candidate status. **267 new step→NPC candidate links**, total 1,565 (previous 1,298). These may still be wrong; game verification is required.
- `dependencies.py`: cycle-safe shortest-reference graph for exact wiki prerequisite statements. The original **57** direct links yield **91** additional transitive *wiki references* (148 pairs); **0** cycles detected in this snapshot. AND/OR prerequisite logic and actual game activation are unresolved.
- **Questie consumer v4**, `questie_catalog.sqlite`: now includes 1,254 structured wiki waypoint hints in `quest_step_locations`, including bare coordinates with NULL map rather than guessing. Also exports per-quest source classifications, matching evidence and dependency paths. No parser/Python dependency is needed at runtime.
- Research SQLite schema v8 retains all original NPC script sources, full step text, flags and provenance. 939 scripts and 3,420 wiki guide rows remain; data were not discarded to inflate coverage.
- `roquestie_core_worklist.json` v2 covers all 286 pages. Provisional categories: 44 profession-related, 22 dungeon/access, 8 storyline, 3 repeatable, 2 collection, 67 seasonal/archival, 12 profession references, **128 unclassified**. Source categories are research directions, not definitive RO quest taxonomy.
- Gaps: **117 guides still lack any candidate script**, and no quest is certified universally available. Exact NPC identification, game conditions, quest progress signals and cross-server compatibility still need separate verification.

The consumer upgrade changes `PRAGMA user_version` from 3 to **4**. Downstream readers must check this value and preserve player state separately from the updateable catalog. See `INTEGRATION_CONTRACT.md`.

## Verified stage 10 — evidence-only discovery of missing quest scripts

**GitHub Actions [#40941334922](https://github.com/alerion08/RoAura/actions/runs/40941334922): SUCCESS.** Fast regression tests [#40941334999](https://github.com/alerion08/RoAura/actions/runs/40941334999): 45 passed. Both SQLite integrity checks returned `ok`; zero foreign-key violations. Scope remains the isolated `research/quest-database` branch. The build reuses the last refreshed wiki/source snapshot and does not claim new upstream verification.

The **117** indexed wiki pages with no accepted script candidates are now audited by `discovery.py`. Results are in `output/missing_script_discovery.json` and the research-only `wiki_missing_script_reviews`, `wiki_missing_script_proposals` and `wiki_missing_script_evidence` tables. For a new source proposal the guide must explicitly say to interact with at least **two distinctive NPC names in two separate steps**, and both NPCs must be unique within the **same** indexed source script. Common, generic or overly frequent names are deliberately excluded. Script blobs, NPC names, source lines and guide step numbers remain available for manual source audit. **No proposal is automatically added to `wiki_script_links` or to the RoAura consumer SQLite.**

Verified queue: 13 wiki pages have **25 research-only script proposals** supported by 83 source evidence rows; the remaining 104 pages are classified by investigation reason (56 with no distinctive matching NPC in the indexed scripts, 29 with insufficient independent evidence, 18 with no explicit NPC interaction phrase and 1 with no extracted guide steps). Multiple script variants (rAthena/Hercules; pre-re/renewal) are retained separately, so 25 proposals do not mean 25 confirmed quests.

`output/unclassified_quest_review.json` is a separate **128-page** curation report preserving wiki requirements, original guide excerpts, source signals and literal map-coordinate evidence. It does **not** invent an arbitrary category for unclassified material.

Examples worth manual scrutiny include `Einbroch Murder Quest` → `quests_ein.txt`, `Rebirth Walkthrough` → `valkyrie.txt`, and `Hugel Memory Quest` → `quests_hugel.txt`. All are source-backed hypotheses, not server-confirmed walkthroughs. **Next stage:** inspect top proposals against actual NPC dialogue and quest variables, approve or reject individually with reviewer provenance, then investigate truly missing source files and the 128 unclassified guide pages.
