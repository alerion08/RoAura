# RoAura Quest research database

Isolated **research branch**. No changes to the RoAura WPF application, settings, updater, main, release files or database runtime.

## Current verified dataset (2026-10-29)

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
