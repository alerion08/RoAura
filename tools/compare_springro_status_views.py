"""Align private RoAura wire samples with read-only SpringRO client-buffer samples.

Usage: python tools/compare_springro_status_views.py debug.log SpringRoClientStatus.jsonl
No captured bytes are written to disk by this tool.
"""

import argparse
import datetime as dt
import json
import re
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
    layout = []
    for line in args.debug_log.read_text(encoding="utf-8-sig").splitlines():
        if MARKER in line:
            item = json.loads(line.split(MARKER, 1)[1])
            wire.append((time(item["observedAtUtc"]), bytes.fromhex(item["statusHex"])))
        if "PACKET LAYOUT " in line and "opcode=0x" in line:
            match = re.search(r"^(\S+ \S+) PACKET LAYOUT .*?opcode=(0x[0-9A-F]+).*?status=(\d+); state=(\d+)", line)
            if match:
                local = dt.datetime.fromisoformat(match[1]).replace(tzinfo=dt.timezone(dt.timedelta(hours=6)))
                layout.append((local.astimezone(dt.timezone.utc), match[2], int(match[3]), int(match[4])))
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
    # Layout observations have rounded millisecond timestamps and no raw bytes.
    # They corroborate event order, but cannot establish a field transform.
    for line in args.client_jsonl.read_text(encoding="utf-8-sig").splitlines():
        item = json.loads(line)
        observed = time(item["observedAtUtc"])
        frame = bytes.fromhex(item["hex"])
        candidates = [(abs((observed - at).total_seconds()), status, state)
                      for at, opcode, status, state in layout if opcode == item["opcode"]]
        if candidates:
            delta, status, state = min(candidates)
            if delta <= 0.05:
                print(f"layout delta={delta * 1000:.3f}ms opcode={item['opcode']} "
                      f"wireParsedStatus={status} clientStatus={int.from_bytes(frame[2:4], 'little')} "
                      f"wireState={state} clientState={frame[8]}")


if __name__ == "__main__":
    main()
