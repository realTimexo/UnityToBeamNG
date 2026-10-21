using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace UnityToBeamNG
{
    public class BeamNGMaterialRegistry
    {
        public const string Missing = "missing_material";
        readonly Dictionary<Material, string> names = new Dictionary<Material, string>();
        readonly HashSet<string> used = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        public readonly List<Material> Materials = new List<Material>();
        public bool UsesMissing;

        public string NameOf(Material m)
        {
            if (m == null) { UsesMissing = true; return Missing; }
            string n;
            if (names.TryGetValue(m, out n)) return n;
            var b = BeamNGEditorUtilities.SafeId(m.name);
            if (b == Missing) b = "m_" + b;
            n = b; int i = 2;
            while (used.Contains(n)) n = b + "_" + (i++);
            used.Add(n); names[m] = n; Materials.Add(m);
            return n;
        }
    }

    /// <summary>Exports textures as PNG (via RenderTexture, works with compressed textures too).</summary>
    public class BeamNGTextureExporter
    {
        readonly BeamNGExportContext c;
        readonly Dictionary<string, string> cache = new Dictionary<string, string>();
        readonly HashSet<string> files = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        public BeamNGTextureExporter(BeamNGExportContext ctx) { c = ctx; }

        string UniqueFile(string baseName, string relDir, string suffix)
        {
            var n = baseName; int i = 2;
            while (files.Contains(relDir + "/" + n + suffix)) n = baseName + "_" + (i++);
            files.Add(relDir + "/" + n + suffix);
            return relDir + "/" + n + suffix;
        }

        /// <summary>Base color texture into relDir. Returns the path relative to the level, or null.</summary>
        public string ExportColor(Texture src, string relDir, out int exportedSize)
        {
            exportedSize = 0;
            if (src == null) return null;
            string key = src.GetInstanceID() + "|" + relDir;
            string rel;
            if (cache.TryGetValue(key, out rel)) { exportedSize = Mathf.Min(Mathf.ClosestPowerOfTwo(Mathf.Max(16, Mathf.Max(src.width, src.height))), c.Settings.maxTextureSize); return rel; }
            try
            {
                // Power-of-two size, capped at the max texture size (safest for BeamNG's texture pipeline).
                int w = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(16, src.width)), 16, c.Settings.maxTextureSize);
                int h = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(16, src.height)), 16, c.Settings.maxTextureSize);
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var prev = RenderTexture.active;
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var t2 = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
                t2.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                t2.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                rel = UniqueFile(BeamNGEditorUtilities.SafeId(src.name), relDir, ".color.png");
                File.WriteAllBytes(c.Abs(rel), t2.EncodeToPNG());
                Object.DestroyImmediate(t2);
                c.Require(rel);
                cache[key] = rel;
                exportedSize = Mathf.Max(w, h);
                return rel;
            }
            catch (System.Exception e)
            {
                c.Report.Add(ReportStatus.WARNING, "Texture", $"Texture '{src.name}' could not be exported: {e.Message}", "Check the texture import settings.");
                return null;
            }
        }


        /// <summary>Exports src resized to exactly size x size (used for terrain texture slots).</summary>
        public string ExportSized(Texture src, string relDir, int size, string suffix, bool linear)
        {
            if (src == null) return null;
            string key = "sz" + size + suffix + "|" + src.GetInstanceID() + "|" + relDir;
            string rel;
            if (cache.TryGetValue(key, out rel)) return rel;
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var t2 = new Texture2D(size, size, TextureFormat.RGBA32, false, linear);
            t2.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            t2.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            rel = UniqueFile(BeamNGEditorUtilities.SafeId(src.name), relDir, suffix);
            File.WriteAllBytes(c.Abs(rel), t2.EncodeToPNG());
            Object.DestroyImmediate(t2);
            c.Require(rel);
            cache[key] = rel;
            return rel;
        }

        /// <summary>Flat single-color PNG of exactly size x size (neutral macro/detail/normal/roughness/ao/height slots).</summary>
        public string ExportFlat(Color32 col, string relDir, string name, int size, string suffix)
        {
            string key = "flat_" + name + size + suffix + "|" + relDir;
            string rel;
            if (cache.TryGetValue(key, out rel)) return rel;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            var px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = col;
            t.SetPixels32(px);
            rel = UniqueFile(name + "_" + size, relDir, suffix);
            File.WriteAllBytes(c.Abs(rel), t.EncodeToPNG());
            Object.DestroyImmediate(t);
            c.Require(rel);
            cache[key] = rel;
            return rel;
        }

        public string ExportSolid(Color col, string relDir)
        {
            var c32 = (Color32)col;
            string key = "solid_" + c32.r.ToString("x2") + c32.g.ToString("x2") + c32.b.ToString("x2") + "|" + relDir;
            string rel;
            if (cache.TryGetValue(key, out rel)) return rel;
            // BeamNG's TextureCooker rejects anything smaller than 16x16 or non power-of-two.
            var t = new Texture2D(16, 16, TextureFormat.RGBA32, false, false);
            var px = new Color32[256];
            for (int i = 0; i < 256; i++) px[i] = new Color32(c32.r, c32.g, c32.b, 255);
            t.SetPixels32(px);
            rel = UniqueFile("solid_" + c32.r.ToString("x2") + c32.g.ToString("x2") + c32.b.ToString("x2"), relDir, ".color.png");
            File.WriteAllBytes(c.Abs(rel), t.EncodeToPNG());
            Object.DestroyImmediate(t);
            c.Require(rel);
            cache[key] = rel;
            return rel;
        }
    }

    public static class BeamNGMaterialExporter
    {
        static Texture FindTex(Material m, params string[] props)
        {
            foreach (var p in props) if (m.HasProperty(p)) { var t = m.GetTexture(p); if (t != null) return t; }
            return null;
        }

        static float FindFloat(Material m, float def, params string[] props)
        {
            foreach (var p in props) if (m.HasProperty(p)) return m.GetFloat(p);
            return def;
        }

        static Color FindColor(Material m, params string[] props)
        {
            foreach (var p in props) if (m.HasProperty(p)) return m.GetColor(p);
            return Color.white;
        }

        /// <summary>Writes main.materials.json. Names match the DAE material names (mapTo).</summary>
        public static void Export(BeamNGExportContext c, BeamNGMaterialRegistry reg, BeamNGTextureExporter tex)
        {
            if (reg.Materials.Count == 0 && !reg.UsesMissing) return;
            var entries = new List<string>();
            int tiled = 0, transparent = 0, normals = 0, emissive = 0, tinted = 0;

            foreach (var m in reg.Materials)
            {
                var name = reg.NameOf(m);
                var mainTex = FindTex(m, "_BaseMap", "_MainTex");
                var tint = FindColor(m, "_BaseColor", "_Color");
                string colorRel = null; int sz;
                if (mainTex != null) colorRel = tex.ExportColor(mainTex, "art/textures", out sz);
                if (colorRel == null) colorRel = tex.ExportSolid(tint, "art/textures");
                else if (Mathf.Abs(tint.r - 1) + Mathf.Abs(tint.g - 1) + Mathf.Abs(tint.b - 1) > 0.05f) tinted++;

                float metallic = Mathf.Clamp01(FindFloat(m, 0f, "_Metallic"));
                float smooth = Mathf.Clamp01(FindFloat(m, 0.3f, "_Smoothness", "_Glossiness"));

                var ts = m.mainTextureScale;
                if (mainTex != null && (Mathf.Abs(ts.x - 1) > 0.001f || Mathf.Abs(ts.y - 1) > 0.001f)) tiled++;
                if (m.renderQueue >= 3000) transparent++;
                if (FindTex(m, "_BumpMap") != null) normals++;
                if (m.IsKeywordEnabled("_EMISSION")) emissive++;

                // Plain string / number values (an array-valued "baseColorMap" made BeamNG ignore the texture).
                var stage = new JsonObj()
                    .Str("baseColorMap", c.VPath(colorRel))
                    .Arr("baseColorFactor", 1f, 1f, 1f, 1f)
                    .Num("metallicFactor", metallic)
                    .Num("roughnessFactor", 1f - smooth);
                var mat = new JsonObj()
                    .Str("name", name).Str("class", "Material").Str("mapTo", name).Num("version", 1.5f)
                    .Raw("Stages", "[" + stage + ",{},{},{}]");
                entries.Add(BeamNGJson.Q(name) + ":" + mat);
            }
            if (reg.UsesMissing)
            {
                var rel = tex.ExportSolid(new Color(0.5f, 0.5f, 0.5f), "art/textures");
                var stage = new JsonObj().Str("baseColorMap", c.VPath(rel));
                var mat = new JsonObj().Str("name", BeamNGMaterialRegistry.Missing).Str("class", "Material")
                    .Str("mapTo", BeamNGMaterialRegistry.Missing).Num("version", 1.5f).Raw("Stages", "[" + stage + ",{},{},{}]");
                entries.Add(BeamNGJson.Q(BeamNGMaterialRegistry.Missing) + ":" + mat);
            }

            // Written next to the shapes AND at the level root (both are valid material locations per the docs).
            var content = "{\n" + string.Join(",\n", entries) + "\n}\n";
            foreach (var relPath in new[] { "art/shapes/main.materials.json", "main.materials.json" })
            {
                File.WriteAllText(c.Abs(relPath), content, new UTF8Encoding(false));
                c.Require(relPath);
            }

            c.Report.Add(ReportStatus.PASS, "Material", $"{reg.Materials.Count} material(s) converted to main.materials.json (base color, metallic, roughness).");
            c.Report.Add(ReportStatus.WARNING, "Material", "Material JSON follows the docs example (version 1.5) but has not been verified in game.", "Check the BeamNG log for material errors on first load.");
            if (normals > 0) c.Report.Add(ReportStatus.WARNING, "Material", $"{normals} material(s) with a normal map: normal maps are NOT transferred (encoding unreliable).", "Add normal maps manually as *.normal.png.");
            if (tiled > 0) c.Report.Add(ReportStatus.PASS, "Material", $"{tiled} material(s) with texture tiling/offset: baked into the DAE UVs.");
            if (transparent > 0) c.Report.Add(ReportStatus.WARNING, "Material", $"{transparent} transparent material(s) are exported as opaque.", "");
            if (emissive > 0) c.Report.Add(ReportStatus.WARNING, "Material", $"{emissive} material(s) with emission: emission is not transferred.", "");
            if (tinted > 0) c.Report.Add(ReportStatus.WARNING, "Material", $"{tinted} material(s) with a color tint: the tint is not baked into the texture.", "");
        }
    }
}
