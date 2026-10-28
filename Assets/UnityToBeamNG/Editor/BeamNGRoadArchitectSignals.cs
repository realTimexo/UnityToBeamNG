using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnityToBeamNG
{
    /// <summary>
    /// Converts the traffic lights / stop signs of Road Architect intersections (class GSDRoadIntersection, both the original
    /// MicroGSD version and forks) into BeamNGTrafficIntersection / BeamNGTrafficLight / BeamNGTrafficSign objects.
    /// Road Architect itself keeps animating its lights in Unity play mode; this only reads its data (reflection, no dependency).
    /// </summary>
    public static class BeamNGRoadArchitectSignals
    {
        const string Holder = "BeamNG Signals (from Road Architect)";

        static object Field(object o, params string[] names)
        {
            if (o == null) return null;
            foreach (var n in names)
            {
                var f = o.GetType().GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return f.GetValue(o);
            }
            return null;
        }

        static float Num(object o, float def, params string[] names)
        {
            var v = Field(o, names);
            return v is float ? (float)v : def;
        }

        /// <summary>Original Road Architect: GSDRoadIntersection. FritzsHero fork: RoadArchitect.RoadIntersection.</summary>
        static bool IsIntersection(object mb)
        {
            var t = mb.GetType();
            if (t.Name == "GSDRoadIntersection") return true;
            return t.Name == "RoadIntersection" && (t.Namespace ?? "").Contains("RoadArchitect");
        }

        public static List<Component> FindIntersections()
        {
            var list = new List<Component>();
            foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (mb != null && IsIntersection(mb)) list.Add(mb);
            list.Sort((a, b) => string.CompareOrdinal(BeamNGEditorUtilities.HierarchyPath(a.transform), BeamNGEditorUtilities.HierarchyPath(b.transform)));
            return list;
        }

        /// <summary>"lights", "stop" or "" for an intersection.</summary>
        static string Kind(Component ri)
        {
            var t = Field(ri, "iStopType", "iDefaultIntersectionType", "intersectionStopType");
            string s = t == null ? "" : t.ToString();
            if (s.StartsWith("TrafficLight", StringComparison.Ordinal)) return "lights";
            if (s.StartsWith("StopSign", StringComparison.Ordinal)) return "stop";
            if (s == "" && Heads(ri).Count > 0) return "lights";
            return "";
        }

        static List<Transform> Heads(Component ri)
        {
            var heads = new List<Transform>();
            foreach (var cn in new[] { "LightsRR", "LightsRL", "LightsLL", "LightsLR", "lightsRR", "lightsRL", "lightsLL", "lightsLR" })
            {
                var ctrl = Field(ri, cn);
                var objs = Field(ctrl, "LightsObj", "lightsObjects") as GameObject[];
                if (objs == null) continue;
                foreach (var g in objs) if (g != null && !heads.Contains(g.transform)) heads.Add(g.transform);
            }
            return heads;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>Outward directions (horizontal) of the 2-4 approaches, taken from the corner points of the intersection.</summary>
        static List<Vector3> Approaches(Component ri, out List<Vector3> edgeMids)
        {
            var cs = new List<Vector3>();
            foreach (var n in new[] { "CornerRR", "CornerRL", "CornerLL", "CornerLR" })
            {
                var v = Field(ri, n, char.ToLowerInvariant(n[0]) + n.Substring(1));
                cs.Add(v is Vector3 ? (Vector3)v : Vector3.zero);
            }
            var dirs = new List<Vector3>(); edgeMids = new List<Vector3>();
            var c = ri.transform.position;
            for (int i = 0; i < 4; i++)
            {
                var a = cs[i]; var b = cs[(i + 1) % 4];
                if (a == Vector3.zero || b == Vector3.zero) continue;
                var mid = (a + b) * 0.5f;
                var d = Flat(mid - c);
                if (d.sqrMagnitude < 0.01f) continue;
                dirs.Add(d.normalized); edgeMids.Add(mid);
            }
            if (dirs.Count < 2)
            {
                dirs.Clear(); edgeMids.Clear();
                var r = ri.transform;
                foreach (var d in new[] { r.forward, r.right, -r.forward, -r.right }) { var f = Flat(d).normalized; dirs.Add(f); edgeMids.Add(c + f * 10f); }
            }
            return dirs;
        }

        /// <summary>
        /// Road Architect switches its lamps by shifting a texture atlas (not exportable). Instead, three own lamp objects are placed
        /// where its redlight/yellowlight/greenlight objects are, and the atlas mesh ("Lights") is left out of the export.
        /// BeamNG then switches these lamps with the signal state.
        /// </summary>
        static void ReplaceLamps(Transform head, Transform target, BeamNGTrafficLight tl)
        {
            Vector3? red = null, yellow = null, green = null;
            foreach (var l in head.GetComponentsInChildren<Light>(true))
            {
                string n = l.name.ToLowerInvariant();
                if (n.Contains("red")) red = l.transform.position;
                else if (n.Contains("yellow")) yellow = l.transform.position;
                else if (n.Contains("green")) green = l.transform.position;
            }
            Renderer lens = null;
            foreach (var r in head.GetComponentsInChildren<Renderer>(true))
                if (r.name.ToLowerInvariant() == "lights") { lens = r; break; }
            if (!(red.HasValue && yellow.HasValue && green.HasValue))
            {
                // fallback: three equal slots from the top to the bottom of the lens mesh
                if (lens == null) return;
                var b = lens.bounds; float q = b.size.y / 3f; var c = b.center;
                red = new Vector3(c.x, b.max.y - q * 0.5f, c.z); yellow = new Vector3(c.x, c.y, c.z); green = new Vector3(c.x, b.min.y + q * 0.5f, c.z);
            }
            float dia = Mathf.Clamp(Mathf.Abs(red.Value.y - green.Value.y) / 2f * 0.8f, 0.15f, 0.45f);
            tl.redLamp = BeamNGRoadTools.MakeLamp(target, "Red", red.Value, dia);
            tl.yellowLamp = BeamNGRoadTools.MakeLamp(target, "Yellow", yellow.Value, dia);
            tl.greenLamp = BeamNGRoadTools.MakeLamp(target, "Green", green.Value, dia);
            if (lens != null)
            {
                var cs = lens.gameObject.GetComponent<BeamNGCollisionSettings>();
                if (cs == null) cs = lens.gameObject.AddComponent<BeamNGCollisionSettings>();
                cs.excludeFromExport = true;
            }
        }

        static void Clear(Component ri)
        {
            var old = ri.transform.Find(Holder);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        }

        /// <summary>Converts all Road Architect intersections; returns "lights/stop" counts. Existing converted objects are rebuilt.</summary>
        public static int Convert(out int lightCount, out int stopCount, List<string> notes = null)
        {
            lightCount = 0; stopCount = 0; int inter = 0;
            foreach (var ri in FindIntersections())
            {
                string kind = Kind(ri);
                if (kind == "") continue;
                Clear(ri);
                List<Vector3> mids; var dirs = Approaches(ri, out mids);
                var holder = new GameObject(Holder);
                holder.transform.SetParent(ri.transform, false);
                holder.transform.position = ri.transform.position;
                var c = ri.transform.position;

                if (kind == "stop")
                {
                    for (int i = 0; i < dirs.Count; i++)
                    {
                        var g = new GameObject("Stop Sign " + i);
                        g.transform.SetParent(holder.transform, false);
                        g.transform.position = mids[i];
                        g.transform.rotation = Quaternion.LookRotation(-dirs[i], Vector3.up);
                        g.AddComponent<BeamNGTrafficSign>();
                        stopCount++;
                    }
                    inter++;
                    continue;
                }

                var heads = Heads(ri);
                if (heads.Count == 0) { notes?.Add("Intersection '" + ri.name + "' has no light heads (build the traffic lights in Road Architect first)."); UnityEngine.Object.DestroyImmediate(holder); continue; }

                float green = Num(ri, 10f, "opt_FixedTime_RegularLightLength", "fixedTimeRegularLightLength");
                float yellow = Num(ri, 2f, "opt_FixedTime_YellowLightLength", "fixedTimeYellowLightLength");
                float allRed = Num(ri, 1f, "opt_FixedTime_AllRedLightLength", "fixedTimeAllRedLightLength");
                float left = Num(ri, 5f, "opt_FixedTime_LeftTurnLightLength", "fixedTimeLeftTurnLightLength");
                var rt = Field(ri, "rType", "roadType");
                bool turn = rt != null && rt.ToString() != "NoTurnLane";

                var ti = holder.AddComponent<BeamNGTrafficIntersection>();
                ti.greenSeconds = green; ti.yellowSeconds = yellow;
                // RA gives the left-turn phase to the same path first; the crossing path stays red meanwhile
                ti.allRedSeconds = allRed + (turn ? left + yellow + allRed : 0f);

                int n = 0;
                foreach (var h in heads)
                {
                    var to = Flat(h.position - c);
                    int best = 0; float bd = -2f;
                    for (int i = 0; i < dirs.Count; i++) { float d = Vector3.Dot(to.normalized, dirs[i]); if (d > bd) { bd = d; best = i; } }
                    var g = new GameObject("Light " + (n++));
                    g.transform.SetParent(holder.transform, false);
                    g.transform.position = h.position;
                    g.transform.rotation = Quaternion.LookRotation(-dirs[best], Vector3.up);   // traffic drives towards the junction
                    var tl = g.AddComponent<BeamNGTrafficLight>();
                    tl.phase = best % 2;                                                      // neighbouring approaches never share a phase
                    ReplaceLamps(h, g.transform, tl);
                    lightCount++;
                }
                inter++;
            }
            return inter;
        }

        /// <summary>Short description for the report when nothing was converted.</summary>
        public static string Diagnose()
        {
            var all = FindIntersections();
            if (all.Count == 0)
            {
                int other = 0; string sample = "";
                foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (mb != null && mb.GetType().Name.ToLowerInvariant().Contains("intersection") && !(mb is BeamNGTrafficIntersection)) { other++; if (sample == "") sample = mb.GetType().FullName; }
                return other > 0 ? "No known Road Architect intersection class found, but " + other + " other component(s) named like an intersection exist (e.g. '" + sample + "'). Please send me this class name." : "";
            }
            var sb = new System.Text.StringBuilder("Road Architect: " + all.Count + " intersection(s) found, none with traffic lights or stop signs: ");
            int k = 0;
            foreach (var ri in all)
            {
                if (k++ >= 4) break;
                var t = Field(ri, "iStopType", "iDefaultIntersectionType", "intersectionStopType");
                sb.Append("'" + ri.name + "' type=" + (t == null ? "?" : t.ToString()) + " heads=" + Heads(ri).Count + "; ");
            }
            return sb.ToString();
        }

        /// <summary>True if there are Road Architect intersections with lights/stop signs that were not converted yet.</summary>
        public static bool NeedsConversion()
        {
            foreach (var ri in FindIntersections())
                if (Kind(ri) != "" && ri.transform.Find(Holder) == null) return true;
            return false;
        }
    }
}
