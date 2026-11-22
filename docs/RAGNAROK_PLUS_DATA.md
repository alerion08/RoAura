# Ragnarok+ data integration

RoAura keeps a local Ragnarok+ catalog, market price cache, watch list and
watch history. Select the Ragnarok+ profile and use **Sign in** in the database
window to authorize the embedded WebView2 session.

## Catalog

Open **Settings → General → Local databases** and click **Download** beside Ragnarok+. The database window links to these settings when the active catalog has not been installed. SpringRO can install its catalog from the copy included with the application.
RoAura fetches the catalog manifest from the `catalogs` branch and downloads the
Ragnarok+ ZIP from GitHub Releases. **Update data** checks the installed archive
against the published SHA-256. Downloads are staged alongside the installed ZIP,
then verified and installed in one replacement. Interrupted downloads leave the
previous catalog available.

The test portable package includes a verified ZIP beside `RoAura.exe`. On first
installation the same button can install it offline. Subsequent update checks
use the GitHub manifest.

The installed archive is stored under `Catalogs/ragnarokplus.zip` in RoAura's
local data directory. Search, descriptions, drops, spawn lists, bundled images
and NPC sell prices use the installed catalog offline. The bundled starter
catalog and older local cache remain available before installation.

The version-1 manifest uses a `servers` object keyed by the server ID. Each entry
provides `version`, `url`, `sizeBytes` and `sha256`. The Ragnarok+ entry
uses key `ragnarokplus` and the ZIP entry `Data/database.ragnarokplus.json`.
Publish the ZIP as a release asset before exposing its manifest entry.
Ragnarok+ is the first server supported by the downloader.

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
