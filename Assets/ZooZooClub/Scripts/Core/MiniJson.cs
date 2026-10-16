using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ZooZooClub.Core
{
    /// <summary>
    /// 아주 작은 JSON 읽기·쓰기. 서버(Firebase)와 주고받는 자료처럼 모양이 정해져 있지 않은 JSON에 쓴다.
    /// 읽으면 Dictionary&lt;string, object&gt;, List&lt;object&gt;, string, double, bool, null 로 나온다.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string text)
        {
            if (text == null) return null;
            int i = 0;
            object value = ReadValue(text, ref i);
            SkipSpace(text, ref i);
            if (i != text.Length) throw new FormatException("JSON 끝에 남은 글자가 있어요: " + i);
            return value;
        }

        /// <summary>읽은 결과를 사전으로 본다. 사전이 아니면 null.</summary>
        public static Dictionary<string, object> AsObject(object value)
        {
            return value as Dictionary<string, object>;
        }

        public static string AsString(object value, string fallback = "")
        {
            return value is string ? (string)value : fallback;
        }

        public static double AsNumber(object value, double fallback = 0)
        {
            if (value is double) return (double)value;
            if (value is string)
            {
                double parsed;
                if (double.TryParse((string)value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return parsed;
            }
            return fallback;
        }

        public static bool AsBool(object value, bool fallback = false)
        {
            return value is bool ? (bool)value : fallback;
        }

        /// <summary>사전에서 키 하나를 꺼낸다. 없으면 null.</summary>
        public static object Get(Dictionary<string, object> map, string key)
        {
            object value;
            return map != null && map.TryGetValue(key, out value) ? value : null;
        }

        public static string Serialize(object value)
        {
            var builder = new StringBuilder();
            Write(builder, value);
            return builder.ToString();
        }

        static void Write(StringBuilder b, object value)
        {
            if (value == null) { b.Append("null"); return; }
            if (value is string) { WriteString(b, (string)value); return; }
            if (value is bool) { b.Append((bool)value ? "true" : "false"); return; }
            if (value is int || value is long)
            {
                b.Append(Convert.ToInt64(value).ToString(CultureInfo.InvariantCulture)); return;
            }
            if (value is float || value is double)
            {
                b.Append(Convert.ToDouble(value).ToString("R", CultureInfo.InvariantCulture)); return;
            }
            var map = value as Dictionary<string, object>;
            if (map != null)
            {
                b.Append('{');
                bool first = true;
                foreach (var pair in map)
                {
                    if (!first) b.Append(',');
                    first = false;
                    WriteString(b, pair.Key);
                    b.Append(':');
                    Write(b, pair.Value);
                }
                b.Append('}');
                return;
            }
            var list = value as System.Collections.IEnumerable;
            if (list != null)
            {
                b.Append('[');
                bool first = true;
                foreach (var item in list)
                {
                    if (!first) b.Append(',');
                    first = false;
                    Write(b, item);
                }
                b.Append(']');
                return;
            }
            WriteString(b, value.ToString());
        }

        static void WriteString(StringBuilder b, string s)
        {
            b.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
                        else b.Append(c);
                        break;
                }
            }
            b.Append('"');
        }

        static void SkipSpace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static object ReadValue(string s, ref int i)
        {
            SkipSpace(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON이 중간에 끝났어요.");
            char c = s[i];
            if (c == '{') return ReadObject(s, ref i);
            if (c == '[') return ReadArray(s, ref i);
            if (c == '"') return ReadString(s, ref i);
            if (c == 't' && Match(s, i, "true")) { i += 4; return true; }
            if (c == 'f' && Match(s, i, "false")) { i += 5; return false; }
            if (c == 'n' && Match(s, i, "null")) { i += 4; return null; }
            return ReadNumber(s, ref i);
        }

        static bool Match(string s, int i, string word)
        {
            return string.CompareOrdinal(s, i, word, 0, word.Length) == 0;
        }

        static Dictionary<string, object> ReadObject(string s, ref int i)
        {
            var map = new Dictionary<string, object>();
            i++; // {
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return map; }
            while (true)
            {
                SkipSpace(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("JSON 키가 있어야 해요: " + i);
                string key = ReadString(s, ref i);
                SkipSpace(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("JSON ':'이 있어야 해요: " + i);
                i++;
                map[key] = ReadValue(s, ref i);
                SkipSpace(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON이 중간에 끝났어요.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return map; }
                throw new FormatException("JSON ',' 또는 '}'이 있어야 해요: " + i);
            }
        }

        static List<object> ReadArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // [
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(ReadValue(s, ref i));
                SkipSpace(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON이 중간에 끝났어요.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return list; }
                throw new FormatException("JSON ',' 또는 ']'이 있어야 해요: " + i);
            }
        }

        static string ReadString(string s, ref int i)
        {
            var b = new StringBuilder();
            i++; // "
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return b.ToString();
                if (c != '\\') { b.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': b.Append('"'); break;
                    case '\\': b.Append('\\'); break;
                    case '/': b.Append('/'); break;
                    case 'b': b.Append('\b'); break;
                    case 'f': b.Append('\f'); break;
                    case 'n': b.Append('\n'); break;
                    case 'r': b.Append('\r'); break;
                    case 't': b.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("JSON \\u 뒤가 짧아요.");
                        b.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default: b.Append(e); break;
                }
            }
            throw new FormatException("JSON 글자가 닫히지 않았어요.");
        }

        static object ReadNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            double value;
            if (i == start || !double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                throw new FormatException("JSON 값을 읽을 수 없어요: " + start);
            }
            return value;
        }
    }
}
