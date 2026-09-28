using CozyCafe.Core.Character;
using CozyCafe.Core.Scene;

namespace CozyCafe.Core.Gauntlet
{
    /// <summary>
    /// Builds the live game state a stage renders or captures. Both capture
    /// hosts (editor camera path and the CLI rasterizer) draw this same state.
    /// </summary>
    public static class StageScenes
    {
        public static GameScene Build(string stage)
        {
            switch (stage)
            {
                case "iso-grid":
                    return IsoGridFloor();
                case "iso-grid-tile":
                    return IsoTileCanvas();
                case "character-rig":
                    return CharacterRigSheet();
                default:
                    return DefaultBoot();
            }
        }

        private static GameScene DefaultBoot()
        {
            var boot = GameBootstrap.Create();
            boot.LoadDefaultScene();
            boot.Registry.ProbeAll();
            return boot.Scene;
        }

        /// Seamless iso floor for the v0.8 grid gate: a full tile floor
        /// (two-tone top faces, seam outlines, 4 px skirts on the outer
        /// contour only) inside a fixed capture viewport, with the grid
        /// origin caret and one highlighted cell — paint-only overlays.
        private static GameScene IsoGridFloor()
        {
            var s = new GameScene();
            s.Room = new RoomGrid(4, 4);
            s.IsLoaded = s.Validate();
            s.FixedViewport = true;
            s.ViewportW = 352;
            s.ViewportH = 320;
            s.AnchorX = 176;
            s.AnchorY = 40;
            s.HighlightCellX = 1;
            s.HighlightCellY = 1;
            s.ShowOriginCaret = true;
            return s;
        }

        /// Single tile on its 64x64 authoring canvas.
        private static GameScene IsoTileCanvas()
        {
            var s = new GameScene();
            s.Room = new RoomGrid(1, 1);
            s.IsLoaded = s.Validate();
            s.TileCanvasView = true;
            return s;
        }

        /// <summary>
        /// Four assembled characters on a real floor, one per contract
        /// direction: presets and a seeded customer are composited by the
        /// actual Character module at capture time — the PNG shows the
        /// layered parts (body/hair/outfit/apron/glasses) and the rear views'
        /// glasses-under-hair occlusion for real.
        /// </summary>
        private static GameScene CharacterRigSheet()
        {
            var module = new CharacterModule();
            var s = new GameScene();
            s.Room = new RoomGrid(4, 4);
            s.IsLoaded = s.Validate();
            s.FixedViewport = true;
            s.ViewportW = 600;
            s.ViewportH = 400;
            s.AnchorX = 300;
            s.AnchorY = 96;
            s.Zoom = 2;

            // casual_02 wears glasses (front view keeps them visible);
            // staff_01 wears the apron; casual_01 shows a rear view; and a
            // seeded customer who rolled glasses takes the other rear view,
            // where the hair layer must occlude them.
            Place(s, module, module.ComboForPreset(1), Facing.SW, 0.9, 0.9);
            Place(s, module, module.ComboForPreset(2), Facing.SE, 3.1, 0.9);
            Place(s, module, module.ComboForPreset(0), Facing.NW, 0.9, 3.1);
            Place(s, module, module.RollAppearance(
                GlassesSeed(module), false), Facing.NE, 3.1, 3.1);
            return s;
        }

        /// First seed whose customer roll includes glasses — deterministic.
        private static int GlassesSeed(CharacterModule module)
        {
            for (int seed = 1; seed < 1000; seed++)
            {
                if (module.RollAppearance(seed, false).GlassesIndex >= 0)
                {
                    return seed;
                }
            }
            return 1;
        }

        private static void Place(GameScene s, CharacterModule module,
            AppearanceCombo combo, Facing dir, double gx, double gy)
        {
            int vis;
            var sprite = module.Composite(combo, dir, "idle", 0, out vis);
            s.Characters.Add(new CharacterPlacement
            {
                Direction = dir.ToString(),
                GridX = gx,
                GridY = gy,
                Sprite = sprite,
                AnchorX = module.Rig.FootAnchorX,
                AnchorY = module.Rig.FootAnchorY
            });
        }
    }
}
