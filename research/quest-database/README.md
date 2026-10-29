# RoAura Quest research database

Isolated **research branch**. No changes to the RoAura WPF application, settings, updater, main, release files or database runtime.

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
