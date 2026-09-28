using System;
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

        /// Progress is kept as remaining_base_work — nominal seconds of
        /// work still owed at rate 1.0 — billed down continuously at
        /// WorkRate. A mid-run rate change settles the elapsed segment at
        /// the old rate and re-anchors, so the last rate always bills the
        /// rest correctly instead of dropping the change.
        public double ActiveRemainingWork { get; private set; }
        public double WorkRate { get; private set; }
        private double rateAnchorClock;

        public ResearchModule(Economy.EconomyModule econ)
        {
            Econ = econ;
            WorkRate = 1.0;
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
            ActiveRemainingWork = next.Seconds;
            rateAnchorClock = Clock;
            ActiveEndsAt = Clock + next.Seconds / WorkRate;
        }

        /// Public retry for the queue front — used at every funds or
        /// prerequisite boundary. Returns true when an entry is running
        /// after the call.
        public bool TryStartQueued()
        {
            TryStartNext();
            return Active != null;
        }

        /// Seconds until the active entry completes (PositiveInfinity when
        /// the lab is idle) — the session's next research boundary.
        public double NextCompletionDelta()
        {
            if (Active == null) return double.PositiveInfinity;
            double d = ActiveEndsAt - Clock;
            return d < 0 ? 0 : d;
        }

        /// Rate-change boundary (e.g. the staff roster changed): the
        /// elapsed segment drains remaining_base_work at the OLD rate, the
        /// anchor moves to now, and the remaining work is re-billed by the
        /// new rate — mid-run speed changes are never lost.
        public void SetWorkRate(double rate)
        {
            if (rate <= 0) rate = 1.0;
            if (Active != null)
            {
                ActiveRemainingWork -= (Clock - rateAnchorClock) * WorkRate;
                if (ActiveRemainingWork < 0) ActiveRemainingWork = 0;
                ActiveEndsAt = Clock + ActiveRemainingWork / rate;
                rateAnchorClock = Clock;
            }
            WorkRate = rate;
        }

        /// Advances the lab clock; a finished entry applies its unlock, then
        /// the front of the line tries to start inside the same time budget.
        /// A zero-length step still completes every entry already due.
        public void SimulateSeconds(double seconds)
        {
            if (seconds < 0) return;
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

        /// Session-stepping variant used by CafeSession's interleaved
        /// advance: the queue front is evaluated at the interval's END
        /// boundary, so a start anchors at the event time when the funds
        /// (or prerequisite) actually became sufficient — never at the
        /// stale pre-step clock. Completions still land on their exact
        /// scheduled instants inside the interval. A zero-length step still
        /// completes every entry already due — the session flush path uses
        /// it for events dated at-or-before the current clock.
        public void SimulateStep(double seconds)
        {
            if (seconds < 0) return;
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
            TryStartNext();
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

        /// Deterministic state record: lab clock, work rate + anchor,
        /// the active entry's remaining base work and scheduled end, the
        /// wait line, unlock flags and the completion count.
        public Dictionary<string, object> SaveState()
        {
            var d = new Dictionary<string, object>();
            d["clock"] = Clock;
            d["rate"] = WorkRate;
            d["anchor"] = rateAnchorClock;
            d["count"] = (long)CompletedCount;
            d["flags"] = SaveDoc.SortedStrings(UnlockedFlags);
            var q = new List<object>();
            foreach (var r in Queue) q.Add(r.Id);
            d["queue"] = q;
            if (Active == null)
            {
                d["active"] = null;
            }
            else
            {
                var a = new Dictionary<string, object>();
                a["id"] = Active.Id;
                a["remaining"] = ActiveRemainingWork;
                a["ends_at"] = ActiveEndsAt;
                d["active"] = a;
            }
            return d;
        }

        /// Replaces the whole lab state from a save record.
        public void RestoreState(Dictionary<string, object> d)
        {
            Clock = SaveDoc.Double(SaveDoc.Get(d, "clock"));
            WorkRate = SaveDoc.Double(SaveDoc.Get(d, "rate"));
            rateAnchorClock = SaveDoc.Double(SaveDoc.Get(d, "anchor"));
            CompletedCount = (int)SaveDoc.Long(SaveDoc.Get(d, "count"));
            UnlockedFlags.Clear();
            foreach (var f in SaveDoc.List(SaveDoc.Get(d, "flags")))
            {
                UnlockedFlags.Add(SaveDoc.Str(f));
            }
            Queue.Clear();
            foreach (var o in SaveDoc.List(SaveDoc.Get(d, "queue")))
            {
                var def = Find(SaveDoc.Str(o));
                if (def == null)
                {
                    throw new FormatException("save: unknown queued research");
                }
                Queue.Add(def);
            }
            var active = SaveDoc.Get(d, "active") as Dictionary<string, object>;
            if (active == null)
            {
                Active = null;
                ActiveRemainingWork = 0;
                ActiveEndsAt = 0;
            }
            else
            {
                var def = Find(SaveDoc.Str(SaveDoc.Get(active, "id")));
                if (def == null)
                {
                    throw new FormatException("save: unknown active research");
                }
                Active = def;
                ActiveRemainingWork = SaveDoc.Double(SaveDoc.Get(active, "remaining"));
                ActiveEndsAt = SaveDoc.Double(SaveDoc.Get(active, "ends_at"));
            }
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
