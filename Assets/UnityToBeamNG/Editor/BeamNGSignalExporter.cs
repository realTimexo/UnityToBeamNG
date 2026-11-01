using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace UnityToBeamNG
{
    /// <summary>
    /// Writes levels/&lt;name&gt;/signals.json (instances / controllers / sequences) per the BeamNG docs.
    /// Each phase of a BeamNGTrafficIntersection becomes one "lightsBasic" controller (green, yellow, red);
    /// the phases of an intersection form one sequence and therefore take turns.
    /// </summary>
    public static class BeamNGSignalExporter
    {
        static bool AddLamp(BeamNGExportContext c, JsonObj lamp, string role, Transform t, string signalName)
        {
            var names = new List<string>();
            if (t != null)
            {
                int k = 0;
                foreach (var mr in t.GetComponentsInChildren<MeshRenderer>(true))
                {
                    string n = signalName + "_" + role + "_" + (k++);
                    c.LampNames[mr] = n; names.Add(n);
                }
            }
            lamp.Raw(role, BeamNGJson.AS(names));
            return names.Count > 0;
        }

        public const float SnapRange = 35f;
        static int snapped, notSnapped;

        /// <summary>BeamNG assigns a signal to the nearest road of the navigation graph: place it on the road centre line (Unity space) and align its direction with the road.</summary>
        public static void SnapToRoad(BeamNGExportContext c, Vector3 pos, Vector3 fwd, out Vector3 sp, out Vector3 sf)
        {
            sp = pos; sf = fwd;
            float best = SnapRange * SnapRange; bool found = false;
            foreach (var rp in c.RoadPaths)
                for (int i = 0; i + 1 < rp.Points.Count; i++)
                {
                    var a = rp.Points[i]; var b = rp.Points[i + 1];
                    var ab = b - a; ab.y = 0f; float len2 = ab.sqrMagnitude; if (len2 < 1e-6f) continue;
                    var ap = pos - a; ap.y = 0f;
                    float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / len2);
                    var q = a + (b - a) * t;
                    var d = pos - q; d.y = 0f;
                    float d2 = d.sqrMagnitude;
                    if (d2 < best)
                    {
                        best = d2; found = true; sp = q;
                        var tan = ab.normalized; if (Vector3.Dot(tan, fwd) < 0f) tan = -tan;
                        sf = tan;
                    }
                }
            if (found) snapped++; else notSnapped++;
        }

        public static void Export(BeamNGExportContext c)
        {
            snapped = 0; notSnapped = 0;
            var r = c.Report;
            var inters = new List<BeamNGTrafficIntersection>(Object.FindObjectsByType<BeamNGTrafficIntersection>(FindObjectsSortMode.None));
            inters.Sort((a, b) => string.CompareOrdinal(BeamNGEditorUtilities.HierarchyPath(a.transform), BeamNGEditorUtilities.HierarchyPath(b.transform)));

            var covered = new HashSet<BeamNGTrafficLight>();
            var instances = new List<string>(); var controllers = new List<string>(); var sequences = new List<string>();
            int id = 1, interCount = 0;
            var usedNames = new HashSet<string>();
            var lampSignals = new List<string>();

            foreach (var inter in inters)
            {
                if (!inter.gameObject.activeInHierarchy || !inter.exportEnabled) continue;
                var lights = new List<BeamNGTrafficLight>();
                foreach (var l in inter.GetComponentsInChildren<BeamNGTrafficLight>(false)) lights.Add(l);
                if (lights.Count == 0) { r.Add(ReportStatus.WARNING, "Traffic", $"Intersection '{inter.name}' has no BeamNGTrafficLight below it.", "Create the lights as children."); continue; }
                lights.Sort((a, b) => string.CompareOrdinal(BeamNGEditorUtilities.HierarchyPath(a.transform), BeamNGEditorUtilities.HierarchyPath(b.transform)));

                var phases = new SortedDictionary<int, int>();   // phase number -> controller id
                var interName = BeamNGEditorUtilities.SafeId(inter.name);
                foreach (var l in lights) phases[l.phase] = 0;
                var phaseKeys = new List<int>(phases.Keys);
                var seqPhases = new List<string>();
                int seqId = id++;
                foreach (var k in phaseKeys)
                {
                    int cid = id++; phases[k] = cid;
                    var states = new List<string>
                    {
                        new JsonObj().Str("state", "greenTrafficLight").Num("duration", inter.greenSeconds).ToString(),
                        new JsonObj().Str("state", "yellowTrafficLight").Num("duration", inter.yellowSeconds).ToString(),
                        new JsonObj().Str("state", "redTrafficLight").Num("duration", inter.allRedSeconds).ToString()
                    };
                    controllers.Add(new JsonObj().Num("id", cid).Str("name", interName + "_phase" + k).Str("type", "lightsBasic")
                        .Bool("isSimple", false).Raw("states", "[" + string.Join(",", states) + "]").ToString());
                    seqPhases.Add(new JsonObj().Raw("controllerIds", "[" + cid + "]").ToString());
                }
                sequences.Add(new JsonObj().Num("id", seqId).Str("name", interName + "_sequence").Num("startTime", 0f)
                    .Bool("startDisabled", false).Bool("ignoreTimer", false).Raw("phases", "[" + string.Join(",", seqPhases) + "]").ToString());

                int phaseCount = phaseKeys.Count;
                foreach (var l in lights)
                {
                    covered.Add(l);
                    string name = interName + "_" + BeamNGEditorUtilities.SafeId(l.name); int n = 2; string baseName = name;
                    while (!usedNames.Add(name)) name = baseName + "_" + (n++);
                    var f = l.transform.forward; f.y = 0f; if (f.sqrMagnitude < 1e-6f) f = Vector3.forward; f.Normalize();
                    Vector3 sp0, sf0; SnapToRoad(c, l.transform.position, f, out sp0, out sf0);
                    var pos = BeamNGCoordinateConverter.Position(sp0);
                    var dir = BeamNGCoordinateConverter.Direction(sf0);
                    instances.Add(new JsonObj().Num("id", id++).Str("name", name).Num("controllerId", phases[l.phase]).Num("sequenceId", seqId)
                        .Arr("pos", pos.x, pos.y, pos.z).Arr("dir", dir.x, dir.y, dir.z).Str("group", interName).Bool("startDisabled", false).ToString());
                    if (l.linkVisual) c.SignalNames[l] = name;

                    var lamp = new JsonObj().Str("name", name)
                        .Num("phaseIndex", phaseKeys.IndexOf(l.phase)).Num("phaseCount", phaseCount)
                        .Num("greenSec", inter.greenSeconds).Num("yellowSec", inter.yellowSeconds).Num("allRedSec", inter.allRedSeconds);
                    bool anyLamp = false;
                    anyLamp |= AddLamp(c, lamp, "red", l.redLamp, name);
                    anyLamp |= AddLamp(c, lamp, "yellow", l.yellowLamp, name);
                    anyLamp |= AddLamp(c, lamp, "green", l.greenLamp, name);
                    if (anyLamp) lampSignals.Add(lamp.ToString());
                }
                interCount++;
            }

            foreach (var l in Object.FindObjectsByType<BeamNGTrafficLight>(FindObjectsSortMode.None))
                if (l.gameObject.activeInHierarchy && !covered.Contains(l))
                    r.Add(ReportStatus.WARNING, "Traffic", $"Traffic light '{l.name}' is not below a BeamNGTrafficIntersection and is ignored.", "Move it below an intersection object.");

            // ----- stop signs (controller type "signStop", state "basicStop", sequenceId 0 - per the signals.json docs) -----
            int stopCtrl = 0, signCount = 0;
            var signs = new List<BeamNGTrafficSign>(Object.FindObjectsByType<BeamNGTrafficSign>(FindObjectsSortMode.None));
            signs.Sort((a, b) => string.CompareOrdinal(BeamNGEditorUtilities.HierarchyPath(a.transform), BeamNGEditorUtilities.HierarchyPath(b.transform)));
            foreach (var sg in signs)
            {
                if (!sg.gameObject.activeInHierarchy || !sg.exportEnabled) continue;
                if (stopCtrl == 0)
                {
                    stopCtrl = id++;
                    controllers.Add(new JsonObj().Num("id", stopCtrl).Str("name", "stop").Str("type", "signStop").Bool("isSimple", true)
                        .Raw("states", "[" + new JsonObj().Str("state", "basicStop") + "]").ToString());
                }
                string sname = "stop_" + BeamNGEditorUtilities.SafeId(sg.name); int sn2 = 2; string sbase = sname;
                while (!usedNames.Add(sname)) sname = sbase + "_" + (sn2++);
                var sf = sg.transform.forward; sf.y = 0f; if (sf.sqrMagnitude < 1e-6f) sf = Vector3.forward; sf.Normalize();
                Vector3 sp1, sf1; SnapToRoad(c, sg.transform.position, sf, out sp1, out sf1);
                var spos = BeamNGCoordinateConverter.Position(sp1);
                var sdir = BeamNGCoordinateConverter.Direction(sf1);
                instances.Add(new JsonObj().Num("id", id++).Str("name", sname).Num("controllerId", stopCtrl).Num("sequenceId", 0)
                    .Arr("pos", spos.x, spos.y, spos.z).Arr("dir", sdir.x, sdir.y, sdir.z).Str("group", "stop_signs").Bool("startDisabled", false).ToString());
                signCount++;
            }
            if (signCount > 0)
                r.Add(ReportStatus.PASS, "Traffic", $"{signCount} stop sign(s) exported as signals (the AI stops at them if they are near a road of the road network).");

            if (instances.Count == 0) return;
            var sb = new StringBuilder();
            sb.Append("{\n\"instances\":[\n").Append(string.Join(",\n", instances)).Append("\n],\n");
            sb.Append("\"controllers\":[\n").Append(string.Join(",\n", controllers)).Append("\n],\n");
            sb.Append("\"sequences\":[\n").Append(string.Join(",\n", sequences)).Append("\n]\n}\n");
            File.WriteAllText(c.Abs("signals.json"), sb.ToString(), new UTF8Encoding(false));
            c.Require("signals.json");
            if (lampSignals.Count > 0)
            {
                File.WriteAllText(c.Abs("signal_lamps.json"), "{\"signals\":[\n" + string.Join(",\n", lampSignals) + "\n]}\n", new UTF8Encoding(false));
                c.Require("signal_lamps.json");
                File.WriteAllText(c.AbsRoot(BeamNGLuaFiles.ModScriptPath), BeamNGLuaFiles.ModScript, new UTF8Encoding(false));
                File.WriteAllText(c.AbsRoot(BeamNGLuaFiles.ExtensionPath), BeamNGLuaFiles.Extension, new UTF8Encoding(false));
                r.Add(ReportStatus.PASS, "Traffic", $"{lampSignals.Count} signal(s) with lamp objects: signal_lamps.json + Lua extension (switches red/yellow/green lamps in game).");
            }
            r.Add(ReportStatus.PASS, "Traffic", $"signals.json: {interCount} intersection(s), {instances.Count} signal(s), {controllers.Count} controller(s).");
            if (snapped + notSnapped > 0)
                r.Add(notSnapped == 0 ? ReportStatus.PASS : ReportStatus.WARNING, "Traffic", $"{snapped} signal(s)/sign(s) placed on the nearest road centre line; {notSnapped} have no road within {SnapRange:F0} m and stay where they are.", notSnapped > 0 ? "Mark the roads next to them (Roads tab) or move them closer to the road." : "");
            if (c.RoadPaths.Count == 0)
                r.Add(ReportStatus.WARNING, "Traffic", "Signals only work on roads of the road network, and this map has no road marked (BeamNG log: 'Map node could not be set for signal instance').", "Mark your roads in the Roads tab.");
        }
    }
}
