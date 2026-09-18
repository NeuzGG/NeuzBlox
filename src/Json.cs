using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NeuzBlox
{
    /// <summary>Tiny dependency-free JSON reader/writer (C# 5 compatible).</summary>
    public static class Json
    {
        public static object Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            int i = 0;
            return ParseValue(s, ref i);
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }

        static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{') return ParseObj(s, ref i);
            if (c == '[') return ParseArr(s, ref i);
            if (c == '"') return ParseStr(s, ref i);
            if (c == 't' && i + 4 <= s.Length && s.Substring(i, 4) == "true") { i += 4; return true; }
            if (c == 'f' && i + 5 <= s.Length && s.Substring(i, 5) == "false") { i += 5; return false; }
            if (c == 'n' && i + 4 <= s.Length && s.Substring(i, 4) == "null") { i += 4; return null; }
            return ParseNum(s, ref i);
        }

        static Dictionary<string, object> ParseObj(string s, ref int i)
        {
            var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (i < s.Length)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') break;
                string key = ParseStr(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                d[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                break;
            }
            return d;
        }

        static List<object> ParseArr(string s, ref int i)
        {
            var l = new List<object>();
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return l; }
            while (i < s.Length)
            {
                l.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                break;
            }
            return l;
        }

        static string ParseStr(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\' && i < s.Length)
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case '/': sb.Append('/'); break;
                        case '\\': sb.Append('\\'); break;
                        case '"': sb.Append('"'); break;
                        case 'u':
                            if (i + 4 <= s.Length)
                            {
                                int code;
                                if (int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                    sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static object ParseNum(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "0123456789+-.eE".IndexOf(s[i]) >= 0) i++;
            string raw = s.Substring(start, i - start);
            double d;
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return 0d;
        }

        public static string Str(object node, string key)
        {
            var d = node as Dictionary<string, object>;
            if (d == null) return null;
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return null;
            if (v is string) return (string)v;
            if (v is double) return ((double)v).ToString("0.############", CultureInfo.InvariantCulture);
            if (v is bool) return ((bool)v) ? "true" : "false";
            return v.ToString();
        }

        public static long Long(object node, string key, long fallback)
        {
            var d = node as Dictionary<string, object>;
            if (d == null) return fallback;
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return fallback;
            if (v is double) return (long)(double)v;
            long l;
            if (v is string && long.TryParse((string)v, out l)) return l;
            return fallback;
        }

        public static int Int(object node, string key, int fallback)
        {
            return (int)Long(node, key, fallback);
        }

        public static bool Bool(object node, string key, bool fallback)
        {
            var d = node as Dictionary<string, object>;
            if (d == null) return fallback;
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return fallback;
            if (v is bool) return (bool)v;
            if (v is string) return string.Equals((string)v, "true", StringComparison.OrdinalIgnoreCase);
            if (v is double) return ((double)v) != 0;
            return fallback;
        }

        public static List<object> Arr(object node, string key)
        {
            var d = node as Dictionary<string, object>;
            if (d == null) return null;
            object v;
            if (!d.TryGetValue(key, out v)) return null;
            return v as List<object>;
        }

        public static object Obj(object node, string key)
        {
            var d = node as Dictionary<string, object>;
            if (d == null) return null;
            object v;
            if (!d.TryGetValue(key, out v)) return null;
            return v;
        }

        public static string Escape(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public static string Quote(string s)
        {
            return "\"" + Escape(s) + "\"";
        }

        public static string Num(double d)
        {
            return d.ToString("0.############", CultureInfo.InvariantCulture);
        }
    }
}
