using System;
using System.Collections.Generic;
using CozyCafe.Core.Modules;

namespace CozyCafe.Core.Staff
{
    /// One hire candidate: an appearance tuple (hair/top/glasses/palette)
    /// drawn from AppearanceSeed and integer percent stats drawn from
    /// StatSeed. The two seeds are independent inputs — the same StatSeed
    /// always reproduces the same stats no matter which appearance seed was
    /// rolled, and the same AppearanceSeed reproduces the same look.
    public sealed class StaffCandidate
    {
        public string Id;
        public int AppearanceSeed;
        public int StatSeed;
        public int HairIndex;
        public int TopIndex;
        public int GlassesIndex;
        public int PaletteIndex;
        public int SalesBonusPct;
        public int ResearchBonusPct;

        public string AppearanceKey
        {
            get { return "h" + HairIndex + "t" + TopIndex + "g" + GlassesIndex + "p" + PaletteIndex; }
        }

        public string StatKey
        {
            get { return "s" + SalesBonusPct + "r" + ResearchBonusPct; }
        }

        public string Serialize()
        {
            return Id + "|" + AppearanceSeed + "|" + StatSeed + "|"
                + AppearanceKey + "|" + StatKey;
        }
    }

    /// <summary>
    /// Hire office per data/mvp.json and planning/03: StaffCandidates(3)
    /// random candidates per generation, the first StaffFree(1) hires are
    /// free, the roster caps at StaffMax(3) and every later hire costs
    /// StaffCost(2000). Candidate generation is persisted — the same
    /// generation seed plus the panel's already-drawn list means reopening
    /// the window (or restarting) can never re-roll what was offered; only a
    /// hired slot is refilled. Appearance parts and the sales/research bonus
    /// percents are rolled from independent seeds, so a staff's look never
    /// decides its numbers. No wages, fatigue, rank or story.
    /// </summary>
    public sealed class StaffModule : ModuleBase
    {
        public override string Name { get { return "staff"; } }

        public readonly Economy.EconomyModule Econ;
        public readonly long GenerationSeed;
        public readonly List<StaffCandidate> Candidates = new List<StaffCandidate>();
        public readonly List<StaffCandidate> Roster = new List<StaffCandidate>();
        public bool PanelOpen { get; private set; }
        private int nextCandidateId;

        public StaffModule(Economy.EconomyModule econ, long generationSeed)
        {
            Econ = econ;
            GenerationSeed = generationSeed;
        }

        /// Deterministic candidate roll. The appearance stream consumes
        /// appSeed only and the stat stream consumes statSeed only — the two
        /// Random instances never share draws.
        public StaffCandidate RollCandidate(int appSeed, int statSeed)
        {
            var c = new StaffCandidate();
            c.AppearanceSeed = appSeed;
            c.StatSeed = statSeed;
            var app = new Random(appSeed);
            c.HairIndex = app.Next(4);
            c.TopIndex = app.Next(3);
            c.GlassesIndex = app.Next(2);
            c.PaletteIndex = app.Next(4);
            var stat = new Random(statSeed);
            int min = (int)Econ.Data.StaffStatPctMin;
            int max = (int)Econ.Data.StaffStatPctMax;
            c.SalesBonusPct = stat.Next(min, max + 1);
            c.ResearchBonusPct = stat.Next(min, max + 1);
            return c;
        }

        /// Opening the hire window returns the persisted candidate list;
        /// it only generates on the first open (or after slots refill),
        /// never re-rolls what was already offered.
        public IReadOnlyList<StaffCandidate> OpenHirePanel()
        {
            PanelOpen = true;
            if (Candidates.Count == 0) RefillCandidates();
            return Candidates;
        }

        public void CloseHirePanel()
        {
            PanelOpen = false;
        }

        /// Draws candidates up to the data-file count using per-candidate
        /// appearance/stat seeds derived from the persisted generation seed.
        private void RefillCandidates()
        {
            while (Candidates.Count < Econ.Data.StaffCandidates)
            {
                int i = nextCandidateId;
                var c = RollCandidate(
                    unchecked((int)(GenerationSeed + 0x9E3779B9L * (i + 1))),
                    unchecked((int)(GenerationSeed + 0x85EBCA6BL * (i + 1))));
                c.Id = "cand_" + i;
                Candidates.Add(c);
                nextCandidateId++;
            }
        }

        /// Hires a shown candidate: free while the roster is under the free
        /// allowance, then StaffCost charged once per hire through the real
        /// wallet; roster size never exceeds the max. The hired slot is
        /// refilled so the panel keeps a full slate without re-rolling.
        public bool Hire(int candidateIndex)
        {
            if (Econ.Data == null || Roster.Count >= Econ.Data.StaffMax) return false;
            if (candidateIndex < 0 || candidateIndex >= Candidates.Count) return false;
            long cost = Roster.Count < Econ.Data.StaffFree ? 0 : Econ.Data.StaffCost;
            if (cost > 0 && !Econ.TryCharge(cost)) return false;
            var cand = Candidates[candidateIndex];
            Candidates.RemoveAt(candidateIndex);
            Roster.Add(cand);
            RefillCandidates();
            return true;
        }

        /// Combined sales bonus applied by the economy module.
        public int TotalSalesBonusPct
        {
            get
            {
                int n = 0;
                foreach (var c in Roster) n += c.SalesBonusPct;
                return n;
            }
        }

        /// Probe exercises the persisted panel and the independent roll
        /// streams on a throwaway office keyed off the live module's seed.
        protected override bool OnProbe()
        {
            if (Econ == null || Econ.Data == null || Econ.Data.StaffCandidates < 1)
            {
                return false;
            }
            var probe = new StaffModule(Econ, GenerationSeed);
            var first = probe.OpenHirePanel();
            if (first.Count != Econ.Data.StaffCandidates) return false;
            var snap = new List<string>();
            foreach (var c in first) snap.Add(c.Serialize());
            probe.CloseHirePanel();
            var second = probe.OpenHirePanel();
            if (second.Count != snap.Count) return false;
            for (int i = 0; i < snap.Count; i++)
            {
                if (second[i].Serialize() != snap[i]) return false;
            }
            var a = probe.RollCandidate(101, 7);
            var b = probe.RollCandidate(202, 7);
            var c3 = probe.RollCandidate(101, 8);
            if (a.StatKey != b.StatKey || a.AppearanceKey != c3.AppearanceKey)
            {
                return false;
            }
            return probe.Roster.Count == 0
                && probe.Hire(0)
                && probe.Roster.Count == 1
                && probe.Candidates.Count == Econ.Data.StaffCandidates;
        }
    }
}
