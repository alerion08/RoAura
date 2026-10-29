# Tides of Comodo (ToC): offline catalog

Scope: data preparation for Database, MVP Timers, Statistics and Gold Goblin.
No ToC packet support, server selector, UI activation or market-price integration
is implied by this catalog. Existing settings, presets and server behavior are unchanged.

## Snapshot: 2026-10-29

- 9,894 items, 1,061 monsters, 61 MVPs.
- 4,946 resolved normal-drop rows and 156 MVP reward rows.
- All indexed normal-drop item IDs and MVP reward Aegis names resolve locally.
- SQLite file: 12,386,304 bytes (11.81 MiB); full record JSON before compression:
  31,764,418 bytes. Exact checksum is in `database.toc.manifest.json`.
- Integrity, every payload ID, counts and manifest checksum checked locally.
  Six importer/reader regression tests pass. Item 501/909 list/detail records
  match exactly; all twelve inverse Jellopy drop rows match its public detail.
- Across **all** retrieved spawn records, the only fields are `map_name`,
  `map_display_name` and `amount`: respawn times are not published here.
- Full Windows app tests and portable packaging are delegated to this branch's
  validation workflow; no local .NET SDK is available. Game integration is outside
  this data-only change and has no ToC manual acceptance yet.

## Source and content

Official public database: https://tidesofcomodo.com/db/item/,
https://tidesofcomodo.com/db/monster/ and its `?mvp=1` filter.
The current SPA obtains public JSON from `https://tidesofcomodo.com/api/v1`.
The builder uses that same API, with English/default presentation, preserving
all language fields supplied by the source.

- `/items?page=N&page_size=100`: complete item records, names, slots, types,
  prices, weight, jobs/classes/locations/levels, descriptions, scripts,
  equip/unequip scripts, stack/trade/use flags and custom fields.
- `/monsters?page=N&page_size=100`: enumeration and displayed server EXP.
- `/monsters/{id}`: complete monster records, raw and server EXP, normal and
  MVP drop fields, resolved drop IDs and actual rates, spawns, skills,
  tooltips, levels and AI rules. Every returned field is preserved.
- `/monsters?mvp=1`: independently checked against monster `is_mvp` flags.

The item detail SPA consumes `item` and `dropped_by`. The list supplies the
complete `item`; the inverse drop relation is reconstructed from monster detail
drops. This avoids almost ten thousand redundant requests and storing both
directions. Live market offers (`/market/item/{id}`) are dynamic service data,
not part of the static catalog. NPC buy/sell prices are not market quotations.

## Compact storage

`Data/database.toc.sqlite` is a single SQLite database. Query columns and indexes
cover ID, name, Aegis name, item prices/slots/type, monster level/HP/server EXP and
MVP flags. Full API records are UTF-8 JSON compressed with zlib, separately per
record, so a reader decompresses only the selected card. `drops` provides an
indexed item-to-monster relation. `mvp_rewards` separately resolves MVP reward
Aegis names to item IDs when an unambiguous match exists; missing matches remain
NULL and retain the original name and complete reward payload. `mvp` is a view of `monsters`, without copying
MVP records. Unknown/new fields remain in the payload instead of being discarded.
`listing_overrides` holds only list fields differing from the detail record
(e.g. displayed EXP); identical list/detail fields are stored once.

No sprite binaries, web pages or game-client files are shipped. Source image
paths can be constructed without a download:

- Item illustration: `https://tidesofcomodo.com/db/collection-img/{id}.png`
- Monster sprite: `https://tidesofcomodo.com/db/mob-img/{id}.gif`

Offline text/statistical data does not require network access; these images do.
HTML descriptions/tooltips are source data: future UI consumers must sanitize
HTML before rendering and resolve relative image paths against the source host.

## Refresh and inspection

Requirements: Python 3.10+ standard library and curl; no Python packages or
application runtime dependencies added. From the repository root:

```sh
python tools/build_toc_database.py --cache /tmp/roaura-toc-YYYYMMDD --workers 6
python tools/inspect_toc_database.py Data/database.toc.sqlite
python tools/inspect_toc_database.py Data/database.toc.sqlite --kind item --id 909
python tools/inspect_toc_database.py Data/database.toc.sqlite --kind monster --id 1002
python -m unittest discover -s tools -p test_build_toc_database.py
```

Use a **new external cache directory per refresh**; reusing one resumes the same
snapshot, it does not refresh existing responses. Keep raw cache outside Git and
distributions. Downloads have bounded concurrency, timeouts and retry backoff.
The previous database is replaced only after full enumeration, detail retrieval,
MVP consistency and SQLite checks succeed. `.building` is an incomplete staging
file, never a usable snapshot. The manifest records counts, retrieval time,
byte size and SHA-256. The API is not transactional; unchanged total counts
cannot rule out source edits while downloading.

## Data semantics and limits

- Preserve raw `monster.base_exp/job_exp` and top-level displayed server
  `base_exp/job_exp` independently. Statistics must not multiply the latter again.
- Preserve `rate` and `real_rate` independently. Do not assume every rate is an
  already formatted percentage. Source item weight is in tenths of a unit.
- Normal drops and MVP reward fields must stay distinct. `drops` indexes the
  resolved normal-drop section; MVP reward definitions remain in full payloads and `mvp_rewards`.
- MVP status comes from the official filter and explicit flag, not `class=Boss`.
- Spawn amount is not a respawn duration. Missing delay/randomness must remain
  unknown; do not derive timers from unrelated skill delay fields or another server.
- This is an offline catalog foundation. Connecting it to the WPF services and
  validating ToC packets, character identity, loot valuation and timers are later work.
