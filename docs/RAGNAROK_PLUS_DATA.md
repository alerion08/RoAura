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

Live search opens the item page and reads the selling rows in the site's
**Live Market** dialog. It does not substitute cached rows when live search
fails. Gold Goblin uses the last successfully observed selling rows, and
the snapshot importer can also update that cache. The live market requires
WebView2 Runtime. Buying offers and Windows end-to-end validation are pending.

Current offers are available per item, for example at
https://ragnarok.plus/item-db/999 under **Market → View shops**.
Import an offline snapshot using **Market → Import** in the Ragnarok+ profile.
RoAura validates it and updates Gold Goblin valuation after import. Use the following structure. Replace the
example timestamp with the time the snapshot was actually captured, and
include only current offers. Each row uses the `MarketListing` schema.

```json
{
  "capturedUtc": "2026-11-19T09:00:00Z",
  "listings": []
}
```

Use `SELL` or `BUY` as `shopType`, and `SC` or `HC` as `gameMode`.
The market displays the snapshot timestamp and supports search by item
name or ID. The Gold Goblin vending price rule uses the lowest Softcore
selling offer in the snapshot. When there is no matching offer, the rule
uses its configured manual price or the catalog's NPC sell price.
Historical averages are not offers.

The same import button accepts UTF-8 `.txt` files containing the English
**Live Market** dialog text, including the title, selling count and all
selling rows. The title must match a unique item name in the loaded catalog.
A text import replaces offers for that item only. Other items keep their
previous observation times. JSON import still replaces the full snapshot.

A full catalog export, background refresh of market offers, and Ragnarok+ market notifications are pending.
