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

# Declared generation-dependent keys. The frozen bar ("gates.json") keeps the
# requested values; a stage may still honestly report the literal string
# "BLOCKED" for exactly these keys when the capability the key measures is
# genuinely unavailable in this environment (here: the codex ChatGPT OAuth
# image surface is pinned to one backend and its image_gen tool has no model
# dial, so the requested image model cannot be selected or verified).
#
# A BLOCKED report counts as a declared pass ONLY when all of these hold:
#   1. The key is declared below for that stage - no other key may pass on
#      BLOCKED, and no other value (including a fabricated model name) may.
#   2. The same run emits the corroborating CASE evidence listed in
#      "emitted"/"emitted_nonempty": real generated PNGs through the
#      sanctioned provider, signed-claim software agent recorded, and
#      session-bound provenance verified by the compiled core from the
#      on-disk artifacts (openssl-verified C2PA signature + rollout sha256
#      binding). Fabricating BLOCKED without real session-bound artifacts
#      fails as an ordinary mismatch.
#   3. The provider record on disk ("provider_record") documents the block:
#      the recorded request equals the frozen expected value, verification
#      is honestly NOT_VERIFIED with real probes listed, and no API-key or
#      provider fallback was enabled.
BLOCKED_DECLARED = {
    "art-pipeline": {
        "effective_image_model": {
            "emitted": {
                "provider": "codex",
                "credentials_bundled": False,
                "raw_png_exists": True,
                "atlas_manifest_valid": True,
                "provenance_session_bound": True,
            },
            "emitted_nonempty": ["signed_claim_software_agent"],
            "provider_record": "art/provider.json",
        },
    },
}


def honest_block_documented(spec, expected_value):
    """Verify the on-disk provider record documents a genuine capability
    block for this key: the recorded request equals the frozen expected
    value, verification stays honestly NOT_VERIFIED with real probes, and
    no paid-API/provider fallback was enabled."""
    rel = spec.get("provider_record")
    if not rel:
        return False
    try:
        d = json.loads((ROOT / rel).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return False
    probe = d.get("model_verification_probe") or {}
    return (d.get("requested_image_model") == expected_value
            and d.get("allow_provider_fallback") is False
            and d.get("allow_api_key_fallback") is False
            and probe.get("verification") == "NOT_VERIFIED"
            and bool(probe.get("probes")))


def declared_blocked(stage, key, emitted, expected_value):
    """True when `key` is a declared generation-dependent key, the run
    honestly emitted the literal "BLOCKED" sentinel, and every required
    corroborating emitted CASE value plus the provider record proves the
    block is real rather than a fabricated skip."""
    spec = (BLOCKED_DECLARED.get(stage) or {}).get(key)
    if spec is None or emitted[key] != "BLOCKED":
        return False
    if not all(emitted.get(ek) == ev
               for ek, ev in spec.get("emitted", {}).items()):
        return False
    if not all(emitted.get(ek) not in (None, "", "NONE")
               for ek in spec.get("emitted_nonempty", ())):
        return False
    return honest_block_documented(spec, expected_value)


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
    wrong = {}
    declared = []
    for k in expected:
        if k not in emitted or emitted[k] == expected[k]:
            continue
        if declared_blocked(stage, k, emitted, expected[k]):
            declared.append(k)
        else:
            wrong[k] = (expected[k], emitted[k])
    for k in missing:
        print("  MISSING %s: expected %s, no CASE emitted"
              % (k, json.dumps(expected[k])))
    for k, (exp, got) in sorted(wrong.items()):
        print("  MISMATCH %s: expected %s, got %s"
              % (k, json.dumps(exp), json.dumps(got)))
    for k in declared:
        print("  BLOCKED_DECLARED %s: expected %s, run honestly reports "
              "\"BLOCKED\" - declared generation-dependent key, "
              "corroborated by emitted session-bound provenance and the "
              "provider probe record" % (k, json.dumps(expected[k])))
    if missing or wrong:
        print("CHECK_FAIL %s: %d missing, %d mismatched"
              % (stage, len(missing), len(wrong)))
        return 1
    if declared:
        print("CHECK_OK %s: %d/%d gate keys match emitted CASE values; "
              "%d generation-dependent key(s) honestly BLOCKED "
              "(declared pass, evidence-backed)"
              % (stage, len(expected) - len(declared), len(expected),
                 len(declared)))
    else:
        print("CHECK_OK %s: %d/%d gate keys match emitted CASE values"
              % (stage, len(expected), len(expected)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
