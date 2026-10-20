using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityToBeamNG
{
    [Serializable]
    public class ManifestObject
    {
        public string id;
        public string unityName;
        public string hierarchyPath;
        public string prefabSource;
        public string beamNGName;
        public string meshFile;
        public string[] materials;
        public string collision;
        public float[] position;
        public float[] rotationMatrix;
        public float[] scale;
    }

    [Serializable]
    public class ExportManifest
    {
        public string tool = "Unity to BeamNG 0.2.1";
        public string unityVersion;
        public string levelName;
        public string mapName;
        public string author;
        public string coordinateConvention = "BeamNG(x,y,z) = Unity(x,z,y); triangle winding reversed; rotationMatrix transposed.";
        public List<ManifestObject> objects = new List<ManifestObject>();
    }

    public static class BeamNGExportPipeline
    {
        public static BeamNGExportReport LastReport;
        public static string LastExportPath;
        public static string LastZipPath;

        public static BeamNGExportReport Validate(BeamNGExportSettings s) { return Run(s, true); }
        public static BeamNGExportReport Export(BeamNGExportSettings s) { return Run(s, false); }

        static BeamNGExportReport Run(BeamNGExportSettings s, bool validateOnly)
        {
            var r = new BeamNGExportReport();
            LastReport = r;
            string tmp = null;
            var ctx = new BeamNGExportContext
            {
                Settings = s,
                Report = r,
                Progress = (step, p) => EditorUtility.DisplayCancelableProgressBar("Unity to BeamNG", step, p)
            };
            try
            {
                if (ctx.Cancelled("Checking settings", 0.02f)) return Abort(r, tmp);
                BeamNGValidator.ValidateSettings(s, r, !validateOnly);
                if (r.HasErrors) return Finish(s, r, "Error (settings)");

                if (ctx.Cancelled("Scanning scene", 0.05f)) return Abort(r, tmp);
                var scan = BeamNGSceneScanner.Scan(s, r);
                BeamNGValidator.ValidateScan(scan, s, r);
                if (validateOnly || r.HasErrors)
                    return Finish(s, r, r.HasErrors ? "Validation: errors" : "Validation ok (nothing exported)");

                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var finalDir = Path.Combine(s.exportDirectory, s.levelName + "_" + stamp);
                tmp = Path.Combine(s.exportDirectory, ".tmp_" + Guid.NewGuid().ToString("N"));
                ctx.Root = tmp;
                ctx.LevelDir = Path.Combine(tmp, "levels", s.levelName);
                ctx.ExtrasDir = Path.Combine(tmp, "extras");
                Directory.CreateDirectory(ctx.LevelDir);
                Directory.CreateDirectory(ctx.ExtrasDir);

                var reg = new BeamNGMaterialRegistry();
                var tex = new BeamNGTextureExporter(ctx);
                var manifest = new ExportManifest { unityVersion = Application.unityVersion, levelName = s.levelName, mapName = s.mapName, author = s.author };

                TerrainResult terrain = null;
                if (s.exportTerrain && scan.Terrains.Count > 0)
                {
                    terrain = BeamNGTerrainExporter.Export(ctx, scan.Terrains, tex);
                    if (ctx.Aborted) return Abort(r, tmp);
                    r.Exported += scan.Terrains.Count;
                }

                var staticLines = new List<string>();
                if (s.exportMeshes && scan.Meshes.Count > 0)
                {
                    staticLines = BeamNGMeshExporter.ExportAll(ctx, scan, reg, manifest);
                    if (ctx.Aborted || staticLines == null) return Abort(r, tmp);
                }
                r.Skipped = scan.SkippedLod + scan.SkippedExcluded + scan.InvalidMeshes;

                if (ctx.Cancelled("Converting materials", 0.82f)) return Abort(r, tmp);
                BeamNGMaterialExporter.Export(ctx, reg, tex);

                if (ctx.Cancelled("Writing level files", 0.86f)) return Abort(r, tmp);
                var spawns = BeamNGLevelBuilder.ResolveSpawns(ctx, scan);
                BeamNGLevelBuilder.Build(ctx, scan, terrain, staticLines, spawns);

                File.WriteAllText(Path.Combine(ctx.ExtrasDir, "export_manifest.json"), JsonUtility.ToJson(manifest, true), new UTF8Encoding(false));

                if (ctx.Cancelled("Validating structure", 0.9f)) return Abort(r, tmp);
                BeamNGPackageBuilder.ValidateStructure(ctx);
                if (r.HasErrors) { CleanDir(tmp); return Finish(s, r, "Error (validation)"); }

                if (ctx.Cancelled("Creating ZIP", 0.94f)) return Abort(r, tmp);
                var zipName = s.levelName + ".zip";
                var zipTmp = Path.Combine(tmp, zipName);
                BeamNGPackageBuilder.CreateZip(tmp, zipTmp);
                if (!BeamNGPackageBuilder.VerifyZip(zipTmp, ctx)) { CleanDir(tmp); return Finish(s, r, "Error (ZIP)"); }
                r.Add(ReportStatus.PASS, "ZIP", $"{zipName} created and contents checked (root: levels/{s.levelName}/).");
                r.Add(ReportStatus.MANUAL, "Test", "Steps 3-5 are still open: load the map, drive it, check the spawn. Nothing has been verified in BeamNG by this tool.", "Put the ZIP into the BeamNG mods folder, start the game, check the log.");

                r.Save(Path.Combine(tmp, "export_report.txt"));
                Directory.Move(tmp, finalDir);
                tmp = null;
                LastExportPath = finalDir;
                LastZipPath = Path.Combine(finalDir, zipName);

                if (s.copyZipToModsFolder)
                {
                    var dest = Path.Combine(s.beamNGModsFolder, zipName);
                    bool ok = !File.Exists(dest) || EditorUtility.DisplayDialog("Overwrite ZIP?", dest + " already exists. Overwrite?", "Overwrite", "Cancel");
                    if (ok) { File.Copy(LastZipPath, dest, true); r.Add(ReportStatus.PASS, "ZIP", "ZIP copied to mods folder: " + dest); }
                    else r.Add(ReportStatus.WARNING, "ZIP", "Copying to the mods folder was cancelled.");
                }
                return Finish(s, r, r.HasWarningsOrManual() ? "ZIP created, with limitations (not tested in game)" : "ZIP created (not tested in game)");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                r.Add(ReportStatus.ERROR, "Pipeline", "Unexpected error: " + e.Message, "See the Unity console for details.");
                CleanDir(tmp);
                return Finish(s, r, "Error (exception)");
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        static BeamNGExportReport Abort(BeamNGExportReport r, string tmp)
        {
            CleanDir(tmp);
            r.Add(ReportStatus.ERROR, "Pipeline", "Export cancelled by the user; nothing was written.");
            return r;
        }

        static BeamNGExportReport Finish(BeamNGExportSettings s, BeamNGExportReport r, string status)
        {
            s.lastExportTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            s.lastExportStatus = status;
            s.Save();
            return r;
        }

        static void CleanDir(string dir)
        {
            try { if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }
}
