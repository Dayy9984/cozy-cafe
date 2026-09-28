using System;
using System.Collections.Generic;
using CozyCafe.Core.Modules;
using CozyCafe.Core.Scene;

namespace CozyCafe.Core.Layout
{
    /// Reject reasons surfaced by real rule evaluations in the editor.
    public enum PlacementReject
    {
        None = 0,
        OutOfBounds,
        CellMissing,
        Occupied,
        NoChange,
        DoorSlotInvalid,
        ConnectivityBroken,
        TableNeedsSeat,
        SeatNeedsTable,
        MachineNeedsHost,
        NotAHost,
        IsChild,
        Unknown
    }

    public sealed class PlacementResult
    {
        public readonly bool Ok;
        public readonly PlacementReject Reason;
        public readonly Furniture Placed;
        /// Room cells actually flipped by a tile-paint op (0 or 1).
        public readonly int ChangedCells;

        private PlacementResult(bool ok, PlacementReject reason, Furniture placed, int changed)
        {
            Ok = ok;
            Reason = reason;
            Placed = placed;
            ChangedCells = changed;
        }

        public static PlacementResult Reject(PlacementReject r)
        {
            return new PlacementResult(false, r, null, 0);
        }

        public static PlacementResult Success(Furniture f, int changed)
        {
            return new PlacementResult(true, PlacementReject.None, f, changed);
        }
    }

    /// <summary>
    /// The layout/furniture editor over a live GameScene: 1x1 tile paint,
    /// outer-wall door slots, validated furniture/machine placement, grouped
    /// undo/redo (one drag = one command) and atomic host-subtree
    /// move/rotate/remove. Placement rules are validity checks - never
    /// revenue bottlenecks - evaluated draft/apply style: a drag may pass
    /// through incomplete states but a command applies only when the whole
    /// layout is valid. Every check judges logical cells; the (0,-4) render
    /// offset lives in RenderContract and never feeds back here.
    /// </summary>
    public sealed class LayoutModule : ModuleBase
    {
        public override string Name { get { return "layout"; } }

        public GameScene Scene { get; private set; }

        private int nextId = 1;
        private readonly List<EditCommand> undoStack = new List<EditCommand>();
        private readonly List<EditCommand> redoStack = new List<EditCommand>();
        private EditCommand draft;

        private sealed class EditCommand
        {
            public string Before;
            public string After;
            public int Depth;
        }

        public int UndoDepth { get { return undoStack.Count; } }
        public int RedoDepth { get { return redoStack.Count; } }
        public bool CommandOpen { get { return draft != null; } }

        private static readonly int[] Dxs = { 1, -1, 0, 0 };
        private static readonly int[] Dys = { 0, 0, 1, -1 };

        public LayoutModule(GameScene scene)
        {
            Scene = scene;
            // Ids for authored content, and auto-link machines stacked on a
            // hostable cell so authored and edited layouts share one model.
            foreach (var f in Scene.Furniture)
            {
                if (f.Id <= 0) f.Id = nextId++;
                else if (f.Id >= nextId) nextId = f.Id + 1;
            }
            foreach (var f in Scene.Furniture)
            {
                if (f.HostId != 0 || !RequiresHost(f.Kind)) continue;
                var host = HostAt(f.CellX, f.CellY, f);
                if (host != null) f.HostId = host.Id;
            }
        }

        // ---- kind predicates -------------------------------------------------

        public static bool IsMachineKind(FurnitureKind k)
        {
            return k == FurnitureKind.EspressoMachine
                || k == FurnitureKind.Grinder
                || k == FurnitureKind.Steamer
                || k == FurnitureKind.IceMaker
                || k == FurnitureKind.Blender;
        }

        /// The five machines mount on a compatible host - they never sit on
        /// the bare floor.
        public static bool RequiresHost(FurnitureKind k) { return IsMachineKind(k); }

        /// Counters and tables are the compatible machine stands.
        public static bool CanHost(FurnitureKind k)
        {
            return k == FurnitureKind.Counter || k == FurnitureKind.Table;
        }

        public static bool IsSeat(FurnitureKind k)
        {
            return k == FurnitureKind.Chair || k == FurnitureKind.Stool;
        }

        /// Doors may only occupy outer-wall slots - perimeter room cells.
        public bool IsDoorSlot(int x, int y)
        {
            var r = Scene.Room;
            return r.InBounds(x, y)
                && (x == 0 || y == 0 || x == r.Width - 1 || y == r.Height - 1);
        }

        // ---- lookups ----------------------------------------------------------

        public Furniture Find(int id)
        {
            foreach (var f in Scene.Furniture) if (f.Id == id) return f;
            return null;
        }

        /// The floor-level furniture occupying (x,y) - children share their
        /// host's cell and never count as occupants.
        public Furniture OccupyingAt(int x, int y)
        {
            foreach (var f in Scene.Furniture)
            {
                if (f.HostId == 0 && f.CellX == x && f.CellY == y) return f;
            }
            return null;
        }

        private Furniture HostAt(int x, int y, Furniture ignore)
        {
            foreach (var f in Scene.Furniture)
            {
                if (f != ignore && f.HostId == 0 && f.CellX == x && f.CellY == y
                    && CanHost(f.Kind)) return f;
            }
            return null;
        }

        public List<Furniture> ChildrenOf(int hostId)
        {
            var kids = new List<Furniture>();
            foreach (var f in Scene.Furniture) if (f.HostId == hostId) kids.Add(f);
            return kids;
        }

        /// Host lookup used by the render contract to resolve mounted
        /// children through their parent's mount transform.
        public Furniture LookupHost(int id) { return Find(id); }

        // ---- command grouping (one drag = one command) ------------------------

        public void BeginCommand()
        {
            if (draft == null) draft = new EditCommand { Before = Snapshot(), Depth = 0 };
            draft.Depth++;
        }

        /// Closes the open drag: the group applies as one atomic commit.
        /// On any invalid result the whole drag rolls back and no undo entry
        /// is recorded.
        public PlacementReject EndCommand()
        {
            if (draft == null) return PlacementReject.None;
            if (--draft.Depth > 0) return PlacementReject.None;
            var c = draft;
            draft = null;
            var v = ValidateLayout();
            if (v != PlacementReject.None)
            {
                Restore(c.Before);
                return v;
            }
            c.After = Snapshot();
            if (c.After != c.Before)
            {
                undoStack.Add(c);
                redoStack.Clear();
            }
            return PlacementReject.None;
        }

        /// Drops the open drag, restoring its pre-state.
        public void AbortCommand()
        {
            if (draft == null) return;
            var c = draft;
            draft = null;
            Restore(c.Before);
        }

        public bool Undo()
        {
            if (draft != null || undoStack.Count == 0) return false;
            var c = undoStack[undoStack.Count - 1];
            undoStack.RemoveAt(undoStack.Count - 1);
            Restore(c.Before);
            redoStack.Add(c);
            return true;
        }

        public bool Redo()
        {
            if (draft != null || redoStack.Count == 0) return false;
            var c = redoStack[redoStack.Count - 1];
            redoStack.RemoveAt(redoStack.Count - 1);
            Restore(c.After);
            undoStack.Add(c);
            return true;
        }

        /// Runs op() as a self-contained command when no drag is open;
        /// inside a drag, relational validation defers to EndCommand.
        private PlacementResult Wrap(Func<PlacementResult> op)
        {
            bool own = draft == null;
            if (own) BeginCommand();
            var r = op();
            if (!own) return r;
            if (!r.Ok)
            {
                AbortCommand();
                return r;
            }
            var v = EndCommand();
            if (v != PlacementReject.None) return PlacementResult.Reject(v);
            return r;
        }

        // ---- edits ------------------------------------------------------------

        /// Paints exactly one room cell. Removing a cell under furniture is
        /// refused; connectivity is re-checked at commit.
        public PlacementResult PaintTile(int x, int y, bool present)
        {
            return Wrap(delegate
            {
                var room = Scene.Room;
                if (!room.InBounds(x, y))
                    return PlacementResult.Reject(PlacementReject.OutOfBounds);
                if (room.HasCell(x, y) == present)
                    return PlacementResult.Reject(PlacementReject.NoChange);
                if (!present && OccupyingAt(x, y) != null)
                    return PlacementResult.Reject(PlacementReject.Occupied);
                room.SetCell(x, y, present);
                return PlacementResult.Success(null, 1);
            });
        }

        /// Places furniture: doors only on outer-wall slots, machines only on
        /// a compatible host at the same cell, everything else on a free
        /// present cell. Relational checks evaluate at commit.
        public PlacementResult TryPlace(FurnitureKind kind, int x, int y, int quarterTurns)
        {
            return Wrap(delegate
            {
                var room = Scene.Room;
                if (!room.InBounds(x, y))
                    return PlacementResult.Reject(PlacementReject.OutOfBounds);
                if (!room.HasCell(x, y))
                    return PlacementResult.Reject(PlacementReject.CellMissing);
                if (kind == FurnitureKind.Door && !IsDoorSlot(x, y))
                    return PlacementResult.Reject(PlacementReject.DoorSlotInvalid);
                var at = OccupyingAt(x, y);
                int hostId = 0;
                if (RequiresHost(kind))
                {
                    if (at == null)
                        return PlacementResult.Reject(PlacementReject.MachineNeedsHost);
                    if (!CanHost(at.Kind))
                        return PlacementResult.Reject(PlacementReject.NotAHost);
                    hostId = at.Id;
                }
                else if (at != null)
                {
                    return PlacementResult.Reject(PlacementReject.Occupied);
                }
                var f = new Furniture(kind, x, y)
                {
                    Id = nextId++,
                    QuarterTurns = ((quarterTurns % 4) + 4) % 4,
                    HostId = hostId
                };
                Scene.Furniture.Add(f);
                return PlacementResult.Success(f, 0);
            });
        }

        /// Moves a host - and its whole mounted subtree - to a new cell in
        /// one atomic step. Children cannot be moved directly.
        public PlacementResult TryMove(int id, int x, int y)
        {
            return Wrap(delegate
            {
                var f = Find(id);
                if (f == null) return PlacementResult.Reject(PlacementReject.Unknown);
                if (f.HostId != 0) return PlacementResult.Reject(PlacementReject.IsChild);
                var room = Scene.Room;
                if (!room.InBounds(x, y) || !room.HasCell(x, y))
                    return PlacementResult.Reject(PlacementReject.CellMissing);
                var at = OccupyingAt(x, y);
                if (at != null && at != f)
                    return PlacementResult.Reject(PlacementReject.Occupied);
                f.CellX = x;
                f.CellY = y;
                foreach (var k in ChildrenOf(f.Id))
                {
                    k.CellX = x;
                    k.CellY = y;
                }
                return PlacementResult.Success(f, 0);
            });
        }

        /// Rotates a host subtree atomically. Rotation touches footprint and
        /// mount orientation only - the screen-up render offset never turns.
        public PlacementResult TryRotate(int id, int quarterTurns)
        {
            return Wrap(delegate
            {
                var f = Find(id);
                if (f == null) return PlacementResult.Reject(PlacementReject.Unknown);
                if (f.HostId != 0) return PlacementResult.Reject(PlacementReject.IsChild);
                int t = ((quarterTurns % 4) + 4) % 4;
                f.QuarterTurns = (f.QuarterTurns + t) % 4;
                foreach (var k in ChildrenOf(f.Id))
                {
                    k.QuarterTurns = (k.QuarterTurns + t) % 4;
                }
                return PlacementResult.Success(f, 0);
            });
        }

        /// Removes a furniture subtree atomically: removing a host also
        /// removes its mounted children; a child may be lifted off alone.
        public PlacementResult TryRemove(int id)
        {
            return Wrap(delegate
            {
                var f = Find(id);
                if (f == null) return PlacementResult.Reject(PlacementReject.Unknown);
                Scene.Furniture.Remove(f);
                if (f.HostId == 0)
                {
                    foreach (var k in ChildrenOf(f.Id)) Scene.Furniture.Remove(k);
                }
                return PlacementResult.Success(f, 0);
            });
        }

        // ---- validation --------------------------------------------------------

        /// Whole-layout validity evaluated at apply time: door slots, host
        /// relations, table<->seat pairing, anchor connectivity.
        public PlacementReject ValidateLayout()
        {
            var room = Scene.Room;
            foreach (var f in Scene.Furniture)
            {
                if (!room.InBounds(f.CellX, f.CellY) || !room.HasCell(f.CellX, f.CellY))
                    return PlacementReject.CellMissing;
                if (f.Kind == FurnitureKind.Door && !IsDoorSlot(f.CellX, f.CellY))
                    return PlacementReject.DoorSlotInvalid;
                if (RequiresHost(f.Kind))
                {
                    var host = f.HostId != 0 ? Find(f.HostId) : null;
                    if (host == null || !CanHost(host.Kind) || host.HostId != 0
                        || host.CellX != f.CellX || host.CellY != f.CellY)
                    {
                        return PlacementReject.MachineNeedsHost;
                    }
                }
                else if (f.HostId != 0)
                {
                    return PlacementReject.NotAHost;
                }
            }
            foreach (var f in Scene.Furniture)
            {
                if (f.HostId != 0) continue;
                if (f.Kind == FurnitureKind.Table && !HasAdjacent(f, IsSeatCheck))
                    return PlacementReject.TableNeedsSeat;
                if (IsSeat(f.Kind) && !HasAdjacent(f, IsTableCheck))
                    return PlacementReject.SeatNeedsTable;
            }
            return ValidateConnectivity()
                ? PlacementReject.None
                : PlacementReject.ConnectivityBroken;
        }

        private static bool IsSeatCheck(FurnitureKind k) { return IsSeat(k); }
        private static bool IsTableCheck(FurnitureKind k) { return k == FurnitureKind.Table; }

        private bool HasAdjacent(Furniture f, Predicate<FurnitureKind> match)
        {
            foreach (var g in Scene.Furniture)
            {
                if (g == f || g.HostId != 0 || !match(g.Kind)) continue;
                if (Math.Abs(g.CellX - f.CellX) + Math.Abs(g.CellY - f.CellY) == 1)
                    return true;
            }
            return false;
        }

        /// BFS 4-direction connectivity over walkable cells: every footprint
        /// must open onto the component containing the anchors (all door
        /// cells when present, else the first footprint's open edge), and
        /// every door must open into that same component.
        public bool ValidateConnectivity()
        {
            var room = Scene.Room;
            int w = room.Width;
            var blocked = new bool[w * room.Height];
            var footprints = new List<Furniture>();
            var doors = new List<Furniture>();
            foreach (var f in Scene.Furniture)
            {
                if (!room.InBounds(f.CellX, f.CellY)) continue;
                if (f.Kind == FurnitureKind.Door) { doors.Add(f); continue; }
                if (f.HostId != 0) continue;
                blocked[f.CellY * w + f.CellX] = true;
                footprints.Add(f);
            }

            var visited = new bool[w * room.Height];
            var queue = new Queue<int>();
            foreach (var d in doors)
            {
                int i = d.CellY * w + d.CellX;
                if (!visited[i] && IsWalkableCell(d.CellX, d.CellY))
                {
                    visited[i] = true;
                    queue.Enqueue(i);
                }
            }
            if (queue.Count == 0 && footprints.Count > 0)
            {
                // Doorless layouts still require a single reachable region:
                // seed from the first open edge of the first footprint.
                var f0 = footprints[0];
                for (int k = 0; k < 4; k++)
                {
                    int nx = f0.CellX + Dxs[k], ny = f0.CellY + Dys[k];
                    if (IsWalkableCell(nx, ny))
                    {
                        visited[ny * w + nx] = true;
                        queue.Enqueue(ny * w + nx);
                        break;
                    }
                }
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % w, y = i / w;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dxs[k], ny = y + Dys[k];
                    if (!IsWalkableCell(nx, ny)) continue;
                    int j = ny * w + nx;
                    if (!visited[j])
                    {
                        visited[j] = true;
                        queue.Enqueue(j);
                    }
                }
            }

            foreach (var f in footprints)
            {
                if (!TouchesVisited(f, visited)) return false;
            }
            foreach (var d in doors)
            {
                int i = d.CellY * w + d.CellX;
                if (!visited[i] || !TouchesVisited(d, visited)) return false;
            }
            return true;
        }

        private bool TouchesVisited(Furniture f, bool[] visited)
        {
            var room = Scene.Room;
            for (int k = 0; k < 4; k++)
            {
                int nx = f.CellX + Dxs[k], ny = f.CellY + Dys[k];
                if (room.InBounds(nx, ny) && visited[ny * room.Width + nx]) return true;
            }
            return false;
        }

        // ---- logical queries (the visual offset never feeds these) -------------

        /// Cells the collision system blocks - logical footprints of
        /// non-child, non-door furniture only.
        public HashSet<int> BlockedCells()
        {
            var set = new HashSet<int>();
            int w = Scene.Room.Width;
            foreach (var f in Scene.Furniture)
            {
                if (f.HostId != 0 || f.Kind == FurnitureKind.Door) continue;
                set.Add(f.CellY * w + f.CellX);
            }
            return set;
        }

        /// Walkable = present room cell with no blocking footprint on it.
        public bool IsWalkableCell(int x, int y)
        {
            if (!Scene.Room.HasCell(x, y)) return false;
            foreach (var f in Scene.Furniture)
            {
                if (f.HostId != 0 || f.Kind == FurnitureKind.Door) continue;
                if (f.CellX == x && f.CellY == y) return false;
            }
            return true;
        }

        /// BFS 4-direction path over walkable logical cells; returns cell
        /// indices (y*W+x) or null when unreachable.
        public List<int> FindPath(int sx, int sy, int tx, int ty)
        {
            var room = Scene.Room;
            int w = room.Width;
            if (!IsWalkableCell(sx, sy) || !IsWalkableCell(tx, ty)) return null;
            var prev = new int[w * room.Height];
            for (int i = 0; i < prev.Length; i++) prev[i] = -2;
            var q = new Queue<int>();
            prev[sy * w + sx] = -1;
            q.Enqueue(sy * w + sx);
            while (q.Count > 0)
            {
                int i = q.Dequeue();
                if (i == ty * w + tx) break;
                int x = i % w, y = i / w;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dxs[k], ny = y + Dys[k];
                    if (!IsWalkableCell(nx, ny)) continue;
                    int j = ny * w + nx;
                    if (prev[j] == -2)
                    {
                        prev[j] = i;
                        q.Enqueue(j);
                    }
                }
            }
            int t = ty * w + tx;
            if (prev[t] == -2) return null;
            var path = new List<int>();
            for (int i = t; i != -1; i = prev[i]) path.Add(i);
            path.Reverse();
            return path;
        }

        /// The cell seating judgement uses - the logical cell, never the
        /// draw-shifted pixel anchor.
        public int SeatJudgementCell(Furniture f)
        {
            return f.CellY * Scene.Room.Width + f.CellX;
        }

        /// Furniture ids in render order - logical depth key, hosts before
        /// their mounted children.
        public List<int> DepthSortedIds()
        {
            var list = new List<Furniture>(Scene.Furniture);
            list.Sort(delegate (Furniture a, Furniture b)
            {
                int c = a.DepthKey.CompareTo(b.DepthKey);
                return c != 0 ? c : a.HostId.CompareTo(b.HostId);
            });
            var ids = new List<int>();
            foreach (var f in list) ids.Add(f.Id);
            return ids;
        }

        // ---- save / load (logical records only) --------------------------------

        /// Deterministic serialization of logical state only: room cells and
        /// per-instance kind/cell/orientation/host records. Visual offsets are
        /// never stored - they re-resolve from the asset contract on load, so
        /// a round-trip cannot accumulate a shift.
        public string Snapshot()
        {
            var d = new Dictionary<string, object>();
            var room = Scene.Room;
            d["w"] = room.Width;
            d["h"] = room.Height;
            var cells = new List<object>();
            for (int y = 0; y < room.Height; y++)
            {
                for (int x = 0; x < room.Width; x++)
                {
                    cells.Add(room.HasCell(x, y) ? 1 : 0);
                }
            }
            d["cells"] = cells;
            var sorted = new List<Furniture>(Scene.Furniture);
            sorted.Sort(delegate (Furniture a, Furniture b) { return a.Id.CompareTo(b.Id); });
            var furn = new List<object>();
            foreach (var f in sorted)
            {
                var r = new Dictionary<string, object>();
                r["id"] = f.Id;
                r["kind"] = (int)f.Kind;
                r["x"] = f.CellX;
                r["y"] = f.CellY;
                r["q"] = f.QuarterTurns;
                r["host"] = f.HostId;
                furn.Add(r);
            }
            d["furniture"] = furn;
            d["next_id"] = nextId;
            return MiniJson.ToJson(d);
        }

        public void Restore(string snapshot)
        {
            var d = (Dictionary<string, object>)MiniJson.Parse(snapshot);
            int w = (int)(long)d["w"], h = (int)(long)d["h"];
            if (Scene.Room == null || Scene.Room.Width != w || Scene.Room.Height != h)
            {
                Scene.Room = new RoomGrid(w, h);
            }
            var cells = (List<object>)d["cells"];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Scene.Room.SetCell(x, y, (long)cells[y * w + x] != 0);
                }
            }
            Scene.Furniture.Clear();
            foreach (var o in (List<object>)d["furniture"])
            {
                var r = (Dictionary<string, object>)o;
                var f = new Furniture(
                    (FurnitureKind)(int)(long)r["kind"],
                    (int)(long)r["x"], (int)(long)r["y"])
                {
                    Id = (int)(long)r["id"],
                    QuarterTurns = (int)(long)r["q"],
                    HostId = (int)(long)r["host"]
                };
                Scene.Furniture.Add(f);
            }
            nextId = (int)(long)d["next_id"];
        }

        /// Saved instances carry logical cell + orientation (+host) only.
        public string SaveLayout() { return Snapshot(); }
        public void LoadLayout(string json) { Restore(json); }

        /// Counts room cells that differ from an earlier snapshot - the
        /// honest tile-paint measurement.
        public int CellDiffFrom(string earlierSnapshot)
        {
            var d = (Dictionary<string, object>)MiniJson.Parse(earlierSnapshot);
            var cells = (List<object>)d["cells"];
            var room = Scene.Room;
            int n = 0;
            for (int y = 0; y < room.Height; y++)
            {
                for (int x = 0; x < room.Width; x++)
                {
                    if (room.HasCell(x, y) != ((long)cells[y * room.Width + x] != 0)) n++;
                }
            }
            return n;
        }

        /// Probe does real edit work on a throwaway room - a paint+undo
        /// round-trip and a real door placement - so the live scene is never
        /// mutated by a health check.
        protected override bool OnProbe()
        {
            var probe = new LayoutModule(new GameScene { Room = new RoomGrid(4, 4) });
            string before = probe.Snapshot();
            var p = probe.PaintTile(1, 1, false);
            if (!p.Ok || p.ChangedCells != 1) return false;
            if (!probe.Undo() || probe.Snapshot() != before) return false;
            return probe.TryPlace(FurnitureKind.Door, 0, 0, 0).Ok
                && probe.ValidateLayout() == PlacementReject.None;
        }
    }
}
