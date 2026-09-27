namespace CozyCafe.Core.Iso
{
    /// <summary>
    /// Ground-plane projection for the v0.8 contract.
    /// Tile TOP face is exactly 64x32 px; the space reference is 64x64 px and
    /// is NOT the grid pitch. The 4 px thickness is paint on outer side faces
    /// only and never enters this projection, colliders, paths, or depth keys.
    /// StepX/StepY are the recorded implementation defaults (derived half of
    /// the top face), not user-fixed sizes. Continuous coordinates round-trip;
    /// snapping happens only at final raster composition.
    /// </summary>
    public static class IsoMath
    {
        public const int TileTopWidthPx = 64;
        public const int TileTopHeightPx = 32;
        public const int TileSpaceWidthPx = 64;
        public const int TileSpaceHeightPx = 64;
        public const int VisualThicknessPx = 4;
        public const int PhysicalThicknessPx = 0;

        public const double StepX = 32.0;
        public const double StepY = 16.0;

        /// Grid (gx,gy) -> screen (sx,sy) on the ground plane.
        /// This function takes no z/thickness argument on purpose.
        public static void Project(double gx, double gy, out double sx, out double sy)
        {
            sx = (gx - gy) * StepX;
            sy = (gx + gy) * StepY;
        }

        /// Screen (sx,sy) on the ground plane -> grid (gx,gy). Exact inverse.
        public static void Unproject(double sx, double sy, out double gx, out double gy)
        {
            gx = sx / 64.0 + sy / 32.0;
            gy = sy / 32.0 - sx / 64.0;
        }
    }
}
