using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CozyCafe.Core.Economy;
using CozyCafe.Core.Layout;
using CozyCafe.Core.Modules;
using CozyCafe.Core.Research;
using CozyCafe.Core.Scene;
using CozyCafe.Core.Staff;

namespace CozyCafe.Core
{
    /// <summary>
    /// Typed accessors for persisted dictionaries, shared by every module's
    /// SaveState/RestoreState. MiniJson yields long for integral numbers and
    /// double otherwise; these coerce both (and numeric strings) or throw
    /// FormatException so a corrupt record is rejected, never silently read.
    /// </summary>
    internal static class SaveDoc
    {
        public static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v))
            {
                throw new FormatException("save: missing key '" + key + "'");
            }
            return v;
        }

        public static object GetOr(Dictionary<string, object> d, string key, object fallback)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v)) return fallback;
            return v;
        }

        public static Dictionary<string, object> Dict(object o)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) throw new FormatException("save: expected object, got " + o);
            return d;
        }

        public static List<object> List(object o)
        {
            var l = o as List<object>;
            if (l == null) throw new FormatException("save: expected array, got " + o);
            return l;
        }

        public static long Long(object o)
        {
            if (o is long) return (long)o;
            if (o is int) return (int)o;
            if (o is bool) return (bool)o ? 1 : 0;
            if (o is double)
            {
                var dv = (double)o;
                if (dv == Math.Floor(dv) && Math.Abs(dv) < 9e18) return (long)dv;
            }
            if (o is string)
            {
                long v;
                if (long.TryParse((string)o, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out v))
                {
                    return v;
                }
            }
            throw new FormatException("save: expected integer, got " + o);
        }

        public static double Double(object o)
        {
            if (o is double) return (double)o;
            if (o is float) return (float)o;
            if (o is long) return (long)o;
            if (o is int) return (int)o;
            if (o is string)
            {
                double v;
                if (double.TryParse((string)o, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out v))
                {
                    return v;
                }
            }
            throw new FormatException("save: expected number, got " + o);
        }

        public static bool Bool(object o)
        {
            if (o is bool) return (bool)o;
            if (o is long) return (long)o != 0;
            if (o is int) return (int)o != 0;
            if (o is string)
            {
                var s = (string)o;
                if (s == "true") return true;
                if (s == "false") return false;
            }
            throw new FormatException("save: expected bool, got " + o);
        }

        public static string Str(object o)
        {
            if (o == null) return null;
            return Convert.ToString(o, CultureInfo.InvariantCulture);
        }

        /// Sorted string list for deterministic serialization of a set.
        public static List<object> SortedStrings(IEnumerable<string> set)
        {
            var l = new List<string>(set);
            l.Sort(StringComparer.Ordinal);
            var o = new List<object>();
            foreach (var s in l) o.Add(s);
            return o;
        }
    }
}

namespace CozyCafe.Core.Save
{
    /// Result of one offline-settlement application.
    public sealed class OfflineResult
    {
        /// Settle window actually applied (already clamped to [0, cap]).
        public long Seconds;
        public long CoinsGained;
        /// Fractional gain in hundredths — reward smaller than one coin is
        /// still measured, never hidden.
        public long RemainderGained;
        /// nowUtc &lt; checkpoint_utc — the clock moved backwards.
        public bool Reversed;
        /// The settlement id was already in the applied ledger.
        public bool Duplicate;
    }

    /// <summary>
    /// File persistence for one save slot: primary + .bak backup + .tmp
    /// staging. Writes are staged in full, flushed, then atomically
    /// exchanged over the primary with the old primary becoming the backup
    /// (File.Replace). A crash therefore leaves either the complete old or
    /// the complete new primary plus the previous-good backup. Reads
    /// validate each candidate (malformed JSON, truncation, power-loss
    /// partial writes) and fall back primary -&gt; backup -&gt; none.
    /// </summary>
    public sealed class SaveStore
    {
        public enum LoadSource
        {
            None = 0,
            Primary = 1,
            Backup = 2
        }

        public readonly string PrimaryPath;
        private readonly Func<string, bool> validator;

        public string BackupPath { get { return PrimaryPath + ".bak"; } }
        public string TempPath { get { return PrimaryPath + ".tmp"; } }

        public SaveStore(string primaryPath)
            : this(primaryPath, null)
        {
        }

        /// `validator` decides whether a candidate file is a usable save —
        /// callers pass the document-level check so a schema-invalid or
        /// truncated primary is skipped for the backup.
        public SaveStore(string primaryPath, Func<string, bool> validator)
        {
            if (string.IsNullOrEmpty(primaryPath))
            {
                throw new ArgumentException("primaryPath required");
            }
            PrimaryPath = primaryPath;
            this.validator = validator ?? DefaultValidator;
        }

        private static bool DefaultValidator(string text)
        {
            try
            {
                return MiniJson.Parse(text) is Dictionary<string, object>;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// Atomically replaces the save contents. The new text is staged in
        /// .tmp and fsynced first; File.Replace then swaps it over the
        /// primary and stores the superseded primary as .bak in one
        /// filesystem operation, so a mid-write power loss can never leave
        /// a torn primary with no backup.
        public void WriteAtomic(string json)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(PrimaryPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(TempPath, json);
            using (var fs = new FileStream(
                TempPath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                fs.Flush(true);
            }
            if (File.Exists(PrimaryPath))
            {
                try
                {
                    File.Replace(TempPath, PrimaryPath, BackupPath);
                }
                catch (PlatformNotSupportedException)
                {
                    ManualReplace();
                }
            }
            else
            {
                File.Move(TempPath, PrimaryPath);
            }
        }

        private void ManualReplace()
        {
            File.Copy(PrimaryPath, BackupPath, true);
            File.Copy(TempPath, PrimaryPath, true);
            File.Delete(TempPath);
        }

        /// Reads the best usable candidate: primary first, backup on any
        /// failure (missing, empty, validator-rejected), else None. A stale
        /// .tmp leftover is never a candidate.
        public LoadSource TryRead(out string text)
        {
            string t;
            if (TryReadFile(PrimaryPath, out t)) { text = t; return LoadSource.Primary; }
            if (TryReadFile(BackupPath, out t)) { text = t; return LoadSource.Backup; }
            text = null;
            return LoadSource.None;
        }

        private bool TryReadFile(string path, out string text)
        {
            text = null;
            try
            {
                if (!File.Exists(path)) return false;
                string t = File.ReadAllText(path);
                if (t.Length == 0 || !validator(t)) return false;
                text = t;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// One versioned save document. Every field needed to resume the session
    /// byte-equal lives here: the settlement checkpoint (id + UTC), the
    /// monotonic runtime clock, the applied-settlement ledger, full module
    /// state dicts, the layout record and the private memo/todo lists.
    /// Parse() validates the whole shape — a malformed file, missing key or
    /// wrong type throws FormatException so the store can fail over to the
    /// backup; older schema versions migrate through Migrate().
    /// </summary>
    public sealed class SaveDocument
    {
        public const int CurrentVersion = 2;

        public int Version = CurrentVersion;
        public string SettlementId;
        public long CheckpointUtc;
        public double RuntimeClock;
        public long SaveSeq;
        public readonly List<string> AppliedSettlements = new List<string>();
        public Dictionary<string, object> Economy;
        public Dictionary<string, object> Research;
        public Dictionary<string, object> Staff;
        public object Layout;
        public List<object> Memos = new List<object>();
        public List<object> Todos = new List<object>();

        public string ToJson()
        {
            var d = new Dictionary<string, object>();
            d["save_version"] = (long)Version;
            d["settlement_id"] = SettlementId;
            d["checkpoint_utc"] = CheckpointUtc;
            d["runtime_clock"] = RuntimeClock;
            d["save_seq"] = SaveSeq;
            var ap = new List<object>();
            foreach (var a in AppliedSettlements) ap.Add(a);
            d["applied"] = ap;
            d["economy"] = Economy;
            d["research"] = Research;
            d["staff"] = Staff;
            d["layout"] = Layout;
            d["memos"] = Memos;
            d["todos"] = Todos;
            return MiniJson.ToJson(d);
        }

        public static bool IsValidJson(string text)
        {
            try
            {
                Parse(text);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// Strict parse: malformed JSON, a non-object root, an unsupported
        /// future version or any missing/mistyped required field throws —
        /// the caller (SaveStore/restore path) treats that as corruption.
        public static SaveDocument Parse(string json)
        {
            object root;
            try
            {
                root = MiniJson.Parse(json);
            }
            catch (Exception e)
            {
                throw new FormatException("save: malformed json", e);
            }
            var raw = root as Dictionary<string, object>;
            if (raw == null) throw new FormatException("save: root is not an object");
            long v = SaveDoc.Long(
                SaveDoc.GetOr(raw, "save_version", SaveDoc.GetOr(raw, "version", (long)1)));
            if (v > CurrentVersion)
            {
                throw new FormatException("save: unsupported future version " + v);
            }
            if (v < CurrentVersion) raw = Migrate(raw, v);

            var doc = new SaveDocument();
            doc.SettlementId = SaveDoc.Str(SaveDoc.Get(raw, "settlement_id"));
            doc.CheckpointUtc = SaveDoc.Long(SaveDoc.Get(raw, "checkpoint_utc"));
            doc.RuntimeClock = SaveDoc.Double(SaveDoc.Get(raw, "runtime_clock"));
            doc.SaveSeq = SaveDoc.Long(SaveDoc.Get(raw, "save_seq"));
            foreach (var s in SaveDoc.List(SaveDoc.Get(raw, "applied")))
            {
                doc.AppliedSettlements.Add(SaveDoc.Str(s));
            }
            doc.Economy = SaveDoc.Dict(SaveDoc.Get(raw, "economy"));
            doc.Research = SaveDoc.Dict(SaveDoc.Get(raw, "research"));
            doc.Staff = SaveDoc.Dict(SaveDoc.Get(raw, "staff"));
            doc.Layout = SaveDoc.Get(raw, "layout");
            doc.Memos = SaveDoc.List(SaveDoc.GetOr(raw, "memos", new List<object>()));
            doc.Todos = SaveDoc.List(SaveDoc.GetOr(raw, "todos", new List<object>()));
            return doc;
        }

        /// v1 -&gt; v2 migration. The v1 field set (pre-checkpoint schema:
        /// flat coins/machines/research/menu_levels plus a layout_json
        /// string) is mapped into the v2 module layout; fields the old
        /// version could not express get explicit defaults — nothing is
        /// dropped silently and unknown versions never reach here.
        private static Dictionary<string, object> Migrate(
            Dictionary<string, object> raw, long fromVersion)
        {
            if (fromVersion != 1)
            {
                throw new FormatException("save: no migration path from v" + fromVersion);
            }
            long utc = SaveDoc.Long(SaveDoc.Get(raw, "utc_saved"));
            long seq = SaveDoc.Long(SaveDoc.GetOr(raw, "settled_seq", (long)0));
            var done = SaveDoc.List(SaveDoc.GetOr(raw, "research_done", new List<object>()));

            var lines = new List<object>();
            var levels = SaveDoc.GetOr(raw, "menu_levels", null)
                as Dictionary<string, object>;
            if (levels != null)
            {
                foreach (var kv in levels)
                {
                    var rec = new Dictionary<string, object>();
                    rec["id"] = kv.Key;
                    rec["level"] = SaveDoc.Long(kv.Value);
                    rec["next_at"] = -1.0; // reschedule from the cycle on restore
                    lines.Add(rec);
                }
            }

            var econ = new Dictionary<string, object>();
            econ["coins"] = SaveDoc.Long(SaveDoc.Get(raw, "coins"));
            econ["rem"] = (long)0;
            econ["clock"] = 0.0;
            econ["staff_pct"] = (long)0;
            econ["charged"] = (long)0;
            econ["sale_seq"] = seq;
            econ["neg"] = false;
            econ["machines"] = SaveDoc.List(
                SaveDoc.GetOr(raw, "machines_owned", new List<object>()));
            econ["research_done"] = done;
            econ["settled"] = new List<object>();
            econ["lines"] = lines;

            var rs = new Dictionary<string, object>();
            rs["clock"] = 0.0;
            rs["rate"] = 1.0;
            rs["anchor"] = 0.0;
            rs["count"] = (long)done.Count;
            rs["flags"] = new List<object>();
            rs["queue"] = new List<object>();
            rs["active"] = null;

            var st = new Dictionary<string, object>();
            st["gen_seed"] = SaveDoc.Long(SaveDoc.GetOr(raw, "staff_seed", (long)0));
            st["next_id"] = (long)0;
            st["panel"] = false;
            st["roster"] = new List<object>();
            st["candidates"] = new List<object>();

            object layoutObj;
            try
            {
                layoutObj = MiniJson.Parse(
                    SaveDoc.Str(SaveDoc.Get(raw, "layout_json")));
            }
            catch (Exception e)
            {
                throw new FormatException("save: v1 layout_json is not valid json", e);
            }

            var d = new Dictionary<string, object>();
            d["save_version"] = (long)CurrentVersion;
            d["settlement_id"] = "v1@" + utc.ToString(CultureInfo.InvariantCulture);
            d["checkpoint_utc"] = utc;
            d["runtime_clock"] = 0.0;
            d["save_seq"] = seq;
            d["applied"] = new List<object>();
            d["economy"] = econ;
            d["research"] = rs;
            d["staff"] = st;
            d["layout"] = layoutObj;
            d["memos"] = new List<object>();
            d["todos"] = new List<object>();
            return d;
        }
    }

    /// <summary>
    /// One cafe timeline: economy, research lab, hire office and room layout
    /// driven through a single monotonic runtime clock. Advance() steps
    /// through every event boundary in chronological order — each pending
    /// sale event, each research completion and each reserved-funds
    /// threshold crossing — so a chunked online run and a one-shot offline
    /// catch-up traverse the identical event sequence. Staff changes are
    /// explicit boundaries (Hire): past work is already billed at the old
    /// rates; from the boundary the new roster's bonuses apply.
    /// </summary>
    public sealed class CafeSession
    {
        public readonly MvpData Data;
        public EconomyModule Econ { get; private set; }
        public ResearchModule Lab { get; private set; }
        public StaffModule Staff { get; private set; }
        public LayoutModule Layout { get; private set; }

        /// Monotonic runtime seconds for this timeline. Advances only
        /// through Advance() and restore; never fed by wall time.
        public double RuntimeClock { get; private set; }

        /// UTC of the last settled checkpoint — the origin of the next
        /// offline interval. Persists through saves.
        public long CheckpointUtc { get; private set; }

        /// Settlement id carried by the last built/restored document.
        public string PendingSettlementId { get; private set; }

        public long SaveSeq { get; private set; }

        /// Private save data (never exported to public presets).
        public readonly List<object> Memos = new List<object>();
        public readonly List<object> Todos = new List<object>();

        private readonly HashSet<string> applied = new HashSet<string>();

        public long OfflineCapSeconds
        {
            get { return Data != null ? Data.OfflineCapSeconds : 86400; }
        }

        private CafeSession(MvpData data)
        {
            Data = data;
        }

        public static CafeSession CreateStartup(
            MvpData data, long staffSeed, int roomW, int roomH)
        {
            var s = new CafeSession(data);
            s.Econ = EconomyModule.CreateStartup(data, 0);
            s.Lab = new ResearchModule(s.Econ);
            s.Staff = new StaffModule(s.Econ, staffSeed);
            s.Layout = new LayoutModule(
                new GameScene { Room = new RoomGrid(roomW, roomH) });
            return s;
        }

        /// Advances the runtime clock by `seconds`, processing each event
        /// boundary in order. The next pending sale or research completion
        /// caps every step, so events are never skipped or reordered; after
        /// each step the lab retries the queue front (a sale may have just
        /// crossed the reserved-funds threshold). Called per-frame online
        /// and once with the clamped window offline — same code, same order.
        public double Advance(double seconds)
        {
            if (seconds <= 0) return 0;
            double target = RuntimeClock + seconds;
            long guard = 0;
            while (RuntimeClock < target)
            {
                double step = target - RuntimeClock;
                double ds = Econ.NextSaleDelta();
                double dr = Lab.NextCompletionDelta();
                if (ds < step) step = ds;
                if (dr < step) step = dr;
                if (step <= 0) break; // pending events are strictly future-dated
                // Wallet events run first so a same-instant completion or
                // queue start already sees the credited coins; the lab
                // anchors queue starts at the step's end boundary.
                Econ.SimulateSeconds(step);
                Lab.SimulateStep(step);
                RuntimeClock += step;
                Lab.TryStartQueued();
                if (++guard > 100000000)
                {
                    throw new InvalidOperationException("session advance stalled");
                }
            }
            return seconds;
        }

        /// Staff-change event boundary: work simulated so far was billed by
        /// the previous roster; from now on sales settle with the new
        /// combined sales bonus and the active research drains its
        /// remaining base work at the new research rate.
        public bool Hire(int candidateIndex)
        {
            if (!Staff.Hire(candidateIndex)) return false;
            Econ.StaffSalesBonusPct = Staff.TotalSalesBonusPct;
            Lab.SetWorkRate(1.0 + Staff.TotalResearchBonusPct / 100.0);
            return true;
        }

        /// Snapshots the whole timeline into a versioned document: a fresh
        /// settlement id marks this checkpoint so its offline interval can
        /// be applied at most once after restore.
        public SaveDocument BuildDocument(long nowUtc)
        {
            SaveSeq++;
            var doc = new SaveDocument();
            doc.SaveSeq = SaveSeq;
            doc.SettlementId = "s" + SaveSeq.ToString(CultureInfo.InvariantCulture)
                + "@" + nowUtc.ToString(CultureInfo.InvariantCulture);
            doc.CheckpointUtc = nowUtc;
            doc.RuntimeClock = RuntimeClock;
            foreach (var id in applied) doc.AppliedSettlements.Add(id);
            doc.Economy = Econ.SaveState();
            doc.Research = Lab.SaveState();
            doc.Staff = Staff.SaveState();
            doc.Layout = MiniJson.Parse(Layout.SaveLayout());
            foreach (var m in Memos) doc.Memos.Add(m);
            foreach (var t in Todos) doc.Todos.Add(t);
            CheckpointUtc = nowUtc;
            PendingSettlementId = doc.SettlementId;
            return doc;
        }

        /// Builds the document and persists it through the atomic store.
        public SaveDocument WriteCheckpoint(SaveStore store, long nowUtc)
        {
            var doc = BuildDocument(nowUtc);
            store.WriteAtomic(doc.ToJson());
            return doc;
        }

        /// Replaces this session's entire state with the document's —
        /// timeline clock, settlement checkpoint, pending id, the applied
        /// ledger and every module record included.
        public void Restore(SaveDocument doc)
        {
            Econ.RestoreState(doc.Economy);
            Lab.RestoreState(doc.Research);
            Staff.RestoreState(doc.Staff);
            Layout.LoadLayout(MiniJson.ToJson(doc.Layout));
            RuntimeClock = doc.RuntimeClock;
            CheckpointUtc = doc.CheckpointUtc;
            PendingSettlementId = doc.SettlementId;
            SaveSeq = doc.SaveSeq;
            applied.Clear();
            foreach (var id in doc.AppliedSettlements) applied.Add(id);
            Memos.Clear();
            foreach (var m in doc.Memos) Memos.Add(m);
            Todos.Clear();
            foreach (var t in doc.Todos) Todos.Add(t);
        }

        /// Applies the document's pending offline interval under its
        /// settlement id, at most once per id: elapsed UTC is measured from
        /// the stored checkpoint, a backwards clock yields zero reward, the
        /// window clamps to data.offline_cap_seconds, and the catch-up runs
        /// through the same Advance path as continuous play.
        public OfflineResult SettleOffline(SaveDocument doc, long nowUtc)
        {
            var r = new OfflineResult();
            long elapsed = nowUtc - doc.CheckpointUtc;
            r.Reversed = elapsed < 0;
            long settle = elapsed <= 0 ? 0 : Math.Min(elapsed, OfflineCapSeconds);
            r.Seconds = settle;
            if (settle <= 0) return r;
            // The id is marked before applying so a crash mid-settle can
            // never let the same checkpoint credit twice.
            if (doc.SettlementId != null && !applied.Add(doc.SettlementId))
            {
                r.Duplicate = true;
                r.Seconds = 0;
                return r;
            }
            if (Math.Abs(RuntimeClock - doc.RuntimeClock) > 0.0000001)
            {
                throw new InvalidOperationException(
                    "settle requires the session restored to the document's checkpoint");
            }
            long c0 = Econ.Coins;
            long h0 = Econ.RemainderHundredths;
            Advance(settle);
            r.CoinsGained = Econ.Coins - c0;
            r.RemainderGained = Econ.RemainderHundredths - h0;
            // The consumed interval is never credited again, including the
            // part discarded by the cap.
            if (CheckpointUtc < nowUtc) CheckpointUtc = nowUtc;
            return r;
        }

        /// Shareable preset — cosmetic/layout content only, allow-listed
        /// keys. Wallet, research, staff stats, memos, local paths and auth
        /// never enter this structure by construction.
        public Dictionary<string, object> ExportPublicPreset(string name)
        {
            var p = new Dictionary<string, object>();
            p["preset_version"] = (long)1;
            p["name"] = name ?? "";
            p["layout"] = MiniJson.Parse(Layout.SaveLayout());
            var agents = new List<object>();
            foreach (var a in Layout.Scene.Agents) agents.Add((long)a.PresetId);
            p["agent_presets"] = agents;
            var skin = new Dictionary<string, object>();
            skin["tile_variant"] = (long)0;
            skin["wall_variant"] = (long)0;
            p["skin"] = skin;
            return p;
        }

        private static readonly string[] PrivateFieldNames =
        {
            "coins", "wallet", "remainder", "charged", "research", "staff",
            "stat", "bonus", "memo", "todos", "path", "auth", "token",
            "checkpoint", "utc", "clock", "secret", "credential", "password",
            "settlement", "save_seq"
        };

        /// True when any key anywhere in the preset tree names a private
        /// field — the recursive audit a public share path must pass.
        public static bool PresetContainsPrivateFields(object node)
        {
            var d = node as Dictionary<string, object>;
            if (d != null)
            {
                foreach (var kv in d)
                {
                    var k = kv.Key.ToLowerInvariant();
                    for (int i = 0; i < PrivateFieldNames.Length; i++)
                    {
                        if (k == PrivateFieldNames[i]
                            || k.IndexOf("auth", StringComparison.Ordinal) >= 0
                            || k.IndexOf("token", StringComparison.Ordinal) >= 0
                            || k.IndexOf("secret", StringComparison.Ordinal) >= 0)
                        {
                            return true;
                        }
                    }
                    if (PresetContainsPrivateFields(kv.Value)) return true;
                }
                return false;
            }
            var l = node as List<object>;
            if (l != null)
            {
                foreach (var item in l)
                {
                    if (PresetContainsPrivateFields(item)) return true;
                }
            }
            return false;
        }

        /// Counts every field that differs from `other` — the honest
        /// offline-vs-online distance across wallet, timers, research,
        /// staff and layout. Zero means the two timelines are identical.
        public long StateDifference(CafeSession other)
        {
            long diff = 0;
            if (Econ.Coins != other.Econ.Coins) diff++;
            if (Econ.RemainderHundredths != other.Econ.RemainderHundredths) diff++;
            if (Econ.Clock != other.Econ.Clock) diff++;
            if (Econ.StaffSalesBonusPct != other.Econ.StaffSalesBonusPct) diff++;
            if (Econ.TotalCharged != other.Econ.TotalCharged) diff++;
            diff += SetDifference(Econ.OwnedMachines, other.Econ.OwnedMachines);
            diff += SetDifference(Econ.CompletedResearch, other.Econ.CompletedResearch);
            if (Econ.Lines.Count != other.Econ.Lines.Count) diff++;
            foreach (var kv in Econ.Lines)
            {
                MenuLine ol;
                if (!other.Econ.Lines.TryGetValue(kv.Key, out ol)) { diff++; continue; }
                if (kv.Value.Level != ol.Level) diff++;
                if (kv.Value.NextSaleAt != ol.NextSaleAt) diff++;
            }
            if (Lab.Clock != other.Lab.Clock) diff++;
            if (Lab.WorkRate != other.Lab.WorkRate) diff++;
            if (Lab.CompletedCount != other.Lab.CompletedCount) diff++;
            if ((Lab.Active == null) != (other.Lab.Active == null)) diff++;
            else if (Lab.Active != null)
            {
                if (Lab.Active.Id != other.Lab.Active.Id) diff++;
                if (Lab.ActiveRemainingWork != other.Lab.ActiveRemainingWork) diff++;
                if (Lab.ActiveEndsAt != other.Lab.ActiveEndsAt) diff++;
            }
            if (Lab.Queue.Count != other.Lab.Queue.Count) diff++;
            else
            {
                for (int i = 0; i < Lab.Queue.Count; i++)
                {
                    if (Lab.Queue[i].Id != other.Lab.Queue[i].Id) diff++;
                }
            }
            diff += SetDifference(Lab.UnlockedFlags, other.Lab.UnlockedFlags);
            if (Staff.Roster.Count != other.Staff.Roster.Count) diff++;
            else
            {
                for (int i = 0; i < Staff.Roster.Count; i++)
                {
                    if (Staff.Roster[i].Serialize() != other.Staff.Roster[i].Serialize())
                    {
                        diff++;
                    }
                }
            }
            if (Staff.Candidates.Count != other.Staff.Candidates.Count) diff++;
            else
            {
                for (int i = 0; i < Staff.Candidates.Count; i++)
                {
                    if (Staff.Candidates[i].Serialize()
                        != other.Staff.Candidates[i].Serialize())
                    {
                        diff++;
                    }
                }
            }
            if (RuntimeClock != other.RuntimeClock) diff++;
            if (Layout.SaveLayout() != other.Layout.SaveLayout()) diff++;
            return diff;
        }

        private static long SetDifference(HashSet<string> a, HashSet<string> b)
        {
            long n = 0;
            foreach (var s in a) if (!b.Contains(s)) n++;
            foreach (var s in b) if (!a.Contains(s)) n++;
            return n;
        }
    }
}
