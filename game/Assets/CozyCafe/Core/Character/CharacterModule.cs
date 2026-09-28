using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CozyCafe.Core.Modules;
using CozyCafe.Core.Render;

namespace CozyCafe.Core.Character
{
    /// Facing in contract order (data/art_contract.json character.directions).
    /// SW/SE look toward the camera; NW/NE are rear views.
    public enum Facing
    {
        SW = 0,
        SE = 1,
        NW = 2,
        NE = 3
    }

    /// Paint order is fixed: body -> outfit -> apron -> glasses -> hair.
    /// Rear hair covers the whole back of the head, so glasses painted in
    /// front of it end up occluded on rear facings — that is the contract's
    /// "rear views hide glasses under hair" rule, done with draw order and
    /// shapes, never with alpha tricks.
    public enum PartKind
    {
        Body = 0,
        Outfit = 1,
        Apron = 2,
        Glasses = 3,
        Hair = 4
    }

    /// The one shared rig descriptor, sourced from data/art_contract.json.
    public sealed class RigDescriptor
    {
        public string Id;
        public int CellWidth;
        public int CellHeight;
        public int FootAnchorX;
        public int FootAnchorY;
        public readonly List<string> Directions = new List<string>();
        public readonly Dictionary<string, int> States =
            new Dictionary<string, int>();
    }

    /// One starter preset from data/character_presets.json, part ids resolved
    /// to indices inside the data-sourced part tables.
    public sealed class CharacterPreset
    {
        public string Id;
        public int HairIndex = -1;
        public int OutfitIndex = -1;
        public int ApronIndex = -1;
        public int GlassesIndex = -1;
    }

    /// <summary>
    /// Appearance is ONLY a seed plus part/palette indices — the small tuple
    /// that rides on an agent. No rendered variant PNG is ever stored, and
    /// there is no customer codex; the same seed always rebuilds the same
    /// combo, so "seen" records need not persist past despawn.
    /// </summary>
    public struct AppearanceCombo
    {
        public int Seed;
        public int HairIndex;
        public int OutfitIndex;
        public int ApronIndex;
        public int GlassesIndex;
        public int SkinPalette;
        public int HairPalette;
        public int OutfitPalette;

        public string Key()
        {
            return "seed" + Seed + "|h" + HairIndex + "|o" + OutfitIndex
                + "|a" + ApronIndex + "|g" + GlassesIndex
                + "|ps" + SkinPalette + "|ph" + HairPalette
                + "|po" + OutfitPalette;
        }
    }
}

namespace CozyCafe.Core.Character
{
    /// <summary>
    /// Runtime-loaded copy of data/art_contract.json (character section) and
    /// data/character_presets.json. Every count the module uses comes from
    /// these files — nothing about part or palette sizes is a code constant.
    /// </summary>
    public sealed class CharacterArtData
    {
        public string ContractPath;
        public string PresetsPath;
        public int RigCount;
        public int PresetCountDeclared;
        public int CellWidth;
        public int CellHeight;
        public int FootAnchorX;
        public int FootAnchorY;
        public readonly List<string> Directions = new List<string>();
        public readonly Dictionary<string, int> States =
            new Dictionary<string, int>();
        public int PartHair;
        public int PartOutfit;
        public int PartApron;
        public int PartGlasses;
        public int PaletteSkin;
        public int PaletteHair;
        public int PaletteOutfit;
        public string FrameSync;
        public bool StatsIndependent;
        public string RigId;
        public readonly List<CharacterPreset> Presets =
            new List<CharacterPreset>();

        public int PartCount(PartKind kind)
        {
            switch (kind)
            {
                case PartKind.Hair: return PartHair;
                case PartKind.Outfit: return PartOutfit;
                case PartKind.Apron: return PartApron;
                case PartKind.Glasses: return PartGlasses;
                default: return 1; // body: exactly one shared body
            }
        }

        public int PaletteCount(PartKind kind)
        {
            switch (kind)
            {
                case PartKind.Body: return PaletteSkin;
                case PartKind.Hair: return PaletteHair;
                case PartKind.Outfit: return PaletteOutfit;
                default: return 0; // apron and glasses are fixed colors
            }
        }

        /// <summary>Canonical part ids ("hair_01" ..) generated from the
        /// data-sourced counts — preset ids resolve against these tables.
        /// </summary>
        public string PartId(PartKind kind, int index)
        {
            string stem;
            switch (kind)
            {
                case PartKind.Hair: stem = "hair"; break;
                case PartKind.Outfit: stem = "outfit"; break;
                case PartKind.Apron: stem = "apron"; break;
                case PartKind.Glasses: stem = "glasses"; break;
                default: stem = "body"; break;
            }
            return stem + "_" + (index + 1).ToString("D2", CultureInfo.InvariantCulture);
        }

        /// Resolves a part id like "hair_03" to its 0-based index; null stays
        /// none (-1). Throws if the id is outside the data-sourced table.
        public int ResolvePartIndex(PartKind kind, object idOrNull)
        {
            if (idOrNull == null) return -1;
            string id = Convert.ToString(idOrNull, CultureInfo.InvariantCulture);
            int n = PartCount(kind);
            for (int i = 0; i < n; i++)
            {
                if (PartId(kind, i) == id) return i;
            }
            throw new FormatException("part id not in data table: " + id);
        }

        private static CharacterArtData sharedData;

        /// Cached load for hot paths (staff rolls). The gauntlet module path
        /// uses a fresh Load() so file reads are always exercised there.
        public static CharacterArtData LoadShared()
        {
            if (sharedData == null) sharedData = Load();
            return sharedData;
        }

        public static CharacterArtData Load()
        {
            string contract = ResolveDataPath("art_contract.json", "COZYCAFE_ART_CONTRACT");
            string presets = ResolveDataPath("character_presets.json", "COZYCAFE_CHARACTER_PRESETS");
            if (contract == null || presets == null)
            {
                throw new FileNotFoundException(
                    "data/art_contract.json or data/character_presets.json not found "
                    + "from cwd, app base, or env overrides");
            }
            var d = new CharacterArtData();
            d.ContractPath = contract;
            d.PresetsPath = presets;
            d.ParseContract(File.ReadAllText(contract));
            d.ParsePresets(File.ReadAllText(presets));
            return d;
        }

        /// Ancestor walk of cwd and app-base dirs for data/<name>, plus env
        /// overrides — same resolution policy MvpData uses for mvp.json.
        private static string ResolveDataPath(string name, string envVar)
        {
            string env = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrEmpty(env) && File.Exists(env))
            {
                return Path.GetFullPath(env);
            }
            string dir = Environment.GetEnvironmentVariable("COZYCAFE_DATA_DIR");
            if (!string.IsNullOrEmpty(dir))
            {
                string p = Path.Combine(dir, name);
                if (File.Exists(p)) return Path.GetFullPath(p);
            }
            var roots = new List<string>();
            try { roots.Add(Directory.GetCurrentDirectory()); } catch (Exception) { }
            try { roots.Add(AppContext.BaseDirectory); } catch (Exception) { }
            try { roots.Add(AppDomain.CurrentDomain.BaseDirectory); } catch (Exception) { }
            var seen = new HashSet<string>();
            foreach (var r in roots)
            {
                if (string.IsNullOrEmpty(r)) continue;
                DirectoryInfo di;
                try { di = new DirectoryInfo(r); }
                catch (Exception) { continue; }
                for (; di != null; di = di.Parent)
                {
                    if (!seen.Add(di.FullName)) continue;
                    string p = Path.Combine(di.FullName, "data", name);
                    if (File.Exists(p)) return p;
                }
            }
            return null;
        }

        private void ParseContract(string json)
        {
            var root = AsDict(MiniJson.Parse(json));
            var ch = AsDict(Get(root, "character"));
            RigCount = (int)AsLong(Get(ch, "rigs"));
            PresetCountDeclared = (int)AsLong(Get(ch, "preset_count"));
            var cell = AsList(Get(ch, "cell"));
            CellWidth = (int)AsLong(cell[0]);
            CellHeight = (int)AsLong(cell[1]);
            var anchor = AsList(Get(ch, "foot_anchor"));
            FootAnchorX = (int)AsLong(anchor[0]);
            FootAnchorY = (int)AsLong(anchor[1]);
            foreach (var o in AsList(Get(ch, "directions")))
            {
                Directions.Add(AsString(o));
            }
            foreach (var kv in AsDict(Get(ch, "states")))
            {
                States[kv.Key] = (int)AsLong(kv.Value);
            }
            var parts = AsDict(Get(ch, "parts"));
            PartHair = (int)AsLong(Get(parts, "hair"));
            PartOutfit = (int)AsLong(Get(parts, "outfit"));
            PartApron = (int)AsLong(Get(parts, "apron"));
            PartGlasses = (int)AsLong(Get(parts, "glasses"));
            var pals = AsDict(Get(ch, "palettes"));
            PaletteSkin = (int)AsLong(Get(pals, "skin"));
            PaletteHair = (int)AsLong(Get(pals, "hair"));
            PaletteOutfit = (int)AsLong(Get(pals, "outfit"));
            FrameSync = AsString(Get(ch, "frame_sync"));
            StatsIndependent = AsLong(Get(ch, "stats_independent")) != 0;
        }

        private void ParsePresets(string json)
        {
            var root = AsDict(MiniJson.Parse(json));
            RigId = AsString(Get(root, "rig"));
            foreach (var po in AsList(Get(root, "presets")))
            {
                var pd = AsDict(po);
                var p = new CharacterPreset();
                p.Id = AsString(Get(pd, "id"));
                p.HairIndex = ResolvePartIndex(PartKind.Hair, Get(pd, "hair"));
                p.OutfitIndex = ResolvePartIndex(PartKind.Outfit, Get(pd, "outfit"));
                p.ApronIndex = ResolvePartIndex(PartKind.Apron, GetOpt(pd, "apron"));
                p.GlassesIndex = ResolvePartIndex(PartKind.Glasses, GetOpt(pd, "glasses"));
                Presets.Add(p);
            }
        }

        private static object GetOpt(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v)) return null;
            return v;
        }

        private static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v))
            {
                throw new FormatException("character data missing key: " + key);
            }
            return v;
        }

        private static Dictionary<string, object> AsDict(object o)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) throw new FormatException("character data expected object");
            return d;
        }

        private static List<object> AsList(object o)
        {
            var l = o as List<object>;
            if (l == null) throw new FormatException("character data expected array");
            return l;
        }

        private static long AsLong(object o)
        {
            if (o is long) return (long)o;
            if (o is int) return (int)o;
            if (o is bool) return (bool)o ? 1 : 0;
            if (o is double)
            {
                var dv = (double)o;
                if (dv == Math.Floor(dv) && Math.Abs(dv) < 9e18) return (long)dv;
            }
            if (o is string)
            {
                long v;
                if (long.TryParse((string)o, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out v))
                {
                    return v;
                }
            }
            throw new FormatException("character data expected integer, got " + o);
        }

        private static string AsString(object o)
        {
            return Convert.ToString(o, CultureInfo.InvariantCulture);
        }
    }
}

namespace CozyCafe.Core.Character
{
    /// One painted layer on the shared rig cell. Every layer carries the
    /// SAME anchor (the rig's foot anchor) and the SAME phase value taken
    /// from the shared frame clock, so compositing is a straight 0,0
    /// overlay and the assembled figure cannot tear between parts.
    public sealed class LayerSprite
    {
        public PartKind Kind;
        public int PartIndex;
        public SoftwareCanvas Pixels;
        public int AnchorX;
        public int AnchorY;
        public int PhaseDy;
    }

    /// <summary>
    /// Procedural layer painter, palette ramps and the compositor. Layers are
    /// painted in canonical colors (palette index 0 of each family); palette
    /// swaps are exact-match RGB replacements that never write the alpha byte
    /// or move a single pixel — geometry and alpha stay byte-invariant.
    /// </summary>
    public static class CharacterArt
    {
        // Fixed (non-palette) colors: features/apron/glasses never recolor —
        // only the three palette families are exact-match swapped.
        private static readonly Rgba Feature = Rgba.Opaque(56, 48, 56);
        private static readonly Rgba Mouth = Rgba.Opaque(188, 122, 106);
        private static readonly Rgba ApronBase = Rgba.Opaque(240, 240, 234);
        private static readonly Rgba ApronShade = Rgba.Opaque(202, 202, 196);
        private static readonly Rgba GlassesColor = Rgba.Opaque(48, 44, 56);

        public static Rgba FixedGlassesColor
        {
            get { return GlassesColor; }
        }

        public static bool IsFront(Facing d)
        {
            return d == Facing.SW || d == Facing.SE;
        }

        /// <summary>
        /// The shared frame clock: phase in px for (state, frame). Every layer
        /// reads the same value — this is the only phase source in the rig.
        /// </summary>
        public static int PhaseDy(CharacterArtData data, string state, int frame)
        {
            int frames;
            if (data == null || !data.States.TryGetValue(state, out frames) || frames < 1)
            {
                return 0;
            }
            int f = frame % frames;
            if (f < 0) f += frames;
            switch (state)
            {
                case "walk": return new[] { 0, -1, 0, -1 }[f % 4];
                case "sit": return 1;
                case "work": return (f % 2 == 0) ? 0 : -1;
                default: return 0; // idle
            }
        }

        /// <summary>
        /// Palette ramps. Counts come from the data file; the tables carry the
        /// authored choices and extra indices (if data ever grows) derive
        /// deterministically — the count is never baked into code.
        /// </summary>
        public static Rgba[] PaletteRamp(PartKind kind, int index, CharacterArtData data)
        {
            int count = data.PaletteCount(kind);
            if (count < 1 || index < 0) index = 0;
            index %= Math.Max(1, count);
            int[][] table;
            int shadeScale; // shade = base * shadeScale / 100
            switch (kind)
            {
                case PartKind.Body: // skin tones, light -> deep
                    table = new[]
                    {
                        new[] { 255, 224, 196 }, new[] { 240, 205, 168 },
                        new[] { 224, 184, 144 }, new[] { 198, 152, 112 },
                        new[] { 168, 120, 84 }, new[] { 128, 88, 60 }
                    };
                    shadeScale = 74;
                    break;
                case PartKind.Hair:
                    table = new[]
                    {
                        new[] { 46, 38, 42 }, new[] { 112, 74, 50 },
                        new[] { 228, 196, 110 }, new[] { 190, 84, 52 },
                        new[] { 150, 150, 158 }, new[] { 232, 232, 236 },
                        new[] { 84, 110, 200 }, new[] { 232, 140, 170 }
                    };
                    shadeScale = 68;
                    break;
                default: // outfit
                    table = new[]
                    {
                        new[] { 70, 90, 170 }, new[] { 70, 140, 140 },
                        new[] { 160, 60, 80 }, new[] { 80, 140, 80 },
                        new[] { 210, 170, 60 }, new[] { 140, 80, 160 },
                        new[] { 230, 120, 90 }, new[] { 120, 130, 70 }
                    };
                    shadeScale = 70;
                    break;
            }
            int[] rgb;
            if (index < table.Length)
            {
                rgb = table[index];
            }
            else
            {
                // Data count grew past the authored table: extend
                // deterministically instead of hardcoding a count.
                var rng = new Random(0x51F1 + index * 131);
                rgb = new[] { 60 + rng.Next(160), 60 + rng.Next(160), 60 + rng.Next(160) };
            }
            return new[]
            {
                Rgba.Opaque(rgb[0], rgb[1], rgb[2]),
                Rgba.Opaque(rgb[0] * shadeScale / 100, rgb[1] * shadeScale / 100,
                    rgb[2] * shadeScale / 100)
            };
        }

        /// <summary>
        /// Exact-match palette recolor: pixels whose RGB exactly equals a
        /// source ramp entry are rewritten to the target ramp's entry at the
        /// same stop. The alpha byte is never touched and no pixel moves —
        /// callers can verify geometry/alpha byte-invariance on the buffer.
        /// </summary>
        public static void ApplyPalette(SoftwareCanvas cv, Rgba[] from, Rgba[] to)
        {
            if (cv == null || from == null || to == null) return;
            int n = Math.Min(from.Length, to.Length);
            if (n == 0) return;
            var map = new Dictionary<int, Rgba>(n);
            for (int i = 0; i < n; i++)
            {
                map[(from[i].R << 16) | (from[i].G << 8) | from[i].B] = to[i];
            }
            byte[] px = cv.Pixels;
            for (int i = 0; i + 3 < px.Length; i += 4)
            {
                if (px[i + 3] == 0) continue;
                Rgba t;
                if (map.TryGetValue((px[i] << 16) | (px[i + 1] << 8) | px[i + 2], out t))
                {
                    px[i] = t.R;
                    px[i + 1] = t.G;
                    px[i + 2] = t.B;
                    // px[i+3] untouched: alpha stays byte-identical.
                }
            }
        }

        /// Applies the combo's palettes to a painted canonical layer.
        public static void RecolorLayer(LayerSprite layer, AppearanceCombo combo,
            CharacterArtData data)
        {
            PartKind family;
            int index;
            switch (layer.Kind)
            {
                case PartKind.Body: family = PartKind.Body; index = combo.SkinPalette; break;
                case PartKind.Hair: family = PartKind.Hair; index = combo.HairPalette; break;
                case PartKind.Outfit: family = PartKind.Outfit; index = combo.OutfitPalette; break;
                default: return; // apron + glasses: fixed colors, never recolored
            }
            ApplyPalette(layer.Pixels,
                PaletteRamp(family, 0, data), PaletteRamp(family, index, data));
        }

        /// <summary>
        /// Paints one part on the shared rig cell in canonical colors. Every
        /// layer is created with the identical foot anchor and the identical
        /// phase read from the shared clock — phase sync is structural.
        /// </summary>
        public static LayerSprite PaintLayer(CharacterArtData data, PartKind kind,
            int partIndex, Facing dir, string state, int frame)
        {
            int phase = PhaseDy(data, state, frame);
            var layer = new LayerSprite();
            layer.Kind = kind;
            layer.PartIndex = partIndex;
            layer.AnchorX = data.FootAnchorX;
            layer.AnchorY = data.FootAnchorY;
            layer.PhaseDy = phase;
            layer.Pixels = new SoftwareCanvas(data.CellWidth, data.CellHeight);
            bool front = IsFront(dir);
            switch (kind)
            {
                case PartKind.Body: PaintBody(layer.Pixels, dir, phase, data); break;
                case PartKind.Hair: PaintHair(layer.Pixels, partIndex, front, phase, data); break;
                case PartKind.Outfit: PaintOutfit(layer.Pixels, partIndex, phase, data); break;
                case PartKind.Apron: PaintApron(layer.Pixels, partIndex, phase); break;
                case PartKind.Glasses: PaintGlasses(layer.Pixels, partIndex, phase); break;
            }
            return layer;
        }

        /// <summary>
        /// Composites a full figure: layers in contract order, each recolored
        /// to the combo's palettes then overlaid at the same origin. Returns
        /// the finished cell sprite (runtime compositing — nothing is cached
        /// or written per variant).
        /// </summary>
        public static SoftwareCanvas Composite(CharacterArtData data,
            AppearanceCombo combo, Facing dir, string state, int frame,
            out int glassesVisiblePx)
        {
            var canvas = new SoftwareCanvas(data.CellWidth, data.CellHeight);
            var order = new[] { PartKind.Body, PartKind.Outfit, PartKind.Apron,
                PartKind.Glasses, PartKind.Hair };
            foreach (var kind in order)
            {
                int idx;
                switch (kind)
                {
                    case PartKind.Hair: idx = combo.HairIndex; break;
                    case PartKind.Outfit: idx = combo.OutfitIndex; break;
                    case PartKind.Apron: idx = combo.ApronIndex; break;
                    case PartKind.Glasses: idx = combo.GlassesIndex; break;
                    default: idx = 0; break;
                }
                if (idx < 0 && kind != PartKind.Body) continue; // absent part
                var layer = PaintLayer(data, kind, Math.Max(0, idx), dir, state, frame);
                RecolorLayer(layer, combo, data);
                Blit(canvas, layer.Pixels, 0, 0);
            }
            glassesVisiblePx = CountColor(canvas, GlassesColor);
            return canvas;
        }

        public static int CountColor(SoftwareCanvas cv, Rgba c)
        {
            int n = 0;
            byte[] px = cv.Pixels;
            for (int i = 0; i + 3 < px.Length; i += 4)
            {
                if (px[i + 3] == 255 && px[i] == c.R && px[i + 1] == c.G && px[i + 2] == c.B) n++;
            }
            return n;
        }

        /// Opaque bbox of a raster: used to measure per-layer motion between
        /// frames (phase sync is proven on painted pixels, not metadata).
        public static bool OpaqueBounds(SoftwareCanvas cv,
            out int minX, out int minY, out int maxX, out int maxY)
        {
            minX = cv.Width; minY = cv.Height; maxX = -1; maxY = -1;
            byte[] px = cv.Pixels;
            for (int y = 0; y < cv.Height; y++)
            {
                for (int x = 0; x < cv.Width; x++)
                {
                    if (px[(y * cv.Width + x) * 4 + 3] == 0) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            return maxX >= minX && maxY >= minY;
        }

        public static void Blit(SoftwareCanvas dst, SoftwareCanvas src, int dx, int dy)
        {
            byte[] sp = src.Pixels;
            for (int y = 0; y < src.Height; y++)
            {
                for (int x = 0; x < src.Width; x++)
                {
                    int i = (y * src.Width + x) * 4;
                    if (sp[i + 3] == 0) continue;
                    dst.SetPixel(dx + x, dy + y,
                        new Rgba(sp[i], sp[i + 1], sp[i + 2], sp[i + 3]));
                }
            }
        }

        // ---- layer painters (canonical palette colors, 32x40 cell) ----

        private static Rgba[] Canon(PartKind kind, CharacterArtData data)
        {
            return PaletteRamp(kind, 0, data);
        }

        private static void PaintBody(SoftwareCanvas cv, Facing dir, int dy,
            CharacterArtData data)
        {
            var skin = Canon(PartKind.Body, data);
            Rgba s0 = skin[0], s1 = skin[1];
            cv.FillEllipse(16, 15 + dy, 4, 4.5, s0);         // head
            cv.FillRect(14, 19 + dy, 3, 2, s1);             // neck shadow
            cv.FillRect(12, 22 + dy, 8, 8, s0);             // torso
            cv.FillRect(10, 22 + dy, 2, 6, s0);             // arm L
            cv.FillRect(20, 22 + dy, 2, 6, s0);             // arm R
            cv.FillRect(13, 30 + dy, 2, 5, s1);             // leg L
            cv.FillRect(16, 30 + dy, 2, 5, s1);             // leg R
            cv.FillRect(12, 35 + dy, 3, 1, s0);             // foot L
            cv.FillRect(16, 35 + dy, 3, 1, s0);             // foot R
            if (IsFront(dir))
            {
                cv.FillRect(14, 18 + dy, 1, 1, Feature);    // eye L
                cv.FillRect(17, 18 + dy, 1, 1, Feature);    // eye R
                cv.FillRect(15, 20 + dy, 2, 1, Mouth);      // mouth
            }
        }

        private static void PaintHair(SoftwareCanvas cv, int index, bool front,
            int dy, CharacterArtData data)
        {
            var hair = Canon(PartKind.Hair, data);
            Rgba h0 = hair[0], h1 = hair[1];
            cv.FillEllipse(16, 13 + dy, 4.5, 3.5, h0);       // cap over crown
            if (!front)
            {
                // Rear view: the back of the head is one mass of hair that
                // fully covers the face zone — glasses painted beneath it
                // stay occluded (direction-aware layering).
                cv.FillRect(11, 13 + dy, 9, 9, h0);
                cv.FillRect(11, 20 + dy, 9, 2, h1);
                if (index == 2)
                {
                    cv.FillRect(11, 20 + dy, 2, 4, h0);
                    cv.FillRect(19, 20 + dy, 2, 4, h0);
                }
                if (index == 3) cv.FillEllipse(16, 10 + dy, 2, 2, h1);
                return;
            }
            switch (index % 4)
            {
                case 0: // short cap + side tufts
                    cv.FillRect(12, 15 + dy, 2, 4, h0);
                    cv.FillRect(18, 15 + dy, 2, 4, h0);
                    cv.FillRect(12, 16 + dy, 8, 1, h1);
                    break;
                case 1: // fringe across the forehead
                    cv.FillRect(12, 15 + dy, 2, 5, h0);
                    cv.FillRect(18, 15 + dy, 2, 5, h0);
                    cv.FillRect(13, 16 + dy, 6, 2, h0);
                    cv.FillRect(15, 16 + dy, 2, 2, h1);
                    break;
                case 2: // long sides
                    cv.FillRect(11, 15 + dy, 2, 8, h0);
                    cv.FillRect(18, 15 + dy, 2, 8, h0);
                    cv.FillRect(11, 22 + dy, 2, 1, h1);
                    cv.FillRect(18, 22 + dy, 2, 1, h1);
                    break;
                default: // bun
                    cv.FillRect(12, 15 + dy, 2, 4, h0);
                    cv.FillRect(18, 15 + dy, 2, 4, h0);
                    cv.FillEllipse(16, 10 + dy, 2, 2, h1);
                    break;
            }
        }

        private static void PaintOutfit(SoftwareCanvas cv, int index, int dy,
            CharacterArtData data)
        {
            var outfit = Canon(PartKind.Outfit, data);
            Rgba o0 = outfit[0], o1 = outfit[1];
            switch (index % 3)
            {
                case 0: // tee: torso + short sleeves + pants
                    cv.FillRect(12, 22 + dy, 8, 8, o0);
                    cv.FillRect(10, 22 + dy, 2, 3, o0);
                    cv.FillRect(20, 22 + dy, 2, 3, o0);
                    cv.FillRect(12, 28 + dy, 8, 2, o1);   // hem shadow
                    cv.FillRect(13, 30 + dy, 6, 5, o1); // pants
                    cv.FillRect(12, 35 + dy, 7, 1, o1);  // shoes
                    break;
                case 1: // long sleeves + pants
                    cv.FillRect(12, 22 + dy, 8, 8, o0);
                    cv.FillRect(10, 22 + dy, 2, 6, o0);
                    cv.FillRect(20, 22 + dy, 2, 6, o0);
                    cv.FillRect(10, 28 + dy, 2, 1, o1);   // cuffs
                    cv.FillRect(20, 28 + dy, 2, 1, o1);
                    cv.FillRect(13, 30 + dy, 6, 5, o1);
                    cv.FillRect(12, 35 + dy, 7, 1, o1);
                    break;
                default: // dress/vest: flared skirt
                    cv.FillRect(12, 22 + dy, 8, 8, o0);
                    cv.FillRect(11, 30 + dy, 10, 3, o0);  // skirt flare
                    cv.FillRect(11, 32 + dy, 10, 1, o1);  // hem
                    cv.FillRect(14, 22 + dy, 3, 8, o1);   // vest line
                    cv.FillRect(12, 35 + dy, 7, 1, o1);
                    break;
            }
        }

        private static void PaintApron(SoftwareCanvas cv, int index, int dy)
        {
            if (index < 0) return;
            cv.FillRect(15, 22 + dy, 2, 1, ApronShade);      // neck strap
            cv.FillRect(13, 23 + dy, 5, 6, ApronBase);       // bib
            cv.FillRect(15, 26 + dy, 2, 2, ApronShade);      // pocket
            cv.FillRect(12, 29 + dy, 7, 2, ApronBase);       // waist band
            cv.FillRect(12, 30 + dy, 7, 1, ApronShade);      // band shade
        }

        private static void PaintGlasses(SoftwareCanvas cv, int index, int dy)
        {
            if (index < 0) return;
            if (index % 2 == 0)
            {
                cv.FillRect(13, 18 + dy, 2, 2, GlassesColor); // lens L
                cv.FillRect(17, 18 + dy, 2, 2, GlassesColor); // lens R
                cv.FillRect(15, 19 + dy, 2, 1, GlassesColor); // bridge
            }
            else
            {
                cv.FillRect(12, 18 + dy, 3, 2, GlassesColor);
                cv.FillRect(16, 18 + dy, 3, 2, GlassesColor);
                cv.FillRect(15, 18 + dy, 1, 1, GlassesColor);
            }
        }
    }
}

namespace CozyCafe.Core.Character
{
    /// <summary>
    /// The shared character rig: one rig descriptor and the starter presets
    /// loaded from data/, deterministic seed->appearance rolls for customers
    /// and staff, runtime layer compositing with exact-match palette
    /// recolors, direction-aware occlusion, and transient spawn/despawn —
    /// there is no customer codex, no per-variant PNG regeneration, and
    /// appearance never touches stats.
    /// </summary>
    public sealed class CharacterModule : ModuleBase
    {
        public override string Name { get { return "character"; } }

        public readonly CharacterArtData Data;
        public readonly RigDescriptor Rig = new RigDescriptor();

        public sealed class SpawnedCharacter
        {
            public int Id;
            public bool IsStaff;
            public AppearanceCombo Appearance;
        }

        public readonly List<SpawnedCharacter> Active = new List<SpawnedCharacter>();
        private int nextSpawnId;
        private static CharacterModule sharedModule;

        public CharacterModule()
            : this(CharacterArtData.Load())
        {
        }

        public CharacterModule(CharacterArtData data)
        {
            Data = data;
            Rig.Id = data.RigId;
            Rig.CellWidth = data.CellWidth;
            Rig.CellHeight = data.CellHeight;
            Rig.FootAnchorX = data.FootAnchorX;
            Rig.FootAnchorY = data.FootAnchorY;
            foreach (var d in data.Directions) Rig.Directions.Add(d);
            foreach (var kv in data.States) Rig.States[kv.Key] = kv.Value;
        }

        /// Lazy shared instance for callers that only need data-driven rolls
        /// (e.g. the hire office). Gate paths construct fresh instances so
        /// the data files are read for real.
        public static CharacterModule Shared
        {
            get
            {
                if (sharedModule == null) sharedModule = new CharacterModule();
                return sharedModule;
            }
        }

        public int RigCount { get { return Data.RigCount; } }
        public int PresetCount { get { return Data.Presets.Count; } }
        public IReadOnlyList<CharacterPreset> Presets { get { return Data.Presets; } }

        /// <summary>
        /// Deterministic seed -> combo roll. The appearance stream consumes
        /// only this seed: part and palette indices are drawn inside the
        /// data-sourced ranges, and no stat input exists on this path.
        /// </summary>
        public AppearanceCombo RollAppearance(int seed, bool isStaff)
        {
            var r = new Random(seed);
            var c = new AppearanceCombo();
            c.Seed = seed;
            c.HairIndex = r.Next(Data.PartHair);
            c.OutfitIndex = r.Next(Data.PartOutfit);
            c.ApronIndex = isStaff && Data.PartApron > 0 ? 0 : -1;
            c.GlassesIndex = r.Next(Data.PartGlasses + 1) - 1; // -1 = none
            c.SkinPalette = r.Next(Data.PaletteSkin);
            c.HairPalette = r.Next(Data.PaletteHair);
            c.OutfitPalette = r.Next(Data.PaletteOutfit);
            return c;
        }

        /// A preset resolves to a concrete combo: preset parts plus
        /// deterministic palette picks bounded by the data-sourced counts.
        public AppearanceCombo ComboForPreset(int presetIndex)
        {
            var p = Data.Presets[presetIndex];
            var c = new AppearanceCombo();
            c.Seed = presetIndex;
            c.HairIndex = p.HairIndex;
            c.OutfitIndex = p.OutfitIndex;
            c.ApronIndex = p.ApronIndex;
            c.GlassesIndex = p.GlassesIndex;
            c.SkinPalette = presetIndex % Data.PaletteSkin;
            c.HairPalette = presetIndex % Data.PaletteHair;
            c.OutfitPalette = presetIndex % Data.PaletteOutfit;
            return c;
        }

        /// Spawns a transient character — only the seed combo rides on it.
        public SpawnedCharacter Spawn(int seed, bool isStaff)
        {
            var s = new SpawnedCharacter();
            s.Id = nextSpawnId++;
            s.IsStaff = isStaff;
            s.Appearance = RollAppearance(seed, isStaff);
            Active.Add(s);
            return s;
        }

        public int CustomerCount
        {
            get
            {
                int n = 0;
                foreach (var s in Active) if (!s.IsStaff) n++;
                return n;
            }
        }

        public bool Despawn(int id)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                if (Active[i].Id == id)
                {
                    Active.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public void DespawnAll()
        {
            Active.Clear();
        }

        /// <summary>
        /// Persistent state: the rig id plus the seed combos of characters
        /// that are still live. There is deliberately no codex, regulars,
        /// seen-registry or request section — once a customer despawns, its
        /// record is gone.
        /// </summary>
        public Dictionary<string, object> SaveSnapshot()
        {
            var agents = new List<object>();
            foreach (var s in Active)
            {
                var a = new Dictionary<string, object>();
                a["id"] = s.Id;
                a["is_staff"] = s.IsStaff;
                a["seed"] = s.Appearance.Seed;
                a["combo"] = s.Appearance.Key();
                agents.Add(a);
            }
            var d = new Dictionary<string, object>();
            d["rig"] = Rig.Id;
            d["agents"] = agents;
            return d;
        }

        /// Customer appearance records still present in persistent state.
        /// A collection/codex feature would leave entries here after despawn;
        /// with seed-only transient storage the count is exactly the live
        /// set.
        public int PersistedCustomerRecords()
        {
            var snap = SaveSnapshot();
            int n = 0;
            foreach (var kv in snap)
            {
                if (kv.Key == "codex" || kv.Key == "collection"
                    || kv.Key == "regulars" || kv.Key == "customers_seen")
                {
                    var l = kv.Value as List<object>;
                    n += l == null ? 1 : l.Count;
                }
            }
            var agents = snap["agents"] as List<object>;
            if (agents != null)
            {
                foreach (var ao in agents)
                {
                    var a = ao as Dictionary<string, object>;
                    object st;
                    if (a != null && a.TryGetValue("is_staff", out st)
                        && st is bool && !(bool)st)
                    {
                        n++;
                    }
                }
            }
            return n;
        }

        /// Composites a full assembled sprite for a combo/facing/state/frame.
        public SoftwareCanvas Composite(AppearanceCombo combo, Facing dir,
            string state, int frame, out int glassesVisiblePx)
        {
            return CharacterArt.Composite(Data, combo, dir, state, frame,
                out glassesVisiblePx);
        }

        /// <summary>
        /// Count of stored per-variant sprite assets, measured on the shipped
        /// asset roots (art/ and game/Assets/CozyCafe, resolved from the
        /// loaded contract's data dir). A baked per-combo variant carries a
        /// combo signature — two or more part-family tokens in its name, or a
        /// seed/variant marker — while base single-part sheets do not match.
        /// Runtime compositing keeps the count at zero.
        /// </summary>
        public int VariantAssetCount
        {
            get
            {
                int n = 0;
                foreach (var root in ShippedAssetRoots())
                {
                    string[] files;
                    try
                    {
                        if (!Directory.Exists(root)) continue;
                        files = Directory.GetFiles(root, "*.png",
                            SearchOption.AllDirectories);
                    }
                    catch (Exception) { continue; }
                    foreach (var f in files)
                    {
                        if (LooksLikeVariantAsset(f)) n++;
                    }
                }
                return n;
            }
        }

        private IEnumerable<string> ShippedAssetRoots()
        {
            string dataDir;
            try { dataDir = Path.GetDirectoryName(Data.ContractPath); }
            catch (Exception) { yield break; }
            if (string.IsNullOrEmpty(dataDir)) yield break;
            string root;
            try { root = Path.GetFullPath(Path.Combine(dataDir, "..")); }
            catch (Exception) { yield break; }
            yield return Path.Combine(root, "art");
            yield return Path.Combine(root, "game", "Assets", "CozyCafe");
        }

        private static bool LooksLikeVariantAsset(string path)
        {
            string name = Path.GetFileName(path).ToLowerInvariant();
            string full = path.Replace('\\', '/').ToLowerInvariant();
            int tokens = 0;
            foreach (var t in new[] { "hair_", "outfit_", "apron_",
                "glasses_", "body_", "skin_" })
            {
                if (name.Contains(t)) tokens++;
            }
            return tokens >= 2
                || name.Contains("seed")
                || full.Contains("variant")
                || full.Contains("combo");
        }

        protected override bool OnProbe()
        {
            if (RigCount != 1 || PresetCount != Data.PresetCountDeclared) return false;
            if (Data.PartHair < 1 || Data.PartOutfit < 1 || Data.PartGlasses < 1)
            {
                return false;
            }
            // Recolor must not move geometry or touch alpha. Recolor to a
            // different palette so the repaint is exercised for real.
            var combo = ComboForPreset(0);
            combo.SkinPalette = Data.PaletteSkin - 1;
            combo.HairPalette = Data.PaletteHair - 1;
            combo.OutfitPalette = Data.PaletteOutfit - 1;
            var layer = CharacterArt.PaintLayer(Data, PartKind.Outfit,
                Math.Max(0, combo.OutfitIndex), Facing.SW, "idle", 0);
            byte[] before = (byte[])layer.Pixels.Pixels.Clone();
            CharacterArt.RecolorLayer(layer, combo, Data);
            byte[] after = layer.Pixels.Pixels;
            bool changed = false;
            for (int i = 0; i + 3 < before.Length; i += 4)
            {
                if (before[i + 3] != after[i + 3]) return false;
                if (before[i] != after[i] || before[i + 1] != after[i + 1]
                    || before[i + 2] != after[i + 2])
                {
                    changed = true;
                }
            }
            if (Data.PaletteOutfit > 1 && !changed) return false;
            // Phase sync: every layer carries the same anchor and phase.
            foreach (PartKind k in new[] { PartKind.Body, PartKind.Hair,
                PartKind.Outfit, PartKind.Glasses })
            {
                var l = CharacterArt.PaintLayer(Data, k, 0, Facing.NE, "walk", 1);
                if (l.AnchorX != Rig.FootAnchorX || l.AnchorY != Rig.FootAnchorY) return false;
                if (l.PhaseDy != CharacterArt.PhaseDy(Data, "walk", 1)) return false;
            }
            // Occlusion: every rear composite shows zero glasses pixels and
            // every front composite keeps them visible.
            var g = RollAppearance(7, false);
            if (g.GlassesIndex < 0) g.GlassesIndex = 0;
            int vis;
            foreach (Facing dir in Enum.GetValues(typeof(Facing)))
            {
                Composite(g, dir, "idle", 0, out vis);
                if (CharacterArt.IsFront(dir) ? vis <= 0 : vis != 0) return false;
            }
            // Seeded spawn/despawn leaves no records behind.
            var s1 = Spawn(101, false);
            var s2 = Spawn(101, false);
            if (s1.Appearance.Key() != s2.Appearance.Key()) return false;
            DespawnAll();
            return PersistedCustomerRecords() == 0;
        }
    }
}
