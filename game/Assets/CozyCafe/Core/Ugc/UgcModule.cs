using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using CozyCafe.Core.Layout;
using CozyCafe.Core.Modules;
using CozyCafe.Core.Render;
using CozyCafe.Core.Save;
using CozyCafe.Core.Scene;
using CozyCafe.Core.Ui;

namespace CozyCafe.Core.Ugc
{
    /// What an imported sprite becomes. ui_panel_skin feeds the shared
    /// 9-slice UI layer; furniture/decor skins join the appearance layer;
    /// floor/wall skins restyle surfaces. Everything else is rejected.
    public enum UgcRoleKind
    {
        UiPanelSkin = 0,
        FloorTileSkin = 1,
        WallSkin = 2,
        FurnitureSkin = 3,
        DecorSprite = 4
    }

    /// A furniture placement referenced by a preset — logical cells only,
    /// applied through the real editor validity checks.
    public sealed class PlacementRef
    {
        public FurnitureKind Kind;
        public int X, Y, Q;
        public string AssetId;
    }

    /// An appearance override referenced by a preset: a target id
    /// ("furniture:counter", "ui:panel") bound to a content asset id —
    /// never a file path, never private save data.
    public sealed class AppearanceRef
    {
        public string Target;
        public string AssetId;
    }

    /// The live creator draft between import and save.
    public sealed class UgcDraft
    {
        public string Name = "";
        public SoftwareCanvas Image;
        public byte[] SourcePng;
        public UgcRoleKind? Role;
        public double AnchorX = 0.5;
        public double AnchorY = 0.5;
        /// Facing id for furniture skins ("SE"/"SW"/"NE"/"NW"), "" = all.
        public string Direction = "";
        public string AssetId;
        public string PresetId;
        public SoftwareCanvas Preview;
        public readonly List<string> Problems = new List<string>();
        public readonly List<PlacementRef> Placements = new List<PlacementRef>();
        public readonly List<AppearanceRef> Appearances = new List<AppearanceRef>();
    }

    /// What an apply did: placed furniture ids (through the real editor),
    /// rejected placement refs, installed skin refs, and assets that fell
    /// back because their file was missing.
    public sealed class UgcInstall
    {
        public string PresetId;
        public readonly List<int> PlacedIds = new List<int>();
        public readonly List<PlacementRef> Rejected = new List<PlacementRef>();
        public readonly List<string> SkinRefs = new List<string>();
        public readonly List<string> FallbackAssets = new List<string>();
    }

    /// <summary>
    /// The local UGC creator: PNG import -> role/anchor/direction ->
    /// preview -> validation -> save -> apply. Saved presets are public
    /// documents holding placement/appearance references only — coins,
    /// research, staff stats, memos, file paths and auth fields never
    /// enter them. Assets are stored locally by content id and referenced
    /// by sha256, so a missing or removed asset falls back to defaults
    /// without stripping owned functional machines.
    /// </summary>
    public sealed class UgcModule : ModuleBase
    {
        public override string Name { get { return "ugc"; } }

        /// Directory the creator saves into (host-provided; the module
        /// never invents absolute paths inside shared presets).
        public string StoreDir;

        public UgcDraft Draft { get; private set; }

        /// Installed appearance overrides: target id -> content asset id.
        /// A null value records "this target intentionally uses the
        /// default skin" (its asset was removed or never installed).
        public readonly Dictionary<string, string> AppearanceByTarget =
            new Dictionary<string, string>();

        /// Saved preset documents, newest last — the module's own record
        /// of what it wrote (the files on disk are the source of truth).
        public readonly List<Dictionary<string, object>> SavedPresets =
            new List<Dictionary<string, object>>();

        private int nextPreset = 1;

        // ---- 1. import -------------------------------------------------

        /// Decodes the PNG the user dropped in. Failure to decode rejects
        /// the import at stage one — no draft, no half-state.
        public UgcDraft BeginImport(byte[] pngBytes, string name)
        {
            SoftwareCanvas img;
            if (pngBytes == null || !PngReader.TryDecode(pngBytes, out img))
            {
                Draft = null;
                return null;
            }
            Draft = new UgcDraft
            {
                Name = name ?? "",
                Image = img,
                SourcePng = pngBytes
            };
            return Draft;
        }

        // ---- 2. role / anchor / direction ------------------------------

        /// Assigns what the sprite is for, its pivot (normalized 0..1) and
        /// an optional facing direction. Invalid combinations are recorded
        /// on the draft and rejected here — validation reports them too.
        public bool Assign(string role, double anchorX, double anchorY,
            string direction)
        {
            if (Draft == null) return false;
            UgcRoleKind r;
            if (!TryParseRole(role, out r))
            {
                Draft.Problems.Add("unknown role: " + (role ?? "null"));
                return false;
            }
            if (double.IsNaN(anchorX) || double.IsNaN(anchorY)
                || anchorX < 0 || anchorX > 1 || anchorY < 0 || anchorY > 1)
            {
                Draft.Problems.Add("anchor outside [0,1]");
                return false;
            }
            string dir = direction ?? "";
            if (!ValidDirection(r, dir))
            {
                Draft.Problems.Add("direction '" + dir + "' invalid for " + role);
                return false;
            }
            Draft.Role = r;
            Draft.AnchorX = anchorX;
            Draft.AnchorY = anchorY;
            Draft.Direction = dir;
            return true;
        }

        public void AddPlacement(FurnitureKind kind, int x, int y, int quarterTurns)
        {
            if (Draft == null) return;
            Draft.Placements.Add(new PlacementRef
            {
                Kind = kind, X = x, Y = y,
                Q = ((quarterTurns % 4) + 4) % 4
            });
        }

        /// Binds this import's appearance to a target id. The ref travels
        /// with the preset; the actual PNG stays in the local store.
        public void AddAppearance(string target)
        {
            if (Draft == null || string.IsNullOrEmpty(target)) return;
            Draft.Appearances.Add(new AppearanceRef { Target = target });
        }

        // ---- 3. preview -------------------------------------------------

        /// Renders the draft into a real preview cell: dark ground, the
        /// decoded image fitted by its anchor, and a role stripe. This is
        /// the same pixels the creator dialog shows — generated from the
        /// imported image, not a stand-in.
        public SoftwareCanvas BuildPreview(int w, int h)
        {
            if (Draft == null || Draft.Image == null) return null;
            var c = new SoftwareCanvas(w, h);
            c.Clear(new Rgba(22, 20, 28, 255));
            var img = Draft.Image;
            double sx = Math.Min((w - 8.0) / img.Width, (h - 16.0) / img.Height);
            if (sx > 2.0) sx = 2.0;
            int pw = Math.Max(1, (int)(img.Width * sx));
            int ph = Math.Max(1, (int)(img.Height * sx));
            int ox = (int)(w / 2.0 - Draft.AnchorX * pw);
            int oy = (int)(h / 2.0 - Draft.AnchorY * ph);
            for (int y = 0; y < ph; y++)
            {
                for (int x = 0; x < pw; x++)
                {
                    var px = img.GetPixel((int)(x / sx), (int)(y / sx));
                    if (px.A != 0) c.SetPixel(ox + x, oy + y, px);
                }
            }
            // anchor crosshair — the pivot the user just set, painted for real.
            int ax = ox + (int)(Draft.AnchorX * pw);
            int ay = oy + (int)(Draft.AnchorY * ph);
            for (int d = -3; d <= 3; d++)
            {
                c.SetPixel(ax + d, ay, Rgba.Opaque(255, 220, 90));
                c.SetPixel(ax, ay + d, Rgba.Opaque(255, 220, 90));
            }
            c.FillRect(0, h - 6, w, 6, Rgba.Opaque(70, 110, 160));
            Draft.Preview = c;
            return c;
        }

        // ---- 4. validation ----------------------------------------------

        /// Full draft audit: decoded image, sane bounds, opaque content,
        /// name, assigned role, in-range anchor, role-legal direction.
        /// Returns the live problem list (also recorded on the draft).
        public List<string> Validate()
        {
            var p = new List<string>();
            if (Draft == null)
            {
                p.Add("no import in progress");
                return p;
            }
            if (Draft.Image == null)
            {
                p.Add("image missing");
            }
            else
            {
                int iw = Draft.Image.Width, ih = Draft.Image.Height;
                if (iw < 4 || ih < 4 || iw > 512 || ih > 512)
                {
                    p.Add("image size out of range");
                }
                bool any = false;
                var px = Draft.Image.Pixels;
                for (int i = 3; i < px.Length; i += 4)
                {
                    if (px[i] != 0) { any = true; break; }
                }
                if (!any) p.Add("fully transparent image");
            }
            if (string.IsNullOrEmpty(Draft.Name)) p.Add("name empty");
            if (Draft.Role == null)
            {
                p.Add("role unset");
            }
            else if (!ValidDirection(Draft.Role.Value, Draft.Direction))
            {
                p.Add("direction invalid for role");
            }
            if (double.IsNaN(Draft.AnchorX) || double.IsNaN(Draft.AnchorY)
                || Draft.AnchorX < 0 || Draft.AnchorX > 1
                || Draft.AnchorY < 0 || Draft.AnchorY > 1)
            {
                p.Add("anchor outside [0,1]");
            }
            Draft.Problems.Clear();
            Draft.Problems.AddRange(p);
            return p;
        }

        // ---- 5. save ----------------------------------------------------

        /// Writes the validated draft to the local store: the canonical
        /// PNG under its content id plus the public preset document —
        /// placement/appearance references only. Returns the preset id,
        /// or null when the draft fails validation.
        public string Save()
        {
            if (Draft == null || string.IsNullOrEmpty(StoreDir)) return null;
            if (Validate().Count != 0) return null;
            string presetId = "preset_" + nextPreset;
            string assetId = "asset_" + nextPreset;
            nextPreset++;
            Draft.PresetId = presetId;
            Draft.AssetId = assetId;
            foreach (var a in Draft.Appearances) a.AssetId = assetId;
            foreach (var p in Draft.Placements) p.AssetId = assetId;

            Directory.CreateDirectory(StoreDir);
            // Canonical re-encode: the stored asset is the decoded image
            // emitted through our PNG writer — same canonical format the
            // renderer reads everywhere else.
            File.WriteAllBytes(AssetPath(assetId),
                PngWriter.Encode(Draft.Image.Width, Draft.Image.Height,
                    Draft.Image.Pixels));
            var doc = BuildPresetDocument(Draft, assetId);
            File.WriteAllText(PresetPath(presetId), MiniJson.ToJson(doc));
            SavedPresets.Add(doc);
            return presetId;
        }

        /// The public preset document for this draft. Keys are an allow
        /// list: version, kind, name, asset records (content id, role,
        /// anchor, direction, size, sha256 — no path), placement refs and
        /// appearance refs. Anything else is excluded by construction.
        private static Dictionary<string, object> BuildPresetDocument(
            UgcDraft d, string assetId)
        {
            var doc = new Dictionary<string, object>();
            doc["preset_version"] = (long)1;
            doc["kind"] = "ugc_package";
            doc["name"] = d.Name;
            var asset = new Dictionary<string, object>();
            asset["asset_id"] = assetId;
            asset["role"] = RoleToString(d.Role.Value);
            asset["anchor_x"] = d.AnchorX;
            asset["anchor_y"] = d.AnchorY;
            asset["direction"] = d.Direction;
            asset["width"] = (long)d.Image.Width;
            asset["height"] = (long)d.Image.Height;
            asset["sha256"] = Sha256Hex(d.SourcePng);
            doc["assets"] = new List<object> { asset };
            var pl = new List<object>();
            foreach (var pr in d.Placements)
            {
                var r = new Dictionary<string, object>();
                r["kind"] = (long)pr.Kind;
                r["x"] = (long)pr.X;
                r["y"] = (long)pr.Y;
                r["q"] = (long)pr.Q;
                r["asset"] = pr.AssetId;
                pl.Add(r);
            }
            doc["placements"] = pl;
            var ap = new List<object>();
            foreach (var ar in d.Appearances)
            {
                var r = new Dictionary<string, object>();
                r["target"] = ar.Target;
                r["asset"] = ar.AssetId;
                ap.Add(r);
            }
            doc["appearances"] = ap;
            return doc;
        }

        // ---- 6. apply ----------------------------------------------------

        /// Applies the saved draft to a live session: placement refs go
        /// through the real LayoutModule validity checks (failures are
        /// reported, never forced) and appearance refs resolve their PNG
        /// in the local store — a missing file installs a recorded
        /// fallback, never touches furniture or the wallet.
        public UgcInstall Apply(CafeSession session)
        {
            var inst = new UgcInstall();
            if (Draft == null || session == null) return inst;
            inst.PresetId = Draft.PresetId;
            foreach (var pr in Draft.Placements)
            {
                var r = session.Layout.TryPlace(pr.Kind, pr.X, pr.Y, pr.Q);
                if (r.Ok) inst.PlacedIds.Add(r.Placed.Id);
                else inst.Rejected.Add(pr);
            }
            foreach (var ar in Draft.Appearances)
            {
                string path = AssetPath(ar.AssetId);
                SoftwareCanvas img;
                if (path == null || !PngReader.TryLoad(path, out img))
                {
                    // Missing asset: the target explicitly falls back to
                    // its default look. Functional state is untouched.
                    AppearanceByTarget[ar.Target] = null;
                    inst.FallbackAssets.Add(ar.AssetId);
                    continue;
                }
                AppearanceByTarget[ar.Target] = ar.AssetId;
                inst.SkinRefs.Add(ar.Target + "=" + ar.AssetId);
            }
            return inst;
        }

        /// Removes an installed appearance: the target drops back to the
        /// default skin. Appearance state only — placed furniture, owned
        /// machines and every private module stay exactly as they were.
        public bool RemoveAppearance(CafeSession session, string assetId)
        {
            bool removed = false;
            var keys = new List<string>(AppearanceByTarget.Keys);
            foreach (var k in keys)
            {
                if (AppearanceByTarget[k] == assetId)
                {
                    AppearanceByTarget[k] = null;
                    removed = true;
                }
            }
            return removed;
        }

        /// The content asset currently styling a target, or null when the
        /// target uses its default look (never installed, removed, or the
        /// file went missing).
        public string AppearanceOf(string target)
        {
            string id;
            return AppearanceByTarget.TryGetValue(target, out id) ? id : null;
        }

        /// Loads a stored asset as a shared 9-slice UI skin. Returns null
        /// when the file is missing — callers keep the default skin.
        public UiSkin InstallUiSkin(string assetId, int borderPx)
        {
            string path = AssetPath(assetId);
            SoftwareCanvas img;
            if (path == null || !PngReader.TryLoad(path, out img)) return null;
            return new UiSkin(assetId, img, borderPx, borderPx, borderPx, borderPx);
        }

        // ---- public preset export ----------------------------------------

        /// Shareable document built from a live session: allow-listed
        /// placement + appearance + preset references only. By construction
        /// it carries no wallet, research, staff stats, memos, file paths
        /// or auth — the private data lives in the save document, which is
        /// never read here.
        public Dictionary<string, object> ExportPublicPreset(
            CafeSession session, string name)
        {
            var p = new Dictionary<string, object>();
            p["preset_version"] = (long)1;
            p["kind"] = "cafe_preset";
            p["name"] = name ?? "";
            p["layout"] = MiniJson.Parse(session.Layout.SaveLayout());
            var agents = new List<object>();
            foreach (var a in session.Layout.Scene.Agents)
            {
                agents.Add((long)a.PresetId);
            }
            p["agent_presets"] = agents;
            var ap = new List<object>();
            foreach (var kv in AppearanceByTarget)
            {
                if (kv.Value == null) continue;
                var r = new Dictionary<string, object>();
                r["target"] = kv.Key;
                r["asset"] = kv.Value;
                ap.Add(r);
            }
            p["appearances"] = ap;
            return p;
        }

        /// True when a preset document actually carries placement or
        /// appearance references — an empty doc would pass the private
        /// audit vacuously, so the gate verifies content too.
        public static bool PresetHasPlacementRefs(
            Dictionary<string, object> doc)
        {
            object o;
            var pl = doc.TryGetValue("placements", out o)
                ? o as List<object> : null;
            if (pl != null && pl.Count > 0) return true;
            var lay = doc.TryGetValue("layout", out o)
                ? o as Dictionary<string, object> : null;
            if (lay != null)
            {
                var furn = lay.TryGetValue("furniture", out o)
                    ? o as List<object> : null;
                if (furn != null && furn.Count > 0) return true;
            }
            var ap = doc.TryGetValue("appearances", out o)
                ? o as List<object> : null;
            return ap != null && ap.Count > 0;
        }

        // ---- store paths + roles ------------------------------------------

        public string AssetPath(string assetId)
        {
            if (string.IsNullOrEmpty(StoreDir) || string.IsNullOrEmpty(assetId))
            {
                return null;
            }
            return Path.Combine(StoreDir, assetId + ".png");
        }

        public string PresetPath(string presetId)
        {
            if (string.IsNullOrEmpty(StoreDir) || string.IsNullOrEmpty(presetId))
            {
                return null;
            }
            return Path.Combine(StoreDir, presetId + ".json");
        }

        public static string RoleToString(UgcRoleKind r)
        {
            switch (r)
            {
                case UgcRoleKind.UiPanelSkin: return "ui_panel_skin";
                case UgcRoleKind.FloorTileSkin: return "floor_tile_skin";
                case UgcRoleKind.WallSkin: return "wall_skin";
                case UgcRoleKind.FurnitureSkin: return "furniture_skin";
                default: return "decor_sprite";
            }
        }

        public static bool TryParseRole(string s, out UgcRoleKind r)
        {
            switch (s)
            {
                case "ui_panel_skin": r = UgcRoleKind.UiPanelSkin; return true;
                case "floor_tile_skin": r = UgcRoleKind.FloorTileSkin; return true;
                case "wall_skin": r = UgcRoleKind.WallSkin; return true;
                case "furniture_skin": r = UgcRoleKind.FurnitureSkin; return true;
                case "decor_sprite": r = UgcRoleKind.DecorSprite; return true;
                default: r = UgcRoleKind.DecorSprite; return false;
            }
        }

        /// Direction legality: only furniture skins may carry a facing
        /// ("" = applies to every facing); other roles are directionless.
        public static bool ValidDirection(UgcRoleKind role, string direction)
        {
            if (role == UgcRoleKind.FurnitureSkin)
            {
                return direction == "" || direction == "SE"
                    || direction == "SW" || direction == "NE"
                    || direction == "NW";
            }
            return string.IsNullOrEmpty(direction);
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(bytes);
                var chars = new char[h.Length * 2];
                for (int i = 0; i < h.Length; i++)
                {
                    chars[i * 2] = "0123456789abcdef"[h[i] >> 4];
                    chars[i * 2 + 1] = "0123456789abcdef"[h[i] & 15];
                }
                return new string(chars);
            }
        }

        /// Probe does the real flow on a throwaway store: encode a PNG,
        /// import it, assign role/anchor/direction, preview, validate and
        /// save — then clean up. Live state and the workspace untouched.
        protected override bool OnProbe()
        {
            var src = new SoftwareCanvas(8, 8);
            src.Clear(new Rgba(0, 0, 0, 0));
            src.FillRect(1, 1, 6, 6, Rgba.Opaque(200, 90, 60));
            byte[] png = PngWriter.Encode(8, 8, src.Pixels);

            var m = new UgcModule();
            var d = m.BeginImport(png, "probe");
            if (d == null || !m.Assign("ui_panel_skin", 0.5, 0.5, ""))
            {
                return false;
            }
            m.AddAppearance("ui:panel");
            if (m.BuildPreview(32, 24) == null) return false;
            if (m.Validate().Count != 0) return false;

            string dir = Path.Combine(Path.GetTempPath(), "cozycafe-ugc-probe");
            try
            {
                m.StoreDir = dir;
                string presetId = m.Save();
                if (presetId == null || !File.Exists(m.PresetPath(presetId))
                    || !File.Exists(m.AssetPath(d.AssetId)))
                {
                    return false;
                }
                var parsed = MiniJson.Parse(
                    File.ReadAllText(m.PresetPath(presetId)))
                    as Dictionary<string, object>;
                return parsed != null && PresetHasPlacementRefs(parsed);
            }
            finally
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
                catch (Exception) { }
            }
        }
    }
}
