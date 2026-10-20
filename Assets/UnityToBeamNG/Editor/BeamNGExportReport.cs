using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UnityToBeamNG
{
    public enum ReportStatus { PASS, WARNING, ERROR, MANUAL }

    public class ReportEntry
    {
        public ReportStatus Status;
        public string Category;
        public string Message;
        public string Hint;
    }

    public class BeamNGExportReport
    {
        public readonly List<ReportEntry> Entries = new List<ReportEntry>();
        public int Detected, Exported, Skipped;

        public void Add(ReportStatus s, string cat, string msg, string hint = "")
        {
            Entries.Add(new ReportEntry { Status = s, Category = cat, Message = msg, Hint = hint });
        }

        public bool HasErrors
        {
            get { foreach (var e in Entries) if (e.Status == ReportStatus.ERROR) return true; return false; }
        }

        public bool HasWarningsOrManual()
        {
            foreach (var e in Entries) if (e.Status == ReportStatus.WARNING || e.Status == ReportStatus.MANUAL) return true;
            return false;
        }

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Unity to BeamNG - export report");
            sb.AppendLine($"Detected: {Detected} | Exported: {Exported} | Skipped: {Skipped}");
            sb.AppendLine("Note: files created != tested in game. Always check the BeamNG log after loading the map.");
            sb.AppendLine();
            foreach (var e in Entries)
            {
                sb.AppendLine($"[{e.Status}] {e.Category}: {e.Message}");
                if (!string.IsNullOrEmpty(e.Hint)) sb.AppendLine($"    -> {e.Hint}");
            }
            return sb.ToString();
        }

        public void Save(string path) { File.WriteAllText(path, ToText(), new UTF8Encoding(false)); }
    }
}
