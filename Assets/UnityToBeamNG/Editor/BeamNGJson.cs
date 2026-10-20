using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace UnityToBeamNG
{
    /// <summary>Minimal JSON writer (invariant culture, no NaN/Infinity).</summary>
    public static class BeamNGJson
    {
        public static string N(float f)
        {
            if (float.IsNaN(f) || float.IsInfinity(f))
                throw new InvalidDataException("Invalid number (NaN/Infinity) while writing JSON.");
            return f.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>Quoted + escaped JSON-String.</summary>
        public static string Q(string s)
        {
            if (s == null) s = "";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var c in s)
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
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>Number array: [1,2,3]</summary>
        public static string A(params float[] v)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < v.Length; i++) { if (i > 0) sb.Append(','); sb.Append(N(v[i])); }
            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>String array: ["a","b"]</summary>
        public static string AS(IEnumerable<string> v)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            bool first = true;
            foreach (var s in v) { if (!first) sb.Append(','); first = false; sb.Append(Q(s)); }
            sb.Append(']');
            return sb.ToString();
        }
    }

    /// <summary>Fluent single-line JSON object builder (items.level.json: one object per line).</summary>
    public class JsonObj
    {
        readonly List<string> parts = new List<string>();

        public JsonObj Str(string key, string value) { parts.Add(BeamNGJson.Q(key) + ":" + BeamNGJson.Q(value)); return this; }
        public JsonObj Num(string key, float value) { parts.Add(BeamNGJson.Q(key) + ":" + BeamNGJson.N(value)); return this; }
        public JsonObj Bool(string key, bool value) { parts.Add(BeamNGJson.Q(key) + ":" + (value ? "true" : "false")); return this; }
        public JsonObj Arr(string key, params float[] values) { parts.Add(BeamNGJson.Q(key) + ":" + BeamNGJson.A(values)); return this; }
        public JsonObj Raw(string key, string rawJson) { parts.Add(BeamNGJson.Q(key) + ":" + rawJson); return this; }

        public override string ToString() { return "{" + string.Join(",", parts) + "}"; }
    }
}
