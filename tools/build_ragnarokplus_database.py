"""Convert rendered Ragnarok+ catalog rows to RoAura's catalog JSON."""

import argparse
import json
import re
from pathlib import Path


def item_id(label):
    if not re.fullmatch(r"#[1-9][0-9]*", label):
        raise ValueError(f"Invalid item or monster ID {label!r}")
    return int(label[1:])


def parse_item(row):
    name, category, identifier = row
    return {"id": item_id(identifier), "name": name, "type": category.title()}


def parse_monster(row):
    name, *fields = row
    flags = []
    while fields and fields[0] in {"MVP", "BOSS", "Boss"}:
        flags.append(fields.pop(0).upper())
    level, race, element, size, base_label, base_exp, job_label, job_exp, identifier = fields
    if not re.fullmatch(r"LV [0-9]+", level) or (base_label, job_label) != ("B", "J"):
        raise ValueError("Unexpected level or experience labels")
    return {
        "id": item_id(identifier), "name": name, "level": level[3:],
        "race": race.title(), "element": element.title(), "size": size.title(),
        "baseExp": str(int(base_exp.replace(",", ""))),
        "jobExp": str(int(job_exp.replace(",", ""))),
        "isMvp": "MVP" in flags, "isBoss": bool(flags),
    }


def parse_map(row):
    name, code = row
    if not re.fullmatch(r"[a-zA-Z0-9_-]+", code):
        raise ValueError(f"Invalid map code {code!r}")
    return {"code": code.lower(), "name": name}


def merge_rows(existing, rows, parser, key):
    records = {record[key]: record for record in existing}
    imported = {}
    for index, row in enumerate(rows, 1):
        try:
            if not isinstance(row, list) or not all(isinstance(value, str) and value.strip() for value in row):
                raise ValueError("Expected an array of nonempty text lines")
            record = parser([value.strip() for value in row])
            identity = record[key]
            if identity in imported and imported[identity] != record:
                raise ValueError(f"Conflicting records for {identity}")
            imported[identity] = record
        except ValueError as error:
            raise ValueError(f"Row {index}: {error}") from error
    for identity, record in imported.items():
        records[identity] = {**records.get(identity, {}), **record}
    return [records[identity] for identity in sorted(records)]


def build(document, base):
    parsers = {
        "items": (parse_item, "id"),
        "monsters": (parse_monster, "id"),
        "maps": (parse_map, "code"),
    }
    if set(document) - parsers.keys():
        raise ValueError("Expected items, monsters or maps")
    if base.get("source", "Ragnarok+") != "Ragnarok+":
        raise ValueError("The base must be a Ragnarok+ catalog")
    result = {"source": "Ragnarok+"}
    for section, (parser, key) in parsers.items():
        try:
            result[section] = merge_rows(base.get(section, []), document.get(section, []), parser, key)
        except ValueError as error:
            raise ValueError(f"{section}: {error}") from error
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("rows", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--base", type=Path)
    args = parser.parse_args()
    try:
        document = json.loads(args.rows.read_text(encoding="utf-8"))
        base = json.loads(args.base.read_text(encoding="utf-8")) if args.base else {}
        result = build(document, base)
    except (OSError, TypeError, ValueError) as error:
        parser.error(str(error))
    args.output.write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
