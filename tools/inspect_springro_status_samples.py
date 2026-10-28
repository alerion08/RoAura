#!/usr/bin/env python3
"""Offline inspection of opt-in RoAura samples. Never modifies settings or learns IDs.
Usage: python tools/inspect_springro_status_samples.py debug.log debug.1.log
Original bytes stay in the input files; output omits AIDs and raw payloads.
"""
import argparse
import hashlib
import json
import struct
import sys
from pathlib import Path

MARKER = "SPRINGRO WIRE SAMPLE "


def inspect(sample):
    if sample.get("schema") != 1:
        raise ValueError("unsupported schema")
    status, option, skill = (bytes.fromhex(sample[key]) for key in ("statusHex", "optionHex", "skillHex"))
    own = sample["selfAid"]
    if not isinstance(own, int) or not 0 < own <= 0xFFFFFFFF:
        raise ValueError("invalid self identity")
    if len(status) not in (9, 29) or len(skill) != 17 or len(option) not in (0, 15):
        raise ValueError("invalid frame lengths")
    opcode, candidate, actor = struct.unpack_from("<HHI", status)
    if opcode != (0x0983 if len(status) == 29 else 0x0196):
        raise ValueError("wrong status opcode")
    if status[8] != (1 if opcode == 0x0983 else 0) or actor >> 16 != own >> 16:
        raise ValueError("invalid status shape")
    ack, skill_id = struct.unpack_from("<HH", skill)
    target, source = struct.unpack_from("<II", skill, 8)
    if ack != 0x09CB or skill_id not in (51, 138) or skill_id != sample["skillId"] or skill[16] != 1:
        raise ValueError("not a successful supported skill ack")
    if target != own or source != own:
        raise ValueError("ack is not self-targeted own skill")
    if skill_id == 138 and opcode != 0x0983:
        raise ValueError("unsupported Enchant Poison end correlation")
    if option and (opcode != 0x0196 or struct.unpack_from("<HI", option) != (0x0229, own)):
        raise ValueError("invalid option identity or position")
    if sample["skillOffset"] - sample["statusOffset"] != len(status) + len(option):
        raise ValueError("non-contiguous sample")
    duration = struct.unpack_from("<II", status, 9) if len(status) == 29 else (0, 0)
    # These are the CURRENT standard-layout interpretation, not recovered EFST.
    return {
        "skill": skill_id, "opcode": f"0x{opcode:04X}",
        "wireStatusCandidate": candidate, "standardActorMatchesSelf": actor == own,
        "totalMsCandidate": duration[0], "leftMsCandidate": duration[1],
        "sha256": hashlib.sha256(status + option + skill).hexdigest(),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("logs", nargs="+", type=Path)
    args = parser.parse_args()
    found, errors = 0, 0
    for path in args.logs:
        with path.open(encoding="utf-8-sig", errors="replace") as stream:
            for number, line in enumerate(stream, 1):
                if MARKER not in line:
                    continue
                try:
                    result = inspect(json.loads(line.split(MARKER, 1)[1]))
                    print(json.dumps(result))
                    found += 1
                except (ValueError, KeyError, TypeError, struct.error):
                    print(f"Invalid sample at line {number} in {path.name}", file=sys.stderr)
                    errors += 1
    print(f"Samples: {found}; invalid: {errors}; no Status ID decoder applied.", file=sys.stderr)
    return 1 if errors or not found else 0


if __name__ == "__main__":
    raise SystemExit(main())
