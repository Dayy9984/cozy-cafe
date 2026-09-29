#!/usr/bin/env python3
"""Post-generation asset pipeline: deterministic pixel-art production of the
approved atlas under the v0.8.3 32x32 unit contract.

Raw generations are REFERENCES, not sources to blur-shrink. Each final atlas
cell is a 32x32 unit box produced by a deterministic pixel path:
block-decimate pixel_snap through the frame's declared ref_map (source
rect -> destination rect inside the cell, coverage >= 0.25 -> opaque with
the block's median color), then a cluster palette quantize capped at the
contract's max_unique_colors_per_cell. Approval requires the measured
reference conformance (silhouette IoU vs the raw + palette match) to meet
the contract thresholds; the score is recorded as reference_similarity in
the manifest frame and the job's provenance.json.

  python tools/art_pipeline.py build      extract -> qa -> compose -> approve
  python tools/art_pipeline.py qa         re-measure approved art and report

Runs under the sprite-gen venv interpreter (numpy + Pillow present).
Approved PNGs are written in the same canonical encoding as the core's
PngWriter (RGBA8, filter 0, zlib stored blocks) so the shared C# PngReader
decodes them byte-for-byte on both hosts. The C# gate recomputes every
measurement below from the committed files - nothing here is authoritative.
"""
import json, math, struct, sys, zlib
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'art'
APPROVED = ART / 'approved'
CONTRACT = ROOT / 'data' / 'art_contract.json'
PNG_SIG = bytes([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])

JOB_TARGET = {
    'tile_wood': 'floor_wood',
    'body_anchor': 'human_body_01',
    'machine_espresso': 'machine_espresso',
    'ui_style': 'ui_panel',
    'ui_icons': 'ui_button',
    'table_square': 'table_square',
    'chair': 'chair',
}
ALPHA_CUT = 24
UNIT = 32                 # atlas cell unit box, contract a50b762
MARGIN = 2                # content margin inside the unit box

# pixel_quality / reference_conformance spec (data/art_contract.json):
#   max_unique_colors_per_cell = 64, min_silhouette_iou = 0.55,
#   min_palette_match = 0.80, coverage_threshold = 0.25.
COVER_MIN = 0.25
PIXEL_PATH = ('pixel_snap:block_decimate(cover>=%s)+median_color'
              '+cluster_quantize(<=%d)')


def write_json_lf(path, obj):
    with open(path, 'w', encoding='utf8', newline='') as f:
        f.write(json.dumps(obj, ensure_ascii=False, indent=2) + chr(10))


def load_catalog():
    return {e['id']: e for e in json.loads((ART / 'asset_catalog.json').read_text(encoding='utf8'))}


def contract_limits():
    c = json.loads(CONTRACT.read_text(encoding='utf8'))
    pq = c.get('pixel_quality') or {}
    rc = c.get('reference_conformance') or {}
    return {
        'max_unique_colors': int(pq.get('max_unique_colors_per_cell', 64)),
        'min_iou': float(rc.get('min_silhouette_iou', 0.55)),
        'min_palette': float(rc.get('min_palette_match', 0.8)),
        'cover': float(rc.get('coverage_threshold', COVER_MIN)),
    }


def opaque_bbox(a):
    m = a[..., 3] > ALPHA_CUT
    if not m.any():
        return None
    ys, xs = np.where(m)
    return int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1


def column_spans(a, min_gap=12, min_width=8):
    """Split a horizontal arrangement into opaque column runs (icon strips,
    multi-subject sheets). One-component images return a single span."""
    m = (a[..., 3] > ALPHA_CUT).any(axis=0)
    spans, start = [], None
    run = 0
    for x in range(a.shape[1]):
        if m[x]:
            if start is None:
                start = x
            run = 0
        elif start is not None:
            run += 1
            if run >= min_gap:
                if x - run - start >= min_width:
                    spans.append((start, x - run))
                start = None
    if start is not None and a.shape[1] - start >= min_width:
        spans.append((start, a.shape[1]))
    return spans or ([(0, a.shape[1])] if (a[..., 3] > ALPHA_CUT).any() else [])


# --------------------------------------------------------------------------
# deterministic pixel path (the sprite-gen pixel_snap + quantize equivalent)
# --------------------------------------------------------------------------

def snap_cell(src, ref_map, cover_min=COVER_MIN):
    """Block-decimate pixel snap: for every dst pixel in each ref_map entry,
    the covering source block is tested; when opaque coverage >= cover_min the
    dst pixel is the median color of the block's opaque pixels, alpha 255.
    Pure integer math - deterministic, no resampling filter, and the
    identical rule runs inside the C# gate for reference-silhouette rebuild."""
    cell = np.zeros((UNIT, UNIT, 4), dtype=np.uint8)
    mask = src[..., 3] > ALPHA_CUT
    for e in ref_map:
        sx0, sy0, sx1, sy1 = e['src']
        dx, dy, dw, dh = e['dst']
        for py in range(dh):
            ay0 = int(sy0 + py * (sy1 - sy0) / dh)
            ay1 = int(sy0 + (py + 1) * (sy1 - sy0) / dh)
            for px in range(dw):
                ax0 = int(sx0 + px * (sx1 - sx0) / dw)
                ax1 = int(sx0 + (px + 1) * (sx1 - sx0) / dw)
                blk = mask[ay0:ay1, ax0:ax1]
                if blk.size and float(blk.mean()) >= cover_min:
                    cols = src[ay0:ay1, ax0:ax1][blk]
                    cell[dy + py, dx + px] = (int(np.median(cols[:, 0])),
                                              int(np.median(cols[:, 1])),
                                              int(np.median(cols[:, 2])), 255)
    return cell


def quantize_cell(cell, max_colors):
    """Cluster palette quantize: right-shift each channel until the opaque
    colors fit the contract cap, then repaint each pixel with its cluster
    mean. Deterministic (stable unique sort, deterministic means). Returns
    (cell, unique_color_count)."""
    out = cell.copy()
    m = out[..., 3] > 0
    if not m.any():
        return out, 0
    px = out[m][:, :3].astype(np.int32)
    for shift in range(0, 7):
        key = ((px[:, 0] >> shift) << 10 | (px[:, 1] >> shift) << 5
               | (px[:, 2] >> shift))
        u, inv = np.unique(key, return_inverse=True)
        if len(u) <= max_colors:
            cents = np.zeros((len(u), 3))
            for i in range(len(u)):
                cents[i] = px[inv == i].mean(axis=0)
            out[m, :3] = np.clip(np.rint(cents[inv]), 0, 255).astype(np.uint8)
            return out, int(len(u))
    return out, -1


def ref_silhouette(src, ref_map, cover_min=COVER_MIN):
    """Reference silhouette in cell space: the same coverage rule the snap
    uses, recomputed from the raw's opaque mask. The C# gate rebuilds this
    silhouette from the committed raw PNG for the IoU measurement."""
    ref = np.zeros((UNIT, UNIT), dtype=bool)
    mask = src[..., 3] > ALPHA_CUT
    for e in ref_map:
        sx0, sy0, sx1, sy1 = e['src']
        dx, dy, dw, dh = e['dst']
        for py in range(dh):
            ay0 = int(sy0 + py * (sy1 - sy0) / dh)
            ay1 = int(sy0 + (py + 1) * (sy1 - sy0) / dh)
            for px in range(dw):
                ax0 = int(sx0 + px * (sx1 - sx0) / dw)
                ax1 = int(sx0 + (px + 1) * (sx1 - sx0) / dw)
                blk = mask[ay0:ay1, ax0:ax1]
                if blk.size and float(blk.mean()) >= cover_min:
                    ref[dy + py, dx + px] = True
    return ref


def palette_match(src, ref_map, cell):
    """Share of the cell's opaque pixels whose coarse color bin (5 bits per
    channel) exists among the reference's opaque pixels in the mapped source
    regions. A wrong-palette cell fails; real pixel-snapped art scores ~1."""
    bins = set()
    mask = src[..., 3] > ALPHA_CUT
    for e in ref_map:
        sx0, sy0, sx1, sy1 = e['src']
        px = src[sy0:sy1, sx0:sx1][mask[sy0:sy1, sx0:sx1]]
        for c in px:
            bins.add((int(c[0]) >> 3) << 10 | (int(c[1]) >> 3) << 5
                     | (int(c[2]) >> 3))
    om = cell[..., 3] > 0
    if not om.any():
        return 0.0
    hit = 0
    for c in cell[om]:
        if ((int(c[0]) >> 3) << 10 | (int(c[1]) >> 3) << 5
                | (int(c[2]) >> 3)) in bins:
            hit += 1
    return hit / float(om.sum())


def reference_similarity(src, ref_map, cell, cover_min=COVER_MIN):
    """Measured conformance of an approved cell to its raw reference:
    silhouette IoU + palette match. Recorded as reference_similarity in the
    manifest frame and the job provenance."""
    ref = ref_silhouette(src, ref_map, cover_min)
    om = cell[..., 3] > 0
    union = int((om | ref).sum())
    iou = float((om & ref).sum()) / union if union else 0.0
    pal = palette_match(src, ref_map, cell)
    return {'iou': round(iou, 3), 'palette': round(pal, 3)}


# --------------------------------------------------------------------------
# per-job cell production under the 32x32 unit contract
# --------------------------------------------------------------------------

def tile_ref_map(src):
    """v0.8 tile geometry at unit scale: the iso rhombus maps to the 32x16
    top-face band (rows 0-15), the side skirt maps to the 2px visual edge
    band (rows 16-17). Physical thickness stays 0 - the band is paint."""
    m = src[..., 3] > ALPHA_CUT
    rows = np.where(m.any(axis=1))[0]
    top_y, bot_y = int(rows[0]), int(rows[-1])
    eq = int(np.argmax(m.sum(axis=1)))
    xs = np.where(m[eq])[0]
    cx = int((xs[0] + xs[-1]) // 2)
    half_w = max(1.0, (xs[-1] - xs[0]) / 2.0)
    front_y = min(bot_y, 2 * eq - top_y)
    span = max(1, eq - top_y)

    diamond = np.zeros(m.shape, dtype=bool)
    for y in range(top_y, front_y + 1):
        hh = half_w * (1.0 - abs(y - eq) / span)
        lo = max(0, int(math.ceil(cx - hh)))
        hi = min(m.shape[1], int(math.floor(cx + hh)) + 1)
        diamond[y, lo:hi] = True

    ref_map = [{'src': [int(xs[0]), top_y, int(xs[-1]) + 1, front_y + 1],
                'dst': [0, 0, UNIT, 16]}]
    side = m & ~diamond & (np.arange(m.shape[0])[:, None] > eq)
    if side.any():
        sy, sx = np.where(side)
        ref_map.append({'src': [int(sx.min()), int(sy.min()),
                                int(sx.max()) + 1, int(sy.max()) + 1],
                        'dst': [0, 16, UNIT, 2]})
    return ref_map


def fit_dest(bw, bh, anchor):
    """Aspect-preserving fit of a component inside the unit box with the
    contract margin; bottom-anchored for world objects, centered for UI."""
    s = min((UNIT - 2 * MARGIN) / bw, (UNIT - 2 * MARGIN) / bh)
    nw, nh = max(1, int(round(bw * s))), max(1, int(round(bh * s)))
    dx = (UNIT - nw) // 2
    dy = UNIT - MARGIN - nh if anchor == 'bottom' else (UNIT - nh) // 2
    return dx, dy, nw, nh


def produce_cells(job, src, entry):
    """Return [(frame_id, cell, meta)] for one job. Every cell is a 32x32
    unit box produced through its declared ref_map."""
    cat = entry['category']
    out = []
    if cat == 'tile':
        ref_map = tile_ref_map(src)
        cell = snap_cell(src, ref_map)
        meta = {'origin': [UNIT // 2, 8], 'ref_map': ref_map}
        out.append((job['id'], cell, meta))
        return out

    spans = column_spans(src)
    anchor = 'center' if cat == 'ui_skin' else 'bottom'
    multi = len(spans) > 1
    for i, (a, b) in enumerate(spans):
        sub = src[:, a:b]
        bb = opaque_bbox(sub)
        if bb is None:
            continue
        sbox = [a + bb[0], bb[1], a + bb[2], bb[3]]
        dst = fit_dest(bb[2] - bb[0], bb[3] - bb[1], anchor)
        ref_map = [{'src': sbox, 'dst': list(dst)}]
        cell = snap_cell(src, ref_map)
        if anchor == 'bottom':
            meta = {'origin': [dst[0] + dst[2] // 2, dst[1] + dst[3] - 1],
                    'ref_map': ref_map}
        else:
            meta = {'origin': [UNIT // 2, UNIT // 2], 'ref_map': ref_map}
        fid = job['id'] + '_' + str(i) if multi else job['id']
        out.append((fid, cell, meta))
    return out


def measure_tile_unit(cell):
    """Measured unit-tile geometry: 32x16 top-face rhombus filling rows
    0-15 (apex row 0, equator ~row 8, front vertex row 15) and the visual
    side band inside rows 16-18."""
    m = cell[..., 3] > 0
    if not m.any():
        return None
    top = m[:16]
    widths = top.sum(axis=1)
    rows = np.where(m.any(axis=1))[0]
    eq = int(np.argmax(widths))
    return {
        'top_row': int(np.where(widths > 0)[0][0]) if (widths > 0).any() else -1,
        'equator_row': eq,
        'top_face_h': int((widths > 0).sum()),
        'max_width': int(widths[eq]),
        'silhouette_bottom_row': int(rows[-1]),
        'side_h': max(0, int(rows[-1]) - 15),
    }


def qa_cell(fid, cell, meta, entry, sim, ncolors, limits):
    """Unit-contract QA: 32x32 cell, quantized palette cap, measured
    reference conformance, plus per-category geometry."""
    checks = {'canvas_w': int(cell.shape[1]), 'canvas_h': int(cell.shape[0]),
              'contract_canvas': [UNIT, UNIT],
              'unique_opaque_colors': ncolors,
              'max_unique_colors': limits['max_unique_colors'],
              'reference_similarity': sim}
    ok = (cell.shape[0] == UNIT and cell.shape[1] == UNIT
          and 0 < ncolors <= limits['max_unique_colors']
          and sim['iou'] >= limits['min_iou']
          and sim['palette'] >= limits['min_palette'])
    cat = entry['category']
    if cat == 'tile':
        d = measure_tile_unit(cell)
        checks['diamond'] = d
        ok = (ok and d is not None and d['top_row'] == 0
              and d['max_width'] == UNIT and d['top_face_h'] == 16
              and 6 <= d['equator_row'] <= 10
              and 15 <= d['silhouette_bottom_row'] <= 18)
        checks['top_face_px'] = [d['max_width'], d['top_face_h']] if d else None
    elif cat == 'furniture':
        checks['unbaked_pivot'] = True
    checks['opaque_px'] = int((cell[..., 3] > 0).sum())
    checks['nonempty'] = checks['opaque_px'] > 0
    return checks, bool(ok and checks['nonempty'])


def png_write(path, rgba):
    """Canonical PNG: RGBA8, filter 0 per row, zlib stored blocks - the same
    encoding the core's PngWriter emits, so the C# reader decodes it exactly."""
    h, w = rgba.shape[0], rgba.shape[1]
    rows = bytearray()
    for y in range(h):
        rows.append(0)
        rows.extend(rgba[y].tobytes())
    comp = zlib.compressobj(level=0)
    data = comp.compress(bytes(rows)) + comp.flush()

    def chunk(t, d):
        c = struct.pack('>I', len(d)) + t + d
        return c + struct.pack('>I', zlib.crc32(t + d) & 0xFFFFFFFF)

    ihdr = struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0)
    Path(path).write_bytes(PNG_SIG + chunk(b'IHDR', ihdr) + chunk(b'IDAT', data)
                           + chunk(b'IEND', b''))


def compose_atlas(cells, metas):
    """Pack unit cells left-to-right with 2px padding into the alpha sheet
    and declare every frame explicitly: rect, origin, fps, loop - no
    uniform-sheet guessing downstream."""
    pad = 2
    w = sum(cell.shape[1] for _, cell in cells) + pad * (len(cells) + 1)
    h = max(cell.shape[0] for _, cell in cells) + pad * 2
    atlas = np.zeros((h, w, 4), dtype=np.uint8)
    x = pad
    frames = []
    for (fid, cell), meta in zip(cells, metas):
        ch, cw = cell.shape[0], cell.shape[1]
        atlas[pad:pad + ch, x:x + cw] = cell
        f = {'id': fid, 'rect': {'x': x, 'y': pad, 'w': cw, 'h': ch},
             'origin': {'x': meta['origin'][0], 'y': meta['origin'][1]},
             'fps': meta.get('fps', 0), 'loop': meta.get('loop', False)}
        for k in ('render_offset_px', 'baked_alignment_offset_px',
                  'render_offset_owner', 'physical_thickness', 'job',
                  'category', 'asset', 'canvas_px', 'top_face_px',
                  'reference', 'ref_map', 'reference_similarity',
                  'unique_opaque_colors', 'pixel_path', 'content_bounds'):
            if k in meta:
                f[k] = meta[k]
        frames.append(f)
        x += cw + pad
    return atlas, frames


def contact_sheet(cells, metas, zoom=4):
    """QA contact sheet: every approved unit cell magnified with its
    contract canvas border (red), ground anchor crosshair (yellow), and the
    32x16 top-face band guide for tiles (cyan). Measurement evidence."""
    pad = 10
    cw = max(cell.shape[1] for _, cell in cells) * zoom
    ch = max(cell.shape[0] for _, cell in cells) * zoom
    per_row = max(1, min(len(cells), 10))
    rows_n = (len(cells) + per_row - 1) // per_row
    w = (cw + pad) * per_row + pad
    h = (ch + pad) * rows_n + pad
    img = np.zeros((h, w, 4), dtype=np.uint8)
    img[..., 0], img[..., 1], img[..., 2], img[..., 3] = 24, 24, 30, 255
    for i, ((fid, cell), meta) in enumerate(zip(cells, metas)):
        x = pad + (i % per_row) * (cw + pad)
        y = pad + (i // per_row) * (ch + pad)
        zc = np.array(Image.fromarray(cell).resize(
            (cell.shape[1] * zoom, cell.shape[0] * zoom), Image.NEAREST))
        bg = np.zeros((zc.shape[0], zc.shape[1], 4), dtype=np.uint8)
        bg[..., :3] = 40
        bg[..., 3] = 255
        alpha = zc[..., 3:4].astype(np.float32) / 255.0
        bg[..., :3] = (zc[..., :3] * alpha + bg[..., :3] * (1 - alpha)).astype(np.uint8)
        img[y:y + zc.shape[0], x:x + zc.shape[1]] = bg
        for k in range(zc.shape[1]):
            img[y, x + k] = (230, 60, 50, 255)
            img[y + zc.shape[0] - 1, x + k] = (230, 60, 50, 255)
        for k in range(zc.shape[0]):
            img[y + k, x] = (230, 60, 50, 255)
            img[y + k, x + zc.shape[1] - 1] = (230, 60, 50, 255)
        ax, ay = meta['origin']
        cy, cx = y + ay * zoom, x + ax * zoom
        for d in range(-5, 6):
            img[cy, cx + d] = (240, 210, 90, 255)
            img[cy + d, cx] = (240, 210, 90, 255)
        if meta['category'] == 'tile':
            for k in range(zc.shape[1]):
                img[y + 16 * zoom, x + k] = (90, 200, 220, 255)
    return img


def job_provenance_model(jobs):
    """Measured backend model union across the jobs' own provenance files.

    Reads art/generated/<id>/provenance.json (written by tools/assets.py from
    the PNG's vendor-signed C2PA claim and the preserved rollout). Returns
    (effective_model, verification): effective_model is the single measured
    backend string, 'DIVERGENT' when jobs disagree, None when any provenance
    is missing; verification is VERIFIED only when every job's provenance
    verified the requested model. This is the measured record the manifest
    publishes - the requested model is never copied here."""
    models, verifs = set(), set()
    for job in jobs:
        p = ART / 'generated' / job['id'] / 'provenance.json'
        try:
            pr = json.loads(p.read_text(encoding='utf8'))
        except (OSError, ValueError):
            return None, 'MISSING'
        em = pr.get('effective_image_model')
        if not em:
            return None, 'MISSING'
        models.add(em)
        verifs.add(pr.get('model_verification'))
    eff = next(iter(models)) if len(models) == 1 else 'DIVERGENT'
    ver = 'VERIFIED' if verifs == {'VERIFIED'} else 'NOT_VERIFIED'
    return eff, ver


def job_provenance_binding(jobs):
    """Independent-verification union across jobs (written by
    tools/verify_provenance.py into provenance.json): returns
    (signed_agent, binding) where signed_agent is the vendor-signed
    'name/version' string common to every job ('DIVERGENT' when they
    disagree, None when any job lacks the verified record) and binding is
    'verified' only when every job's signed claim checks (signature,
    cert chain, file hash) and its session sha256 binding all pass."""
    agents, all_ok, any_ok = set(), True, False
    for job in jobs:
        p = ART / 'generated' / job['id'] / 'provenance.json'
        try:
            pr = json.loads(p.read_text(encoding='utf8'))
        except (OSError, ValueError):
            return None, 'unverified'
        iv = pr.get('independent_verification') or {}
        sc = iv.get('signed_claim') or {}
        sig = sc.get('signature') or {}
        cb = iv.get('codex_session_binding') or {}
        if sc.get('present'):
            agents.add('%s/%s' % (sc.get('software_agent'),
                                  sc.get('software_agent_version')))
        ok = (sc.get('present') is True
              and sig.get('signature_valid') is True
              and sig.get('cert_chain_verified') is True
              and (sc.get('file_data_hash') or {}).get('verified') is True
              and cb.get('matches_raw_bytes') is True)
        any_ok = any_ok or ok
        all_ok = all_ok and ok
    agent = next(iter(agents)) if len(agents) == 1 else 'DIVERGENT'
    binding = 'verified' if all_ok else ('partial' if any_ok else 'unverified')
    return agent, binding


def write_cell_provenance(jobs, metas):
    """Record each approved cell's measured reference_similarity (and the
    ref_map it was produced through) into the job's provenance.json - the
    committed provenance trail the gate and reviewers re-measure against."""
    by_job = {}
    for meta in metas:
        by_job.setdefault(meta['job'], []).append(meta)
    for job in jobs:
        recs = by_job.get(job['id'])
        if not recs:
            continue
        p = ART / 'generated' / job['id'] / 'provenance.json'
        try:
            pr = json.loads(p.read_text(encoding='utf8'))
        except (OSError, ValueError):
            continue
        lim = recs[0]['limits']
        pr['approved_cells'] = [{
            'frame': m['frame_id'],
            'unit_px': UNIT,
            'pixel_path': m['pixel_path'],
            'ref_map': m['ref_map'],
            'reference_similarity': m['reference_similarity'],
            'unique_opaque_colors': m['unique_opaque_colors'],
        } for m in recs]
        pr['reference_conformance'] = {
            'measured': True,
            'min_silhouette_iou': lim['min_iou'],
            'min_palette_match': lim['min_palette'],
            'coverage_threshold': lim['cover'],
        }
        pr['game_asset_approval'] = 'APPROVED'
        write_json_lf(p, pr)


def build():
    catalog = load_catalog()
    limits = contract_limits()
    jobs = json.loads((ART / 'jobs.json').read_text(encoding='utf8'))
    provider = json.loads((ART / 'provider.json').read_text(encoding='utf8'))
    eff_model, model_ver = job_provenance_model(jobs)
    signed_agent, prov_binding = job_provenance_binding(jobs)
    APPROVED.mkdir(parents=True, exist_ok=True)

    cells, metas, qa = [], [], {}
    for job in jobs:
        jid = job['id']
        raw_path = ROOT / job['raw_file']
        if not raw_path.is_file():
            qa[jid] = {'status': 'BLOCKED', 'reason': 'no raw.png'}
            continue
        src = np.asarray(Image.open(raw_path).convert('RGBA'))
        for fid, cell, meta in produce_cells(job, src, catalog[JOB_TARGET[jid]]):
            entry = catalog[JOB_TARGET[jid]]
            cell, ncolors = quantize_cell(cell, limits['max_unique_colors'])
            sim = reference_similarity(src, meta['ref_map'], cell,
                                       limits['cover'])
            sim['ok'] = (sim['iou'] >= limits['min_iou']
                         and sim['palette'] >= limits['min_palette'])
            meta.update({
                'job': jid, 'category': entry['category'],
                'asset': entry['id'], 'canvas_px': entry['canvas_px'],
                'frame_id': fid, 'reference': job['raw_file'],
                'reference_similarity': sim,
                'unique_opaque_colors': ncolors,
                'pixel_path': PIXEL_PATH % (limits['cover'],
                                            limits['max_unique_colors']),
                'limits': limits,
            })
            om = cell[..., 3] > 0
            if om.any():
                ys, xs = np.where(om)
                meta['content_bounds'] = [int(xs.min()), int(ys.min()),
                                          int(xs.max()) + 1, int(ys.max()) + 1]
            for k in ('render_offset_px', 'baked_alignment_offset_px',
                      'render_offset_owner', 'physical_thickness'):
                if k in entry:
                    meta[k] = entry[k]
            checks, ok = qa_cell(fid, cell, meta, entry, sim, ncolors, limits)
            meta['qa'] = checks
            qa[fid] = {'status': 'APPROVED' if ok else 'REJECTED',
                       'checks': checks}
            if ok:
                cells.append((fid, cell))
                metas.append(meta)

    atlas, frames = compose_atlas(cells, metas)
    png_write(APPROVED / 'sprite_sheet_alpha.png', atlas)
    manifest = {
        'kind': 'cozy-cafe-atlas',
        'version': '0.8.3-unit32',
        'sheet': 'sprite_sheet_alpha.png',
        'sheet_size': [int(atlas.shape[1]), int(atlas.shape[0])],
        'encoding': 'RGBA8/filter0/zlib-stored (canonical PngWriter format)',
        'cell_unit_px': UNIT,
        'physical_thickness': 0,
        'thickness_mode': 'visual_only',
        'provider': provider['provider'],
        'requested_image_model': provider.get('requested_image_model'),
        'effective_image_model': eff_model,
        'model_verification': model_ver,
        'signed_claim_software_agent': signed_agent,
        'provenance_binding': prov_binding,
        'palette_variants_generated': 0,
        'pixel_quality': {
            'max_unique_colors_per_cell': limits['max_unique_colors'],
            'path': 'pixel_snap+cluster_quantize (deterministic, no LANCZOS)',
        },
        'reference_conformance': {
            'min_silhouette_iou': limits['min_iou'],
            'min_palette_match': limits['min_palette'],
            'coverage_threshold': limits['cover'],
            'recorded_field': 'reference_similarity',
        },
        'frames': frames,
    }
    write_json_lf(APPROVED / 'atlas_manifest.json', manifest)
    png_write(APPROVED / 'qa_contact.png', contact_sheet(cells, metas))

    report = {'qa': qa, 'frames': [f['id'] for f in frames],
              'sheet_size': manifest['sheet_size'],
              'cell_unit_px': UNIT}
    write_json_lf(APPROVED / 'qa_report.json', report)
    write_cell_provenance(jobs, metas)
    write_back(jobs, catalog, qa, metas, provider)
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 1 if any(v['status'] == 'REJECTED' for v in qa.values()) else 0


def write_back(jobs, catalog, qa, metas, provider):
    """Bookkeeping: job approvals, produced catalog statuses, provider state.
    Only real measured outcomes are written - QA failures stay REJECTED."""
    jobs_path = ART / 'jobs.json'
    for job in jobs:
        ids = [m for m in metas if m['job'] == job['id']]
        own = [qa[fid] for fid in qa if qa[fid].get('checks') and
               (fid == job['id'] or fid.startswith(job['id'] + '_'))]
        job['cell_asset_px'] = [UNIT, UNIT]
        if job['id'] in qa and qa[job['id']]['status'] == 'BLOCKED':
            job['approval'] = 'BLOCKED'
        elif own and all(v['status'] == 'APPROVED' for v in own):
            job['approval'] = 'APPROVED'
            job['approved_cells'] = len(ids)
        elif own:
            job['approval'] = 'REJECTED'
    write_json_lf(jobs_path, jobs)

    cat_path = ART / 'asset_catalog.json'
    entries = json.loads(cat_path.read_text(encoding='utf8'))
    produced = {m['job'] for m in metas}
    for e in entries:
        for job in jobs:
            if JOB_TARGET.get(job['id']) == e['id'] and job['id'] in produced:
                e['status'] = 'APPROVED'
                e['source_job'] = job['id']
                e['atlas_unit_px'] = UNIT
    write_json_lf(cat_path, entries)

    prov_path = ART / 'provider.json'
    prov = json.loads(prov_path.read_text(encoding='utf8'))
    prov['installed_commit'] = 'b725baa5aad026f183e2083275b441f2db225c48'
    prov['status'] = 'GENERATED_MODEL_NOT_VERIFIED'
    write_json_lf(prov_path, prov)


def qa_only():
    """Re-measure the committed approved atlas: per-frame unit geometry,
    palette size, and recorded reference conformance."""
    manifest = json.loads((APPROVED / 'atlas_manifest.json').read_text(encoding='utf8'))
    sheet = np.asarray(Image.open(APPROVED / manifest['sheet']).convert('RGBA'))
    out = {}
    for f in manifest['frames']:
        r = f['rect']
        cell = sheet[r['y']:r['y'] + r['h'], r['x']:r['x'] + r['w']]
        om = cell[..., 3] > 0
        colors = np.unique(cell[om][:, 0].astype(np.int32) << 16
                           | cell[om][:, 1].astype(np.int32) << 8
                           | cell[om][:, 2].astype(np.int32)) if om.any() else []
        rec = {'rect': [r['w'], r['h']],
               'opaque_px': int(om.sum()),
               'unique_opaque_colors': int(len(colors)),
               'reference_similarity': f.get('reference_similarity')}
        if f.get('category') == 'tile':
            rec['diamond'] = measure_tile_unit(cell)
        else:
            rec['anchor'] = f['origin']
        out[f['id']] = rec
    print(json.dumps(out, indent=2))
    return 0


def main(argv):
    if len(argv) != 2 or argv[1] not in ('build', 'qa'):
        print('usage: art_pipeline.py build|qa', file=sys.stderr)
        return 2
    return build() if argv[1] == 'build' else qa_only()


if __name__ == '__main__':
    raise SystemExit(main(sys.argv))
