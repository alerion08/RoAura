# RoAura Quest research database

Isolated **research branch**. No changes to the RoAura WPF application, settings, updater, main, release files or database runtime.

## Verified GitHub Actions build (2026-10-29)

[Successful validation and publication run](https://github.com/alerion08/RoAura/actions/runs/40899345670).

- SQLite: 26,980,352 bytes; gzip: 11,481,308 bytes.
- SQLite integrity_check: ok; foreign key violations: 0.
- Script sources: rAthena 184/184; Hercules 87/87; source download errors 0.
- NPC positions: 3,837; command rows: 393,561; pre-re quest registry entries: 2,879.
- iRO Wiki: 286/286 discovered pages fetched; **only 90 structured wiki steps and 53 wiki details**. This is a major extraction coverage gap, not complete quest coverage.
- "Fetched" proves an HTTP request succeeded; it does not prove the guide's steps were parsed. Original script payloads are kept, but semantic branching and wiki-to-script quest matching require further work.

**Status: source research dataset, not a complete Questie-ready database.**

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
