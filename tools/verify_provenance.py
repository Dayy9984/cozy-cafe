"""Independent provenance verifier for generated art.

Second, read-only pass over what tools/assets.py generated. For every job dir
under art/generated that carries provider-report.json + raw.png it:

1. Parses the PNG's vendor-signed C2PA manifest itself (caBX -> JUMBF ->
   claim/actions/signature CBOR). The signed claim's softwareAgent.name and
   softwareAgent.version are the backend's own attestation of what generated
   the image - stronger evidence than any field a pipeline file reports.
2. Cryptographically verifies the signed claim when openssl is
   available: x5chain cert-chain verify over the chain the claim presents
   (SSL.com C2PA and Trufo C2PA chains both observed on this backend), the
   COSE_Sign1 signature over the reconstructed Sig_structure, and the
   c2pa.hash.data exclusion hash that binds the claim to this exact file.
   The TSA timestamp token is extracted and its genTime recorded.
3. Binds the PNG bytes to the recorded codex OAuth session: the preserved
   rollout jsonl (kept via --keep-session) stores the image_gen tool result
   inline as base64; decoding and sha256-ing it must equal raw.png's sha256.
   That ties this file to a real `codex exec` session on the user's state
   root - provenance stops resting on self-reported metadata.
4. Re-derives model_verification honestly: VERIFIED only when the
   vendor-signed softwareAgent.version equals the requested model.

Results are written into each job's provenance.json under
`independent_verification` (additive - existing measured fields untouched).
Exits 0 when verification ran for every candidate dir; nonzero on tool
misuse or a dir whose verification could not complete (a measured
signature/model failure is reported in data, not hidden).

  python tools/verify_provenance.py            verify + write
  python tools/verify_provenance.py --check    verify, print, do not write
"""
import base64
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'art'
GEN = ART / 'generated'
PNG_SIG = bytes([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])
_ALG_NAMES = {-7: 'ES256', -35: 'ES384', -36: 'ES512', -8: 'EdDSA',
              -257: 'RS256', -258: 'RS384', -259: 'RS512',
              -37: 'PS256', -38: 'PS384', -39: 'PS512',
              -65535: 'RS1'}
_ALG_HASH = {'ES256': 'sha256', 'ES384': 'sha384', 'ES512': 'sha512',
             'RS256': 'sha256', 'RS384': 'sha384', 'RS512': 'sha512',
             'PS256': 'sha256', 'PS384': 'sha384', 'PS512': 'sha512',
             'EdDSA': None, 'RS1': 'sha1'}


# ---------- minimal definite-length CBOR ----------
def _dec(buf, pos=0):
    ib = buf[pos]
    pos += 1
    mt, ai = ib >> 5, ib & 31
    if ai < 24:
        arg = ai
    elif ai == 24:
        arg = buf[pos]; pos += 1
    elif ai == 25:
        arg = int.from_bytes(buf[pos:pos + 2], 'big'); pos += 2
    elif ai == 26:
        arg = int.from_bytes(buf[pos:pos + 4], 'big'); pos += 4
    elif ai == 27:
        arg = int.from_bytes(buf[pos:pos + 8], 'big'); pos += 8
    else:
        raise ValueError('indefinite/reserved CBOR length')
    if mt == 0:
        return arg, pos
    if mt == 1:
        return -1 - arg, pos
    if mt == 2:
        return bytes(buf[pos:pos + arg]), pos + arg
    if mt == 3:
        return buf[pos:pos + arg].decode('utf8'), pos + arg
    if mt == 4:
        arr = []
        for _ in range(arg):
            v, pos = _dec(buf, pos)
            arr.append(v)
        return arr, pos
    if mt == 5:
        m = {}
        for _ in range(arg):
            k, pos = _dec(buf, pos)
            v, pos = _dec(buf, pos)
            m[k.hex() if isinstance(k, bytes) else k] = v
        return m, pos
    if mt == 6:
        v, pos = _dec(buf, pos)
        return ('tag%d' % arg, v), pos
    if mt == 7:
        if arg in (20, 21):
            return arg == 21, pos
        if arg == 22:
            return None, pos
        return 'simple%d' % arg, pos
    raise ValueError('unreachable')


def _enc(obj):
    """CBOR encoder for the Sig_structure (bytes/str/int/list only)."""
    def head(mt, n):
        if n < 24:
            return bytes([(mt << 5) | n])
        if n < 256:
            return bytes([(mt << 5) | 24, n])
        if n < 65536:
            return bytes([(mt << 5) | 25]) + n.to_bytes(2, 'big')
        return bytes([(mt << 5) | 26]) + n.to_bytes(4, 'big')
    if isinstance(obj, bytes):
        return head(2, len(obj)) + obj
    if isinstance(obj, str):
        b = obj.encode('utf8')
        return head(3, len(b)) + b
    if isinstance(obj, int):
        return head(0, obj) if obj >= 0 else head(1, -1 - obj)
    if isinstance(obj, (list, tuple)):
        return head(4, len(obj)) + b''.join(_enc(x) for x in obj)
    raise TypeError(obj)


# ---------- PNG + JUMBF ----------
def _chunks(raw):
    off = 8
    while off + 8 <= len(raw):
        ln = int.from_bytes(raw[off:off + 4], 'big')
        yield raw[off + 4:off + 8], raw[off + 8:off + 8 + ln]
        off += 12 + ln


def _boxes(buf):
    off = 0
    while off + 8 <= len(buf):
        ln = int.from_bytes(buf[off:off + 4], 'big')
        hdr = 8
        if ln == 1:
            ln = int.from_bytes(buf[off + 8:off + 16], 'big')
            hdr = 16
        elif ln == 0:
            ln = len(buf) - off
        yield buf[off + 4:off + 8], buf[off + hdr:off + ln]
        off += ln


def _jumd_label(p):
    """jumd payload = 16-byte type id + toggles(1) + label cstr + optional
    id + optional signature box. Returns the label string (or None)."""
    if len(p) < 18:
        return None
    toggles = p[16]
    i = 17
    label = None
    if toggles & 0x03:
        end = p.find(b'\x00', i)
        if end > i:
            cand = p[i:end]
            if all(32 <= b < 127 for b in cand):
                label = cand.decode('utf8', 'replace')
                i = end + 1
    return label


def _jumbf_pairs(buf):
    """Yield (label, content_type, content_payload) pairing each jumd
    description with its sibling content boxes inside one jumb payload;
    nested jumb boxes recurse into their own jumd+content pairs."""
    label = None
    for ct, cp in _boxes(buf):
        if ct == b'jumd':
            label = _jumd_label(cp)
        elif ct == b'jumb':
            yield from _jumbf_pairs(cp)
        else:
            yield (label, ct, cp)


def _jumbf_leaves(cabx):
    """All leaf (label, type, payload) triples in a caBX payload."""
    for typ, pay in _boxes(cabx):
        if typ == b'jumb':
            yield from _jumbf_pairs(pay)


def extract_c2pa(png_path):
    """Returns dict: claim (decoded), assertions {label: decoded-or-raw},
    signature raw COSE bytes, error."""
    raw = Path(png_path).read_bytes()
    if raw[:8] != PNG_SIG:
        return {'error': 'not a png'}
    cabx = None
    for typ, data in _chunks(raw):
        if typ == b'caBX':
            cabx = data
            break
    if cabx is None:
        return {'error': 'no caBX chunk'}
    leaves = list(_jumbf_leaves(cabx))
    claim, assertions, sig_raw = None, {}, None
    for label, ct, cp in leaves:
        if label == 'c2pa.claim.v2' and ct == b'cbor':
            claim, _ = _dec(cp)
        elif label == 'c2pa.signature' and ct in (b'cbor', b'c2cs'):
            sig_raw = cp
        elif label and label.startswith('c2pa.') and ct == b'cbor':
            try:
                assertions[label], _ = _dec(cp)
            except Exception:
                assertions[label] = cp
    return {'raw': raw, 'claim': claim, 'assertions': assertions,
            'signature_cose': sig_raw, 'leaves': len(leaves),
            'claim_label_ok': claim is not None}


# ---------- openssl-backed crypto verification ----------
def _openssl():
    import shutil
    return shutil.which('openssl')


def _pem_from_der(der):
    b64 = base64.b64encode(der).decode()
    lines = '\n'.join(b64[i:i + 64] for i in range(0, len(b64), 64))
    return '-----BEGIN CERTIFICATE-----\n%s\n-----END CERTIFICATE-----\n' % lines



def _cert_text(der, openssl):
    with tempfile.NamedTemporaryFile('wb', suffix='.der', delete=False) as f:
        f.write(der)
        p = f.name
    try:
        r = subprocess.run([openssl, 'x509', '-inform', 'DER', '-in', p,
                            '-noout', '-subject', '-issuer'],
                           capture_output=True, text=True, timeout=30)
        subj = iss = None
        for line in r.stdout.splitlines():
            if line.startswith('subject='):
                subj = line[8:].strip()
            elif line.startswith('issuer='):
                iss = line[7:].strip()
        return {'subject': subj, 'issuer': iss}
    finally:
        os.unlink(p)


def _verify_signature(openssl, alg, leaf_der, sig, toi):
    """Verify COSE_Sign1 signature bytes over the Sig_structure bytes `toi`.
    ECDSA raw r||s is converted to DER for openssl. Returns True/False/None
    (None = could not attempt)."""
    with tempfile.TemporaryDirectory() as td:
        cert = Path(td) / 'leaf.pem'
        cert.write_text(_pem_from_der(leaf_der))
        pub = subprocess.run([openssl, 'x509', '-in', str(cert), '-pubkey',
                              '-noout'], capture_output=True, text=True,
                             timeout=30)
        if pub.returncode != 0:
            return None
        Path(td, 'pub.pem').write_text(pub.stdout)
        Path(td, 'toi.bin').write_bytes(toi)
        sig_bytes = sig
        argv = None
        if alg and alg.startswith('ES'):
            n = len(sig) // 2
            r_, s_ = sig[:n], sig[n:]
            def _derint(x):
                x = x.lstrip(b'\x00') or b'\x00'
                if x[0] & 0x80:
                    x = b'\x00' + x
                return b'\x02' + bytes([len(x)]) + x
            body = _derint(r_) + _derint(s_)
            sig_bytes = b'\x30' + bytes([len(body)]) + body
            argv = [openssl, 'dgst', '-' + _ALG_HASH[alg], '-verify',
                    str(Path(td, 'pub.pem')), '-signature',
                    str(Path(td, 'sig.bin')), str(Path(td, 'toi.bin'))]
        elif alg and alg.startswith('PS'):
            argv = [openssl, 'dgst', '-' + _ALG_HASH[alg], '-verify',
                    str(Path(td, 'pub.pem')), '-signature',
                    str(Path(td, 'sig.bin')), '-sigopt', 'rsa_padding_mode:pss',
                    '-sigopt', 'rsa_pss_saltlen:-1', str(Path(td, 'toi.bin'))]
        elif alg and alg.startswith('RS'):
            argv = [openssl, 'dgst', '-' + _ALG_HASH[alg], '-verify',
                    str(Path(td, 'pub.pem')), '-signature',
                    str(Path(td, 'sig.bin')), str(Path(td, 'toi.bin'))]
        if argv is None:
            return None
        Path(td, 'sig.bin').write_bytes(sig_bytes)
        r = subprocess.run(argv, capture_output=True, text=True, timeout=30)
        return r.returncode == 0


def _cert_chain_verify(openssl, ders):
    """openssl verify of the x5chain as presented. Returns (ok, subjects)."""
    if not ders:
        return None, []
    subjects = [_cert_text(d, openssl) for d in ders]
    with tempfile.TemporaryDirectory() as td:
        for i, d in enumerate(ders):
            Path(td, 'c%d.pem' % i).write_text(_pem_from_der(d))
        others = [str(Path(td, 'c%d.pem' % i)) for i in range(1, len(ders))]
        argv = [openssl, 'verify', '-partial_chain']
        if others:
            argv += ['-trusted', others[-1]]
            for mid in others[:-1]:
                argv += ['-untrusted', mid]
        argv.append(str(Path(td, 'c0.pem')))
        r = subprocess.run(argv, capture_output=True, text=True, timeout=30)
        return r.returncode == 0, subjects


def _tsa_times(openssl, token_der):
    """Extract times out of a CMS TimeStampToken by walking DER items
    directly: tag 0x18 = GeneralizedTime, 0x17 = UTCTime. The TSTInfo
    genTime is the fractional-second GeneralizedTime inside the eContent;
    cert validity times carry the same tags elsewhere in the blob."""
    times = []
    i = 0
    n = len(token_der)
    while i + 2 <= n:
        tag, ln = token_der[i], token_der[i + 1]
        if tag in (0x17, 0x18) and 0 < ln <= 32 and i + 2 + ln <= n:
            v = token_der[i + 2:i + 2 + ln]
            try:
                s = v.decode('ascii')
            except UnicodeDecodeError:
                i += 1
                continue
            if re.fullmatch(r'[0-9]{12,14}([.][0-9]+)?Z', s):
                times.append((tag, s))
            i += 2 + ln
            continue
        i += 1
    gen = [s for tag, s in times if tag == 0x18]
    utc = [s for tag, s in times if tag == 0x17]
    return gen + utc


def verify_signed_claim(png_path, openssl=None):
    """Full signed-claim inspection of one PNG. Returns a dict of measured
    facts; every field is parsed/verified from file bytes."""
    out = {'present': False}
    try:
        ext = extract_c2pa(png_path)
    except Exception as e:
        out['error'] = 'parse failed: %s' % e
        return out
    if 'error' in ext:
        out['error'] = ext['error']
        return out
    claim, assertions = ext['claim'], ext['assertions']
    if not isinstance(claim, dict):
        out['error'] = 'claim box not decoded'
        return out
    out['present'] = True
    cgi = claim.get('claim_generator_info') or {}
    out['claim_generator'] = cgi.get('name')
    out['claim_generator_spec'] = cgi.get('specVersion')
    out['instance_id'] = claim.get('instanceID')
    out['dc_title'] = claim.get('dc:title')
    out['alg'] = claim.get('alg')
    created = claim.get('created_assertions') or []
    out['created_assertions'] = [a.get('url') for a in created
                                 if isinstance(a, dict)]

    acts = (assertions.get('c2pa.actions.v2') or {}).get('actions') or []
    for a in acts:
        if not isinstance(a, dict):
            continue
        if a.get('action') == 'c2pa.created':
            sa = a.get('softwareAgent') or {}
            out['software_agent'] = sa.get('name')
            out['software_agent_version'] = sa.get('version')
            out['digital_source_type'] = a.get('digitalSourceType')
            when = a.get('when')
            out['signed_at'] = (when[1] if isinstance(when, tuple)
                                and len(when) == 2 else when)
            break

    sig_raw = ext['signature_cose']
    if not sig_raw:
        out['signature'] = {'present': False}
        return out
    out['signature'] = {'present': True}
    try:
        cose_obj, _ = _dec(sig_raw)
    except Exception as e:
        out['signature']['error'] = 'COSE decode failed: %s' % e
        return out
    cose = (cose_obj[1] if isinstance(cose_obj, tuple)
            and len(cose_obj) == 2 and cose_obj[0] == 'tag18' else cose_obj)
    out['signature']['cose_tag'] = (cose_obj[0] if isinstance(cose_obj, tuple)
                                    else None)
    if not (isinstance(cose, list) and len(cose) == 4):
        out['signature']['error'] = 'not a COSE_Sign1 4-array'
        return out
    protected, unprotected, payload, signature = cose
    try:
        phdr, _ = _dec(protected)
    except Exception:
        phdr = {}
    alg = _ALG_NAMES.get(phdr.get(1)) if isinstance(phdr, dict) else None
    out['signature']['alg'] = alg or ('unknown(%r)' % (phdr.get(1)
                                        if isinstance(phdr, dict) else None))
    out['signature']['bytes'] = len(signature) if isinstance(signature,
                                                           bytes) else None
    out['signature']['detached_payload'] = payload is None

    ders = []
    for src in (phdr, unprotected):
        if isinstance(src, dict):
            x5 = src.get(33) or src.get('x5chain')
            if isinstance(x5, list):
                ders = [d for d in x5 if isinstance(d, bytes)]
            elif isinstance(x5, bytes):
                ders = [x5]
            if ders:
                break
    out['signature']['cert_count'] = len(ders)

    if openssl:
        chain_ok, subjects = _cert_chain_verify(openssl, ders)
        out['signature']['cert_chain'] = [
            {'subject': s['subject'], 'issuer': s['issuer']} for s in subjects]
        out['signature']['cert_chain_verified'] = chain_ok

        claim_bytes = None
        for label, ct, cp in _iter_claim_content(png_path):
            if label == 'c2pa.claim.v2' and ct == b'cbor':
                claim_bytes = cp
        if claim_bytes is not None and ders and isinstance(signature, bytes):
            toi = _enc(['Signature1', protected, b'', claim_bytes])
            ok = _verify_signature(openssl, alg, ders[0], signature, toi)
            out['signature']['signature_valid'] = ok

        tst = unprotected.get('sigTst') or unprotected.get('sigTst2')
        tokens = []
        if isinstance(tst, dict):
            for t in tst.get('tstTokens') or []:
                if isinstance(t, dict) and isinstance(t.get('val'), bytes):
                    tokens.append(t['val'])
        out['signature']['tsa_tokens'] = len(tokens)
        if tokens:
            out['signature']['tsa_times'] = _tsa_times(openssl, tokens[0])
    else:
        out['signature']['openssl'] = 'not found; crypto checks skipped'

    hd = assertions.get('c2pa.hash.data')
    if isinstance(hd, dict) and hd.get('hash'):
        raw = ext['raw']
        excl = sorted([(e.get('start', 0), e.get('length', 0))
                       for e in hd.get('exclusions') or []
                       if isinstance(e, dict)])
        parts, cur = [], 0
        for st, ln in excl:
            parts.append(raw[cur:st])
            cur = st + ln
        parts.append(raw[cur:])
        digest = hashlib.sha256(b''.join(parts)).digest()
        out['file_data_hash'] = {
            'alg': hd.get('alg'), 'exclusions': excl,
            'name': hd.get('name'),
            'verified': digest == hd['hash'],
        }
    return out


def _iter_claim_content(png_path):
    raw = Path(png_path).read_bytes()
    for typ, data in _chunks(raw):
        if typ == b'caBX':
            yield from _jumbf_leaves(data)


def _codex_home():
    h = os.environ.get('CODEX_HOME')
    if h:
        return Path(h)
    home = Path.home() / '.codex'
    if home.is_dir():
        return home
    alt = Path('/c/Users/dlgkr/.codex')
    return alt if alt.is_dir() else home


def _find_rollout(session_id, sessions_root):
    suffix = '-' + str(session_id) + '.jsonl'
    hits = [p for p in sessions_root.rglob('rollout-*.jsonl')
            if p.name.endswith(suffix)]
    return hits[0] if hits else None


def session_binding(job_dir, report, sessions_root):
    """Bind raw.png bytes to the preserved codex rollout for report's
    session_id. The image_gen tool result stored inline in the rollout is
    decoded and sha256-compared to raw.png."""
    raw = job_dir / 'raw.png'
    res = {'session_id': report.get('session_id'), 'rollout_found': False}
    if not raw.is_file() or not report.get('session_id'):
        res['error'] = 'no raw.png or no session_id'
        return res
    res['raw_sha256'] = hashlib.sha256(raw.read_bytes()).hexdigest()
    ro = _find_rollout(report['session_id'], sessions_root)
    if ro is None:
        res['error'] = 'no rollout jsonl for session'
        return res
    res['rollout_found'] = True
    res['rollout_file'] = ro.name
    item_keys, result_sha = None, []
    for line in ro.read_text(encoding='utf8', errors='replace').splitlines():
        if 'image_gen' not in line and 'image_generation' not in line:
            continue
        try:
            rec = json.loads(line)
        except json.JSONDecodeError:
            continue
        payload = rec.get('payload') or {}
        item = payload.get('item') if isinstance(payload.get('item'), dict) \
            else payload
        if not isinstance(item, dict) or not item.get('result'):
            continue
        try:
            blob = base64.b64decode(item['result'])
        except Exception:
            continue
        if item_keys is None:
            item_keys = sorted(str(k) for k in item.keys())
            res['image_gen_item_kind'] = item.get('kind') or item.get('type')
            res['image_gen_item_status'] = item.get('status')
        result_sha.append(hashlib.sha256(blob).hexdigest())
    if item_keys is None:
        res['error'] = 'no image_gen result item in rollout'
        return res
    res['tool_item_keys'] = item_keys
    res['tool_schema_has_model_field'] = 'model' in item_keys
    res['result_sha256'] = result_sha
    res['matches_raw_bytes'] = res['raw_sha256'] in result_sha
    return res


def verify_job_dir(job_dir, sessions_root, openssl, requested, write):
    rep_path = job_dir / 'provider-report.json'
    if not rep_path.is_file():
        return None
    try:
        report = json.loads(rep_path.read_text(encoding='utf8'))
    except ValueError:
        report = {}
    prov_path = job_dir / 'provenance.json'
    prov = {}
    if prov_path.is_file():
        try:
            prov = json.loads(prov_path.read_text(encoding='utf8'))
        except ValueError:
            prov = {}

    sc = verify_signed_claim(job_dir / 'raw.png', openssl)
    bind = session_binding(job_dir, report, sessions_root)
    signed_model = sc.get('software_agent_version')
    verified = (sc.get('present') and signed_model is not None
                and signed_model == requested)
    entry = {
        'verified_at': '2026-09-29',
        'verifier': 'tools/verify_provenance.py',
        'signed_claim': sc,
        'codex_session_binding': bind,
        'model_verification_basis':
            'vendor-signed C2PA softwareAgent.version + rollout sha256',
        'requested_image_model': requested,
        'signed_model_matches_request': verified,
    }
    result = {
        'dir': job_dir.name,
        'signed_claim_present': sc.get('present'),
        'signed_model': signed_model,
        'cert_chain_verified':
            (sc.get('signature') or {}).get('cert_chain_verified'),
        'signature_valid': (sc.get('signature') or {}).get('signature_valid'),
        'file_data_hash_verified':
            (sc.get('file_data_hash') or {}).get('verified'),
        'session_match': bind.get('matches_raw_bytes'),
        'model_verified': verified,
    }
    if write:
        prov.setdefault('provider', 'codex')
        prov.setdefault('requested_image_model', requested)
        prov['effective_image_model'] = (signed_model
                                       or prov.get('effective_image_model'))
        prov['model_verification'] = 'VERIFIED' if verified else 'NOT_VERIFIED'
        prov['independent_verification'] = entry
        if bind.get('session_id'):
            prov.setdefault('session_id', bind['session_id'])
        if not prov.get('raw_sha256'):
            raw = job_dir / 'raw.png'
            if raw.is_file():
                prov['raw_sha256'] = hashlib.sha256(
                    raw.read_bytes()).hexdigest()
        prov_path.write_text(json.dumps(prov, ensure_ascii=False, indent=2),
                             encoding='utf8')
    return result


def main(argv):
    write = '--check' not in argv[1:]
    prov = json.loads((ART / 'provider.json').read_text(encoding='utf8'))
    requested = prov.get('requested_image_model')
    openssl = _openssl()
    sessions_root = _codex_home() / 'sessions'
    if not sessions_root.is_dir():
        print('BLOCKED: codex sessions root missing: %s' % sessions_root,
              file=sys.stderr)
        return 2
    results = []
    for d in sorted(GEN.iterdir()):
        if not d.is_dir():
            continue
        r = verify_job_dir(d, sessions_root, openssl, requested, write)
        if r is not None:
            results.append(r)
    print(json.dumps({
        'openssl': openssl or 'NOT_FOUND',
        'sessions_root': str(sessions_root),
        'requested_image_model': requested,
        'jobs': results,
    }, ensure_ascii=False, indent=2))
    if any(r['session_match'] is not True or not r['signed_claim_present']
           for r in results):
        return 1
    return 0


if __name__ == '__main__':
    raise SystemExit(main(sys.argv))
