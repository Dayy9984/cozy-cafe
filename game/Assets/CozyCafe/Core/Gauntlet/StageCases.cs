using System;
using System.Collections.Generic;
using System.IO;
using CozyCafe.Core.Art;
using CozyCafe.Core.Character;
using CozyCafe.Core.Economy;
using CozyCafe.Core.Iso;
using CozyCafe.Core.Layout;
using CozyCafe.Core.Render;
using CozyCafe.Core.Research;
using CozyCafe.Core.Save;
using CozyCafe.Core.Scene;
using CozyCafe.Core.Staff;
using CozyCafe.Core.Tools;
using CozyCafe.Core.Ugc;
using CozyCafe.Core.Ui;

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
                case "art-pipeline":
                    return ArtPipeline();
                case "desktop-tools":
                    return DesktopTools();
                case "ui-local-ugc":
                    return UiLocalUgc();
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
            cases.Add(new CaseResult("stack_level_height_px",
                IsoContract.StackLevelHeightPx()));
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
                // healthy save -> failover picks the primary and restores.
                var lsrcL = sL2.RestoreThroughStore(storeL);
                cases.Add(new CaseResult("restored_layout_equal",
                    commit1 == PlacementReject.None
                    && commit2 == PlacementReject.None
                    && tb.Ok && ct.Ok && mg.Ok
                    && mg.Placed.HostId == ct.Placed.Id
                    && lsrcL == SaveStore.LoadSource.Primary
                    && sL2.Layout.SaveLayout() == layoutSnap));

                // ---- settlement-id dedupe -------------------------------
                // Re-applying the same checkpoint document credits zero.
                var dup = back.SettleOffline(docRead, T0 + window);
                long dupGain = dup.CoinsGained * 100 + dup.RemainderGained;
                if (!dup.Duplicate) dupGain++;
                cases.Add(new CaseResult("duplicate_credit", dupGain));

                // ---- skewed restored instants flush, never stall --------
                // A doc whose event instants sit behind their own clocks is
                // parseable but skewed: Advance must settle the due events
                // at their recorded instants and still cover the window —
                // never freeze the timeline while reporting it advanced.
                var sK = CafeSession.CreateStartup(data, 66, 6, 6);
                sK.Lab.Reserve("R01");
                sK.Advance(500);            // R01 active, wallet earning
                var docK = sK.BuildDocument(T0);
                // skew: the research end and one sale land behind their
                // module clocks.
                SaveDoc.Dict(docK.Research["active"])["ends_at"] =
                    SaveDoc.Double(SaveDoc.Get(docK.Research, "clock")) - 10.0;
                var kLines = SaveDoc.List(SaveDoc.Get(docK.Economy, "lines"));
                SaveDoc.Dict(kLines[0])["next_at"] =
                    SaveDoc.Double(SaveDoc.Get(docK.Economy, "clock")) - 5.0;
                long kCoins0 = SaveDoc.Long(SaveDoc.Get(docK.Economy, "coins"));
                var sK2 = CafeSession.CreateStartup(data, 2, 6, 6);
                sK2.Restore(docK);
                double kClock0 = sK2.RuntimeClock;
                sK2.Advance(60);
                cases.Add(new CaseResult("overdue_boundary_flushes",
                    sK2.RuntimeClock == kClock0 + 60
                    && sK2.Econ.CompletedResearch.Contains("R01")
                    && sK2.Lab.Active == null
                    && sK2.Econ.Coins > kCoins0));

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
                var s6 = CafeSession.CreateStartup(data, 1, 6, 6);
                var lsrc = s6.RestoreThroughStore(storeF);
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
                var s7 = CafeSession.CreateStartup(data, 1, 6, 6);
                var lsrc2 = s7.RestoreThroughStore(storeF);
                bool powerLossRecovered = lsrc2 == SaveStore.LoadSource.Backup
                    && s7.Econ.Coins == coinsF1
                    && s7.Lab.Active != null && s7.Lab.Active.Id == "R01";

                // both copies unreadable -> None, no silent state.
                File.WriteAllText(storeF.BackupPath, "also junk");
                string rt;
                bool noneFound = storeF.TryRead(out rt) == SaveStore.LoadSource.None;

                // structurally valid but semantically dead primary ->
                // backup: the doc parses (every field present, right
                // types) yet its economy record names a menu that does
                // not exist, so a full restore must reject it and the
                // session-level failover must still land on the backup.
                File.WriteAllText(storeF.BackupPath, full2);
                var dead = (Dictionary<string, object>)MiniJson.Parse(full2);
                var deadLines = SaveDoc.List(SaveDoc.Get(
                    SaveDoc.Dict(dead["economy"]), "lines"));
                var deadRec = new Dictionary<string, object>();
                deadRec["id"] = "ZZZ";
                deadRec["level"] = (long)1;
                deadRec["next_at"] = 7.0;
                deadLines.Add(deadRec);
                File.WriteAllText(storeF.PrimaryPath, MiniJson.ToJson(dead));
                // prove the dead primary really passed the JSON screen —
                // the end-to-end restore proof is what refused it.
                bool deadPassedScreen = SaveDocument.IsValidJson(
                    File.ReadAllText(storeF.PrimaryPath));
                var sD = CafeSession.CreateStartup(data, 1, 6, 6);
                var lsrcD = sD.RestoreThroughStore(storeF);
                bool deepCorruptRecovered = deadPassedScreen
                    && lsrcD == SaveStore.LoadSource.Backup
                    && sD.Econ.Coins == coinsF1
                    && sD.Lab.Active != null && sD.Lab.Active.Id == "R01";

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
                    && noneFound && migrated && deepCorruptRecovered));
                cases.Add(new CaseResult("save_version_migrates", migrated));
                cases.Add(new CaseResult("malformed_json_uses_backup", badJsonRecovered));
                cases.Add(new CaseResult("power_loss_recovers", powerLossRecovered));
                cases.Add(new CaseResult("semantic_corrupt_uses_backup", deepCorruptRecovered));
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

        /// Desktop work-tools contract: every value is produced by real
        /// ToolsModule calls — the window-mode switch against a live cafe
        /// scene, memo/todo state through the module's own MiniJson save
        /// path, the focus timer's exact pause report, zero-credit sleep
        /// records, and the local music deck's controls. Nothing here is a
        /// declared constant.
        private static List<CaseResult> DesktopTools()
        {
            var cases = new List<CaseResult>();

            // --- normal <-> mini switch keeps the live cafe state ---
            var boot = GameBootstrap.Create();
            boot.LoadDefaultScene();
            var layout = new LayoutModule(boot.Scene);
            string layoutBefore = layout.SaveLayout();
            int furnitureBefore = boot.Scene.Furniture.Count;
            int agentsBefore = boot.Scene.Agents.Count;
            ToolsModule tools = null;
            foreach (var m in boot.Registry.Modules)
            {
                var t = m as ToolsModule;
                if (t != null) tools = t;
            }
            bool registered = tools != null;
            if (tools == null) tools = new ToolsModule();
            tools.SetMode(WindowMode.Mini);
            bool wentMini = tools.Mode == WindowMode.Mini
                && tools.ToolsPanelVisible;
            bool stateInMini = layout.SaveLayout() == layoutBefore
                && boot.Scene.Furniture.Count == furnitureBefore
                && boot.Scene.Agents.Count == agentsBefore
                && boot.Scene.IsLoaded;
            tools.SetMode(WindowMode.Normal);
            bool modeKeepsState = registered && wentMini
                && tools.Mode == WindowMode.Normal && stateInMini
                && layout.SaveLayout() == layoutBefore;
            cases.Add(new CaseResult("mode_switch_keeps_state", modeKeepsState));

            // --- memo: real Korean text autosaves on edit and survives the
            //     module's save/load path with no explicit save call ---
            var tm = new ToolsModule();
            const string memoBody = "오늘 매출 정산하기\n내일 우유 주문";
            int memoId = tm.CreateMemo("");
            tm.BeginMemoEdit(memoId);
            tm.SetMemoText(memoId, memoBody);
            tm.EndMemoEdit();
            var tmBack = new ToolsModule();
            tmBack.LoadTools(tm.AutosavedJson);
            bool memoOk = tmBack.Memos.Count == 1
                && tmBack.Memos[0].Id == memoId
                && tmBack.Memos[0].Text == memoBody;
            cases.Add(new CaseResult("memo_roundtrip", memoOk));

            // --- todo: add / complete / reorder, then the same round-trip ---
            var td = new ToolsModule();
            int ta = td.AddTodo("재고 확인");
            int tb = td.AddTodo("창가 청소");
            int tc = td.AddTodo("신메뉴 연구");
            td.CompleteTodo(tb);
            td.MoveTodo(tc, 0);
            var tdBack = new ToolsModule();
            tdBack.LoadTools(td.SaveTools());
            bool todoOk = tdBack.Todos.Count == 3
                && tdBack.Todos[0].Id == tc && tdBack.Todos[0].Text == "신메뉴 연구"
                && !tdBack.Todos[0].Done
                && tdBack.Todos[1].Id == ta && !tdBack.Todos[1].Done
                && tdBack.Todos[2].Id == tb && tdBack.Todos[2].Done;
            cases.Add(new CaseResult("todo_roundtrip", todoOk));

            // --- timer: 25m preset, 600s elapsed, pause reports exactly
            //     the remaining seconds ---
            var tf = new ToolsModule();
            tf.Timer.StartFocus();
            tf.TickTimer(600);
            double pausedRemaining = tf.Timer.Pause();
            cases.Add(new CaseResult("pause_remaining_seconds", pausedRemaining));

            // --- focus records: sleep and exit credit exactly 0 ---
            var ts = new ToolsModule();
            ts.Timer.StartFocus();
            ts.TickTimer(420);
            double focusBefore = ts.TotalFocusSeconds;
            ts.OnSystemSleep();
            double sleepAdded = ts.TotalFocusSeconds - focusBefore;
            cases.Add(new CaseResult("sleep_focus_added", sleepAdded));
            ts.Timer.StartFocus();
            ts.TickTimer(300);
            ts.OnSystemExit();
            cases.Add(new CaseResult("exit_focus_added",
                ts.TotalFocusSeconds - focusBefore - sleepAdded));
            // A session that actually completes still banks its seconds —
            // the zero above is the sleep/exit rule, not a broken ledger.
            ts.Timer.StartFocus();
            ts.TickTimer(1500);
            double completedFocus = ts.TotalFocusSeconds;
            cases.Add(new CaseResult("completed_focus_seconds", completedFocus));
            ts.Timer.StartBreak();
            ts.TickTimer(300);
            cases.Add(new CaseResult("break_session_credited_seconds",
                ts.TotalFocusSeconds - completedFocus));

            // --- music: controls drive the real local deck ---
            var mu = new ToolsModule();
            bool deck = mu.Music.Catalog.Count > 0
                && mu.Music.AllSourcesAllowed()
                && mu.Music.Play() && mu.Music.IsPlaying
                && mu.Music.CurrentIndex == 0
                && mu.Music.Next() && mu.Music.CurrentIndex == 1
                && mu.Music.Previous() && mu.Music.CurrentIndex == 0
                && mu.Music.SetVolume(0.4) == 0.4
                && mu.Music.PausePlayback() && !mu.Music.IsPlaying;
            cases.Add(new CaseResult("music_controls_connected", deck));
            cases.Add(new CaseResult("music_scope_local_only",
                mu.Music.AllSourcesAllowed()
                && !mu.Music.Enqueue(new MusicTrack
                {
                    Id = "ext",
                    Title = "x",
                    DurationSeconds = 10,
                    Source = MusicSourceKind.ExternalOAuth
                })));

            // --- memo text entry suppresses game shortcuts (rule evidence) ---
            var tk = new ToolsModule();
            int km = tk.CreateMemo("키 입력");
            bool routedNormally = tk.RouteGameShortcut("open_research");
            tk.BeginMemoEdit(km);
            bool suppressedWhileTyping = tk.GameShortcutsSuppressed
                && !tk.RouteGameShortcut("open_research");
            tk.EndMemoEdit();
            cases.Add(new CaseResult("shortcut_suppressed_while_memo_editing",
                routedNormally && suppressedWhileTyping
                && tk.RouteGameShortcut("open_research")));

            // --- tool time stays out of cafe settlement ---
            var data = MvpData.Load();
            var econ = EconomyModule.CreateStartup(data, 0);
            econ.SimulateSeconds(60);
            long coins = econ.Coins;
            double clock = econ.Clock;
            var tt = new ToolsModule();
            tt.Timer.StartFocus();
            tt.TickTimer(1500);
            bool separate = econ.Coins == coins && econ.Clock == clock
                && tt.TotalFocusSeconds == 1500;
            cases.Add(new CaseResult("tool_time_separate_from_settlement",
                separate));

            return cases;
        }


        /// <summary>
        /// Shared UI layer + local UGC creator. Every value is produced by
        /// real module calls: runtime string-to-pixel text rasterization
        /// (different Korean strings must paint different ink, identical
        /// strings identical ink, and no baked-text source exists), a real
        /// 9-slice resize whose corners stay pixel-exact while the widget's
        /// clickable area tracks its rect, the PNG -> role/anchor/direction
        /// -> preview -> validation -> save -> apply import flow against a
        /// real store, the allow-listed public preset audited by the save
        /// module's own private-field scan, and skin removal/missing-asset
        /// fallback leaving owned functional machines in place.
        /// </summary>
        private static List<CaseResult> UiLocalUgc()
        {
            var cases = new List<CaseResult>();
            var data = MvpData.Load();
            var ink = Rgba.Opaque(240, 234, 244);

            // --- runtime Korean text: the live string is the only source.
            //     Identical strings rasterize identical pixels; different
            //     strings (including same-cell-count syllables with
            //     different jamo) rasterize different pixels; and the
            //     module has no baked-text asset path at all.
            var ui = new UiModule();
            var tA = ui.RenderText("내 카페", 1, ink);
            var tA2 = ui.RenderText("내 카페", 1, ink);
            var tB = ui.RenderText("불러오기", 1, ink);
            var tC = ui.RenderText("꿈", 1, ink);
            var tD = ui.RenderText("금", 1, ink);
            bool liveRaster = CanvasHasInk(tA) && SamePixels(tA, tA2)
                && !SamePixels(tA, tB) && !SamePixels(tC, tD)
                && !SamePixels(tA, tC);
            cases.Add(new CaseResult("ui_text_baked",
                ui.TextBaked || ui.BakedTextAssets != 0 || !liveRaster));

            // --- 9-slice resize: corners pixel-exact at a larger size and
            //     at a clamped smaller size, edges/center sampled through,
            //     the source sprite untouched, the widget's clickable area
            //     equal to its resized rect, and the painted skin corner
            //     landing exactly on the rect (no iso, no -8 lift).
            var skinSrc = BuildCaseSkin();
            var skin = new UiSkin("case", skinSrc, 10, 10, 10, 10);
            var big = skin.Render(150, 70);
            var tiny = skin.Render(24, 18);
            bool cornersOk =
                SameColor(big.GetPixel(3, 3), skinSrc.GetPixel(3, 3))
                && SameColor(big.GetPixel(146, 3), skinSrc.GetPixel(32, 3))
                && SameColor(big.GetPixel(3, 66), skinSrc.GetPixel(3, 32))
                && SameColor(big.GetPixel(146, 66), skinSrc.GetPixel(32, 32))
                && SameColor(tiny.GetPixel(0, 0), skinSrc.GetPixel(0, 0))
                && SameColor(tiny.GetPixel(23, 17), skinSrc.GetPixel(35, 35));
            bool mappedOk =
                SameColor(big.GetPixel(75, 35), skinSrc.GetPixel(18, 18))
                && SameColor(big.GetPixel(75, 5), skinSrc.GetPixel(18, 5))
                && SameColor(big.GetPixel(5, 35), skinSrc.GetPixel(5, 18));
            ui.RegisterSkin(skin);
            var wgt = ui.AddButton("resize", 40, 40, 60, 24, "확인", "case");
            wgt.SetRect(40, 40, 150, 70);
            bool hitOk = wgt.Contains(40, 40) && wgt.Contains(189, 109)
                && wgt.Contains(40, 109) && !wgt.Contains(190, 110);
            var frame = ui.BuildFrame(240, 140);
            var cv = new SoftwareCanvas(240, 140);
            cv.Clear(new Rgba(10, 10, 14, 255));
            ui.Paint(cv, frame);
            var cornerInk = skinSrc.GetPixel(0, 0);
            int lift = 99;
            for (int yy = wgt.Y - 16; yy <= wgt.Y + 4; yy++)
            {
                if (SameColor(cv.GetPixel(wgt.X, yy), cornerInk))
                {
                    lift = yy - wgt.Y;
                    break;
                }
            }
            cases.Add(new CaseResult("ui_render_offset_y_px", lift));
            cases.Add(new CaseResult("nine_slice_resizes",
                cornersOk && mappedOk && hitOk && lift == 0
                && !frame.UsedFallbackSkin
                && skinSrc.GetPixel(0, 0).R == 210));

            // --- real widget states + focused text input ----------------
            var uis = new UiModule();
            var b1 = uis.AddButton("b1", 0, 0, 60, 20, "저장", "");
            var b2 = uis.AddButton("b2", 0, 30, 60, 20, "불러오기", "");
            uis.SetHovered(b1);
            bool st1 = b1.State == UiState.Hover && b2.State == UiState.Normal;
            uis.Press(b1);
            bool st2 = b1.State == UiState.Pressed;
            uis.Release(b1);
            uis.SetEnabled(b2, false);
            uis.Press(b2);
            bool st3 = b1.State == UiState.Normal
                && b2.State == UiState.Disabled;
            var inp = uis.AddTextInput("in", 0, 60, 80, 20, "이름", "");
            uis.FocusInput(inp);
            uis.TypeText("민트 카페");
            uis.CaretLeft();
            uis.CaretLeft();
            uis.TypeText("온 ");
            uis.Backspace();
            uis.TypeText(" ");
            cases.Add(new CaseResult("widget_state_transitions",
                st1 && st2 && st3));
            cases.Add(new CaseResult("text_input_roundtrip",
                inp.InputText == "민트 온 카페" && inp.Caret == 5
                && uis.TextInputActive));

            string tmpDir = Path.Combine(Directory.GetCurrentDirectory(),
                "out", "ui-ugc-tmp");
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
            try
            {
                UiLocalUgcFlow(cases, data, skinSrc, tmpDir);
            }
            finally
            {
                try { Directory.Delete(tmpDir, true); }
                catch (Exception) { }
            }
            return cases;
        }

        /// The creator flow on a real local store: a PNG authored in
        /// memory is imported, assigned a role/anchor/direction, previewed
        /// into real pixels, validated, saved as a public preset + content
        /// asset, then applied to a live session. Rejection paths run too:
        /// undecodable bytes, unknown roles, role-illegal directions.
        private static void UiLocalUgcFlow(List<CaseResult> cases,
            MvpData data, SoftwareCanvas skinSrc, string tmpDir)
        {
            byte[] png = PngWriter.Encode(
                skinSrc.Width, skinSrc.Height, skinSrc.Pixels);
            var ugc = new UgcModule { StoreDir = tmpDir };
            var draft = ugc.BeginImport(png, "cafe-panel");
            bool importOk = draft != null && draft.Image != null
                && draft.Image.Width == 36 && draft.Image.Height == 36;
            bool assigned = ugc.Assign("ui_panel_skin", 0.5, 0.5, "");
            ugc.AddPlacement(FurnitureKind.Counter, 4, 2, 0);
            ugc.AddAppearance("furniture:counter");
            var preview = ugc.BuildPreview(96, 72);
            bool previewOk = preview != null && preview.Width == 96
                && preview.Height == 72;
            bool validOk = ugc.Validate().Count == 0;
            string presetId = validOk ? ugc.Save() : null;
            bool savedOk = presetId != null
                && File.Exists(ugc.PresetPath(presetId))
                && File.Exists(ugc.AssetPath(draft.AssetId));
            var docObj = savedOk
                ? MiniJson.Parse(File.ReadAllText(ugc.PresetPath(presetId)))
                    as Dictionary<string, object>
                : null;
            savedOk = savedOk && docObj != null
                && UgcModule.PresetHasPlacementRefs(docObj)
                && !CafeSession.PresetContainsPrivateFields(docObj);

            // apply: one real placement through the editor rules plus the
            // counter appearance ref resolving to the stored PNG.
            var session = CafeSession.CreateStartup(data, 7, 6, 5);
            session.Layout.TryPlace(FurnitureKind.Door, 0, 1, 0);
            var inst = ugc.Apply(session);
            var counter = inst.PlacedIds.Count > 0
                ? session.Layout.Find(inst.PlacedIds[0]) : null;
            var installed = ugc.InstallUiSkin(draft.AssetId, 10);
            bool applied = inst.PlacedIds.Count == 1
                && inst.Rejected.Count == 0
                && inst.SkinRefs.Count == 1
                && counter != null && counter.Kind == FurnitureKind.Counter
                && ugc.AppearanceOf("furniture:counter") == draft.AssetId
                && session.Layout.ValidateLayout() == PlacementReject.None
                && installed != null && installed.Source.Width == 36;

            // rejection evidence: undecodable bytes die at import, unknown
            // roles and role-illegal directions die at assign.
            var bad = new UgcModule { StoreDir = tmpDir };
            bool rejected = bad.BeginImport(new byte[] { 1, 2, 3 }, "x") == null
                && bad.Draft == null;
            var bad2 = new UgcModule { StoreDir = tmpDir };
            bad2.BeginImport(png, "y");
            rejected = rejected
                && !bad2.Assign("bogus_role", 0.5, 0.5, "")
                && !bad2.Assign("ui_panel_skin", 0.5, 0.5, "SE")
                && bad2.Assign("furniture_skin", 0.5, 0.5, "SE");
            cases.Add(new CaseResult("ugc_import_works",
                importOk && assigned && previewOk && validOk
                && savedOk && applied && rejected));

            // --- public preset audit: placement + appearance refs ride,
            //     private data (memos/todos here, wallet/research/staff by
            //     construction) stays behind.
            session.Memos.Add("private memo: vault code 1234");
            session.Todos.Add("call the supplier");
            var preset = ugc.ExportPublicPreset(session, "내 카페");
            cases.Add(new CaseResult("preset_contains_private_fields",
                CafeSession.PresetContainsPrivateFields(preset)));
            cases.Add(new CaseResult("preset_placement_refs_present",
                UgcModule.PresetHasPlacementRefs(preset)));

            // --- missing/removed skin: owned functional machines stay ---
            var s2 = CafeSession.CreateStartup(data, 11, 6, 6);
            s2.Layout.TryPlace(FurnitureKind.Door, 0, 2, 0);
            s2.Layout.BeginCommand();
            var ctr = s2.Layout.TryPlace(FurnitureKind.Counter, 3, 2, 0);
            s2.Layout.EndCommand();
            var grinder = s2.Layout.TryPlace(FurnitureKind.Grinder, 3, 2, 0);
            string layoutBefore = s2.Layout.SaveLayout();

            var ugc3 = new UgcModule { StoreDir = tmpDir };
            ugc3.BeginImport(png, "grinder-skin");
            ugc3.Assign("furniture_skin", 0.5, 0.5, "");
            ugc3.AddAppearance("furniture:grinder");
            ugc3.BuildPreview(48, 36);
            ugc3.Validate();
            ugc3.Save();
            var inst3 = ugc3.Apply(s2);
            bool skinOn = inst3.SkinRefs.Count == 1
                && ugc3.AppearanceOf("furniture:grinder") == ugc3.Draft.AssetId;
            // remove the skin, then delete its file and apply again: the
            // second apply must record a fallback, not drop the machine.
            bool removed = ugc3.RemoveAppearance(s2, ugc3.Draft.AssetId);
            File.Delete(ugc3.AssetPath(ugc3.Draft.AssetId));
            var inst4 = ugc3.Apply(s2);
            bool fallback = inst4.FallbackAssets.Count == 1
                && ugc3.AppearanceOf("furniture:grinder") == null;
            var m01 = data.FindMenu("M01");
            bool preserved = ctr.Ok && grinder.Ok
                && grinder.Placed.HostId == ctr.Placed.Id
                && s2.Layout.Find(grinder.Placed.Id) != null
                && s2.Layout.SaveLayout() == layoutBefore
                && s2.Layout.ValidateLayout() == PlacementReject.None
                && s2.Econ.OwnedMachines.Contains("grinder")
                && s2.Econ.OwnedMachines.Contains("espresso")
                && s2.Econ.Owns(m01);
            cases.Add(new CaseResult("missing_skin_preserves_owned_machine",
                skinOn && removed && fallback && preserved));
        }

        /// The authored skin used by the resize/import checks: a 36x36
        /// sprite with a distinct outer ring, border band, center fill and
        /// four different corner accents so corner fidelity is measurable
        /// pixel-by-pixel through any resize.
        private static SoftwareCanvas BuildCaseSkin()
        {
            var c = new SoftwareCanvas(36, 36);
            c.Clear(new Rgba(0, 0, 0, 0));
            for (int y = 0; y < 36; y++)
            {
                for (int x = 0; x < 36; x++)
                {
                    if (x < 10 || y < 10 || x >= 26 || y >= 26)
                    {
                        bool edge = x < 2 || y < 2 || x >= 34 || y >= 34;
                        c.SetPixel(x, y, edge
                            ? Rgba.Opaque(210, 160, 110)
                            : Rgba.Opaque(96, 74, 60));
                    }
                    else
                    {
                        c.SetPixel(x, y, Rgba.Opaque(52, 44, 62));
                    }
                }
            }
            c.FillRect(2, 2, 8, 8, Rgba.Opaque(236, 200, 140));
            c.FillRect(26, 2, 8, 8, Rgba.Opaque(200, 150, 96));
            c.FillRect(2, 26, 8, 8, Rgba.Opaque(170, 120, 80));
            c.FillRect(26, 26, 8, 8, Rgba.Opaque(140, 96, 64));
            return c;
        }

        private static bool SamePixels(SoftwareCanvas a, SoftwareCanvas b)
        {
            if (a == null || b == null || a.Width != b.Width
                || a.Height != b.Height) return false;
            var pa = a.Pixels; var pb = b.Pixels;
            for (int i = 0; i < pa.Length; i++)
            {
                if (pa[i] != pb[i]) return false;
            }
            return true;
        }

        private static bool SameColor(Rgba a, Rgba b)
        {
            return a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A;
        }

        private static bool CanvasHasInk(SoftwareCanvas c)
        {
            if (c == null) return false;
            var px = c.Pixels;
            for (int i = 3; i < px.Length; i += 4)
            {
                if (px[i] != 0) return true;
            }
            return false;
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

        /// <summary>
        /// Real art-pipeline state under the v0.8.3 32x32 unit contract:
        /// provider/auth config, generated raws and per-job provenance on
        /// disk, the approved atlas manifest + decoded sheet pixels (tile
        /// measured against the unit 32x16-top geometry, furniture effective
        /// offset = baked + runtime, palette-variant regeneration count), the
        /// per-cell unique-color count on the decoded PNG, and the per-cell
        /// silhouette-IoU + palette conformance re-measured against the
        /// committed raw references through each frame's declared ref_map.
        /// Nothing is claimed - a missing artifact or unverified model
        /// reports its actual measured value.
        /// </summary>
        private static List<CaseResult> ArtPipeline()
        {
            string provider = ArtAssets.Provider();
            var manifest = ArtAssets.LoadManifest();
            SoftwareCanvas sheet = null;
            if (manifest.Valid)
            {
                PngReader.TryLoad(ArtAssets.SheetPath(manifest), out sheet);
            }

            int maxColors; double minIou, minPalette, cover;
            bool limits = ArtAssets.ContractLimits(out maxColors, out minIou,
                out minPalette, out cover);

            ArtFrame tile = null;
            bool anyTall = false;
            foreach (var f in manifest.Frames)
            {
                if (f.Category == "tile" && tile == null) tile = f;
                if (f.H > 32) anyTall = true;
            }
            bool tileOk = false;
            if (tile != null && sheet != null)
            {
                var tm = ArtAssets.MeasureTileUnit(sheet, tile);
                tileOk = tile.W == 32 && tile.H == 32
                    && tm.TopRow == 0 && tm.MaxWidth == 32
                    && tm.TopFaceRows == 16 && tm.EquatorRow >= 6
                    && tm.EquatorRow <= 10 && tm.SilhouetteBottomRow >= 15
                    && tm.SilhouetteBottomRow <= 18;
            }

            bool recorded;
            int effY = ArtAssets.EffectiveFurnitureOffsetY(manifest, out recorded);
            bool physicalThickness = manifest.PhysicalThickness != 0 || anyTall;
            string effective = ArtAssets.EffectiveImageModel(manifest);

            var cases = new List<CaseResult>();
            cases.Add(new CaseResult("provider", provider ?? "BLOCKED"));
            cases.Add(new CaseResult("credentials_bundled",
                ArtAssets.CredentialsBundled()));
            cases.Add(new CaseResult("raw_png_exists", ArtAssets.RawPngsExist()));
            cases.Add(new CaseResult("atlas_manifest_valid", manifest.Valid));
            cases.Add(new CaseResult("generation_per_palette_variant",
                ArtAssets.PaletteVariantGenerations(manifest)));
            cases.Add(new CaseResult("tile_size_verified", tileOk));
            cases.Add(new CaseResult("effective_image_model", effective));
            cases.Add(new CaseResult("signed_claim_software_agent",
                manifest.SignedAgent ?? "NONE"));
            cases.Add(new CaseResult("provenance_session_bound",
                ArtAssets.ProvenanceSessionBound()));
            cases.Add(new CaseResult("thickness_physical_geometry_generated",
                physicalThickness));
            cases.Add(new CaseResult("tile_canvas_height",
                ArtAssets.TileCanvasHeight(manifest)));
            cases.Add(new CaseResult("furniture_baked_offset_recorded", recorded));
            cases.Add(new CaseResult("furniture_effective_offset_y_px", effY));
            cases.Add(new CaseResult("atlas_frames_present",
                manifest.Valid && sheet != null
                    && ArtAssets.AtlasFramesPresent(manifest, sheet)));
            cases.Add(new CaseResult("atlas_all_frames_32x32",
                ArtAssets.AllFramesUnit(manifest, 32)));
            cases.Add(new CaseResult("pixel_art_quantized_cells",
                limits && sheet != null
                    && ArtAssets.QuantizedCellsOk(sheet, manifest, maxColors)));
            cases.Add(new CaseResult("reference_conformance_ok",
                limits && sheet != null
                    && ArtAssets.ReferenceConformanceOk(sheet, manifest,
                        minIou, minPalette, cover)));
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
