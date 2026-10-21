using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace UnityToBeamNG
{
    public class RoadPath
    {
        public string Name;
        public List<Vector3> Points = new List<Vector3>(); // Unity world space
        public float Width = 8f, Drivability = 1f;
        public bool OneWay, AutoLanes = true, Loop;
        public int LanesLeft = 1, LanesRight = 1;
    }

    /// <summary>
    /// Builds the BeamNG road network: every BeamNGRoad / BeamNGRoadPiece becomes an invisible DecalRoad
    /// (material road_invisible, drivability 1). BeamNG builds its AI navigation graph from DecalRoads.
    /// </summary>
    public static class BeamNGRoadExporter
    {
        public const float SnapDistance = 0.75f;

        public static List<RoadPath> Collect(BeamNGExportReport r)
        {
            var list = new List<RoadPath>();
            int roadsFound = 0, piecesFound = 0;

            var roads = new List<BeamNGRoad>(UnityEngine.Object.FindObjectsByType<BeamNGRoad>(FindObjectsSortMode.None));
            roads.Sort((a, b) => string.CompareOrdinal(BeamNGEditorUtilities.HierarchyPath(a.transform), BeamNGEditorUtilities.HierarchyPath(b.transform)));
            foreach (var road in roads)
            {
                if (!road.gameObject.activeInHierarchy || !road.exportEnabled) continue;
                roadsFound++;
                List<Vector3> pts; float w = road.width;
                var effectiveSource = road.source;
                // Road Architect nodes found on/around this object: always prefer them over the child objects (meshes etc.)
                if (effectiveSource == BeamNGRoadSource.ChildPoints && RaNodeTransforms(road.gameObject, true) != null) effectiveSource = BeamNGRoadSource.RoadArchitect;
                if (effectiveSource == BeamNGRoadSource.RoadArchitect)
                {
                    float rw;
                    pts = FromRoadArchitect(road, out rw);
                    if (pts == null)
                    {
                        r.Add(ReportStatus.WARNING, "Roads", $"'{road.name}': no Road Architect road found (components seen: {DescribeComponents(road.gameObject)}).", "Send this list to the tool author, or use 'Create road from points'.");
                        continue;
                    }
                    if (rw > 0.5f) w = rw;
                }
                else pts = Dedupe(road.ChildPointPositions());
                if (pts.Count < 2 && road.source == BeamNGRoadSource.ChildPoints)
                {
                    // the user may have marked a mesh object that belongs to a Road Architect road: look around it
                    float rw2; var alt = FromRoadArchitect(road, out rw2);
                    if (alt != null && alt.Count >= 2) { pts = alt; if (rw2 > 0.5f) w = rw2; }
                }
                if (pts.Count < 2) { r.Add(ReportStatus.WARNING, "Roads", $"'{road.name}': fewer than 2 distinct points, skipped (a mesh object has all children at one position).", "Components seen: " + DescribeComponents(road.gameObject)); continue; }
                list.Add(new RoadPath
                {
                    Name = BeamNGEditorUtilities.SafeId(road.name), Points = Dedupe(pts), Width = w, Drivability = road.drivability,
                    OneWay = road.oneWay, AutoLanes = road.autoLanes, LanesLeft = road.lanesLeft, LanesRight = road.lanesRight, Loop = road.closedLoop
                });
            }

            var pieces = new List<BeamNGRoadPiece>(UnityEngine.Object.FindObjectsByType<BeamNGRoadPiece>(FindObjectsSortMode.None));
            pieces.Sort((a, b) => string.CompareOrdinal(BeamNGEditorUtilities.HierarchyPath(a.transform), BeamNGEditorUtilities.HierarchyPath(b.transform)));
            foreach (var p in pieces)
            {
                if (!p.gameObject.activeInHierarchy || !p.exportEnabled) continue;
                piecesFound++;
                var conns = new List<Transform>();
                foreach (var c in p.connectors) if (c != null) conns.Add(c);
                if (conns.Count < 2) { r.Add(ReportStatus.WARNING, "Roads", $"Road piece '{p.name}' needs at least 2 connectors, skipped.", "Use 'Add road piece' in the Roads tab."); continue; }
                string baseName = BeamNGEditorUtilities.SafeId(p.name);
                if (conns.Count == 2)
                    list.Add(Piece(baseName, Curve(conns[0], conns[1]), p));
                else
                {
                    var center = Junction(conns);
                    for (int i = 0; i < conns.Count; i++)
                        list.Add(Piece(baseName + "_arm" + i, new List<Vector3> { conns[i].position, center }, p));
                }
            }

            SnapEndpoints(list);
            if (list.Count > 0)
                r.Add(ReportStatus.PASS, "Roads", $"{list.Count} road segment(s) from {roadsFound} road object(s) and {piecesFound} road piece(s) written as invisible DecalRoads (AI/navigation).");
            else if (roadsFound + piecesFound == 0)
                r.Add(ReportStatus.MANUAL, "Roads", "No BeamNGRoad / BeamNGRoadPiece in the scene: the map has no AI road network (no traffic, no AI routes).", "Use the 'Roads' tab.");
            return list;
        }

        static RoadPath Piece(string name, List<Vector3> pts, BeamNGRoadPiece p)
        {
            return new RoadPath { Name = name, Points = pts, Width = p.width, Drivability = p.drivability };
        }

        static List<Vector3> Dedupe(List<Vector3> pts)
        {
            var res = new List<Vector3>();
            foreach (var p in pts) if (res.Count == 0 || (res[res.Count - 1] - p).sqrMagnitude > 0.0025f) res.Add(p);
            return res;
        }

        /// <summary>Path inside a 2-connector piece. Connector forward points OUT of the piece.</summary>
        public static List<Vector3> Curve(Transform a, Transform b)
        {
            Vector3 pa = a.position, pb = b.position;
            var d = pb - pa; float dist = d.magnitude;
            if (dist < 0.01f) return new List<Vector3> { pa, pb };
            var dn = d / dist;
            bool straight = Vector3.Dot(-a.forward, dn) > 0.999f && Vector3.Dot(-b.forward, -dn) > 0.999f;
            if (straight) return new List<Vector3> { pa, pb };
            float k = dist * 0.33f;
            Vector3 p1 = pa - a.forward * k, p2 = pb - b.forward * k;
            var res = new List<Vector3>();
            const int n = 10;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n, u = 1f - t;
                res.Add(u * u * u * pa + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * pb);
            }
            return res;
        }

        /// <summary>Junction centre: point closest to all connector lines (XZ), height = average.</summary>
        public static Vector3 Junction(List<Transform> conns)
        {
            double a11 = 0, a12 = 0, a22 = 0, b1 = 0, b2 = 0; float y = 0; Vector3 avg = Vector3.zero;
            foreach (var c in conns)
            {
                var f = c.forward; f.y = 0; if (f.sqrMagnitude < 1e-6f) continue; f.Normalize();
                double m11 = 1 - f.x * f.x, m12 = -f.x * f.z, m22 = 1 - f.z * f.z;
                a11 += m11; a12 += m12; a22 += m22;
                b1 += m11 * c.position.x + m12 * c.position.z;
                b2 += m12 * c.position.x + m22 * c.position.z;
                y += c.position.y; avg += c.position;
            }
            avg /= conns.Count; y /= conns.Count;
            double det = a11 * a22 - a12 * a12;
            if (Math.Abs(det) < 1e-4) return avg;
            float x = (float)((b1 * a22 - b2 * a12) / det), z = (float)((a11 * b2 - a12 * b1) / det);
            return new Vector3(x, y, z);
        }

        static void SnapEndpoints(List<RoadPath> list)
        {
            var refs = new List<KeyValuePair<RoadPath, bool>>();
            foreach (var rp in list) { refs.Add(new KeyValuePair<RoadPath, bool>(rp, true)); refs.Add(new KeyValuePair<RoadPath, bool>(rp, false)); }
            var done = new bool[refs.Count];
            for (int i = 0; i < refs.Count; i++)
            {
                if (done[i]) continue;
                var group = new List<int> { i }; done[i] = true;
                var pi = Get(refs[i]);
                for (int j = i + 1; j < refs.Count; j++)
                    if (!done[j] && (Get(refs[j]) - pi).sqrMagnitude <= SnapDistance * SnapDistance) { group.Add(j); done[j] = true; }
                if (group.Count < 2) continue;
                var avg = Vector3.zero; foreach (int g in group) avg += Get(refs[g]); avg /= group.Count;
                foreach (int g in group) Set(refs[g], avg);
            }
        }
        static Vector3 Get(KeyValuePair<RoadPath, bool> e) { var p = e.Key.Points; return e.Value ? p[0] : p[p.Count - 1]; }
        static void Set(KeyValuePair<RoadPath, bool> e, Vector3 v) { var p = e.Key.Points; if (e.Value) p[0] = v; else p[p.Count - 1] = v; }

        // ---------------- Road Architect (reflection, no hard dependency) ----------------

        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        static object Member(object o, params string[] names)
        {
            if (o == null) return null;
            var t = o.GetType();
            foreach (var n in names)
            {
                for (var tt = t; tt != null; tt = tt.BaseType)
                {
                    var f = tt.GetField(n, All | BindingFlags.DeclaredOnly);
                    if (f != null) return f.GetValue(o);
                    var p = tt.GetProperty(n, All | BindingFlags.DeclaredOnly);
                    if (p != null && p.GetIndexParameters().Length == 0) { try { return p.GetValue(o, null); } catch { } }
                }
            }
            return null;
        }

        static IEnumerable<MonoBehaviour> Candidates(GameObject go)
        {
            foreach (var m in go.GetComponents<MonoBehaviour>()) yield return m;
            foreach (var m in go.GetComponentsInParent<MonoBehaviour>(true)) yield return m;
            foreach (var m in go.GetComponentsInChildren<MonoBehaviour>(true)) yield return m;
        }

        /// <summary>True if the component looks like a Road Architect road (has a spline with nodes).</summary>
        public static bool IsRoadArchitectRoad(Component comp)
        {
            if (comp == null) return false;
            if (comp.GetType().Name.EndsWith("Road", StringComparison.Ordinal) && !(comp is BeamNGRoad) && RaNodeTransforms(comp.gameObject, false) != null) return true;
            try
            {
                var spline = Member(comp, "spline", "mSpline", "Spline");
                if (spline == null) return false;
                var nl = Member(spline, "mNodes", "nodes", "Nodes") as IEnumerable;
                if (nl == null) return false;
                int n = 0; foreach (var x in nl) n++;
                return n >= 2;
            }
            catch { return false; }
        }

        static List<Vector3> FromRoadArchitect(BeamNGRoad road, out float width)
        {
            var legacy = FromRoadArchitectLegacy(road, out width);
            if (legacy != null) return legacy;
            return FromRaNodes(road.gameObject, out width);
        }

        static bool TypeEndsWith(Component c, string suffix)
        {
            return c != null && c.GetType().Name.EndsWith(suffix, StringComparison.Ordinal);
        }

        /// <summary>Road Architect keeps its nodes as child objects with a "SplineN" component (below the "Spline" object).</summary>
        public static List<Transform> RaNodeTransforms(GameObject go, bool climb)
        {
            for (var t = go.transform; t != null; t = climb ? t.parent : null)
            {
                var nodes = new List<Transform>();
                foreach (var mb in t.GetComponentsInChildren<MonoBehaviour>(true))
                    if (TypeEndsWith(mb, "SplineN")) nodes.Add(mb.transform);
                if (nodes.Count >= 2) return nodes;
            }
            return null;
        }

        static List<Vector3> FromRaNodes(GameObject go, out float width)
        {
            width = -1f;
            Transform owner = null; List<Transform> nodeT = null;
            for (var t = go.transform; t != null && nodeT == null; t = t.parent)
            {
                var nodes = new List<Transform>();
                foreach (var mb in t.GetComponentsInChildren<MonoBehaviour>(true)) if (TypeEndsWith(mb, "SplineN")) nodes.Add(mb.transform);
                if (nodes.Count >= 2) { owner = t; nodeT = nodes; }
            }
            if (nodeT == null) return null;
            var pts = new List<Vector3>();
            foreach (var n in nodeT) pts.Add(n.position);

            // width from the Road component (this object or a parent)
            for (var t = owner; t != null && width < 0f; t = t.parent)
                foreach (var mb in t.GetComponents<MonoBehaviour>())
                {
                    var w = Member(mb, "RoadWidth", "roadWidth", "mRoadWidth");
                    if (w is float && (float)w > 0.5f) { width = (float)w; break; }
                }

            // smooth sampling through the SplineC itself
            try
            {
                foreach (var mb in owner.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (!TypeEndsWith(mb, "SplineC")) continue;
                    var m = mb.GetType().GetMethod("GetSplineValue", All);
                    if (m == null) continue;
                    var prm = m.GetParameters();
                    if (prm.Length < 1 || prm.Length > 2) continue;
                    var samples = new List<Vector3>();
                    int count = Mathf.Clamp(pts.Count * 8, 8, 2000);
                    bool ok = true;
                    for (int i = 0; i <= count && ok; i++)
                    {
                        float tt = i / (float)count;
                        object res = prm.Length == 1 ? m.Invoke(mb, new object[] { tt }) : m.Invoke(mb, new object[] { tt, false });
                        if (res is Vector3) samples.Add((Vector3)res); else ok = false;
                    }
                    if (ok && samples.Count >= 2 && (samples[0] - pts[0]).magnitude < 25f && (samples[samples.Count - 1] - pts[pts.Count - 1]).magnitude < 25f)
                        return Dedupe(samples);
                }
            }
            catch (Exception e) { Debug.LogWarning("Road Architect spline sampling failed, using nodes: " + e.Message); }
            return Dedupe(pts);
        }

        public static string DescribeComponents(GameObject go)
        {
            var names = new SortedSet<string>();
            for (var t = go.transform; t != null; t = t.parent)
                foreach (var mb in t.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) names.Add(mb.GetType().Name);
            var l = new List<string>(names); if (l.Count > 25) l = l.GetRange(0, 25);
            return string.Join(", ", l);
        }

        static List<Vector3> FromRoadArchitectLegacy(BeamNGRoad road, out float width)
        {
            width = -1f;
            foreach (var comp in Candidates(road.gameObject))
            {
                if (comp == null || comp == road) continue;
                try
                {
                    var spline = Member(comp, "spline", "mSpline", "Spline");
                    if (spline == null) continue;
                    var wObj = Member(comp, "RoadWidth", "roadWidth", "mRoadWidth");
                    if (wObj is float) width = (float)wObj;

                    var nodes = new List<Vector3>();
                    var nl = Member(spline, "mNodes", "nodes", "Nodes") as IEnumerable;
                    if (nl != null)
                        foreach (var n in nl)
                        {
                            var pv = Member(n, "pos", "position");
                            if (pv is Vector3) nodes.Add((Vector3)pv);
                            else { var tr = Member(n, "transform") as Transform; if (tr != null) nodes.Add(tr.position); }
                        }

                    // smooth sampling through the spline itself when the API allows it
                    var m = spline.GetType().GetMethod("GetSplineValue", All);
                    if (m != null && nodes.Count >= 2)
                    {
                        var prm = m.GetParameters();
                        var samples = new List<Vector3>();
                        int count = Mathf.Clamp(nodes.Count * 8, 8, 2000);
                        bool ok = true;
                        for (int i = 0; i <= count && ok; i++)
                        {
                            float t = i / (float)count;
                            object res = prm.Length == 1 ? m.Invoke(spline, new object[] { t })
                                       : prm.Length == 2 ? m.Invoke(spline, new object[] { t, false }) : null;
                            if (res is Vector3) samples.Add((Vector3)res); else ok = false;
                        }
                        if (ok && samples.Count >= 2 && (samples[0] - samples[samples.Count - 1]).sqrMagnitude > 0.01f) return Dedupe(samples);
                    }
                    if (nodes.Count >= 2) return Dedupe(nodes);
                }
                catch (Exception e) { Debug.LogWarning("Road Architect reflection failed: " + e.Message); }
            }
            return null;
        }

        // ---------------- items.level.json lines ----------------

        public static List<string> BuildLines(List<RoadPath> roads)
        {
            var lines = new List<string>();
            var used = new HashSet<string>();
            foreach (var rp in roads)
            {
                string name = "road_" + rp.Name; int n = 2;
                while (!used.Add(name)) name = "road_" + rp.Name + "_" + (n++);
                var nodes = new StringBuilder("[");
                for (int i = 0; i < rp.Points.Count; i++)
                {
                    var b = BeamNGCoordinateConverter.Position(rp.Points[i]);
                    if (i > 0) nodes.Append(',');
                    nodes.Append(BeamNGJson.A(b.x, b.y, b.z, rp.Width));
                }
                nodes.Append(']');
                var o = new JsonObj().Str("class", "DecalRoad").Str("name", name).Str("__parent", "Roads")
                    .Str("material", "road_invisible").Num("textureLength", 5f).Num("drivability", rp.Drivability)
                    .Bool("improvedSpline", true).Bool("overObjects", true).Bool("oneWay", rp.OneWay).Bool("looped", rp.Loop)
                    .Bool("autoLanes", rp.AutoLanes);
                if (!rp.AutoLanes) o.Num("lanesLeft", rp.LanesLeft).Num("lanesRight", rp.LanesRight);
                o.Raw("nodes", nodes.ToString());
                lines.Add(o.ToString());
            }
            return lines;
        }
    }
}
