using System;
using CozyCafe.Core.Iso;

namespace CozyCafe.Core.Render
{
    /// <summary>
    /// Authored floor-tile art rasterized onto its 32x32 working canvas.
    /// The 32x16 top face is anchored at canvas (0,0) so the painted slab
    /// occupies canvas rows 0-15 exactly: apex flush at the top edge, bottom
    /// vertex on the midline. The 2 px thickness is painted as thin side
    /// faces straddling the two lower (outer-contour) edges inside that band
    /// — visual only, physical thickness stays 0, no 32x34 canvas.
    /// </summary>
    public static class TileArt
    {
        public static readonly Rgba WoodTop = Rgba.Opaque(176, 124, 78);
        public static readonly Rgba WoodGrain = Rgba.Opaque(140, 96, 58);
        public static readonly Rgba SideLeft = Rgba.Opaque(120, 82, 48);
        public static readonly Rgba SideRight = Rgba.Opaque(140, 96, 58);

        public static SoftwareCanvas RasterizeFloorTile()
        {
            var src = new SoftwareCanvas(TileCanvasContract.WidthPx, TileCanvasContract.HeightPx);
            // Top face: wood fill with diagonal grain stripes.
            for (int y = 0; y < TileCanvasContract.HeightPx; y++)
            {
                for (int x = 0; x < TileCanvasContract.WidthPx; x++)
                {
                    double nx = Math.Abs(x + 0.5 - 16.0) / 16.0;
                    double ny = Math.Abs(y + 0.5 - 8.0) / 8.0;
                    if (nx + ny > 1.0) continue;
                    src.SetPixel(x, y, ((x + y) % 7 == 0) ? WoodGrain : WoodTop);
                }
            }
            // Side faces over the top face: a band straddling each lower edge
            // (~0.4 px inside the diamond plus ~0.25 px below the edge) reads
            // as the slab's 2 px edge thickness while the silhouette stays
            // inside canvas rows 0-15.
            SideBand(src, 0, 8.0, 16, 16.0, SideLeft);
            SideBand(src, 16, 16.0, 32, 8.0, SideRight);
            return src;
        }

        /// <summary>
        /// Fills the side-face band straddling edge (ax,ay)->(bx,by). The edge
        /// must be wound so the normal (uy,-ux) points into the diamond —
        /// toward the centroid — for both lower edges.
        /// </summary>
        private static void SideBand(SoftwareCanvas cv,
            double ax, double ay, double bx, double by, Rgba color)
        {
            double dx = bx - ax, dy = by - ay;
            double len = Math.Sqrt(dx * dx + dy * dy);
            double nx = dy / len, ny = -dx / len;
            double wi = IsoMath.VisualThicknessPx * 0.2;
            double wo = IsoMath.VisualThicknessPx * 0.125;
            cv.FillPolygon(
                new double[]
                {
                    ax + nx * wi, bx + nx * wi, bx - nx * wo, ax - nx * wo
                },
                new double[]
                {
                    ay + ny * wi, by + ny * wi, by - ny * wo, ay - ny * wo
                },
                color);
        }
    }
}
