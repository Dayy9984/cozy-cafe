using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using CozyCafe.Core.Modules;
using CozyCafe.Core.Save;

namespace CozyCafe.Core.Native
{
    /// <summary>
    /// Verifies the recorded native-OS evidence: native/&lt;os&gt;.json is a
    /// report written by the run orchestrator after a real OS player build
    /// was actually executed on that OS. This module checks the document —
    /// never asserts PASS itself: every recorded artifact path must exist
    /// and its recorded sha256 must match a fresh hash of the file on disk.
    /// A report that cannot fully verify downgrades the stage key to the
    /// document's own status — an honestly recorded BLOCKED/FAIL is carried
    /// through rather than hidden.
    /// </summary>
    public sealed class NativeReleaseModule : ModuleBase
    {
        public override string Name { get { return "native-release"; } }

        /// Evidence case ids the run must record and PASS for a PASS report.
        public static readonly string[] RequiredCases =
        {
            "transparency",
            "always_on_top",
            "click_through_recovery",
            "korean_ime",
            "focus",
            "dpi",
            "multi_monitor",
            "sleep",
        };

        public sealed class OsVerification
        {
            public string Os;
            public string ReportPath;
            public bool ReportExists;
            public string Status;              // report's own status string
            public bool StructureOk;           // schema + hashes all verified
            public bool BlockedDocumented;     // BLOCKED with reason + probes
            public string BuildSha256;         // verified build hash if any
            public int EvidenceVerified;       // evidence entries that hashed
            public int CasesPassed;            // required cases recorded PASS
            public readonly List<string> Errors = new List<string>();

            /// The stage CASE value this verification supports: true only on
            /// a fully verified PASS, the literal BLOCKED sentinel when the
            /// document honestly records the OS as unavailable, else false.
            public object CaseValue
            {
                get
                {
                    if (Status == "PASS" && StructureOk
                        && CasesPassed == RequiredCases.Length
                        && EvidenceVerified >= 2)
                    {
                        return true;
                    }
                    if (Status == "BLOCKED" && BlockedDocumented) return "BLOCKED";
                    return false;
                }
            }
        }

        /// Locates the workspace root: COZYCAFE_WORKSPACE_ROOT first, then an
        /// ancestor walk for the native/ directory — the same resolution the
        /// other core modules use for data files.
        public static string ResolveWorkspaceRoot()
        {
            string env = Environment.GetEnvironmentVariable(
                "COZYCAFE_WORKSPACE_ROOT");
            if (!string.IsNullOrEmpty(env)
                && Directory.Exists(Path.Combine(env, "native")))
            {
                return Path.GetFullPath(env);
            }
            var roots = new List<string>();
            try { roots.Add(Directory.GetCurrentDirectory()); }
            catch (Exception) { }
            try { roots.Add(AppContext.BaseDirectory); } catch (Exception) { }
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
                    if (Directory.Exists(Path.Combine(d.FullName, "native")))
                    {
                        return d.FullName;
                    }
                }
            }
            return null;
        }

        /// Runs the full verification of native/&lt;os&gt;.json. Never throws:
        /// a missing or broken report is a verification result, not an error.
        public OsVerification VerifyOs(string os)
        {
            var v = new OsVerification { Os = os, Status = "MISSING" };
            string root = ResolveWorkspaceRoot();
            if (root == null)
            {
                v.Errors.Add("workspace root with native/ not found");
                return v;
            }
            v.ReportPath = Path.Combine(root, "native", os + ".json");
            if (!File.Exists(v.ReportPath))
            {
                v.Errors.Add("report file missing: native/" + os + ".json");
                return v;
            }
            v.ReportExists = true;
            Dictionary<string, object> doc;
            try
            {
                doc = AsDict(MiniJson.Parse(
                    File.ReadAllText(v.ReportPath)));
            }
            catch (Exception e)
            {
                v.Errors.Add("report is not valid json: " + e.Message);
                return v;
            }
            if (doc == null)
            {
                v.Errors.Add("report is not a json object");
                return v;
            }
            if (AsStr(TryGet(doc, "os")) != os)
            {
                v.Errors.Add("report os field mismatch");
                return v;
            }
            v.Status = AsStr(TryGet(doc, "status"));
            if (v.Status != "PASS" && v.Status != "BLOCKED"
                && v.Status != "FAIL")
            {
                v.Errors.Add("report status not one of PASS/BLOCKED/FAIL: "
                    + v.Status);
                return v;
            }

            VerifyBuild(doc, root, v);
            int evVerified = VerifyEvidence(doc, root, v);
            v.EvidenceVerified = evVerified;
            v.CasesPassed = CountPassedCases(doc, v);

            if (v.Status == "PASS")
            {
                if (v.CasesPassed != RequiredCases.Length)
                {
                    v.Errors.Add("PASS report does not record PASS for all "
                        + RequiredCases.Length + " required cases");
                }
                if (evVerified < 2)
                {
                    v.Errors.Add("PASS report has fewer than 2 verified "
                        + "evidence artifacts (log + recording required)");
                }
                if (v.BuildSha256 == null)
                {
                    v.Errors.Add("PASS report build hash could not be "
                        + "verified against a real file");
                }
            }
            else if (v.Status == "BLOCKED")
            {
                string reason = AsStr(TryGet(doc, "blocked_reason"));
                var probes = AsList(TryGet(doc, "probes"));
                v.BlockedDocumented = reason.Length > 0
                    && probes != null && probes.Count > 0;
                if (!v.BlockedDocumented)
                {
                    v.Errors.Add("BLOCKED report lacks blocked_reason/probes");
                }
            }
            else // FAIL
            {
                string reason = AsStr(TryGet(doc, "fail_reason"));
                if (reason.Length == 0)
                {
                    v.Errors.Add("FAIL report lacks fail_reason");
                }
            }
            v.StructureOk = v.Errors.Count == 0;
            return v;
        }

        private static object TryGet(Dictionary<string, object> d,
            string key)
        {
            if (d == null) return null;
            object v;
            d.TryGetValue(key, out v);
            return v;
        }

        private static Dictionary<string, object> AsDict(object o)
        {
            return o as Dictionary<string, object>;
        }

        private static List<object> AsList(object o)
        {
            return o as List<object>;
        }

        private static string AsStr(object o)
        {
            return o == null ? ""
                : Convert.ToString(o,
                    System.Globalization.CultureInfo.InvariantCulture);
        }

        private void VerifyBuild(Dictionary<string, object> doc, string root,
            OsVerification v)
        {
            var build = AsDict(TryGet(doc, "build"));
            if (build == null)
            {
                if (v.Status == "PASS")
                    v.Errors.Add("PASS report has no build object");
                return;
            }
            string rel = AsStr(TryGet(build, "path"));
            string sha = AsStr(TryGet(build, "sha256"));
            if (rel.Length == 0 || sha.Length == 0)
            {
                if (v.Status == "PASS")
                    v.Errors.Add("build object lacks path/sha256");
                return;
            }
            if (sha.Length != 64 || !IsHex(sha))
            {
                v.Errors.Add("build sha256 is not 64 lowercase hex");
                return;
            }
            string abs = Path.GetFullPath(Path.Combine(root,
                rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(abs))
            {
                v.Errors.Add("build artifact missing on disk: " + rel);
                return;
            }
            string actual = Sha256Hex(abs);
            if (actual != sha)
            {
                v.Errors.Add("build sha256 mismatch: recorded " + sha
                    + " actual " + actual);
                return;
            }
            v.BuildSha256 = sha;
        }

        private int VerifyEvidence(Dictionary<string, object> doc,
            string root, OsVerification v)
        {
            var list = AsList(TryGet(doc, "evidence"));
            int ok = 0;
            if (list == null || list.Count == 0)
            {
                if (v.Status == "PASS")
                    v.Errors.Add("PASS report lists no evidence artifacts");
                return 0;
            }
            foreach (var e in list)
            {
                var ed = AsDict(e);
                if (ed == null)
                {
                    v.Errors.Add("evidence entry not an object");
                    continue;
                }
                string rel = AsStr(TryGet(ed, "path"));
                string sha = AsStr(TryGet(ed, "sha256"));
                if (rel.Length == 0 || sha.Length != 64 || !IsHex(sha))
                {
                    v.Errors.Add("evidence entry missing path/sha256: " + rel);
                    continue;
                }
                string abs = Path.GetFullPath(Path.Combine(root,
                    rel.Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(abs))
                {
                    v.Errors.Add("evidence file missing: " + rel);
                    continue;
                }
                if (new FileInfo(abs).Length == 0)
                {
                    v.Errors.Add("evidence file empty: " + rel);
                    continue;
                }
                string actual = Sha256Hex(abs);
                if (actual != sha)
                {
                    v.Errors.Add("evidence sha256 mismatch: " + rel);
                    continue;
                }
                ok++;
            }
            return ok;
        }

        private int CountPassedCases(Dictionary<string, object> doc,
            OsVerification v)
        {
            var cases = AsDict(TryGet(doc, "cases"));
            if (cases == null)
            {
                if (v.Status == "PASS")
                    v.Errors.Add("PASS report has no cases object");
                return 0;
            }
            int n = 0;
            foreach (var id in RequiredCases)
            {
                object raw;
                if (!cases.TryGetValue(id, out raw)) continue;
                var cd = AsDict(raw);
                if (cd == null) continue;
                if (AsStr(TryGet(cd, "status")) == "PASS") n++;
            }
            return n;
        }

        private static bool IsHex(string s)
        {
            foreach (char c in s)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                {
                    return false;
                }
            }
            return true;
        }

        public static string Sha256Hex(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = new FileStream(path, FileMode.Open,
                FileAccess.Read, FileShare.Read))
            {
                byte[] h = sha.ComputeHash(fs);
                var sb = new System.Text.StringBuilder(h.Length * 2);
                foreach (byte b in h)
                {
                    sb.Append(b.ToString("x2",
                        System.Globalization.CultureInfo.InvariantCulture));
                }
                return sb.ToString();
            }
        }

        /// Probe proves the verifier runs against the real report files;
        /// it deliberately does not require a PASS — probe health is the
        /// module's own operability, not the OS availability.
        protected override bool OnProbe()
        {
            try
            {
                var v = VerifyOs("windows");
                return v.ReportExists;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
