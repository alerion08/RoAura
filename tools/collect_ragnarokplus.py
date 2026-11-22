"""Collect a local RoAura catalog from the signed-in Ragnarok+ website."""

import argparse
import asyncio
import base64
from datetime import datetime, timezone
from io import BytesIO
import json
from pathlib import Path
import re
from time import monotonic
from urllib.parse import unquote, urlsplit
from zipfile import ZIP_DEFLATED, ZipFile

from build_ragnarokplus_database import parse_item, parse_monster

ROOT = Path(__file__).resolve().parents[1]
ORIGIN = "https://ragnarok.plus"
SECTIONS = {"items": "item-db", "monsters": "mob-db", "maps": "map-db"}
LABELS = {"items": "items", "monsters": "monsters", "maps": "maps"}

INDEX_SCRIPT = r"""({route, label}) => {
    const main = document.querySelector('main');
    if (!main) return null;
    const totals = [...main.querySelectorAll('strong')].map(node =>
        node.parentElement.textContent.trim().replace(/\s+/g, ' '));
    const pattern = new RegExp('^(\\d[\\d, ]*) ' + label + '$', 'i');
    const total = totals.map(text => text.match(pattern)).find(Boolean);
    const base = '/' + route + '/';
    const rows = [...main.querySelectorAll('a[href^="' + base + '"]')]
      .filter(link => link.getClientRects().length)
      .map(link => ({path: new URL(link.href).pathname, text: link.innerText.trim()}))
      .filter(row => row.path.startsWith(base) && !row.path.slice(base.length).includes('/'));
    return total && rows.length ? {total: Number(total[1].replace(/[, ]/g, '')), rows} : null;
}"""

ART_SCRIPT = r"""(kind) => {
    const allowed = kind === 'maps' ? /\/maps?\//i : /\/(mobs?|monsters?)\//i;
    return [...document.querySelectorAll('main img')]
      .map(img => img.currentSrc || img.src)
      .filter(src => {
        if (!src) return false;
        const image = new URL(src);
        return image.hostname === 'cdn.ragnarok.plus' && allowed.test(image.pathname);
      });
}"""


def extract_script(relative_file, name):
    source = (ROOT / relative_file).read_text(encoding="utf-8")
    return source.split(f'const string {name} = """', 1)[1].split('"""', 1)[0].strip()


def save_json(path, data):
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(data, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    temporary.replace(path)


async def open_page(page, path):
    response = await page.goto(ORIGIN + path, wait_until="domcontentloaded")
    if response and response.status >= 400:
        raise RuntimeError(f"Ragnarok+ HTTP {response.status} for {path}")
    if urlsplit(page.url).path == "/login":
        raise RuntimeError("Ragnarok+ session expired. Run again and sign in.")


class PageDataUnavailable(ValueError):
    pass


async def wait_for_dom(read, description, timeout=12):
    deadline = monotonic() + timeout
    while True:
        result = await read()
        if result:
            return result
        if monotonic() >= deadline:
            raise PageDataUnavailable(f"Page data unavailable: {description}")
        await asyncio.sleep(0.25)


async def read_index(page, kind):
    route = SECTIONS[kind]
    await open_page(page, "/" + route)
    args = {"route": route, "label": LABELS[kind]}

    async def read_snapshot():
        return await page.evaluate(INDEX_SCRIPT, args)

    records = {}
    snapshot = await wait_for_dom(read_snapshot, route)
    while True:
        for row in snapshot["rows"]:
            path = row["path"]
            if path in records:
                raise ValueError(f"{kind}: duplicate card {path}")
            records[path] = row["text"]
        next_page = page.get_by_role("button", name="Next Page", exact=True)
        if not await next_page.count() or not await next_page.is_enabled():
            break
        first = snapshot["rows"][0]["path"]
        await next_page.press("Enter")

        async def read_next_snapshot():
            candidate = await read_snapshot()
            if candidate and all(row["path"] != first for row in candidate["rows"]):
                return candidate
            return None

        snapshot = await wait_for_dom(read_next_snapshot, f"{route} after {first}")
    if len(records) != snapshot["total"]:
        raise ValueError(f"{kind}: saved {len(records)} of {snapshot['total']} listed cards")
    return records


async def get_detail(page, script):
    return await wait_for_dom(lambda: page.evaluate(f"() => ({script})"), urlsplit(page.url).path)


async def local_image(request, urls, size):
    from PIL import Image
    from playwright.async_api import Error as BrowserError

    for url in dict.fromkeys(urls):
        if urlsplit(url).netloc != "cdn.ragnarok.plus" or urlsplit(url).scheme != "https":
            continue
        for attempt, timeout in enumerate((20000, 40000), 1):
            try:
                response = await request.get(url, timeout=timeout, max_redirects=0)
                try:
                    if response.status in (404, 410):
                        break
                    if not response.ok:
                        print(f"Image HTTP {response.status}: {url}", flush=True)
                        if response.status < 500 and response.status not in (408, 429):
                            break
                    else:
                        with Image.open(BytesIO(await response.body())) as source:
                            image = source.convert("RGBA")
                            image.thumbnail((size, size))
                            buffer = BytesIO()
                            image.save(buffer, "PNG", optimize=True)
                            return base64.b64encode(buffer.getvalue()).decode("ascii")
                finally:
                    await response.dispose()
            except (BrowserError, OSError, Image.DecompressionBombError) as error:
                print(f"Image attempt {attempt}/2: {url} ({str(error).splitlines()[0]})", flush=True)
            if attempt == 1:
                await asyncio.sleep(1)

    return None


async def card(page, kind, path, label, script):
    await open_page(page, path)
    identity = unquote(path.rsplit("/", 1)[-1])
    lines = [line.strip() for line in label.splitlines() if line.strip()]
    try:
        detail = await get_detail(page, script)
    except PageDataUnavailable:
        if kind != "items":
            raise
        print(f"Item details unavailable: {path}. Keeping index entry for retry.", flush=True)
        return {**parse_item(lines), "droppedBy": [], "localImagePng": None,
                "ragnarokPlusDetailsCaptured": False}
    if kind == "items":
        record = parse_item(lines)
        raw = detail["Sell"].strip().rstrip("zZ").strip()
        price = 0 if raw in {"—", "–", "-"} else int(re.sub(r"[,\s]", "", raw))
        record.update(npcSellPrice=price, description=detail["Description"], droppedBy=detail["Drops"])
        urls = detail["Images"]
    elif kind == "monsters":
        record = parse_monster(lines)
        record.update(drops=detail["Drops"], spawns=detail["Spawns"])
        urls = await page.evaluate(ART_SCRIPT, kind)
    else:
        record = {"code": identity, "name": detail["Name"], "monsters": detail["Monsters"]}
        urls = await page.evaluate(ART_SCRIPT, kind)
        urls.append(f"https://cdn.ragnarok.plus/images/maps/{identity}.webp")
    if kind != "maps" and record["id"] != int(identity):
        raise ValueError(f"Index ID does not match {path}")
    record["localImagePng"] = await local_image(page.request, urls, 512 if kind == "maps" else 128)
    record["ragnarokPlusDetailsCaptured"] = True
    return record


def package(output, catalog, indexes):
    missing_images = {}
    for kind in SECTIONS:
        if len(catalog[kind]) != len(indexes[kind]) or not catalog[kind]:
            raise ValueError(f"{kind}: incomplete catalog")
        missing_images[kind] = [
            row.get("id", row.get("code")) for row in catalog[kind] if not row["localImagePng"]
        ]
    item_ids = {row["id"] for row in catalog["items"]}
    monster_ids = {row["id"] for row in catalog["monsters"]}
    map_codes = {row["code"] for row in catalog["maps"]}
    report = {
        "unresolvedReferences": {
            "dropItems": sorted({drop["ItemId"] for row in catalog["monsters"] for drop in row["drops"]} - item_ids),
            "dropMonsters": sorted({source["MonsterId"] for row in catalog["items"] for source in row["droppedBy"]} - monster_ids),
            "spawnMaps": sorted({spawn["Code"] for row in catalog["monsters"] for spawn in row["spawns"]} - map_codes),
            "mapMonsters": sorted({monster["MonsterId"] for row in catalog["maps"] for monster in row["monsters"]} - monster_ids),
        },
        "counts": {kind: len(catalog[kind]) for kind in SECTIONS},
        "missingImages": missing_images,
        "missingDetails": {
            kind: [row.get("id", row.get("code")) for row in catalog[kind]
                   if row.get("ragnarokPlusDetailsCaptured") is False]
            for kind in SECTIONS
        },
    }
    if any(missing_images.values()):
        print("Image gaps are listed in coverage.json.")
    temporary = output / "ragnarokplus-offline.zip.tmp"
    with ZipFile(temporary, "w", compression=ZIP_DEFLATED) as archive:
        archive.writestr("Data/database.ragnarokplus.json", json.dumps(catalog, ensure_ascii=False))
        archive.writestr("coverage.json", json.dumps(report, ensure_ascii=False, indent=2))
    with ZipFile(temporary) as archive:
        if archive.testzip() is not None:
            raise ValueError("ZIP integrity check failed")
    result = output / "ragnarokplus-offline.zip"
    temporary.replace(result)
    return result


async def collect_section(context, index, kind, directory, script, workers):
    pending = iter(index.items())
    results = {}

    async def worker():
        page = await context.new_page()
        try:
            for path, label in pending:
                identity = unquote(path.rsplit("/", 1)[-1])
                if not re.fullmatch(r"[a-zA-Z0-9_-]+", identity):
                    raise ValueError(f"Unexpected card address: {path}")
                saved = directory / (identity + ".json")
                record = json.loads(saved.read_text(encoding="utf-8")) if saved.exists() else None
                if not record or not record.get("localImagePng") or record.get("ragnarokPlusDetailsCaptured") is False:
                    record = await card(page, kind, path, label, script)
                    save_json(saved, record)
                    await asyncio.sleep(0.5)
                results[path] = record
                if len(results) % 25 == 0 or len(results) == len(index):
                    print(f"{kind}: {len(results)}/{len(index)}", flush=True)
        finally:
            await page.close()

    tasks = [asyncio.create_task(worker()) for _ in range(min(workers, len(index)))]
    try:
        await asyncio.gather(*tasks)
    finally:
        for task in tasks:
            if not task.done():
                task.cancel()
        await asyncio.gather(*tasks, return_exceptions=True)
    return [results[path] for path in index]


async def collect(output, browser_data, channel, workers):
    from playwright.async_api import async_playwright

    output.mkdir(parents=True, exist_ok=True)
    scripts = {
        "items": extract_script("RagnarokPlusMarketWindow.ItemDetails.cs", "ItemDetailsScript"),
        "monsters": extract_script("RagnarokPlusMarketWindow.WorldDetails.cs", "MonsterDetailsScript"),
        "maps": extract_script("RagnarokPlusMarketWindow.WorldDetails.cs", "MapDetailsScript"),
    }
    async with async_playwright() as playwright:
        context = await playwright.chromium.launch_persistent_context(str(browser_data), channel=channel, headless=False)
        try:
            page = context.pages[0] if context.pages else await context.new_page()
            await page.goto(ORIGIN + "/mob-db", wait_until="domcontentloaded")
            if urlsplit(page.url).path == "/login":
                await asyncio.to_thread(input, "Войдите в Ragnarok+ в открывшемся окне и нажмите Enter здесь. ")
            checkpoint = output / "checkpoint"
            checkpoint.mkdir(exist_ok=True)
            indexes = {}
            catalog = {"source": "Ragnarok+"}
            for kind in ("monsters", "maps", "items"):
                index = await read_index(page, kind)
                indexes[kind] = index
                directory = checkpoint / kind
                directory.mkdir(exist_ok=True)
                catalog[kind] = await collect_section(context, index, kind, directory, scripts[kind], workers)
            catalog["catalogSynchronizedUtc"] = datetime.now(timezone.utc).isoformat()
            return package(output, catalog, indexes)
        finally:
            await context.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--browser-data", type=Path, required=True, help="Dedicated browser profile outside the output directory")
    parser.add_argument("--channel", choices=["chrome", "msedge"], help="Use installed Chrome or Edge")
    parser.add_argument("--workers", type=int, choices=range(1, 11), default=3)
    args = parser.parse_args()
    if args.browser_data.resolve().is_relative_to(args.output.resolve()):
        parser.error("--browser-data must be outside --output")
    print(asyncio.run(collect(args.output, args.browser_data, args.channel, args.workers)))


if __name__ == "__main__":
    main()
