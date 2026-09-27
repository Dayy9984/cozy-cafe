using System;
using System.Collections.Generic;
using CozyCafe.Core.Iso;
using CozyCafe.Core.Scene;

namespace CozyCafe.Core.Render
{
    /// <summary>
    /// Software rasterizer of real game state used by the GameCli host's
    /// "render" command. Draws the iso floor (64x31 top faces), the 4 px
    /// visual-only side skirts on exterior edges only, furniture lifted by
    /// its screen-space render offset, and agents. Logical geometry is never
    /// modified — this only paints.
    /// </summary>
    public static class SceneRenderer
    {
        private struct Fill
        {
            public double[] Xs;
            public double[] Ys;
            public Rgba Color;
            public int Order;
            public double Depth;
        }

        private static readonly Rgba Background = Rgba.Opaque(27, 23, 31);
        private static readonly Rgba WoodA = Rgba.Opaque(202, 152, 96);
        private static readonly Rgba WoodB = Rgba.Opaque(188, 139, 86);
        private static readonly Rgba SideRight = Rgba.Opaque(122, 82, 52);
        private static readonly Rgba SideLeft = Rgba.Opaque(102, 68, 44);
        private static readonly Rgba TableTop = Rgba.Opaque(160, 102, 64);
        private static readonly Rgba TableSkirt = Rgba.Opaque(112, 72, 46);
        private static readonly Rgba ChairTop = Rgba.Opaque(176, 116, 74);
        private static readonly Rgba ChairSkirt = Rgba.Opaque(124, 80, 50);
        private static readonly Rgba MachineBody = Rgba.Opaque(84, 88, 98);
        private static readonly Rgba MachineTop = Rgba.Opaque(204, 208, 218);
        private static readonly Rgba CounterBody = Rgba.Opaque(150, 104, 66);
        private static readonly Rgba CounterTop = Rgba.Opaque(196, 148, 100);
        private static readonly Rgba StaffColor = Rgba.Opaque(92, 142, 224);
        private static readonly Rgba CustomerColor = Rgba.Opaque(226, 146, 92);
        private static readonly Rgba Shadow = new Rgba(0, 0, 0, 70);

        public static byte[] RenderPng(GameScene scene, int zoom)
        {
            if (scene == null || scene.Room == null)
                throw new ArgumentNullException("scene");
            if (zoom < 1) zoom = 1;

            var fills = new List<Fill>();
            var room = scene.Room;

            for (int y = 0; y < room.Height; y++)
            {
                for (int x = 0; x < room.Width; x++)
                {
                    if (!room.HasCell(x, y)) continue;
                    double[] xs, ys;
                    Diamond(x, y, zoom, out xs, out ys);
                    bool odd = ((x + y) & 1) == 1;
                    fills.Add(Make(xs, ys, odd ? WoodB : WoodA, 0, 0));

                    // Side faces paint the outer boundary only; interior
                    // edges between present cells get none (contract).
                    if (!room.HasCell(x + 1, y))
                        fills.Add(Make(SkirtX(xs[1], xs[2]),
                            SkirtY(ys[1], ys[2], zoom), SideRight, 1, 0));
                    if (!room.HasCell(x, y + 1))
                        fills.Add(Make(SkirtX(xs[3], xs[2]),
                            SkirtY(ys[3], ys[2], zoom), SideLeft, 1, 0));
                }
            }

            var furn = new List<Furniture>(scene.Furniture);
            furn.Sort(delegate (Furniture a, Furniture b) { return a.DepthKey.CompareTo(b.DepthKey); });
            foreach (var f in furn) AddFurniture(fills, f, zoom);

            foreach (var a in scene.Agents) AddAgent(fills, a, zoom);

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var f in fills)
            {
                for (int i = 0; i < f.Xs.Length; i++)
                {
                    if (f.Xs[i] < minX) minX = f.Xs[i];
                    if (f.Xs[i] > maxX) maxX = f.Xs[i];
                    if (f.Ys[i] < minY) minY = f.Ys[i];
                    if (f.Ys[i] > maxY) maxY = f.Ys[i];
                }
            }
            int pad = 10;
            int w = (int)Math.Ceiling(maxX - minX) + pad * 2;
            int h = (int)Math.Ceiling(maxY - minY) + pad * 2;
            var canvas = new SoftwareCanvas(w, h);
            canvas.Clear(Background);
            fills.Sort(delegate (Fill a, Fill b)
            {
                int c = a.Order.CompareTo(b.Order);
                return c != 0 ? c : a.Depth.CompareTo(b.Depth);
            });
            foreach (var f in fills)
            {
                var txs = new double[f.Xs.Length];
                var tys = new double[f.Ys.Length];
                for (int i = 0; i < f.Xs.Length; i++)
                {
                    txs[i] = f.Xs[i] - minX + pad;
                    tys[i] = f.Ys[i] - minY + pad;
                }
                canvas.FillPolygon(txs, tys, f.Color);
            }
            return PngWriter.Encode(w, h, canvas.Pixels);
        }

        private static Fill Make(double[] xs, double[] ys, Rgba c, int order, double depth)
        {
            var f = new Fill();
            f.Xs = xs; f.Ys = ys; f.Color = c; f.Order = order; f.Depth = depth;
            return f;
        }

        /// Top-face diamond of room cell (x,y): corners top,right,bottom,left.
        private static void Diamond(int x, int y, int zoom, out double[] xs, out double[] ys)
        {
            double ax, ay, bx, by, cx, cy, dx, dy;
            IsoMath.Project(x, y, out ax, out ay);
            IsoMath.Project(x + 1, y, out bx, out by);
            IsoMath.Project(x + 1, y + 1, out cx, out cy);
            IsoMath.Project(x, y + 1, out dx, out dy);
            xs = new[] { ax * zoom, bx * zoom, cx * zoom, dx * zoom };
            ys = new[] { ay * zoom, by * zoom, cy * zoom, dy * zoom };
        }

        private static void DiamondAt(double cx, double cy, double hw, double hh,
            out double[] xs, out double[] ys)
        {
            xs = new[] { cx, cx + hw, cx, cx - hw };
            ys = new[] { cy - hh, cy, cy + hh, cy };
        }

        private static void Rect(double x0, double y0, double x1, double y1,
            out double[] xs, out double[] ys)
        {
            xs = new[] { x0, x1, x1, x0 };
            ys = new[] { y0, y0, y1, y1 };
        }

        private static void Ellipse(double cx, double cy, double rx, double ry,
            out double[] xs, out double[] ys)
        {
            const int n = 20;
            xs = new double[n];
            ys = new double[n];
            for (int i = 0; i < n; i++)
            {
                double a = i * 2.0 * Math.PI / n;
                xs[i] = cx + Math.Cos(a) * rx;
                ys[i] = cy + Math.Sin(a) * ry;
            }
        }

        /// 4 px visual skirt hanging below the top-face edge (topA -> topB).
        private static double[] SkirtX(double xa, double xb)
        {
            return new[] { xa, xb, xb, xa };
        }

        private static double[] SkirtY(double ya, double yb, int zoom)
        {
            double t = IsoMath.VisualThicknessPx * zoom;
            return new[] { ya, yb, yb + t, ya + t };
        }

        private static void AddFurniture(List<Fill> fills, Furniture f, int zoom)
        {
            double gx, gy, ox, oy;
            IsoMath.Project(f.CellX + 0.5, f.CellY + 0.5, out gx, out gy);
            gx *= zoom;
            gy *= zoom;
            f.RenderOffset(out ox, out oy);
            double dx = gx + ox * zoom;
            double dy = gy + oy * zoom;
            double depth = f.DepthKey;

            double[] xs, ys;
            Ellipse(gx, gy, 13 * zoom, 4.5 * zoom, out xs, out ys);
            fills.Add(Make(xs, ys, Shadow, 2, depth - 0.5));

            switch (f.Kind)
            {
                case FurnitureKind.Table:
                    Ellipse(dx, dy + 8 * zoom, 3 * zoom, 6 * zoom, out xs, out ys);
                    fills.Add(Make(xs, ys, TableSkirt, 3, depth));
                    DiamondAt(dx, dy, 20 * zoom, 9 * zoom, out xs, out ys);
                    fills.Add(Make(SkirtX(xs[3], xs[2]), SkirtY(ys[3], ys[2], zoom),
                        TableSkirt, 3, depth + 0.1));
                    fills.Add(Make(xs, ys, TableTop, 3, depth));
                    break;
                case FurnitureKind.Chair:
                case FurnitureKind.Stool:
                    DiamondAt(dx, dy, 11 * zoom, 5 * zoom, out xs, out ys);
                    fills.Add(Make(SkirtX(xs[3], xs[2]), SkirtY(ys[3], ys[2], zoom),
                        ChairSkirt, 3, depth + 0.1));
                    fills.Add(Make(xs, ys, ChairTop, 3, depth));
                    Rect(dx - 9 * zoom, dy - 14 * zoom, dx - 5 * zoom, dy, out xs, out ys);
                    fills.Add(Make(xs, ys, ChairSkirt, 3, depth - 0.1));
                    break;
                case FurnitureKind.Counter:
                    Rect(dx - 15 * zoom, dy - 10 * zoom, dx + 15 * zoom, dy + 2 * zoom,
                        out xs, out ys);
                    fills.Add(Make(xs, ys, CounterBody, 3, depth));
                    DiamondAt(dx, dy - 10 * zoom, 15 * zoom, 6 * zoom, out xs, out ys);
                    fills.Add(Make(xs, ys, CounterTop, 3, depth + 0.1));
                    break;
                default: // machines: grounded box, no lift
                    Rect(dx - 13 * zoom, dy - 12 * zoom, dx + 13 * zoom, dy + 2 * zoom,
                        out xs, out ys);
                    fills.Add(Make(xs, ys, MachineBody, 3, depth));
                    DiamondAt(dx, dy - 12 * zoom, 13 * zoom, 5 * zoom, out xs, out ys);
                    fills.Add(Make(xs, ys, MachineTop, 3, depth + 0.1));
                    break;
            }
        }

        private static void AddAgent(List<Fill> fills, Agent a, int zoom)
        {
            double gx, gy;
            IsoMath.Project(a.GridX, a.GridY, out gx, out gy);
            gx *= zoom;
            gy *= zoom;
            double depth = a.GridX + a.GridY;
            double[] xs, ys;
            Ellipse(gx, gy, 7 * zoom, 3 * zoom, out xs, out ys);
            fills.Add(Make(xs, ys, Shadow, 2, depth - 0.5));
            Ellipse(gx, gy - 8 * zoom, 6 * zoom, 9 * zoom, out xs, out ys);
            fills.Add(Make(xs, ys, a.IsStaff ? StaffColor : CustomerColor, 4, depth));
        }
    }
}
