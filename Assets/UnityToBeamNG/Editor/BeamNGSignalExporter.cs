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

        public static void Export(BeamNGExportContext c)
        {
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
                    var pos = BeamNGCoordinateConverter.Position(l.transform.position);
                    var f = l.transform.forward; f.y = 0f; if (f.sqrMagnitude < 1e-6f) f = Vector3.forward; f.Normalize();
                    var dir = BeamNGCoordinateConverter.Direction(f);
                    instances.Add(new JsonObj().Num("id", id++).Str("name", name).Num("controllerId", phases[l.phase]).Num("sequenceId", seqId)
                        .Arr("pos", pos.x, pos.y, pos.z).Arr("dir", dir.x, dir.y, dir.z).Str("group", interName).Bool("startDisabled", false).ToString());
                    if (l.linkVisual) c.SignalNames[l] = name;

                    var lamp = new JsonObj().Str("name", name)
                        .Num("phaseIndex", phaseKeys.IndexOf(l.phase)).Num("phaseCount", phaseCount)
                        .Num("green", inter.greenSeconds).Num("yellow", inter.yellowSeconds).Num("allRed", inter.allRedSeconds);
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
                r.Add(ReportStatus.WARNING, "Traffic", "Lamp switching uses a Lua extension that reads the signal state from core_trafficSignals (API names unverified; falls back to the exported timing).", "Check the BeamNG log for 'unityToBeamNG'.");
            }
            r.Add(ReportStatus.PASS, "Traffic", $"signals.json: {interCount} intersection(s), {instances.Count} signal(s), {controllers.Count} controller(s).");
            r.Add(ReportStatus.WARNING, "Traffic", "Signal logic follows the BeamNG docs (controller type 'lightsBasic') but is untested in game. Lights only act on roads that are part of the AI road network; place each light near the stop line.", "Check the BeamNG log and the signals editor (World Editor).");
        }
    }
}
