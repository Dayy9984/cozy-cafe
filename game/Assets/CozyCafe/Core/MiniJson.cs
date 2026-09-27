using System;
using System.Collections;
using System.Collections.Generic;
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

        /// Minimal JSON reader for runtime data files (data/*.json). Numbers
        /// become long when integral and double otherwise; objects map to
        /// Dictionary<string,object>, arrays to List<object>. Kept in this
        /// assembly so the same source compiles under both hosts with no
        /// external packages.
        public static object Parse(string text)
        {
            var p = new Parser(text);
            object v = p.ReadValue();
            p.SkipWs();
            if (!p.End) throw new FormatException("MiniJson: trailing content at " + p.Pos);
            return v;
        }

        private sealed class Parser
        {
            private readonly string s;
            private int i;

            public Parser(string text)
            {
                s = text ?? "";
            }

            public bool End
            {
                get { return i >= s.Length; }
            }

            public int Pos
            {
                get { return i; }
            }

            public void SkipWs()
            {
                while (i < s.Length
                    && (s[i] == ' ' || s[i] == (char)9 || s[i] == (char)10 || s[i] == (char)13))
                {
                    i++;
                }
            }

            public object ReadValue()
            {
                SkipWs();
                if (i >= s.Length) throw new FormatException("MiniJson: unexpected end");
                char c = s[i];
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return ReadJsonString();
                if (c == 't') { Expect("true"); return true; }
                if (c == 'f') { Expect("false"); return false; }
                if (c == 'n') { Expect("null"); return null; }
                return ReadNumber();
            }

            private void Expect(string lit)
            {
                if (string.Compare(s, i, lit, 0, lit.Length, StringComparison.Ordinal) != 0)
                {
                    throw new FormatException("MiniJson: expected '" + lit + "' at " + i);
                }
                i += lit.Length;
            }

            private Dictionary<string, object> ReadObject()
            {
                var d = new Dictionary<string, object>();
                i++;
                SkipWs();
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    SkipWs();
                    if (i >= s.Length || s[i] != '"')
                    {
                        throw new FormatException("MiniJson: expected key at " + i);
                    }
                    string key = ReadJsonString();
                    SkipWs();
                    if (i >= s.Length || s[i] != ':')
                    {
                        throw new FormatException("MiniJson: expected ':' at " + i);
                    }
                    i++;
                    d[key] = ReadValue();
                    SkipWs();
                    if (i >= s.Length) throw new FormatException("MiniJson: unterminated object");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("MiniJson: expected ',' or '}' at " + i);
                }
            }

            private List<object> ReadArray()
            {
                var l = new List<object>();
                i++;
                SkipWs();
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(ReadValue());
                    SkipWs();
                    if (i >= s.Length) throw new FormatException("MiniJson: unterminated array");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("MiniJson: expected ',' or ']' at " + i);
                }
            }

            private string ReadJsonString()
            {
                var sb = new StringBuilder();
                i++;
                while (i < s.Length)
                {
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c == (char)92)
                    {
                        if (i >= s.Length) break;
                        char e = s[i++];
                        switch (e)
                        {
                            case 'n': sb.Append((char)10); break;
                            case 'r': sb.Append((char)13); break;
                            case 't': sb.Append((char)9); break;
                            case 'b': sb.Append((char)8); break;
                            case 'f': sb.Append((char)12); break;
                            case 'u':
                                if (i + 4 > s.Length)
                                {
                                    throw new FormatException("MiniJson: bad escape at " + i);
                                }
                                sb.Append((char)int.Parse(
                                    s.Substring(i, 4), NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture));
                                i += 4;
                                break;
                            default: sb.Append(e); break;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                throw new FormatException("MiniJson: unterminated string");
            }

            private object ReadNumber()
            {
                int start = i;
                bool frac = false;
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c >= '0' && c <= '9') { i++; continue; }
                    if (c == '.' || c == 'e' || c == 'E') { frac = true; i++; continue; }
                    if ((c == '-' || c == '+') && i > start) { i++; continue; }
                    if (c == '-' && i == start) { i++; continue; }
                    break;
                }
                string t = s.Substring(start, i - start);
                if (t.Length == 0 || t == "-")
                {
                    throw new FormatException("MiniJson: bad number at " + start);
                }
                if (!frac)
                {
                    long lv;
                    if (long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out lv))
                    {
                        return lv;
                    }
                }
                return double.Parse(t, CultureInfo.InvariantCulture);
            }
        }
    }
}
