"""Convert a Ragnarok+ collector SQLite file into the RoAura quest catalog."""
import argparse
import gzip
import json
import re
import sqlite3
from pathlib import Path

def card_id(path, kind):
    match = re.fullmatch(r"/" + kind + r"-db/(\d+)", path)
    return int(match[1]) if match else None

def clean_label(label):
    return re.sub(r"^\s*\d[\d, ]*\s*[x×]\s*", "", label.split("\n")[0]).strip()

def export(source, output):
    with sqlite3.connect(source.resolve().as_uri() + "?mode=ro", uri=True) as db:
        if db.execute("PRAGMA integrity_check").fetchone() != ("ok",) or db.execute("PRAGMA foreign_key_check").fetchone():
            raise ValueError("SQLite integrity checks failed")
        cards = []
        for path, kind, title, payload in db.execute("SELECT path,kind,title,payload FROM card ORDER BY path"):
            data = json.loads(payload)
            uses = {}
            for step, item_id, role, quantity, label in db.execute(
                    "SELECT step,item_id,role,quantity,label FROM item_use WHERE path=? ORDER BY step,item_id,role", (path,)):
                uses.setdefault(step, []).append(dict(id=item_id, role=role, quantity=quantity, name=clean_label(label)))
            reputation = []
            for rep in data["reputation"]:
                # Multi-faction cards provide only the first faction in the structured object.
                for faction, value, multiplier in re.findall(r"^([^\n]+? Rep)\s*\n\+(\d+)\s*(x\d+)?", rep["text"], re.M):
                    reputation.append(dict(faction=faction.removesuffix(" Rep"), value=int(value), multiplier=multiplier, mode=rep["mode"]))
            steps = data["steps"] if kind == "quest" else [dict(
                ordinal=1, title=title, description=data["text"], maps=data["maps"],
                links=data["links"], images=data["images"])]
            rows = []
            for step in steps:
                ordinal = step["ordinal"]
                maps = step["maps"] or (data["maps"] if kind == "hunting" else [])
                pins = [dict(map=map_["code"], x=pin["x"], y=pin["y"], name=pin["name"])
                        for map_ in maps for pin in map_["pins"] if pin["x"] is not None and pin["y"] is not None]
                mobs = []
                for link in data["targets"] if kind == "hunting" else step["links"]:
                    monster_id = card_id(link["path"], "mob")
                    if monster_id is not None:
                        mobs.append(dict(id=monster_id, name=clean_label(link["label"]), count=link.get("count")))
                items = list(uses.get(ordinal, []))
                if kind == "hunting":
                    items.extend(uses.get(0, []))
                for link in step["links"]:
                    item_id = card_id(link["path"], "item")
                    if item_id is None:
                        continue
                    label_role = (link.get("role") or "").strip()
                    role = "bring" if re.match(r"^BRING\b", label_role, re.I) else "reference"
                    if not any(i["id"] == item_id and (role == "reference" or i["role"] == role) for i in items):
                        items.append(dict(id=item_id, role=role, quantity=None, name=clean_label(link["label"]),
                                          condition=label_role if role == "bring" else None))
                known_roles = {i["id"] for i in items if i["role"] != "reference"}
                items = [i for i in items if i["role"] != "reference" or i["id"] not in known_roles]
                image = next((m.get("image") for m in maps if m.get("image")), None)
                image = image or step.get("npc_image")
                text = step.get("description") or step.get("text") or title
                if kind == "hunting":
                    targets = ", ".join(f'{mob["name"]} {mob["count"] or ""}'.strip() for mob in mobs)
                    text = targets + "\n\n" + text if targets else text
                rows.append(dict(number=ordinal, title=step["title"], text=text,
                                 npc=step.get("npc"), points=pins, items=items, monsters=mobs, image=image))
            level = re.search(r"\bLv\. (\d+)\+", data["text"]) if kind == "hunting" else None
            cards.append(dict(path=path, kind=kind, title=title, level=int(level[1]) if level else None,
                              steps=rows, reputation=reputation, requirements=uses.get(0, [])))
    output.write_bytes(gzip.compress(json.dumps(cards, ensure_ascii=False, separators=(",", ":")).encode(), mtime=0))

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    export(args.source, args.output)
