using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace UnityToBeamNG
{
    /// <summary>Convex collision shape (Unity space, Unity winding) written as the extra node "Colmesh-1" (used by forest items).</summary>
    public class ColMesh
    {
        public Vector3[] V;
        public int[] T;
    }

    /// <summary>Extras for forest shapes (all optional): collision node, LOD naming ("_a&lt;px&gt;"), auto billboard, null detail, vertex colours (wind).</summary>
    public class ShapeOptions
    {
        public ColMesh Col;
        public int LodPixels;        // > 0: mesh node is named "<name>_a<px>" (BeamNG/Torque detail level = min. on-screen size in pixels)
        public int BillboardPixels;  // > 0: empty node "bb_autobillboard<px>" under base00 (engine generates a billboard LOD)
        public int NullPixels;       // > 0: empty node "nulldetail<px>" under base00 (nothing is drawn below this size)
        public bool Colors;          // write Mesh.colors as COLOR input (wind weights)
        public Mesh Lod1;            // optional simplified second detail level (node "<name>_a<Lod1Pixels>")
        public int Lod1Pixels;
    }

    /// <summary>
    /// Writes meshes as COLLADA 1.4.1 (.dae): Z-up, meters, hierarchy base00 > start01 > mesh (per BeamNG docs),
    /// axes converted to BeamNG, triangle winding reversed. The object transform
    /// is not in the DAE but in the TSStatic line of items.level.json.
    /// </summary>
    public static class BeamNGMeshExporter
    {
        static string F(float f) { return f.ToString("R", CultureInfo.InvariantCulture); }

        public static string[] MaterialSymbols(Mesh m, Material[] mats, BeamNGMaterialRegistry reg)
        {
            var res = new string[m.subMeshCount];
            for (int i = 0; i < res.Length; i++)
                res[i] = reg.NameOf(mats == null || mats.Length == 0 ? null : mats[Mathf.Min(i, mats.Length - 1)]); // Unity renders extra submeshes with the last material
            return res;
        }

        /// <summary>tiling: per submesh (scaleX, scaleY, offsetX, offsetY) baked into the UVs (null = unchanged).</summary>
        static void WriteGeometry(TextWriter w, Mesh m, string id, string name, string[] matSymbols, BeamNGExportReport r, Vector4[] tiling, Vector3 mirror, bool withColors)
        {
            var cols = withColors ? m.colors : null;
            bool hasC = cols != null && cols.Length == m.vertexCount && cols.Length > 0;
            var mir = mirror;
            bool flip = mir.x * mir.y * mir.z < 0f;
            var verts = m.vertices;
            var nrm = m.normals;
            for (int i = 0; i < verts.Length; i++) verts[i] = Vector3.Scale(verts[i], mir);
            if (nrm != null) for (int i = 0; i < nrm.Length; i++) nrm[i] = Vector3.Scale(nrm[i], mir);
            var uv = m.uv;
            bool hasN = nrm != null && nrm.Length == verts.Length;
            bool hasUV = uv != null && uv.Length == verts.Length;

            w.WriteLine("<geometry id=\"" + id + "-geom\" name=\"" + id + "\"><mesh>");
            w.Write("<source id=\"" + id + "-pos\"><float_array id=\"" + id + "-pos-arr\" count=\"" + verts.Length * 3 + "\">");
            foreach (var v in verts) { var b = BeamNGCoordinateConverter.Position(v); w.Write(F(b.x)); w.Write(' '); w.Write(F(b.y)); w.Write(' '); w.Write(F(b.z)); w.Write(' '); }
            w.WriteLine("</float_array><technique_common><accessor source=\"#" + id + "-pos-arr\" count=\"" + verts.Length + "\" stride=\"3\"><param name=\"X\" type=\"float\"/><param name=\"Y\" type=\"float\"/><param name=\"Z\" type=\"float\"/></accessor></technique_common></source>");

            if (hasN)
            {
                w.Write("<source id=\"" + id + "-nrm\"><float_array id=\"" + id + "-nrm-arr\" count=\"" + nrm.Length * 3 + "\">");
                foreach (var v in nrm) { var b = BeamNGCoordinateConverter.Direction(v); w.Write(F(b.x)); w.Write(' '); w.Write(F(b.y)); w.Write(' '); w.Write(F(b.z)); w.Write(' '); }
                w.WriteLine("</float_array><technique_common><accessor source=\"#" + id + "-nrm-arr\" count=\"" + nrm.Length + "\" stride=\"3\"><param name=\"X\" type=\"float\"/><param name=\"Y\" type=\"float\"/><param name=\"Z\" type=\"float\"/></accessor></technique_common></source>");
            }
            if (hasUV)
            {
                w.Write("<source id=\"" + id + "-uv\"><float_array id=\"" + id + "-uv-arr\" count=\"" + uv.Length * 2 + "\">");
                var vertTiling = new int[verts.Length];
                for (int i = 0; i < vertTiling.Length; i++) vertTiling[i] = -1;
                if (tiling != null)
                    for (int sm = 0; sm < m.subMeshCount && sm < tiling.Length; sm++)
                        foreach (var idx in m.GetIndices(sm)) if (vertTiling[idx] < 0) vertTiling[idx] = sm;
                for (int i = 0; i < uv.Length; i++)
                {
                    var v = uv[i];
                    int sm = vertTiling[i];
                    if (tiling != null && sm >= 0) { var tl = tiling[sm]; v = new Vector2(v.x * tl.x + tl.z, v.y * tl.y + tl.w); }
                    w.Write(F(v.x)); w.Write(' '); w.Write(F(v.y)); w.Write(' ');
                }
                w.WriteLine("</float_array><technique_common><accessor source=\"#" + id + "-uv-arr\" count=\"" + uv.Length + "\" stride=\"2\"><param name=\"S\" type=\"float\"/><param name=\"T\" type=\"float\"/></accessor></technique_common></source>");
            }
            if (hasC)
            {
                w.Write("<source id=\"" + id + "-col\"><float_array id=\"" + id + "-col-arr\" count=\"" + cols.Length * 4 + "\">");
                foreach (var cc in cols) { w.Write(F(cc.r)); w.Write(' '); w.Write(F(cc.g)); w.Write(' '); w.Write(F(cc.b)); w.Write(' '); w.Write(F(cc.a)); w.Write(' '); }
                w.WriteLine("</float_array><technique_common><accessor source=\"#" + id + "-col-arr\" count=\"" + cols.Length + "\" stride=\"4\"><param name=\"R\" type=\"float\"/><param name=\"G\" type=\"float\"/><param name=\"B\" type=\"float\"/><param name=\"A\" type=\"float\"/></accessor></technique_common></source>");
            }
            w.WriteLine("<vertices id=\"" + id + "-vtx\"><input semantic=\"POSITION\" source=\"#" + id + "-pos\"/></vertices>");

            for (int s = 0; s < m.subMeshCount; s++)
            {
                if (m.GetTopology(s) != MeshTopology.Triangles)
                {
                    r.Add(ReportStatus.WARNING, "Mesh", $"'{name}': submesh {s} is not triangle topology and is skipped.");
                    continue;
                }
                var tris = m.GetTriangles(s);
                w.Write("<triangles material=\"" + matSymbols[s] + "-material\" count=\"" + tris.Length / 3 + "\">");
                w.Write("<input semantic=\"VERTEX\" source=\"#" + id + "-vtx\" offset=\"0\"/>");
                if (hasN) w.Write("<input semantic=\"NORMAL\" source=\"#" + id + "-nrm\" offset=\"0\"/>");
                if (hasUV) w.Write("<input semantic=\"TEXCOORD\" source=\"#" + id + "-uv\" offset=\"0\" set=\"0\"/>");
                if (hasC) w.Write("<input semantic=\"COLOR\" source=\"#" + id + "-col\" offset=\"0\" set=\"0\"/>");
                w.Write("<p>");
                for (int i = 0; i < tris.Length; i += 3)
                { int a = tris[i], b2 = flip ? tris[i + 1] : tris[i + 2], c2 = flip ? tris[i + 2] : tris[i + 1]; w.Write(a); w.Write(' '); w.Write(b2); w.Write(' '); w.Write(c2); w.Write(' '); }
                w.WriteLine("</p></triangles>");
            }
            w.WriteLine("</mesh></geometry>");
        }

        public static void Write(TextWriter w, Mesh m, string name, string[] matSymbols, BeamNGExportReport r, Vector4[] tiling = null, Vector3? mirror = null, ShapeOptions opt = null)
        {
            var id = BeamNGEditorUtilities.NodeName(name);
            var col = opt != null ? opt.Col : null;
            var nodeName = (opt != null && opt.LodPixels > 0) ? BeamNGEditorUtilities.SafeId(name) + "_a" + opt.LodPixels : id;
            // Torque reads trailing digits of a mesh node name as its detail size (pixels): "ID1012" would only be visible at 1012 px
            // on screen, i.e. practically never. (SketchUp models are named like that.)
            if (nodeName.Length > 0 && char.IsDigit(nodeName[nodeName.Length - 1])) nodeName += "_m";
            w.WriteLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            w.WriteLine("<COLLADA xmlns=\"http://www.collada.org/2005/11/COLLADASchema\" version=\"1.4.1\">");
            w.WriteLine("<asset><contributor><authoring_tool>Unity to BeamNG</authoring_tool></contributor><unit name=\"meter\" meter=\"1\"/><up_axis>Z_UP</up_axis></asset>");

            var uniq = new List<string>();
            foreach (var s in matSymbols) if (!uniq.Contains(s)) uniq.Add(s);

            w.WriteLine("<library_effects>");
            foreach (var s in uniq)
                w.WriteLine("<effect id=\"" + s + "-effect\"><profile_COMMON><technique sid=\"common\"><phong><diffuse><color>0.6 0.6 0.6 1</color></diffuse></phong></technique></profile_COMMON></effect>");
            w.WriteLine("</library_effects>");
            w.WriteLine("<library_materials>");
            foreach (var s in uniq)
                w.WriteLine("<material id=\"" + s + "-material\" name=\"" + s + "\"><instance_effect url=\"#" + s + "-effect\"/></material>");
            w.WriteLine("</library_materials>");

            w.WriteLine("<library_geometries>");
            WriteGeometry(w, m, id, name, matSymbols, r, tiling, mirror ?? Vector3.one, opt != null && opt.Colors);
            if (opt != null && opt.Lod1 != null && opt.Lod1Pixels > 0) WriteGeometry(w, opt.Lod1, id + "_l1", name, matSymbols, r, tiling, mirror ?? Vector3.one, false);
            if (col != null && col.V != null && col.T != null && col.T.Length >= 9)
            {
                // Collision shape for forest items: BeamNG/Torque read nodes named "Colmesh-<n>" as collision detail n.
                w.Write("<geometry id=\"colmesh-geom\" name=\"Colmesh-1\"><mesh>");
                w.Write("<source id=\"colmesh-pos\"><float_array id=\"colmesh-pos-arr\" count=\"" + col.V.Length * 3 + "\">");
                foreach (var v in col.V) { var b = BeamNGCoordinateConverter.Position(v); w.Write(F(b.x)); w.Write(' '); w.Write(F(b.y)); w.Write(' '); w.Write(F(b.z)); w.Write(' '); }
                w.Write("</float_array><technique_common><accessor source=\"#colmesh-pos-arr\" count=\"" + col.V.Length + "\" stride=\"3\"><param name=\"X\" type=\"float\"/><param name=\"Y\" type=\"float\"/><param name=\"Z\" type=\"float\"/></accessor></technique_common></source>");
                w.Write("<vertices id=\"colmesh-vtx\"><input semantic=\"POSITION\" source=\"#colmesh-pos\"/></vertices>");
                w.Write("<triangles material=\"" + uniq[0] + "-material\" count=\"" + col.T.Length / 3 + "\"><input semantic=\"VERTEX\" source=\"#colmesh-vtx\" offset=\"0\"/><p>");
                for (int i = 0; i + 2 < col.T.Length; i += 3) { w.Write(col.T[i]); w.Write(' '); w.Write(col.T[i + 2]); w.Write(' '); w.Write(col.T[i + 1]); w.Write(' '); }
                w.WriteLine("</p></triangles></mesh></geometry>");
            }
            w.WriteLine("</library_geometries>");

            w.WriteLine("<library_visual_scenes><visual_scene id=\"Scene\">");
            w.WriteLine("<node id=\"base00\" name=\"base00\" type=\"NODE\">");
            if (opt != null && opt.BillboardPixels > 0) w.WriteLine("<node id=\"bb_autobillboard" + opt.BillboardPixels + "\" name=\"bb_autobillboard" + opt.BillboardPixels + "\" type=\"NODE\"/>");
            if (opt != null && opt.NullPixels > 0) w.WriteLine("<node id=\"nulldetail" + opt.NullPixels + "\" name=\"nulldetail" + opt.NullPixels + "\" type=\"NODE\"/>");
            w.WriteLine("<node id=\"start01\" name=\"start01\" type=\"NODE\">");
            w.WriteLine("<node id=\"" + nodeName + "\" name=\"" + nodeName + "\" type=\"NODE\"><instance_geometry url=\"#" + id + "-geom\"><bind_material><technique_common>");
            foreach (var s in uniq) w.WriteLine("<instance_material symbol=\"" + s + "-material\" target=\"#" + s + "-material\"/>");
            w.WriteLine("</technique_common></bind_material></instance_geometry></node>");
            if (opt != null && opt.Lod1 != null && opt.Lod1Pixels > 0)
            {
                string n1 = BeamNGEditorUtilities.SafeId(name) + "_a" + opt.Lod1Pixels;
                w.WriteLine("<node id=\"" + n1 + "\" name=\"" + n1 + "\" type=\"NODE\"><instance_geometry url=\"#" + id + "_l1-geom\"><bind_material><technique_common>");
                foreach (var s2 in uniq) w.WriteLine("<instance_material symbol=\"" + s2 + "-material\" target=\"#" + s2 + "-material\"/>");
                w.WriteLine("</technique_common></bind_material></instance_geometry></node>");
            }
            if (col != null && col.V != null && col.T != null && col.T.Length >= 9)
                w.WriteLine("<node id=\"Colmesh-1\" name=\"Colmesh-1\" type=\"NODE\"><instance_geometry url=\"#colmesh-geom\"><bind_material><technique_common><instance_material symbol=\"" + uniq[0] + "-material\" target=\"#" + uniq[0] + "-material\"/></technique_common></bind_material></instance_geometry></node>");
            w.WriteLine("</node></node>");
            w.WriteLine("</visual_scene></library_visual_scenes>");
            w.WriteLine("<scene><instance_visual_scene url=\"#Scene\"/></scene></COLLADA>");
        }

        public static string BuildDae(Mesh m, string name, string[] matSymbols, BeamNGExportReport r)
        {
            var sw = new StringWriter(CultureInfo.InvariantCulture);
            Write(sw, m, name, matSymbols, r);
            return sw.ToString();
        }

        static string CollisionString(BeamNGCollisionMode m)
        {
            switch (m)
            {
                case BeamNGCollisionMode.None: return "None";
                case BeamNGCollisionMode.Bounds: return "Bounds";
                case BeamNGCollisionMode.Simplified: return "Collision Mesh";
                default: return "Visible Mesh Final"; // BeamNG logs a performance warning for plain "Visible Mesh"
            }
        }

        public const int MaxVertsPerShape = 60000;

        /// <summary>Splits a mesh with more than MaxVertsPerShape vertices into parts (one material/submesh each) so that no single shape exceeds 16-bit vertex indices.</summary>
        static List<KeyValuePair<Mesh, int>> SplitMesh(Mesh m)
        {
            var res = new List<KeyValuePair<Mesh, int>>();     // part mesh, original submesh index
            var verts = m.vertices; var nrm = m.normals; var uv = m.uv; var cols = m.colors;
            bool hasN = nrm != null && nrm.Length == verts.Length, hasUV = uv != null && uv.Length == verts.Length, hasC = cols != null && cols.Length == verts.Length;
            int partNo = 0;
            for (int sm = 0; sm < m.subMeshCount; sm++)
            {
                if (m.GetTopology(sm) != MeshTopology.Triangles) continue;
                var tris = m.GetTriangles(sm);
                int pos = 0;
                while (pos < tris.Length)
                {
                    var map = new Dictionary<int, int>();
                    var pv = new List<Vector3>(); var pn = new List<Vector3>(); var pu = new List<Vector2>(); var pc = new List<Color>(); var pt = new List<int>();
                    while (pos + 2 < tris.Length && map.Count + 3 <= MaxVertsPerShape)
                    {
                        for (int k = 0; k < 3; k++)
                        {
                            int o = tris[pos + k]; int n;
                            if (!map.TryGetValue(o, out n))
                            {
                                n = pv.Count; map[o] = n; pv.Add(verts[o]);
                                if (hasN) pn.Add(nrm[o]); if (hasUV) pu.Add(uv[o]); if (hasC) pc.Add(cols[o]);
                            }
                            pt.Add(n);
                        }
                        pos += 3;
                    }
                    var part = new Mesh { name = m.name + "_p" + (partNo++), hideFlags = HideFlags.DontSave };
                    part.SetVertices(pv); if (hasN) part.SetNormals(pn); if (hasUV) part.SetUVs(0, pu); if (hasC) part.SetColors(pc);
                    part.SetTriangles(pt, 0);
                    res.Add(new KeyValuePair<Mesh, int>(part, sm));
                }
            }
            return res;
        }

        static List<MeshInstance> ExpandBig(BeamNGExportContext c, List<MeshInstance> src, List<Mesh> created)
        {
            var list = new List<MeshInstance>(src.Count);
            var cache = new Dictionary<int, List<KeyValuePair<Mesh, int>>>();
            int split = 0;
            foreach (var inst in src)
            {
                if (inst.Mesh == null || inst.Mesh.vertexCount <= MaxVertsPerShape) { list.Add(inst); continue; }
                List<KeyValuePair<Mesh, int>> parts;
                if (!cache.TryGetValue(inst.Mesh.GetInstanceID(), out parts))
                {
                    parts = SplitMesh(inst.Mesh); cache[inst.Mesh.GetInstanceID()] = parts;
                    foreach (var p in parts) created.Add(p.Key);
                    c.Report.Add(ReportStatus.PASS, "Mesh", $"'{inst.Path}': {inst.Mesh.vertexCount:N0} vertices - split into {parts.Count} part(s) (a single shape with more than ~65,000 vertices may not load in BeamNG).");
                }
                if (parts.Count == 0) { list.Add(inst); continue; }
                foreach (var p in parts)
                {
                    var mats = inst.Materials != null && inst.Materials.Length > 0 ? new[] { inst.Materials[Mathf.Min(p.Value, inst.Materials.Length - 1)] } : new Material[] { null };
                    list.Add(new MeshInstance { Renderer = inst.Renderer, Mesh = p.Key, Materials = mats, Path = inst.Path, Collision = inst.Collision });
                }
                split++;
            }
            return list;
        }

        /// <summary>Exports all meshes as DAE (deduplicated) and returns the TSStatic lines.</summary>
        public static List<string> ExportAll(BeamNGExportContext c, ScanResult scan, BeamNGMaterialRegistry reg, ExportManifest manifest)
        {
            var lines = new List<string>();
            var files = new Dictionary<string, string>();
            int counter = 0, instanceNo = 0, autoLods = 0;
            var colCache = new Dictionary<int, Mesh>(); var lodCache = new Dictionary<int, Mesh>();
            var splitMeshes = new List<Mesh>();
            var meshList = ExpandBig(c, scan.Meshes, splitMeshes);

            for (int i = 0; i < meshList.Count; i++)
            {
                var inst = meshList[i];
                if (i % 16 == 0 && c.Cancelled("Mesh " + (i + 1) + "/" + meshList.Count + ": " + inst.Path, 0.45f + 0.35f * i / meshList.Count)) return null;

                var syms = MaterialSymbols(inst.Mesh, inst.Materials, reg);
                var tiling = new Vector4[inst.Mesh.subMeshCount];
                var tileSig = new System.Text.StringBuilder();
                for (int sm = 0; sm < tiling.Length; sm++)
                {
                    var mat = (inst.Materials != null && inst.Materials.Length > 0) ? inst.Materials[Mathf.Min(sm, inst.Materials.Length - 1)] : null;
                    Vector2 sc0 = Vector2.one, of0 = Vector2.zero;
                    if (mat != null) BeamNGMaterialExporter.TexST(mat, out sc0, out of0);
                    tiling[sm] = new Vector4(sc0.x, sc0.y, of0.x, of0.y);
                    tileSig.Append(tiling[sm].ToString("F4")).Append(';');
                }
                var ls0 = inst.Renderer.transform.lossyScale;
                var mirror = new Vector3(ls0.x < 0f ? -1f : 1f, ls0.y < 0f ? -1f : 1f, ls0.z < 0f ? -1f : 1f);
                bool simpl = inst.Collision == BeamNGCollisionMode.Simplified;
                Mesh colSrc = null;
                if (simpl && !colCache.TryGetValue(inst.Mesh.GetInstanceID(), out colSrc))
                {
                    colSrc = BeamNGMeshSimplifier.Simplify(inst.Mesh, 0.1f, false);
                    colCache[inst.Mesh.GetInstanceID()] = colSrc;
                }
                if (simpl && colSrc == null) inst.Collision = BeamNGCollisionMode.VisibleMesh;   // could not be simplified: keep exact collision
                simpl = inst.Collision == BeamNGCollisionMode.Simplified;
                Mesh lodMesh = null;
                if (c.Settings.autoLod && BeamNGMeshSimplifier.TriangleCount(inst.Mesh) >= c.Settings.autoLodMinTris && !(inst.Renderer is SkinnedMeshRenderer))
                {
                    if (!lodCache.TryGetValue(inst.Mesh.GetInstanceID(), out lodMesh))
                    {
                        lodMesh = BeamNGMeshSimplifier.Simplify(inst.Mesh, 0.25f, true);
                        lodCache[inst.Mesh.GetInstanceID()] = lodMesh;
                        if (lodMesh != null) c.Report.Exported += 0;
                    }
                }
                var key = inst.Mesh.GetInstanceID() + "|" + string.Join(",", syms) + "|" + tileSig + "|" + mirror + "|" + (simpl ? "c" : "-") + (lodMesh != null ? "l" : "-");
                string rel;
                if (!files.TryGetValue(key, out rel))
                {
                    rel = "art/shapes/" + BeamNGEditorUtilities.ShortId(inst.Mesh.name, 40) + "_" + (counter++) + ".dae";
                    ShapeOptions so = null;
                    if (simpl || lodMesh != null)
                    {
                        so = new ShapeOptions();
                        if (simpl)
                        {
                            var cm = BeamNGMeshSimplifier.ToCol(colSrc);
                            for (int vi = 0; vi < cm.V.Length; vi++) cm.V[vi] = Vector3.Scale(cm.V[vi], mirror);
                            so.Col = cm;
                        }
                        if (lodMesh != null) { so.LodPixels = 60; so.Lod1 = lodMesh; so.Lod1Pixels = 12; }
                    }
                    using (var sw = new StreamWriter(c.Abs(rel), false, new UTF8Encoding(false), 1 << 16))
                        Write(sw, inst.Mesh, inst.Mesh.name, syms, c.Report, tiling, mirror, so);
                    if (lodMesh != null) autoLods++;
                    files[key] = rel;
                    c.Require(rel);
                }

                var t = inst.Renderer.transform;
                var pos = BeamNGCoordinateConverter.Position(t.position);
                var rotQ = BeamNGCoordinateConverter.Rotation(t.rotation);
                var rot = BeamNGCoordinateConverter.RotationMatrix(rotQ);
                var sc = BeamNGCoordinateConverter.Scale(new Vector3(Mathf.Abs(t.lossyScale.x), Mathf.Abs(t.lossyScale.y), Mathf.Abs(t.lossyScale.z))); // mirroring is baked into the mesh
                var objName = BeamNGEditorUtilities.SafeId(inst.Renderer.name) + "_" + (instanceNo++);

                string lampName; var collision = inst.Collision;
                if (c.LampNames.TryGetValue(inst.Renderer, out lampName)) { objName = lampName; collision = BeamNGCollisionMode.None; }
                var line = BeamNGLevelBuilder.StaticLine(objName, c.VPath(rel), pos, rot, sc, CollisionString(collision));
                var sigLight = inst.Renderer.GetComponentInParent<BeamNGTrafficLight>();
                string sigName;
                if (sigLight != null && c.SignalNames.TryGetValue(sigLight, out sigName))
                    line = line.Substring(0, line.Length - 1) + ",\"signalInstance\":" + BeamNGJson.Q(sigName) + "}";
                lines.Add(line);

                string prefab = "";
                var src = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(inst.Renderer.gameObject);
                if (src != null) prefab = UnityEditor.AssetDatabase.GetAssetPath(src);
                manifest.objects.Add(new ManifestObject
                {
                    id = UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(inst.Renderer.gameObject).ToString(),
                    unityName = inst.Renderer.name,
                    hierarchyPath = inst.Path,
                    prefabSource = prefab,
                    beamNGName = objName,
                    meshFile = rel,
                    materials = syms,
                    collision = CollisionString(inst.Collision),
                    position = new[] { pos.x, pos.y, pos.z },
                    rotationMatrix = rot,
                    scale = new[] { sc.x, sc.y, sc.z }
                });
            }

            foreach (var cm in colCache.Values) if (cm != null) Object.DestroyImmediate(cm);
            foreach (var lm in lodCache.Values) if (lm != null) Object.DestroyImmediate(lm);
            foreach (var sm0 in splitMeshes) if (sm0 != null) Object.DestroyImmediate(sm0);
            c.Report.Exported += lines.Count;
            c.Report.Add(ReportStatus.PASS, "Mesh", $"{files.Count} unique mesh(es) written as .dae, {lines.Count} TSStatic object(s).");
            if (autoLods > 0) c.Report.Add(ReportStatus.PASS, "Performance", $"{autoLods} heavy mesh file(s) got an extra simplified distance detail level (lower triangle count when far away).");
            if (scan.SkippedLod > 0) c.Report.Add(ReportStatus.PASS, "Mesh", $"{scan.SkippedLod} LOD 1+ renderer(s) skipped (only LOD0 exported).");
            if (scan.MirroredObjects > 0) c.Report.Add(ReportStatus.PASS, "Mesh", $"{scan.MirroredObjects} object(s) with negative scale: mirroring baked into the mesh (BeamNG does not render negative scale).");
            return lines;
        }
    }
}
