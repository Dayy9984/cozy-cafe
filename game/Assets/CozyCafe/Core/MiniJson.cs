using System.Collections;
using System.Globalization;
using System.Text;

namespace CozyCafe.Core
{
    /// <summary>
    /// Dependency-free JSON writer used for CASE values and small snapshots so
    /// the same source compiles under the editor host (netstandard2.1 profile)
    /// and the .NET 8 CLI host without external packages.
    /// </summary>
    public static class MiniJson
    {
        public static string ToJson(object value)
        {
            var sb = new StringBuilder();
            Write(value, sb);
            return sb.ToString();
        }

        private static void Write(object v, StringBuilder sb)
        {
            if (v == null) { sb.Append("null"); return; }
            switch (v)
            {
                case bool b:
                    sb.Append(b ? "true" : "false");
                    return;
                case string s:
                    WriteString(s, sb);
                    return;
                case IDictionary d:
                    WriteDict(d, sb);
                    return;
                case IEnumerable e:
                    WriteList(e, sb);
                    return;
                case double dbl:
                    sb.Append(dbl.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case float f:
                    sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case System.IConvertible c:
                    sb.Append(System.Convert.ToString(c, CultureInfo.InvariantCulture));
                    return;
                default:
                    WriteString(v.ToString(), sb);
                    return;
            }
        }

        private static void WriteDict(IDictionary d, StringBuilder sb)
        {
            sb.Append('{');
            bool first = true;
            foreach (DictionaryEntry e in d)
            {
                if (!first) sb.Append(',');
                first = false;
                WriteString(System.Convert.ToString(e.Key, CultureInfo.InvariantCulture), sb);
                sb.Append(':');
                Write(e.Value, sb);
            }
            sb.Append('}');
        }

        private static void WriteList(IEnumerable e, StringBuilder sb)
        {
            sb.Append('[');
            bool first = true;
            foreach (var item in e)
            {
                if (!first) sb.Append(',');
                first = false;
                Write(item, sb);
            }
            sb.Append(']');
        }

        private static void WriteString(string s, StringBuilder sb)
        {
            const char bs = (char)92; // avoid backslash literals in source
            sb.Append('"');
            foreach (char ch in s)
            {
                if (ch == '"' || ch == bs)
                {
                    sb.Append(bs).Append(ch);
                }
                else if (ch == (char)10) { sb.Append(bs).Append('n'); }
                else if (ch == (char)13) { sb.Append(bs).Append('r'); }
                else if (ch == (char)9)  { sb.Append(bs).Append('t'); }
                else if (ch < (char)32)
                {
                    sb.Append(bs).Append('u')
                      .Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append(ch);
                }
            }
            sb.Append('"');
        }
    }
}
