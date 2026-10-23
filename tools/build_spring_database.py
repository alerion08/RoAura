#!/usr/bin/env python3
"""Build the offline linked SpringRO database used by the in-game overlay."""
import concurrent.futures
import json
import pathlib
import re
import urllib.request
from lxml import html

ROOT = pathlib.Path(__file__).resolve().parents[1]
UA = {"User-Agent": "RoAura database builder/1.0"}


def text(node):
    return " ".join(node.text_content().split())


def section(tree, title):
    found = tree.xpath(f'//h2[normalize-space()="{title}"]')
    return found[0].getparent().getparent() if found else None


def fetch_monster(entry):
    url = f"https://springro.com/database/mobs/{entry['Id']}"
    try:
        request = urllib.request.Request(url, headers=UA)
        with urllib.request.urlopen(request, timeout=30) as response:
            raw = response.read().replace(b"\0", b"")
        tree = html.fromstring(raw)
        stats_node = section(tree, "Stats")
        stats = {}
        if stats_node is not None:
            for row in stats_node.xpath('.//div[dt and dd]'):
                key = " ".join(row.xpath('./dt//text()')).strip()
                value = " ".join(row.xpath('./dd//text()')).strip()
                if key:
                    stats[key] = value

        spawns = []
        spawn_node = section(tree, "Spawns")
        if spawn_node is not None:
            for row in spawn_node.xpath('.//tbody/tr'):
                link = row.xpath('.//a[contains(@href,"/database/maps/")]')
                cells = row.xpath('./td')
                if not link or len(cells) < 3:
                    continue
                code = link[0].get('href', '').rsplit('/', 1)[-1]
                lines = [" ".join(s.split()) for s in link[0].xpath('.//text()') if " ".join(s.split())]
                name = next((s for s in lines if s != code), code)
                spawns.append({"Code": code, "Name": name, "Count": text(cells[1]), "Respawn": text(cells[2])})

        drops = []
        for title, is_mvp in (("Drops", False), ("MVP Drops", True)):
            drop_node = section(tree, title)
            if drop_node is None:
                continue
            for link in drop_node.xpath('.//a[contains(@href,"/database/items/")]'):
                item_id = int(link.get('href').rsplit('/', 1)[-1])
                parent_text = text(link.getparent())
                rate = re.search(r'(\d+(?:\.\d+)?)\s*%', parent_text)
                drops.append({"ItemId": item_id, "Name": text(link), "Rate": rate.group(1) + "%" if rate else "", "IsMvp": is_mvp})

        entry.update({
            "Level": stats.get("Level", ""), "Hp": stats.get("HP", ""),
            "Race": stats.get("Race", ""), "Element": stats.get("Property", ""),
            "Size": stats.get("Size", ""), "Attack": stats.get("Attack", ""),
            "Defense": stats.get("Defense", ""), "MagicDefense": stats.get("Magic Defense", ""),
            "BaseExp": stats.get("Base EXP", ""), "JobExp": stats.get("Job EXP", ""),
            "Drops": drops, "Spawns": spawns,
            "ImageUrl": f"https://pub-36f18a13614d49f0997ef23bce857ec4.r2.dev/sprites/mobs/{entry['Id']}.png"
        })
        return entry
    except Exception as exc:
        entry["LoadError"] = str(exc)
        entry.setdefault("Drops", [])
        entry.setdefault("Spawns", [])
        return entry


def main():
    items = json.loads((ROOT / "Data/items.springro.json").read_text(encoding="utf-8-sig"))
    monsters = json.loads((ROOT / "Data/monsters.springro.json").read_text(encoding="utf-8-sig"))
    with concurrent.futures.ThreadPoolExecutor(max_workers=16) as pool:
        monsters = list(pool.map(fetch_monster, monsters))

    item_by_id = {item["Id"]: item for item in items}
    maps = {}
    for monster in monsters:
        for drop in monster.get("Drops", []):
            item = item_by_id.get(drop["ItemId"])
            if item is not None:
                item.setdefault("DroppedBy", []).append({
                    "MonsterId": monster["Id"], "Name": monster["Name"],
                    "Rate": drop["Rate"], "IsMvp": drop["IsMvp"]
                })
        for spawn in monster.get("Spawns", []):
            current = maps.setdefault(spawn["Code"], {"Code": spawn["Code"], "Name": spawn["Name"], "Monsters": []})
            current["Monsters"].append({
                "MonsterId": monster["Id"], "Name": monster["Name"],
                "Count": spawn["Count"], "Respawn": spawn["Respawn"]
            })

    payload = {"Source": "SpringRO", "GeneratedUtc": __import__('datetime').datetime.now(__import__('datetime').timezone.utc).isoformat(),
               "Monsters": monsters, "Items": items, "Maps": sorted(maps.values(), key=lambda x: x["Name"])}
    output = ROOT / "Data/database.springro.json"
    output.write_text(json.dumps(payload, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"Wrote {output}: {len(monsters)} monsters, {len(items)} items, {len(maps)} maps")


if __name__ == "__main__":
    main()
