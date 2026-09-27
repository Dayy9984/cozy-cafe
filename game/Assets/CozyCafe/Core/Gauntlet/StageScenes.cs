using CozyCafe.Core.Layout;
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
                case "layout-editor":
                    return LayoutEditorRoom();
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

        /// Furnished room for the layout-editor capture: a wall-slot door,
        /// paired table/seat sets, a counter hosting a real espresso machine
        /// and two agents - built through the real editor so every placement
        /// passed the same validity checks the gates exercise.
        private static GameScene LayoutEditorRoom()
        {
            var s = new GameScene();
            s.Room = new RoomGrid(6, 5);
            var ed = new LayoutModule(s);
            ed.TryPlace(FurnitureKind.Door, 0, 2, 0);
            ed.BeginCommand();
            ed.TryPlace(FurnitureKind.Table, 2, 1, 0);
            ed.TryPlace(FurnitureKind.Chair, 1, 1, 0);
            ed.TryPlace(FurnitureKind.Chair, 3, 1, 0);
            ed.TryPlace(FurnitureKind.Table, 4, 3, 0);
            ed.TryPlace(FurnitureKind.Stool, 4, 2, 0);
            ed.TryPlace(FurnitureKind.Counter, 5, 0, 0);
            ed.EndCommand();
            ed.TryPlace(FurnitureKind.EspressoMachine, 5, 0, 0);
            s.Agents.Add(new Agent { Name = "staff_0", PresetId = 0, GridX = 2.5, GridY = 3.5, IsStaff = true });
            s.Agents.Add(new Agent { Name = "customer_0", PresetId = 1, GridX = 1.5, GridY = 3.5, IsStaff = false });
            s.IsLoaded = s.Validate();
            s.FixedViewport = true;
            s.ViewportW = 420;
            s.ViewportH = 300;
            s.AnchorX = 200;
            s.AnchorY = 40;
            return s;
        }
    }
}