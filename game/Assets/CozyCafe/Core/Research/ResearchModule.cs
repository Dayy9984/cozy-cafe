using System.Collections.Generic;
using CozyCafe.Core.Economy;
using CozyCafe.Core.Modules;

namespace CozyCafe.Core.Research
{
    /// <summary>
    /// Queued research lab per data/mvp.json and planning/03: one running
    /// slot plus a bounded user-ordered wait line. Reservations start
    /// automatically in queue order only when every prerequisite research is
    /// complete (AND) and the wallet covers the data-file cost, which is
    /// charged exactly once at start. A blocked front entry never lets a
    /// later one jump the line; a queued entry can still be cancelled or
    /// reordered, a running one cannot. Completion applies its unlock as a
    /// permanent flag — machine-class unlocks join OwnedMachines on the
    /// economy state, ingredient-class unlocks (milk, chocolate) join
    /// UnlockedFlags. InventoryItems is the consumable item store: the MVP
    /// has none, so it stays empty and research results never land there.
    /// </summary>
    public sealed class ResearchModule : ModuleBase
    {
        public override string Name { get { return "research"; } }

        public readonly Economy.EconomyModule Econ;
        public readonly List<ResearchDef> Queue = new List<ResearchDef>();
        public readonly HashSet<string> UnlockedFlags = new HashSet<string>();
        public readonly HashSet<string> InventoryItems = new HashSet<string>();
        public ResearchDef Active { get; private set; }
        public double Clock { get; private set; }
        public double ActiveEndsAt { get; private set; }
        public int CompletedCount { get; private set; }

        public ResearchModule(Economy.EconomyModule econ)
        {
            Econ = econ;
        }

        public bool IsCompleted(ResearchDef r)
        {
            return Econ.CompletedResearch.Contains(r.Id);
        }

        /// User-ordered reservation. Any not-yet-done, not-yet-held entry may
        /// be queued even while its prerequisites or the coins are missing;
        /// the entry only starts once the AND-gate opens and the wallet
        /// covers the charge. The wait line holds at most data.research_queue.
        public bool Reserve(string id)
        {
            var def = Find(id);
            if (def == null || IsCompleted(def) || Active == def
                || Queue.Contains(def) || Queue.Count >= Econ.Data.ResearchQueue)
            {
                return false;
            }
            Queue.Add(def);
            TryStartNext();
            return true;
        }

        /// Queued entries can be cancelled before they start; a running one
        /// cannot (default per spec).
        public bool CancelQueued(string id)
        {
            for (int i = 0; i < Queue.Count; i++)
            {
                if (Queue[i].Id == id)
                {
                    Queue.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// Reorders the wait line; positions outside the list are clamped.
        public bool Reorder(string id, int newIndex)
        {
            for (int i = 0; i < Queue.Count; i++)
            {
                if (Queue[i].Id == id)
                {
                    var def = Queue[i];
                    Queue.RemoveAt(i);
                    if (newIndex < 0) newIndex = 0;
                    if (newIndex > Queue.Count) newIndex = Queue.Count;
                    Queue.Insert(newIndex, def);
                    TryStartNext();
                    return true;
                }
            }
            return false;
        }

        /// All prerequisites must hold at once (AND); the cost is charged
        /// exactly once, at start. Only the queue front is ever considered —
        /// when it is blocked, nothing behind it begins.
        private void TryStartNext()
        {
            if (Active != null || Queue.Count == 0) return;
            var next = Queue[0];
            foreach (var p in next.Prerequisites)
            {
                if (!Econ.CompletedResearch.Contains(p)) return;
            }
            if (!Econ.TryCharge(next.Cost)) return;
            Queue.RemoveAt(0);
            Active = next;
            ActiveEndsAt = Clock + next.Seconds;
        }

        /// Advances the lab clock; a finished entry applies its unlock, then
        /// the front of the line tries to start inside the same time budget.
        public void SimulateSeconds(double seconds)
        {
            if (seconds <= 0) return;
            TryStartNext();
            double horizon = Clock + seconds;
            while (Active != null && ActiveEndsAt <= horizon)
            {
                Clock = ActiveEndsAt;
                var done = Active;
                Active = null;
                ApplyCompletion(done);
                TryStartNext();
            }
            Clock = horizon;
        }

        /// Completion is an event boundary: the research id becomes a
        /// permanent completed flag, its unlock lands as a machine or an
        /// ingredient flag — never an inventory item — and newly ownable
        /// menus auto-open at Lv1 with no open cost.
        private void ApplyCompletion(ResearchDef r)
        {
            Econ.CompletedResearch.Add(r.Id);
            if (Econ.Data.Machines.Contains(r.Unlocks))
            {
                Econ.OwnedMachines.Add(r.Unlocks);
            }
            else
            {
                UnlockedFlags.Add(r.Unlocks);
            }
            CompletedCount++;
            Econ.SyncOwnedMenus();
        }

        public ResearchDef Find(string id)
        {
            foreach (var r in Econ.Data.Research) if (r.Id == id) return r;
            return null;
        }

        /// Probe drives the real queue path on a throwaway economy state:
        /// reserve R01, earn the coins through the sale loop, run the lab
        /// clock, and require the ice machine to land as a permanent flag.
        protected override bool OnProbe()
        {
            if (Econ == null || Econ.Data == null || Econ.Data.Research.Count == 0)
            {
                return false;
            }
            var probeEcon = Economy.EconomyModule.CreateStartup(Econ.Data, 0);
            var probe = new ResearchModule(probeEcon);
            if (!probe.Reserve("R01")) return false;
            probeEcon.SimulateSeconds(10.0 * 60.0);
            probe.SimulateSeconds(200.0);
            return probeEcon.CompletedResearch.Contains("R01")
                && probeEcon.OwnedMachines.Contains("ice")
                && !probe.InventoryItems.Contains("ice")
                && probe.Active == null;
        }
    }
}
