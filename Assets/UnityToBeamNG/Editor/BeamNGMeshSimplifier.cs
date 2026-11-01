using System.Collections.Generic;
using UnityEngine;

namespace UnityToBeamNG
{
    /// <summary>
    /// Fast vertex-clustering simplification (grid based). Used for (1) simplified collision meshes of heavy objects and
    /// (2) a lower visual detail level for distant viewing. Submeshes (materials) are kept apart, UVs/normals are averaged per cluster.
    /// </summary>
    public static class BeamNGMeshSimplifier
    {
        public static int TriangleCount(Mesh m)
        {
            int n = 0;
            for (int s = 0; s < m.subMeshCount; s++) if (m.GetTopology(s) == MeshTopology.Triangles) n += (int)(m.GetIndexCount(s) / 3);
            return n;
        }

        /// <summary>Simplifies towards <paramref name="targetRatio"/> (0..1) of the triangles; returns null if nothing meaningful is gained.</summary>
        public static Mesh Simplify(Mesh src, float targetRatio, bool keepUv)
        {
            int total = TriangleCount(src);
            if (total < 24) return null;
            int target = Mathf.Max(12, Mathf.RoundToInt(total * targetRatio));
            var b = src.bounds;
            float diag = Mathf.Max(0.01f, b.size.magnitude);
            // search the cell size: smaller cells keep more triangles
            float lo = diag / 2000f, hi = diag / 2f, cell = diag / 40f;
            Mesh best = null;
            for (int it = 0; it < 7; it++)
            {
                var res = Cluster(src, cell, keepUv);
                int tc = res == null ? 0 : TriangleCount(res);
                if (res != null && tc >= 4) best = res;
                if (tc > target) lo = cell; else hi = cell;
                cell = Mathf.Sqrt(lo * hi);
                if (tc > 0 && Mathf.Abs(tc - target) < target * 0.15f) break;
            }
            if (best == null || TriangleCount(best) >= total * 0.9f) return null;
            return best;
        }

        static Mesh Cluster(Mesh src, float cell, bool keepUv)
        {
            var verts = src.vertices; var nrm = src.normals; var uv = src.uv;
            bool hasN = nrm != null && nrm.Length == verts.Length, hasUV = keepUv && uv != null && uv.Length == verts.Length;
            var min = src.bounds.min;
            var outV = new List<Vector3>(); var outN = new List<Vector3>(); var outUV = new List<Vector2>(); var count = new List<int>();
            var tris = new List<int[]>();
            for (int s = 0; s < src.subMeshCount; s++)
            {
                if (src.GetTopology(s) != MeshTopology.Triangles) { tris.Add(new int[0]); continue; }
                var idx = src.GetTriangles(s);
                var map = new Dictionary<long, int>();
                var remap = new Dictionary<int, int>();
                var list = new List<int>();
                for (int i = 0; i + 2 < idx.Length; i += 3)
                {
                    int a = Cl(idx[i], verts, min, cell, map, remap, outV, outN, outUV, count, nrm, uv, hasN, hasUV);
                    int bb = Cl(idx[i + 1], verts, min, cell, map, remap, outV, outN, outUV, count, nrm, uv, hasN, hasUV);
                    int c = Cl(idx[i + 2], verts, min, cell, map, remap, outV, outN, outUV, count, nrm, uv, hasN, hasUV);
                    if (a == bb || bb == c || a == c) continue;
                    list.Add(a); list.Add(bb); list.Add(c);
                }
                tris.Add(list.ToArray());
            }
            if (outV.Count == 0) return null;
            for (int i = 0; i < outV.Count; i++)
            {
                float k = 1f / Mathf.Max(1, count[i]);
                outV[i] *= k;
                if (hasN) outN[i] = outN[i].normalized;
                if (hasUV) outUV[i] *= k;
            }
            var m = new Mesh { name = src.name + "_lod", hideFlags = HideFlags.DontSave };
            if (outV.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(outV);
            if (hasN) m.SetNormals(outN);
            if (hasUV) m.SetUVs(0, outUV);
            m.subMeshCount = tris.Count;
            for (int s = 0; s < tris.Count; s++) m.SetTriangles(tris[s], s, false);
            m.RecalculateBounds();
            return m;
        }

        static int Cl(int vi, Vector3[] verts, Vector3 min, float cell, Dictionary<long, int> map, Dictionary<int, int> remap,
            List<Vector3> outV, List<Vector3> outN, List<Vector2> outUV, List<int> count, Vector3[] nrm, Vector2[] uv, bool hasN, bool hasUV)
        {
            int r;
            if (remap.TryGetValue(vi, out r)) return r;
            var p = verts[vi];
            long x = (long)((p.x - min.x) / cell), y = (long)((p.y - min.y) / cell), z = (long)((p.z - min.z) / cell);
            long key = (x & 0x1FFFFF) | ((y & 0x1FFFFF) << 21) | ((z & 0x1FFFFF) << 42);
            if (!map.TryGetValue(key, out r))
            {
                r = outV.Count; map[key] = r;
                outV.Add(Vector3.zero); outN.Add(Vector3.zero); outUV.Add(Vector2.zero); count.Add(0);
            }
            outV[r] += p; count[r]++;
            if (hasN) outN[r] += nrm[vi];
            if (hasUV) outUV[r] += uv[vi];
            remap[vi] = r;
            return r;
        }

        /// <summary>Collision shape (Unity space) from a simplified mesh.</summary>
        public static ColMesh ToCol(Mesh m)
        {
            var v = m.vertices; var t = new List<int>();
            for (int s = 0; s < m.subMeshCount; s++) if (m.GetTopology(s) == MeshTopology.Triangles) t.AddRange(m.GetTriangles(s));
            return new ColMesh { V = v, T = t.ToArray() };
        }
    }
}
