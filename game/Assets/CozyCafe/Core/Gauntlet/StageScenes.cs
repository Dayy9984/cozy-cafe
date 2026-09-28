using CozyCafe.Core.Art;
using CozyCafe.Core.Layout;
using CozyCafe.Core.Character;
using CozyCafe.Core.Render;
using CozyCafe.Core.Scene;
using CozyCafe.Core.Tools;

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
                case "character-rig":
                    return CharacterRigSheet();
                case "art-pipeline":
                    return ArtPipelineSheet();
                case "desktop-tools":
                    return DesktopToolsMini();
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
            s.ViewportW = 176;
            s.ViewportH = 160;
            s.AnchorX = 88;
            s.AnchorY = 20;
            s.HighlightCellX = 1;
            s.HighlightCellY = 1;
            s.ShowOriginCaret = true;
            return s;
        }

        /// Single tile on its 32x32 authoring canvas.
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
            s.ViewportW = 210;
            s.ViewportH = 150;
            s.AnchorX = 100;
            s.AnchorY = 20;
            return s;
        }

        /// Mini-mode scene: the cafe shrunk into the small window with the
        /// real work-tools docked on it. The panel is the module's own
        /// BuildPanel() snapshot — a memo holding real Korean text, three
        /// todos (one done), the focus timer paused at exactly 900s, and
        /// the local deck mid-track — so the capture is live module state.
        private static GameScene DesktopToolsMini()
        {
            var s = new GameScene();
            s.Room = new RoomGrid(4, 3);
            var ed = new LayoutModule(s);
            ed.TryPlace(FurnitureKind.Door, 0, 1, 0);
            ed.BeginCommand();
            ed.TryPlace(FurnitureKind.Table, 1, 1, 0);
            ed.TryPlace(FurnitureKind.Chair, 1, 0, 0);
            ed.TryPlace(FurnitureKind.Counter, 3, 0, 0);
            ed.EndCommand();
            ed.TryPlace(FurnitureKind.EspressoMachine, 3, 0, 0);
            s.Agents.Add(new Agent
            {
                Name = "staff_0",
                PresetId = 0,
                GridX = 2.5,
                GridY = 2.5,
                IsStaff = true
            });

            var tools = new ToolsModule();
            int memo = tools.CreateMemo("");
            tools.SetMemoText(memo, "오늘 매출 정산하기\n내일 우유 주문");
            int t1 = tools.AddTodo("재고 확인");
            tools.AddTodo("창가 청소");
            tools.AddTodo("신메뉴 연구");
            tools.CompleteTodo(t1);
            tools.Timer.StartFocus();
            tools.TickTimer(600);
            tools.Timer.Pause();
            tools.Music.Play();
            tools.Music.Next();
            tools.Music.TickPlayback(20);
            tools.SetMode(WindowMode.Mini);

            s.MiniMode = true;
            s.ToolsPanel = tools.BuildPanel();
            s.IsLoaded = s.Validate();
            s.FixedViewport = true;
            s.ViewportW = 460;
            s.ViewportH = 400;
            s.AnchorX = 230;
            s.AnchorY = 72;
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
            s.ViewportW = 300;
            s.ViewportH = 200;
            s.AnchorX = 150;
            s.AnchorY = 48;
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

        /// <summary>
        /// Approved-art contact sheet: the real atlas manifest drives the
        /// layout - each declared frame is cropped out of the approved sheet
        /// the pipeline wrote and staged with its recorded anchor and offset
        /// metadata. If the approved artifacts are absent or invalid the
        /// scene reports not-loaded (capture = BLOCKED, never substituted).
        /// </summary>
        private static GameScene ArtPipelineSheet()
        {
            var s = new GameScene();
            s.Room = new RoomGrid(1, 1);
            var m = ArtAssets.LoadManifest();
            if (!m.Valid)
            {
                s.IsLoaded = false;
                return s;
            }
            SoftwareCanvas sheet;
            if (!PngReader.TryLoad(ArtAssets.SheetPath(m), out sheet))
            {
                s.IsLoaded = false;
                return s;
            }
            foreach (var f in m.Frames)
            {
                var cell = new SoftwareCanvas(f.W, f.H);
                for (int y = 0; y < f.H; y++)
                {
                    for (int x = 0; x < f.W; x++)
                    {
                        int si = ((f.Y + y) * sheet.Width + f.X + x) * 4;
                        int di = (y * f.W + x) * 4;
                        cell.Pixels[di] = sheet.Pixels[si];
                        cell.Pixels[di + 1] = sheet.Pixels[si + 1];
                        cell.Pixels[di + 2] = sheet.Pixels[si + 2];
                        cell.Pixels[di + 3] = sheet.Pixels[si + 3];
                    }
                }
                s.ArtCells.Add(new ArtCellPlacement
                {
                    Id = f.Id,
                    Category = f.Category,
                    Sprite = cell,
                    AnchorX = f.OriginX,
                    AnchorY = f.OriginY,
                    BakedDy = f.BakedDy,
                    RenderDy = f.RenderDy,
                });
            }
            s.ArtContactView = true;
            s.FixedViewport = true;
            s.Zoom = 1;
            s.IsLoaded = s.Validate();
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