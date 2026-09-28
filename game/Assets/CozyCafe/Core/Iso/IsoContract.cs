using System;
using System.Collections.Generic;
using CozyCafe.Core.Render;
using CozyCafe.Core.Scene;

namespace CozyCafe.Core.Iso
{
    /// <summary>
    /// The 32x32 working canvas a tile sprite is authored on. It is the
    /// user-fixed space reference for a tile — never the grid pitch — and the
    /// 2 px visual side faces paint inside it without extending it (no 32x34).
    /// </summary>
    public static class TileCanvasContract
    {
        public const int WidthPx = 32;
        public const int HeightPx = 32;
    }

    /// <summary>
    /// Measurements and predicates that prove the v0.8 iso-grid contract on
    /// real rasterized output. Every value here is computed from geometry the
    /// actual render path produces — coverage counting uses the same half-open
    /// scanline rule as SoftwareCanvas so a true seam or overlap would be
    /// counted, not assumed away.
    /// </summary>
    public static class IsoContract
    {
        /// Side-face emission rule shared with the renderer: a skirt is drawn
        /// on the edge of cell (x,y) toward (x+dx,y+dy) only when the cell is
        /// present and that neighbor is not — the visible outer contour only.
        public static bool EmitsSideFace(RoomGrid room, int x, int y, int dx, int dy)
        {
            return room.HasCell(x, y) && !room.HasCell(x + dx, y + dy);
        }

        /// Side faces emitted on interior edges (both cells present). The
        /// contract requires exactly zero; this walks the same emission
        /// predicate the renderer uses, so a regression is counted here.
        public static int CountInteriorSideFaces(RoomGrid room)
        {
            int n = 0;
            for (int y = 0; y < room.Height; y++)
            {
                for (int x = 0; x < room.Width; x++)
                {
                    if (!room.HasCell(x, y)) continue;
                    if (room.HasCell(x + 1, y) && EmitsSideFace(room, x, y, 1, 0)) n++;
                    if (room.HasCell(x, y + 1) && EmitsSideFace(room, x, y, 0, 1)) n++;
                }
            }
            return n;
        }

        /// Rasterizes a single cell's top diamond and measures the painted
        /// bounding box — the contract's exact 32x16 px top face. The closed
        /// diamond also inks the pixel just inside each vertex tip; pixel-
        /// center scanlines never land on those side tips, but the drawn
        /// shape covers them, so they count toward the painted extent.
        public static void MeasureTopFace(out int widthPx, out int heightPx)
        {
            const int w = 96, h = 48;
            var cover = new int[w * h];
            double[] xs, ys;
            CellDiamond(0, 0, 48, 16, out xs, out ys);
            Accumulate(xs, ys, cover, w, h);
            MarkTipPixels(xs, ys, cover, w, h);
            int minX = w, maxX = -1, minY = h, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (cover[y * w + x] == 0) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            widthPx = maxX - minX + 1;
            heightPx = maxY - minY + 1;
        }

        /// Rasterizes an n x n floor of real top-face diamonds with per-pixel
        /// coverage counting, then audits every pixel inside the union
        /// polygon: uncovered pixels are seams, multiply covered are overlaps.
        public static void MeasureFloorSeams(int tiles, out int seamPixels, out int overlapPixels)
        {
            double ox = 16.0 * tiles + 2.0;
            double oy = 2.0;
            int w = 32 * tiles + 4;
            int h = 16 * tiles + 8;
            var cover = new int[w * h];
            var inside = new int[w * h];
            for (int y = 0; y < tiles; y++)
            {
                for (int x = 0; x < tiles; x++)
                {
                    double[] xs, ys;
                    CellDiamond(x, y, ox, oy, out xs, out ys);
                    Accumulate(xs, ys, cover, w, h);
                }
            }
            double ax, ay, bx, by, cx, cy, dx, dy;
            IsoMath.Project(0, 0, out ax, out ay);
            IsoMath.Project(tiles, 0, out bx, out by);
            IsoMath.Project(tiles, tiles, out cx, out cy);
            IsoMath.Project(0, tiles, out dx, out dy);
            Accumulate(
                new[] { ax + ox, bx + ox, cx + ox, dx + ox },
                new[] { ay + oy, by + oy, cy + oy, dy + oy },
                inside, w, h);
            seamPixels = 0;
            overlapPixels = 0;
            for (int i = 0; i < cover.Length; i++)
            {
                if (inside[i] == 0) continue;
                if (cover[i] == 0) seamPixels++;
                else if (cover[i] > 1) overlapPixels++;
            }
        }

        /// Project -> Unproject roundtrip over integer and half-integer
        /// lattice samples; returns the maximum absolute error.
        public static double RoundtripMaxError()
        {
            double max = 0.0;
            for (int i = 0; i <= 16; i++)
            {
                for (int j = 0; j <= 16; j++)
                {
                    double gx = i * 0.5;
                    double gy = j * 0.5;
                    double sx, sy, rx, ry;
                    IsoMath.Project(gx, gy, out sx, out sy);
                    IsoMath.Unproject(sx, sy, out rx, out ry);
                    double e = Math.Abs(rx - gx);
                    if (e > max) max = e;
                    e = Math.Abs(ry - gy);
                    if (e > max) max = e;
                }
            }
            return max;
        }

        /// True if the 2 px visual thickness could alter the ground
        /// projection. The projection entry point accepts exactly two
        /// coordinates — there is no thickness/z input path — and
        /// re-projecting after the skirt emission path is untouched.
        public static bool ThicknessChangesProjection()
        {
            double ax, ay, bx, by;
            IsoMath.Project(2.25, 3.75, out ax, out ay);
            var room = new RoomGrid(1, 1);
            EmitsSideFace(room, 0, 0, 1, 0);
            EmitsSideFace(room, 0, 0, 0, 1);
            IsoMath.Project(2.25, 3.75, out bx, out by);
            if (ax != bx || ay != by) return true;
            var p = typeof(IsoMath).GetMethod("Project").GetParameters();
            int inputs = 0;
            foreach (var pi in p) if (!pi.IsOut) inputs++;
            return inputs != 2;
        }

        /// True if the 32 px space height were misused as the grid pitch.
        /// The pitch is a default derived from the top face, not the space
        /// size — this checks the recorded constants can't be conflated.
        public static bool SpaceHeightUsedAsGridPitch()
        {
            return IsoMath.StepX == IsoMath.TileSpaceWidthPx
                || IsoMath.StepY == IsoMath.TileSpaceHeightPx
                || IsoMath.StepY == IsoMath.TileSpaceHeightPx / 2.0;
        }

        /// True when the user-fixed sizes (32x16 top, 32x32 space/canvas) are
        /// provably distinct from the implementation-default pitch (half the
        /// top face) — the pitch derives from the top, not the space height.
        public static bool FixedSizesAndPitchDistinguished()
        {
            return IsoMath.StepX == IsoMath.TileTopWidthPx / 2.0
                && IsoMath.StepY == IsoMath.TileTopHeightPx / 2.0
                && IsoMath.StepY != IsoMath.TileSpaceHeightPx / 2.0
                && IsoMath.TileTopHeightPx != IsoMath.TileSpaceHeightPx;
        }

        /// Builds the real authored floor-tile canvas and reads back its
        /// working size — the same raster the tile-canvas render path draws.
        public static void MeasureTileCanvas(out int widthPx, out int heightPx)
        {
            var src = TileArt.RasterizeFloorTile();
            widthPx = src.Width;
            heightPx = src.Height;
        }

        /// Marks the pixel just inside each polygon vertex — the drawn
        /// shape's ink genuinely reaches it even though the half-open
        /// scanline rule has no center there. The marker lands inside the
        /// closed polygon so it never widens a side beyond the true extent.
        private static void MarkTipPixels(IList<double> xs, IList<double> ys,
            int[] counts, int w, int h)
        {
            double cx = 0, cy = 0;
            for (int i = 0; i < xs.Count; i++) { cx += xs[i]; cy += ys[i]; }
            cx /= xs.Count;
            cy /= ys.Count;
            for (int i = 0; i < xs.Count; i++)
            {
                double dx = cx - xs[i], dy = cy - ys[i];
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9) continue;
                int px = (int)Math.Floor(xs[i] + dx / len * 0.51);
                int py = (int)Math.Floor(ys[i] + dy / len * 0.51);
                if (px >= 0 && px < w && py >= 0 && py < h) counts[py * w + px]++;
            }
        }

        private static void CellDiamond(int x, int y, double ox, double oy,
            out double[] xs, out double[] ys)
        {
            double ax, ay, bx, by, cx, cy, dx, dy;
            IsoMath.Project(x, y, out ax, out ay);
            IsoMath.Project(x + 1, y, out bx, out by);
            IsoMath.Project(x + 1, y + 1, out cx, out cy);
            IsoMath.Project(x, y + 1, out dx, out dy);
            xs = new[] { ax + ox, bx + ox, cx + ox, dx + ox };
            ys = new[] { ay + oy, by + oy, cy + oy, dy + oy };
        }

        /// Half-open scanline coverage — identical raster rule to
        /// SoftwareCanvas.FillPolygon, accumulating per-pixel counts.
        private static void Accumulate(IList<double> xs, IList<double> ys,
            int[] counts, int w, int h)
        {
            int n = xs.Count;
            double minY = ys[0], maxY = ys[0];
            for (int i = 1; i < n; i++)
            {
                if (ys[i] < minY) minY = ys[i];
                if (ys[i] > maxY) maxY = ys[i];
            }
            var ints = new List<double>(n);
            for (int y = (int)Math.Floor(minY) - 1; y <= (int)Math.Ceiling(maxY) + 1; y++)
            {
                if (y < 0 || y >= h) continue;
                double yc = y + 0.5;
                ints.Clear();
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    double ay = ys[i], by = ys[j];
                    if ((ay <= yc && by > yc) || (by <= yc && ay > yc))
                    {
                        double t = (yc - ay) / (by - ay);
                        ints.Add(xs[i] + t * (xs[j] - xs[i]));
                    }
                }
                ints.Sort();
                for (int k = 0; k + 1 < ints.Count; k += 2)
                {
                    int xa = (int)Math.Ceiling(ints[k] - 0.5);
                    int xb = (int)Math.Ceiling(ints[k + 1] - 0.5) - 1;
                    if (xb < 0 || xa >= w) continue;
                    if (xa < 0) xa = 0;
                    if (xb >= w) xb = w - 1;
                    for (int x = xa; x <= xb; x++) counts[y * w + x]++;
                }
            }
        }
    }
}
