using System;
using System.Collections.Generic;
using System.IO;

namespace UnityToBeamNG
{
    /// <summary>Shared state of one export run.</summary>
    public class BeamNGExportContext
    {
        public BeamNGExportSettings Settings;
        public BeamNGExportReport Report;
        public string Root;        // temporary root folder (contains levels/ and extras/)
        public string LevelDir;    // Root/levels/<levelName>
        public string ExtrasDir;   // Root/extras (NOT zipped)
        public Func<string, float, bool> Progress;
        public bool Aborted;
        public readonly List<string> Required = new List<string>(); // relative to LevelDir

        public bool Cancelled(string step, float p)
        {
            if (Progress != null && Progress(step, p)) { Aborted = true; return true; }
            return false;
        }

        /// <summary>Virtual BeamNG path, e.g. /levels/name/art/shapes/x.dae</summary>
        public string VPath(string rel) { return "/levels/" + Settings.levelName + "/" + rel.Replace('\\', '/'); }

        public string Abs(string rel)
        {
            var p = Path.Combine(LevelDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            return p;
        }

        public void Require(string rel) { if (!Required.Contains(rel)) Required.Add(rel); }
    }
}
