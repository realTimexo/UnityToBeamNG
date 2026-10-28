using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityToBeamNG
{
    /// <summary>
    /// Water -> WaterBlock lines (docs: position = surface, scale X/Y = footprint, Z = volume depth, material).
    /// Sources: BeamNGWater objects, and (optional) water that already exists in the scene: flat meshes whose shader/material
    /// name contains "water" (they are then NOT exported as normal meshes) and HDRP WaterSurface components.
    /// </summary>
    public static class BeamNGWaterExporter
    {
        class Item { public string Name, Material; public Vector3 Center; public float SizeX, SizeZ, Yaw, Depth; }

        static bool HasWaterName(Material m)
        {
            if (m == null) return false;
            string mn = (m.name ?? "").ToLowerInvariant(), sn = m.shader != null ? (m.shader.name ?? "").ToLowerInvariant() : "";
            bool w = mn.Contains("water") || sn.Contains("water");
            return w && !mn.Contains("waterfall") && !mn.Contains("tower") && !mn.Contains("bottle") && !mn.Contains("tank");
        }

        /// <summary>A big, flat mesh with a water material = a water surface that is exported as BeamNG water instead of a mesh.</summary>
        public static bool IsAutoWaterMesh(MeshRenderer mr, BeamNGExportSettings s)
        {
            if (s == null || !s.autoDetectWater || mr == null) return false;
            if (mr.GetComponentInParent<BeamNGWater>() != null) return false;
            var b = mr.bounds;
            if (b.size.y > 1f || b.size.x < 4f || b.size.z < 4f) return false;
            foreach (var m in mr.sharedMaterials) if (HasWaterName(m)) return true;
            return false;
        }

        static string MaterialFor(string hint)
        {
            hint = (hint ?? "").ToLowerInvariant();
            if (hint.Contains("ocean") || hint.Contains("sea")) return "water_ocean";
            if (hint.Contains("river")) return "water_river";
            if (hint.Contains("pool")) return "water_pool";
            if (hint.Contains("stream") || hint.Contains("creek")) return "water_stream";
            return "water_lake";
        }

        public static List<string> Export(BeamNGExportContext c)
        {
            var items = new List<Item>();
            var list = new List<BeamNGWater>(UnityEngine.Object.FindObjectsByType<BeamNGWater>(FindObjectsSortMode.None));
            list.Sort((a, b) => string.CompareOrdinal(BeamNGEditorUtilities.HierarchyPath(a.transform), BeamNGEditorUtilities.HierarchyPath(b.transform)));
            foreach (var w in list)
            {
                if (!w.gameObject.activeInHierarchy || !w.exportEnabled) continue;
                var t = w.transform; var ls = t.lossyScale;
                items.Add(new Item { Name = w.name, Center = t.position, SizeX = Mathf.Abs(ls.x), SizeZ = Mathf.Abs(ls.z), Yaw = t.eulerAngles.y, Depth = w.depth, Material = w.MaterialName });
            }
            int auto = 0;
            if (c.Settings.autoDetectWater)
            {
                foreach (var mr in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    if (!mr.gameObject.activeInHierarchy || !mr.enabled || !IsAutoWaterMesh(mr, c.Settings)) continue;
                    var mf = mr.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                    var mb = mf.sharedMesh.bounds; var ls = mr.transform.lossyScale; var b = mr.bounds;
                    var ctr = mr.transform.TransformPoint(mb.center);
                    items.Add(new Item { Name = mr.name, Center = new Vector3(ctr.x, b.max.y, ctr.z), SizeX = Mathf.Abs(mb.size.x * ls.x), SizeZ = Mathf.Abs(mb.size.z * ls.z), Yaw = mr.transform.eulerAngles.y, Depth = 10f, Material = MaterialFor(mr.name + " " + (mr.sharedMaterial != null ? mr.sharedMaterial.name : "")) });
                    auto++;
                }
                // HDRP water (UnityEngine.Rendering.HighDefinition.WaterSurface), read without a dependency
                foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                {
                    if (mb == null || mb.GetType().Name != "WaterSurface" || !mb.gameObject.activeInHierarchy) continue;
                    var ls = mb.transform.lossyScale;
                    items.Add(new Item { Name = mb.name, Center = mb.transform.position, SizeX = Mathf.Max(20f, Mathf.Abs(ls.x)), SizeZ = Mathf.Max(20f, Mathf.Abs(ls.z)), Yaw = mb.transform.eulerAngles.y, Depth = 10f, Material = MaterialFor(mb.name) });
                    auto++;
                }
            }

            var lines = new List<string>(); int n = 0;
            foreach (var it in items)
            {
                float sx = Mathf.Max(1f, it.SizeX), sz = Mathf.Max(1f, it.SizeZ);
                var yaw = Quaternion.Euler(0f, it.Yaw, 0f);                 // a water surface is horizontal: only the yaw is used
                var pos = BeamNGCoordinateConverter.Position(it.Center);
                var rot = BeamNGCoordinateConverter.RotationMatrix(BeamNGCoordinateConverter.Rotation(yaw));
                var name = "water_" + BeamNGEditorUtilities.SafeId(it.Name).ToLowerInvariant() + "_" + (n++);
                lines.Add(new JsonObj().Str("class", "WaterBlock").Str("name", name).Str("__parent", "Water")
                    .Arr("position", pos.x, pos.y, pos.z).Raw("rotationMatrix", BeamNGJson.A(rot))
                    .Arr("scale", sx, sz, Mathf.Max(0.5f, it.Depth)).Str("material", it.Material).Num("gridElementSize", Mathf.Clamp(Mathf.Min(sx, sz) / 20f, 2f, 10f)).ToString());
                c.WaterAreas.Add(new WaterArea { Center = new Vector2(it.Center.x, it.Center.z), Size = new Vector2(sx, sz), Yaw = it.Yaw });
            }
            if (lines.Count > 0)
            {
                c.Report.Exported += lines.Count;
                c.Report.Add(ReportStatus.PASS, "Water", $"{lines.Count} water volume(s) exported as WaterBlock ({auto} found automatically in the scene: flat meshes with a water material / HDRP WaterSurface; surface = object height). Stock materials: water_lake / ocean / river / pool / stream.");
            }
            return lines;
        }
    }
}
