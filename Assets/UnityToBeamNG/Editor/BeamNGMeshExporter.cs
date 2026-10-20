using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace UnityToBeamNG
{
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
                res[i] = reg.NameOf((mats != null && i < mats.Length) ? mats[i] : null);
            return res;
        }

        /// <summary>tiling: per submesh (scaleX, scaleY, offsetX, offsetY) baked into the UVs (null = unchanged).</summary>
        public static void Write(TextWriter w, Mesh m, string name, string[] matSymbols, BeamNGExportReport r, Vector4[] tiling = null)
        {
            var id = BeamNGEditorUtilities.SafeId(name);
            var verts = m.vertices;
            var nrm = m.normals;
            var uv = m.uv;
            bool hasN = nrm != null && nrm.Length == verts.Length;
            bool hasUV = uv != null && uv.Length == verts.Length;

            w.WriteLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            w.WriteLine("<COLLADA xmlns=\"http://www.collada.org/2005/11/COLLADASchema\" version=\"1.4.1\">");
            w.WriteLine("<asset><contributor><authoring_tool>Unity to BeamNG</authoring_tool></contributor><unit name=\"meter\" meter=\"1\"/><up_axis>Z_UP</up_axis></asset>");

            var uniq = new List<string>();
            foreach (var s in matSymbols) if (!uniq.Contains(s)) uniq.Add(s);

            w.WriteLine("<library_effects>");
            foreach (var s in uniq)
                w.WriteLine("<effect id=\"" + s + "-fx\"><profile_COMMON><technique sid=\"common\"><phong><diffuse><color>0.6 0.6 0.6 1</color></diffuse></phong></technique></profile_COMMON></effect>");
            w.WriteLine("</library_effects>");
            w.WriteLine("<library_materials>");
            foreach (var s in uniq)
                w.WriteLine("<material id=\"" + s + "\" name=\"" + s + "\"><instance_effect url=\"#" + s + "-fx\"/></material>");
            w.WriteLine("</library_materials>");

            w.WriteLine("<library_geometries><geometry id=\"" + id + "-geom\" name=\"" + id + "\"><mesh>");
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
            w.WriteLine("<vertices id=\"" + id + "-vtx\"><input semantic=\"POSITION\" source=\"#" + id + "-pos\"/></vertices>");

            for (int s = 0; s < m.subMeshCount; s++)
            {
                if (m.GetTopology(s) != MeshTopology.Triangles)
                {
                    r.Add(ReportStatus.WARNING, "Mesh", $"'{name}': submesh {s} is not triangle topology and is skipped.");
                    continue;
                }
                var tris = m.GetTriangles(s);
                w.Write("<triangles material=\"" + matSymbols[s] + "\" count=\"" + tris.Length / 3 + "\">");
                w.Write("<input semantic=\"VERTEX\" source=\"#" + id + "-vtx\" offset=\"0\"/>");
                if (hasN) w.Write("<input semantic=\"NORMAL\" source=\"#" + id + "-nrm\" offset=\"0\"/>");
                if (hasUV) w.Write("<input semantic=\"TEXCOORD\" source=\"#" + id + "-uv\" offset=\"0\" set=\"0\"/>");
                w.Write("<p>");
                for (int i = 0; i < tris.Length; i += 3)
                { w.Write(tris[i]); w.Write(' '); w.Write(tris[i + 2]); w.Write(' '); w.Write(tris[i + 1]); w.Write(' '); }
                w.WriteLine("</p></triangles>");
            }
            w.WriteLine("</mesh></geometry></library_geometries>");

            w.WriteLine("<library_visual_scenes><visual_scene id=\"Scene\">");
            w.WriteLine("<node id=\"base00\" name=\"base00\" type=\"NODE\"><node id=\"start01\" name=\"start01\" type=\"NODE\">");
            w.WriteLine("<node id=\"" + id + "\" name=\"" + id + "\" type=\"NODE\"><instance_geometry url=\"#" + id + "-geom\"><bind_material><technique_common>");
            foreach (var s in uniq) w.WriteLine("<instance_material symbol=\"" + s + "\" target=\"#" + s + "\"/>");
            w.WriteLine("</technique_common></bind_material></instance_geometry></node>");
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
                default: return "Visible Mesh";
            }
        }

        /// <summary>Exports all meshes as DAE (deduplicated) and returns the TSStatic lines.</summary>
        public static List<string> ExportAll(BeamNGExportContext c, ScanResult scan, BeamNGMaterialRegistry reg, ExportManifest manifest)
        {
            var lines = new List<string>();
            var files = new Dictionary<string, string>();
            int counter = 0, instanceNo = 0;

            for (int i = 0; i < scan.Meshes.Count; i++)
            {
                var inst = scan.Meshes[i];
                if (i % 16 == 0 && c.Cancelled("Mesh " + (i + 1) + "/" + scan.Meshes.Count + ": " + inst.Path, 0.45f + 0.35f * i / scan.Meshes.Count)) return null;

                var syms = MaterialSymbols(inst.Mesh, inst.Materials, reg);
                var tiling = new Vector4[inst.Mesh.subMeshCount];
                var tileSig = new System.Text.StringBuilder();
                for (int sm = 0; sm < tiling.Length; sm++)
                {
                    var mat = (inst.Materials != null && sm < inst.Materials.Length) ? inst.Materials[sm] : null;
                    var sc0 = mat != null ? mat.mainTextureScale : Vector2.one;
                    var of0 = mat != null ? mat.mainTextureOffset : Vector2.zero;
                    tiling[sm] = new Vector4(sc0.x, sc0.y, of0.x, of0.y);
                    tileSig.Append(tiling[sm].ToString("F4")).Append(';');
                }
                var key = inst.Mesh.GetInstanceID() + "|" + string.Join(",", syms) + "|" + tileSig;
                string rel;
                if (!files.TryGetValue(key, out rel))
                {
                    rel = "art/shapes/" + BeamNGEditorUtilities.SafeId(inst.Mesh.name) + "_" + (counter++) + ".dae";
                    using (var sw = new StreamWriter(c.Abs(rel), false, new UTF8Encoding(false), 1 << 16))
                        Write(sw, inst.Mesh, inst.Mesh.name, syms, c.Report, tiling);
                    files[key] = rel;
                    c.Require(rel);
                }

                var t = inst.Renderer.transform;
                var pos = BeamNGCoordinateConverter.Position(t.position);
                var rotQ = BeamNGCoordinateConverter.Rotation(t.rotation);
                var rot = BeamNGCoordinateConverter.RotationMatrix(rotQ);
                var sc = BeamNGCoordinateConverter.Scale(t.lossyScale);
                var objName = BeamNGEditorUtilities.SafeId(inst.Renderer.name) + "_" + (instanceNo++);

                lines.Add(BeamNGLevelBuilder.StaticLine(objName, c.VPath(rel), pos, rot, sc, CollisionString(inst.Collision)));

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

            c.Report.Exported += lines.Count;
            c.Report.Add(ReportStatus.PASS, "Mesh", $"{files.Count} unique mesh(es) written as .dae, {lines.Count} TSStatic object(s).");
            c.Report.Add(ReportStatus.WARNING, "Mesh", "DAE files and axis conversion have not been verified in BeamNG.", "After loading, check orientation/size of a test object.");
            if (scan.SkippedLod > 0) c.Report.Add(ReportStatus.PASS, "Mesh", $"{scan.SkippedLod} LOD 1+ renderer(s) skipped (only LOD0 exported).");
            if (scan.MirroredObjects > 0) c.Report.Add(ReportStatus.WARNING, "Mesh", $"{scan.MirroredObjects} object(s) with negative scale (mirrored): face direction may be wrong.", "Apply the mirroring in the model.");
            return lines;
        }
    }
}
