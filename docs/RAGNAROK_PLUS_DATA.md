# Ragnarok+ data integration

Ragnarok+ uses its own catalog and Gold Goblin rules. The bundled catalog
contains a small verified sample for initial validation.
The official catalogs are at https://ragnarok.plus/item-db,
https://ragnarok.plus/mob-db and https://ragnarok.plus/map-db.

Put a full `database.ragnarokplus.json` in the RoAura local application-data
directory to override the sample. Use the `GameDatabase` schema in
`Models/DatabaseModels.cs`. Restart the application to reload the catalog.

Use `python tools/build_ragnarokplus_database.py rows.json output.json --base Data/database.ragnarokplus.json`
to merge rendered catalog lists into the local format. The input is a JSON
object with optional `items`, `monsters` and `maps` arrays. Each entry is
an array of visible text lines from one catalog card. The importer preserves
existing detail fields such as NPC sell price and rejects conflicting rows
for the same ID.

## Live market

The Ragnarok+ Market tab opens the official sign-in page through WebView2.
Use **Sign in**, complete the site sign-in, and search for an item from the
local Ragnarok+ catalog by name or ID. The separate WebView2 profile under
RoAura's local application-data directory retains the site session. The app
does not read the login form or export its cookies.

Live search opens the item page and reads the selected selling or buying
rows from **Live Market**. A partial name returns matches to select in the
overlay. The browser stays in the background during searches and appears
when the site requires sign-in. Gold Goblin uses the last observed selling
rows. RoAura displays the Ragnarok+ minimap and its pin when available.
Live search requires WebView2 Runtime.

A full catalog export, background refresh of market offers, and Ragnarok+
market notifications are pending.

## Catalog and price coverage

The current catalog includes the first item page, the first monster page,
previously verified MVPs, and reviewed individual entries. Ragnarok+ showed
6,657 items in the item catalog on 2026-11-19. No consolidated live offer export
has been verified. Visiting the item pages once per ten minutes would require
about 11 page navigations per second per client, before loading the dialogs.
Gold Goblin needs only selling prices for items with a vending price rule.
A scoped refresh can reuse the live market reader and the existing price
cache without requiring a complete offer snapshot.
