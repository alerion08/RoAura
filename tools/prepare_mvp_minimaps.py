#!/usr/bin/env python3
"""Bundle exact minimaps and monster sprites from the pinned timer catalogs."""
import concurrent.futures
import hashlib
import json
import pathlib
import re
import sqlite3
import struct
import time
import urllib.request
import urllib.error
import zlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
SOURCES = ("https://choobs.org/maps/{}.png", "https://tidesofcomodo.com/db/map-img/{}.png",
           "https://www.divine-pride.net/img/map/original/{}")
TOC_SPRITE = "https://tidesofcomodo.com/db/mob-img/{}.gif"
SPRING_SPRITE = "https://pub-36f18a13614d49f0997ef23bce857ec4.r2.dev/sprites/mobs/{}.png"
TOC_SPRITE_FALLBACK = {
    1382: "https://file5s.ratemyserver.net/mobs/1382.gif",
    1429: "https://file5s.ratemyserver.net/mobs/1429.gif",
}
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
MAX_BYTES = 2 * 1024 * 1024

def get(obj, name, default=None):
    return next((v for k, v in obj.items() if k.lower() == name.lower()), default)

def catalog_assets():
    result = set()
    spring_ids = set()
    additional_maps = set()
    sprites = {}
    for path in ROOT.glob("Data/monsters.*.json"):
        version = "SpringRO" if path.name == "monsters.springro.json" else "RTM"
        for monster in json.loads(path.read_text(encoding="utf-8-sig")):
            monster_id = get(monster, "Id")
            if isinstance(monster_id, int) and monster_id > 0:
                sprites[(version, monster_id)] = (
                    (SPRING_SPRITE.format(monster_id), TOC_SPRITE.format(monster_id))
                    if version == "SpringRO" else (TOC_SPRITE.format(monster_id),))
            target = result if get(monster, "IsMvp", True) else additional_maps
            target.update(get(spawn, "Code", "") for spawn in get(monster, "Spawns", []))
            if not get(monster, "IsMvp", True):
                continue
            if version == "SpringRO":
                spring_ids.add(monster_id)
    spring = json.loads((ROOT / "Data/database.springro.json").read_text(encoding="utf-8-sig"))
    for monster in get(spring, "Monsters", []):
        target = result if get(monster, "Id") in spring_ids else additional_maps
        target.update(get(spawn, "Code", "") for spawn in get(monster, "Spawns", []))
    with sqlite3.connect((ROOT / "Data/database.toc.sqlite").resolve().as_uri() + "?mode=ro", uri=True) as db:
        for monster_id, is_mvp, payload in db.execute("SELECT id,is_mvp,payload FROM monsters"):
            record = json.loads(zlib.decompress(payload))
            target = result if is_mvp else additional_maps
            target.update(spawn["map_name"] for spawn in record.get("spawns", []))
            urls = [TOC_SPRITE.format(monster_id)]
            if monster_id in TOC_SPRITE_FALLBACK:
                urls.append(TOC_SPRITE_FALLBACK[monster_id])
            sprites[("ToC", monster_id)] = urls
        for (payload,) in db.execute("SELECT payload FROM mvp_respawns"):
            result.add(json.loads(zlib.decompress(payload))["map_code"])
    def normalize(codes):
        return {str(code).strip().lower() for code in codes if str(code).strip()}
    return normalize(result), normalize(additional_maps), sprites

def image_extension(data):
    if 24 <= len(data) <= MAX_BYTES and data.startswith(PNG_SIGNATURE):
        width, height = struct.unpack(">II", data[16:24])
        if 2 <= width <= 4096 and 2 <= height <= 4096:
            return ".png"
    if 14 <= len(data) <= MAX_BYTES and data[:6] in (b"GIF87a", b"GIF89a"):
        width, height = struct.unpack("<HH", data[6:10])
        if 2 <= width <= 4096 and 2 <= height <= 4096:
            return ".gif"
    return None

def fetch(urls, allowed_extensions):
    last = ""
    for url in urls:
        for attempt in range(2):
            try:
                request = urllib.request.Request(url, headers={"User-Agent": "RoAura timer asset preparation"})
                with urllib.request.urlopen(request, timeout=10) as response:
                    data = response.read(MAX_BYTES + 1)
                extension = image_extension(data)
                if extension not in allowed_extensions:
                    raise ValueError("invalid image response")
                return data, extension, url
            except urllib.error.HTTPError as exc:
                last = str(exc)
                if exc.code in (403, 404, 410):
                    break
                time.sleep(0.25)
            except Exception as exc:
                last = str(exc)
                time.sleep(0.25)
    return None, None, last

def download(code):
    if not re.fullmatch(r"[a-z0-9_-]{1,64}", code):
        return code, None, "invalid code"
    data, _, source = fetch((source.format(code) for source in SOURCES), {".png"})
    return code, data, source

def download_sprite(entry):
    (version, monster_id), urls = entry
    data, extension, source = fetch(urls, {".png", ".gif"})
    return version, monster_id, data, extension, source

def main():
    output = ROOT / "Data/Minimaps"
    output.mkdir(parents=True, exist_ok=True)
    required, additional, sprites = catalog_assets()
    expected = sorted(required | additional)
    manifest, missing = {}, {}
    with concurrent.futures.ThreadPoolExecutor(max_workers=10) as pool:
        for code, data, error in pool.map(download, expected):
            if data is None:
                missing[code] = error
                print("MVP MAP MISSING:", code, error)
                continue
            (output / (code + ".png")).write_bytes(data)
            manifest[code] = dict(sha256=hashlib.sha256(data).hexdigest(), source=error)
    (output / "manifest.json").write_text(json.dumps(dict(sources=SOURCES, images=manifest, missing=missing), indent=2), encoding="utf-8")
    (output / "SOURCES.txt").write_text("Ragnarok Online minimaps. Game assets: Gravity Co.\nPer-file source URLs and SHA256 are recorded in manifest.json.\n", encoding="utf-8")
    print(f"TIMER MAP BUNDLE: {len(manifest)}/{len(expected)} exact-code images", flush=True)
    if required.intersection(missing):
        raise SystemExit("Missing MVP minimaps: " + ", ".join(sorted(required.intersection(missing))))

    output = ROOT / "Data/TimerMonsters"
    output.mkdir(parents=True, exist_ok=True)
    manifest, missing = {}, {}
    with concurrent.futures.ThreadPoolExecutor(max_workers=10) as pool:
        for version, monster_id, data, extension, source in pool.map(download_sprite, sorted(sprites.items())):
            key = f"{version}/{monster_id}"
            if data is None:
                missing[key] = source
                continue
            path = output / (key + extension)
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
            manifest[key] = dict(file=key + extension, sha256=hashlib.sha256(data).hexdigest(), source=source)
    (output / "manifest.json").write_text(json.dumps(dict(images=manifest, missing=missing), indent=2), encoding="utf-8")
    (output / "SOURCES.txt").write_text("Ragnarok Online monster sprites. Game assets: Gravity Co.\nPer-file source URLs and SHA256 are recorded in manifest.json.\n", encoding="utf-8")
    print(f"TIMER SPRITE BUNDLE: {len(manifest)}/{len(sprites)} images")

if __name__ == "__main__":
    main()
