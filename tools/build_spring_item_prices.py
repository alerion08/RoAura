#!/usr/bin/env python3
"""Build SpringRO NPC sell prices from the official server-rendered item list."""
import concurrent.futures
import datetime
import json
import math
import pathlib
import re
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]
HEADERS = {"User-Agent": "RoAura item price builder/1.0"}
TOTAL_RE = re.compile(rb"total:(\d+)")
ITEM_RE = re.compile(
    rb'id:(\d+),aegisName:"(?:\\.|[^"])*",name:"((?:\\.|[^"])*)".*?,buy:(\d+),slots:',
    re.DOTALL,
)


def download(page):
    url = f"https://springro.com/database/items?page={page}"
    request = urllib.request.Request(url, headers=HEADERS)
    with urllib.request.urlopen(request, timeout=45) as response:
        return response.read().replace(b"\0", b"")


def parse(raw):
    result = []
    for item_id, encoded_name, buy in ITEM_RE.findall(raw):
        # The list payload exposes the official NPC buy value. Ragnarok's NPC
        # sell value shown on each detail page is exactly floor(buy / 2).
        name = json.loads(b'"' + encoded_name + b'"')
        result.append({"Id": int(item_id), "Name": name, "NpcSellPrice": int(buy) // 2})
    return result


def main():
    first = download(1)
    total_match = TOTAL_RE.search(first)
    if not total_match:
        raise RuntimeError("SpringRO item count was not found")
    total = int(total_match.group(1))
    page_count = max(1, math.ceil(total / 50))
    pages = {1: first}
    with concurrent.futures.ThreadPoolExecutor(max_workers=10) as pool:
        futures = {pool.submit(download, page): page for page in range(2, page_count + 1)}
        for future in concurrent.futures.as_completed(futures):
            pages[futures[future]] = future.result()

    prices = []
    for page in range(1, page_count + 1):
        prices.extend(parse(pages[page]))
    prices = sorted({item["Id"]: item for item in prices}.values(), key=lambda item: item["Id"])
    if len(prices) != total:
        raise RuntimeError(f"Expected {total} items, parsed {len(prices)}")

    payload = {
        "Source": "SpringRO",
        "SourceUrl": "https://springro.com/database/items",
        "GeneratedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "Items": prices,
    }
    output = ROOT / "Data/item-prices.springro.json"
    output.write_text(json.dumps(payload, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"Wrote {output}: {len(prices)} items")


if __name__ == "__main__":
    main()
