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
    internal error via EditorApplication.Exit. The editor always
    runs on a staged temp copy of game/ (stage_unity_project):
    Unity rewrites project files on open and the tracked tree
    must stay byte-identical.
"""
from pathlib import Path
import argparse, glob, os, re, shutil, subprocess, sys, tempfile

ROOT = Path(__file__).resolve().parents[1]
ENTRY_METHOD = "CozyCafe.Editor.GauntletEntry.Run"


def _execable_path(p):
    """Normalize a possibly Windows-form env path for POSIX subprocess
    exec under msys/cygwin hosts (drive-letter paths get forward
    slashes, which native Windows also accepts)."""
    if p and len(p) > 2 and p[1] == ":":
        return p.replace(chr(92), "/")
    return p


def find_dotnet():
    cand = os.environ.get("DOTNET_BIN")
    if cand:
        cand = _execable_path(cand)
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
        cand = _execable_path(cand)
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


_EDITOR_VERSION_RE = re.compile(r"(\d+\.\d+\.\d+[a-z]+\d+)")


def project_editor_version():
    """The m_EditorVersion the tracked Unity project is authored for."""
    pv = ROOT / "game" / "ProjectSettings" / "ProjectVersion.txt"
    try:
        m = _EDITOR_VERSION_RE.search(
            pv.read_text(encoding="utf-8", errors="replace"))
    except OSError:
        return None
    return m.group(1) if m else None


def unity_editor_version(engine):
    """Editor version embedded in a Hub-style install path, e.g.
    .../Unity/Hub/Editor/6000.6.3f1/Editor/Unity.exe."""
    hits = _EDITOR_VERSION_RE.findall(str(engine))
    return hits[-1] if hits else None


def unity_matches_project(engine):
    """True only when the found editor provably equals the project's
    m_EditorVersion. Launching any other version silently upgrades or
    downgrades tracked files under game/Packages and game/ProjectSettings
    in place, so callers must skip the Unity host instead of running it."""
    ev = unity_editor_version(engine)
    pv = project_editor_version()
    return bool(ev) and bool(pv) and ev == pv


def native_path(p):
    """Drive-letter form of p when this interpreter is an MSYS/Cygwin build.

    MSYS converts path-like argv entries for native children but leaves env
    var values untouched, so every path handed to Unity or .NET through the
    environment must be converted explicitly or the native process resolves
    it against the wrong root."""
    s = str(p)
    if sys.platform in ("cygwin", "msys"):
        try:
            out = subprocess.check_output(["cygpath", "-w", s],
                                          text=True).strip()
            if out:
                return out
        except (OSError, subprocess.CalledProcessError):
            pass
    return s


def stage_unity_project():
    """Copy the tracked Unity project into a throwaway temp dir.

    Returns (stage_root, staged_project). The editor rewrites project files
    on open - ProjectVersion.txt, Packages manifests/locks, ProjectSettings
    assets - so editor hosts must run on this copy; the committed tree stays
    byte-identical no matter what the editor does. Callers rmtree stage_root
    once the child process exits."""
    stage_root = Path(tempfile.mkdtemp(prefix="gauntlet-unity-proj-"))
    dst = stage_root / "game"
    src_s = os.path.normpath(str(ROOT / "game"))
    top_skip = {"Library", "Temp", "Logs", "obj", "UserSettings",
                ".vs", ".git", ".idea"}
    any_skip = {"bin", "obj"}

    def _ignore(dirpath, names):
        skip = set(any_skip)
        if os.path.normpath(dirpath) == src_s:
            skip |= top_skip
        return [n for n in names if n in skip]

    shutil.copytree(src_s, str(dst), ignore=_ignore)
    return stage_root, dst


def run_gamecli(stage):
    """Preferred host: real C# core executed by dotnet. Streams CASE stdout."""
    dotnet = find_dotnet()
    if not dotnet:
        print("BLOCKED: .NET SDK not found; install dotnet or set DOTNET_BIN")
        return 2
    env = os.environ.copy()
    env["COZYCAFE_MVP_JSON"] = native_path(ROOT / "data" / "mvp.json")
    cmd = [dotnet, "run", "-c", "Release", "--project",
           str(ROOT / "game" / "GameCli"), "--", "case", stage]
    print("DOTNET_BIN:", dotnet)
    print("ARGV:", cmd)
    try:
        proc = subprocess.run(cmd, cwd=ROOT, env=env, timeout=900)
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
    if not unity_matches_project(engine):
        print("BLOCKED: Unity editor %s (version %s) does not match the "
              "project's m_EditorVersion=%s; launching it would rewrite "
              "tracked project files in place"
              % (engine, unity_editor_version(engine),
                 project_editor_version()))
        return 2
    try:
        staged_root, game = stage_unity_project()
    except OSError as e:
        print("BLOCKED: could not stage Unity project copy: %s" % e)
        return 2
    fd, results_path = tempfile.mkstemp(prefix="gauntlet-cases-", suffix=".txt")
    os.close(fd)
    fd, log_path = tempfile.mkstemp(prefix="gauntlet-unity-log-", suffix=".txt")
    os.close(fd)
    env = os.environ.copy()
    env["GAUNTLET_STAGE"] = stage
    env["GAUNTLET_RESULTS"] = native_path(results_path)
    # The staged project lives outside the workspace; Unity also sets the
    # editor CWD to -projectPath, so ancestor-walking would never find
    # data/mvp.json. Point modules at the real data file explicitly.
    env["COZYCAFE_MVP_JSON"] = native_path(ROOT / "data" / "mvp.json")
    cmd = [engine, "-batchmode", "-projectPath", native_path(game),
           "-executeMethod", ENTRY_METHOD, "-quit",
           "-logFile", native_path(log_path)]
    print("UNITY_BIN:", engine)
    print("ARGV:", cmd)
    try:
        proc = subprocess.run(cmd, cwd=ROOT, env=env, capture_output=True, text=True,
                              timeout=1800)
        rc = proc.returncode
    except subprocess.TimeoutExpired:
        print("Unity run timed out (1800s)")
        return 2
    finally:
        shutil.rmtree(staged_root, ignore_errors=True)
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
