"""Align private RoAura wire samples with read-only SpringRO client-buffer samples.

Usage: python tools/compare_springro_status_views.py debug.log SpringRoClientStatus.jsonl
No captured bytes are written to disk by this tool.
"""

import argparse
import datetime as dt
import json
from pathlib import Path


MARKER = "SPRINGRO WIRE SAMPLE "


def time(value):
    return dt.datetime.fromisoformat(value.replace("Z", "+00:00"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("debug_log", type=Path)
    parser.add_argument("client_jsonl", type=Path)
    args = parser.parse_args()
    wire = []
    for line in args.debug_log.read_text(encoding="utf-8-sig").splitlines():
        if MARKER in line:
            item = json.loads(line.split(MARKER, 1)[1])
            wire.append((time(item["observedAtUtc"]), bytes.fromhex(item["statusHex"])))
    client = []
    for line in args.client_jsonl.read_text(encoding="utf-8-sig").splitlines():
        item = json.loads(line)
        if item["opcode"] == "0x0983":
            client.append((time(item["observedAtUtc"]), bytes.fromhex(item["hex"])))

    used = set()
    for observed, decoded in client:
        matches = [(abs((observed - at).total_seconds()), index, raw)
                   for index, (at, raw) in enumerate(wire)
                   if index not in used and len(raw) == len(decoded)
                   and raw[:2] == decoded[:2] and raw[6:] == decoded[6:]]
        matches.sort()
        if not matches or matches[0][0] > 0.05 or (len(matches) > 1 and matches[1][0] <= 0.05):
            print(f"{observed.isoformat()}: no unique wire match within 50 ms")
            continue
        delta, index, raw = matches[0]
        used.add(index)
        print(f"{observed.isoformat()} delta={delta * 1000:.3f}ms "
              f"wire[2:6]={raw[2:6].hex().upper()} "
              f"client[2:6]={decoded[2:6].hex().upper()} "
              f"status={int.from_bytes(decoded[2:4], 'little')} "
              f"actor={int.from_bytes(decoded[4:8], 'little')}")
    print(f"Matched {len(used)}/{len(client)} client frames; {len(wire)} wire samples available")


if __name__ == "__main__":
    main()
