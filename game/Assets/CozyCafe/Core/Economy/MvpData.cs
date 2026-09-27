using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CozyCafe.Core.Economy
{
    public sealed class MenuDef
    {
        public string Id;
        public string Name;
        public long Price;
        public long CycleSeconds;
        public readonly List<string> Machines = new List<string>();
        public readonly List<string> Research = new List<string>();
    }

    public sealed class ResearchDef
    {
        public string Id;
        public string Name;
        public long Cost;
        public long Seconds;
        public readonly List<string> Prerequisites = new List<string>();
        public string Unlocks;
    }

    public sealed class UpgradeDef
    {
        public long BaseMinutes;
        public long RatioNumerator;
        public long RatioDenominator;
        public long Cap;
        public readonly SortedList<long, long> Milestones = new SortedList<long, long>();

        /// Stage multiplier for a level: the value of the highest milestone
        /// at or below the level (Lv1-9 x1, 10-24 x2, 25-49 x4, 50-cap x8).
        public long MultiplierFor(int level)
        {
            long m = 1;
            foreach (var kv in Milestones)
            {
                if (level >= kv.Key) m = kv.Value;
                else break;
            }
            return m;
        }
    }

    /// <summary>
    /// Runtime-loaded copy of data/mvp.json. Every economy number the game
    /// uses flows through this object — tuning lives in the data file (and a
    /// DECISIONS.md record), never as code constants.
    /// </summary>
    public sealed class MvpData
    {
        public string SourcePath;
        public long InitialCoins;
        public readonly List<string> InitialOwnedMachines = new List<string>();
        public long OfflineCapSeconds;
        public long ResearchSlots;
        public long ResearchQueue;
        public long StaffFree;
        public long StaffMax;
        public long StaffCost;
        public long StaffStatPctMin;
        public long StaffStatPctMax;
        public readonly UpgradeDef Upgrade = new UpgradeDef();
        public readonly List<MenuDef> Menus = new List<MenuDef>();
        public readonly List<ResearchDef> Research = new List<ResearchDef>();
        public readonly List<string> Machines = new List<string>();

        public static MvpData Load()
        {
            string path = ResolvePath();
            if (path == null)
            {
                throw new FileNotFoundException(
                    "data/mvp.json not found from cwd, app base, or COZYCAFE_MVP_JSON");
            }
            var d = Parse(File.ReadAllText(path));
            d.SourcePath = path;
            return d;
        }

        public static MvpData TryLoad()
        {
            try { return Load(); }
            catch (Exception) { return null; }
        }

        public MenuDef FindMenu(string id)
        {
            foreach (var m in Menus) if (m.Id == id) return m;
            return null;
        }

        /// <summary>
        /// Locates data/mvp.json by walking ancestors of the process working
        /// directory and the app base directory (covers the CLI host run from
        /// the workspace root and the editor host run under -projectPath), or
        /// the COZYCAFE_MVP_JSON override.
        /// </summary>
        public static string ResolvePath()
        {
            string env = Environment.GetEnvironmentVariable("COZYCAFE_MVP_JSON");
            if (!string.IsNullOrEmpty(env) && File.Exists(env))
            {
                return Path.GetFullPath(env);
            }
            var roots = new List<string>();
            try { roots.Add(Directory.GetCurrentDirectory()); } catch (Exception) { }
            try { roots.Add(AppContext.BaseDirectory); } catch (Exception) { }
            try { roots.Add(AppDomain.CurrentDomain.BaseDirectory); } catch (Exception) { }
            var seen = new HashSet<string>();
            foreach (var r in roots)
            {
                if (string.IsNullOrEmpty(r)) continue;
                DirectoryInfo d;
                try { d = new DirectoryInfo(r); }
                catch (Exception) { continue; }
                for (; d != null; d = d.Parent)
                {
                    if (!seen.Add(d.FullName)) continue;
                    string p = Path.Combine(d.FullName, "data", "mvp.json");
                    if (File.Exists(p)) return p;
                }
            }
            return null;
        }

        public static MvpData Parse(string json)
        {
            var root = AsDict(MiniJson.Parse(json));
            var d = new MvpData();
            foreach (var m in AsList(Get(root, "machines"))) d.Machines.Add(AsString(m));

            var initial = AsDict(Get(root, "initial"));
            d.InitialCoins = AsLong(Get(initial, "coins"));
            foreach (var m in AsList(Get(initial, "owned_machines")))
            {
                d.InitialOwnedMachines.Add(AsString(m));
            }

            d.OfflineCapSeconds = AsLong(Get(root, "offline_cap_seconds"));
            d.ResearchSlots = AsLong(Get(root, "research_slots"));
            d.ResearchQueue = AsLong(Get(root, "research_queue"));

            var staff = AsDict(Get(root, "staff"));
            d.StaffFree = AsLong(Get(staff, "free"));
            d.StaffMax = AsLong(Get(staff, "max"));
            d.StaffCost = AsLong(Get(staff, "cost"));
            var pct = AsList(Get(staff, "stat_percent"));
            d.StaffStatPctMin = AsLong(pct[0]);
            d.StaffStatPctMax = AsLong(pct[1]);

            var up = AsDict(Get(root, "upgrade"));
            d.Upgrade.BaseMinutes = AsLong(Get(up, "base_minutes"));
            d.Upgrade.RatioNumerator = AsLong(Get(up, "ratio_numerator"));
            d.Upgrade.RatioDenominator = AsLong(Get(up, "ratio_denominator"));
            d.Upgrade.Cap = AsLong(Get(up, "cap"));
            foreach (var kv in AsDict(Get(up, "milestones")))
            {
                d.Upgrade.Milestones[long.Parse(kv.Key, CultureInfo.InvariantCulture)]
                    = AsLong(kv.Value);
            }

            foreach (var mo in AsList(Get(root, "menus")))
            {
                var md = AsDict(mo);
                var menu = new MenuDef();
                menu.Id = AsString(Get(md, "id"));
                menu.Name = AsString(Get(md, "name"));
                menu.Price = AsLong(Get(md, "price"));
                menu.CycleSeconds = AsLong(Get(md, "cycle_seconds"));
                foreach (var m in AsList(Get(md, "machines"))) menu.Machines.Add(AsString(m));
                foreach (var r in AsList(Get(md, "research"))) menu.Research.Add(AsString(r));
                d.Menus.Add(menu);
            }

            foreach (var ro in AsList(Get(root, "research")))
            {
                var rd = AsDict(ro);
                var r = new ResearchDef();
                r.Id = AsString(Get(rd, "id"));
                r.Name = AsString(Get(rd, "name"));
                r.Cost = AsLong(Get(rd, "cost"));
                r.Seconds = AsLong(Get(rd, "seconds"));
                foreach (var p in AsList(Get(rd, "prerequisites"))) r.Prerequisites.Add(AsString(p));
                r.Unlocks = AsString(Get(rd, "unlocks"));
                d.Research.Add(r);
            }
            return d;
        }

        private static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v))
            {
                throw new FormatException("mvp.json missing key: " + key);
            }
            return v;
        }

        private static Dictionary<string, object> AsDict(object o)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) throw new FormatException("mvp.json expected object");
            return d;
        }

        private static List<object> AsList(object o)
        {
            var l = o as List<object>;
            if (l == null) throw new FormatException("mvp.json expected array");
            return l;
        }

        private static long AsLong(object o)
        {
            if (o is long) return (long)o;
            if (o is int) return (int)o;
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
            throw new FormatException("mvp.json expected integer, got " + o);
        }

        private static string AsString(object o)
        {
            return Convert.ToString(o, CultureInfo.InvariantCulture);
        }
    }
}
