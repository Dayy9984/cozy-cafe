"""Post-generation asset pipeline: extract real generated art into contract
cells, run v0.8 geometry QA, compose the approved atlas + manifest, and emit
the QA contact sheet. Deterministic measurement only - QA failures are
reported, never edited into compliance.

  python tools/art_pipeline.py build      extract -> qa -> compose -> approve
  python tools/art_pipeline.py qa         re-measure approved art and report

Runs under the sprite-gen venv interpreter (numpy + Pillow present).
Approved PNGs are written in the same canonical encoding as the core's
PngWriter (RGBA8, filter 0, zlib stored blocks) so the shared C# PngReader
decodes them byte-for-byte on both hosts.
"""
import json, math, struct, sys, zlib
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'art'
APPROVED = ART / 'approved'
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
SIDE_PX = 4


def write_json_lf(path, obj):
    with open(path, 'w', encoding='utf8', newline='') as f:
        f.write(json.dumps(obj, ensure_ascii=False, indent=2) + chr(10))


def load_catalog():
    return {e['id']: e for e in json.loads((ART / 'asset_catalog.json').read_text(encoding='utf8'))}


def opaque_bbox(a):
    m = a[..., 3] > ALPHA_CUT
    if not m.any():
        return None
    ys, xs = np.where(m)
    return int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1


def column_spans(a, min_gap=12, min_width=8):
    """Split a horizontal arrangement into opaque column runs (icon strips,
    two-machine sheets). One-component images return a single span."""
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


def widest_row(mask):
    """Row index with the most opaque pixels (the iso diamond equator)."""
    return int(np.argmax(mask.sum(axis=1)))


def paste_fit(src_rgba, cell_w, cell_h, anchor_xy, fit_box):
    """Uniformly scale src to fit fit_box, then place its bottom-center on
    anchor_xy inside the cell. Returns (cell, bounds)."""
    cell = np.zeros((cell_h, cell_w, 4), dtype=np.uint8)
    fw, fh = fit_box
    sw, sh = src_rgba.shape[1], src_rgba.shape[0]
    s = min(fw / sw, fh / sh)
    nw, nh = max(1, int(round(sw * s))), max(1, int(round(sh * s)))
    arr = np.array(Image.fromarray(src_rgba).resize((nw, nh), Image.LANCZOS))
    ax, ay = anchor_xy
    ox, oy = int(round(ax - nw / 2)), int(round(ay - nh))
    ox = min(max(0, ox), cell_w - nw)
    oy = min(max(0, oy), cell_h - nh)
    dst = cell[oy:oy + nh, ox:ox + nw]
    keep = arr[..., 3] > 0
    dst[keep] = arr[keep]
    return cell, (ox, oy, ox + nw, oy + nh)


def extract_tile(src_rgba, cell_w=64, cell_h=64):
    """Fit a generated iso floor tile onto the 64x64 working canvas.

    The full top-face rhombus (top vertex to front vertex, equator at the
    widest row) is masked out of the source and resampled to the exact 64x32
    top-face band at canvas rows 0-31. The side skirt below the rhombus's
    lower edges is resampled through the same iso transform into the 4 px
    edge band straddling (0,16)->(32,32) and (32,32)->(64,16) - visual only,
    silhouette stays inside rows 0-35, physical thickness stays 0.
    Returns (cell, meta)."""
    m = src_rgba[..., 3] > ALPHA_CUT
    rows = np.where(m.any(axis=1))[0]
    top_y, bot_y = int(rows[0]), int(rows[-1])
    eq = widest_row(m)
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

    top_art = np.zeros_like(src_rgba)
    top_art[diamond & m] = src_rgba[diamond & m]
    cell = np.zeros((cell_h, cell_w, 4), dtype=np.uint8)
    t = np.array(Image.fromarray(top_art[top_y:front_y + 1]).resize(
        (cell_w, 32), Image.LANCZOS))
    t[t[..., 3] <= ALPHA_CUT] = 0
    cell[0:32] = t

    # Side skirt: real generated pixels below the rhombus's lower edges,
    # reprojected with the same iso scale into the 4 px edge band.
    s = cell_w / (2.0 * half_w + 1.0)
    sy = 32.0 / max(1, front_y - top_y)
    side_mask = m & ~diamond & (np.arange(m.shape[0])[:, None] > eq)
    side = np.zeros_like(src_rgba)
    side[side_mask] = src_rgba[side_mask]
    if side_mask.any():
        srows = np.where(side_mask.any(axis=1))[0]
        scols = np.where(side_mask.any(axis=0))[0]
        sb = side[int(srows[0]):int(srows[-1]) + 1, int(scols[0]):int(scols[-1]) + 1]
        tw = max(1, int(round((int(scols[-1]) - int(scols[0]) + 1) * s)))
        th = max(1, int(round((int(srows[-1]) - int(srows[0]) + 1) * sy)))
        s_arr = np.array(Image.fromarray(sb).resize((tw, th), Image.LANCZOS))
        # paste origin: skirt bbox top-left through the iso transform
        px = int(round((int(scols[0]) - cx) * s + 32))
        py = int(round(16 + (int(srows[0]) - eq) * sy))
        for yy in range(th):
            cy = py + yy
            if cy < 16 or cy >= min(cell_h, 36):
                continue
            for xx in range(tw):
                cxp = px + xx
                if cxp < 0 or cxp >= cell_w or s_arr[yy, xx, 3] <= ALPHA_CUT:
                    continue
                if in_side_band(cxp, cy):
                    cell[cy, cxp] = s_arr[yy, xx]
    meta = {'top_face_rows': [0, 32],
            'side_band': 'straddle(0,16)-(32,32)-(64,16), visual 4px'}
    return cell, meta


def in_side_band(x, y):
    """Point in either 4px side-face parallelogram under the rhombus's lower
    edges: (0,16)-(32,32) left, (32,32)-(64,16) right, each shifted down 4."""
    # left face quad (0,16)(32,32)(32,36)(0,20); right face quad
    # (64,16)(32,32)(32,36)(64,20). Band = edge + 0..4px down.
    dl = (y - 16) * 2.0          # x on left upper edge at row y
    dr = 64.0 - (y - 16) * 2.0   # x on right upper edge at row y
    on_l = (16 <= y <= 36) and (dl - 8 <= x <= dl + 2)
    on_r = (16 <= y <= 36) and (dr - 2 <= x <= dr + 8)
    return on_l or on_r


def extract_job(job, catalog):
    """Extract the job's raw.png into contract cells. Returns
    (frame_id, cell_array, meta) tuples."""
    raw_path = ROOT / job['raw_file']
    a = np.asarray(Image.open(raw_path).convert('RGBA'))
    cat = job.get('category')
    entry = catalog[JOB_TARGET[job['id']]]
    cw, ch = entry['canvas_px']

    if cat == 'tile':
        b = opaque_bbox(a)
        sub = a[b[1]:b[3], b[0]:b[2]]
        cell, meta = extract_tile(sub, cw, ch)
        meta.update({'anchor': [32, 32], 'origin': 'top_center_diamond'})
        return [(job['id'], cell, meta)]

    comps = []
    for (x0, x1) in column_spans(a):
        sub = a[:, x0:x1]
        b = opaque_bbox(sub)
        if b:
            comps.append(sub[b[1]:b[3], b[0]:b[2]])
    if cat in ('machine', 'character', 'furniture'):
        comps.sort(key=lambda c: int((c[..., 3] > ALPHA_CUT).sum()), reverse=True)
        comps = comps[:1]
    elif cat == 'ui':
        comps = comps[:8]
    cells = []
    for i, comp in enumerate(comps):
        fid = job['id'] if len(comps) == 1 else '%s_%d' % (job['id'], i)
        if cat == 'character':
            cell, b = paste_fit(comp, cw, ch, (cw // 2, 72), (cw - 12, 70))
            meta = {'anchor': [cw // 2, 72], 'origin': 'foot_anchor'}
        elif cat == 'machine':
            cell, b = paste_fit(comp, cw, ch, (cw // 2, ch - 12), (cw - 24, ch - 24))
            meta = {'anchor': [cw // 2, ch - 12], 'origin': 'ground_bottom_center'}
        elif cat == 'furniture':
            cell, b = paste_fit(comp, cw, ch, (cw // 2, ch - 24), (cw - 32, ch - 40))
            meta = {'anchor': [cw // 2, ch - 24], 'origin': 'ground_bottom_center'}
        else:
            cell, b = paste_fit(comp, cw, ch, (cw // 2, ch - 4), (cw - 6, ch - 6))
            meta = {'anchor': [cw // 2, ch // 2], 'origin': 'center'}
        meta['content_bounds'] = list(b)
        cells.append((fid, cell, meta))
    return cells


def measure_diamond(cell):
    """Measure the top-face rhombus of a tile cell (canvas rows 0-31: apex at
    row 0, equator at the midline ~row 16, front vertex at row 31) plus the
    side-band silhouette below it."""
    m = cell[..., 3] > ALPHA_CUT
    if not m.any():
        return None
    top = m[:32]
    widths = top.sum(axis=1)
    rows = np.where(m.any(axis=1))[0]
    eq = int(np.argmax(widths))
    filled = int((widths > 0).sum())
    return {
        'top_row': int(np.where(widths > 0)[0][0]) if filled else -1,
        'equator_row': eq,
        'top_face_h': filled,
        'max_width': int(widths[eq]),
        'rhombus_bottom_row': int(np.where(widths > 0)[0][-1]) if filled else -1,
        'silhouette_bottom_row': int(rows[-1]),
        'side_h': max(0, int(rows[-1]) - 31),
    }


def qa_cell(fid, cell, meta, entry):
    """Geometry QA vs the catalog contract. Returns (checks, ok)."""
    cat = entry['category']
    cw, ch = entry['canvas_px']
    checks = {'canvas_w': int(cell.shape[1]), 'canvas_h': int(cell.shape[0]),
              'contract_canvas': [cw, ch]}
    ok = cell.shape[1] == cw and cell.shape[0] == ch
    if cat == 'tile':
        d = measure_diamond(cell)
        checks['diamond'] = d
        ok = ok and d is not None and d['top_row'] == 0 and d['max_width'] == 64             and d['top_face_h'] == 32 and 12 <= d['equator_row'] <= 20             and 0 <= d['side_h'] <= SIDE_PX
        checks['top_face_px'] = [d['max_width'], d['top_face_h']] if d else None
    elif cat == 'furniture':
        checks['anchor'] = meta['anchor']
        checks['unbaked_pivot'] = True
        ok = ok and meta.get('content_bounds') is not None
    checks['opaque_px'] = int((cell[..., 3] > ALPHA_CUT).sum())
    checks['nonempty'] = checks['opaque_px'] > 0
    ok = ok and checks['nonempty']
    return checks, ok


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
    """Pack cells left-to-right with 2px padding into the alpha sheet and
    declare every frame explicitly: rect, origin, fps, loop - no
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
             'origin': {'x': meta['anchor'][0], 'y': meta['anchor'][1]},
             'fps': meta.get('fps', 0), 'loop': meta.get('loop', False)}
        for k in ('render_offset_px', 'baked_alignment_offset_px',
                  'render_offset_owner', 'physical_thickness', 'job',
                  'category', 'content_bounds', 'top_face_px'):
            if k in meta:
                f[k] = meta[k]
        frames.append(f)
        x += cw + pad
    return atlas, frames


def contact_sheet(cells, metas, zoom=2):
    """QA contact sheet: every approved cell magnified with its contract
    canvas border (red), ground anchor crosshair (yellow), and the 64x32
    top-face band guide for tiles (cyan). Measurement evidence."""
    pad = 10
    cw = max(cell.shape[1] for _, cell in cells) * zoom
    ch = max(cell.shape[0] for _, cell in cells) * zoom
    w = (cw + pad) * len(cells) + pad
    h = ch + pad * 2
    img = np.zeros((h, w, 4), dtype=np.uint8)
    img[..., 0], img[..., 1], img[..., 2], img[..., 3] = 24, 24, 30, 255
    x = pad
    for (fid, cell), meta in zip(cells, metas):
        zc = np.array(Image.fromarray(cell).resize(
            (cell.shape[1] * zoom, cell.shape[0] * zoom), Image.NEAREST))
        bg = np.zeros((zc.shape[0], zc.shape[1], 4), dtype=np.uint8)
        bg[..., :3] = 40
        bg[..., 3] = 255
        alpha = zc[..., 3:4].astype(np.float32) / 255.0
        bg[..., :3] = (zc[..., :3] * alpha + bg[..., :3] * (1 - alpha)).astype(np.uint8)
        img[pad:pad + zc.shape[0], x:x + zc.shape[1]] = bg
        for i in range(zc.shape[1]):
            img[pad, x + i] = (230, 60, 50, 255)
            img[pad + zc.shape[0] - 1, x + i] = (230, 60, 50, 255)
        for i in range(zc.shape[0]):
            img[pad + i, x] = (230, 60, 50, 255)
            img[pad + i, x + zc.shape[1] - 1] = (230, 60, 50, 255)
        ax, ay = meta['anchor']
        cy, cx = pad + ay * zoom, x + ax * zoom
        for dx in range(-5, 6):
            img[cy, cx + dx] = (240, 210, 90, 255)
        for dy in range(-5, 6):
            img[cy + dy, cx] = (240, 210, 90, 255)
        if meta['category'] == 'tile':
            for i in range(zc.shape[1]):
                img[pad + 32 * zoom, x + i] = (90, 200, 220, 255)
        x += zc.shape[1] + pad
    return img


def build():
    catalog = load_catalog()
    jobs = json.loads((ART / 'jobs.json').read_text(encoding='utf8'))
    provider = json.loads((ART / 'provider.json').read_text(encoding='utf8'))
    APPROVED.mkdir(parents=True, exist_ok=True)

    cells, metas, qa = [], [], {}
    for job in jobs:
        jid = job['id']
        raw_path = ROOT / job['raw_file']
        if not raw_path.is_file():
            qa[jid] = {'status': 'BLOCKED', 'reason': 'no raw.png'}
            continue
        for fid, cell, meta in extract_job(job, catalog):
            entry = catalog[JOB_TARGET[jid]]
            meta['job'] = jid
            meta['category'] = entry['category']
            for k in ('render_offset_px', 'baked_alignment_offset_px',
                      'render_offset_owner', 'physical_thickness'):
                if k in entry:
                    meta[k] = entry[k]
            checks, ok = qa_cell(fid, cell, meta, entry)
            meta['qa'] = checks
            qa[fid] = {'status': 'APPROVED' if ok else 'REJECTED', 'checks': checks}
            if ok:
                cells.append((fid, cell))
                metas.append(meta)

    atlas, frames = compose_atlas(cells, metas)
    png_write(APPROVED / 'sprite_sheet_alpha.png', atlas)
    manifest = {
        'kind': 'cozy-cafe-atlas',
        'version': '0.8',
        'sheet': 'sprite_sheet_alpha.png',
        'sheet_size': [int(atlas.shape[1]), int(atlas.shape[0])],
        'encoding': 'RGBA8/filter0/zlib-stored (canonical PngWriter format)',
        'physical_thickness': 0,
        'thickness_mode': 'visual_only',
        'provider': provider['provider'],
        'requested_image_model': provider.get('requested_image_model'),
        'palette_variants_generated': 0,
        'frames': frames,
    }
    write_json_lf(APPROVED / 'atlas_manifest.json', manifest)
    png_write(APPROVED / 'qa_contact.png', contact_sheet(cells, metas))

    report = {'qa': qa, 'frames': [f['id'] for f in frames],
              'sheet_size': manifest['sheet_size']}
    write_json_lf(APPROVED / 'qa_report.json', report)
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
        if job['id'] in qa and qa[job['id']]['status'] == 'BLOCKED':
            job['approval'] = 'BLOCKED'
        elif own and all(v['status'] == 'APPROVED' for v in own):
            job['approval'] = 'APPROVED'
            job['approved_cells'] = [m['qa'] for m in metas if m['job'] == job['id']] and                 len([m for m in metas if m['job'] == job['id']])
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
    write_json_lf(cat_path, entries)

    prov_path = ART / 'provider.json'
    prov = json.loads(prov_path.read_text(encoding='utf8'))
    prov['installed_commit'] = 'b725baa5aad026f183e2083275b441f2db225c48'
    prov['status'] = 'GENERATED_MODEL_NOT_VERIFIED'
    write_json_lf(prov_path, prov)


def qa_only():
    manifest = json.loads((APPROVED / 'atlas_manifest.json').read_text(encoding='utf8'))
    sheet = np.asarray(Image.open(APPROVED / manifest['sheet']).convert('RGBA'))
    out = {}
    for f in manifest['frames']:
        r = f['rect']
        cell = sheet[r['y']:r['y'] + r['h'], r['x']:r['x'] + r['w']]
        out[f['id']] = measure_diamond(cell) if f.get('category') == 'tile'             else {'bounds': f.get('content_bounds'), 'anchor': f['origin']}
    print(json.dumps(out, indent=2))
    return 0


def main(argv):
    if len(argv) != 2 or argv[1] not in ('build', 'qa'):
        print('usage: art_pipeline.py build|qa', file=sys.stderr)
        return 2
    return build() if argv[1] == 'build' else qa_only()


if __name__ == '__main__':
    raise SystemExit(main(sys.argv))
