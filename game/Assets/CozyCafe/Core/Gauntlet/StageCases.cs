using System;
using System.Collections.Generic;
using CozyCafe.Core.Character;
using CozyCafe.Core.Economy;
using CozyCafe.Core.Iso;
using CozyCafe.Core.Research;
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
