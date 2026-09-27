using System;
using System.Collections.Generic;
using CozyCafe.Core.Economy;

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

        private static object JsonNumber(Fraction f)
        {
            return f.IsWhole ? (object)f.Whole : (object)f.ToDouble();
        }
    }
}
