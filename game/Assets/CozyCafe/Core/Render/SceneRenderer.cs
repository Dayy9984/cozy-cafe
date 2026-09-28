using System;
using System.Collections.Generic;
using CozyCafe.Core.Iso;
using CozyCafe.Core.Layout;
using CozyCafe.Core.Scene;
using CozyCafe.Core.Tools;

namespace CozyCafe.Core.Render
{
    /// <summary>
    /// Software rasterizer of real game state used by the GameCli host's
    /// "render" command. Draws the iso floor (64x32 top faces with dark seam
    /// outlines), the 4 px visual-only side skirts on exterior edges only,
    /// furniture lifted by its screen-space render offset, and agents.
    /// Logical geometry is never modified — this only paints.
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

        private static readonly Rgba Background = Rgba.Opaque(28, 26, 32);
        private static readonly Rgba CanvasBg = Rgba.Opaque(20, 20, 24);
        private static readonly Rgba WoodA = Rgba.Opaque(176, 124, 78);
        private static readonly Rgba WoodB = Rgba.Opaque(160, 112, 70);
        private static readonly Rgba SeamLine = Rgba.Opaque(44, 44, 50);
        private static readonly Rgba SideRight = Rgba.Opaque(140, 96, 58);
        private static readonly Rgba SideLeft = Rgba.Opaque(120, 82, 48);
        private static readonly Rgba MarkRed = Rgba.Opaque(220, 60, 50);
        private static readonly Rgba GuideYellow = Rgba.Opaque(230, 200, 80);
        private static readonly Rgba GuideCyan = Rgba.Opaque(90, 200, 220);
        private static readonly Rgba GuideGreen = Rgba.Opaque(90, 210, 110);
        private static readonly Rgba GridDot = Rgba.Opaque(72, 72, 90);
        private static readonly Rgba TableTop = Rgba.Opaque(160, 102, 64);
        private static readonly Rgba TableSkirt = Rgba.Opaque(112, 72, 46);
        private static readonly Rgba ChairTop = Rgba.Opaque(176, 116, 74);
        private static readonly Rgba ChairSkirt = Rgba.Opaque(124, 80, 50);
        private static readonly Rgba DoorMat = Rgba.Opaque(88, 128, 92);
        private static readonly Rgba MachineBody = Rgba.Opaque(84, 88, 98);
        private static readonly Rgba MachineTop = Rgba.Opaque(204, 208, 218);
        private static readonly Rgba CounterBody = Rgba.Opaque(150, 104, 66);
        private static readonly Rgba CounterTop = Rgba.Opaque(196, 148, 100);
        private static readonly Rgba StaffColor = Rgba.Opaque(92, 142, 224);
        private static readonly Rgba CustomerColor = Rgba.Opaque(226, 146, 92);
        private static readonly Rgba Shadow = new Rgba(0, 0, 0, 70);

        public static byte[] RenderPng(GameScene scene, int zoom)
        {
            int w, h;
            byte[] rgba = RenderPixels(scene, zoom, out w, out h);
            return PngWriter.Encode(w, h, rgba);
        }

        /// RGBA pixel buffer of the composed view. The editor capture host
        /// presents these same pixels through its real camera pipeline, so
        /// both hosts emit the identical render.
        public static byte[] RenderPixels(GameScene scene, int zoom, out int width, out int height)
        {
            if (scene == null || scene.Room == null)
                throw new ArgumentNullException("scene");
            if (scene.TileCanvasView)
                return RenderTileCanvasPixels(out width, out height);
            if (scene.ArtContactView)
                return RenderArtContactPixels(scene, out width, out height);
            if (zoom < 1) zoom = 1;

            var fills = new List<Fill>();
            var room = scene.Room;

            for (int y = 0; y < room.Height; y++)
            {
                for (int x = 0; x < room.Width; x++)
                {
                    if (!room.HasCell(x, y)) continue;
                    double[] xs, ys, ixs, iys;
                    Diamond(x, y, zoom, out xs, out ys);
                    fills.Add(Make(xs, ys, SeamLine, 0, 0));
                    Inset(xs, ys, 0.92, out ixs, out iys);
                    bool odd = ((x + y) & 1) == 1;
                    fills.Add(Make(ixs, iys, odd ? WoodB : WoodA, 1, 0));

                    // Side faces paint the outer boundary only; interior
                    // edges between present cells get none (contract).
                    if (IsoContract.EmitsSideFace(room, x, y, 1, 0))
                        fills.Add(Make(SkirtX(xs[1], xs[2]),
                            SkirtY(ys[1], ys[2], zoom), SideRight, 2, 0));
                    if (IsoContract.EmitsSideFace(room, x, y, 0, 1))
                        fills.Add(Make(SkirtX(xs[3], xs[2]),
                            SkirtY(ys[3], ys[2], zoom), SideLeft, 2, 0));
                }
            }

            var furn = new List<Furniture>(scene.Furniture);
            furn.Sort(delegate (Furniture a, Furniture b)
            {
                int c = a.DepthKey.CompareTo(b.DepthKey);
                return c != 0 ? c : a.HostId.CompareTo(b.HostId);
            });
            var byId = new Dictionary<int, Furniture>();
            foreach (var f in furn) if (f.Id != 0) byId[f.Id] = f;
            foreach (var f in furn) AddFurniture(fills, f, zoom, byId);

            foreach (var a in scene.Agents) AddAgent(fills, a, zoom);

            fills.Sort(delegate (Fill a, Fill b)
            {
                int c = a.Order.CompareTo(b.Order);
                return c != 0 ? c : a.Depth.CompareTo(b.Depth);
            });

            int w, h;
            double tx, ty;
            if (scene.FixedViewport)
            {
                // Real viewport with a camera anchor: grid corner (0,0) lands
                // at (AnchorX, AnchorY); snap happens once at composite.
                w = scene.ViewportW;
                h = scene.ViewportH;
                tx = scene.AnchorX;
                ty = scene.AnchorY;
            }
            else
            {
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
                w = (int)Math.Ceiling(maxX - minX) + pad * 2;
                h = (int)Math.Ceiling(maxY - minY) + pad * 2;
                tx = pad - minX;
                ty = pad - minY;
            }

            var canvas = new SoftwareCanvas(w, h);
            canvas.Clear(Background);
            foreach (var f in fills)
            {
                var txs = new double[f.Xs.Length];
                var tys = new double[f.Ys.Length];
                for (int i = 0; i < f.Xs.Length; i++)
                {
                    txs[i] = f.Xs[i] + tx;
                    tys[i] = f.Ys[i] + ty;
                }
                canvas.FillPolygon(txs, tys, f.Color);
            }

            // Tip pixels at each diamond vertex land inside the closed top
            // face but off scanline centers - painting them keeps rendered
            // ink at the contract's exact 64x32 top-face extent.
            for (int y = 0; y < room.Height; y++)
            {
                for (int x = 0; x < room.Width; x++)
                {
                    if (!room.HasCell(x, y)) continue;
                    double[] vxs, vys;
                    Diamond(x, y, zoom, out vxs, out vys);
                    double ccx = 0, ccy = 0;
                    for (int i = 0; i < 4; i++) { ccx += vxs[i]; ccy += vys[i]; }
                    ccx /= 4;
                    ccy /= 4;
                    for (int i = 0; i < 4; i++)
                    {
                        double vx = ccx - vxs[i], vy = ccy - vys[i];
                        double len = Math.Sqrt(vx * vx + vy * vy);
                        if (len < 1e-9) continue;
                        canvas.SetPixel(
                            (int)Math.Floor(vxs[i] + vx / len * 0.51 + tx),
                            (int)Math.Floor(vys[i] + vy / len * 0.51 + ty),
                            SeamLine);
                    }
                }
            }

            // Assembled characters: real composited sprites standing on
            // their logical ground anchor, depth-sorted with each other —
            // shadow + facing tick + the layered sprite. Visual only.
            if (scene.Characters.Count > 0)
            {
                var chars = new List<CharacterPlacement>(scene.Characters);
                chars.Sort(delegate (CharacterPlacement a, CharacterPlacement b)
                {
                    return a.Depth.CompareTo(b.Depth);
                });
                foreach (var ch in chars)
                {
                    double cx, cy;
                    IsoMath.Project(ch.GridX, ch.GridY, out cx, out cy);
                    cx = cx * zoom + tx;
                    cy = cy * zoom + ty;
                    canvas.FillEllipse(cx, cy, 7 * zoom, 3 * zoom, Shadow);
                    BlitSprite(canvas, ch.Sprite,
                        (int)Math.Round(cx - ch.AnchorX * zoom),
                        (int)Math.Round(cy - ch.AnchorY * zoom), zoom);
                    // Facing tick: paint-only overlay pointing the way the
                    // sprite faces — drawn over the feet like the stage's
                    // other guide marks.
                    double vx, vy;
                    FacingVector(ch.Direction, out vx, out vy);
                    canvas.DrawLine(cx + vx * 4 * zoom, cy + vy * 4 * zoom,
                        cx + vx * 12 * zoom, cy + vy * 12 * zoom, GuideYellow,
                        Math.Max(1, 2 * zoom));
                }
            }

            // Stage overlays: red outline on the highlighted cell and the
            // yellow origin caret at grid corner (0,0) — paint only.
            if (scene.HighlightCellX >= 0 && scene.HighlightCellY >= 0)
            {
                double[] hxs, hys;
                Diamond(scene.HighlightCellX, scene.HighlightCellY, zoom, out hxs, out hys);
                for (int i = 0; i < 4; i++)
                {
                    int j = (i + 1) % 4;
                    canvas.DrawLine(hxs[i] + tx, hys[i] + ty,
                        hxs[j] + tx, hys[j] + ty, MarkRed, 2);
                }
            }
            if (scene.ShowOriginCaret)
            {
                double ax = tx, ay = ty;
                canvas.DrawLine(ax - 8, ay - 18, ax - 32, ay - 3, GuideYellow, 2);
                canvas.DrawLine(ax + 8, ay - 18, ax + 32, ay - 3, GuideYellow, 2);
            }
            // Mini-mode window chrome + the real tools panel snapshot —
            // paint-only overlays like the other stage marks.
            if (scene.MiniMode) PaintWindowChrome(canvas, w);
            if (scene.ToolsPanel != null) PaintToolsPanel(canvas, scene.ToolsPanel, w, h);
            // Shared-UI overlay: the frame built by the real UiModule —
            // 9-slice skins at widget rects, live-state cues, runtime text,
            // the focused input's caret. Screen space, never iso.
            if (scene.UiFrame != null && scene.UiModule != null)
            {
                scene.UiModule.Paint(canvas, scene.UiFrame);
            }

            width = w;
            height = h;
            return canvas.Pixels;
        }

        /// The tile-authoring view: the real 64x64 canvas raster magnified
        /// 4x on a dark viewport, with a pixel-grid dot lattice, a separator
        /// at the top-face band bottom, and the canvas bounds outlined.
        private static byte[] RenderTileCanvasPixels(out int width, out int height)
        {
            const int zoom = 4;
            var src = TileArt.RasterizeFloorTile();
            int cw = TileCanvasContract.WidthPx;
            int ch = TileCanvasContract.HeightPx;
            const int w = 320, h = 310, ox = 10, oy = 10;
            var dev = new SoftwareCanvas(w, h);
            dev.Clear(CanvasBg);

            // Pixel-grid dots inside the canvas (tile pixels cover their own).
            for (int y = oy; y < oy + ch * zoom; y++)
            {
                for (int x = ox; x < ox + cw * zoom; x++)
                {
                    if ((x & 3) == 3 && (y & 3) == 3) dev.SetPixel(x, y, GridDot);
                }
            }
            // Magnified tile pixels.
            for (int sy = 0; sy < ch; sy++)
            {
                for (int sx = 0; sx < cw; sx++)
                {
                    Rgba c = src.GetPixel(sx, sy);
                    if (c.A == 0) continue;
                    dev.FillRect(ox + sx * zoom, oy + sy * zoom, zoom, zoom, c);
                }
            }
            // Guide at the bottom edge of the 64x32 top-face band.
            dev.FillRect(ox, oy + IsoMath.TileTopHeightPx * zoom, cw * zoom, 2, GuideYellow);
            // Canvas bounds.
            dev.FillRect(ox, oy, cw * zoom, 2, MarkRed);
            dev.FillRect(ox, oy + ch * zoom - 2, cw * zoom, 2, MarkRed);
            dev.FillRect(ox, oy, 2, ch * zoom, MarkRed);
            dev.FillRect(ox + cw * zoom - 2, oy, 2, ch * zoom, MarkRed);
            width = w;
            height = h;
            return dev.Pixels;
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

        /// Diamond shrunk toward its centroid — draws the tile's seam outline.
        private static void Inset(double[] xs, double[] ys, double keep,
            out double[] ixs, out double[] iys)
        {
            double cx = 0, cy = 0;
            for (int i = 0; i < xs.Length; i++) { cx += xs[i]; cy += ys[i]; }
            cx /= xs.Length; cy /= xs.Length;
            ixs = new double[xs.Length];
            iys = new double[ys.Length];
            for (int i = 0; i < xs.Length; i++)
            {
                ixs[i] = cx + (xs[i] - cx) * keep;
                iys[i] = cy + (ys[i] - cy) * keep;
            }
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

        /// Iso ground direction -> screen vector for the facing tick.
        private static void FacingVector(string dir, out double vx, out double vy)
        {
            switch (dir)
            {
                case "SW": vx = -0.894; vy = 0.447; return;
                case "SE": vx = 0.894; vy = 0.447; return;
                case "NW": vx = -0.894; vy = -0.447; return;
                default: vx = 0.894; vy = -0.447; return; // NE
            }
        }

        /// Nearest-neighbor blit of a composited character sprite.
        private static void BlitSprite(SoftwareCanvas dst, SoftwareCanvas src,
            int ox, int oy, int zoom)
        {
            byte[] sp = src.Pixels;
            for (int y = 0; y < src.Height; y++)
            {
                for (int x = 0; x < src.Width; x++)
                {
                    int i = (y * src.Width + x) * 4;
                    if (sp[i + 3] == 0) continue;
                    var c = new Rgba(sp[i], sp[i + 1], sp[i + 2], sp[i + 3]);
                    if (zoom <= 1) dst.SetPixel(ox + x, oy + y, c);
                    else dst.FillRect(ox + x * zoom, oy + y * zoom, zoom, zoom, c);
                }
            }
        }

        private static void AddFurniture(List<Fill> fills, Furniture f, int zoom,
            Dictionary<int, Furniture> byId)
        {
            double gx, gy;
            IsoMath.Project(f.CellX + 0.5, f.CellY + 0.5, out gx, out gy);
            gx *= zoom;
            gy *= zoom;
            // The real render transform: (ground + target) * zoom for floor
            // pieces; children draw through their host's mount once.
            double dx, dy;
            RenderContract.DrawAnchorResolved(f,
                delegate (int id)
                {
                    Furniture h;
                    return byId.TryGetValue(id, out h) ? h : null;
                },
                zoom, out dx, out dy);
            double depth = f.DepthKey;

            double[] xs, ys;
            if (f.HostId == 0)
            {
                Ellipse(gx, gy, 13 * zoom, 4.5 * zoom, out xs, out ys);
                fills.Add(Make(xs, ys, Shadow, 3, depth - 0.5));
            }

            switch (f.Kind)
            {
                case FurnitureKind.Table:
                    Ellipse(dx, dy + 8 * zoom, 3 * zoom, 6 * zoom, out xs, out ys);
                    fills.Add(Make(xs, ys, TableSkirt, 4, depth));
                    DiamondAt(dx, dy, 20 * zoom, 9 * zoom, out xs, out ys);
                    fills.Add(Make(SkirtX(xs[3], xs[2]), SkirtY(ys[3], ys[2], zoom),
                        TableSkirt, 4, depth + 0.1));
                    fills.Add(Make(xs, ys, TableTop, 4, depth));
                    break;
                case FurnitureKind.Chair:
                case FurnitureKind.Stool:
                    DiamondAt(dx, dy, 11 * zoom, 5 * zoom, out xs, out ys);
                    fills.Add(Make(SkirtX(xs[3], xs[2]), SkirtY(ys[3], ys[2], zoom),
                        ChairSkirt, 4, depth + 0.1));
                    fills.Add(Make(xs, ys, ChairTop, 4, depth));
                    Rect(dx - 9 * zoom, dy - 14 * zoom, dx - 5 * zoom, dy, out xs, out ys);
                    fills.Add(Make(xs, ys, ChairSkirt, 4, depth - 0.1));
                    break;
                case FurnitureKind.Counter:
                    Rect(dx - 15 * zoom, dy - 10 * zoom, dx + 15 * zoom, dy + 2 * zoom,
                        out xs, out ys);
                    fills.Add(Make(xs, ys, CounterBody, 4, depth));
                    DiamondAt(dx, dy - 10 * zoom, 15 * zoom, 6 * zoom, out xs, out ys);
                    fills.Add(Make(xs, ys, CounterTop, 4, depth + 0.1));
                    break;
                case FurnitureKind.Door:
                    DiamondAt(dx, dy + 2 * zoom, 16 * zoom, 7 * zoom, out xs, out ys);
                    fills.Add(Make(xs, ys, DoorMat, 2, depth - 0.4));
                    break;
                default: // machines: grounded box, no lift
                    Rect(dx - 13 * zoom, dy - 12 * zoom, dx + 13 * zoom, dy + 2 * zoom,
                        out xs, out ys);
                    fills.Add(Make(xs, ys, MachineBody, 4, depth));
                    DiamondAt(dx, dy - 12 * zoom, 13 * zoom, 5 * zoom, out xs, out ys);
                    fills.Add(Make(xs, ys, MachineTop, 4, depth + 0.1));
                    break;
            }
        }

        /// The mini window's title bar: an always-on-top chrome strip with
        /// the window dots and a pin — pure paint over the composed view.
        private static void PaintWindowChrome(SoftwareCanvas canvas, int w)
        {
            canvas.FillRect(0, 0, w, 16, Rgba.Opaque(38, 34, 44));
            canvas.FillRect(0, 16, w, 1, Rgba.Opaque(58, 52, 68));
            canvas.FillEllipse(9, 8, 3.5, 3.5, Rgba.Opaque(224, 96, 88));
            canvas.FillEllipse(21, 8, 3.5, 3.5, Rgba.Opaque(232, 190, 80));
            canvas.FillEllipse(33, 8, 3.5, 3.5, Rgba.Opaque(120, 200, 120));
            canvas.FillRect(w - 14, 4, 8, 8, Rgba.Opaque(200, 196, 220));
            canvas.FillRect(w - 11, 12, 2, 3, Rgba.Opaque(200, 196, 220));
        }

        /// The docked work-tools strip: four cells painting the module's
        /// real snapshot — memo lines scaled by their actual text length,
        /// todo checkboxes by their done flags, the timer ring + digits by
        /// the exact remaining seconds, and the local deck's controls.
        private static void PaintToolsPanel(SoftwareCanvas canvas,
            ToolPanel p, int vw, int vh)
        {
            int panelX = 10, panelY = vh - 132, panelW = vw - 20, panelH = 118;
            canvas.FillRect(panelX - 1, panelY - 1, panelW + 2, panelH + 2,
                Rgba.Opaque(16, 14, 20));
            canvas.FillRect(panelX, panelY, panelW, panelH,
                Rgba.Opaque(34, 30, 40));
            int gap = 6;
            int cellW = (panelW - gap * 5) / 4;
            int cellH = panelH - gap * 2;
            PaintMemoCell(canvas, p, panelX + gap, panelY + gap, cellW, cellH);
            PaintTodoCell(canvas, p, panelX + gap + (cellW + gap),
                panelY + gap, cellW, cellH);
            PaintTimerCell(canvas, p, panelX + gap + (cellW + gap) * 2,
                panelY + gap, cellW, cellH);
            PaintMusicCell(canvas, p, panelX + gap + (cellW + gap) * 3,
                panelY + gap, cellW, cellH);
        }

        private static void PaintMemoCell(SoftwareCanvas canvas, ToolPanel p,
            int cx, int cy, int cw, int ch)
        {
            canvas.FillRect(cx, cy, cw, ch, Rgba.Opaque(44, 40, 52));
            // Stacked sheets behind the top page = the real memo count.
            for (int i = Math.Min(p.MemoCount - 1, 2); i > 0; i--)
            {
                canvas.FillRect(cx + 8 + i * 3, cy + 8 - i * 3, cw - 16,
                    ch - 16, Rgba.Opaque(208, 200, 180));
            }
            canvas.FillRect(cx + 8, cy + 8, cw - 16, ch - 16,
                Rgba.Opaque(238, 232, 216));
            int lines = Math.Min(p.MemoLineChars.Count, 5);
            for (int i = 0; i < lines; i++)
            {
                int lw = Math.Min(cw - 34, p.MemoLineChars[i] * 3);
                if (lw <= 0) lw = 3;
                canvas.FillRect(cx + 15, cy + 15 + i * 15, lw, 4,
                    Rgba.Opaque(96, 88, 108));
            }
        }

        private static void PaintTodoCell(SoftwareCanvas canvas, ToolPanel p,
            int cx, int cy, int cw, int ch)
        {
            canvas.FillRect(cx, cy, cw, ch, Rgba.Opaque(44, 40, 52));
            int rows = Math.Min(p.TodoDone.Count, 5);
            for (int i = 0; i < rows; i++)
            {
                int ry = cy + 10 + i * 19;
                bool done = p.TodoDone[i];
                canvas.FillRect(cx + 8, ry, 11, 11, Rgba.Opaque(30, 27, 37));
                canvas.FillRect(cx + 9, ry + 1, 9, 9,
                    done ? Rgba.Opaque(96, 200, 170) : Rgba.Opaque(90, 86, 104));
                int tw = Math.Min(cw - 36, p.TodoTextChars[i] * 3);
                if (tw <= 0) tw = 4;
                var ink = done ? Rgba.Opaque(84, 78, 96) : Rgba.Opaque(206, 200, 220);
                canvas.FillRect(cx + 25, ry + 3, tw, 5, ink);
                if (done)
                {
                    canvas.DrawLine(cx + 25, ry + 5, cx + 25 + tw, ry + 5,
                        Rgba.Opaque(96, 200, 170), 1);
                }
            }
        }

        private static void PaintTimerCell(SoftwareCanvas canvas, ToolPanel p,
            int cx, int cy, int cw, int ch)
        {
            canvas.FillRect(cx, cy, cw, ch, Rgba.Opaque(44, 40, 52));
            double rcx = cx + cw / 2.0, rcy = cy + 30;
            canvas.FillEllipse(rcx, rcy, 21, 21, Rgba.Opaque(70, 64, 84));
            double frac = p.TimerDuration > 0
                ? 1.0 - p.TimerRemaining / p.TimerDuration : 0;
            RingWedge(canvas, rcx, rcy, 21, frac, Rgba.Opaque(224, 132, 90));
            canvas.FillEllipse(rcx, rcy, 14, 14, Rgba.Opaque(44, 40, 52));
            // Pause bars while paused, a run tick while running.
            if (p.TimerState == TimerState.Paused)
            {
                canvas.FillRect((int)rcx - 5, (int)rcy - 5, 3, 10,
                    Rgba.Opaque(232, 190, 90));
                canvas.FillRect((int)rcx + 2, (int)rcy - 5, 3, 10,
                    Rgba.Opaque(232, 190, 90));
            }
            else if (p.TimerState == TimerState.Running)
            {
                double[] xs, ys;
                Triangle((int)rcx - 3, (int)rcy - 5, (int)rcx - 3,
                    (int)rcy + 5, (int)rcx + 6, (int)rcy, out xs, out ys);
                canvas.FillPolygon(xs, ys, Rgba.Opaque(120, 200, 120));
            }
            // mm:ss of the exact remaining seconds.
            int rem = (int)Math.Max(0, p.TimerRemaining);
            string clock = (rem / 60).ToString("D2") + ":"
                + (rem % 60).ToString("D2");
            DrawTinyText(canvas, clock, cx + cw / 2 - 19, cy + 62, 2,
                Rgba.Opaque(236, 232, 244));
            // Focus seconds banked so far, as a thin accumulation bar.
            int banked = Math.Min(cw - 16, (int)(p.FocusSecondsTotal / 60));
            canvas.FillRect(cx + 8, cy + ch - 12, cw - 16, 4,
                Rgba.Opaque(30, 27, 37));
            if (banked > 0)
            {
                canvas.FillRect(cx + 8, cy + ch - 12, banked, 4,
                    Rgba.Opaque(96, 200, 170));
            }
        }

        private static void PaintMusicCell(SoftwareCanvas canvas, ToolPanel p,
            int cx, int cy, int cw, int ch)
        {
            canvas.FillRect(cx, cy, cw, ch, Rgba.Opaque(44, 40, 52));
            double dcx = cx + 24, dcy = cy + 28;
            canvas.FillEllipse(dcx, dcy, 17, 17, Rgba.Opaque(28, 26, 34));
            canvas.FillEllipse(dcx, dcy, 5, 5, Rgba.Opaque(140, 170, 240));
            // Track index pips across the top right.
            for (int i = 0; i < p.MusicTrackCount && i < 6; i++)
            {
                canvas.FillEllipse(cx + cw - 14 - i * 9, cy + 10, 3, 3,
                    i == p.MusicTrackIndex
                        ? Rgba.Opaque(140, 170, 240) : Rgba.Opaque(70, 64, 84));
            }
            // Transport glyph: pause bars while playing, play triangle else.
            int gx = cx + cw / 2 + 8, gy = cy + 20;
            if (p.MusicPlaying)
            {
                canvas.FillRect(gx, gy, 4, 14, Rgba.Opaque(236, 232, 244));
                canvas.FillRect(gx + 7, gy, 4, 14, Rgba.Opaque(236, 232, 244));
            }
            else
            {
                double[] xs, ys;
                Triangle(gx, gy, gx, gy + 14, gx + 11, gy + 7, out xs, out ys);
                canvas.FillPolygon(xs, ys, Rgba.Opaque(236, 232, 244));
            }
            // Progress bar = real track position over its real duration.
            int bx = cx + 10, bw = cw - 20;
            canvas.FillRect(bx, cy + 56, bw, 5, Rgba.Opaque(30, 27, 37));
            double pos = p.MusicTrackDuration > 0
                ? p.MusicPosition / p.MusicTrackDuration : 0;
            if (pos > 0)
            {
                canvas.FillRect(bx, cy + 56,
                    Math.Max(2, (int)(bw * Math.Min(1, pos))), 5,
                    Rgba.Opaque(140, 170, 240));
            }
            // Volume: five ticks lit by the deck's real level.
            for (int i = 0; i < 5; i++)
            {
                int vh2 = 4 + i * 2;
                bool lit = p.MusicVolume * 5 >= i + 1;
                canvas.FillRect(cx + 10 + i * 9, cy + 78 - vh2, 6, vh2,
                    lit ? Rgba.Opaque(140, 170, 240) : Rgba.Opaque(70, 64, 84));
            }
        }

        /// Elapsed-slice wedge on the timer ring: 12 o'clock, clockwise.
        private static void RingWedge(SoftwareCanvas canvas, double cx,
            double cy, double r, double fraction, Rgba color)
        {
            if (fraction <= 0) return;
            if (fraction > 1) fraction = 1;
            var xs = new List<double> { cx };
            var ys = new List<double> { cy };
            int steps = Math.Max(2, (int)Math.Ceiling(fraction * 24));
            for (int i = 0; i <= steps; i++)
            {
                double a = -Math.PI / 2
                    + (i / (double)steps) * fraction * 2 * Math.PI;
                xs.Add(cx + Math.Cos(a) * r);
                ys.Add(cy + Math.Sin(a) * r);
            }
            canvas.FillPolygon(xs, ys, color);
        }

        private static void Triangle(int x0, int y0, int x1, int y1,
            int x2, int y2, out double[] xs, out double[] ys)
        {
            xs = new double[] { x0, x1, x2 };
            ys = new double[] { y0, y1, y2 };
        }

        /// 3x5 digit font for the timer's real remaining mm:ss.
        private static readonly string[] TinyGlyphs =
        {
            "###", "#.#", "#.#", "#.#", "###",
            ".#.", "##.", ".#.", ".#.", "###",
            "###", "..#", "###", "#..", "###",
            "###", "..#", "###", "..#", "###",
            "#.#", "#.#", "###", "..#", "..#",
            "###", "#..", "###", "..#", "###",
            "###", "#..", "###", "#.#", "###",
            "###", "..#", "..#", "..#", "..#",
            "###", "#.#", "###", "#.#", "###",
            "###", "#.#", "###", "..#", "###",
            "...", ".#.", "...", ".#.", "...",
        };

        private static void DrawTinyText(SoftwareCanvas canvas, string text,
            int x, int y, int scale, Rgba color)
        {
            foreach (char ch in text)
            {
                int g = ch == ':' ? 10 : ch - '0';
                if (g >= 0 && g <= 10)
                {
                    for (int r = 0; r < 5; r++)
                    {
                        string row = TinyGlyphs[g * 5 + r];
                        for (int c = 0; c < 3; c++)
                        {
                            if (row[c] == '#')
                            {
                                canvas.FillRect(x + c * scale, y + r * scale,
                                    scale, scale, color);
                            }
                        }
                    }
                }
                x += 4 * scale;
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
            fills.Add(Make(xs, ys, Shadow, 3, depth - 0.5));
            Ellipse(gx, gy - 8 * zoom, 6 * zoom, 9 * zoom, out xs, out ys);
            fills.Add(Make(xs, ys, a.IsStaff ? StaffColor : CustomerColor, 5, depth));
        }

        /// <summary>
        /// The art-pipeline stage view: every approved atlas frame staged by
        /// StageScenes, magnified 2x over a dark panel with its contract
        /// canvas border (red), ground anchor crosshair (yellow), the 64x32
        /// top-face band guide on tiles (cyan), and the furniture -8 px
        /// effective-offset marker (green) - the renderer's own evidence
        /// that the approved cells carry the declared geometry.
        /// </summary>
        private static byte[] RenderArtContactPixels(GameScene scene,
            out int width, out int height)
        {
            const int zoom = 2, pad = 12, gutter = 12;
            const int maxRowW = 1700;
            var cells = scene.ArtCells;
            int x = pad, y = pad, rowH = 0, w = 0;
            var pos = new List<int[]>();
            foreach (var c in cells)
            {
                int cw = c.Sprite.Width * zoom, ch = c.Sprite.Height * zoom;
                if (x > pad && x + cw > maxRowW) { x = pad; y += rowH + gutter; rowH = 0; }
                pos.Add(new[] { x, y });
                x += cw + gutter;
                if (ch > rowH) rowH = ch;
                if (x > w) w = x;
            }
            int h = y + rowH + pad;
            w += pad - gutter;

            var dev = new SoftwareCanvas(w, h);
            dev.Clear(Background);
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                int cw = c.Sprite.Width * zoom, ch = c.Sprite.Height * zoom;
                int ox = pos[i][0], oy = pos[i][1];
                dev.FillRect(ox, oy, cw, ch, CanvasBg);
                BlendSprite(dev, c.Sprite, ox, oy, zoom);

                // contract canvas border
                dev.FillRect(ox, oy, cw, 1, MarkRed);
                dev.FillRect(ox, oy + ch - 1, cw, 1, MarkRed);
                dev.FillRect(ox, oy, 1, ch, MarkRed);
                dev.FillRect(ox + cw - 1, oy, 1, ch, MarkRed);

                if (c.Category == "tile")
                {
                    dev.FillRect(ox, oy + 32 * zoom, cw, 1, GuideCyan);
                }
                // anchor crosshair
                int ax = ox + c.AnchorX * zoom, ay = oy + c.AnchorY * zoom;
                for (int d = -6; d <= 6; d++)
                {
                    dev.SetPixel(ax + d, ay, GuideYellow);
                    dev.SetPixel(ax, ay + d, GuideYellow);
                }
                if (c.Category == "furniture" && c.RenderDy != 0)
                {
                    // the single applied lift: marker at anchor+(-8)
                    int ey = ay + (c.BakedDy + c.RenderDy) * zoom;
                    for (int d = -6; d <= 6; d++)
                    {
                        dev.SetPixel(ax + d, ey, GuideGreen);
                        dev.SetPixel(ax, ey + d, GuideGreen);
                    }
                }
            }
            width = w;
            height = h;
            return dev.Pixels;
        }

        /// Alpha-composited nearest-neighbor blit (art cells carry real alpha).
        private static void BlendSprite(SoftwareCanvas dst, SoftwareCanvas src,
            int ox, int oy, int zoom)
        {
            byte[] sp = src.Pixels;
            for (int y = 0; y < src.Height; y++)
            {
                for (int x = 0; x < src.Width; x++)
                {
                    int i = (y * src.Width + x) * 4;
                    int a = sp[i + 3];
                    if (a == 0) continue;
                    var c = new Rgba(sp[i], sp[i + 1], sp[i + 2], sp[i + 3]);
                    if (zoom <= 1) BlendPixel(dst, ox + x, oy + y, c);
                    else
                    {
                        for (int dy = 0; dy < zoom; dy++)
                        {
                            for (int dx = 0; dx < zoom; dx++)
                            {
                                BlendPixel(dst, ox + x * zoom + dx,
                                    oy + y * zoom + dy, c);
                            }
                        }
                    }
                }
            }
        }

        /// Source-over a single pixel (same rule as the iso compositor).
        private static void BlendPixel(SoftwareCanvas dst, int x, int y, Rgba c)
        {
            if (x < 0 || y < 0 || x >= dst.Width || y >= dst.Height) return;
            int i = (y * dst.Width + x) * 4;
            int a = c.A;
            if (a == 255)
            {
                dst.Pixels[i] = c.R; dst.Pixels[i + 1] = c.G;
                dst.Pixels[i + 2] = c.B; dst.Pixels[i + 3] = 255;
                return;
            }
            int inv = 255 - a;
            dst.Pixels[i] = (byte)((c.R * a + dst.Pixels[i] * inv) / 255);
            dst.Pixels[i + 1] = (byte)((c.G * a + dst.Pixels[i + 1] * inv) / 255);
            dst.Pixels[i + 2] = (byte)((c.B * a + dst.Pixels[i + 2] * inv) / 255);
            dst.Pixels[i + 3] = 255;
        }
    }
}
