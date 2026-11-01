using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace UnityToBeamNG
{
    /// <summary>
    /// Speed signs -> speed limit of the road network. BeamNG docs: map.json segments (nodes = BeamNGWaypoint names) may carry
    /// "speedLimit" in m/s; the segments are merged into the navigation graph that DecalRoads feed. Every sign therefore writes a chain of
    /// waypoints along the road in front of it plus one segment with the limit. (The sign model itself is a normal mesh.)
    /// </summary>
    public static class BeamNGSpeedSignExporter
    {
        const float Step = 12f;

        public static void Export(BeamNGExportContext c)
        {
            var r = c.Report;
            var signs = new List<BeamNGSpeedSign>(Object.FindObjectsByType<BeamNGSpeedSign>(FindObjectsSortMode.None));
            signs.Sort((a, b) => string.CompareOrdinal(BeamNGEditorUtilities.HierarchyPath(a.transform), BeamNGEditorUtilities.HierarchyPath(b.transform)));
            var segs = new List<string>(); int n = 0, noRoad = 0, visualOnly = 0;
            foreach (var sg in signs)
            {
                if (!sg.gameObject.activeInHierarchy || !sg.exportEnabled) continue;
                if (!sg.affectNavigation) { visualOnly++; continue; }
                var fwd = sg.transform.forward; fwd.y = 0f; if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward; fwd.Normalize();
                var pts = Walk(c, sg.transform.position, fwd, sg.lengthMeters, out var width);
                if (pts == null || pts.Count < 2) { noRoad++; continue; }
                string id = "spd_" + BeamNGEditorUtilities.SafeId(sg.name).ToLowerInvariant() + "_" + (n++);
                var names = new List<string>();
                for (int i = 0; i < pts.Count; i++)
                {
                    string wn = id + "_" + i; names.Add(BeamNGJson.Q(wn));
                    var p = BeamNGCoordinateConverter.Position(pts[i]);
                    c.WaypointLines.Add(new JsonObj().Str("class", "BeamNGWaypoint").Str("name", wn).Str("__parent", "SpeedLimits")
                        .Arr("position", p.x, p.y, p.z).Raw("rotationMatrix", "[1,0,0,0,1,0,0,0,1]")
                        .Arr("scale", 2f, 2f, 2f).Bool("drawDebug", false).Bool("excludeFromMap", false).ToString());
                }
                segs.Add(BeamNGJson.Q(id) + ":" + new JsonObj().Raw("nodes", "[" + string.Join(",", names) + "]").Num("speedLimit", sg.MetersPerSecond).Num("drivability", 1f).ToString());
            }
            // ----- speed zones: limit for the road between two nodes -----
            var zones = new List<BeamNGSpeedZone>(Object.FindObjectsByType<BeamNGSpeedZone>(FindObjectsSortMode.None));
            zones.Sort((a, b) => string.CompareOrdinal(BeamNGEditorUtilities.HierarchyPath(a.transform), BeamNGEditorUtilities.HierarchyPath(b.transform)));
            int zoneBad = 0, zoneCount = 0;
            foreach (var z in zones)
            {
                if (!z.gameObject.activeInHierarchy || !z.exportEnabled) continue;
                float zw;
                var zp = ZonePoints(c, z, out zw);
                if (zp == null || zp.Count < 2) { zoneBad++; continue; }
                string zid = "zone_" + BeamNGEditorUtilities.SafeId(z.name).ToLowerInvariant() + "_" + (n++);
                var znames = new List<string>();
                for (int i = 0; i < zp.Count; i++)
                {
                    string wn = zid + "_" + i; znames.Add(BeamNGJson.Q(wn));
                    var p = BeamNGCoordinateConverter.Position(zp[i]);
                    c.WaypointLines.Add(new JsonObj().Str("class", "BeamNGWaypoint").Str("name", wn).Str("__parent", "SpeedLimits")
                        .Arr("position", p.x, p.y, p.z).Raw("rotationMatrix", "[1,0,0,0,1,0,0,0,1]")
                        .Arr("scale", 2f, 2f, 2f).Bool("drawDebug", false).Bool("excludeFromMap", false).ToString());
                }
                segs.Add(BeamNGJson.Q(zid) + ":" + new JsonObj().Raw("nodes", "[" + string.Join(",", znames) + "]").Num("speedLimit", z.MetersPerSecond).Num("drivability", 1f).ToString());
                zoneCount++;
            }
            if (zoneCount > 0) r.Add(ReportStatus.PASS, "Traffic", $"{zoneCount} speed zone(s) written (limit applies to the road between the start and end node).");
            if (zoneBad > 0) r.Add(ReportStatus.WARNING, "Traffic", $"{zoneBad} speed zone(s) not exported: start and end node must both be next to the SAME marked road.", "Assign both nodes and mark the road in the Roads tab.");
            if (segs.Count > 0)
            {
                File.WriteAllText(c.Abs("map.json"), "{\n\"segments\":{\n" + string.Join(",\n", segs) + "\n}\n}\n", new UTF8Encoding(false));
                c.Require("map.json");
                r.Add(ReportStatus.PASS, "Traffic", $"{segs.Count} speed limit(s) written to map.json (speedLimit in m/s on a chain of waypoints along the road in front of the sign).");
                r.Add(ReportStatus.MANUAL, "Traffic", "Speed limits follow the BeamNG docs (map.json segments with speedLimit + BeamNGWaypoint nodes merged into the road network). BeamNG's docs do not describe sign objects, so how the game shows the limit to the driver is untested.", "Check the AI speed / the game's speed limit display on that road.");
            }
            if (noRoad > 0) r.Add(ReportStatus.WARNING, "Traffic", $"{noRoad} speed sign(s) have no marked road within {BeamNGSignalExporter.SnapRange:F0} m: the limit is not applied (the sign is only a model).", "Mark the road in the Roads tab or move the sign next to the road.");
            if (visualOnly > 0) r.Add(ReportStatus.PASS, "Traffic", $"{visualOnly} speed sign(s) are visual only (affectNavigation off).");
        }

        static bool Project(BeamNGExportContext c, Vector3 pos, out RoadPath path, out int seg, out float t)
        {
            path = null; seg = -1; t = 0f; float best = BeamNGSignalExporter.SnapRange * BeamNGSignalExporter.SnapRange;
            foreach (var rp in c.RoadPaths)
                for (int i = 0; i + 1 < rp.Points.Count; i++)
                {
                    var a = rp.Points[i]; var ab = rp.Points[i + 1] - a; ab.y = 0f; float l2 = ab.sqrMagnitude; if (l2 < 1e-6f) continue;
                    var ap = pos - a; ap.y = 0f;
                    float tt = Mathf.Clamp01(Vector3.Dot(ap, ab) / l2);
                    var d = pos - (a + (rp.Points[i + 1] - a) * tt); d.y = 0f;
                    if (d.sqrMagnitude < best) { best = d.sqrMagnitude; path = rp; seg = i; t = tt; }
                }
            return path != null;
        }

        static Vector3 PointAt(List<Vector3> P, float k) { int i = Mathf.Clamp(Mathf.FloorToInt(k), 0, P.Count - 2); return P[i] + (P[i + 1] - P[i]) * Mathf.Clamp01(k - i); }

        static List<Vector3> ZonePoints(BeamNGExportContext c, BeamNGSpeedZone z, out float width)
        {
            width = 6f;
            if (z.startNode == null || z.endNode == null) return null;
            RoadPath p1, p2; int s1, s2; float t1, t2;
            if (!Project(c, z.startNode.position, out p1, out s1, out t1) || !Project(c, z.endNode.position, out p2, out s2, out t2) || p1 != p2) return null;
            width = p1.Width > 0f ? p1.Width : 6f;
            var P = p1.Points;
            float k1 = s1 + t1, k2 = s2 + t2; bool rev = k1 > k2; if (rev) { var tmp = k1; k1 = k2; k2 = tmp; }
            var poly = new List<Vector3> { PointAt(P, k1) };
            for (int i = Mathf.FloorToInt(k1) + 1; i <= Mathf.FloorToInt(k2) && i < P.Count; i++) poly.Add(P[i]);
            poly.Add(PointAt(P, k2));
            var res = new List<Vector3> { poly[0] }; float acc = 0f, next = Step;
            for (int i = 0; i + 1 < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[i + 1]; float seg = Vector3.Distance(a, b);
                while (next <= acc + seg) { res.Add(Vector3.Lerp(a, b, (next - acc) / Mathf.Max(0.001f, seg))); next += Step; }
                acc += seg;
            }
            res.Add(poly[poly.Count - 1]);
            return res;
        }

        /// <summary>Points (Unity space) along the nearest road starting at the sign, in the direction of <paramref name="fwd"/>.</summary>
        static List<Vector3> Walk(BeamNGExportContext c, Vector3 pos, Vector3 fwd, float length, out float width)
        {
            width = 6f; RoadPath bestPath = null; int bestSeg = -1; float bestT = 0f; float best = BeamNGSignalExporter.SnapRange * BeamNGSignalExporter.SnapRange;
            foreach (var rp in c.RoadPaths)
                for (int i = 0; i + 1 < rp.Points.Count; i++)
                {
                    var a = rp.Points[i]; var b = rp.Points[i + 1];
                    var ab = b - a; ab.y = 0f; float len2 = ab.sqrMagnitude; if (len2 < 1e-6f) continue;
                    var ap = pos - a; ap.y = 0f;
                    float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / len2);
                    var q = a + (b - a) * t; var d = pos - q; d.y = 0f;
                    if (d.sqrMagnitude < best) { best = d.sqrMagnitude; bestPath = rp; bestSeg = i; bestT = t; }
                }
            if (bestPath == null) return null;
            width = bestPath.Width > 0f ? bestPath.Width : 6f;
            var P = bestPath.Points;
            var tan = P[bestSeg + 1] - P[bestSeg]; tan.y = 0f;
            bool forward = Vector3.Dot(tan.normalized, fwd) >= 0f;
            var start = P[bestSeg] + (P[bestSeg + 1] - P[bestSeg]) * bestT;
            var poly = new List<Vector3> { start };
            if (forward) for (int i = bestSeg + 1; i < P.Count; i++) poly.Add(P[i]);
            else for (int i = bestSeg; i >= 0; i--) poly.Add(P[i]);
            // resample every Step metres up to 'length'
            var res = new List<Vector3> { start }; float acc = 0f, next = Step;
            for (int i = 0; i + 1 < poly.Count && acc < length; i++)
            {
                var a = poly[i]; var b = poly[i + 1]; float seg = Vector3.Distance(a, b);
                while (next <= acc + seg && next <= length) { res.Add(Vector3.Lerp(a, b, (next - acc) / Mathf.Max(0.001f, seg))); next += Step; }
                acc += seg;
            }
            if (res.Count < 2 && poly.Count >= 2) res.Add(poly[poly.Count - 1]);
            return res;
        }
    }
}
