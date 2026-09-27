using System;
using System.Collections.Generic;
using CozyCafe.Core.Economy;
using CozyCafe.Core.Research;
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
                case "idle-economy":
                    return IdleEconomy();
                case "research-staff":
                    return ResearchStaff();
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
