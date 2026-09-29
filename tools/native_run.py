#!/usr/bin/env python3
"""Run the real native-release evidence path on this host.

windows: stage the tracked Unity project, build the standalone player
(carrying NativeOverlayScenario), compute its sha256, launch it windowed on
this desktop, let it drive the real OS paths (transparency, topmost,
click-through + tray/hotkey recovery, Korean IME, focus, DPI, monitors,
suspend/resume handling), then hash the raw run log + MP4 screen recording
it produced and write native/windows.json from the results.

macos: honestly records BLOCKED — this is a Windows host: it probes for a
macOS host, a Unity macOS playback module, and a dotnet osx apphost pack
and records the measured facts; it never claims a build it cannot run.

usage: python3 tools/native_run.py windows|macos|all
"""
import glob
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from game_adapter import (find_dotnet, find_unity, native_path,
                          stage_unity_project, unity_matches_project,
                          project_editor_version)

BUILD_METHOD = "CozyCafe.Editor.NativePlayerBuild.BuildWindows"
WIN_EXE = ROOT / "out" / "builds" / "windows" / "CozyCafe.exe"
NATIVE_DIR = ROOT / "native"
EVIDENCE = NATIVE_DIR / "evidence"


def sha256(path):
    import hashlib
    h = hashlib.sha256()
    with open(str(path), "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def sha256_bytes(data):
    import hashlib
    return hashlib.sha256(data).hexdigest()


def rel(p):
    return str(Path(p).resolve().relative_to(ROOT.resolve())).replace(
        os.sep, "/")


PACKAGE = {
    "windows": NATIVE_DIR / "build" / "windows",
    "macos": NATIVE_DIR / "build" / "macos",
}


def package_file(src, os_name):
    """Copy a recorded build artifact into the tracked native/build/<os>/
    tree. out/builds/ is gitignored build scratch: snapshots of this
    workspace only carry tracked files, so the report must point at the
    packaged copy for its sha256 to verify anywhere the report travels."""
    dst_dir = PACKAGE[os_name]
    dst_dir.mkdir(parents=True, exist_ok=True)
    dst = dst_dir / Path(src).name
    shutil.copy2(str(src), str(dst))
    return dst


def unity_build_windows():
    """Build the real standalone player via the editor entry point on a
    staged copy. Returns (exe_path, log_tail) or (None, log_tail)."""
    engine = find_unity()
    if not engine:
        return None, "unity editor not found"
    if not unity_matches_project(engine):
        return None, ("unity %s does not match project %s"
                      % (engine, project_editor_version()))
    staged_root, staged_game = stage_unity_project()
    fd, log_path = tempfile.mkstemp(prefix="native-build-log-",
                                    suffix=".txt")
    os.close(fd)
    env = os.environ.copy()
    env["GAUNTLET_NATIVE_BUILD_EXE"] = native_path(WIN_EXE)
    env["COZYCAFE_MVP_JSON"] = native_path(ROOT / "data" / "mvp.json")
    env["COZYCAFE_WORKSPACE_ROOT"] = native_path(ROOT)
    env["COZYCAFE_DATA_DIR"] = native_path(ROOT / "data")
    cmd = [engine, "-batchmode", "-projectPath", native_path(staged_game),
           "-executeMethod", BUILD_METHOD, "-quit",
           "-logFile", native_path(log_path)]
    print("BUILD_ARGV %s" % cmd)
    try:
        rc = subprocess.run(cmd, cwd=str(ROOT), env=env,
                            timeout=2400).returncode
    except subprocess.TimeoutExpired:
        return None, "unity build timed out"
    finally:
        shutil.rmtree(staged_root, ignore_errors=True)
    try:
        tail = "\n".join(Path(log_path).read_text(
            encoding="utf-8", errors="replace").splitlines()[-40:])
    except OSError:
        tail = ""
    if rc == 0 and WIN_EXE.is_file():
        return str(WIN_EXE), tail
    return None, "unity build rc=%d\n%s" % (rc, tail)

REQUIRED_CASES = [
    "transparency", "always_on_top", "click_through_recovery",
    "korean_ime", "focus", "dpi", "multi_monitor", "sleep",
]


def run_windows_player():
    """Launch the freshly built player windowed on this desktop. The player
    itself writes the raw run log and the MP4; returns (summary dict,
    duration_s, argv, timed_out)."""
    log_path = EVIDENCE / "windows-run.log"
    video_path = EVIDENCE / "windows-recording.mp4"
    summary_path = EVIDENCE / "windows-summary.json"
    tmp_dir = Path(tempfile.mkdtemp(prefix="cozycafe-native-"))
    EVIDENCE.mkdir(parents=True, exist_ok=True)
    for p in (log_path, video_path, summary_path):
        try:
            p.unlink()
        except OSError:
            pass
    env = os.environ.copy()
    env["GAUNTLET_NATIVE_LOG"] = native_path(log_path)
    env["GAUNTLET_NATIVE_VIDEO"] = native_path(video_path)
    env["GAUNTLET_NATIVE_SUMMARY"] = native_path(summary_path)
    env["GAUNTLET_NATIVE_TMP"] = native_path(tmp_dir)
    env["COZYCAFE_MVP_JSON"] = native_path(ROOT / "data" / "mvp.json")
    env["COZYCAFE_WORKSPACE_ROOT"] = native_path(ROOT)
    env["COZYCAFE_DATA_DIR"] = native_path(ROOT / "data")
    argv = [str(WIN_EXE), "-screen-fullscreen", "0",
            "-screen-width", "560", "-screen-height", "400"]
    print("RUN_ARGV %s" % argv)
    t0 = time.time()
    timed_out = False
    try:
        proc = subprocess.run(argv, cwd=str(ROOT), env=env, timeout=300,
                              capture_output=True, text=True)
        rc = proc.returncode
        out_tail = (proc.stdout or "")[-2000:]
        err_tail = (proc.stderr or "")[-2000:]
    except subprocess.TimeoutExpired:
        timed_out = True
        rc = -1
        out_tail = err_tail = "player run timed out at 300s"
    dur = time.time() - t0
    summary = {}
    try:
        summary = json.loads(summary_path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        pass
    meta = {"rc": rc, "timed_out": timed_out,
            "duration_s": round(dur, 1), "argv": argv,
            "stdout_tail": out_tail, "stderr_tail": err_tail}
    return summary, meta


def evidence_entry(p):
    return {"path": rel(p), "sha256": sha256(p),
            "bytes": p.stat().st_size}


def write_windows_report():
    build_path, build_log = unity_build_windows()
    report = {
        "os": "windows",
        "status": "FAIL",
        "recorded_utc": int(time.time()),
        "host": {
            "os": os.environ.get("OS", sys.platform),
            "unity": str(find_unity()),
            "editor_version": project_editor_version(),
        },
    }
    if not build_path:
        report["fail_reason"] = "windows build failed"
        report["build_log_tail"] = build_log
        write_report("windows", report)
        return report
    packaged = package_file(build_path, "windows")
    report["build"] = {
        "path": rel(packaged),
        "sha256": sha256(packaged),
        "bytes": packaged.stat().st_size,
        "kind": "unity-standalone-windows64",
        "executed_path": rel(build_path),
        "packaged_note": (
            "byte-identical copy of the exe that ran; the full player dir "
            "(UnityPlayer.dll, CozyCafe_Data) is reproduced by "
            "tools/native_run.py at out/builds/windows/"),
    }
    summary, meta = run_windows_player()
    # Raw log tail: orchestrator appends its own harness facts after the
    # player's own lines; the recorded hash covers this final file.
    log_path = EVIDENCE / "windows-run.log"
    with open(str(log_path), "a", encoding="utf-8") as f:
        f.write("\n=== orchestrator ===\n")
        f.write("argv: %s\n" % " ".join(meta["argv"]))
        f.write("build_sha256: %s\n" % report["build"]["sha256"])
        f.write("duration_s: %s rc=%s timed_out=%s\n"
                % (meta["duration_s"], meta["rc"], meta["timed_out"]))
        if meta["stderr_tail"]:
            f.write("stderr_tail: %s\n" % meta["stderr_tail"])
    video_path = EVIDENCE / "windows-recording.mp4"
    ev = []
    if log_path.is_file() and log_path.stat().st_size > 0:
        ev.append(evidence_entry(log_path))
    if video_path.is_file() and video_path.stat().st_size > 0:
        ev.append(evidence_entry(video_path))
    report["run"] = {"argv": meta["argv"], "duration_s": meta["duration_s"],
                     "rc": meta["rc"], "timed_out": meta["timed_out"]}
    report["cases"] = summary.get("cases", {})
    report["evidence"] = ev
    passed = sum(1 for c in REQUIRED_CASES
                 if (report["cases"].get(c) or {}).get("status") == "PASS")
    report["cases_passed"] = passed
    report["cases_required"] = len(REQUIRED_CASES)
    if (passed == len(REQUIRED_CASES) and len(ev) >= 2
            and meta["rc"] == 0 and not meta["timed_out"]):
        report["status"] = "PASS"
    else:
        report["fail_reason"] = (
            "cases_passed=%d/%d evidence=%d rc=%s timed_out=%s"
            % (passed, len(REQUIRED_CASES), len(ev), meta["rc"],
               meta["timed_out"]))
    write_report("windows", report)
    return report


def write_report(os_name, report):
    NATIVE_DIR.mkdir(exist_ok=True)
    p = NATIVE_DIR / (os_name + ".json")
    p.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n",
                 encoding="utf-8")
    print("wrote %s status=%s" % (rel(p), report["status"]))


def write_macos_report():
    """Honest BLOCKED record: every entry is a measured fact about this
    host, not a claim about a build that does not exist."""
    probes = []
    host_is_mac = sys.platform == "darwin"
    probes.append({
        "probe": "host_os",
        "result": sys.platform,
        "macos_host_present": host_is_mac,
    })
    engines = glob.glob(
        "C:/Program Files/Unity/Hub/Editor/*/Editor/Data/PlaybackEngines/*")
    engines += glob.glob("/Applications/Unity/Hub/Editor/*/"
                         "Unity.app/Contents/PlaybackEngines/*")
    mac_modules = [e for e in engines
                   if "mac" in e.lower() or "osx" in e.lower()]
    probes.append({
        "probe": "unity_macos_build_module",
        "installed_playback_engines": engines,
        "macos_module_present": bool(mac_modules),
    })
    nuget = Path.home() / ".nuget" / "packages"
    host_packs = sorted(p.name for p in nuget.glob(
        "microsoft.netcore.app.host.osx-*")) if nuget.is_dir() else []
    probes.append({
        "probe": "dotnet_osx_apphost_pack",
        "cached_osx_host_packs": host_packs,
        "apphost_pack_present": bool(host_packs),
    })
    # A real publish attempt records what the toolchain can do here.
    dotnet = find_dotnet()
    outdir = ROOT / "out" / "builds" / "macos"
    attempt = {"attempted": False}
    if dotnet:
        try:
            r = subprocess.run(
                [dotnet, "publish", "-c", "Release", "-r", "osx-arm64",
                 "--self-contained", "false",
                 str(ROOT / "game" / "GameCli"), "-o", str(outdir)],
                cwd=str(ROOT), capture_output=True, text=True, timeout=300)
            attempt = {"attempted": True, "rc": r.returncode,
                       "output_tail": ((r.stdout or "")
                                       + (r.stderr or ""))[-1200:]}
        except (subprocess.TimeoutExpired, OSError) as e:
            attempt = {"attempted": True, "error": str(e)}
    probes.append({"probe": "dotnet_osx_publish_attempt", **attempt})
    report = {
        "os": "macos",
        "status": "BLOCKED",
        "recorded_utc": int(time.time()),
        "blocked_reason": (
            "no macOS host on this Windows build machine; the produced "
            "Mach-O arm64 apphost cannot be executed or verified here, "
            "and Unity has no macOS playback module installed, so no "
            "GUI run evidence exists to back a PASS"),
        "probes": probes,
    }
    exe = outdir / "GameCli"
    if exe.is_file() and exe.stat().st_size > 0 and             attempt.get("rc") == 0:
        magics = ("cffaedfe", "cffaedff", "feedface", "feedfacf",
                  "cafebabe")
        macho = exe.read_bytes()[:4].hex() in magics
        pkg_dir = PACKAGE["macos"]
        pkg_dir.mkdir(parents=True, exist_ok=True)
        for f in outdir.iterdir():
            if f.is_file():
                shutil.copy2(str(f), str(pkg_dir / f.name))
        pkg = pkg_dir / "GameCli"
        report["build"] = {
            "path": rel(pkg),
            "sha256": sha256(pkg),
            "bytes": pkg.stat().st_size,
            "kind": "dotnet-osx-arm64-apphost",
            "mach_o_magic_verified": bool(macho),
            "runnable_here": False,
            "published_path": rel(exe),
            "packaged_note": (
                "byte-identical copy of the dotnet osx-arm64 apphost "
                "produced by the recorded publish probe; full publish dir "
                "reproduced by tools/native_run.py at out/builds/macos/"),
        }
    write_report("macos", report)
    return report


def main(argv):
    if len(argv) != 2 or argv[1] not in ("windows", "macos", "all"):
        print(__doc__, file=sys.stderr)
        return 2
    mode = argv[1]
    if mode in ("windows", "all"):
        write_windows_report()
    if mode in ("macos", "all"):
        write_macos_report()
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
