using System;
using System.Collections.Generic;
using System.Globalization;
using CozyCafe.Core.Modules;

namespace CozyCafe.Core.Economy
{
    /// One owned menu line: its level and its own independent sale timer.
    public sealed class MenuLine
    {
        public readonly MenuDef Def;
        public int Level;
        public double NextSaleAt;

        public MenuLine(MenuDef def, double firstSaleAt)
        {
            Def = def;
            Level = 1;
            NextSaleAt = firstSaleAt;
        }
    }

    /// Exact rational quantity (coins/min or coin hundredths): Num / Den.
    public struct Fraction
    {
        public long Num;
        public long Den;

        public Fraction(long num, long den)
        {
            Num = num;
            Den = den;
        }

        public void Add(long num, long den)
        {
            Num = Num * den + num * Den;
            Den = Den * den;
        }

        public bool IsWhole
        {
            get { return Den != 0 && Num % Den == 0; }
        }

        public long Whole
        {
            get { return Num / Den; }
        }

        public double ToDouble()
        {
            return Den == 0 ? 0.0 : (double)Num / (double)Den;
        }
    }

    /// <summary>
    /// Automatic idle-sale economy. Owned menus run independent sale timers;
    /// each completed cycle settles once through a deduplicated event id.
    /// Per-minute rate = price * 60/cycle * level * stageMultiplier
    /// * (1 + staffBonusPct/100). Upgrade cost = ceil(base * (112/100)^(L-1))
    /// with base = ceil(Lv1 revenue/min * baseMinutes); bulk buys sum
    /// per-level ceils. The wallet holds integer coins plus a preserved
    /// fractional remainder in hundredths and can never go negative. Every
    /// number comes from MvpData (data/mvp.json); nothing is layout-coupled.
    /// </summary>
    public sealed class EconomyModule : ModuleBase
    {
        public override string Name { get { return "economy"; } }

        public readonly MvpData Data;
        public long Coins { get; private set; }
        public long RemainderHundredths { get; private set; }
        public int StaffSalesBonusPct;
        public double Clock { get; private set; }
        public bool WalletWentNegative { get; private set; }
        /// Sum of every successful wallet charge — the audit total a
        /// save restores alongside the balance so a re-billed cost is
        /// measurable across the round-trip.
        public long TotalCharged { get; private set; }
        public readonly HashSet<string> OwnedMachines = new HashSet<string>();
        public readonly HashSet<string> CompletedResearch = new HashSet<string>();
        private readonly Dictionary<string, MenuLine> lines =
            new Dictionary<string, MenuLine>();
        private readonly HashSet<string> settledEvents = new HashSet<string>();
        private long saleSeq;

        public EconomyModule(MvpData data)
        {
            Data = data;
            Coins = 0;
            RemainderHundredths = 0;
            Clock = 0;
            StaffSalesBonusPct = 0;
        }

        /// Startup state: initial coins and owned machines from the data
        /// file, no completed research, explicit staff sales bonus.
        public static EconomyModule CreateStartup(MvpData data, int staffBonusPct)
        {
            if (data == null) throw new ArgumentNullException("data");
            var e = new EconomyModule(data);
            e.Coins = data.InitialCoins;
            foreach (var m in data.InitialOwnedMachines) e.OwnedMachines.Add(m);
            e.StaffSalesBonusPct = staffBonusPct;
            e.SyncOwnedMenus();
            return e;
        }

        public IReadOnlyDictionary<string, MenuLine> Lines
        {
            get { return lines; }
        }

        /// Owned = all required machines owned AND all required research done.
        public bool Owns(MenuDef m)
        {
            foreach (var machine in m.Machines)
            {
                if (!OwnedMachines.Contains(machine)) return false;
            }
            foreach (var r in m.Research)
            {
                if (!CompletedResearch.Contains(r)) return false;
            }
            return true;
        }

        /// Auto-unlock every newly ownable menu at Lv1 (no open cost); a menu
        /// once owned stays owned.
        public int SyncOwnedMenus()
        {
            int added = 0;
            foreach (var m in Data.Menus)
            {
                if (!lines.ContainsKey(m.Id) && Owns(m))
                {
                    lines[m.Id] = new MenuLine(m, Clock + m.CycleSeconds);
                    added++;
                }
            }
            return added;
        }

        public long StageMultiplier(int level)
        {
            return Data.Upgrade.MultiplierFor(level);
        }

        /// Exact coins/min for one menu at a level under a sales bonus pct:
        /// price*60*level*stageMult*(100+pct) / (cycle*100).
        public Fraction RatePerMinute(MenuDef m, int level, int bonusPct)
        {
            long num = m.Price * 60 * level * StageMultiplier(level) * (100 + bonusPct);
            long den = m.CycleSeconds * 100;
            return new Fraction(num, den);
        }

        /// Sum of per-minute rates over all currently owned menu lines.
        public Fraction TotalRatePerMinute()
        {
            var f = new Fraction(0, 1);
            foreach (var line in lines.Values)
            {
                var r = RatePerMinute(line.Def, line.Level, StaffSalesBonusPct);
                f.Add(r.Num, r.Den);
            }
            return f;
        }

        /// ceil(Lv1 base per-minute revenue of the menu * upgrade.base_minutes).
        /// Revenue is price*60/cycle, exact integer math with ceiling.
        public long UpgradeBaseCost(MenuDef m)
        {
            long num = m.Price * 60 * Data.Upgrade.BaseMinutes;
            long den = m.CycleSeconds;
            return (num + den - 1) / den;
        }

        /// Cost to go from level L to L+1 = ceil(base * (num/den)^(L-1)).
        /// decimal keeps the 1.12^k term exact far past the level cap.
        public long UpgradeCost(MenuDef m, int fromLevel)
        {
            if (fromLevel < 1) throw new ArgumentOutOfRangeException("fromLevel");
            long b = UpgradeBaseCost(m);
            decimal ratio = (decimal)Data.Upgrade.RatioNumerator
                / (decimal)Data.Upgrade.RatioDenominator;
            decimal v = b;
            for (int k = 1; k < fromLevel; k++) v *= ratio;
            return (long)Math.Ceiling(v);
        }

        /// Bulk purchase (1/10/MAX buttons): per-level ceil applied first,
        /// then summed; levels past the cap are not sold.
        public long UpgradeCostSum(MenuDef m, int fromLevel, int levels)
        {
            long sum = 0;
            for (int i = 0; i < levels && fromLevel + i < Data.Upgrade.Cap; i++)
            {
                sum += UpgradeCost(m, fromLevel + i);
            }
            return sum;
        }

        /// Buys `levels` upgrade steps on an owned menu: atomic coin charge,
        /// clamped at the cap, only if the wallet covers the summed cost.
        public bool PurchaseUpgrade(MenuDef m, int levels)
        {
            MenuLine line;
            if (!lines.TryGetValue(m.Id, out line)) return false;
            if (levels < 1 || line.Level >= Data.Upgrade.Cap) return false;
            if (line.Level + levels > Data.Upgrade.Cap)
            {
                levels = (int)(Data.Upgrade.Cap - line.Level);
            }
            long cost = UpgradeCostSum(m, line.Level, levels);
            if (!TryCharge(cost)) return false;
            line.Level += levels;
            return true;
        }

        /// Charges integer coins. Refuses anything that would take the wallet
        /// negative and never mutates the balance on refusal.
        public bool TryCharge(long amount)
        {
            if (amount < 0 || Coins < amount) return false;
            Coins -= amount;
            TotalCharged += amount;
            if (Coins < 0) WalletWentNegative = true;
            return true;
        }

        /// Issues a unique sale-event id; the caller settles it exactly once.
        public string IssueSaleEventId(string menuId)
        {
            saleSeq++;
            return menuId + "#" + saleSeq.ToString(CultureInfo.InvariantCulture);
        }

        /// Credits one completed sale for the event id; a repeated id
        /// credits nothing, so a machine/NPC animation end can never pay
        /// twice for the same event. The payment keeps its fractional
        /// remainder in hundredths (price*level*mult*(100+pct)/100 coins).
        public bool SettleSale(string eventId, MenuDef m, int level)
        {
            if (string.IsNullOrEmpty(eventId) || m == null || level < 1) return false;
            if (!settledEvents.Add(eventId)) return false;
            long hundredths = m.Price * level * StageMultiplier(level)
                * (100 + StaffSalesBonusPct);
            RemainderHundredths += hundredths;
            Coins += RemainderHundredths / 100;
            RemainderHundredths %= 100;
            if (Coins < 0) WalletWentNegative = true;
            return true;
        }

        /// Advances the idle clock. Every owned menu completes sale events on
        /// its own cycle (first sale after one full cycle); each event goes
        /// through the once-only settlement path. Layout edits never reach
        /// this loop, so editing can neither throttle nor boost sales. A
        /// zero-length step still settles every sale already due — the
        /// session uses it to flush boundary events dated at-or-before the
        /// current clock instead of stalling on a skewed record.
        public void SimulateSeconds(double seconds)
        {
            if (seconds < 0) return;
            double horizon = Clock + seconds;
            while (true)
            {
                MenuLine next = null;
                double t = double.MaxValue;
                foreach (var line in lines.Values)
                {
                    if (line.NextSaleAt <= horizon && line.NextSaleAt < t)
                    {
                        t = line.NextSaleAt;
                        next = line;
                    }
                }
                if (next == null) break;
                Clock = next.NextSaleAt;
                SettleSale(IssueSaleEventId(next.Def.Id), next.Def, next.Level);
                next.NextSaleAt += next.Def.CycleSeconds;
            }
            Clock = horizon;
        }

        /// Seconds until the earliest pending sale event across owned menu
        /// lines — the session's next wallet boundary (PositiveInfinity when
        /// nothing is scheduled).
        public double NextSaleDelta()
        {
            double t = double.PositiveInfinity;
            foreach (var line in lines.Values)
            {
                double d = line.NextSaleAt - Clock;
                if (d < t) t = d;
            }
            if (t < 0) return 0;
            return t;
        }

        /// Deterministic state record for the save module: wallet,
        /// remainder, charge audit, ownership, dedupe set, sale sequence
        /// and every menu line's level + next-sale instant.
        public Dictionary<string, object> SaveState()
        {
            var d = new Dictionary<string, object>();
            d["coins"] = Coins;
            d["rem"] = RemainderHundredths;
            d["clock"] = Clock;
            d["staff_pct"] = (long)StaffSalesBonusPct;
            d["charged"] = TotalCharged;
            d["sale_seq"] = saleSeq;
            d["neg"] = WalletWentNegative;
            d["machines"] = SaveDoc.SortedStrings(OwnedMachines);
            d["research_done"] = SaveDoc.SortedStrings(CompletedResearch);
            d["settled"] = SaveDoc.SortedStrings(settledEvents);
            var ls = new List<object>();
            var ids = new List<string>(lines.Keys);
            ids.Sort(StringComparer.Ordinal);
            foreach (var id in ids)
            {
                var l = lines[id];
                var rec = new Dictionary<string, object>();
                rec["id"] = id;
                rec["level"] = (long)l.Level;
                rec["next_at"] = l.NextSaleAt;
                ls.Add(rec);
            }
            d["lines"] = ls;
            return d;
        }

        /// Replaces the whole module state from a save record. A next_at
        /// below zero (migrated records without a stored instant)
        /// reschedules the line one full cycle out; SyncOwnedMenus then
        /// opens any menu the restored flags newly unlock.
        public void RestoreState(Dictionary<string, object> d)
        {
            if (Data == null)
            {
                throw new FormatException("save: cannot restore economy without data");
            }
            Coins = SaveDoc.Long(SaveDoc.Get(d, "coins"));
            RemainderHundredths = SaveDoc.Long(SaveDoc.Get(d, "rem"));
            Clock = SaveDoc.Double(SaveDoc.Get(d, "clock"));
            StaffSalesBonusPct = (int)SaveDoc.Long(SaveDoc.Get(d, "staff_pct"));
            TotalCharged = SaveDoc.Long(SaveDoc.Get(d, "charged"));
            saleSeq = SaveDoc.Long(SaveDoc.Get(d, "sale_seq"));
            WalletWentNegative = SaveDoc.Bool(SaveDoc.Get(d, "neg"));
            OwnedMachines.Clear();
            foreach (var m in SaveDoc.List(SaveDoc.Get(d, "machines")))
            {
                OwnedMachines.Add(SaveDoc.Str(m));
            }
            CompletedResearch.Clear();
            foreach (var r in SaveDoc.List(SaveDoc.Get(d, "research_done")))
            {
                CompletedResearch.Add(SaveDoc.Str(r));
            }
            settledEvents.Clear();
            foreach (var e in SaveDoc.List(SaveDoc.Get(d, "settled")))
            {
                settledEvents.Add(SaveDoc.Str(e));
            }
            lines.Clear();
            foreach (var o in SaveDoc.List(SaveDoc.Get(d, "lines")))
            {
                var rec = SaveDoc.Dict(o);
                var menu = FindMenu(SaveDoc.Str(SaveDoc.Get(rec, "id")));
                if (menu == null)
                {
                    throw new FormatException("save: unknown menu id in record");
                }
                double nextAt = SaveDoc.Double(SaveDoc.Get(rec, "next_at"));
                if (nextAt < 0) nextAt = Clock + menu.CycleSeconds;
                var line = new MenuLine(menu, nextAt);
                line.Level = (int)SaveDoc.Long(SaveDoc.Get(rec, "level"));
                lines[menu.Id] = line;
            }
            SyncOwnedMenus();
        }

        private MenuDef FindMenu(string id)
        {
            return Data != null ? Data.FindMenu(id) : null;
        }

        /// Probe does real work on a throwaway startup state — the live
        /// module state is never mutated by a health check.
        protected override bool OnProbe()
        {
            if (Data == null || Data.Menus.Count == 0) return false;
            var probe = CreateStartup(Data, 0);
            if (probe.lines.Count == 0) return false;
            probe.SimulateSeconds(60.0);
            string ev = probe.IssueSaleEventId("__probe__");
            bool first = probe.SettleSale(ev, probe.Data.Menus[0], 1);
            bool dup = probe.SettleSale(ev, probe.Data.Menus[0], 1);
            return probe.Coins > 0
                && !probe.WalletWentNegative
                && first
                && !dup;
        }
    }
}
