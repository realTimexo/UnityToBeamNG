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
                    if (name == "items.level.json")
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
                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(dae), "<material id=\"([^\"]+)\""))
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
            var rxFile = new System.Text.RegularExpressions.Regex("\"(?:shapeName|terrainFile)\":\"([^\"]*)\"");

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
