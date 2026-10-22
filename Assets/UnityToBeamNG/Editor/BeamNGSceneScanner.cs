using System.Collections.Generic;
using UnityEngine;

namespace UnityToBeamNG
{
    public class MeshInstance
    {
        public Renderer Renderer;
        public Mesh Mesh;
        public Material[] Materials;
        public string Path;
        public BeamNGCollisionMode Collision;
    }

    public class SpawnInfo
    {
        public string Name;
        public string Display;
        public Transform T;
        public bool IsDefault;
    }

    public class ScanResult
    {
        public readonly List<Terrain> Terrains = new List<Terrain>();
        public readonly List<MeshInstance> Meshes = new List<MeshInstance>();
        public readonly List<SpawnInfo> Spawns = new List<SpawnInfo>();
        public readonly Dictionary<string, int> Unsupported = new Dictionary<string, int>();
        public int Detected;
        public int InvalidMeshes;
        public int MissingMaterials;
        public int SkippedLod;
        public int SkippedExcluded;
        public int MirroredObjects;
        public int SkinnedBaked;
        public readonly List<string> NotExported = new List<string>();
        public long Triangles;

        public SpawnInfo DefaultSpawn { get { foreach (var s in Spawns) if (s.IsDefault) return s; return null; } }

        public void CountUnsupported(string type)
        {
            int n; Unsupported.TryGetValue(type, out n); Unsupported[type] = n + 1;
        }
    }

    public static class BeamNGSceneScanner
    {
        public const long TriangleWarnThreshold = 5000000;

        public static ScanResult Scan(BeamNGExportSettings s, BeamNGExportReport r)
        {
            var res = new ScanResult();

            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                if (!t.gameObject.activeInHierarchy) continue;
                if (t.terrainData == null)
                {
                    r.Add(ReportStatus.ERROR, "Terrain", $"'{t.name}' has no TerrainData.", "Assign TerrainData or remove the object.");
                    continue;
                }
                res.Terrains.Add(t);
            }
            res.Terrains.Sort((a, b) => string.CompareOrdinal(BeamNGEditorUtilities.HierarchyPath(a.transform), BeamNGEditorUtilities.HierarchyPath(b.transform)));

            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!mr.gameObject.activeInHierarchy || !mr.enabled) { res.NotExported.Add(BeamNGEditorUtilities.HierarchyPath(mr.transform) + ": inactive/disabled"); continue; }
                var path = BeamNGEditorUtilities.HierarchyPath(mr.transform);

                var cs = mr.GetComponentInParent<BeamNGCollisionSettings>();
                if (cs != null && cs.excludeFromExport) { res.SkippedExcluded++; continue; }

                // LOD: export LOD0 only
                var lg = mr.GetComponentInParent<LODGroup>();
                if (lg != null)
                {
                    var lods = lg.GetLODs();
                    bool inAny = false, in0 = false;
                    for (int i = 0; i < lods.Length; i++)
                        foreach (var rend in lods[i].renderers)
                            if (rend == mr) { inAny = true; if (i == 0) in0 = true; }
                    if (inAny && !in0) { res.SkippedLod++; res.NotExported.Add(path + ": LOD1+"); continue; }
                }

                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null)
                {
                    res.InvalidMeshes++;
                    r.Add(ReportStatus.ERROR, "Mesh", $"'{path}': MeshRenderer without a mesh.", "Assign a MeshFilter/mesh or remove the renderer.");
                    continue;
                }
                var mesh = mf.sharedMesh;
                if (!mesh.isReadable)
                {
                    res.InvalidMeshes++;
                    r.Add(ReportStatus.ERROR, "Mesh", $"'{path}': Mesh '{mesh.name}' is not readable.", "Enable 'Read/Write' in the model's import settings.");
                    continue;
                }
                var mats = mr.sharedMaterials;
                foreach (var m in mats) if (m == null) res.MissingMaterials++;

                var mode = cs != null ? cs.mode : BeamNGCollisionMode.Auto;
                if (mode == BeamNGCollisionMode.Auto)
                    mode = (s.collideAllByDefault || mr.GetComponentInParent<Collider>() != null) ? BeamNGCollisionMode.VisibleMesh : BeamNGCollisionMode.None;

                var ls = mr.transform.lossyScale;
                if (ls.x * ls.y * ls.z < 0f) res.MirroredObjects++;

                res.Meshes.Add(new MeshInstance { Renderer = mr, Mesh = mesh, Materials = mats, Path = path, Collision = mode });
                for (int i = 0; i < mesh.subMeshCount; i++) res.Triangles += mesh.GetIndexCount(i) / 3;
            }
            res.Meshes.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

            // SkinnedMeshRenderer (e.g. Blender/FBX models with an armature or shape keys): baked in the current pose and exported as a static mesh.
            foreach (var sr in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
            {
                if (!sr.gameObject.activeInHierarchy || !sr.enabled || sr.sharedMesh == null) continue;
                var spath = BeamNGEditorUtilities.HierarchyPath(sr.transform);
                var scs = sr.GetComponentInParent<BeamNGCollisionSettings>();
                if (scs != null && scs.excludeFromExport) { res.SkippedExcluded++; continue; }
                try
                {
                    var baked = new Mesh { name = sr.sharedMesh.name + "_baked", hideFlags = HideFlags.DontSave };
                    sr.BakeMesh(baked);
                    var smats = sr.sharedMaterials;
                    foreach (var m in smats) if (m == null) res.MissingMaterials++;
                    var smode = scs != null ? scs.mode : BeamNGCollisionMode.Auto;
                    if (smode == BeamNGCollisionMode.Auto)
                        smode = (s.collideAllByDefault || sr.GetComponentInParent<Collider>() != null) ? BeamNGCollisionMode.VisibleMesh : BeamNGCollisionMode.None;
                    res.Meshes.Add(new MeshInstance { Renderer = sr, Mesh = baked, Materials = smats, Path = spath, Collision = smode });
                    for (int i = 0; i < baked.subMeshCount; i++) res.Triangles += baked.GetIndexCount(i) / 3;
                    res.SkinnedBaked++;
                    var sl = sr.transform.lossyScale; if (sl.x * sl.y * sl.z < 0f) res.MirroredObjects++;
                }
                catch (System.Exception e) { res.CountUnsupported("SkinnedMeshRenderer (bake failed: " + e.Message + ")"); }
            }
            res.Meshes.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            foreach (var x in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) res.CountUnsupported("ParticleSystem");

            CollectSpawns(s, res);
            res.Detected = res.Terrains.Count + res.Meshes.Count + res.InvalidMeshes + res.SkippedLod + res.SkippedExcluded;
            return res;
        }

        static void CollectSpawns(BeamNGExportSettings s, ScanResult res)
        {
            var used = new HashSet<string>();
            if (s.spawnObject != null)
            {
                res.Spawns.Add(new SpawnInfo { Name = "spawns_default", Display = "Default", T = s.spawnObject, IsDefault = true });
                used.Add("spawns_default");
            }
            var comps = new List<BeamNGSpawnPoint>(Object.FindObjectsByType<BeamNGSpawnPoint>(FindObjectsSortMode.None));
            comps.Sort((a, b) => string.CompareOrdinal(BeamNGEditorUtilities.HierarchyPath(a.transform), BeamNGEditorUtilities.HierarchyPath(b.transform)));
            bool defaultTaken = s.spawnObject != null;
            foreach (var c in comps)
            {
                if (!c.gameObject.activeInHierarchy || c.transform == s.spawnObject) continue;
                var baseName = !string.IsNullOrEmpty(c.spawnName) ? BeamNGEditorUtilities.SafeId(c.spawnName).ToLowerInvariant()
                                                                   : "spawns_" + BeamNGEditorUtilities.SafeId(c.name).ToLowerInvariant();
                var name = baseName; int n = 2;
                while (used.Contains(name)) name = baseName + "_" + (n++);
                used.Add(name);
                var info = new SpawnInfo { Name = name, Display = string.IsNullOrEmpty(c.displayName) ? c.name : c.displayName, T = c.transform };
                if (!defaultTaken && c.isDefault) { info.IsDefault = true; defaultTaken = true; }
                res.Spawns.Add(info);
            }
            if (!defaultTaken && res.Spawns.Count > 0) res.Spawns[0].IsDefault = true;
        }
    }
}
