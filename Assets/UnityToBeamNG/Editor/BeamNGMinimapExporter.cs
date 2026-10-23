using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UnityToBeamNG
{
    public class MinimapResult
    {
        public const string File = "minimap/terrain.png";
        public float X0, Z0, Side;   // Unity world: west edge, south edge, edge length (square)
        public int Pixels;
        /// <summary>info.json "offset": top-left corner in BeamNG coordinates (x = Unity x, y = Unity z; image top = north = +y).</summary>
        public Vector2 OffsetBeam { get { return new Vector2(X0, Z0 + Side); } }
    }

    /// <summary>
    /// Top-down minimap written to minimap/terrain.png and registered in info.json as
    /// "minimap":[{"file","size":[w,h],"offset":[x,y]}] (size/offset in world units, per the BeamNG info.json docs).
    /// It is computed on the CPU (terrain layer colours + hillshade, roads, building footprints, trees),
    /// so it does not depend on scene lighting or cameras.
    /// </summary>
    public static class BeamNGMinimapExporter
    {
        static readonly Color32 Background = new Color32(24, 28, 32, 255);
        static readonly Color32 FlatGround = new Color32(70, 82, 66, 255);

        public static MinimapResult Export(BeamNGExportContext c, ScanResult scan, TerrainResult terrain, VegetationResult veg)
        {
            var s = c.Settings; var r = c.Report;
            if (!s.exportMinimap) return null;
            int N = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(256, s.minimapResolution)), 256, 4096);

            // ----- extent -----
            var res = new MinimapResult { Pixels = N };
            var terrains = terrain != null ? scan.Terrains : new List<Terrain>();
            if (terrains.Count > 0)
            {
                float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
                foreach (var t in terrains)
                {
                    var p = t.transform.position; var sz = t.terrainData.size;
                    minX = Mathf.Min(minX, p.x); minZ = Mathf.Min(minZ, p.z); maxX = Mathf.Max(maxX, p.x + sz.x); maxZ = Mathf.Max(maxZ, p.z + sz.z);
                }
                res.X0 = minX; res.Z0 = minZ; res.Side = Mathf.Max(maxX - minX, maxZ - minZ);
            }
            else
            {
                bool any = false; Bounds b = new Bounds();
                foreach (var m in scan.Meshes) { if (!any) { b = m.Renderer.bounds; any = true; } else b.Encapsulate(m.Renderer.bounds); }
                if (!any) { r.Add(ReportStatus.WARNING, "Minimap", "No terrain and no meshes: minimap skipped.", ""); return null; }
                float side = Mathf.Max(b.size.x, b.size.z) * 1.1f + 20f;
                res.X0 = b.center.x - side * 0.5f; res.Z0 = b.center.z - side * 0.5f; res.Side = side;
            }
            if (res.Side < 1f) { r.Add(ReportStatus.WARNING, "Minimap", "Map extent is too small: minimap skipped.", ""); return null; }

            var buf = new Color32[N * N];
            for (int i = 0; i < buf.Length; i++) buf[i] = terrains.Count > 0 ? Background : FlatGround;

            // ----- terrain colours + hillshade -----
            if (terrains.Count > 0 && !PaintTerrain(c, terrains, res, buf, N)) return null;

            // ----- buildings (footprints of tall objects) -----
            int buildings = 0;
            float maxFoot = res.Side * 0.25f;
            foreach (var m in scan.Meshes)
            {
                var bb = m.Renderer.bounds;
                if (bb.size.y < 2.5f || bb.size.x > maxFoot || bb.size.z > maxFoot) continue;
                FillRect(buf, N, res, bb.min.x, bb.min.z, bb.max.x, bb.max.z, new Color32(86, 90, 100, 255), new Color32(36, 38, 44, 255));
                buildings++;
            }

            // ----- roads (the same centre lines that become the DecalRoad network) -----
            foreach (var road in c.RoadPaths)
            {
                if (road.Points.Count < 2) continue;
                float halfPx = Mathf.Max(1.5f, road.Width * 0.5f / res.Side * N);
                for (int pass = 0; pass < 2; pass++)
                {
                    var col = pass == 0 ? new Color32(40, 42, 46, 255) : new Color32(225, 225, 215, 255);
                    float hp = pass == 0 ? halfPx + 1.2f : halfPx;
                    for (int i = 0; i + 1 < road.Points.Count; i++)
                        DrawSegment(buf, N, ToPx(res, road.Points[i], N), ToPx(res, road.Points[i + 1], N), hp, col);
                }
            }

            // ----- trees -----
            if (veg != null)
                foreach (var t in veg.TreeXZ)
                    Disc(buf, N, (t.x - res.X0) / res.Side * N, (t.y - res.Z0) / res.Side * N, 1.6f, new Color32(38, 84, 46, 255));

            // ----- write PNG (row 0 = bottom = south, so the image top is north) -----
            var tex = new Texture2D(N, N, TextureFormat.RGB24, false, false);
            try
            {
                tex.SetPixels32(buf);
                tex.Apply();
                File.WriteAllBytes(c.Abs(MinimapResult.File), tex.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(tex); }
            c.Require(MinimapResult.File);

            r.Add(ReportStatus.PASS, "Minimap", $"minimap/terrain.png ({N}x{N}) written: terrain colours, {c.RoadPaths.Count} road(s), {buildings} building footprint(s), {(veg != null ? veg.TreeXZ.Count : 0):N0} tree(s). info.json: size [{res.Side:F0},{res.Side:F0}], offset [{res.OffsetBeam.x:F0},{res.OffsetBeam.y:F0}].");
            r.Add(ReportStatus.WARNING, "Minimap", "Minimap follows the info.json docs (minimap: file/size/offset). The offset is assumed to be the TOP-LEFT corner in world units; not yet verified in game.", "Open the map screen in BeamNG: if the image is shifted/flipped, send a screenshot.");
            return res;
        }

        static Vector2 ToPx(MinimapResult m, Vector3 w, int N) { return new Vector2((w.x - m.X0) / m.Side * N, (w.z - m.Z0) / m.Side * N); }

        // ---------- terrain ----------

        static Color AverageColor(Texture t)
        {
            if (t == null) return new Color(0.42f, 0.5f, 0.35f);
            try
            {
                var rt = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var prev = RenderTexture.active;
                Graphics.Blit(t, rt);
                RenderTexture.active = rt;
                var tx = new Texture2D(8, 8, TextureFormat.RGBA32, false);
                tx.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                var px = tx.GetPixels(); Object.DestroyImmediate(tx);
                float rr = 0, gg = 0, bb = 0;
                foreach (var p in px) { rr += p.r; gg += p.g; bb += p.b; }
                return new Color(rr / px.Length, gg / px.Length, bb / px.Length);
            }
            catch { return new Color(0.42f, 0.5f, 0.35f); }
        }

        static bool PaintTerrain(BeamNGExportContext c, List<Terrain> terrains, MinimapResult m, Color32[] buf, int N)
        {
            int nt = terrains.Count;
            var layerCols = new Color[nt][];
            var cache = new Dictionary<TerrainLayer, Color>();
            for (int i = 0; i < nt; i++)
            {
                var ls = terrains[i].terrainData.terrainLayers ?? new TerrainLayer[0];
                layerCols[i] = new Color[ls.Length];
                for (int l = 0; l < ls.Length; l++)
                {
                    if (ls[l] == null) { layerCols[i][l] = new Color(0.42f, 0.5f, 0.35f); continue; }
                    Color col;
                    if (!cache.TryGetValue(ls[l], out col)) { col = AverageColor(ls[l].diffuseTexture); cache[ls[l]] = col; }
                    layerCols[i][l] = col;
                }
            }
            var light = new Vector3(-0.5f, 0.75f, 0.5f).normalized;
            var alphaRow = new float[nt][,,]; var alphaRowY = new int[nt];
            for (int i = 0; i < nt; i++) alphaRowY[i] = -1;
            var cand = new List<int>();
            for (int row = 0; row < N; row++)
            {
                if (row % 32 == 0 && c.Cancelled("Minimap " + row + "/" + N, 0.795f + 0.005f * row / N)) return false;
                float wz = m.Z0 + (row + 0.5f) / N * m.Side;
                cand.Clear();
                for (int ti = 0; ti < nt; ti++)
                {
                    float pz = terrains[ti].transform.position.z;
                    if (wz >= pz && wz <= pz + terrains[ti].terrainData.size.z) cand.Add(ti);
                }
                if (cand.Count == 0) continue;
                for (int col = 0; col < N; col++)
                {
                    float wx = m.X0 + (col + 0.5f) / N * m.Side;
                    int found = -1;
                    foreach (int ti in cand)
                    {
                        float px = terrains[ti].transform.position.x;
                        if (wx >= px && wx <= px + terrains[ti].terrainData.size.x) { found = ti; break; }
                    }
                    if (found < 0) continue;
                    var td = terrains[found].terrainData; var tp = terrains[found].transform.position;
                    float u = Mathf.Clamp01((wx - tp.x) / td.size.x), v = Mathf.Clamp01((wz - tp.z) / td.size.z);

                    var baseCol = new Color(0.42f, 0.5f, 0.35f);
                    int lc = layerCols[found].Length;
                    if (lc > 0)
                    {
                        int ares = td.alphamapResolution;
                        int ax = Mathf.Clamp(Mathf.RoundToInt(u * (ares - 1)), 0, ares - 1), ay = Mathf.Clamp(Mathf.RoundToInt(v * (ares - 1)), 0, ares - 1);
                        if (alphaRowY[found] != ay) { alphaRow[found] = td.GetAlphamaps(0, ay, ares, 1); alphaRowY[found] = ay; }
                        var arr = alphaRow[found];
                        int n = Mathf.Min(lc, arr.GetLength(2));
                        float rr = 0, gg = 0, bb = 0, sum = 0;
                        for (int l = 0; l < n; l++) { float a = arr[0, ax, l]; var lcol = layerCols[found][l]; rr += lcol.r * a; gg += lcol.g * a; bb += lcol.b * a; sum += a; }
                        if (sum > 0.001f) baseCol = new Color(rr / sum, gg / sum, bb / sum);
                    }
                    // slightly desaturated map look + hillshade
                    float gray = baseCol.grayscale;
                    var mc = Color.Lerp(new Color(gray, gray, gray), baseCol, 0.65f);
                    float shade = Mathf.Clamp(0.55f + 0.55f * Vector3.Dot(td.GetInterpolatedNormal(u, v), light), 0.35f, 1.15f);
                    buf[row * N + col] = new Color32(
                        (byte)Mathf.Clamp(mc.r * shade * 255f * 0.9f, 0, 255), (byte)Mathf.Clamp(mc.g * shade * 255f * 0.9f, 0, 255), (byte)Mathf.Clamp(mc.b * shade * 255f * 0.9f, 0, 255), 255);
                }
            }
            return true;
        }

        // ---------- drawing ----------

        static void FillRect(Color32[] buf, int N, MinimapResult m, float x0, float z0, float x1, float z1, Color32 fill, Color32 edge)
        {
            int px0 = Mathf.Clamp(Mathf.FloorToInt((x0 - m.X0) / m.Side * N), 0, N - 1), px1 = Mathf.Clamp(Mathf.CeilToInt((x1 - m.X0) / m.Side * N), 0, N - 1);
            int py0 = Mathf.Clamp(Mathf.FloorToInt((z0 - m.Z0) / m.Side * N), 0, N - 1), py1 = Mathf.Clamp(Mathf.CeilToInt((z1 - m.Z0) / m.Side * N), 0, N - 1);
            for (int y = py0; y <= py1; y++)
                for (int x = px0; x <= px1; x++)
                    buf[y * N + x] = (x == px0 || x == px1 || y == py0 || y == py1) ? edge : fill;
        }

        static void DrawSegment(Color32[] buf, int N, Vector2 a, Vector2 b, float half, Color32 col)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, b.x) - half - 1), 0, N - 1), x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, b.x) + half + 1), 0, N - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, b.y) - half - 1), 0, N - 1), y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, b.y) + half + 1), 0, N - 1);
            var ab = b - a; float len2 = ab.sqrMagnitude; float h2 = half * half;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
                    if ((p - (a + ab * t)).sqrMagnitude <= h2) buf[y * N + x] = col;
                }
        }

        static void Disc(Color32[] buf, int N, float cx, float cy, float rad, Color32 col)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - rad)), x1 = Mathf.Min(N - 1, Mathf.CeilToInt(cx + rad));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - rad)), y1 = Mathf.Min(N - 1, Mathf.CeilToInt(cy + rad));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    if (dx * dx + dy * dy <= rad * rad) buf[y * N + x] = col;
                }
        }
    }
}
