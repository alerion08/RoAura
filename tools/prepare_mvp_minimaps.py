#!/usr/bin/env python3
"""Bundle the exact minimap codes used by the pinned MVP catalogs; no guessed substitutions."""
import concurrent.futures
import hashlib
import json
import pathlib
import re
import sqlite3
import time
import urllib.request
import zlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
SOURCE = "https://pub-36f18a13614d49f0997ef23bce857ec4.r2.dev/sprites/minimaps/{}.png"

def get(obj, name, default=None):
    return next((v for k, v in obj.items() if k.lower() == name.lower()), default)

def codes():
    result = set()
    spring_ids = set()
    for path in ROOT.glob("Data/monsters.*.json"):
        for monster in json.loads(path.read_text(encoding="utf-8-sig")):
            if not get(monster, "IsMvp", True):
                continue
            if path.name == "monsters.springro.json":
                spring_ids.add(get(monster, "Id"))
            result.update(get(spawn, "Code", "") for spawn in get(monster, "Spawns", []))
    spring = json.loads((ROOT / "Data/database.springro.json").read_text(encoding="utf-8-sig"))
    for monster in get(spring, "Monsters", []):
        if get(monster, "Id") in spring_ids:
            result.update(get(spawn, "Code", "") for spawn in get(monster, "Spawns", []))
    with sqlite3.connect((ROOT / "Data/database.toc.sqlite").resolve().as_uri() + "?mode=ro", uri=True) as db:
        for (payload,) in db.execute("SELECT monsters.payload FROM monsters JOIN mvp ON mvp.id=monsters.id"):
            record = json.loads(zlib.decompress(payload))
            result.update(spawn["map_name"] for spawn in record.get("spawns", []))
        for (payload,) in db.execute("SELECT payload FROM mvp_respawns"):
            result.add(json.loads(zlib.decompress(payload))["map_code"])
    return sorted({str(code).strip().lower() for code in result if str(code).strip()})

def download(code):
    if not re.fullmatch(r"[a-z0-9_]{1,64}", code):
        return code, None, "invalid code"
    last = ""
    for attempt in range(3):
        try:
            with urllib.request.urlopen(SOURCE.format(code), timeout=15) as response:
                data = response.read(2 * 1024 * 1024 + 1)
            if not data.startswith(b"\x89PNG\r\n\x1a\n") or len(data) > 2 * 1024 * 1024:
                raise ValueError("invalid PNG response")
            return code, data, ""
        except Exception as exc:
            last = str(exc)
            if attempt < 2:
                time.sleep(0.2)
    return code, None, last

def main():
    output = ROOT / "Data/Minimaps"
    output.mkdir(parents=True, exist_ok=True)
    expected = codes()
    manifest, missing = {}, {}
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
        for code, data, error in pool.map(download, expected):
            if data is None:
                missing[code] = error
                print("MVP MAP MISSING:", code, error)
                continue
            (output / (code + ".png")).write_bytes(data)
            manifest[code] = hashlib.sha256(data).hexdigest()
    (output / "manifest.json").write_text(json.dumps(dict(source=SOURCE, images=manifest, missing=missing), indent=2), encoding="utf-8")
    print(f"MVP MAP BUNDLE: {len(manifest)}/{len(expected)} exact-code images")
    if missing:
        raise SystemExit("Missing MVP minimaps: " + ", ".join(missing))

if __name__ == "__main__":
    main()
