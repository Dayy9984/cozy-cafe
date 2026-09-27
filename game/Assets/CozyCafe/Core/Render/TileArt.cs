using System;
using CozyCafe.Core.Iso;

namespace CozyCafe.Core.Render
{
    /// <summary>
    /// Authored floor-tile art rasterized onto its 64x62 working canvas.
    /// The 64x31 top face is anchored at canvas (0,0); the 4 px side faces
    /// extend below the two lower (viewer-facing) edges inside the same
    /// canvas — visual thickness only, physical thickness stays 0.
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
            double t = IsoMath.VisualThicknessPx;
            // Side faces first (the top face overlaps their shared edge):
            // a 4 px skirt hanging below each of the two lower edges.
            src.FillPolygon(
                new double[] { 0, 32, 32, 0 },
                new double[] { 15.5, 31, 31 + t, 15.5 + t },
                SideLeft);
            src.FillPolygon(
                new double[] { 32, 64, 64, 32 },
                new double[] { 31, 15.5, 15.5 + t, 31 + t },
                SideRight);
            // Top face: wood fill with diagonal grain stripes.
            for (int y = 0; y < TileCanvasContract.HeightPx; y++)
            {
                for (int x = 0; x < TileCanvasContract.WidthPx; x++)
                {
                    double nx = Math.Abs(x + 0.5 - 32.0) / 32.0;
                    double ny = Math.Abs(y + 0.5 - 15.5) / 15.5;
                    if (nx + ny > 1.0) continue;
                    src.SetPixel(x, y, ((x + y) % 7 == 0) ? WoodGrain : WoodTop);
                }
            }
            return src;
        }
    }
}
