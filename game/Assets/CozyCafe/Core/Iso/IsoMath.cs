namespace CozyCafe.Core.Iso
{
    /// <summary>
    /// Ground-plane projection for the v0.8 contract.
    /// Tile TOP face is exactly 32x16 px; the space reference is 32x32 px and
    /// is NOT the grid pitch. The 2 px thickness is paint on outer side faces
    /// only and never enters this projection, colliders, paths, or depth keys.
    /// StepX/StepY are the recorded implementation defaults (derived half of
    /// the top face), not user-fixed sizes. Continuous coordinates round-trip;
    /// snapping happens only at final raster composition.
    /// </summary>
    public static class IsoMath
    {
        public const int TileTopWidthPx = 32;
        public const int TileTopHeightPx = 16;
        public const int TileSpaceWidthPx = 32;
        public const int TileSpaceHeightPx = 32;
        public const int VisualThicknessPx = 2;
        public const int PhysicalThicknessPx = 0;

        public const double StepX = 16.0;
        public const double StepY = 8.0;

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
            gx = sx / (2.0 * StepX) + sy / (2.0 * StepY);
            gy = sy / (2.0 * StepY) - sx / (2.0 * StepX);
        }
    }
}
