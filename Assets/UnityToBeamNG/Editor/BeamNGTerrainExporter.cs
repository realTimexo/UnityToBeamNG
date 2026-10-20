using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace UnityToBeamNG
{
    public class TerrainResult
    {
        public int Size;
        public float SquareSize;
        public float MaxHeight;
        public float Side;
        public Vector3 PositionBeam;           // corner (min x, min y, base height) in BeamNG coordinates
        public List<string> MaterialNames = new List<string>();
        public const string TerFile = "theTerrain.ter";
    }

    /// <summary>
    /// Writes the BeamNG terrain in the documented .ter format (version 9):
    /// u8 version | u32 size | u16[size^2] heights | u8[size^2] layers | u32 material count | material names.
    /// Multiple Unity terrains are merged into ONE square terrain.
    /// UNVERIFIED: material name encoding (here: u8 length + ASCII) and row order (row 0 = smallest Z).
    /// Additionally a 16-bit PNG heightmap for manual import in the World Editor is written to extras/.
    /// </summary>
    public static class BeamNGTerrainExporter
    {
        static string GuidFor(string s)
        {
            using (var md5 = MD5.Create())
                return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(s))).ToString();
        }

        static string GroundModelFor(string layerName)
        {
            var n = layerName.ToLowerInvariant();
            if (n.Contains("asphalt") || n.Contains("road") || n.Contains("street") || n.Contains("tarmac")) return "asphalt";
            if (n.Contains("sand") || n.Contains("beach")) return "sand";
            if (n.Contains("rock") || n.Contains("stone") || n.Contains("cliff")) return "rock";
            if (n.Contains("dirt") || n.Contains("mud") || n.Contains("soil") || n.Contains("gravel")) return "dirt";
            return "grass";
        }

        public static TerrainResult Export(BeamNGExportContext c, List<Terrain> terrains, BeamNGTextureExporter tex)
        {
            var s = c.Settings;
            var r = c.Report;
            int R = s.terrainResolution;

            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue, minY = float.MaxValue, top = float.MinValue;
            foreach (var t in terrains)
            {
                var p = t.transform.position; var sz = t.terrainData.size;
                minX = Mathf.Min(minX, p.x); minZ = Mathf.Min(minZ, p.z);
                maxX = Mathf.Max(maxX, p.x + sz.x); maxZ = Mathf.Max(maxZ, p.z + sz.z);
                minY = Mathf.Min(minY, p.y); top = Mathf.Max(top, p.y + sz.y);
            }
            float side = Mathf.Max(maxX - minX, maxZ - minZ);
            float baseY = minY;
            float maxH = Mathf.Max(1f, top - baseY);
            float sq = side / R;

            var res = new TerrainResult { Size = R, SquareSize = sq, MaxHeight = maxH, Side = side, PositionBeam = BeamNGCoordinateConverter.Position(new Vector3(minX, baseY, minZ)) };

            // --- Layers/materials ---
            var layers = new List<TerrainLayer>();
            var names = res.MaterialNames;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var layerIdx = new Dictionary<TerrainLayer, int>();
            int fallbackIdx = -1;
            Func<int> fallback = () =>
            {
                if (fallbackIdx < 0) { fallbackIdx = names.Count; names.Add("unity_default"); used.Add("unity_default"); layers.Add(null); }
                return fallbackIdx;
            };
            var tileMap = new int[terrains.Count][];
            bool clamped = false;
            for (int ti = 0; ti < terrains.Count; ti++)
            {
                var ls = terrains[ti].terrainData.terrainLayers ?? new TerrainLayer[0];
                tileMap[ti] = new int[ls.Length];
                for (int l = 0; l < ls.Length; l++)
                {
                    var tl = ls[l];
                    if (tl == null) { tileMap[ti][l] = fallback(); continue; }
                    int gi;
                    if (!layerIdx.TryGetValue(tl, out gi))
                    {
                        var b = BeamNGEditorUtilities.SafeId(tl.name); var n = b; int k = 2;
                        while (used.Contains(n)) n = b + "_" + (k++);
                        used.Add(n); gi = names.Count; names.Add(n); layers.Add(tl); layerIdx[tl] = gi;
                    }
                    if (gi > 253) { gi = 253; clamped = true; }
                    tileMap[ti][l] = gi;
                }
            }
            if (names.Count == 0) fallback();
            if (clamped) r.Add(ReportStatus.WARNING, "Terrain", "More than 254 terrain layers: surplus layers are mapped to layer 253.");

            // --- Heights ---
            var hm = new ushort[R * R];
            var cand = new List<int>();
            for (int row = 0; row < R; row++)
            {
                if (row % 32 == 0 && c.Cancelled("Terrain heights " + row + "/" + R, 0.1f + 0.15f * row / R)) return null;
                float wz = minZ + row * sq;
                cand.Clear();
                for (int ti = 0; ti < terrains.Count; ti++)
                {
                    float pz = terrains[ti].transform.position.z;
                    if (wz >= pz && wz <= pz + terrains[ti].terrainData.size.z) cand.Add(ti);
                }
                for (int col = 0; col < R; col++)
                {
                    float wx = minX + col * sq;
                    int found = -1;
                    foreach (int ti in cand)
                    {
                        float px = terrains[ti].transform.position.x;
                        if (wx >= px && wx <= px + terrains[ti].terrainData.size.x) { found = ti; break; }
                    }
                    if (found < 0) continue; // 0 = base height, marked as hole in the layer map
                    var t = terrains[found]; var td = t.terrainData; var p = t.transform.position;
                    float u = Mathf.Clamp01((wx - p.x) / td.size.x);
                    float v = Mathf.Clamp01((wz - p.z) / td.size.z);
                    float hw = p.y + td.GetInterpolatedHeight(u, v);
                    int q = Mathf.Clamp(Mathf.RoundToInt((hw - baseY) / maxH * 65536f), 0, 65535);
                    hm[row * R + col] = (ushort)q;
                }
            }

            // --- Write .ter ---
            var terPath = c.Abs(TerrainResult.TerFile);
            int holesTotal = 0;
            using (var fs = new FileStream(terPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                fs.WriteByte(9);
                fs.Write(BitConverter.GetBytes((uint)R), 0, 4);
                var rowBytes = new byte[R * 2];
                for (int row = 0; row < R; row++)
                {
                    int o = 0, bi = row * R;
                    for (int col = 0; col < R; col++) { ushort v = hm[bi + col]; rowBytes[o++] = (byte)(v & 0xFF); rowBytes[o++] = (byte)(v >> 8); }
                    fs.Write(rowBytes, 0, rowBytes.Length);
                }

                // Layer map
                int nt = terrains.Count;
                var alphaRowY = new int[nt]; var alphaRow = new float[nt][,,];
                var holeRowY = new int[nt]; var holeRow = new bool[nt][,];
                for (int i = 0; i < nt; i++) { alphaRowY[i] = -1; holeRowY[i] = -1; }
                var layerBytes = new byte[R];
                for (int row = 0; row < R; row++)
                {
                    if (row % 32 == 0 && c.Cancelled("Terrain layers " + row + "/" + R, 0.25f + 0.2f * row / R)) return null;
                    float wz = minZ + row * sq;
                    cand.Clear();
                    for (int ti = 0; ti < nt; ti++)
                    {
                        float pz = terrains[ti].transform.position.z;
                        if (wz >= pz && wz <= pz + terrains[ti].terrainData.size.z) cand.Add(ti);
                    }
                    for (int col = 0; col < R; col++)
                    {
                        float wx = minX + col * sq;
                        int found = -1;
                        foreach (int ti in cand)
                        {
                            float px = terrains[ti].transform.position.x;
                            if (wx >= px && wx <= px + terrains[ti].terrainData.size.x) { found = ti; break; }
                        }
                        if (found < 0) { layerBytes[col] = 255; holesTotal++; continue; }
                        var t = terrains[found]; var td = t.terrainData; var p = t.transform.position;
                        float u = Mathf.Clamp01((wx - p.x) / td.size.x);
                        float v = Mathf.Clamp01((wz - p.z) / td.size.z);

                        int hres = td.holesResolution;
                        if (hres > 0)
                        {
                            int hx = Mathf.Clamp(Mathf.RoundToInt(u * (hres - 1)), 0, hres - 1);
                            int hy = Mathf.Clamp(Mathf.RoundToInt(v * (hres - 1)), 0, hres - 1);
                            if (holeRowY[found] != hy) { holeRow[found] = td.GetHoles(0, hy, hres, 1); holeRowY[found] = hy; }
                            if (!holeRow[found][0, hx]) { layerBytes[col] = 255; holesTotal++; continue; }
                        }

                        int lc = tileMap[found].Length;
                        if (lc == 0) { layerBytes[col] = (byte)fallback(); continue; }
                        int ares = td.alphamapResolution;
                        int ax = Mathf.Clamp(Mathf.RoundToInt(u * (ares - 1)), 0, ares - 1);
                        int ay = Mathf.Clamp(Mathf.RoundToInt(v * (ares - 1)), 0, ares - 1);
                        if (alphaRowY[found] != ay) { alphaRow[found] = td.GetAlphamaps(0, ay, ares, 1); alphaRowY[found] = ay; }
                        var arr = alphaRow[found];
                        int best = 0; float bv = -1f;
                        int n = Mathf.Min(lc, arr.GetLength(2));
                        for (int l = 0; l < n; l++) { float a = arr[0, ax, l]; if (a > bv) { bv = a; best = l; } }
                        layerBytes[col] = (byte)tileMap[found][best];
                    }
                    fs.Write(layerBytes, 0, R);
                }

                fs.Write(BitConverter.GetBytes((uint)names.Count), 0, 4);
                foreach (var n in names)
                {
                    var bytes = Encoding.ASCII.GetBytes(n);
                    int len = Mathf.Min(bytes.Length, 255);
                    fs.WriteByte((byte)len);
                    fs.Write(bytes, 0, len);
                }
            }
            c.Require(TerrainResult.TerFile);

            // --- .terrain.json (descriptive only per docs) ---
            var tj = new JsonObj()
                .Num("version", 9).Str("datafile", c.VPath(TerrainResult.TerFile)).Num("size", R)
                .Str("binaryFormat", "version(u8) size(u32) heightMap(u16[size*size]) layerMap(u8[size*size]) materialCount(u32) materialNames")
                .Num("heightMapSize", (float)R * R).Num("heightMapItemSize", 2).Num("layerMapSize", (float)R * R).Num("layerMapItemSize", 1)
                .Raw("materials", BeamNGJson.AS(names));
            File.WriteAllText(c.Abs("theTerrain.terrain.json"), tj.ToString(), new UTF8Encoding(false));
            c.Require("theTerrain.terrain.json");

            // --- Terrain materials + textures ---
            var entries = new List<string>();
            for (int i = 0; i < names.Count; i++)
            {
                var tl = layers[i];
                var o = new JsonObj().Str("name", names[i]).Str("internalName", names[i]).Str("class", "TerrainMaterial")
                    .Str("persistentId", GuidFor(c.Settings.levelName + "/" + names[i]))
                    .Str("groundmodelName", tl != null ? GroundModelFor(tl.name) : "grass");
                if (tl != null && tl.diffuseTexture != null)
                {
                    int sz; var rel = tex.ExportColor(tl.diffuseTexture, "art/terrains", out sz);
                    if (rel != null) { o.Str("baseColorBaseTex", c.VPath(rel)); o.Num("baseColorBaseTexSize", sz); }
                }
                entries.Add(BeamNGJson.Q(names[i]) + ":" + o);
            }
            File.WriteAllText(c.Abs("art/terrains/main.materials.json"), "{\n" + string.Join(",\n", entries) + "\n}\n", new UTF8Encoding(false));
            c.Require("art/terrains/main.materials.json");

            // --- Fallback import: 16-bit PNG + values (not in the mod ZIP) ---
            Directory.CreateDirectory(c.ExtrasDir);
            BeamNGPng16.WriteGray16(Path.Combine(c.ExtrasDir, "theTerrain_heightmap16.png"), hm, R, true);
            var info = new JsonObj().Num("heightmapSize", R).Num("metersPerPixel", sq).Num("maxHeight", maxH)
                .Arr("terrainPositionBeamNG", res.PositionBeam.x, res.PositionBeam.y, res.PositionBeam.z)
                .Str("note", "Fallback for the terrain importer in the World Editor (F11). Top image row = north/+Y (assumption).");
            File.WriteAllText(Path.Combine(c.ExtrasDir, "terrain_import_values.json"), info.ToString(), new UTF8Encoding(false));

            r.Add(ReportStatus.PASS, "Terrain", $"{terrains.Count} Unity terrain(s) -> ONE BeamNG terrain {R}x{R}, {sq:F2} m per sample, edge length {side:F0} m, height {maxH:F1} m, {names.Count} terrain material(s).");
            if (sq > 2f) r.Add(ReportStatus.WARNING, "Terrain", $"Coarse resolution ({sq:F2} m/sample): detail is lost.", "Increase the terrain resolution (up to 8192).");
            if (Mathf.Abs((maxX - minX) - (maxZ - minZ)) > 0.01f) r.Add(ReportStatus.WARNING, "Terrain", "Terrain is not square: empty areas are marked as holes (no collision).");
            if (holesTotal > 0) r.Add(ReportStatus.PASS, "Terrain", $"{holesTotal:N0} sample(s) marked as hole (255) (hole mask / empty areas).");
            r.Add(ReportStatus.WARNING, "Terrain", ".ter file follows the docs (version 9). Not documented and therefore UNVERIFIED: material name encoding and row order.", "Load the map: if the terrain is missing or flipped, use extras/theTerrain_heightmap16.png with the World Editor terrain importer.");
            r.Add(ReportStatus.WARNING, "Terrain", "TerrainMaterial JSON keys are not documented (best guess); textures may be missing in game.", "Check/reassign terrain materials in the Terrain Painter.");
            if (terrains.Exists(t => t.terrainData.treeInstanceCount > 0 || t.terrainData.detailPrototypes.Length > 0))
                r.Add(ReportStatus.WARNING, "Vegetation", "Terrain trees/details are not exported.", "Vegetation is planned for a later version.");
            return res;
        }
    }
}
