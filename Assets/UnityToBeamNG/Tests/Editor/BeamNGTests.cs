using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;
using UnityEngine;

namespace UnityToBeamNG.Tests
{
    public class BeamNGTests
    {
        [Test]
        public void Converter_SwapsYZ()
        {
            Assert.AreEqual(new Vector3(1, 3, 2), BeamNGCoordinateConverter.Position(new Vector3(1, 2, 3)));
        }

        [Test]
        public void Converter_Identity_GivesIdentityMatrix()
        {
            var m = BeamNGCoordinateConverter.RotationMatrix(BeamNGCoordinateConverter.Rotation(Quaternion.identity));
            CollectionAssert.AreEqual(new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }, m);
        }

        [Test]
        public void Converter_UnityYaw90_UsesTransposedLayout()
        {
            // Unity: facing +Z (north); after a 90 degree yaw it faces +X (east).
            // Standard column-vector matrix would be [0,1,0,-1,0,0,0,0,1]; BeamNG layout is its transpose.
            var m = BeamNGCoordinateConverter.RotationMatrix(BeamNGCoordinateConverter.Rotation(Quaternion.Euler(0, 90, 0)));
            CollectionAssert.AreEqual(new float[] { 0, -1, 0, 1, 0, 0, 0, 0, 1 }, RoundAll(m));
        }

        [Test]
        public void Converter_UnityPitch_IsTransposedToo()
        {
            // 11 degree pitch around Unity X: the standard layout has +sin at [1][2]; the transposed layout has it at [2][1].
            var m = BeamNGCoordinateConverter.RotationMatrix(BeamNGCoordinateConverter.Rotation(Quaternion.Euler(11, 0, 0)));
            Assert.AreEqual(Mathf.Sin(11 * Mathf.Deg2Rad), m[7], 1e-4f);
            Assert.AreEqual(-Mathf.Sin(11 * Mathf.Deg2Rad), m[5], 1e-4f);
        }

        static float[] RoundAll(float[] v)
        {
            var o = new float[v.Length];
            for (int i = 0; i < v.Length; i++) o[i] = Mathf.Round(v[i] * 1000f) / 1000f + 0f;
            return o;
        }

        [Test]
        public void Converter_MatrixIsOrthonormalForArbitraryRotation()
        {
            var m = BeamNGCoordinateConverter.RotationMatrix(BeamNGCoordinateConverter.Rotation(Quaternion.Euler(10, 20, 30)));
            float det = m[0] * (m[4] * m[8] - m[5] * m[7]) - m[1] * (m[3] * m[8] - m[5] * m[6]) + m[2] * (m[3] * m[7] - m[4] * m[6]);
            Assert.AreEqual(1f, det, 1e-4f);
        }

        [Test]
        public void LevelName_Validation()
        {
            Assert.IsTrue(BeamNGEditorUtilities.IsValidLevelName("my_map_1"));
            Assert.IsFalse(BeamNGEditorUtilities.IsValidLevelName("My Map"));
            Assert.IsFalse(BeamNGEditorUtilities.IsValidLevelName(""));
        }

        [Test]
        public void Json_ObjectIsSingleLine()
        {
            var l = new JsonObj().Str("class", "SpawnSphere").Arr("position", 1, 2.5f, 3).ToString();
            Assert.AreEqual("{\"class\":\"SpawnSphere\",\"position\":[1,2.5,3]}", l);
            Assert.IsFalse(l.Contains("\n"));
        }

        [Test]
        public void Dae_ForCube_HasCorrectCounts()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;
                var r = new BeamNGExportReport();
                var dae = BeamNGMeshExporter.BuildDae(mesh, "cube", new[] { "mat" }, r);
                StringAssert.Contains("<triangles material=\"mat\" count=\"12\">", dae);
                StringAssert.Contains("count=\"72\"", dae);
                Assert.IsFalse(r.HasErrors);
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---- Full run: terrain + cube + spawn -> ZIP ----
        [Test]
        public void Pipeline_ProducesZipWithExpectedStructure()
        {
            var dir = Path.Combine(Path.GetTempPath(), "bng_test_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var td = new TerrainData { heightmapResolution = 129, size = new Vector3(200, 40, 200) };
            var terrain = Terrain.CreateTerrainGameObject(td);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.position = new Vector3(50, 5, 50);
            var spawn = new GameObject("spawn");
            spawn.AddComponent<BeamNGSpawnPoint>().spawnName = "spawns_default";
            spawn.transform.position = new Vector3(100, 2, 100);
            try
            {
                var s = new BeamNGExportSettings
                {
                    persist = false,
                    levelName = "test_level",
                    mapName = "Test",
                    exportDirectory = dir,
                    terrainResolution = 128
                };
                var r = BeamNGExportPipeline.Export(s);
                Assert.IsFalse(r.HasErrors, r.ToText());
                Assert.IsTrue(File.Exists(BeamNGExportPipeline.LastZipPath), "ZIP fehlt");

                var names = new List<string>();
                using (var za = new ZipArchive(File.OpenRead(BeamNGExportPipeline.LastZipPath), ZipArchiveMode.Read))
                    foreach (var e in za.Entries) names.Add(e.FullName);

                CollectionAssert.Contains(names, "levels/test_level/info.json");
                CollectionAssert.Contains(names, "levels/test_level/theTerrain.ter");
                CollectionAssert.Contains(names, "levels/test_level/main/items.level.json");
                CollectionAssert.Contains(names, "levels/test_level/main/MissionGroup/Gameplay/items.level.json");
                CollectionAssert.Contains(names, "levels/test_level/main.materials.json");
                foreach (var n in names) Assert.IsFalse(n.Contains("\\"), n);

                // .ter size: 1 + 4 + R*R*2 + R*R + 4 + names
                var terEntry = names.Find(n => n.EndsWith("theTerrain.ter"));
                using (var za = new ZipArchive(File.OpenRead(BeamNGExportPipeline.LastZipPath), ZipArchiveMode.Read))
                    Assert.GreaterOrEqual(za.GetEntry(terEntry).Length, 1 + 4 + 128L * 128 * 3 + 4);
            }
            finally
            {
                Object.DestroyImmediate(terrain);
                Object.DestroyImmediate(td);
                Object.DestroyImmediate(cube);
                Object.DestroyImmediate(spawn);
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Test]
        public void Forest_Line_HasDocumentedFields()
        {
            var line = BeamNGVegetationExporter.ForestLine("tree_oak", new Vector3(1.5f, 2f, 3f), new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }, 0.85f);
            Assert.AreEqual("{\"type\":\"tree_oak\",\"pos\":[1.5,2,3],\"rotationMatrix\":[1,0,0,0,1,0,0,0,1],\"scale\":0.85}", line);
        }

        [Test]
        public void Forest_Collision_ShapesPointOutward()
        {
            foreach (var col in new[] { BeamNGVegetationExporter.MakePrism(new Vector2(0, 0), 0.4f, 0f, 5f, 8), BeamNGVegetationExporter.MakeBox(new Bounds(Vector3.zero, Vector3.one * 2f)) })
            {
                var center = Vector3.zero;
                foreach (var v in col.V) center += v;
                center /= col.V.Length;
                for (int i = 0; i < col.T.Length; i += 3)
                {
                    var a = col.V[col.T[i]]; var b = col.V[col.T[i + 1]]; var c = col.V[col.T[i + 2]];
                    Assert.Greater(Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - center), 0f);
                }
            }
        }

        [Test]
        public void Foliage_Detection_IgnoresBark()
        {
            var shader = Shader.Find("Standard");
            var leaf = new Material(shader) { name = "Oak_Leaves" };
            var bark = new Material(shader) { name = "Oak_Bark" };
            try
            {
                Assert.IsTrue(BeamNGVegetationExporter.LooksLikeFoliage(leaf));
                Assert.IsFalse(BeamNGVegetationExporter.LooksLikeFoliage(bark));
            }
            finally { Object.DestroyImmediate(leaf); Object.DestroyImmediate(bark); }
        }

        [Test]
        public void Pipeline_WithoutSpawn_FailsWithError()
        {
            var dir = Path.Combine(Path.GetTempPath(), "bng_test_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var s = new BeamNGExportSettings { persist = false, levelName = "no_spawn", exportDirectory = dir };
                var r = BeamNGExportPipeline.Export(s);
                Assert.IsTrue(r.HasErrors);
                Assert.AreEqual(0, Directory.GetFiles(dir, "*.zip", SearchOption.AllDirectories).Length);
            }
            finally { Object.DestroyImmediate(cube); try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
