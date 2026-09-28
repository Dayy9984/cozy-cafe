"""Image toolchain bootstrap + generation driver for the art pipeline.

Packet contract (art/PIPELINE.md): explicit codex provider on the user's
existing ChatGPT OAuth session, image model requested in the prompt as
gpt-image-2.5-sunburst, never --model to Codex, never a paid-API/other
provider fallback, never token handling. Raw PNG bytes and the recorded
backend model are verified from the real session rollout before a claim.

  python tools/assets.py install --allow-network   clone sprite-gen + venv
  python tools/assets.py doctor                    report toolchain presence
  python tools/assets.py generate <job> --allow-generation
"""
from pathlib import Path
import argparse, hashlib, json, os, shutil, subprocess, sys

ROOT = Path(__file__).resolve().parents[1]
EXTERNAL = ROOT / '.external' / 'sprite-gen'
LOCK = ROOT / '.external' / 'sprite-gen.lock.json'
PINNED_COMMIT = 'b725baa5aad026f183e2083275b441f2db225c48'
REPO = 'https://github.com/aldegad/sprite-gen.git'


def python_path():
    """Venv interpreter, whichever host layout it was built with."""
    for rel in ('Scripts/python.exe', 'bin/python', 'bin/python3'):
        p = EXTERNAL / '.venv' / rel
        if p.is_file():
            return p
    return EXTERNAL / '.venv' / ('Scripts/python.exe' if os.name == 'nt' else 'bin/python')


def _venv_base_python():
    """Pick a real interpreter to build the venv: on POSIX hosts prefer a
    native Windows python (so pip/numpy install cleanly and child processes
    can exec codex.cmd); fall back to the running interpreter."""
    if os.name == 'nt':
        return sys.executable
    for cand in (
        os.environ.get('WINDOWS_PYTHON'),
        'C:/Users/dlgkr/AppData/Local/Programs/Python/Python313/python.exe',
    ):
        if cand and Path(cand).is_file():
            return cand
    return sys.executable


def install():
    if EXTERNAL.exists():
        if not (EXTERNAL / '.git').exists():
            raise ValueError('Existing target is not a Git clone; refusing overwrite')
    else:
        EXTERNAL.parent.mkdir(exist_ok=True)
        subprocess.run(['git', 'clone', REPO, str(EXTERNAL)], check=True)
        subprocess.run(['git', '-C', str(EXTERNAL), 'checkout', PINNED_COMMIT], check=True)
    for f in ('SKILL.md', 'pyproject.toml', 'scripts/generate_sprite_image.py'):
        if not (EXTERNAL / f).is_file():
            raise ValueError('Upstream contract changed: missing ' + f)
    if not python_path().exists():
        base = _venv_base_python()
        subprocess.run([base, '-m', 'venv', str(EXTERNAL / '.venv')], check=True)
    subprocess.run([str(python_path()), '-m', 'pip', 'install', '-e', str(EXTERNAL)], check=True)
    sha = subprocess.check_output(['git', '-C', str(EXTERNAL), 'rev-parse', 'HEAD'],
                                  text=True).strip()
    lock = {'repo': REPO, 'commit': sha, 'python': str(python_path()), 'installed': True}
    LOCK.write_text(json.dumps(lock, indent=2), encoding='utf8')
    print(json.dumps(lock, indent=2))
    return 0


def _profile_roots():
    """Candidate roots for the user's Windows profile, whichever python
    flavor runs this file: native Windows reports Path.home() directly;
    cygwin/msys interpreters report the POSIX home instead, so USERPROFILE
    (drive-letter and /x/ mounted spellings) is tried too."""
    roots, seen = [], set()
    for cand in (Path.home(),
                 Path(os.environ['USERPROFILE'])
                 if os.environ.get('USERPROFILE') else None):
        if cand is None:
            continue
        key = str(cand)
        if key not in seen and cand.is_dir():
            seen.add(key)
            roots.append(cand)
    up = os.environ.get('USERPROFILE', '')
    if len(up) > 2 and up[1] == ':' and os.name == 'posix':
        mp = Path('/' + up[0].lower() + up[2:].replace(chr(92), '/'))
        if str(mp) not in seen and mp.is_dir():
            seen.add(str(mp))
            roots.append(mp)
    return roots


def _codex_env():
    """Child env resolving `codex` to the real CLI binary.

    The npm dir also holds an opencodex shim (`codex.EXE`, 323 MB) that
    cannot see codex-code-mode-host.exe, so image_gen never starts through
    it. The vendored package binary sits next to the host — prepend that
    directory so shutil.which picks it first, then the npm dir and node."""
    env = os.environ.copy()
    extra = []
    npms = [r / 'AppData/Roaming/npm' for r in _profile_roots()]
    for npm in npms:
        found = False
        for hit in sorted(npm.glob(
                'node_modules/@openai/**/codex-win32-*/vendor/*/bin/codex.exe')):
            if hit.with_name('codex-code-mode-host.exe').is_file():
                extra.append(str(hit.parent))
                found = True
                break
        if found:
            break
    extra.extend(str(n) for n in npms if n.is_dir())
    extra.append('C:/Program Files/nodejs')
    env['PATH'] = os.pathsep.join(extra + [env.get('PATH', '')])
    return env


def _rollout_image_model(session_id):
    """Read the preserved codex rollout for the backend image model recorded
    on the image_gen item. Returns the model string, or None when the record
    carries none — the caller reports UNVERIFIED rather than guessing."""
    if not session_id:
        return None
    home = os.environ.get('CODEX_HOME')
    root = Path(home) if home else Path.home() / '.codex'
    if os.name != 'nt' and not root.is_dir():
        alt = Path('/c/Users/dlgkr/.codex')
        if alt.is_dir():
            root = alt
    sessions = root / 'sessions'
    if not sessions.is_dir():
        return None
    hits = [p for p in sessions.rglob('rollout-*.jsonl')
            if p.name.endswith('-' + str(session_id) + '.jsonl')]
    if not hits:
        return None
    for line in hits[0].read_text(encoding='utf8', errors='replace').splitlines():
        try:
            rec = json.loads(line)
        except json.JSONDecodeError:
            continue
        payload = rec.get('payload') or {}
        item = payload.get('item') if isinstance(payload.get('item'), dict) else payload
        if not isinstance(item, dict):
            continue
        blob = json.dumps(item)
        if 'image_gen' not in blob and 'image_generation' not in blob:
            continue
        for key in ('model', 'image_model', 'effective_image_model'):
            v = item.get(key)
            if isinstance(v, str) and v:
                return v
    return None


def _c2pa_image_model(png_path):
    """Extract the backend generator model from the PNG's C2PA manifest
    (caBX chunk -> c2pa.actions assertion -> softwareAgent.version). This is
    the vendor-signed provenance record of what actually generated the image.
    Returns e.g. 'gpt-image' or None when no C2PA claim is present."""
    import re as _re
    try:
        raw = Path(png_path).read_bytes()
    except OSError:
        return None
    sig = bytes([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])
    if raw[:8] != sig:
        return None
    off = 8
    while off + 8 <= len(raw):
        ln = int.from_bytes(raw[off:off + 4], 'big')
        typ = raw[off + 4:off + 8]
        if typ == b'caBX':
            data = raw[off + 8:off + 8 + ln]
            # CBOR text item after the 'version' key: 0x60+len prefix, then
            # exactly len bytes — the version string ends where the next key
            # begins, so a regex on bytes alone over-reads.
            idx = data.find(b'version')
            while idx != -1:
                p = idx + 7
                if p < len(data) and 0x60 <= data[p] <= 0x77:
                    n = data[p] - 0x60
                    v = data[p + 1:p + 1 + n]
                    if all(32 <= b < 127 for b in v):
                        return v.decode('ascii')
                idx = data.find(b'version', idx + 1)
            return None
        off += 12 + ln
    return None


def generate(job_id, project):
    py = python_path()
    if not py.is_file():
        raise ValueError('First install with: python tools/assets.py install --allow-network')
    cfg = json.loads((project / 'art/provider.json').read_text(encoding='utf8'))
    jobs = json.loads((project / 'art/jobs.json').read_text(encoding='utf8'))
    if cfg['provider'] != 'codex':
        raise ValueError('Explicit Codex provider required; no provider fallback')
    job = next((j for j in jobs if j['id'] == job_id), None)
    if job is None:
        raise ValueError('Unknown job ' + job_id)
    env = _codex_env()
    codex_bin = shutil.which('codex', path=env['PATH']) or 'codex'
    status = subprocess.run([codex_bin, 'login', 'status'], capture_output=True,
                            text=True, env=env)
    if status.returncode:
        raise ValueError('Codex login unavailable. User must sign in; do not copy tokens.')
    out = project / job['raw_file']
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists():
        raise ValueError('Raw exists; preserve prior take and create a new job '
                         'ID/output before regenerating')
    report = out.with_name('provider-report.json')
    cmd = [str(py), '-m', 'sprite_gen.cli', 'gen', '--provider', 'codex',
           '--prompt-file', str(project / job['prompt_file']),
           '--out', str(out), '--report', str(report), '--keep-session']
    # Do not forward requested_image_model to --model: upstream may mean the
    # main Codex agent model. The image model rides in the prompt text only.
    subprocess.run(cmd, cwd=str(EXTERNAL), env=_codex_env(), check=True)
    if not out.exists() or out.read_bytes()[:8] != b'\x89PNG\r\n\x1a\n':
        raise ValueError('No verified PNG; not successful generation')
    provider_report = (json.loads(report.read_text(encoding='utf8'))
                       if report.is_file() else {})
    # The rollout survives (--keep-session) so the image_gen item itself can
    # be read for a backend-reported model — request.model is the reasoning
    # model and is never the answer here.
    rollout_model = _rollout_image_model(provider_report.get('session_id'))
    c2pa_model = _c2pa_image_model(out)
    actual_image_model = (c2pa_model or rollout_model
                          or provider_report.get('effective_image_model')
                          or provider_report.get('image_generation', {}).get('model'))
    summary = {
        'provider': 'codex',
        'requested_image_model': cfg['requested_image_model'],
        'effective_image_model': actual_image_model,
        'model_evidence': {'c2pa': c2pa_model, 'rollout': rollout_model},
        'model_verification': ('VERIFIED'
                               if actual_image_model == cfg['requested_image_model']
                               else 'NOT_VERIFIED'),
        'raw_sha256': hashlib.sha256(out.read_bytes()).hexdigest(),
        'game_asset_approval': 'PENDING',
        'note': 'Read actual tool/provider metadata before claiming Sunburst. '
                'Raw is not an atlas or approved asset.'}
    out.with_name('provenance.json').write_text(
        json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf8')
    # Independent second pass: re-parses the vendor-signed C2PA claim and
    # binds raw.png bytes to the preserved session rollout, appending the
    # verified record into provenance.json (never fails the generation).
    try:
        subprocess.run([sys.executable,
                        str(ROOT / 'tools' / 'verify_provenance.py')],
                       cwd=str(ROOT), check=False, capture_output=True)
    except Exception:
        pass
    print(str(out))
    print('Raw PNG verified; exact image model and production asset approval '
          'remain pending.')
    return 0


def main():
    p = argparse.ArgumentParser()
    p.add_argument('command', choices=['install', 'generate', 'doctor'])
    p.add_argument('job', nargs='?')
    p.add_argument('--project', type=Path, default=ROOT)
    p.add_argument('--allow-network', action='store_true')
    p.add_argument('--allow-generation', action='store_true')
    a = p.parse_args()
    if a.command == 'doctor':
        env = _codex_env()
        print(json.dumps({
            'codex': shutil.which('codex', path=env['PATH']),
            'sprite_gen_python_exists': python_path().is_file(),
            'auth_read': False}, indent=2))
        return 0
    if a.command == 'install':
        if not a.allow_network:
            raise ValueError('Installing third-party code needs --allow-network')
        return install()
    if not a.allow_generation:
        raise ValueError('Authenticated generation needs --allow-generation')
    return generate(a.job, a.project.resolve())


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except (OSError, ValueError, subprocess.SubprocessError) as e:
        print('BLOCKED:', e, file=sys.stderr)
        raise SystemExit(2)
