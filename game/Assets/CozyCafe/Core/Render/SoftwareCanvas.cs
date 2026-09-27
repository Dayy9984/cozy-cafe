using System;
using System.Collections.Generic;

namespace CozyCafe.Core.Render
{
    public struct Rgba
    {
        public byte R, G, B, A;

        public Rgba(byte r, byte g, byte b, byte a)
        {
            R = r; G = g; B = b; A = a;
        }

        public static Rgba Opaque(int r, int g, int b)
        {
            return new Rgba((byte)r, (byte)g, (byte)b, 255);
        }
    }

    /// <summary>
    /// RGBA8 pixel buffer with scanline polygon fill. Pixel-center coverage is
    /// used so adjacent diamonds share edges without seams or double-draw.
    /// </summary>
    public sealed class SoftwareCanvas
    {
        public readonly int Width;
        public readonly int Height;
        private readonly byte[] px;

        public SoftwareCanvas(int width, int height)
        {
            Width = width;
            Height = height;
            px = new byte[width * height * 4];
        }

        public byte[] Pixels
        {
            get { return px; }
        }

        public void Clear(Rgba c)
        {
            for (int i = 0; i < px.Length; i += 4)
            {
                px[i] = c.R;
                px[i + 1] = c.G;
                px[i + 2] = c.B;
                px[i + 3] = c.A;
            }
        }

        public void SetPixel(int x, int y, Rgba c)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;
            int i = (y * Width + x) * 4;
            if (c.A == 255)
            {
                px[i] = c.R; px[i + 1] = c.G; px[i + 2] = c.B; px[i + 3] = 255;
                return;
            }
            int a = c.A, ia = 255 - a;
            px[i]     = (byte)((c.R * a + px[i]     * ia) / 255);
            px[i + 1] = (byte)((c.G * a + px[i + 1] * ia) / 255);
            px[i + 2] = (byte)((c.B * a + px[i + 2] * ia) / 255);
            px[i + 3] = (byte)Math.Min(255, a + (px[i + 3] * ia) / 255);
        }

        /// <summary>
        /// Scanline even-odd fill. For each scanline the half-open coverage
        /// rule [xl-0.5, xr-0.5) keeps shared polygon edges seam-free.
        /// </summary>
        public void FillPolygon(IList<double> xs, IList<double> ys, Rgba color)
        {
            int n = xs.Count;
            if (n < 3) return;
            double minY = ys[0], maxY = ys[0];
            for (int i = 1; i < n; i++)
            {
                if (ys[i] < minY) minY = ys[i];
                if (ys[i] > maxY) maxY = ys[i];
            }
            var ints = new List<double>(n);
            for (int y = (int)Math.Floor(minY) - 1; y <= (int)Math.Ceiling(maxY) + 1; y++)
            {
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
                    for (int x = xa; x <= xb; x++) SetPixel(x, y, color);
                }
            }
        }

        /// <summary>Filled ellipse via implicit equation; used for agents/shadows.</summary>
        public void FillEllipse(double cx, double cy, double rx, double ry, Rgba color)
        {
            if (rx <= 0 || ry <= 0) return;
            int x0 = (int)Math.Floor(cx - rx), x1 = (int)Math.Ceiling(cx + rx);
            int y0 = (int)Math.Floor(cy - ry), y1 = (int)Math.Ceiling(cy + ry);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                double nx = (x + 0.5 - cx) / rx;
                double ny = (y + 0.5 - cy) / ry;
                if (nx * nx + ny * ny <= 1.0) SetPixel(x, y, color);
            }
        }
    }
}
