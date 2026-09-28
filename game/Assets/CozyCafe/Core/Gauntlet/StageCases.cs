using System;
using System.Collections.Generic;
using System.IO;
using CozyCafe.Core.Character;
using CozyCafe.Core.Economy;
using CozyCafe.Core.Iso;
using CozyCafe.Core.Layout;
using CozyCafe.Core.Research;
using CozyCafe.Core.Save;
using CozyCafe.Core.Scene;
using CozyCafe.Core.Staff;

namespace CozyCafe.Core.Gauntlet
{
    public sealed class CaseResult
    {
        public readonly string Key;
        public readonly object Value;

        public CaseResult(string key, object value)
        {
            Key = key;
            Value = value;
        }
    }

    /// <summary>
    /// Stage -> CASE dispatcher shared by both hosts. Every value is computed
    /// by calling real core modules; a stage with no implementation returns
    /// null so the host reports NOT_IMPLEMENTED instead of fabricating lines.
    /// </summary>
    public static class StageCases
    {
        public static List<CaseResult> Run(string stage)
        {
            switch (stage)
            {
                case "project-boot":
                    return ProjectBoot();
                case "iso-grid":
                    return IsoGrid();
                case "idle-economy":
                    return IdleEconomy();
                case "research-staff":
                    return ResearchStaff();
                case "layout-editor":
                    return LayoutEditorStage();
                case "save-offline":
                    return SaveOffline();
                case "character-rig":
                    return CharacterRig();
                default:
                    return null;
            }
        }

        private static List<CaseResult> ProjectBoot()
        {
            var boot = GameBootstrap.Create();
            bool sceneLoaded = boot.LoadDefaultScene()
                && boot.Scene.IsLoaded
                && boot.Scene.Validate();

            bool modulesOk = boot.Registry.ProbeAll();
            bool allInvoked = modulesOk
                && boot.Registry.Count > 0
                && boot.Registry.InvokedCount == boot.Registry.Count;

            var cases = new List<CaseResult>();
            cases.Add(new CaseResult("game_scene_loaded", sceneLoaded));
            cases.Add(new CaseResult("test_entry_calls_real_modules", allInvoked));
            return cases;
        }

        /// v0.8 iso-grid contract: every value is measured or evaluated from
        /// the same core code paths the renderer uses (rasterized diamonds,
        /// coverage audit, emission predicates), never a declared constant
        /// copied into a CASE line.
        private static List<CaseResult> IsoGrid()
        {
            int topW, topH, canvasW, canvasH, seams, overlaps;
            IsoContract.MeasureTopFace(out topW, out topH);
            IsoContract.MeasureTileCanvas(out canvasW, out canvasH);
            IsoContract.MeasureFloorSeams(4, out seams, out overlaps);
            int interior = IsoContract.CountInteriorSideFaces(new RoomGrid(4, 4));
            double roundtrip = IsoContract.RoundtripMaxError();

            var cases = new List<CaseResult>();
            cases.Add(new CaseResult("tile_top_width", topW));
            cases.Add(new CaseResult("tile_top_height", topH));
            cases.Add(new CaseResult("tile_space_width", IsoMath.TileSpaceWidthPx));
            cases.Add(new CaseResult("tile_space_height", IsoMath.TileSpaceHeightPx));
            cases.Add(new CaseResult("tile_canvas_width", canvasW));
            cases.Add(new CaseResult("tile_canvas_height", canvasH));
            cases.Add(new CaseResult("tile_visual_thickness_px", IsoMath.VisualThicknessPx));
            cases.Add(new CaseResult("tile_physical_thickness", IsoMath.PhysicalThicknessPx));
            cases.Add(new CaseResult("thickness_changes_projection",
                IsoContract.ThicknessChangesProjection()));
            cases.Add(new CaseResult("space_height_used_as_grid_pitch",
                IsoContract.SpaceHeightUsedAsGridPitch()));
            cases.Add(new CaseResult("grid_step_x", IsoMath.StepX));
            cases.Add(new CaseResult("grid_step_y", IsoMath.StepY));
            cases.Add(new CaseResult("interior_side_faces", interior));
            cases.Add(new CaseResult("roundtrip_error", roundtrip));
            cases.Add(new CaseResult("tile_seam_or_overlap_pixels", seams + overlaps));
            cases.Add(new CaseResult("fixed_sizes_and_default_pitch_distinguished",
                IsoContract.FixedSizesAndPitchDistinguished()));
            return cases;
        }

        /// Idle-sale economy against data/mvp.json: per-menu independent
        /// revenue, milestone-scaled rates, the 112/100 upgrade curve, a
        /// non-negative wallet, and once-per-event NPC payment. Every value
        /// below is computed from the loaded data file by real module calls.
        private static List<CaseResult> IdleEconomy()
        {
            var data = MvpData.Load();
            var espresso = data.FindMenu("M01");
            if (espresso == null)
            {
                throw new InvalidOperationException("mvp.json has no menu M01");
            }

            // Startup state, 0% staff bonus: total coins/min over owned menus.
            var econ = EconomyModule.CreateStartup(data, 0);
            var baseRate = econ.TotalRatePerMinute();

            // A real 25-minute idle run on a fresh startup state.
            var run = EconomyModule.CreateStartup(data, 0);
            run.SimulateSeconds(25.0 * 60.0);

            // Espresso at the Lv10 milestone with a +5% staff sales bonus.
            var lv10 = econ.RatePerMinute(espresso, 10, 5);

            // Wallet floor: every illegal charge must fail without moving coins.
            var wallet = EconomyModule.CreateStartup(data, 0);
            wallet.SimulateSeconds(60.0);
            long have = wallet.Coins;
            bool overchargeOk = wallet.TryCharge(have + 1);
            long afterRefusal = wallet.Coins;
            bool drainOk = wallet.TryCharge(have);
            bool underOk = wallet.TryCharge(1);
            bool negativeArg = wallet.TryCharge(-10);
            bool negativeWallet = wallet.WalletWentNegative
                || wallet.Coins < 0
                || afterRefusal != have
                || overchargeOk
                || !drainOk
                || underOk
                || negativeArg;

            // NPC payment: one sale event may credit coins exactly once.
            var pay = EconomyModule.CreateStartup(data, 0);
            string ev = pay.IssueSaleEventId(espresso.Id);
            bool credited = pay.SettleSale(ev, espresso, 1);
            long afterFirst = pay.Coins;
            bool duplicateOk = pay.SettleSale(ev, espresso, 1);
            bool paysAgain = !credited || duplicateOk || pay.Coins != afterFirst;

            var cases = new List<CaseResult>();
            cases.Add(new CaseResult("base_rate", JsonNumber(baseRate)));
            cases.Add(new CaseResult("coins_25m", run.Coins));
            cases.Add(new CaseResult("espresso_lv10_bonus5_rate", JsonNumber(lv10)));
            cases.Add(new CaseResult("cost_lv1", econ.UpgradeCost(espresso, 1)));
            cases.Add(new CaseResult("cost_lv9", econ.UpgradeCost(espresso, 9)));
            cases.Add(new CaseResult("cost_lv10", econ.UpgradeCost(espresso, 10)));
            cases.Add(new CaseResult("negative_wallet", negativeWallet));
            cases.Add(new CaseResult("npc_pays_coins_again", paysAgain));
            return cases;
        }

        /// <summary>
        /// Research lab + hire office against data/mvp.json: the 5 machines /
        /// 8 menus / 5 research counts come straight from the loaded data
        /// file; menu availability is AND-gated by owned machines plus
        /// completed research (ice+milk opens M04, a missing steam machine
        /// keeps M05 closed); milk/ice/chocolate resolve to permanent
        /// research flags, never an inventory item; and the hire office
        /// persists its three candidates across panel opens while each
        /// candidate's stats and appearance roll on independent seeds.
        /// </summary>
        private static List<CaseResult> ResearchStaff()
        {
            var data = MvpData.Load();
            var m04 = data.FindMenu("M04");
            var m05 = data.FindMenu("M05");
            if (m04 == null || m05 == null)
            {
                throw new InvalidOperationException("mvp.json missing menu M04/M05");
            }

            // Each scenario is a fresh startup economy funded through the
            // real idle-sale path with its own lab instance, so the AND-gate
            // checks never share state.
            var full = RunResearchScenario(data, 250.0 * 60.0, "R01", "R02", "R04");
            var both = RunResearchScenario(data, 60.0 * 60.0, "R01", "R02");
            var onlyIce = RunResearchScenario(data, 10.0 * 60.0, "R01");
            var onlyMilk = RunResearchScenario(data, 60.0 * 60.0, "R02");
            var steamMilk = RunResearchScenario(data, 90.0 * 60.0, "R02", "R03");

            // milk/ice/chocolate must land on permanent research flags and
            // machine ownership — the consumable item store stays empty.
            bool milkIsInventory =
                full.Research.InventoryItems.Contains("milk")
                || full.Research.InventoryItems.Contains("ice")
                || full.Research.InventoryItems.Contains("chocolate")
                || !full.Econ.OwnedMachines.Contains("ice")
                || !full.Research.UnlockedFlags.Contains("milk")
                || !full.Research.UnlockedFlags.Contains("chocolate");

            // AND-gate both directions: ice alone and milk alone each keep
            // M04 closed; only ice+milk together open it.
            bool iceMilkUnlocksM04 =
                onlyIce.Econ.OwnedMachines.Contains("ice")
                && !onlyIce.Econ.Owns(m04)
                && !onlyMilk.Econ.OwnedMachines.Contains("ice")
                && !onlyMilk.Econ.Owns(m04)
                && both.Econ.OwnedMachines.Contains("ice")
                && both.Econ.CompletedResearch.Contains("R02")
                && both.Econ.Owns(m04);

            // M05 needs steam+R02: without the steam machine the menu stays
            // closed even with milk done; adding steam opens it.
            bool missingSteamBlocksM05 =
                !onlyMilk.Econ.Owns(m05)
                && steamMilk.Econ.OwnedMachines.Contains("steam")
                && steamMilk.Econ.Owns(m05);

            // The hire panel persists its candidates: reopening returns the
            // identical offers rather than re-rolling.
            var staff = new StaffModule(EconomyModule.CreateStartup(data, 0), 20260927);
            var open1 = new List<string>();
            foreach (var c in staff.OpenHirePanel()) open1.Add(c.Serialize());
            staff.CloseHirePanel();
            var open2 = new List<string>();
            foreach (var c in staff.OpenHirePanel()) open2.Add(c.Serialize());
            bool reopenRerolls = open1.Count != open2.Count;
            for (int i = 0; !reopenRerolls && i < open1.Count; i++)
            {
                if (open1[i] != open2[i]) reopenRerolls = true;
            }

            // Independent seeds: the stat stream alone decides stats and the
            // appearance stream alone decides the look.
            var candA = staff.RollCandidate(1111, 5);
            var candB = staff.RollCandidate(2222, 5);
            var candC = staff.RollCandidate(1111, 6);
            bool statAppearanceIndependent =
                candA.StatKey == candB.StatKey
                && candA.AppearanceKey == candC.AppearanceKey
                && candA.SalesBonusPct >= data.StaffStatPctMin
                && candA.SalesBonusPct <= data.StaffStatPctMax;

            var cases = new List<CaseResult>();
            cases.Add(new CaseResult("machines", data.Machines.Count));
            cases.Add(new CaseResult("menus", data.Menus.Count));
            cases.Add(new CaseResult("research", data.Research.Count));
            cases.Add(new CaseResult("milk_is_inventory", milkIsInventory));
            cases.Add(new CaseResult("ice_milk_unlocks_M04", iceMilkUnlocksM04));
            cases.Add(new CaseResult("missing_steam_blocks_M05", missingSteamBlocksM05));
            cases.Add(new CaseResult("reopen_rerolls_staff", reopenRerolls));
            cases.Add(new CaseResult("stat_appearance_independent", statAppearanceIndependent));
            return cases;
        }

        /// <summary>
        /// Layout/furniture editor gates. Every value below is computed by a
        /// real LayoutModule run on a real room - 1x1 tile paint, grouped
        /// undo/redo, door/pair/host validity, atomic host-subtree ops, and
        /// the v0.8 single (0,-8) screen-up render offset resolved through
        /// RenderContract while logical state stays unshifted.
        /// </summary>
        private static List<CaseResult> LayoutEditorStage()
        {
            var cases = new List<CaseResult>();

            // --- 1x1 tile paint changes exactly one cell ---
            var paint = new LayoutModule(new GameScene { Room = new RoomGrid(6, 6) });
            string paintBefore = paint.Snapshot();
            var pr = paint.PaintTile(4, 4, false);
            int cellDiff = paint.CellDiffFrom(paintBefore);
            cases.Add(new CaseResult("one_tile_changed",
                pr.Ok && pr.ChangedCells == 1 && cellDiff == 1 ? 1 : cellDiff));

            // --- grouped undo/redo: one 3-cell drag is one command ---
            var drag = new LayoutModule(new GameScene { Room = new RoomGrid(6, 6) });
            string d0 = drag.Snapshot();
            drag.BeginCommand();
            drag.PaintTile(0, 0, false);
            drag.PaintTile(1, 0, false);
            drag.PaintTile(2, 0, false);
            var dragCommit = drag.EndCommand();
            string d1 = drag.Snapshot();
            bool undoOk = dragCommit == PlacementReject.None && d0 != d1
                && drag.UndoDepth == 1 && drag.Undo() && drag.Snapshot() == d0;
            bool redoOk = drag.Redo() && drag.Snapshot() == d1;
            cases.Add(new CaseResult("undo_restores", undoOk));
            cases.Add(new CaseResult("redo_restores", redoOk));

            // --- door rules: wall slots only, never sealed off ---
            var door = new LayoutModule(new GameScene { Room = new RoomGrid(5, 5) });
            bool interiorRejected = !door.TryPlace(FurnitureKind.Door, 2, 2, 0).Ok;
            bool wallOk = door.TryPlace(FurnitureKind.Door, 0, 1, 0).Ok;
            door.TryPlace(FurnitureKind.Counter, 0, 0, 0);
            door.TryPlace(FurnitureKind.Counter, 0, 2, 0);
            bool sealRejected = !door.TryPlace(FurnitureKind.Counter, 1, 1, 0).Ok;
            cases.Add(new CaseResult("door_block_rejected",
                interiorRejected && wallOk && sealRejected));

            // --- table<->seat pairing ---
            var pair = new LayoutModule(new GameScene { Room = new RoomGrid(5, 5) });
            bool loneTable = !pair.TryPlace(FurnitureKind.Table, 2, 2, 0).Ok;
            pair.BeginCommand();
            bool pairOps = pair.TryPlace(FurnitureKind.Table, 2, 2, 0).Ok
                && pair.TryPlace(FurnitureKind.Chair, 2, 3, 0).Ok;
            bool pairedOk = pairOps && pair.EndCommand() == PlacementReject.None;
            cases.Add(new CaseResult("table_without_chair_rejected", loneTable && pairedOk));

            // --- machine/host relations and atomic subtree move ---
            var host = new LayoutModule(new GameScene { Room = new RoomGrid(6, 6) });
            bool noHost = !host.TryPlace(FurnitureKind.Grinder, 2, 2, 0).Ok;
            var counter = host.TryPlace(FurnitureKind.Counter, 2, 2, 0);
            var grinder = host.TryPlace(FurnitureKind.Grinder, 2, 2, 0);
            bool hosted = counter.Ok && grinder.Ok
                && grinder.Placed.HostId == counter.Placed.Id;
            cases.Add(new CaseResult("unhosted_machine_rejected", noHost && hosted));

            var mv = host.TryMove(counter.Placed.Id, 3, 2);
            bool preserved = mv.Ok && grinder.Placed.CellX == 3
                && grinder.Placed.CellY == 2
                && grinder.Placed.HostId == counter.Placed.Id
                && host.ChildrenOf(counter.Placed.Id).Count == 1;
            cases.Add(new CaseResult("host_move_preserves_child", preserved));

            // --- editing never pauses the idle-sale loop ---
            var data = MvpData.Load();
            var steady = EconomyModule.CreateStartup(data, 0);
            steady.SimulateSeconds(60.0);
            var edited = EconomyModule.CreateStartup(data, 0);
            edited.SimulateSeconds(30.0);
            var midEdit = new LayoutModule(new GameScene { Room = new RoomGrid(6, 6) });
            midEdit.BeginCommand();
            midEdit.TryPlace(FurnitureKind.Table, 3, 3, 0);
            midEdit.TryPlace(FurnitureKind.Chair, 3, 4, 0);
            var editCommit = midEdit.EndCommand();
            edited.SimulateSeconds(30.0);
            cases.Add(new CaseResult("editing_keeps_sales",
                editCommit == PlacementReject.None
                && edited.Coins == steady.Coins && edited.Coins > 0));

            // --- the v0.8 single (0,-8) screen-up render offset ---
            var room = new LayoutModule(new GameScene { Room = new RoomGrid(6, 6) });
            room.BeginCommand();
            room.TryPlace(FurnitureKind.Door, 0, 3, 0);
            var table = room.TryPlace(FurnitureKind.Table, 3, 2, 0).Placed;
            var chair = room.TryPlace(FurnitureKind.Chair, 3, 3, 0).Placed;
            room.TryPlace(FurnitureKind.Stool, 2, 2, 0);
            var roomCommit = room.EndCommand();

            double ox, oy;
            RenderContract.TargetOffset(FurnitureKind.Table, out ox, out oy);
            cases.Add(new CaseResult("table_render_offset_y_px", oy));
            RenderContract.TargetOffset(FurnitureKind.Chair, out ox, out oy);
            cases.Add(new CaseResult("chair_render_offset_y_px", oy));
            RenderContract.RuntimeOffset(FurnitureKind.Table, 0.0, -8.0, out ox, out oy);
            cases.Add(new CaseResult("table_runtime_offset_when_baked_y_px", oy));
            RenderContract.EffectiveOffset(FurnitureKind.Table, 0.0, -8.0, out ox, out oy);
            cases.Add(new CaseResult("table_effective_offset_when_baked_y_px", oy));

            double gx, gy, dx, dy;
            IsoMath.Project(table.CellX + 0.5, table.CellY + 0.5, out gx, out gy);
            RenderContract.DrawAnchor(table, 2.0, out dx, out dy);
            cases.Add(new CaseResult("table_offset_at_zoom2_y_px", dy - gy * 2.0));

            // The -8 is always screen-up: resolving the draw transform at
            // every quarter turn must yield the identical (0,-8) delta.
            double cgx, cgy;
            IsoMath.Project(chair.CellX + 0.5, chair.CellY + 0.5, out cgx, out cgy);
            bool rotates = false;
            for (int q = 0; q < 4; q++)
            {
                chair.QuarterTurns = q;
                double ddx, ddy;
                RenderContract.DrawAnchor(chair, 1.0, out ddx, out ddy);
                if (ddy - cgy != -8.0 || ddx - cgx != 0.0) rotates = true;
            }
            cases.Add(new CaseResult("chair_offset_rotates_with_furniture", rotates));

            // --- the offset must never leak into logical geometry ---
            // Prove the -8 actually moves draw anchors, then prove every
            // logical structure is byte-identical after the render path runs.
            double tgx, tgy, tdx, tdy;
            IsoMath.Project(table.CellX + 0.5, table.CellY + 0.5, out tgx, out tgy);
            RenderContract.DrawAnchor(table, 1.0, out tdx, out tdy);
            bool offsetApplied = tdy == tgy - 8.0 && tdx == tgx;

            string logBefore = room.Snapshot();
            var blockedBefore = room.BlockedCells();
            var pathBefore = room.FindPath(0, 3, 5, 5);
            var depthBefore = room.DepthSortedIds();
            int seatBefore = room.SeatJudgementCell(chair);
            foreach (var f in room.Scene.Furniture)
            {
                double ax, ay;
                RenderContract.DrawAnchorResolved(f, room.LookupHost, 2.0, out ax, out ay);
            }
            bool sameSnapshot = room.Snapshot() == logBefore;
            bool sameBlocked = room.BlockedCells().SetEquals(blockedBefore);
            var pathAfter = room.FindPath(0, 3, 5, 5);
            bool samePath = SameInts(pathBefore, pathAfter) && pathBefore != null;
            var depthAfter = room.DepthSortedIds();
            bool sameDepth = SameInts(depthBefore, depthAfter);
            bool depthIsLogical = true;
            foreach (var f in room.Scene.Furniture)
            {
                if (f.DepthKey != f.CellX + f.CellY) depthIsLogical = false;
            }
            bool seatIsLogical = seatBefore == chair.CellY * 6 + chair.CellX
                && room.SeatJudgementCell(chair) == seatBefore;

            cases.Add(new CaseResult("visual_offset_changes_logical_cell",
                !(offsetApplied && roomCommit == PlacementReject.None
                    && sameSnapshot && seatIsLogical)));
            cases.Add(new CaseResult("visual_offset_changes_collision",
                !(offsetApplied && sameBlocked)));
            cases.Add(new CaseResult("visual_offset_changes_pathfinding",
                !(offsetApplied && samePath)));
            cases.Add(new CaseResult("visual_offset_changes_depth_key",
                !(offsetApplied && sameDepth && depthIsLogical)));

            // --- save/reload: logical records only, zero accumulation ---
            string saved = room.SaveLayout();
            var re1 = new LayoutModule(new GameScene { Room = new RoomGrid(6, 6) });
            re1.LoadLayout(saved);
            var re2 = new LayoutModule(new GameScene { Room = new RoomGrid(6, 6) });
            re2.LoadLayout(re1.SaveLayout());
            bool accumulates = re1.SaveLayout() != saved;
            foreach (var f in re2.Scene.Furniture)
            {
                var orig = room.Find(f.Id);
                double ex, ey;
                RenderContract.EffectiveOffset(f, out ex, out ey);
                double tox, toy;
                RenderContract.TargetOffset(f.Kind, out tox, out toy);
                if (orig == null || f.CellX != orig.CellX || f.CellY != orig.CellY
                    || ex != tox || ey != toy)
                {
                    accumulates = true;
                }
            }
            cases.Add(new CaseResult("reload_accumulates_visual_offset", accumulates));

            // --- mounted child resolves the parent mount once ---
            var hostEd = new LayoutModule(new GameScene { Room = new RoomGrid(6, 6) });
            hostEd.BeginCommand();
            var hostTable = hostEd.TryPlace(FurnitureKind.Table, 3, 3, 0).Placed;
            hostEd.TryPlace(FurnitureKind.Chair, 3, 4, 0);
            hostEd.EndCommand();
            var childRes = hostEd.TryPlace(FurnitureKind.Grinder, 3, 3, 0);
            bool duplicates = true;
            if (childRes.Ok && hostTable != null)
            {
                var child = childRes.Placed;
                double cdx, cdy, hgx, hgy, mx, my, hox, hoy, chx, chy;
                RenderContract.DrawAnchorResolved(child, hostEd.LookupHost, 1.0,
                    out cdx, out cdy);
                IsoMath.Project(child.CellX + 0.5, child.CellY + 0.5, out hgx, out hgy);
                RenderContract.TargetOffset(hostTable.Kind, out hox, out hoy);
                RenderContract.MountLocal(hostTable.Kind, out mx, out my);
                RenderContract.TargetOffset(child.Kind, out chx, out chy);
                // parent(-8) + mount + child each appear exactly once.
                duplicates = cdy != hgy + hoy + my + chy
                    || cdx != hgx + hox + mx + chx;
            }
            cases.Add(new CaseResult("child_attachment_duplicates_parent_offset",
                duplicates));

            // --- standing agents are never lifted by the table rule ---
            var agent = new Agent { GridX = 3.5, GridY = 3.5, IsStaff = false };
            double adx, ady, agx, agy;
            RenderContract.AgentDrawAnchor(agent, 1.0, out adx, out ady);
            IsoMath.Project(agent.GridX, agent.GridY, out agx, out agy);
            cases.Add(new CaseResult("standing_characters_lifted_by_table_rule",
                adx != agx || ady != agy));

            // --- picking/ghost share the real render transform ---
            double ghx, ghy, pdx, pdy;
            RenderContract.GhostAnchor(FurnitureKind.Table,
                table.CellX, table.CellY, 1.0, out ghx, out ghy);
            RenderContract.PickAnchor(table, 1.0, out pdx, out pdy);
            cases.Add(new CaseResult("picking_and_ghost_share_render_transform",
                ghx == pdx && ghy == pdy));

            return cases;
        }

        /// <summary>
        /// Save + offline settlement. Every value below is produced by real
        /// SaveStore file I/O, a real document parse/migration and real
        /// session simulation through the event-boundary Advance path:
        /// the online timeline is driven continuously while the offline
        /// one is written to disk at T0, read back, restored into a fresh
        /// session and settled once — then the whole timeline state is
        /// compared field by field. Backup failover, torn-write recovery,
        /// v1 migration, the 24h cap, reversal clamp, settlement-id dedupe,
        /// byte-equal layout restore and the private-field-free public
        /// preset are all exercised on the same real paths.
        /// </summary>
        private static List<CaseResult> SaveOffline()
        {
            var cases = new List<CaseResult>();
            var data = MvpData.Load();
            string tmpDir = Path.Combine(Directory.GetCurrentDirectory(),
                "out", "save-offline-tmp");
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
            Directory.CreateDirectory(tmpDir);
            const long T0 = 1700000000L;
            try
            {
                // ---- offline settle == continuous play ------------------
                const long window = 3600;
                const long preSave = 500;
                var online = CafeSession.CreateStartup(data, 777, 6, 6);
                online.Staff.OpenHirePanel();
                online.Hire(0);              // staff-change boundary
                online.Lab.Reserve("R01");
                online.Lab.Reserve("R02");
                online.Advance(preSave);     // R01 running by now
                online.Lab.SetWorkRate(1.25); // mid-run speed change
                for (long t = preSave; t < window; t += 150)
                {
                    online.Advance(Math.Min(150, window - t));
                }

                var off = CafeSession.CreateStartup(data, 777, 6, 6);
                off.Staff.OpenHirePanel();
                off.Hire(0);
                off.Lab.Reserve("R01");
                off.Lab.Reserve("R02");
                off.Advance(preSave);
                off.Lab.SetWorkRate(1.25);
                var storeA = new SaveStore(Path.Combine(tmpDir, "a", "save.json"),
                    SaveDocument.IsValidJson);
                off.WriteCheckpoint(storeA, T0);

                var back = CafeSession.CreateStartup(data, 424242, 5, 4);
                string textA;
                var srcA = storeA.TryRead(out textA);
                var docRead = SaveDocument.Parse(textA);
                back.Restore(docRead);
                var res = back.SettleOffline(docRead, T0 + (window - preSave));
                long diff = back.StateDifference(online);
                if (srcA != SaveStore.LoadSource.Primary
                    || res.Seconds != window - preSave)
                {
                    diff++;
                }
                cases.Add(new CaseResult("offline_online_difference", diff));

                // ---- research cost charged exactly once -----------------
                // The R01 reservation charges 300 when the wallet crosses
                // the funds threshold mid-run; a checkpoint taken while it
                // is still active must not re-bill on restore+settle.
                var s1 = CafeSession.CreateStartup(data, 555, 6, 6);
                s1.Lab.Reserve("R01");
                s1.Advance(500);             // charged already, still running
                var storeB = new SaveStore(Path.Combine(tmpDir, "b", "save.json"),
                    SaveDocument.IsValidJson);
                var docB = s1.WriteCheckpoint(storeB, T0);
                var s2 = CafeSession.CreateStartup(data, 9, 6, 6);
                string textB;
                storeB.TryRead(out textB);
                s2.Restore(SaveDocument.Parse(textB));
                var resB = s2.SettleOffline(docB, T0 + 700);
                bool chargedOnce = resB.Seconds == 700
                    && s2.Econ.TotalCharged == 300
                    && s2.Econ.CompletedResearch.Contains("R01");
                cases.Add(new CaseResult("research_cost_charged_once", chargedOnce));

                // ---- time reversal yields zero reward -------------------
                var s3 = CafeSession.CreateStartup(data, 31, 6, 6);
                s3.Restore(SaveDocument.Parse(textA));
                var back3 = s3.SettleOffline(docRead, T0 - 500);
                long rewardBack = back3.CoinsGained * 100 + back3.RemainderGained;
                cases.Add(new CaseResult("time_backwards_reward", rewardBack));

                // ---- byte-equal layout restore through the file ---------
                var sL = CafeSession.CreateStartup(data, 42, 6, 5);
                sL.Layout.TryPlace(FurnitureKind.Door, 0, 2, 0);
                sL.Layout.BeginCommand();
                var tb = sL.Layout.TryPlace(FurnitureKind.Table, 2, 1, 0);
                sL.Layout.TryPlace(FurnitureKind.Chair, 1, 1, 0);
                var commit1 = sL.Layout.EndCommand();
                sL.Layout.BeginCommand();
                var ct = sL.Layout.TryPlace(FurnitureKind.Counter, 4, 3, 0);
                var mg = sL.Layout.TryPlace(FurnitureKind.Grinder, 4, 3, 0);
                var commit2 = sL.Layout.EndCommand();
                string layoutSnap = sL.Layout.SaveLayout();
                var storeL = new SaveStore(Path.Combine(tmpDir, "l", "save.json"),
                    SaveDocument.IsValidJson);
                sL.WriteCheckpoint(storeL, T0);
                var sL2 = CafeSession.CreateStartup(data, 4242, 3, 3);
                string textL;
                storeL.TryRead(out textL);
                sL2.Restore(SaveDocument.Parse(textL));
                cases.Add(new CaseResult("restored_layout_equal",
                    commit1 == PlacementReject.None
                    && commit2 == PlacementReject.None
                    && tb.Ok && ct.Ok && mg.Ok
                    && mg.Placed.HostId == ct.Placed.Id
                    && sL2.Layout.SaveLayout() == layoutSnap));

                // ---- settlement-id dedupe -------------------------------
                // Re-applying the same checkpoint document credits zero.
                var dup = back.SettleOffline(docRead, T0 + window);
                long dupGain = dup.CoinsGained * 100 + dup.RemainderGained;
                if (!dup.Duplicate) dupGain++;
                cases.Add(new CaseResult("duplicate_credit", dupGain));

                // ---- recovery: malformed primary -> backup --------------
                var storeF = new SaveStore(Path.Combine(tmpDir, "f", "save.json"),
                    SaveDocument.IsValidJson);
                var s5 = CafeSession.CreateStartup(data, 77, 6, 6);
                s5.Lab.Reserve("R01");
                s5.Advance(450);             // R01 charged ~420, still active
                var docF1 = s5.WriteCheckpoint(storeF, T0);
                s5.Advance(300);             // R01 completes ~540
                s5.WriteCheckpoint(storeF, T0 + 300);   // primary=v2, backup=v1
                long coinsF1 = SaveDoc.Long(SaveDoc.Get(docF1.Economy, "coins"));

                File.WriteAllText(storeF.PrimaryPath,
                    "{ \"save_version\": 2, broken,,,");
                string rt;
                var lsrc = storeF.TryRead(out rt);
                var s6 = CafeSession.CreateStartup(data, 1, 6, 6);
                s6.Restore(SaveDocument.Parse(rt));
                bool badJsonRecovered = lsrc == SaveStore.LoadSource.Backup
                    && s6.Econ.Coins == coinsF1
                    && s6.Lab.Active != null && s6.Lab.Active.Id == "R01"
                    && !s6.Econ.CompletedResearch.Contains("R01");

                // power loss mid-write: a torn primary plus a stale .tmp
                // leftover are never candidates — backup still recovers.
                string full2 = File.ReadAllText(storeF.BackupPath);
                File.WriteAllText(storeF.TempPath, full2.Substring(0, 40));
                File.WriteAllText(storeF.PrimaryPath,
                    full2.Substring(0, full2.Length / 2));
                var lsrc2 = storeF.TryRead(out rt);
                var s7 = CafeSession.CreateStartup(data, 1, 6, 6);
                s7.Restore(SaveDocument.Parse(rt));
                bool powerLossRecovered = lsrc2 == SaveStore.LoadSource.Backup
                    && s7.Econ.Coins == coinsF1
                    && s7.Lab.Active != null && s7.Lab.Active.Id == "R01";

                // both copies unreadable -> None, no silent state.
                File.WriteAllText(storeF.BackupPath, "also junk");
                bool noneFound = storeF.TryRead(out rt) == SaveStore.LoadSource.None;

                // ---- version migration: real v1-shaped save -------------
                var migStore = new SaveStore(Path.Combine(tmpDir, "m", "save.json"),
                    SaveDocument.IsValidJson);
                var v1 = new Dictionary<string, object>();
                v1["version"] = (long)1;
                v1["utc_saved"] = T0;
                v1["coins"] = (long)1234;
                v1["settled_seq"] = (long)41;
                v1["staff_seed"] = (long)77;
                v1["machines_owned"] = new List<object> { "grinder", "espresso", "ice" };
                v1["research_done"] = new List<object> { "R01" };
                var lv = new Dictionary<string, object>();
                lv["M01"] = (long)3;
                lv["M02"] = (long)2;
                lv["M03"] = (long)1;
                v1["menu_levels"] = lv;
                v1["layout_json"] = layoutSnap;
                Directory.CreateDirectory(Path.GetDirectoryName(migStore.PrimaryPath));
                File.WriteAllText(migStore.PrimaryPath, MiniJson.ToJson(v1));
                var msrc = migStore.TryRead(out rt);
                var mdoc = SaveDocument.Parse(rt);
                var s8 = CafeSession.CreateStartup(data, 5, 4, 4);
                s8.Restore(mdoc);
                bool migrated = msrc == SaveStore.LoadSource.Primary
                    && s8.Econ.Coins == 1234
                    && s8.Econ.OwnedMachines.Contains("ice")
                    && s8.Econ.CompletedResearch.Contains("R01")
                    && s8.Econ.Lines["M01"].Level == 3
                    && s8.Layout.SaveLayout() == layoutSnap;

                // ---- remaining_base_work re-billed by the last rate ------
                // Rate change mid-run: the elapsed segment drains at the old
                // rate and only the remainder is re-billed by the new one.
                var sR = CafeSession.CreateStartup(data, 88, 6, 6);
                sR.Lab.Reserve("R01");
                sR.Advance(500);                  // R01 active since 420
                sR.Lab.SetWorkRate(2.0);          // rate-change boundary
                double remAfter = sR.Lab.ActiveRemainingWork; // 120-80=40 left
                double expectedEnd = sR.Lab.Clock + remAfter / 2.0;
                bool scheduled = sR.Lab.ActiveEndsAt == expectedEnd
                    && remAfter == 40.0;
                sR.Advance(200);
                bool rebills = scheduled
                    && sR.Econ.CompletedResearch.Contains("R01")
                    && sR.Lab.Active == null
                    && sR.Lab.CompletedCount == 1
                    && sR.Lab.Clock == 700;

                // ---- 24h cap + private-field-free preset ----------------
                var s9 = CafeSession.CreateStartup(data, 3, 6, 6);
                s9.Restore(SaveDocument.Parse(textA));
                var capRes = s9.SettleOffline(docRead, T0 + 90000);
                bool capOk = capRes.Seconds == data.OfflineCapSeconds;

                sL.Memos.Add("private memo: vault code 1234");
                sL.Todos.Add("call the supplier");
                var preset = sL.ExportPublicPreset("my-cafe");
                bool privateLeak = CafeSession.PresetContainsPrivateFields(preset);

                cases.Add(new CaseResult("save_backup_recovers",
                    badJsonRecovered && powerLossRecovered
                    && noneFound && migrated));
                cases.Add(new CaseResult("save_version_migrates", migrated));
                cases.Add(new CaseResult("malformed_json_uses_backup", badJsonRecovered));
                cases.Add(new CaseResult("power_loss_recovers", powerLossRecovered));
                cases.Add(new CaseResult("research_rate_rebills", rebills));
                cases.Add(new CaseResult("offline_cap_enforced", capOk));
                cases.Add(new CaseResult("private_fields_in_preset", privateLeak));
            }
            finally
            {
                try { Directory.Delete(tmpDir, true); }
                catch (Exception) { }
            }
            return cases;
        }

        private static bool SameInts(List<int> a, List<int> b)
        {
            if (a == null || b == null || a.Count != b.Count) return a == b;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// Shared character rig against data/art_contract.json and
        /// data/character_presets.json: the rig and preset counts are read
        /// from the loaded files; palette swaps are verified on real
        /// composited pixels (alpha + coverage byte-compared); phase sync is
        /// measured on every layer's recorded anchor/phase and on painted
        /// bbox motion between walk frames; rear occlusion is measured on
        /// the composited raster; and customer collection is measured by
        /// spawning, despawning and scanning the real persistent snapshot.
        /// </summary>
        private static List<CaseResult> CharacterRig()
        {
            var module = new CharacterModule();
            var data = module.Data;

            // --- palette geometry invariance on real composited pixels ---
            bool paletteMoved = false;
            int recoloredPx = 0;
            // Every state the contract declares (idle/walk/sit/work), not a
            // picked subset — the sweep reads the data-sourced table itself.
            var states = new List<string>(data.States.Keys);
            states.Sort();
            for (int p = 0; p < module.PresetCount && !paletteMoved; p++)
            {
                var c0 = module.ComboForPreset(p);
                var c1 = c0;
                c1.SkinPalette = data.PaletteSkin - 1;
                c1.HairPalette = data.PaletteHair - 1;
                c1.OutfitPalette = data.PaletteOutfit - 1;
                foreach (Facing dir in Enum.GetValues(typeof(Facing)))
                {
                    foreach (var st in states)
                    {
                        int frames = data.States[st];
                        for (int f = 0; f < frames; f++)
                        {
                            int g;
                            var a = module.Composite(c0, dir, st, f, out g);
                            var b = module.Composite(c1, dir, st, f, out g);
                            byte[] pa = a.Pixels, pb = b.Pixels;
                            for (int i = 0; i + 3 < pa.Length; i += 4)
                            {
                                if (pa[i + 3] != pb[i + 3]) paletteMoved = true;
                                if ((pa[i] != pb[i] || pa[i + 1] != pb[i + 1]
                                        || pa[i + 2] != pb[i + 2])
                                    && pa[i + 3] != 0)
                                {
                                    recoloredPx++;
                                }
                            }
                        }
                    }
                }
            }
            // Recolor must actually repaint (nonzero) while moving nothing.
            if (recoloredPx == 0) paletteMoved = true;

            // --- phase/anchor sync on every layer, plus painted motion ---
            bool synced = true;
            var kinds = new[] { PartKind.Body, PartKind.Hair, PartKind.Outfit,
                PartKind.Apron, PartKind.Glasses };
            foreach (Facing dir in Enum.GetValues(typeof(Facing)))
            {
                foreach (var st in states)
                {
                    int frames = data.States[st];
                    for (int f = 0; f < frames; f++)
                    {
                        int phase = CharacterArt.PhaseDy(data, st, f);
                        int baseDx = int.MinValue, baseDy = int.MinValue;
                        foreach (var kind in kinds)
                        {
                            var l0 = CharacterArt.PaintLayer(data, kind, 0, dir, st, f);
                            if (l0.AnchorX != module.Rig.FootAnchorX
                                || l0.AnchorY != module.Rig.FootAnchorY
                                || l0.PhaseDy != phase)
                            {
                                synced = false;
                            }
                            // Painted motion vs the previous frame must be
                            // identical for every layer (shared clock).
                            if (f > 0)
                            {
                                var lp = CharacterArt.PaintLayer(data, kind, 0, dir, st, f - 1);
                                int x0, y0, x1, y1, px0, py0, px1, py1;
                                if (!CharacterArt.OpaqueBounds(l0.Pixels, out x0, out y0, out x1, out y1)
                                    || !CharacterArt.OpaqueBounds(lp.Pixels, out px0, out py0, out px1, out py1))
                                {
                                    continue;
                                }
                                int ddx = x0 - px0, ddy = y0 - py0;
                                if (baseDx == int.MinValue) { baseDx = ddx; baseDy = ddy; }
                                else if (ddx != baseDx || ddy != baseDy) synced = false;
                            }
                        }
                    }
                }
            }

            // --- seeded customers: spawn -> despawn -> snapshot scan ---
            var live = new CharacterModule();
            for (int i = 0; i < 24; i++) live.Spawn(5000 + i, false);
            int liveCustomers = live.CustomerCount;
            // Seeds must replay deterministically and stay distinct.
            bool replay = live.RollAppearance(4242, false).Key()
                == live.RollAppearance(4242, false).Key()
                && live.RollAppearance(1, false).Key()
                != live.RollAppearance(2, false).Key();
            live.DespawnAll();
            bool collectionAdded = live.PersistedCustomerRecords() != 0
                || liveCustomers != 24;

            // --- occlusion evidence on the real composite, all directions ---
            var glassed = module.ComboForPreset(1); // casual_02 wears glasses_01
            int swVis, seVis, nwVis, neVis;
            module.Composite(glassed, Facing.SW, "idle", 0, out swVis);
            module.Composite(glassed, Facing.SE, "idle", 0, out seVis);
            module.Composite(glassed, Facing.NW, "idle", 0, out nwVis);
            module.Composite(glassed, Facing.NE, "idle", 0, out neVis);
            int frontVis = Math.Min(swVis, seVis);
            int rearVis = Math.Max(nwVis, neVis);

            var cases = new List<CaseResult>();
            cases.Add(new CaseResult("rig_count", module.RigCount));
            cases.Add(new CaseResult("starter_presets", module.PresetCount));
            cases.Add(new CaseResult("palette_moves_geometry", paletteMoved));
            cases.Add(new CaseResult("layer_phase_synced", synced));
            cases.Add(new CaseResult("customer_collection_added", collectionAdded));
            cases.Add(new CaseResult("part_hair_count", data.PartHair));
            cases.Add(new CaseResult("part_outfit_count", data.PartOutfit));
            cases.Add(new CaseResult("part_apron_count", data.PartApron));
            cases.Add(new CaseResult("part_glasses_count", data.PartGlasses));
            cases.Add(new CaseResult("palette_skin_count", data.PaletteSkin));
            cases.Add(new CaseResult("palette_hair_count", data.PaletteHair));
            cases.Add(new CaseResult("palette_outfit_count", data.PaletteOutfit));
            cases.Add(new CaseResult("front_glasses_visible_px", frontVis));
            cases.Add(new CaseResult("rear_glasses_visible_px", rearVis));
            cases.Add(new CaseResult("glasses_visible_sw_px", swVis));
            cases.Add(new CaseResult("glasses_visible_se_px", seVis));
            cases.Add(new CaseResult("glasses_visible_nw_px", nwVis));
            cases.Add(new CaseResult("glasses_visible_ne_px", neVis));
            cases.Add(new CaseResult("recolored_pixel_count", recoloredPx));
            cases.Add(new CaseResult("variant_png_assets", module.VariantAssetCount));
            cases.Add(new CaseResult("appearance_seed_replay", replay));
            return cases;
        }

        /// Fresh startup state funded through the idle-sale path, then the
        /// named research reserved in order and run to completion on the lab
        /// clock. Returns the economy+lab pair for gate inspection.
        private sealed class ResearchScenario
        {
            public EconomyModule Econ;
            public ResearchModule Research;
        }

        private static ResearchScenario RunResearchScenario(
            MvpData data, double earnSeconds, params string[] reserveIds)
        {
            var econ = EconomyModule.CreateStartup(data, 0);
            var lab = new ResearchModule(econ);
            econ.SimulateSeconds(earnSeconds);
            foreach (var id in reserveIds) lab.Reserve(id);
            lab.SimulateSeconds(7200.0);
            return new ResearchScenario { Econ = econ, Research = lab };
        }

        private static object JsonNumber(Fraction f)
        {
            return f.IsWhole ? (object)f.Whole : (object)f.ToDouble();
        }
    }
}
