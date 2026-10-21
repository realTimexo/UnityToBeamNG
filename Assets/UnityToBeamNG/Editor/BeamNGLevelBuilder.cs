using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace UnityToBeamNG
{
    public class ResolvedSpawn
    {
        public SpawnInfo Info;
        public Vector3 UnityPos;      // position after ground snap
        public Quaternion UnityRot;
    }

    /// <summary>Creates info.json, preview.png and main/**/items.level.json according to the BeamNG docs.</summary>
    public static class BeamNGLevelBuilder
    {
        const string MG = "main/MissionGroup/";

        public static string StaticLine(string name, string shapeVPath, Vector3 pos, float[] rot, Vector3 scale, string collisionType)
        {
            return new JsonObj().Str("class", "TSStatic").Str("name", name).Str("__parent", "Buildings")
                .Str("shapeName", shapeVPath).Arr("position", pos.x, pos.y, pos.z).Raw("rotationMatrix", BeamNGJson.A(rot))
                .Arr("scale", scale.x, scale.y, scale.z).Str("collisionType", collisionType).ToString();
        }

        static void WriteLines(BeamNGExportContext c, string rel, List<string> lines)
        {
            File.WriteAllText(c.Abs(rel), string.Join("\n", lines) + "\n", new UTF8Encoding(false));
            c.Require(rel);
        }

        public static List<ResolvedSpawn> ResolveSpawns(BeamNGExportContext c, ScanResult scan)
        {
            var s = c.Settings;
            var list = new List<ResolvedSpawn>();
            UnityEngine.Physics.SyncTransforms();
            foreach (var sp in scan.Spawns)
            {
                var pos = sp.T.position;
                if (s.snapSpawnToGround)
                {
                    bool snapped = false;
                    var hits = Physics.RaycastAll(pos + Vector3.up * 1f, Vector3.down, 10000f, ~0, QueryTriggerInteraction.Ignore);
                    float bestDist = float.MaxValue; Vector3 bestPoint = pos;
                    foreach (var h in hits)
                    {
                        if (h.collider.transform.IsChildOf(sp.T)) continue;
                        if (h.distance < bestDist) { bestDist = h.distance; bestPoint = h.point; snapped = true; }
                    }
                    if (!snapped)
                    {
                        foreach (var t in scan.Terrains)
                        {
                            var tp = t.transform.position; var sz = t.terrainData.size;
                            if (pos.x >= tp.x && pos.x <= tp.x + sz.x && pos.z >= tp.z && pos.z <= tp.z + sz.z)
                            { bestPoint = new Vector3(pos.x, tp.y + t.SampleHeight(pos), pos.z); snapped = true; break; }
                        }
                    }
                    if (snapped) pos = new Vector3(pos.x, bestPoint.y + s.spawnClearance, pos.z);
                    else c.Report.Add(ReportStatus.WARNING, "Spawn", $"'{sp.Name}': no ground found below the spawn point, position left unchanged.", "Place the spawn above a terrain/collider.");
                }
                list.Add(new ResolvedSpawn { Info = sp, UnityPos = pos, UnityRot = sp.T.rotation });
            }
            return list;
        }

        public static string Build(BeamNGExportContext c, ScanResult scan, TerrainResult terrain, List<string> staticLines, List<ResolvedSpawn> spawns)
        {
            var s = c.Settings;
            var r = c.Report;

            // --- main/items.level.json: MissionGroup only; children live in main/MissionGroup/... (template layout per docs) ---
            WriteLines(c, "main/items.level.json", new List<string>
            {
                new JsonObj().Str("name", "MissionGroup").Str("class", "SimGroup").Str("enabled", "1").ToString()
            });
            var groups = new List<string>
            {
                new JsonObj().Str("class", "SimGroup").Str("name", "Environment").Str("__parent", "MissionGroup").ToString()
            };
            if (terrain != null) groups.Add(new JsonObj().Str("class", "SimGroup").Str("name", "Terrain").Str("__parent", "MissionGroup").ToString());
            if (staticLines != null && staticLines.Count > 0) groups.Add(new JsonObj().Str("class", "SimGroup").Str("name", "Buildings").Str("__parent", "MissionGroup").ToString());
            if (c.RoadLines.Count > 0) groups.Add(new JsonObj().Str("class", "SimGroup").Str("name", "Roads").Str("__parent", "MissionGroup").ToString());
            groups.Add(new JsonObj().Str("class", "SimGroup").Str("name", "Gameplay").Str("__parent", "MissionGroup").ToString());
            WriteLines(c, MG + "items.level.json", groups);

            // --- Environment ---
            var env = new List<string>
            {
                new JsonObj().Str("name", "theLevelInfo").Str("class", "LevelInfo").Str("__parent", "Environment")
                    .Num("nearClip", 0.1f).Num("visibleDistance", s.visibleDistance).Num("fogDensity", 0.0006f).Num("fogAtmosphereHeight", 800f)
                    .Num("gravity", -9.80665f).Str("globalEnviromentMap", "BNG_Sky_02_cubemap").ToString(),
                new JsonObj().Str("name", "tod").Str("class", "TimeOfDay").Str("__parent", "Environment")
                    .Num("startTime", s.timeOfDay).Num("time", s.timeOfDay).Bool("play", false).Num("axisTilt", 23.44f).Num("dayLength", 1800f)
                    .Num("latitude", 47f).Num("longitude", 8f).Num("year", 2026f).Num("month", 6f).Num("day", 20f)
                    .Str("utcOffset", "2").Str("celestialProfile", "earth").ToString(),
                new JsonObj().Str("name", "sunsky").Str("class", "ScatterSky").Str("__parent", "Environment").ToString()
            };
            WriteLines(c, MG + "Environment/items.level.json", env);

            // --- Terrain ---
            var terLines = new List<string>();
            if (terrain != null)
            {
                var tb = new JsonObj().Str("class", "TerrainBlock").Str("name", "theTerrain").Str("__parent", "Terrain")
                    .Str("terrainFile", c.VPath(TerrainResult.TerFile))
                    .Arr("position", terrain.PositionBeam.x, terrain.PositionBeam.y, terrain.PositionBeam.z)
                    .Arr("rotationMatrix", 1, 0, 0, 0, 1, 0, 0, 0, 1)
                    .Num("squareSize", terrain.SquareSize).Num("maxHeight", terrain.MaxHeight).Bool("castShadows", true);
                if (!string.IsNullOrEmpty(terrain.TextureSetName)) tb.Str("materialTextureSet", terrain.TextureSetName);
                terLines.Add(tb.ToString());
            }
            if (terLines.Count > 0) WriteLines(c, MG + "Terrain/items.level.json", terLines);

            // --- Buildings ---
            if (staticLines != null && staticLines.Count > 0) WriteLines(c, MG + "Buildings/items.level.json", staticLines);

            // --- Roads (invisible DecalRoads = AI navigation network) ---
            if (c.RoadLines.Count > 0) WriteLines(c, MG + "Roads/items.level.json", c.RoadLines);

            // --- Gameplay: Spawnpunkte ---
            var gp = new List<string>();
            foreach (var sp in spawns)
            {
                var posB = BeamNGCoordinateConverter.Position(sp.UnityPos);
                var rot = BeamNGCoordinateConverter.RotationMatrix(BeamNGCoordinateConverter.Rotation(sp.UnityRot));
                gp.Add(new JsonObj().Str("class", "SpawnSphere").Str("name", sp.Info.Name).Str("__parent", "Gameplay")
                    .Arr("position", posB.x, posB.y, posB.z).Raw("rotationMatrix", BeamNGJson.A(rot)).Arr("scale", 1, 1, 1)
                    .Str("dataBlock", "SpawnSphereMarker").ToString());
            }
            WriteLines(c, MG + "Gameplay/items.level.json", gp);

            // --- preview.png ---
            var def = spawns.Find(x => x.Info.IsDefault) ?? spawns[0];
            RenderPreview(c, def);

            // --- info.json ---
            float size = terrain != null ? terrain.Side : 1024f;
            var spawnArr = new List<string>();
            foreach (var sp in spawns)
                spawnArr.Add(new JsonObj().Str("translationId", sp.Info.Display).Str("description", sp.Info.Display)
                    .Str("objectname", sp.Info.Name).Str("preview", "preview.png").ToString());
            var info = new JsonObj().Str("title", s.mapName).Str("description", s.description).Str("authors", s.author)
                .Str("version", s.version).Str("defaultSpawnPointName", def.Info.Name)
                .Raw("defaultDate", new JsonObj().Num("year", 2026).Num("month", 6).Num("day", 20).ToString())
                .Raw("size", BeamNGJson.A(Mathf.Round(size), Mathf.Round(size)))
                .Raw("previews", BeamNGJson.AS(new[] { "preview.png" }))
                .Raw("spawnPoints", "[" + string.Join(",", spawnArr) + "]");
            if (s.driveOnLeft) info.Raw("roadRules", new JsonObj().Bool("rightHandDrive", true).ToString());
            File.WriteAllText(c.Abs("info.json"), info.ToString(), new UTF8Encoding(false));
            c.Require("info.json");

            r.Add(ReportStatus.PASS, "Level", $"info.json, preview.png and main/MissionGroup/**/items.level.json written ({staticLines?.Count ?? 0} TSStatic, {spawns.Count} spawn point(s), default: '{def.Info.Name}').");
            r.Add(ReportStatus.WARNING, "Level", "Structure follows the BeamNG docs (items.level.json, info.json); completeness (e.g. sky/lighting details) is untested.", "Check the log on first load.");
            return def.Info.Name;
        }

        static void RenderPreview(BeamNGExportContext c, ResolvedSpawn sp)
        {
            var path = c.Abs("preview.png");
            c.Require("preview.png");
            GameObject go = null; RenderTexture rt = null; Texture2D tex = null;
            try
            {
                go = new GameObject("__bng_preview_cam") { hideFlags = HideFlags.HideAndDontSave };
                var cam = go.AddComponent<Camera>();
                cam.fieldOfView = 60f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 4000f; cam.clearFlags = CameraClearFlags.Skybox;
                var fwd = sp.UnityRot * Vector3.forward; fwd.y = 0; if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward; fwd.Normalize();
                go.transform.position = sp.UnityPos + Vector3.up * 3f - fwd * 8f;
                go.transform.rotation = Quaternion.LookRotation(fwd + Vector3.down * 0.15f);
                rt = RenderTexture.GetTemporary(1280, 720, 24);
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            catch (Exception e)
            {
                var t = new Texture2D(16, 9, TextureFormat.RGB24, false);
                var px = new Color[16 * 9]; for (int i = 0; i < px.Length; i++) px[i] = new Color(0.2f, 0.3f, 0.4f);
                t.SetPixels(px); File.WriteAllBytes(path, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
                c.Report.Add(ReportStatus.WARNING, "Level", "Preview image could not be rendered (placeholder used): " + e.Message, "Replace preview.png in the mod later.");
            }
            finally
            {
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            }
        }
    }
}
