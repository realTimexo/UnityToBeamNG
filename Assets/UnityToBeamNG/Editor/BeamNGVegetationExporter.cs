using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace UnityToBeamNG
{
    public class VegetationResult
    {
        public int TreeInstances;
        public int DetailInstances;
        public int ItemTypes;
        public int CollidableTrees;
        public readonly List<Vector2> TreeXZ = new List<Vector2>(); // Unity world x/z, used for the minimap
        public const string ManagedFile = "art/forest/managedItemData.json";
        public bool HasForest { get { return ItemTypes > 0; } }
    }

    /// <summary>
    /// Terrain trees (Paint Trees) and details (Paint Details) -> BeamNG forest items.
    /// Per the BeamNG docs: art/forest/managedItemData.json (item types), forest/*.forest4.json (one JSON object per line:
    /// type, pos, rotationMatrix, scale) and a "Forest" object in the level. Forest items only support ONE uniform scale value.
    /// Collision of forest items comes from a collision detail inside the shape: an extra node "Colmesh-1" is written
    /// into the DAE (trunk prism for trees, bounding box for detail meshes) and the item gets "collidable": true.
    /// </summary>
    public static class BeamNGVegetationExporter
    {
        class ItemType
        {
            public string Name;
            public string ShapeRel;
            public bool Collidable;
            public float Radius = 1f;
            public StreamWriter Writer;
            public string ForestRel;
            public int Count;
        }

        const float TreeSink = 0.05f;   // tiny sink so trees do not float on the (coarser) BeamNG terrain
        const float GrassSink = 0.05f;
        const int WarnTreeCount = 60000;

        // ---------- helpers (public: used by tests) ----------

        public static bool LooksLikeFoliage(Material m)
        {
            if (m == null) return false;
            var n = (m.name + " " + (m.shader != null ? m.shader.name : "")).ToLowerInvariant();
            if (n.Contains("bark") || n.Contains("trunk") || n.Contains("stem")) return false;
            return n.Contains("leaf") || n.Contains("leaves") || n.Contains("foliage") || n.Contains("frond") || n.Contains("grass")
                || n.Contains("needle") || n.Contains("plant") || n.Contains("fern") || n.Contains("bush") || n.Contains("speedtree");
        }

        /// <summary>One line of a .forest4.json file (docs: type, pos, rotationMatrix, scale).</summary>
        public static string ForestLine(string type, Vector3 posBeam, float[] rot, float scale)
        {
            var sb = new StringBuilder(160);
            AppendForestLine(sb, type, posBeam, rot, scale);
            return sb.ToString();
        }

        static string F3(float f) { return CheckNum(f).ToString("0.###", CultureInfo.InvariantCulture); }
        static string F4(float f) { return CheckNum(f).ToString("0.####", CultureInfo.InvariantCulture); }
        static float CheckNum(float f) { return (float.IsNaN(f) || float.IsInfinity(f)) ? 0f : f; }

        static void AppendForestLine(StringBuilder sb, string type, Vector3 p, float[] rot, float scale)
        {
            sb.Append("{\"type\":\"").Append(type).Append("\",\"pos\":[").Append(F3(p.x)).Append(',').Append(F3(p.y)).Append(',').Append(F3(p.z))
              .Append("],\"rotationMatrix\":[");
            for (int i = 0; i < 9; i++) { if (i > 0) sb.Append(','); sb.Append(F4(rot[i])); }
            sb.Append("],\"scale\":").Append(F4(scale <= 0f ? 1f : scale)).Append('}');
        }

        /// <summary>Convex n-sided prism (Unity space) around a vertical axis; triangles are wound clockwise seen from outside (Unity front faces).</summary>
        public static ColMesh MakePrism(Vector2 centerXZ, float radius, float y0, float y1, int sides)
        {
            sides = Mathf.Clamp(sides, 3, 24);
            var v = new List<Vector3>();
            for (int i = 0; i < sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                v.Add(new Vector3(centerXZ.x + Mathf.Cos(a) * radius, y0, centerXZ.y + Mathf.Sin(a) * radius));
            }
            for (int i = 0; i < sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                v.Add(new Vector3(centerXZ.x + Mathf.Cos(a) * radius, y1, centerXZ.y + Mathf.Sin(a) * radius));
            }
            var t = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                t.Add(i); t.Add(sides + i); t.Add(j);                 // side quad, 2 triangles
                t.Add(j); t.Add(sides + i); t.Add(sides + j);
            }
            for (int i = 1; i + 1 < sides; i++) { t.Add(0); t.Add(i); t.Add(i + 1); }                                  // bottom
            for (int i = 1; i + 1 < sides; i++) { t.Add(sides); t.Add(sides + i + 1); t.Add(sides + i); }              // top
            var col = new ColMesh { V = v.ToArray(), T = t.ToArray() };
            OrientOutward(col);
            return col;
        }

        public static ColMesh MakeBox(Bounds b)
        {
            var v = new Vector3[8];
            for (int i = 0; i < 8; i++)
                v[i] = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
            var t = new[] { 0, 1, 3, 0, 3, 2, 4, 6, 7, 4, 7, 5, 0, 4, 5, 0, 5, 1, 2, 3, 7, 2, 7, 6, 0, 2, 6, 0, 6, 4, 1, 5, 7, 1, 7, 3 };
            var col = new ColMesh { V = v, T = t };
            OrientOutward(col);
            return col;
        }

        /// <summary>Flips triangles whose Unity normal (cross(b-a, c-a)) points towards the centre of the shape.</summary>
        public static void OrientOutward(ColMesh c)
        {
            var center = Vector3.zero;
            foreach (var p in c.V) center += p;
            center /= c.V.Length;
            for (int i = 0; i + 2 < c.T.Length; i += 3)
            {
                var a = c.V[c.T[i]]; var b = c.V[c.T[i + 1]]; var d = c.V[c.T[i + 2]];
                var n = Vector3.Cross(b - a, d - a);
                var mid = (a + b + d) / 3f;
                if (Vector3.Dot(n, mid - center) < 0f) { int tmp = c.T[i + 1]; c.T[i + 1] = c.T[i + 2]; c.T[i + 2] = tmp; }
            }
        }

        // ---------- prefab -> combined mesh ----------

        class MatAcc { public Material Mat; public List<int> Tris = new List<int>(); }

        static List<MeshRenderer> CollectRenderers(GameObject prefab)
        {
            var list = new List<MeshRenderer>();
            var lg = prefab.GetComponentInChildren<LODGroup>(true);
            if (lg != null)
            {
                var lods = lg.GetLODs();
                if (lods.Length > 0)
                    foreach (var r in lods[0].renderers) { var mr = r as MeshRenderer; if (mr != null && mr.GetComponent<MeshFilter>() != null) list.Add(mr); }
            }
            if (list.Count == 0)
                foreach (var mr in prefab.GetComponentsInChildren<MeshRenderer>(true))
                    if (mr.GetComponent<MeshFilter>() != null) list.Add(mr);
            list.RemoveAll(mr =>
            {
                if (mr.name.ToLowerInvariant().Contains("billboard")) return true;
                foreach (var m in mr.sharedMaterials)
                    if (m != null && m.shader != null && m.shader.name.ToLowerInvariant().Contains("billboard")) return true;
                return false;
            });
            return list;
        }

        /// <summary>Combines all (LOD0) renderers of a prefab into ONE mesh with one submesh per material. Returns null if nothing usable.</summary>
        static Mesh CombinePrefab(GameObject prefab, string name, out int unreadable)
        {
            unreadable = 0;
            var rends = CollectRenderers(prefab);
            if (rends.Count == 0) return null;
            var root = prefab.transform;
            var verts = new List<Vector3>(); var nrm = new List<Vector3>(); var uvs = new List<Vector2>();
            var accs = new List<MatAcc>();
            foreach (var mr in rends)
            {
                var mesh = mr.GetComponent<MeshFilter>().sharedMesh;
                if (mesh == null) continue;
                if (!mesh.isReadable) { unreadable++; continue; }
                var M = Matrix4x4.Scale(root.localScale) * (root.worldToLocalMatrix * mr.transform.localToWorldMatrix);
                var N = M.inverse.transpose;
                bool flip = M.determinant < 0f;
                var mv = mesh.vertices; var mn = mesh.normals; var mu = mesh.uv;
                int baseIdx = verts.Count;
                for (int i = 0; i < mv.Length; i++)
                {
                    verts.Add(M.MultiplyPoint3x4(mv[i]));
                    var n = (mn != null && mn.Length == mv.Length) ? N.MultiplyVector(mn[i]).normalized : Vector3.up;
                    nrm.Add(n);
                    uvs.Add((mu != null && mu.Length == mv.Length) ? mu[i] : Vector2.zero);
                }
                var mats = mr.sharedMaterials;
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                {
                    if (mesh.GetTopology(sm) != MeshTopology.Triangles) continue;
                    var mat = (mats != null && mats.Length > 0) ? mats[Mathf.Min(sm, mats.Length - 1)] : null;
                    MatAcc acc = null;
                    foreach (var a in accs) if (a.Mat == mat) { acc = a; break; }
                    if (acc == null) { acc = new MatAcc { Mat = mat }; accs.Add(acc); }
                    var tr = mesh.GetTriangles(sm);
                    for (int i = 0; i + 2 < tr.Length; i += 3)
                    {
                        acc.Tris.Add(baseIdx + tr[i]);
                        acc.Tris.Add(baseIdx + (flip ? tr[i + 2] : tr[i + 1]));
                        acc.Tris.Add(baseIdx + (flip ? tr[i + 1] : tr[i + 2]));
                    }
                }
            }
            if (verts.Count == 0 || accs.Count == 0) return null;
            var res = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            if (verts.Count > 65535) res.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            res.SetVertices(verts); res.SetNormals(nrm); res.SetUVs(0, uvs);
            res.subMeshCount = accs.Count;
            for (int i = 0; i < accs.Count; i++) res.SetTriangles(accs[i].Tris, i);
            res.RecalculateBounds();
            _lastMats = new Material[accs.Count];
            for (int i = 0; i < accs.Count; i++) _lastMats[i] = accs[i].Mat;
            return res;
        }
        static Material[] _lastMats;

        // ---------- collision estimate ----------

        static ColMesh TrunkCollision(GameObject prefab, Mesh mesh, float minHeight, out bool tooLow)
        {
            tooLow = false;
            var b = mesh.bounds;
            float height = b.size.y;
            if (height < minHeight) { tooLow = true; return null; }
            var vs = mesh.vertices;
            float y0 = b.min.y;
            float band = Mathf.Min(1.5f, height * 0.2f);
            float cx = 0, cz = 0; int n = 0;
            foreach (var v in vs) if (v.y <= y0 + band) { cx += v.x; cz += v.z; n++; }
            if (n == 0) { cx = b.center.x; cz = b.center.z; n = 1; }
            cx /= n; cz /= n;
            var d = new List<float>();
            foreach (var v in vs) if (v.y <= y0 + band) d.Add(new Vector2(v.x - cx, v.z - cz).magnitude);
            float radius = 0.25f;
            if (d.Count > 0) { d.Sort(); radius = d[Mathf.Min(d.Count - 1, (int)(d.Count * 0.3f))]; }

            var cap = prefab.GetComponentInChildren<CapsuleCollider>(true);
            if (cap != null)
            {
                var ls = cap.transform.lossyScale;
                radius = cap.radius * Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.z));
            }
            float maxR = 0.5f * Mathf.Min(b.size.x, b.size.z);
            radius = Mathf.Clamp(radius, 0.08f, Mathf.Max(0.1f, Mathf.Min(1.5f, maxR)));
            float top = y0 + Mathf.Min(height * 0.7f, 8f);
            return MakePrism(new Vector2(cx, cz), radius, y0, top, 8);
        }

        // ---------- shape export ----------

        static ItemType MakePrefabType(BeamNGExportContext c, BeamNGMaterialRegistry reg, GameObject prefab, string baseName, bool isTree,
                                       Dictionary<string, int> usedNames, ref int unreadableTotal, ref int lowTrees, ref int failed)
        {
            var s = c.Settings;
            int unread;
            var id = BeamNGEditorUtilities.SafeId(baseName);
            var mesh = CombinePrefab(prefab, id, out unread);
            unreadableTotal += unread;
            if (mesh == null)
            {
                failed++;
                c.Report.Add(ReportStatus.WARNING, "Vegetation", $"Prefab '{prefab.name}' has no usable (readable) mesh and is skipped" + (unread > 0 ? " - enable 'Read/Write' in the model import settings." : "."), "Trees/details with this prefab are not exported.");
                return null;
            }
            var mats = _lastMats;
            foreach (var m in mats) if (m != null && LooksLikeFoliage(m)) reg.Foliage.Add(m);

            ColMesh col = null; bool collidable = false; float radius = 1f;
            if (isTree && s.treeCollision)
            {
                bool low; col = TrunkCollision(prefab, mesh, s.minCollidableTreeHeight, out low);
                if (low) lowTrees++;
                collidable = col != null;
                radius = Mathf.Max(0.5f, 0.25f * Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.z));
            }
            else if (!isTree && s.detailMeshCollision)
            {
                col = MakeBox(mesh.bounds); collidable = true;
            }
            return WriteShape(c, reg, mesh, mats, id, isTree ? "tree" : "detail", col, collidable, radius, usedNames);
        }

        static ItemType WriteShape(BeamNGExportContext c, BeamNGMaterialRegistry reg, Mesh mesh, Material[] mats, string id, string prefix,
                                   ColMesh col, bool collidable, float radius, Dictionary<string, int> usedNames)
        {
            var type = prefix + "_" + id;
            int k; usedNames.TryGetValue(type, out k); usedNames[type] = k + 1;
            if (k > 0) type += "_" + (k + 1);
            var rel = "art/shapes/" + type + ".dae";
            var syms = BeamNGMeshExporter.MaterialSymbols(mesh, mats, reg);
            var tiling = new Vector4[mesh.subMeshCount];
            for (int sm = 0; sm < tiling.Length; sm++)
            {
                var m = mats[Mathf.Min(sm, mats.Length - 1)];
                var sc = m != null ? m.mainTextureScale : Vector2.one; var of = m != null ? m.mainTextureOffset : Vector2.zero;
                tiling[sm] = new Vector4(sc.x, sc.y, of.x, of.y);
            }
            using (var sw = new StreamWriter(c.Abs(rel), false, new UTF8Encoding(false), 1 << 16))
                BeamNGMeshExporter.Write(sw, mesh, type, syms, c.Report, tiling, null, col);
            c.Require(rel);
            return new ItemType { Name = type, ShapeRel = rel, Collidable = collidable, Radius = radius };
        }

        /// <summary>Grass: three crossed quads with the detail texture (tinted with the Unity healthy/dry colour).</summary>
        static ItemType MakeGrassCard(BeamNGExportContext c, BeamNGMaterialRegistry reg, DetailPrototype dp, int index, Dictionary<string, int> usedNames)
        {
            float w = Mathf.Max(0.05f, (dp.minWidth + dp.maxWidth) * 0.5f), h = Mathf.Max(0.05f, (dp.minHeight + dp.maxHeight) * 0.5f);
            var verts = new List<Vector3>(); var nrm = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
            for (int q = 0; q < 3; q++)
            {
                float a = q * Mathf.PI / 3f;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (w * 0.5f);
                int b = verts.Count;
                verts.Add(-dir); verts.Add(dir); verts.Add(dir + Vector3.up * h); verts.Add(-dir + Vector3.up * h);
                for (int i = 0; i < 4; i++) nrm.Add(Vector3.up);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                // both windings: BeamNG culls back faces unless the material is double-sided (it is), but keep a valid front face
                tris.Add(b); tris.Add(b + 3); tris.Add(b + 1); tris.Add(b + 1); tris.Add(b + 3); tris.Add(b + 2);
            }
            var mesh = new Mesh { name = "grass_card", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(verts); mesh.SetNormals(nrm); mesh.SetUVs(0, uv); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            var tex = TintedCopy(dp.prototypeTexture, Color.Lerp(dp.healthyColor, dp.dryColor, 0.2f));
            var shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader) { name = "grass_" + BeamNGEditorUtilities.SafeId(dp.prototypeTexture.name), hideFlags = HideFlags.DontSave };
            mat.mainTexture = tex;
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.35f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.05f);
            reg.Foliage.Add(mat);
            return WriteShape(c, reg, mesh, new[] { mat }, "grass_" + BeamNGEditorUtilities.SafeId(dp.prototypeTexture.name), "detail", null, false, 0.5f, usedNames);
        }

        /// <summary>Readable RGBA copy of src multiplied by tint (rgb only, alpha untouched).</summary>
        static Texture2D TintedCopy(Texture src, Color tint)
        {
            int w = Mathf.Clamp(src.width, 16, 1024), h = Mathf.Clamp(src.height, 16, 1024);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false, false) { name = src.name + "_tinted", hideFlags = HideFlags.DontSave };
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = t.GetPixels32();
            bool nearWhite = tint.r > 0.97f && tint.g > 0.97f && tint.b > 0.97f;
            if (!nearWhite)
                for (int i = 0; i < px.Length; i++)
                    px[i] = new Color32((byte)(px[i].r * tint.r), (byte)(px[i].g * tint.g), (byte)(px[i].b * tint.b), px[i].a);
            t.SetPixels32(px); t.Apply();
            return t;
        }

        // ---------- main ----------

        static void Write(ItemType it, BeamNGExportContext c, StringBuilder sb)
        {
            if (it.Writer == null)
            {
                it.ForestRel = "forest/" + it.Name + ".forest4.json";
                it.Writer = new StreamWriter(c.Abs(it.ForestRel), false, new UTF8Encoding(false), 1 << 16);
                c.Require(it.ForestRel);
            }
            it.Writer.Write(sb.ToString()); it.Writer.Write('\n');
            it.Count++;
        }

        public static VegetationResult Export(BeamNGExportContext c, ScanResult scan, BeamNGMaterialRegistry reg)
        {
            var s = c.Settings; var r = c.Report;
            var res = new VegetationResult();
            long treeTotal = 0; int detailProtos = 0;
            foreach (var t in scan.Terrains) { treeTotal += t.terrainData.treeInstanceCount; detailProtos += t.terrainData.detailPrototypes.Length; }
            if (treeTotal == 0 && detailProtos == 0) return res;
            if (!s.exportVegetation)
            {
                r.Add(ReportStatus.WARNING, "Vegetation", $"Vegetation export is switched off: {treeTotal:N0} tree(s) and {detailProtos} detail prototype(s) are not exported.", "Enable it in the 'Vegetation' tab.");
                return res;
            }

            var types = new List<ItemType>();
            var treeByPrefab = new Dictionary<GameObject, ItemType>();
            var usedNames = new Dictionary<string, int>();
            var sb = new StringBuilder(256);
            int unreadable = 0, lowTrees = 0, failed = 0, skippedInst = 0;
            try
            {
                // ---------------- trees ----------------
                long done = 0;
                foreach (var t in scan.Terrains)
                {
                    var td = t.terrainData; var tp = t.transform.position; var size = td.size;
                    var protos = td.treePrototypes;
                    var map = new ItemType[protos.Length]; var tried = new bool[protos.Length];
                    foreach (var ti in td.treeInstances)
                    {
                        if (++done % 4000 == 0 && c.Cancelled("Trees " + done + "/" + treeTotal, 0.80f + 0.02f * done / Mathf.Max(1, treeTotal))) return null;
                        int pi = ti.prototypeIndex;
                        if (pi < 0 || pi >= protos.Length) { skippedInst++; continue; }
                        if (!tried[pi])
                        {
                            tried[pi] = true;
                            var prefab = protos[pi].prefab;
                            if (prefab == null) { failed++; r.Add(ReportStatus.WARNING, "Vegetation", $"Tree prototype {pi} on '{t.name}' has no prefab and is skipped.", "Assign a prefab or remove the prototype."); }
                            else
                            {
                                ItemType existing;
                                if (!treeByPrefab.TryGetValue(prefab, out existing))
                                {
                                    existing = MakePrefabType(c, reg, prefab, prefab.name, true, usedNames, ref unreadable, ref lowTrees, ref failed);
                                    treeByPrefab[prefab] = existing; // null = failed, do not retry
                                    if (existing != null) types.Add(existing);
                                }
                                map[pi] = existing;
                            }
                        }
                        var it = map[pi];
                        if (it == null) { skippedInst++; continue; }

                        float wy = tp.y + td.GetInterpolatedHeight(ti.position.x, ti.position.z);
                        var world = new Vector3(tp.x + ti.position.x * size.x, wy - TreeSink, tp.z + ti.position.z * size.z);
                        var rotB = BeamNGCoordinateConverter.RotationMatrix(BeamNGCoordinateConverter.Rotation(Quaternion.Euler(0f, ti.rotation * Mathf.Rad2Deg, 0f)));
                        float sc = (ti.widthScale + ti.heightScale) * 0.5f; // forest items support one uniform scale only
                        sb.Length = 0;
                        AppendForestLine(sb, it.Name, BeamNGCoordinateConverter.Position(world), rotB, sc);
                        Write(it, c, sb);
                        res.TreeInstances++;
                        if (it.Collidable) res.CollidableTrees++;
                        if (res.TreeXZ.Count < 300000) res.TreeXZ.Add(new Vector2(world.x, world.z));
                    }
                }

                // ---------------- details (grass / detail meshes) ----------------
                if (s.exportDetails && detailProtos > 0)
                {
                    float factor = 1f; long estimated = 0;
                    foreach (var t in scan.Terrains) estimated += CountDetails(t.terrainData, s.detailDensity);
                    if (estimated > s.maxDetailInstances) factor = s.maxDetailInstances / (float)estimated;
                    if (factor < 1f)
                        r.Add(ReportStatus.WARNING, "Vegetation", $"Detail budget: {estimated:N0} detail instance(s) at density {s.detailDensity:0.##} exceed the limit of {s.maxDetailInstances:N0}; reduced to {(int)(factor * 100)}%.", "Raise 'Max detail instances' or lower the density in the 'Vegetation' tab.");
                    var rnd = new System.Random(12345);
                    long cells = 0, totalCells = 0;
                    foreach (var t in scan.Terrains) totalCells += (long)t.terrainData.detailWidth * t.terrainData.detailHeight * t.terrainData.detailPrototypes.Length;

                    foreach (var t in scan.Terrains)
                    {
                        var td = t.terrainData; var tp = t.transform.position; var size = td.size;
                        var dps = td.detailPrototypes;
                        int dw = td.detailWidth, dh = td.detailHeight;
                        if (dw <= 0 || dh <= 0) continue;
                        for (int p = 0; p < dps.Length; p++)
                        {
                            var dp = dps[p];
                            ItemType it = null; bool isMesh = dp.usePrototypeMesh && dp.prototype != null;
                            bool usable = isMesh || (!dp.usePrototypeMesh && dp.prototypeTexture != null);
                            if (!usable) { r.Add(ReportStatus.WARNING, "Vegetation", $"Detail prototype {p} on '{t.name}' has neither a mesh nor a texture and is skipped.", ""); continue; }
                            float meanW = Mathf.Max(0.05f, (dp.minWidth + dp.maxWidth) * 0.5f), meanH = Mathf.Max(0.05f, (dp.minHeight + dp.maxHeight) * 0.5f);
                            int rowsPer = Mathf.Max(1, 2000000 / dw);
                            for (int y0 = 0; y0 < dh; y0 += rowsPer)
                            {
                                int rows = Mathf.Min(rowsPer, dh - y0);
                                if (c.Cancelled("Details " + t.name + " #" + p + " " + y0 + "/" + dh, 0.82f + 0.02f * cells / Mathf.Max(1L, totalCells))) return null;
                                var layer = td.GetDetailLayer(0, y0, dw, rows, p);
                                cells += (long)dw * rows;
                                for (int ry = 0; ry < rows; ry++)
                                    for (int x = 0; x < dw; x++)
                                    {
                                        int val = layer[ry, x];
                                        if (val <= 0) continue;
                                        float e = val * s.detailDensity * factor;
                                        int n = (int)e; if (rnd.NextDouble() < e - n) n++;
                                        if (n == 0) continue;
                                        if (it == null)
                                        {
                                            it = isMesh
                                                ? MakePrefabType(c, reg, dp.prototype, dp.prototype.name, false, usedNames, ref unreadable, ref lowTrees, ref failed)
                                                : MakeGrassCard(c, reg, dp, p, usedNames);
                                            if (it == null) goto nextProto;
                                            types.Add(it);
                                        }
                                        for (int k = 0; k < n; k++)
                                        {
                                            float u = (x + (float)rnd.NextDouble()) / dw, v = (y0 + ry + (float)rnd.NextDouble()) / dh;
                                            float wy = tp.y + td.GetInterpolatedHeight(u, v);
                                            var world = new Vector3(tp.x + u * size.x, wy - GrassSink, tp.z + v * size.z);
                                            float tt = (float)rnd.NextDouble();
                                            float sc;
                                            if (isMesh) sc = (Mathf.Lerp(dp.minWidth, dp.maxWidth, tt) + Mathf.Lerp(dp.minHeight, dp.maxHeight, tt)) * 0.5f;
                                            else sc = 0.5f * (Mathf.Lerp(dp.minWidth, dp.maxWidth, tt) / meanW + Mathf.Lerp(dp.minHeight, dp.maxHeight, tt) / meanH);
                                            var rotB = BeamNGCoordinateConverter.RotationMatrix(BeamNGCoordinateConverter.Rotation(Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f)));
                                            sb.Length = 0;
                                            AppendForestLine(sb, it.Name, BeamNGCoordinateConverter.Position(world), rotB, sc);
                                            Write(it, c, sb);
                                            res.DetailInstances++;
                                        }
                                    }
                            }
                            nextProto:;
                        }
                    }
                }
                else if (!s.exportDetails && detailProtos > 0)
                    r.Add(ReportStatus.WARNING, "Vegetation", $"Details (grass etc.) are switched off: {detailProtos} detail prototype(s) not exported.", "Enable 'Export details' in the 'Vegetation' tab.");
            }
            finally
            {
                foreach (var it in types) if (it.Writer != null) { it.Writer.Dispose(); it.Writer = null; }
            }

            // ---------------- managedItemData.json ----------------
            var used = types.FindAll(x => x.Count > 0);
            res.ItemTypes = used.Count;
            if (used.Count > 0)
            {
                var entries = new List<string>();
                foreach (var it in used)
                {
                    var o = new JsonObj().Str("class", "TSForestItemData").Str("internalName", it.Name).Str("shapeFile", c.VPath(it.ShapeRel))
                        .Bool("collidable", it.Collidable).Num("radius", it.Radius).Bool("snapRotationToTerrain", false)
                        .Num("windScale", 0f).Num("trunkBendScale", 0.4f).Num("branchAmp", 1f).Num("detailAmp", 0.25f).Num("detailFreq", 1f)
                        .Num("mass", 5f).Num("rigidity", 10f).Num("tightnessCoefficient", 0.4f).Num("dampingCoefficient", 0.7f);
                    entries.Add(BeamNGJson.Q(it.Name) + ":" + o);
                }
                File.WriteAllText(c.Abs(VegetationResult.ManagedFile), "{\n" + string.Join(",\n", entries) + "\n}\n", new UTF8Encoding(false));
                c.Require(VegetationResult.ManagedFile);
            }

            // ---------------- report ----------------
            if (res.TreeInstances + res.DetailInstances > 0)
            {
                r.Exported += res.TreeInstances + res.DetailInstances;
                r.Add(ReportStatus.PASS, "Vegetation", $"{res.TreeInstances:N0} tree(s) and {res.DetailInstances:N0} detail instance(s) of {res.ItemTypes} item type(s) written to forest/*.forest4.json + art/forest/managedItemData.json; {res.CollidableTrees:N0} tree(s) with trunk collision.");
                r.Add(ReportStatus.WARNING, "Vegetation", "Forest files follow the BeamNG docs (managedItemData.json, .forest4.json, Forest object). The collision node name 'Colmesh-1' inside the DAE is NOT documented and untested in game.", "Drive into a tree after loading. If you pass through, send beamng.log and tell me.");
                r.Add(ReportStatus.MANUAL, "Vegetation", "Forest items have ONE uniform scale: tree width/height scale is averaged. No wind sway (windScale 0) and no far-distance LOD/billboards are generated.", "");
            }
            if (res.TreeInstances > WarnTreeCount) r.Add(ReportStatus.WARNING, "Performance", $"{res.TreeInstances:N0} trees without LOD/billboards can be heavy in BeamNG.", "Remove distant trees or use fewer/lower-poly tree prefabs.");
            if (lowTrees > 0) r.Add(ReportStatus.PASS, "Vegetation", $"{lowTrees} tree type(s) lower than {s.minCollidableTreeHeight:0.#} m (bushes) get no collision.");
            if (unreadable > 0) r.Add(ReportStatus.WARNING, "Vegetation", $"{unreadable} vegetation mesh(es) are not readable and were skipped.", "Enable 'Read/Write' in the import settings of the tree/detail model.");
            if (skippedInst > 0) r.Add(ReportStatus.WARNING, "Vegetation", $"{skippedInst:N0} tree instance(s) skipped (missing prefab/invalid prototype).", "");
            return res;
        }

        static long CountDetails(TerrainData td, float density)
        {
            long total = 0;
            int dw = td.detailWidth, dh = td.detailHeight;
            if (dw <= 0 || dh <= 0) return 0;
            int rowsPer = Mathf.Max(1, 2000000 / dw);
            for (int p = 0; p < td.detailPrototypes.Length; p++)
            {
                var dp = td.detailPrototypes[p];
                if (!((dp.usePrototypeMesh && dp.prototype != null) || (!dp.usePrototypeMesh && dp.prototypeTexture != null))) continue;
                for (int y0 = 0; y0 < dh; y0 += rowsPer)
                {
                    int rows = Mathf.Min(rowsPer, dh - y0);
                    var layer = td.GetDetailLayer(0, y0, dw, rows, p);
                    long sum = 0;
                    for (int y = 0; y < rows; y++) for (int x = 0; x < dw; x++) sum += layer[y, x];
                    total += (long)(sum * density);
                }
            }
            return total;
        }
    }
}
