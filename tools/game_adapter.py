"""Run actual contract tests against the real game code. Missing game/engine is a failure, not PASS.

v0.8.1 engine decision (see DECISIONS.md): Unity replaces the packet's Godot
MVP default per explicit user directive 2026-09-27. The harness contract is
unchanged: this adapter must emit 'CASE\\t<key>\\t<json>' lines produced by the
real game modules and exit 0 only after they ran; a missing engine or project
is BLOCKED/NOT_IMPLEMENTED, never a pass.

v0.8.2 dual-host decision (see DECISIONS.md): the game logic lives in a pure
C# core (game/Assets/CozyCafe/Core, no UnityEngine dependency) that is
compiled BOTH by Unity and by the .NET 8 console host at game/GameCli. The
GameCli host is preferred for gate CASE output because it needs no Unity
editor license and executes the identical core sources; the Unity
-executeMethod host below remains for machines where the editor+license are
installed and for native evidence.

Host contract (whichever runs must satisfy it):
  * Unity project at <workspace>/game (ProjectSettings/ProjectVersion.txt).
  * .NET host: game/GameCli/GameCli.csproj. Invocation:
      dotnet run -c Release --project game/GameCli -- case <stage>
    The program prints 'CASE\\tkey\\tjson' lines for that stage to stdout and
    exits 0 on success, nonzero on internal error. It must compile the same
    core sources Unity compiles (Compile Include glob over Assets/**/Core).
  * Unity host: Editor entry point `CozyCafe.Editor.GauntletEntry.Run`
    (static void) with env GAUNTLET_STAGE and GAUNTLET_RESULTS (absolute path
    the entry writes 'CASE\\tkey\\tjson' lines to); process exits nonzero on
    internal error via EditorApplication.Exit.
"""
from pathlib import Path
import argparse, glob, os, shutil, subprocess, sys, tempfile

ROOT = Path(__file__).resolve().parents[1]
ENTRY_METHOD = "CozyCafe.Editor.GauntletEntry.Run"


def find_dotnet():
    cand = os.environ.get("DOTNET_BIN")
    if cand:
        return cand if Path(cand).is_file() else None
    w = shutil.which("dotnet")
    if w:
        return w
    hits = []
    for pat in ("/opt/homebrew/opt/dotnet*/bin/dotnet",
                "/usr/local/share/dotnet/dotnet",
                os.path.expanduser("~/.dotnet/dotnet"),
                "C:/Program Files/dotnet/dotnet.exe"):
        hits.extend(glob.glob(pat))
    return sorted(hits)[-1] if hits else None


def find_unity():
    cand = os.environ.get("UNITY_BIN")
    if cand:
        return cand if Path(cand).is_file() else None
    hits = []
    for pat in (
        "/Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity",
        os.path.expanduser("~/Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity"),
        os.path.expanduser("~/Unity/Editor/*/Unity.app/Contents/MacOS/Unity"),
        "C:/Program Files/Unity/Hub/Editor/*/Editor/Unity.exe",
        "C:/Program Files (x86)/Unity/Hub/Editor/*/Editor/Unity.exe",
    ):
        hits.extend(glob.glob(pat))
    if hits:
        return sorted(hits)[-1]
    return shutil.which("unity") or shutil.which("Unity")


def run_gamecli(stage):
    """Preferred host: real C# core executed by dotnet. Streams CASE stdout."""
    dotnet = find_dotnet()
    if not dotnet:
        print("BLOCKED: .NET SDK not found; install dotnet or set DOTNET_BIN")
        return 2
    cmd = [dotnet, "run", "-c", "Release", "--project",
           str(ROOT / "game" / "GameCli"), "--", "case", stage]
    print("DOTNET_BIN:", dotnet)
    print("ARGV:", cmd)
    try:
        proc = subprocess.run(cmd, cwd=ROOT, timeout=900)
        rc = proc.returncode
    except subprocess.TimeoutExpired:
        print("GameCli run timed out (900s)")
        return 2
    return rc


def run_unity(stage):
    engine = find_unity()
    if not engine:
        print("BLOCKED: Unity editor not installed; install Unity or set UNITY_BIN")
        return 2
    game = ROOT / "game"
    fd, results_path = tempfile.mkstemp(prefix="gauntlet-cases-", suffix=".txt")
    os.close(fd)
    fd, log_path = tempfile.mkstemp(prefix="gauntlet-unity-log-", suffix=".txt")
    os.close(fd)
    env = os.environ.copy()
    env["GAUNTLET_STAGE"] = stage
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


def main():
    p = argparse.ArgumentParser()
    p.add_argument("stage")
    a = p.parse_args()
    game = ROOT / "game"
    has_unity_project = (game / "ProjectSettings" / "ProjectVersion.txt").is_file()
    has_cli = (game / "GameCli" / "GameCli.csproj").is_file()
    has_editor_entry = bool(
        list(game.glob("Assets/**/Editor/GauntletEntry.cs"))
        + list(game.glob("Assets/**/GauntletEntry.Editor.cs")))
    if has_cli:
        return run_gamecli(a.stage)
    if has_unity_project and has_editor_entry:
        return run_unity(a.stage)
    print("NOT_IMPLEMENTED: need game/GameCli/GameCli.csproj (preferred .NET host "
          "over the shared C# core) or a Unity project + Editor/GauntletEntry.cs")
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
