# Ragnarok+ data integration

RoAura keeps a local Ragnarok+ catalog, market price cache, watch list and
watch history. Select the Ragnarok+ profile and use **Sign in** in the database
window to authorize the embedded WebView2 session.

## Catalog

RoAura synchronizes the official item and monster indexes in the background
after sign-in and checks their published totals. It then visits item cards
sequentially to collect the displayed NPC sell value, description, drop
sources and a local PNG thumbnail (at most 96 pixels on the longest side).
It checkpoints progress in a compressed local catalog and resumes from
uncollected cards on subsequent runs. A displayed zero sell price is kept
distinct from an item whose price is still unknown.

Search and card rendering in the Items tab read the local catalog. NPC sell
prices are also available to Gold Goblin as the catalog is updated. The portable
package contains a starter snapshot. Extended monster detail collection is
outside the current item-card sync.

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
