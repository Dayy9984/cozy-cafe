using System.Collections.Generic;
using CozyCafe.Core.Render;

namespace CozyCafe.Core.Scene
{
    public enum FurnitureKind
    {
        Table,
        Chair,
        Stool,
        EspressoMachine,
        Grinder,
        Steamer,
        IceMaker,
        Blender,
        Counter,
        Door
    }

    /// <summary>
    /// v0.8 render-offset contract in source-art pixels: tables, chairs and
    /// stools draw once at (0,-8) from their floor placement anchor; every
    /// other kind uses (0,0). The offset is screen-space: applied at render
    /// time, scaled by zoom, never rotated with the furniture, never fed back
    /// into logical cells, collision, pathing or depth keys.
    /// </summary>
    public static class RenderOffsetTable
    {
        public static void For(FurnitureKind kind, out int dx, out int dy)
        {
            switch (kind)
            {
                case FurnitureKind.Table:
                case FurnitureKind.Chair:
                case FurnitureKind.Stool:
                    dx = 0;
                    dy = -8;
                    return;
                default:
                    dx = 0;
                    dy = 0;
                    return;
            }
        }
    }

    public sealed class Furniture
    {
        public FurnitureKind Kind;
        public int CellX;
        public int CellY;
        public int QuarterTurns;

        public Furniture(FurnitureKind kind, int cellX, int cellY)
        {
            Kind = kind;
            CellX = cellX;
            CellY = cellY;
            QuarterTurns = 0;
        }

        /// Depth ordering uses the logical cell only.
        public int DepthKey
        {
            get { return CellX + CellY; }
        }

        /// Screen-space render offset in pixels (pre-zoom).
        public void RenderOffset(out double dx, out double dy)
        {
            int ox, oy;
            RenderOffsetTable.For(Kind, out ox, out oy);
            dx = ox;
            dy = oy;
        }
    }
}

namespace CozyCafe.Core.Scene
{
    public sealed class RoomGrid
    {
        public readonly int Width;
        public readonly int Height;
        private readonly bool[] cells;

        public RoomGrid(int width, int height)
        {
            Width = width;
            Height = height;
            cells = new bool[width * height];
            for (int i = 0; i < cells.Length; i++) cells[i] = true;
        }

        public int CellCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < cells.Length; i++) if (cells[i]) n++;
                return n;
            }
        }

        public bool InBounds(int x, int y)
        {
            return x >= 0 && y >= 0 && x < Width && y < Height;
        }

        public bool HasCell(int x, int y)
        {
            return InBounds(x, y) && cells[y * Width + x];
        }

        public void SetCell(int x, int y, bool present)
        {
            if (InBounds(x, y)) cells[y * Width + x] = present;
        }
    }

    public sealed class Agent
    {
        public string Name;
        public int PresetId;
        public double GridX;
        public double GridY;
        public bool IsStaff;
    }

    /// <summary>
    /// A composited character sprite placed on the ground plane: the shared
    /// rig cell (64x80) with its foot anchor, a facing id from the contract's
    /// direction list, and a grid point. Only the renderer reads these —
    /// logical cells, collision, paths and depth keys are untouched.
    /// </summary>
    public sealed class CharacterPlacement
    {
        public string Direction;
        public double GridX;
        public double GridY;
        public SoftwareCanvas Sprite;
        public int AnchorX;
        public int AnchorY;

        public double Depth
        {
            get { return GridX + GridY; }
        }
    }

    /// <summary>
    /// An approved atlas frame staged for the art-pipeline contact-sheet
    /// render: the decoded cell sprite with its manifest-declared anchor,
    /// category, and baked/runtime offsets so the painter can evidence the
    /// single -8 px furniture correction. Read-only render data - no
    /// logical cells touch it.
    /// </summary>
    public sealed class ArtCellPlacement
    {
        public string Id;
        public string Category;
        public SoftwareCanvas Sprite;
        public int AnchorX;
        public int AnchorY;
        public int BakedDy;
        public int RenderDy;
    }

    /// <summary>
    /// The loaded game scene: the logical room grid plus placed furniture and
    /// agents. IsLoaded is set by GameBootstrap.LoadDefaultScene only after
    /// the scene validates — CASE reporters read it, they never set it.
    /// </summary>
    public sealed class GameScene
    {
        public RoomGrid Room;
        public readonly List<Furniture> Furniture = new List<Furniture>();
        public readonly List<Agent> Agents = new List<Agent>();
        public readonly List<CharacterPlacement> Characters =
            new List<CharacterPlacement>();
        public bool IsLoaded;

        // Render hints for stage captures. These only steer the painter —
        // logical cells, collisions, paths and depth keys never read them.
        public bool FixedViewport;
        public int ViewportW;
        public int ViewportH;
        public double AnchorX;      // device px where grid corner (0,0) lands
        public double AnchorY;
        public int HighlightCellX = -1;
        public int HighlightCellY = -1;
        public bool ShowOriginCaret;
        public bool TileCanvasView;
        /// Art-pipeline stage view: render the approved atlas cells with
        /// their contract borders, anchors and the furniture -8 px lift.
        public bool ArtContactView;
        public readonly List<ArtCellPlacement> ArtCells =
            new List<ArtCellPlacement>();
        /// Render magnification for stage captures (1 = source pixels). The
        /// character sheet uses 2 so part assembly is legible in the PNG.
        /// Paint-time only — logical geometry never reads it.
        public int Zoom = 1;

        public bool Validate()
        {
            if (Room == null || Room.Width <= 0 || Room.Height <= 0) return false;
            if (Room.CellCount != Room.Width * Room.Height) return false;
            foreach (var f in Furniture)
            {
                if (!Room.HasCell(f.CellX, f.CellY)) return false;
            }
            return true;
        }
    }
}
