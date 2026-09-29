using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CozyCafe.Core.Render;

namespace CozyCafe.Core.Art
{
    /// One explicitly-declared atlas frame: rect + origin + fps + loop are all
    /// read from the manifest, never inferred from a uniform grid.
    public sealed class ArtFrame
    {
        public string Id;
        public int X, Y, W, H;
        public int OriginX, OriginY;
        public double Fps;
        public bool Loop;
        public string Category;
        public string Job;
        public string Asset;
        public int CanvasW, CanvasH;
        public int BakedDx, BakedDy;
        public int RenderDx, RenderDy;
        public bool BakedOffsetRecorded;
        public bool RenderOffsetRecorded;
        public int UniqueColors = -1;
        public string Reference;
        /// ref_map entries as {sx0,sy0,sx1,sy1,dx,dy,dw,dh}: the committed
        /// declaration of which raw-reference region produced which part of
        /// this cell - the gate rebuilds the reference silhouette with it.
        public readonly List<int[]> RefMap = new List<int[]>();
        public double RefIou = -1, RefPalette = -1;
        public bool RefRecordedOk;
    }

    /// The approved-atlas manifest after structural validation.
    public sealed class ArtManifest
    {
        public readonly List<ArtFrame> Frames = new List<ArtFrame>();
        public int SheetW, SheetH;
        public string SheetFile;
        public string Provider;
        public string RequestedImageModel;
        public string SignedAgent;
        public string ProvenanceBinding;
        public int PhysicalThickness = -1;
        public int PaletteVariantsGenerated = -1;
        public int CellUnitPx;
        public string EffectiveImageModel;
        public string ModelVerification;
        public bool Valid;
        public string Error;
    }

    /// Real measurements off an approved tile frame's pixels.
    public sealed class TileMeasure
    {
        public int TopRow = -1;
        public int EquatorRow = -1;
        public int MaxWidth;
        public int TopFaceRows;
        public int SilhouetteBottomRow = -1;
        public int OpaquePx;
    }

    /// <summary>
    /// Loads and measures the real art-pipeline outputs on disk:
    /// art/provider.json, art/jobs.json, per-job provenance, and the approved
    /// atlas + manifest. CASE values come from these files and from decoded
    /// pixels - never from expected gate constants.
    /// </summary>
    public static class ArtAssets
    {
        /// Locates the workspace root: the COZYCAFE_WORKSPACE_ROOT override
        /// (staged Unity copies live outside the workspace, so ancestor
        /// walking can never find it there), then ancestors of the process
        /// working directory and the app base directory for art/provider.json.
        public static string WorkspaceRoot()
        {
            string env = Environment.GetEnvironmentVariable("COZYCAFE_WORKSPACE_ROOT");
            if (!string.IsNullOrEmpty(env)
                && File.Exists(Path.Combine(env, "art", "provider.json")))
            {
                return Path.GetFullPath(env);
            }
            var roots = new List<string>();
            try { roots.Add(Directory.GetCurrentDirectory()); } catch (Exception) { }
            try { roots.Add(AppContext.BaseDirectory); } catch (Exception) { }
            var seen = new HashSet<string>();
            foreach (var r in roots)
            {
                if (string.IsNullOrEmpty(r)) continue;
                DirectoryInfo d;
                try { d = new DirectoryInfo(r); }
                catch (Exception) { continue; }
                for (; d != null; d = d.Parent)
                {
                    if (!seen.Add(d.FullName)) continue;
                    if (File.Exists(Path.Combine(d.FullName, "art", "provider.json")))
                    {
                        return d.FullName;
                    }
                }
            }
            return null;
        }

        public static Dictionary<string, object> LoadJsonDict(string relPath)
        {
            string root = WorkspaceRoot();
            if (root == null) return null;
            string p = Path.Combine(root, relPath.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                if (!File.Exists(p)) return null;
                return AsDict(MiniJson.Parse(File.ReadAllText(p)));
            }
            catch (Exception) { return null; }
        }

        public static List<object> LoadJobs()
        {
            string root = WorkspaceRoot();
            if (root == null) return null;
            string p = Path.Combine(root, "art", "jobs.json");
            try
            {
                if (!File.Exists(p)) return null;
                return AsList(MiniJson.Parse(File.ReadAllText(p)));
            }
            catch (Exception) { return null; }
        }

        public static string Provider()
        {
            var prov = LoadJsonDict("art/provider.json");
            return prov == null ? null : AsString(Get(prov, "provider"));
        }

        public static string RequestedImageModel()
        {
            var prov = LoadJsonDict("art/provider.json");
            return prov == null ? null : AsString(Get(prov, "requested_image_model"));
        }


        /// Every declared job's raw.png exists and carries the PNG signature.
        public static bool RawPngsExist()
        {
            var jobs = LoadJobs();
            if (jobs == null || jobs.Count == 0) return false;
            string root = WorkspaceRoot();
            foreach (var o in jobs)
            {
                var job = AsDict(o);
                string rel = AsString(Get(job, "raw_file"));
                if (string.IsNullOrEmpty(rel)) return false;
                string p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    if (!File.Exists(p)) return false;
                    using (var fs = File.OpenRead(p))
                    {
                        byte[] sig = { 0x89, 0x50, 0x4E, 0x47 };
                        var buf = new byte[4];
                        if (fs.Read(buf, 0, 4) != 4) return false;
                        for (int i = 0; i < 4; i++) if (buf[i] != sig[i]) return false;
                    }
                }
                catch (Exception) { return false; }
            }
            return true;
        }

        /// No OAuth/credential material may ship inside the product tree:
        /// scan art/, data/, tools/, game/Assets/ for credential-named or
        /// token-shaped files and report whether any are bundled.
        public static bool CredentialsBundled()
        {
            string root = WorkspaceRoot();
            if (root == null) return false;
            string[] dirs = { "art", "data", "tools", Path.Combine("game", "Assets") };
            string[] names = { "auth.json", "token", "secret", "credential",
                ".pem", ".key", ".pfx", "oauth", "bearer" };
            foreach (var d in dirs)
            {
                string dir = Path.Combine(root, d);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.EnumerateFiles(dir, "*",
                    SearchOption.AllDirectories))
                {
                    string name = Path.GetFileName(f).ToLowerInvariant();
                    foreach (var n in names)
                    {
                        if (name.Contains(n)) return true;
                    }
                }
            }
            return false;
        }

        /// Parses and structurally validates art/approved/atlas_manifest.json:
        /// every frame must declare an explicit rect, origin, fps and loop,
        /// rects must fit inside the sheet, and the sheet must decode.
        public static ArtManifest LoadManifest()
        {
            var m = new ArtManifest();
            var d = LoadJsonDict("art/approved/atlas_manifest.json");
            if (d == null) { m.Error = "missing/invalid manifest"; return m; }
            m.SheetFile = AsString(Get(d, "sheet"));
            m.Provider = AsString(Get(d, "provider"));
            m.RequestedImageModel = AsString(Get(d, "requested_image_model"));
            m.SignedAgent = AsString(Get(d, "signed_claim_software_agent"));
            m.ProvenanceBinding = AsString(Get(d, "provenance_binding"));
            var size = AsList(Get(d, "sheet_size"));
            if (size != null && size.Count == 2)
            {
                m.SheetW = (int)AsLong(size[0]);
                m.SheetH = (int)AsLong(size[1]);
            }
            object pt = Get(d, "physical_thickness");
            if (pt != null) m.PhysicalThickness = (int)AsLong(pt);
            object pv = Get(d, "palette_variants_generated");
            if (pv != null) m.PaletteVariantsGenerated = (int)AsLong(pv);
            m.CellUnitPx = (int)AsLong(Get(d, "cell_unit_px"));
            m.EffectiveImageModel = AsString(Get(d, "effective_image_model"));
            m.ModelVerification = AsString(Get(d, "model_verification"));

            var frames = AsList(Get(d, "frames"));
            if (frames == null || frames.Count == 0)
            {
                m.Error = "no frames"; return m;
            }
            var ids = new HashSet<string>();
            foreach (var o in frames)
            {
                var f = AsDict(o);
                if (f == null) { m.Error = "bad frame"; return m; }
                var fr = new ArtFrame();
                fr.Id = AsString(Get(f, "id"));
                var rect = AsDict(Get(f, "rect"));
                var org = AsDict(Get(f, "origin"));
                if (string.IsNullOrEmpty(fr.Id) || rect == null || org == null
                    || !f.ContainsKey("fps") || !f.ContainsKey("loop"))
                {
                    m.Error = "frame lacks id/rect/origin/fps/loop"; return m;
                }
                fr.X = (int)AsLong(Get(rect, "x"));
                fr.Y = (int)AsLong(Get(rect, "y"));
                fr.W = (int)AsLong(Get(rect, "w"));
                fr.H = (int)AsLong(Get(rect, "h"));
                fr.OriginX = (int)AsLong(Get(org, "x"));
                fr.OriginY = (int)AsLong(Get(org, "y"));
                fr.Fps = AsDouble(Get(f, "fps"));
                fr.Loop = AsBool(Get(f, "loop"));
                fr.Category = AsString(Get(f, "category"));
                fr.Job = AsString(Get(f, "job"));
                fr.Asset = AsString(Get(f, "asset"));
                fr.Reference = AsString(Get(f, "reference"));
                var cvs = AsList(Get(f, "canvas_px"));
                if (cvs != null && cvs.Count == 2)
                {
                    fr.CanvasW = (int)AsLong(cvs[0]);
                    fr.CanvasH = (int)AsLong(cvs[1]);
                }
                object uc = Get(f, "unique_opaque_colors");
                if (uc != null) fr.UniqueColors = (int)AsLong(uc);
                var refMap = AsList(Get(f, "ref_map"));
                if (refMap != null)
                {
                    foreach (var mo in refMap)
                    {
                        var md = AsDict(mo);
                        var src = md == null ? null : AsList(Get(md, "src"));
                        var dst = md == null ? null : AsList(Get(md, "dst"));
                        if (src != null && src.Count == 4
                            && dst != null && dst.Count == 4)
                        {
                            fr.RefMap.Add(new int[]
                            {
                                (int)AsLong(src[0]), (int)AsLong(src[1]),
                                (int)AsLong(src[2]), (int)AsLong(src[3]),
                                (int)AsLong(dst[0]), (int)AsLong(dst[1]),
                                (int)AsLong(dst[2]), (int)AsLong(dst[3])
                            });
                        }
                    }
                }
                var rs = AsDict(Get(f, "reference_similarity"));
                if (rs != null)
                {
                    fr.RefIou = AsDouble(Get(rs, "iou"));
                    fr.RefPalette = AsDouble(Get(rs, "palette"));
                    fr.RefRecordedOk = AsBool(Get(rs, "ok"));
                }
                var baked = AsList(Get(f, "baked_alignment_offset_px"));
                if (baked != null && baked.Count == 2)
                {
                    fr.BakedOffsetRecorded = true;
                    fr.BakedDx = (int)AsLong(baked[0]);
                    fr.BakedDy = (int)AsLong(baked[1]);
                }
                var ro = AsList(Get(f, "render_offset_px"));
                if (ro != null && ro.Count == 2)
                {
                    fr.RenderOffsetRecorded = true;
                    fr.RenderDx = (int)AsLong(ro[0]);
                    fr.RenderDy = (int)AsLong(ro[1]);
                }
                if (fr.W <= 0 || fr.H <= 0 || fr.X < 0 || fr.Y < 0
                    || fr.X + fr.W > m.SheetW || fr.Y + fr.H > m.SheetH
                    || !ids.Add(fr.Id))
                {
                    m.Error = "frame rect out of sheet"; return m;
                }
                m.Frames.Add(fr);
            }
            SoftwareCanvas sheet;
            if (!PngReader.TryLoad(SheetPath(m), out sheet)
                || sheet.Width != m.SheetW || sheet.Height != m.SheetH)
            {
                m.Error = "sheet missing or size mismatch"; return m;
            }
            m.Valid = true;
            return m;
        }

        public static string SheetPath(ArtManifest m)
        {
            string root = WorkspaceRoot();
            return root == null || m.SheetFile == null ? null
                : Path.Combine(root, "art", "approved", m.SheetFile);
        }

        /// Measures an approved tile frame's real pixels against the v0.8
        /// contract: the rhombus fills canvas rows 0-15 (apex row 0, equator
        /// at the midline ~8, front vertex at row 15, max width 32) and the
        /// side-face silhouette stays within the 2 px visual budget.
        public static TileMeasure MeasureTile(SoftwareCanvas sheet, ArtFrame f)
        {
            var t = new TileMeasure();
            for (int y = 0; y < f.H && y < 36; y++)
            {
                int rowW = 0;
                for (int x = 0; x < f.W; x++)
                {
                    int i = ((f.Y + y) * sheet.Width + f.X + x) * 4;
                    if (sheet.Pixels[i + 3] > 24)
                    {
                        rowW++;
                        t.OpaquePx++;
                        if (t.TopRow < 0) t.TopRow = y;
                        t.SilhouetteBottomRow = y;
                    }
                }
                if (y < 32 && rowW > 0) t.TopFaceRows++;
                if (y < 32 && rowW > t.MaxWidth)
                {
                    t.MaxWidth = rowW;
                    t.EquatorRow = y;
                }
            }
            return t;
        }

        /// The single effective lift for a furniture frame: manifest-declared
        /// baked alignment plus the runtime render offset the renderer adds.
        /// Contract: authored unbaked (0,0) + renderer (0,-8) = -8 once.
        public static int EffectiveFurnitureOffsetY(ArtManifest m, out bool recorded)
        {
            recorded = false;
            int? eff = null;
            foreach (var f in m.Frames)
            {
                if (f.Category != "furniture") continue;
                if (!f.BakedOffsetRecorded || !f.RenderOffsetRecorded) return 0;
                int e = f.BakedDy + f.RenderDy;
                if (eff == null) eff = e;
                else if (eff.Value != e) return 0;
            }
            recorded = eff != null;
            return eff ?? 0;
        }

        /// Independent-verification union across declared jobs: true only
        /// when every job's provenance.json carries a complete
        /// independent_verification record - vendor-signed C2PA claim
        /// present, openssl-verified signature, cert chain and file-data
        /// hash - and its codex session rollout sha256 byte match.
        public static bool ProvenanceSessionBound()
        {
            var jobs = LoadJobs();
            if (jobs == null || jobs.Count == 0) return false;
            foreach (var o in jobs)
            {
                var job = AsDict(o);
                string id = AsString(Get(job, "id"));
                var prov = LoadJsonDict("art/generated/" + id + "/provenance.json");
                if (prov == null) return false;
                var iv = AsDict(Get(prov, "independent_verification"));
                if (iv == null) return false;
                var sc = AsDict(Get(iv, "signed_claim"));
                var cb = AsDict(Get(iv, "codex_session_binding"));
                if (sc == null || !AsBool(Get(sc, "present"))) return false;
                var sig = AsDict(Get(sc, "signature"));
                if (sig == null || !AsBool(Get(sig, "signature_valid"))
                    || !AsBool(Get(sig, "cert_chain_verified"))) return false;
                var fdh = AsDict(Get(sc, "file_data_hash"));
                if (fdh == null || !AsBool(Get(fdh, "verified"))) return false;
                if (cb == null || !AsBool(Get(cb, "matches_raw_bytes"))) return false;
            }
            return true;
        }

        /// Palette variants must recolor shared art, never regenerate sheets:
        /// count declared variant-generation markers in manifest + jobs.
        public static int PaletteVariantGenerations(ArtManifest m)
        {
            int n = 0;
            if (m.PaletteVariantsGenerated > 0) n += m.PaletteVariantsGenerated;
            var jobs = LoadJobs();
            if (jobs != null)
            {
                foreach (var o in jobs)
                {
                    var job = AsDict(o);
                    if (job == null) continue;
                    string cat = AsString(Get(job, "category"));
                    string id = AsString(Get(job, "id"));
                    if ((cat != null && cat.Contains("palette"))
                        || (id != null && id.Contains("palette")))
                    {
                        n++;
                    }
                }
            }
            return n;
        }

        /// Loads a JSON file whose top level is a list (asset_catalog.json).
        public static List<object> LoadJsonList(string relPath)
        {
            string root = WorkspaceRoot();
            if (root == null) return null;
            string p = Path.Combine(root, relPath.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                if (!File.Exists(p)) return null;
                return AsList(MiniJson.Parse(File.ReadAllText(p)));
            }
            catch (Exception) { return null; }
        }

        /// Pixel-quality and reference-conformance thresholds from the data
        /// source (data/art_contract.json) - numbers live in data, not code.
        public static bool ContractLimits(out int maxColors, out double minIou,
            out double minPalette, out double cover)
        {
            maxColors = 0; minIou = 0; minPalette = 0; cover = 0;
            var c = LoadJsonDict("data/art_contract.json");
            if (c == null) return false;
            var pq = AsDict(Get(c, "pixel_quality"));
            var rc = AsDict(Get(c, "reference_conformance"));
            if (pq == null || rc == null) return false;
            object mc = Get(pq, "max_unique_colors_per_cell");
            object mi = Get(rc, "min_silhouette_iou");
            object mp = Get(rc, "min_palette_match");
            object cv = Get(rc, "coverage_threshold");
            if (mc == null || mi == null || mp == null || cv == null) return false;
            maxColors = (int)AsLong(mc);
            minIou = AsDouble(mi);
            minPalette = AsDouble(mp);
            cover = AsDouble(cv);
            return maxColors > 0 && minIou > 0 && minPalette > 0 && cover > 0;
        }

        /// The tile asset's declared authoring canvas height: read from the
        /// produced frame's asset entry in art/asset_catalog.json (the data
        /// source), falling back to data/art_contract.json tile.canvas.
        public static int TileCanvasHeight(ArtManifest m)
        {
            ArtFrame tile = null;
            foreach (var f in m.Frames)
            {
                if (f.Category == "tile") { tile = f; break; }
            }
            if (tile != null && tile.Asset != null)
            {
                var cat = LoadJsonList("art/asset_catalog.json");
                if (cat != null)
                {
                    foreach (var o in cat)
                    {
                        var e = AsDict(o);
                        if (e == null || AsString(Get(e, "id")) != tile.Asset) continue;
                        var cp = AsList(Get(e, "canvas_px"));
                        if (cp != null && cp.Count == 2)
                        {
                            return (int)AsLong(cp[1]);
                        }
                    }
                }
            }
            var c = LoadJsonDict("data/art_contract.json");
            var t = c == null ? null : AsDict(Get(c, "tile"));
            var cv = t == null ? null : AsList(Get(t, "canvas"));
            if (cv != null && cv.Count == 2) return (int)AsLong(cv[1]);
            return tile != null ? tile.CanvasH : 0;
        }

        /// atlas_frames_present: the manifest declares a nonempty frame list,
        /// every declared job in art/jobs.json produced at least one frame,
        /// and every frame rect contains real opaque pixels in the sheet.
        public static bool AtlasFramesPresent(ArtManifest m, SoftwareCanvas sheet)
        {
            if (!m.Valid || sheet == null || m.Frames.Count == 0) return false;
            var jobs = LoadJobs();
            if (jobs == null || jobs.Count == 0) return false;
            foreach (var o in jobs)
            {
                var job = AsDict(o);
                string jid = AsString(Get(job, "id"));
                bool any = false;
                foreach (var f in m.Frames)
                {
                    if (f.Job == jid) { any = true; break; }
                }
                if (!any) return false;
            }
            foreach (var f in m.Frames)
            {
                if (CountOpaque(sheet, f) == 0) return false;
            }
            return true;
        }

        /// Every declared frame rect is exactly the contract unit box.
        public static bool AllFramesUnit(ArtManifest m, int unit)
        {
            if (!m.Valid || m.Frames.Count == 0) return false;
            foreach (var f in m.Frames)
            {
                if (f.W != unit || f.H != unit) return false;
            }
            return true;
        }

        private static int CountOpaque(SoftwareCanvas sheet, ArtFrame f)
        {
            int n = 0;
            for (int y = 0; y < f.H; y++)
            {
                for (int x = 0; x < f.W; x++)
                {
                    int i = ((f.Y + y) * sheet.Width + f.X + x) * 4;
                    if (sheet.Pixels[i + 3] > 24) n++;
                }
            }
            return n;
        }

        /// pixel_art_quantized_cells: every cell is real pixel art - at
        /// least one opaque pixel and at most maxColors distinct opaque
        /// colors, counted on the decoded sheet. A smooth resample lands
        /// hundreds of unique colors per cell and fails.
        public static bool QuantizedCellsOk(SoftwareCanvas sheet,
            ArtManifest m, int maxColors)
        {
            if (!m.Valid || sheet == null || m.Frames.Count == 0) return false;
            foreach (var f in m.Frames)
            {
                var seen = new HashSet<int>();
                int opaque = 0;
                for (int y = 0; y < f.H; y++)
                {
                    for (int x = 0; x < f.W; x++)
                    {
                        int i = ((f.Y + y) * sheet.Width + f.X + x) * 4;
                        if (sheet.Pixels[i + 3] > 24)
                        {
                            opaque++;
                            seen.Add((sheet.Pixels[i] << 16)
                                | (sheet.Pixels[i + 1] << 8)
                                | sheet.Pixels[i + 2]);
                        }
                    }
                }
                if (opaque == 0 || seen.Count > maxColors) return false;
            }
            return true;
        }

        /// <summary>
        /// reference_conformance_ok: for every frame, rebuild the reference
        /// silhouette in cell space from the committed raw reference PNG via
        /// the frame's committed ref_map (block coverage >= cover over the
        /// mapped source region), measure silhouette IoU against the decoded
        /// cell, and measure palette match (share of the cell's opaque pixels
        /// whose 5-bit-per-channel color bin is populated by the reference's
        /// mapped region). A frame passes when iou >= minIou, palette >=
        /// minPalette, and the manifest's recorded reference_similarity
        /// agrees with the re-measurement (recorded provenance is audited,
        /// not trusted).
        /// </summary>
        public static bool ReferenceConformanceOk(SoftwareCanvas sheet,
            ArtManifest m, double minIou, double minPalette, double cover)
        {
            if (!m.Valid || sheet == null || m.Frames.Count == 0) return false;
            string root = WorkspaceRoot();
            if (root == null) return false;
            var refs = new Dictionary<string, SoftwareCanvas>();
            foreach (var f in m.Frames)
            {
                if (string.IsNullOrEmpty(f.Reference) || f.RefMap.Count == 0)
                {
                    return false;
                }
                SoftwareCanvas raw;
                string path = Path.Combine(root,
                    f.Reference.Replace('/', Path.DirectorySeparatorChar));
                if (!refs.TryGetValue(path, out raw))
                {
                    if (!PngReader.TryLoad(path, out raw)) return false;
                    refs[path] = raw;
                }
                var refMask = new bool[f.W * f.H];
                for (int e = 0; e < f.RefMap.Count; e++)
                {
                    var r = f.RefMap[e];
                    int sx0 = r[0], sy0 = r[1], sx1 = r[2], sy1 = r[3];
                    int dx = r[4], dy = r[5], dw = r[6], dh = r[7];
                    for (int py = 0; py < dh; py++)
                    {
                        int ay0 = sy0 + py * (sy1 - sy0) / dh;
                        int ay1 = sy0 + (py + 1) * (sy1 - sy0) / dh;
                        for (int px = 0; px < dw; px++)
                        {
                            int ax0 = sx0 + px * (sx1 - sx0) / dw;
                            int ax1 = sx0 + (px + 1) * (sx1 - sx0) / dw;
                            int tot = 0, op = 0;
                            for (int yy = ay0; yy < ay1; yy++)
                            {
                                for (int xx = ax0; xx < ax1; xx++)
                                {
                                    if (yy < 0 || xx < 0 || yy >= raw.Height
                                        || xx >= raw.Width) continue;
                                    tot++;
                                    if (raw.Pixels[(yy * raw.Width + xx) * 4 + 3] > 24)
                                    {
                                        op++;
                                    }
                                }
                            }
                            if (tot > 0 && (double)op / tot >= cover)
                            {
                                refMask[(dy + py) * f.W + dx + px] = true;
                            }
                        }
                    }
                }
                int inter = 0, uni = 0;
                for (int y = 0; y < f.H; y++)
                {
                    for (int x = 0; x < f.W; x++)
                    {
                        int i = ((f.Y + y) * sheet.Width + f.X + x) * 4;
                        bool c = sheet.Pixels[i + 3] > 24;
                        if (c && refMask[y * f.W + x]) inter++;
                        if (c || refMask[y * f.W + x]) uni++;
                    }
                }
                if (uni == 0) return false;
                double iou = (double)inter / uni;
                if (iou < minIou) return false;

                var bins = new HashSet<int>();
                for (int e = 0; e < f.RefMap.Count; e++)
                {
                    var r = f.RefMap[e];
                    int y1 = Math.Min(r[3], raw.Height);
                    int x1 = Math.Min(r[2], raw.Width);
                    for (int yy = Math.Max(0, r[1]); yy < y1; yy++)
                    {
                        for (int xx = Math.Max(0, r[0]); xx < x1; xx++)
                        {
                            int i = (yy * raw.Width + xx) * 4;
                            if (raw.Pixels[i + 3] > 24)
                            {
                                bins.Add(((raw.Pixels[i] >> 3) << 10)
                                    | ((raw.Pixels[i + 1] >> 3) << 5)
                                    | (raw.Pixels[i + 2] >> 3));
                            }
                        }
                    }
                }
                int hit = 0, cellOpaque = 0;
                for (int y = 0; y < f.H; y++)
                {
                    for (int x = 0; x < f.W; x++)
                    {
                        int i = ((f.Y + y) * sheet.Width + f.X + x) * 4;
                        if (sheet.Pixels[i + 3] > 24)
                        {
                            cellOpaque++;
                            if (bins.Contains(((sheet.Pixels[i] >> 3) << 10)
                                | ((sheet.Pixels[i + 1] >> 3) << 5)
                                | (sheet.Pixels[i + 2] >> 3)))
                            {
                                hit++;
                            }
                        }
                    }
                }
                if (cellOpaque == 0) return false;
                double pal = (double)hit / cellOpaque;
                if (pal < minPalette) return false;

                if (!f.RefRecordedOk || f.RefIou < 0 || f.RefPalette < 0)
                {
                    return false;
                }
                if (Math.Abs(f.RefIou - iou) > 0.05
                    || Math.Abs(f.RefPalette - pal) > 0.05)
                {
                    return false;
                }
            }
            return true;
        }

        /// Unit-tile pixel measure on the approved 32x32 cell: the 32x16
        /// top-face rhombus fills rows 0-15 (apex row 0, equator ~row 8,
        /// front vertex row 15) and the visual side band ends by row 18.
        public static TileMeasure MeasureTileUnit(SoftwareCanvas sheet, ArtFrame f)
        {
            var t = new TileMeasure();
            for (int y = 0; y < f.H && y < 32; y++)
            {
                int rowW = 0;
                for (int x = 0; x < f.W; x++)
                {
                    int i = ((f.Y + y) * sheet.Width + f.X + x) * 4;
                    if (sheet.Pixels[i + 3] > 24)
                    {
                        rowW++;
                        t.OpaquePx++;
                        if (t.TopRow < 0) t.TopRow = y;
                        t.SilhouetteBottomRow = y;
                    }
                }
                if (y < 16 && rowW > 0) t.TopFaceRows++;
                if (y < 16 && rowW > t.MaxWidth)
                {
                    t.MaxWidth = rowW;
                    t.EquatorRow = y;
                }
            }
            return t;
        }

        /// <summary>
        /// The effective image model emitted from the committed approved
        /// manifest's provenance - never a live OAuth/codex probe (the eval
        /// environment may have no codex surface). The recorded measured
        /// backend string is emitted only when the committed verification is
        /// VERIFIED and the measurement equals the recorded request;
        /// otherwise the key honestly reports BLOCKED.
        /// </summary>
        public static string EffectiveImageModel(ArtManifest m)
        {
            if (m == null || string.IsNullOrEmpty(m.EffectiveImageModel))
            {
                return "BLOCKED";
            }
            if (m.ModelVerification != "VERIFIED") return "BLOCKED";
            string requested = RequestedImageModel();
            return m.EffectiveImageModel == requested
                ? m.EffectiveImageModel : "BLOCKED";
        }

        public static Dictionary<string, object> AsDict(object o)
        {
            return o as Dictionary<string, object>;
        }

        public static List<object> AsList(object o)
        {
            return o as List<object>;
        }

        public static string AsString(object o)
        {
            return o as string;
        }

        public static long AsLong(object o)
        {
            if (o is long l) return l;
            if (o is int i) return i;
            if (o is double d) return (long)d;
            if (o is string s) { long v; return long.TryParse(s, out v) ? v : 0; }
            return 0;
        }

        public static double AsDouble(object o)
        {
            if (o is double d) return d;
            if (o is long l) return l;
            if (o is int i) return i;
            if (o is string s) { double v; return double.TryParse(s, out v) ? v : 0; }
            return 0;
        }

        public static bool AsBool(object o)
        {
            return o is bool b && b;
        }

        public static object Get(Dictionary<string, object> d, string key)
        {
            if (d == null) return null;
            object v;
            return d.TryGetValue(key, out v) ? v : null;
        }
    }
}
