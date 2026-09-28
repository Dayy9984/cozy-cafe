using System;
using CozyCafe.Core.Iso;
using CozyCafe.Core.Scene;

namespace CozyCafe.Core.Layout
{
    /// <summary>
    /// The v0.8 (0,-4) screen-space render-offset pipeline.
    ///
    ///   p_draw = (p_ground + target_offset) * camera_zoom + camera_origin
    ///
    /// The target comes from the asset contract table (tables/chairs/stools
    /// (0,-4); everything else (0,0)). runtime = target - baked, so
    /// effective = baked + runtime is applied exactly once - unbaked source
    /// pivot plus renderer -4, or recorded baked -4 plus renderer 0, never
    /// twice. The offset is always screen-up: it never rotates with the
    /// furniture, is not physical elevation, and never feeds logical cells,
    /// footprints, collision, pathfinding, service links or depth keys.
    /// Picking and ghosts share this real render transform while seat/cell
    /// judgement stays on the unshifted logical anchors.
    /// </summary>
    public static class RenderContract
    {
        /// Contract target offset for a kind, in source-art px at zoom 1.
        public static void TargetOffset(FurnitureKind kind, out double dx, out double dy)
        {
            int ox, oy;
            RenderOffsetTable.For(kind, out ox, out oy);
            dx = ox;
            dy = oy;
        }

        /// Runtime remainder for a source-art baked offset: target - baked.
        /// Unbaked art (0,0) yields the full (0,-4); art already baked at -4
        /// yields 0 so the effective sum stays -4 exactly once.
        public static void RuntimeOffset(FurnitureKind kind,
            double bakedX, double bakedY, out double dx, out double dy)
        {
            double tx, ty;
            TargetOffset(kind, out tx, out ty);
            dx = tx - bakedX;
            dy = ty - bakedY;
        }

        /// baked + runtime == target - the single effective offset.
        public static void EffectiveOffset(FurnitureKind kind,
            double bakedX, double bakedY, out double dx, out double dy)
        {
            double rx, ry;
            RuntimeOffset(kind, bakedX, bakedY, out rx, out ry);
            dx = bakedX + rx;
            dy = bakedY + ry;
        }

        /// Effective offset a placed piece resolves at draw time (unbaked
        /// authored art path: baked (0,0)). Rotation never enters here.
        public static void EffectiveOffset(Furniture f, out double dx, out double dy)
        {
            TargetOffset(f.Kind, out dx, out dy);
        }

        /// The unshifted logical ground anchor of a cell - what collision,
        /// pathfinding, depth keys and seat judgement all use.
        public static void GroundAnchor(int cellX, int cellY, out double gx, out double gy)
        {
            IsoMath.Project(cellX + 0.5, cellY + 0.5, out gx, out gy);
        }

        /// Screen draw anchor for a floor-placed piece:
        /// (ground + target) * zoom. At zoom 2 the -4 becomes -8.
        public static void DrawAnchor(Furniture f, double zoom, out double dx, out double dy)
        {
            double gx, gy, ox, oy;
            GroundAnchor(f.CellX, f.CellY, out gx, out gy);
            TargetOffset(f.Kind, out ox, out oy);
            dx = (gx + ox) * zoom;
            dy = (gy + oy) * zoom;
        }

        /// Local mount point on the host sprite where a mounted child sits,
        /// in pre-zoom art px. The parent's own correction is already inside
        /// the mount transform - it is never re-applied to the child.
        public static void MountLocal(FurnitureKind hostKind, out double dx, out double dy)
        {
            switch (hostKind)
            {
                case FurnitureKind.Counter:
                    dx = 0.0;
                    dy = -4.5;
                    return;
                case FurnitureKind.Table:
                    dx = 0.0;
                    dy = -2.5;
                    return;
                default:
                    dx = 0.0;
                    dy = 0.0;
                    return;
            }
        }

        /// Draw anchor for any piece, resolving mounted children through the
        /// parent's mount transform once: child = hostDraw + (mountLocal +
        /// childTarget) * zoom. The parent's correction appears exactly once
        /// inside the result - never repeated on the child.
        public static void DrawAnchorResolved(Furniture f,
            Func<int, Furniture> hostLookup, double zoom, out double dx, out double dy)
        {
            if (f.HostId != 0 && hostLookup != null)
            {
                var host = hostLookup(f.HostId);
                if (host != null)
                {
                    double hx, hy, mx, my, cx, cy;
                    DrawAnchor(host, zoom, out hx, out hy);
                    MountLocal(host.Kind, out mx, out my);
                    TargetOffset(f.Kind, out cx, out cy);
                    dx = hx + (mx + cx) * zoom;
                    dy = hy + (my + cy) * zoom;
                    return;
                }
            }
            DrawAnchor(f, zoom, out dx, out dy);
        }

        /// Standing characters draw at their logical ground anchor - the
        /// table/chair lift rule never applies to agents.
        public static void AgentDrawAnchor(Agent a, double zoom, out double dx, out double dy)
        {
            double gx, gy;
            IsoMath.Project(a.GridX, a.GridY, out gx, out gy);
            dx = gx * zoom;
            dy = gy * zoom;
        }

        /// Ghost pieces share the exact render transform of a placed piece -
        /// the visual the player drags is the visual that draws.
        public static void GhostAnchor(FurnitureKind kind, int cellX, int cellY,
            double zoom, out double dx, out double dy)
        {
            double gx, gy, ox, oy;
            GroundAnchor(cellX, cellY, out gx, out gy);
            TargetOffset(kind, out ox, out oy);
            dx = (gx + ox) * zoom;
            dy = (gy + oy) * zoom;
        }

        /// Picking shares the real render transform of the placed piece.
        public static void PickAnchor(Furniture f, double zoom, out double dx, out double dy)
        {
            DrawAnchor(f, zoom, out dx, out dy);
        }
    }
}
