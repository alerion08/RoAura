# Ragnarok+ data integration

RoAura keeps a local Ragnarok+ catalog, market price cache, watch list and
watch history. Select the Ragnarok+ profile and use **Sign in** in the database
window to authorize the embedded WebView2 session.

## Catalog

RoAura starts synchronization in the background after sign-in. It follows the
official item and monster catalog pagination, verifies unique IDs and the
reported totals for both lists, then replaces the local catalog atomically.
The database search, item catalog and watch editor reload without restarting
RoAura. An interrupted sync keeps the previous catalog.

The portable package includes a starter catalog for offline use before the
first successful synchronization. The sync collects item IDs, names and types
and monster IDs, names, levels, race, element, size, Base/Job EXP and MVP flags.
Previously collected details are preserved. Individual descriptions, drop
tables, spawn locations and advanced stats still require collection from
detail pages.

## Market

Live search reads selling and buying offers from the official item page.
Background checks reuse the authorized WebView2 session and use the interval
configured in Database settings. They visit the union of enabled watch
items and items with an active Gold Goblin vending price rule. Each price
refresh uses the lowest observed Softcore selling price for that item.

The watch list checks selling rows, applies the item's configured price limit
and stores its alert history locally. A successful empty response clears old
selling rows from the price cache. Failed checks preserve the previous prices
and leave the initial notification baseline untouched. If the session has
expired, open **Sign in** to resume the background checks.
