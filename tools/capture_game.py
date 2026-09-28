#!/usr/bin/env python3
"""Capture a real rendered PNG for a stage.

Host order:
  1. Unity editor host `CozyCafe.Editor.CaptureShot.Run` (real camera ->
     RenderTexture -> ReadPixels) when a licensed editor is installed and the
     project entry exists. Env: GAUNTLET_CAPTURE_STAGE / GAUNTLET_CAPTURE_OUTPUT.
     The Unity host is skipped when the found editor's version differs from
     ProjectSettings/ProjectVersion.txt m_EditorVersion, and it always runs
     on a staged temp copy of game/ (stage_unity_project): any editor open
     rewrites tracked project files in place, so the committed tree is
     evaluated byte-identical and never mutated.
  2. GameCli `render <stage> <abs.png>` which software-rasterizes the same
     shared game state — for machines without an editor license.

Exits nonzero if neither produces a valid non-empty PNG. No image is ever
fabricated: BLOCKED is reported instead.
"""
import argparse
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from game_adapter import (find_dotnet, find_unity,  # noqa: E402
                          native_path, project_editor_version,
                          stage_unity_project, unity_editor_version,
                          unity_matches_project)

CAPTURE_METHOD = "CozyCafe.Editor.CaptureShot.Run"
PNG_MAGIC = b"\x89PNG\r\n\x1a\n"


def valid_png(path):
    try:
        with open(path, "rb") as f:
            head = f.read(8)
        return head == PNG_MAGIC and os.path.getsize(path) > 64
    except OSError:
        return False


def try_unity(stage, output):
    engine = find_unity()
    if not engine:
        print("capture: Unity editor not found; skipping Unity host")
        return False
    if not unity_matches_project(engine):
        print("capture: Unity %s (version %s) does not match project "
              "m_EditorVersion=%s; skipping Unity host (it would rewrite "
              "tracked project files)"
              % (engine, unity_editor_version(engine),
                 project_editor_version()))
        return False
    if not list((ROOT / "game").glob("Assets/**/Editor/CaptureShot.cs")):
        print("capture: Editor/CaptureShot.cs not present; skipping Unity host")
        return False
    # Unity rewrites project files on open; run it on a staged copy so the
    # tracked tree stays exactly as committed.
    try:
        staged_root, staged_game = stage_unity_project()
    except OSError as e:
        print("capture: could not stage Unity project copy: %s" % e)
        return False
    env = os.environ.copy()
    env["GAUNTLET_CAPTURE_STAGE"] = stage
    env["GAUNTLET_CAPTURE_OUTPUT"] = native_path(output)
    # Staged copy lives outside the workspace and Unity chdirs into it;
    # without these the modules' ancestor search for data/mvp.json and
    # art/provider.json (approved atlas + provenance) fails.
    env["COZYCAFE_MVP_JSON"] = native_path(ROOT / "data" / "mvp.json")
    env["COZYCAFE_WORKSPACE_ROOT"] = native_path(ROOT)
    fd, log_path = tempfile.mkstemp(prefix="gauntlet-capture-log-", suffix=".txt")
    os.close(fd)
    cmd = [engine, "-batchmode", "-projectPath", native_path(staged_game),
           "-executeMethod", CAPTURE_METHOD, "-quit",
           "-logFile", native_path(log_path)]
    print("capture: UNITY_ARGV %s" % cmd)
    try:
        rc = subprocess.run(cmd, cwd=str(ROOT), env=env, timeout=1800).returncode
    except subprocess.TimeoutExpired:
        print("capture: Unity host timed out (log %s)" % log_path)
        return False
    finally:
        shutil.rmtree(staged_root, ignore_errors=True)
    if rc == 0 and valid_png(output):
        return True
    print("capture: Unity host failed rc=%d (log %s)" % (rc, log_path))
    return False


def try_gamecli(stage, output):
    dotnet = find_dotnet()
    if not dotnet:
        print("capture: .NET SDK not found; skipping GameCli host")
        return False
    env = os.environ.copy()
    env["COZYCAFE_MVP_JSON"] = native_path(ROOT / "data" / "mvp.json")
    env["COZYCAFE_WORKSPACE_ROOT"] = native_path(ROOT)
    cmd = [dotnet, "run", "-c", "Release", "--project",
           str(ROOT / "game" / "GameCli"), "--", "render", stage,
           native_path(output)]
    print("capture: DOTNET_ARGV %s" % cmd)
    try:
        rc = subprocess.run(cmd, cwd=str(ROOT), env=env, timeout=900).returncode
    except subprocess.TimeoutExpired:
        print("capture: GameCli render timed out")
        return False
    if rc == 0 and valid_png(output):
        return True
    print("capture: GameCli render failed rc=%d" % rc)
    return False


def resolve_output(arg):
    """Absolute output path on whichever flavor of python runs the harness.
    Native Windows python already treats a drive-letter path as absolute;
    cygwin/msys flavors translate X:/... to the mounted /x/... form so file
    I/O and child-process argv resolve the same file."""
    p = str(arg)
    if Path(p).is_absolute():
        return Path(p)
    if (len(p) > 2 and p[0].isalpha() and p[1] == ":"
            and p[2] in ("/", chr(92)) and os.name == "posix"
            and sys.platform.startswith(("cygwin", "msys"))):
        mp = Path("/" + p[0].lower() + p[2:].replace(chr(92), "/"))
        if mp.parent.exists() or mp.parent.parent.exists():
            return mp
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--stage", required=True)
    ap.add_argument("--output", required=True)
    a = ap.parse_args()

    output = resolve_output(a.output)
    if output is None:
        print("capture: --output must be an absolute path", file=sys.stderr)
        return 2
    output.parent.mkdir(parents=True, exist_ok=True)

    if try_unity(a.stage, output):
        print("capture: wrote %s via Unity CaptureShot" % output)
        return 0
    if try_gamecli(a.stage, output):
        print("capture: wrote %s via GameCli render" % output)
        return 0
    print("capture: BLOCKED — no working capture host for stage %s" % a.stage)
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
