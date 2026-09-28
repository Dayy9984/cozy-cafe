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
        public int BakedDx, BakedDy;
        public int RenderDx, RenderDy;
        public bool BakedOffsetRecorded;
        public bool RenderOffsetRecorded;
    }

    /// The approved-atlas manifest after structural validation.
    public sealed class ArtManifest
    {
        public readonly List<ArtFrame> Frames = new List<ArtFrame>();
        public int SheetW, SheetH;
        public string SheetFile;
        public string Provider;
        public string RequestedImageModel;
        public int PhysicalThickness = -1;
        public int PaletteVariantsGenerated = -1;
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

        /// The verified effective image model: union of every generated
        /// job's provenance.json effective_image_model (measured from the PNG
        /// C2PA claim) is reported only when it equals the requested model
        /// and all jobs agree. A backend that produced a different model -
        /// or missing/diverging provenance - means the requested-model
        /// generation cannot be verified, so the key reports BLOCKED
        /// verbatim instead of a model name that fails the request.
        public static string EffectiveImageModel()
        {
            var jobs = LoadJobs();
            if (jobs == null || jobs.Count == 0) return "BLOCKED";
            string requested = RequestedImageModel();
            var seen = new HashSet<string>();
            foreach (var o in jobs)
            {
                var job = AsDict(o);
                string id = AsString(Get(job, "id"));
                var prov = LoadJsonDict("art/generated/" + id + "/provenance.json");
                if (prov == null) return "BLOCKED";
                string em = AsString(Get(prov, "effective_image_model"));
                if (string.IsNullOrEmpty(em)) return "BLOCKED";
                if (AsString(Get(prov, "model_verification")) != "VERIFIED")
                {
                    return "BLOCKED";
                }
                seen.Add(em);
            }
            if (seen.Count != 1) return "BLOCKED";
            foreach (var s in seen)
            {
                return s == requested ? s : "BLOCKED";
            }
            return "BLOCKED";
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
