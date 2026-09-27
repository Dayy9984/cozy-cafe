"""Run actual Unity contract tests. Missing game/engine is a failure, not PASS.

v0.8.1 engine decision (see DECISIONS.md): Unity replaces the packet's Godot
MVP default per explicit user directive 2026-09-27. The harness contract is
unchanged: this adapter must emit 'CASE\\t<key>\\t<json>' lines produced by the
real game modules and exit 0 only after they ran; a missing engine or project
is BLOCKED/NOT_IMPLEMENTED, never a pass.

Unity-side contract the game must implement:
  * Unity project at <workspace>/game (ProjectSettings/ProjectVersion.txt).
  * Editor entry point `CozyCafe.Editor.GauntletEntry.Run` (static void):
      env GAUNTLET_STAGE    gate stage id to evaluate
      env GAUNTLET_RESULTS  absolute path; entry writes 'CASE\\tkey\\tjson' lines
      the process must exit nonzero on internal error via EditorApplication.Exit
"""
from pathlib import Path
import argparse, glob, os, shutil, subprocess, sys, tempfile

ROOT = Path(__file__).resolve().parents[1]
ENTRY_METHOD = "CozyCafe.Editor.GauntletEntry.Run"


def find_unity():
    cand = os.environ.get("UNITY_BIN")
    if cand:
        return cand if Path(cand).is_file() else None
    pats = [
        "/Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity",
        os.path.expanduser("~/Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity"),
        os.path.expanduser("~/Unity/Editor/*/Unity.app/Contents/MacOS/Unity"),
        "C:/Program Files/Unity/Hub/Editor/*/Editor/Unity.exe",
        "C:/Program Files (x86)/Unity/Hub/Editor/*/Editor/Unity.exe",
    ]
    hits = []
    for pat in pats:
        hits.extend(glob.glob(pat))
    if hits:
        return sorted(hits)[-1]
    return shutil.which("unity") or shutil.which("Unity")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("stage")
    a = p.parse_args()
    engine = find_unity()
    if not engine:
        print("BLOCKED: Unity editor not installed; install Unity or set UNITY_BIN")
        return 2
    game = ROOT / "game"
    has_project = (game / "ProjectSettings" / "ProjectVersion.txt").is_file()
    entries = list(game.glob("Assets/**/Editor/GauntletEntry.cs")) + list(
        game.glob("Assets/**/GauntletEntry.Editor.cs"))
    if not has_project or not entries:
        print("NOT_IMPLEMENTED: Unity project under game/ and an Editor/GauntletEntry.cs "
              "entry point are required")
        return 2
    fd, results_path = tempfile.mkstemp(prefix="gauntlet-cases-", suffix=".txt")
    os.close(fd)
    fd, log_path = tempfile.mkstemp(prefix="gauntlet-unity-log-", suffix=".txt")
    os.close(fd)
    env = os.environ.copy()
    env["GAUNTLET_STAGE"] = a.stage
    env["GAUNTLET_RESULTS"] = results_path
    cmd = [engine, "-batchmode", "-projectPath", str(game), "-executeMethod",
           ENTRY_METHOD, "-quit", "-logFile", log_path]
    print("UNITY_BIN:", engine)
    print("ARGV:", cmd)
    try:
        proc = subprocess.run(cmd, cwd=ROOT, env=env, capture_output=True, text=True,
                              timeout=1800)
        rc = proc.returncode
    except subprocess.TimeoutExpired:
        print("Unity run timed out (1800s)")
        return 2
    try:
        log_text = Path(log_path).read_text(encoding="utf-8", errors="replace")
    except OSError:
        log_text = ""
    print("=== UNITY LOG TAIL ===")
    print("\n".join(log_text.splitlines()[-120:]))
    results = Path(results_path)
    if results.is_file() and results.stat().st_size:
        print("=== CASE RESULTS ===")
        for line in results.read_text(encoding="utf-8").splitlines():
            if line.startswith("CASE\t"):
                print(line)
        if rc != 0:
            return rc
        return 0
    print("NOT_IMPLEMENTED: entry point produced no CASE lines")
    return 2 if rc == 0 else rc


if __name__ == "__main__":
    raise SystemExit(main())
