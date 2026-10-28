using System.IO;

namespace UnityToBeamNG
{
    public static class BeamNGValidator
    {
        public static void ValidateSettings(BeamNGExportSettings s, BeamNGExportReport r, bool needExportDir)
        {
            if (!BeamNGEditorUtilities.IsValidLevelName(s.levelName))
                r.Add(ReportStatus.ERROR, "Settings", "Internal level name is invalid.", "Use only lowercase a-z, digits and underscore.");
            if (!BeamNGEditorUtilities.IsPowerOfTwo(s.terrainResolution) || s.terrainResolution < 64 || s.terrainResolution > 8192)
                r.Add(ReportStatus.ERROR, "Settings", $"Terrain resolution {s.terrainResolution} is invalid.", "Use a power of two between 64 and 8192 (e.g. 2048).");
            if (s.maxTextureSize < 16 || s.maxTextureSize > 8192)
                r.Add(ReportStatus.ERROR, "Settings", "Max texture size is invalid.", "Use a value between 16 and 8192.");
            if (needExportDir)
            {
                if (string.IsNullOrEmpty(s.exportDirectory))
                    r.Add(ReportStatus.ERROR, "Settings", "No export folder selected.", "Pick an export folder in the 'Map' tab.");
                else if (!Directory.Exists(s.exportDirectory))
                    r.Add(ReportStatus.ERROR, "Settings", "Export folder does not exist.", "Create the folder or pick another one.");
            }
            if (s.copyZipToModsFolder && !Directory.Exists(s.beamNGModsFolder))
                r.Add(ReportStatus.ERROR, "Settings", "BeamNG mods folder does not exist.", "Pick the folder or disable 'Copy ZIP to mods folder'.");
            if (s.detailDensity < 0.01f || s.detailDensity > 1f)
                r.Add(ReportStatus.ERROR, "Settings", "Detail density is invalid.", "Use a value between 0.01 and 1.");
            if (s.maxDetailInstances < 1000 || s.maxDetailInstances > 5000000)
                r.Add(ReportStatus.ERROR, "Settings", "Max detail instances is invalid.", "Use a value between 1,000 and 5,000,000.");
            if (!s.exportTerrain && !s.exportMeshes)
                r.Add(ReportStatus.ERROR, "Settings", "No module enabled.", "Enable at least Terrain or Meshes.");
        }

        public static void ValidateScan(ScanResult scan, BeamNGExportSettings s, BeamNGExportReport r)
        {
            r.Detected = scan.Detected;
            if (scan.Detected == 0)
                r.Add(ReportStatus.ERROR, "Scene", "No exportable objects found.", "Add a Terrain or a MeshRenderer with a mesh to the active scene.");
            if (scan.Spawns.Count == 0)
                r.Add(ReportStatus.ERROR, "Spawn", "No spawn point defined.", "Create an empty GameObject and pick it in the 'Spawn' tab (or add the 'BeamNG/Spawn Point' component).");
            if (scan.MissingMaterials > 0)
                r.Add(ReportStatus.WARNING, "Material", $"{scan.MissingMaterials} empty material slot(s); they get the grey 'missing_material'.", "Assign materials.");
            foreach (var n in scan.EmptySlotObjects)
                r.Add(ReportStatus.WARNING, "Material", "Empty material slot on: " + n, "Select this object in the Hierarchy and assign a material to the empty slot (or remove the extra slot).");
            foreach (var mo in scan.MissingMaterialObjects)
                r.Add(ReportStatus.WARNING, "Material", "Empty material slot: " + mo, "Select this object in the Hierarchy and assign a material (or remove the extra slot).");
            foreach (var kv in scan.Unsupported)
                r.Add(ReportStatus.WARNING, "Unsupported", $"{kv.Value}x {kv.Key} will not be exported.", "");
            if (scan.Triangles > BeamNGSceneScanner.TriangleWarnThreshold)
                r.Add(ReportStatus.WARNING, "Performance", $"{scan.Triangles:N0} triangles; 'Visible Mesh' collision on all of them can be heavy for BeamNG.", "Use BeamNGCollisionSettings (None/Bounds) on decoration. Nothing is reduced automatically.");
            if (scan.Terrains.Count == 0 && s.exportTerrain)
                r.Add(ReportStatus.WARNING, "Terrain", "No terrain in the scene: the map has no TerrainBlock; the drivable surface must be made of meshes.", "");
            if (scan.SkinnedBaked > 0)
                r.Add(ReportStatus.PASS, "Mesh", $"{scan.SkinnedBaked} SkinnedMeshRenderer(s) baked in their current pose and exported as static meshes.");
            foreach (var n in scan.NotExported)
                r.Add(ReportStatus.WARNING, "Scene", "Not exported: " + n, "");
            r.Add(ReportStatus.MANUAL, "Features", "Not included: normal maps, yield signs, pedestrian crossings.", "See README.");
        }
    }
}
