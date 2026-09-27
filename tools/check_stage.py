#!/usr/bin/env python3
"""Verify a stage's CASE output against gauntlet/gates.json.

Runs tools/game_adapter.py <stage> — which executes the real compiled game
code (GameCli .NET host, or the Unity editor host when licensed) — collects
CASE<TAB>key<TAB>json lines from its stdout, and exits 0 iff every expected
key in gates.json[stage] is present and equal to the emitted value.

CASE text from any source other than the compiled game core never satisfies
the gate: this script only trusts lines the adapter's real process produced.
"""
import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ADAPTER = ROOT / "tools" / "game_adapter.py"
GATES = ROOT / "gauntlet" / "gates.json"


def main(argv):
    if len(argv) != 2:
        print("usage: check_stage.py <stage>", file=sys.stderr)
        return 2
    stage = argv[1]

    gates = json.loads(GATES.read_text(encoding="utf-8"))
    if stage not in gates:
        print("CHECK_FAIL %s: stage not present in %s" % (stage, GATES))
        return 2
    expected = gates[stage]

    try:
        proc = subprocess.run(
            [sys.executable, str(ADAPTER), stage],
            cwd=str(ROOT), capture_output=True, text=True, timeout=1800)
    except subprocess.TimeoutExpired:
        print("CHECK_FAIL %s: adapter timed out" % stage)
        return 2

    if proc.stdout:
        sys.stdout.write(proc.stdout)
        if not proc.stdout.endswith("\n"):
            sys.stdout.write("\n")
    if proc.stderr:
        sys.stderr.write(proc.stderr)

    emitted = {}
    for line in proc.stdout.splitlines():
        if not line.startswith("CASE\t"):
            continue
        parts = line.split("\t", 2)
        if len(parts) != 3:
            continue
        try:
            emitted[parts[1]] = json.loads(parts[2])
        except json.JSONDecodeError:
            print("CHECK_FAIL %s: unparseable CASE line %r" % (stage, line))
            return 2

    if proc.returncode != 0:
        print("CHECK_FAIL %s: adapter exited %d" % (stage, proc.returncode))
        return 2

    missing = [k for k in expected if k not in emitted]
    wrong = {k: (expected[k], emitted[k]) for k in expected
             if k in emitted and emitted[k] != expected[k]}
    for k in missing:
        print("  MISSING %s: expected %s, no CASE emitted"
              % (k, json.dumps(expected[k])))
    for k, (exp, got) in sorted(wrong.items()):
        print("  MISMATCH %s: expected %s, got %s"
              % (k, json.dumps(exp), json.dumps(got)))
    if missing or wrong:
        print("CHECK_FAIL %s: %d missing, %d mismatched"
              % (stage, len(missing), len(wrong)))
        return 1
    print("CHECK_OK %s: %d/%d gate keys match emitted CASE values"
          % (stage, len(expected), len(expected)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
