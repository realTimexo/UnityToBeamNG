using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace UnityToBeamNG
{
    public static class BeamNGEditorUtilities
    {
        /// <summary>Only [A-Za-z0-9_], everything else becomes '_'. Empty names become "unnamed".</summary>
        public static string SafeId(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unnamed";
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
                sb.Append((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' ? c : '_');
            if (sb[0] >= '0' && sb[0] <= '9') sb.Insert(0, "n_"); // BeamNG: "Object name cannot start with a digit"
            return sb.ToString();
        }

        /// <summary>SafeId, but at most max characters (longer names: start + 8-char hash). Keeps Windows paths below MAX_PATH.</summary>
        public static string ShortId(string s, int max = 40)
        {
            var id = SafeId(s);
            if (id.Length <= max) return id;
            uint h = 2166136261u;
            foreach (var ch in id) { h ^= ch; h *= 16777619u; }
            return id.Substring(0, max - 9) + "_" + h.ToString("x8");
        }

        /// <summary>DAE node name: must not end in a digit (Torque reads a trailing number as detail level size -> "Shape with invalid detail levels").</summary>
        public static string NodeName(string s)
        {
            var n = SafeId(s);
            return char.IsDigit(n[n.Length - 1]) ? n + "_mesh" : n;
        }

        public static bool IsValidLevelName(string s)
        {
            return !string.IsNullOrEmpty(s) && Regex.IsMatch(s, "^[a-z0-9_]+$");
        }

        public static bool IsPowerOfTwo(int v) { return v > 0 && (v & (v - 1)) == 0; }

        public static string HierarchyPath(Transform t)
        {
            var p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }
}
