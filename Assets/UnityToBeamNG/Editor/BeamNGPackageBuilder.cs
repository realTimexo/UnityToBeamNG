using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace UnityToBeamNG
{
    [Serializable] class JsonProbe { }

    public static class BeamNGPackageBuilder
    {
        /// <summary>Checks file structure and syntax of the generated level files.</summary>
        public static void ValidateStructure(BeamNGExportContext c)
        {
            var r = c.Report;
            int missing = 0, badJson = 0;
            foreach (var rel in c.Required)
                if (!File.Exists(Path.Combine(c.LevelDir, rel.Replace('/', Path.DirectorySeparatorChar))))
                { missing++; r.Add(ReportStatus.ERROR, "Structure", $"Required file missing: {rel}"); }

            foreach (var f in Directory.GetFiles(c.LevelDir, "*.json", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(f);
                var rel = f.Substring(c.LevelDir.Length + 1).Replace('\\', '/');
                try
                {
                    if (name.EndsWith(".forest4.json"))
                    {
                        // line-delimited JSON; can be very large: parse the first lines, check braces on all
                        int ln = 0;
                        foreach (var line in File.ReadLines(f))
                        {
                            ln++;
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            if (ln <= 20) JsonUtility.FromJson<JsonProbe>(line);
                            if (!line.StartsWith("{") || !line.EndsWith("}")) throw new Exception("line " + ln + " is not a JSON object");
                        }
                    }
                    else if (name == "items.level.json")
                    {
                        int ln = 0;
                        foreach (var line in File.ReadAllLines(f))
                        {
                            ln++;
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            JsonUtility.FromJson<JsonProbe>(line);
                            if (!line.TrimStart().StartsWith("{") || !line.TrimEnd().EndsWith("}")) throw new Exception("line " + ln + " is not a JSON object");
                        }
                    }
                    else JsonUtility.FromJson<JsonProbe>(File.ReadAllText(f));
                }
                catch (Exception e) { badJson++; r.Add(ReportStatus.ERROR, "Structure", $"Invalid JSON in {rel}: {e.Message}"); }
            }

            // Every material name used in a DAE must be defined in main.materials.json
            var matFile = Path.Combine(c.LevelDir, "art", "shapes", "main.materials.json");
            if (Directory.Exists(Path.Combine(c.LevelDir, "art", "shapes")))
            {
                var matText = File.Exists(matFile) ? File.ReadAllText(matFile) : "";
                foreach (var dae in Directory.GetFiles(Path.Combine(c.LevelDir, "art", "shapes"), "*.dae"))
                {
                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(dae), "<material id=\"[^\"]+\" name=\"([^\"]+)\""))
                        if (!matText.Contains("\"" + m.Groups[1].Value + "\":"))
                        { missing++; r.Add(ReportStatus.ERROR, "Structure", $"Material '{m.Groups[1].Value}' from {Path.GetFileName(dae)} is missing in main.materials.json."); }
                }
            }
            // --- References between files ---
            int refErrors = CheckReferences(c);
            missing += refErrors;

            if (missing == 0 && badJson == 0)
                r.Add(ReportStatus.PASS, "Structure", $"File structure validated: {c.Required.Count} required files present, JSON syntax ok (level 2 of 5; not loaded in game).");
        }


        static int PngWidth(string path)
        {
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    var b = new byte[24];
                    if (fs.Read(b, 0, 24) < 24) return -1;
                    return (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
                }
            }
            catch { return -1; }
        }

        static int CheckReferences(BeamNGExportContext c)
        {
            var r = c.Report;
            int errors = 0;
            var names = new HashSet<string>();
            var groups = new HashSet<string> { "MissionGroup" };
            var parents = new List<string>();
            var rxName = new System.Text.RegularExpressions.Regex("\"name\":\"([^\"]*)\"");
            var rxClass = new System.Text.RegularExpressions.Regex("\"class\":\"([^\"]*)\"");
            var rxParent = new System.Text.RegularExpressions.Regex("\"__parent\":\"([^\"]*)\"");
            var rxFile = new System.Text.RegularExpressions.Regex("\"(?:shapeName|terrainFile|minimapImage)\":\"([^\"]*)\"");

            foreach (var f in Directory.GetFiles(Path.Combine(c.LevelDir, "main"), "items.level.json", SearchOption.AllDirectories))
            {
                foreach (var line in File.ReadAllLines(f))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var nm = rxName.Match(line); var cl = rxClass.Match(line);
                    if (!nm.Success || !cl.Success) { errors++; r.Add(ReportStatus.ERROR, "Structure", "Object line without name/class in " + f.Substring(c.LevelDir.Length + 1)); continue; }
                    if (!names.Add(nm.Groups[1].Value)) { errors++; r.Add(ReportStatus.ERROR, "Structure", "Duplicate object name: " + nm.Groups[1].Value); }
                    if (cl.Groups[1].Value == "SimGroup") groups.Add(nm.Groups[1].Value);
                    var pm = rxParent.Match(line); if (pm.Success) parents.Add(pm.Groups[1].Value);
                    var fm = rxFile.Match(line);
                    if (fm.Success)
                    {
                        var rel = fm.Groups[1].Value.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                        if (!File.Exists(Path.Combine(c.Root, rel))) { errors++; r.Add(ReportStatus.ERROR, "Structure", "Referenced file missing: " + fm.Groups[1].Value); }
                    }
                }
            }
            foreach (var p in parents)
                if (!groups.Contains(p)) { errors++; r.Add(ReportStatus.ERROR, "Structure", "__parent points to an unknown group: " + p); }

            var infoPath = Path.Combine(c.LevelDir, "info.json");
            if (File.Exists(infoPath))
            {
                var info = File.ReadAllText(infoPath);
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(info, "\"(?:objectname|defaultSpawnPointName)\":\"([^\"]*)\""))
                    if (!names.Contains(m.Groups[1].Value)) { errors++; r.Add(ReportStatus.ERROR, "Structure", "info.json refers to a missing spawn object: " + m.Groups[1].Value); }
            }

            foreach (var f in Directory.GetFiles(c.LevelDir, "*.materials.json", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(f);
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, "\"(/levels/[^\"]+\\.png)\""))
                    if (!File.Exists(Path.Combine(c.Root, m.Groups[1].Value.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))))
                    { errors++; r.Add(ReportStatus.ERROR, "Structure", "Texture missing: " + m.Groups[1].Value); }
            }

            // --- PBR terrain: the TerrainBlock must refer to a defined TerrainMaterialTextureSet and every texture must match its slot size
            //     (BeamNG crashed with a D3D12 error when the set was missing) ---
            var terrItems = Path.Combine(c.LevelDir, "main", "MissionGroup", "Terrain", "items.level.json");
            if (File.Exists(terrItems))
            {
                var sm0 = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(terrItems), "\"materialTextureSet\":\"([^\"]*)\"");
                if (sm0.Success)
                {
                    var terMat = Path.Combine(c.LevelDir, "art", "terrains", "main.materials.json");
                    var tm = File.Exists(terMat) ? File.ReadAllText(terMat) : "";
                    var setM = System.Text.RegularExpressions.Regex.Match(tm, "\"" + System.Text.RegularExpressions.Regex.Escape(sm0.Groups[1].Value) + "\":\\{[^{}]*\"class\":\"TerrainMaterialTextureSet\"[^{}]*\\}");
                    if (!setM.Success)
                    { errors++; r.Add(ReportStatus.ERROR, "Structure", "TerrainBlock uses materialTextureSet '" + sm0.Groups[1].Value + "' but it is not defined in art/terrains/main.materials.json (BeamNG would crash)."); }
                    else
                    {
                        var slot = new Dictionary<string, int>();
                        foreach (var kind in new[] { "base", "macro", "detail" })
                        {
                            var km = System.Text.RegularExpressions.Regex.Match(setM.Value, "\"" + kind + "TexSize\":\\[(\\d+),");
                            slot[kind] = km.Success ? int.Parse(km.Groups[1].Value) : 0;
                        }
                        foreach (System.Text.RegularExpressions.Match t in System.Text.RegularExpressions.Regex.Matches(tm, "\"[A-Za-z]+?(Base|Macro|Detail)Tex\":\"(/levels/[^\"]+\\.png)\""))
                        {
                            var file = Path.Combine(c.Root, t.Groups[2].Value.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                            int w = PngWidth(file);
                            int want = slot[t.Groups[1].Value.ToLowerInvariant()];
                            if (w != want) { errors++; r.Add(ReportStatus.ERROR, "Structure", $"Terrain texture {t.Groups[2].Value} is {w}px wide but its slot is {want}px (BeamNG requires exact sizes)."); }
                        }
                    }
                }
            }

            // --- forest: every item type needs an existing shape, every placement a defined type ---
            var managed = Path.Combine(c.LevelDir, "art", "forest", "managedItemData.json");
            var forestDir = Path.Combine(c.LevelDir, "forest");
            if (File.Exists(managed))
            {
                var mtext = File.ReadAllText(managed);
                var defined = new HashSet<string>();
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(mtext, "\"internalName\":\"([^\"]*)\""))
                    defined.Add(m.Groups[1].Value);
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(mtext, "\"shapeFile\":\"([^\"]*)\""))
                    if (!File.Exists(Path.Combine(c.Root, m.Groups[1].Value.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))))
                    { errors++; r.Add(ReportStatus.ERROR, "Structure", "Forest shape missing: " + m.Groups[1].Value); }
                if (Directory.Exists(forestDir))
                {
                    var rxType = new System.Text.RegularExpressions.Regex("\"type\":\"([^\"]*)\"");
                    foreach (var f in Directory.GetFiles(forestDir, "*.forest4.json"))
                    {
                        var seen = new HashSet<string>();
                        foreach (var line in File.ReadLines(f))
                        {
                            var tm = rxType.Match(line);
                            if (tm.Success) seen.Add(tm.Groups[1].Value);
                        }
                        foreach (var t in seen)
                            if (!defined.Contains(t)) { errors++; r.Add(ReportStatus.ERROR, "Structure", $"{Path.GetFileName(f)}: item type '{t}' is not defined in managedItemData.json."); }
                    }
                }
            }
            else if (Directory.Exists(forestDir) && Directory.GetFiles(forestDir, "*.forest4.json").Length > 0)
            { errors++; r.Add(ReportStatus.ERROR, "Structure", "forest/*.forest4.json exists but art/forest/managedItemData.json is missing."); }

            if (File.Exists(infoPath))
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(infoPath), "\"file\":\"([^\"]*)\""))
                    if (!File.Exists(Path.Combine(c.LevelDir, m.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar))))
                    { errors++; r.Add(ReportStatus.ERROR, "Structure", "info.json minimap file missing: " + m.Groups[1].Value); }

            // no absolute local paths
            foreach (var f in Directory.GetFiles(c.LevelDir, "*.json", SearchOption.AllDirectories))
                if (System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(f), @"""[A-Za-z]:(\\\\|/)"))
                { errors++; r.Add(ReportStatus.ERROR, "Structure", "Absolute local path in " + f.Substring(c.LevelDir.Length + 1)); }
            return errors;
        }

        /// <summary>Zips Root/levels to zipPath. Entries use '/', root = levels/.</summary>
        public static void CreateZip(string root, string zipPath)
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            var fileList = new List<string>();
            foreach (var top in new[] { "levels", "scripts", "lua" })
            {
                var dir = Path.Combine(root, top);
                if (Directory.Exists(dir)) fileList.AddRange(Directory.GetFiles(dir, "*", SearchOption.AllDirectories));
            }
            var files = fileList.ToArray();
            Array.Sort(files, StringComparer.Ordinal);
            using (var zs = new FileStream(zipPath, FileMode.Create))
            using (var za = new ZipArchive(zs, ZipArchiveMode.Create))
            {
                foreach (var f in files)
                {
                    var entry = f.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
                    var ze = za.CreateEntry(entry, System.IO.Compression.CompressionLevel.Optimal);
                    using (var es = ze.Open())
                    using (var fsIn = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read))
                        fsIn.CopyTo(es);
                }
            }
        }

        public static bool VerifyZip(string zipPath, BeamNGExportContext c)
        {
            using (var za = ZipFile_Open(zipPath))
            {
                var names = new HashSet<string>();
                foreach (var e in za.Entries)
                {
                    if (e.FullName.Contains("\\")) { c.Report.Add(ReportStatus.ERROR, "ZIP", "Entry contains a backslash: " + e.FullName); return false; }
                    names.Add(e.FullName);
                }
                foreach (var rel in c.Required)
                    if (!names.Contains("levels/" + c.Settings.levelName + "/" + rel))
                    { c.Report.Add(ReportStatus.ERROR, "ZIP", "File missing in ZIP: " + rel); return false; }
            }
            return true;
        }

        static ZipArchive ZipFile_Open(string path) { return new ZipArchive(new FileStream(path, FileMode.Open, FileAccess.Read), ZipArchiveMode.Read); }
    }
}
