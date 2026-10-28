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
        /// <summary>Names of BeamNG/Torque classes: a material called like this is not found / not mapped (seen in beamng.log: Camera, spotlight, Material).</summary>
        static readonly HashSet<string> Reserved = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "camera", "spotlight", "material", "light", "pointlight", "sun", "player", "terrain", "sky", "water", "sound", "shape",
            "forest", "decal", "particle", "marker", "object", "group", "simgroup", "tsstatic", "item", "trigger", "vehicle", "default"
        };
        /// <summary>Materials of leaves/grass: exported double-sided, alpha as cutout (set by the vegetation exporter).</summary>
        public readonly HashSet<Material> Foliage = new HashSet<Material>();

        public string NameOf(Material m)
        {
            if (m == null) { UsesMissing = true; return Missing; }
            string n;
            if (names.TryGetValue(m, out n)) return n;
            var b = BeamNGEditorUtilities.SafeId(m.name);
            if (b == Missing || Reserved.Contains(b)) b = "m_" + b;
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
            baseName = BeamNGEditorUtilities.ShortId(baseName, 40);
            var n = baseName; int i = 2;
            while (files.Contains(relDir + "/" + n + suffix)) n = baseName + "_" + (i++);
            files.Add(relDir + "/" + n + suffix);
            return relDir + "/" + n + suffix;
        }

        /// <summary>Info about the alpha channel of the last exported color texture (per relative path).</summary>
        public readonly Dictionary<string, float> AlphaCoverage = new Dictionary<string, float>(); // fraction of texels with alpha < 0.98
        public readonly Dictionary<string, string> OpacityFiles = new Dictionary<string, string>();

        /// <summary>Base color texture into relDir. Returns the path relative to the level, or null.
        /// BeamNG's texture cooker treats *.color.png as RGB (alpha is dropped), so a texture with an alpha channel
        /// is split: the color PNG is opaque (colors bled into transparent areas) and the alpha goes to a separate
        /// *.dataR.png opacity map (see OpacityFor).</summary>
        public string ExportColor(Texture src, string relDir, out int exportedSize)
        {
            exportedSize = 0;
            if (src == null) return null;
            string key = src.GetInstanceID() + "|" + relDir;
            string rel;
            if (cache.TryGetValue(key, out rel)) { exportedSize = Mathf.Min(Mathf.ClosestPowerOfTwo(Mathf.Max(16, Mathf.Max(src.width, src.height))), c.Settings.maxTextureSize); return rel; }
            try
            {
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

                // alpha analysis
                var px = t2.GetPixels32();
                int semi = 0;
                for (int i = 0; i < px.Length; i++) if (px[i].a < 250) semi++;
                float coverage = semi / (float)px.Length;
                rel = UniqueFile(BeamNGEditorUtilities.SafeId(src.name), relDir, ".color.png");
                if (coverage > 0.0005f)
                {
                    // opacity map (red channel = alpha)
                    var op = new Color32[px.Length];
                    for (int i = 0; i < px.Length; i++) { byte a = px[i].a; op[i] = new Color32(a, a, a, 255); }
                    var topac = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                    topac.SetPixels32(op);
                    var orel = UniqueFile(BeamNGEditorUtilities.SafeId(src.name) + "_o", relDir, ".data.png");
                    File.WriteAllBytes(c.Abs(orel), topac.EncodeToPNG());
                    Object.DestroyImmediate(topac);
                    c.Require(orel);
                    OpacityFiles[rel] = orel;
                    BleedColors(px, w, h);
                    t2.SetPixels32(px);
                    t2.Apply();
                }
                AlphaCoverage[rel] = coverage;
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

        /// <summary>Opacity map written next to a color texture that has alpha, or null.</summary>
        public string OpacityFor(string colorRel)
        {
            string o; return colorRel != null && OpacityFiles.TryGetValue(colorRel, out o) ? o : null;
        }

        /// <summary>Makes all texels opaque and spreads the colors of visible texels into transparent ones (no dark/black fringes). Works in place.</summary>
        static void BleedColors(Color32[] px, int w, int h)
        {
            var known = new bool[px.Length];
            for (int i = 0; i < px.Length; i++) known[i] = px[i].a >= 128;
            for (int pass = 0; pass < 24; pass++)
            {
                bool any = false;
                var nk = (bool[])known.Clone();
                var np = (Color32[])px.Clone();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (known[i]) continue;
                        int r = 0, g = 0, b = 0, n = 0;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx, yy = y + dy;
                                if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                                int j = yy * w + xx;
                                if (!known[j]) continue;
                                r += px[j].r; g += px[j].g; b += px[j].b; n++;
                            }
                        if (n > 0) { np[i] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255); nk[i] = true; any = true; }
                    }
                System.Array.Copy(np, px, px.Length);
                known = nk;
                if (!any) break;
            }
            long sr = 0, sg = 0, sb = 0, sn = 0;
            for (int i = 0; i < px.Length; i++) if (known[i]) { sr += px[i].r; sg += px[i].g; sb += px[i].b; sn++; }
            var avg = sn > 0 ? new Color32((byte)(sr / sn), (byte)(sg / sn), (byte)(sb / sn), 255) : new Color32(255, 255, 255, 255);
            for (int i = 0; i < px.Length; i++) { var q = px[i]; px[i] = known[i] ? new Color32(q.r, q.g, q.b, 255) : avg; }
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

        static bool HasAlpha(Texture t)
        {
            try { return UnityEngine.Experimental.Rendering.GraphicsFormatUtility.HasAlphaChannel(t.graphicsFormat); }
            catch { return false; }
        }

        static readonly string[] MarkingWords = { "mark", "line", "yellow", "white", "decal", "overlay", "tire", "stripe", "paint", "crack", "arrow", "crosswalk", "zebra" };
        static bool LooksLikeMarking(string name)
        {
            var n = name.ToLowerInvariant();
            foreach (var w in MarkingWords) if (n.Contains(w)) return true;
            return false;
        }

        static bool HasAlphaPixels(BeamNGTextureExporter tex, string colorRel)
        {
            float cov; return tex.AlphaCoverage.TryGetValue(colorRel, out cov) && cov > 0.0005f;
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
            int tiled = 0, transparent = 0, cutouts = 0, normals = 0, emissive = 0, tinted = 0, noAlpha = 0, opacityMaps = 0;

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
                if (FindTex(m, "_BumpMap") != null) normals++;

                // transparency: blend (queue >= 3000 / Standard _Mode 2,3), cutout (queue >= 2450 / _Mode 1), or an
                // RGBA texture on a road-marking/decal-like material (name hint).
                float mode = m.HasProperty("_Mode") ? m.GetFloat("_Mode") : 0f;
                bool blend = m.renderQueue >= 3000 || mode >= 2f || m.IsKeywordEnabled("_ALPHABLEND_ON") || m.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON");
                bool cutout = !blend && (m.renderQueue >= 2450 || Mathf.Approximately(mode, 1f) || m.IsKeywordEnabled("_ALPHATEST_ON"));
                if (!blend && !cutout && mainTex != null && HasAlpha(mainTex) && LooksLikeMarking(name)) blend = true;
                string opacityRel = mainTex != null ? tex.OpacityFor(colorRel) : null;
                float tintAlpha = Mathf.Clamp01(tint.a);
                // Sign-like textures (alpha almost binary) look better and sort correctly as alpha test.
                if (blend && opacityRel != null && tintAlpha > 0.99f && !LooksLikeMarking(name)) { blend = false; cutout = true; }
                if (opacityRel == null && (blend || cutout) && mainTex != null && !HasAlphaPixels(tex, colorRel))
                {
                    // no alpha in the texture: the transparent render mode would only make the object see-through/black
                    if (tintAlpha > 0.99f) { blend = false; cutout = false; noAlpha++; }
                }
                bool foliage = reg.Foliage.Contains(m);
                // leaves / grass: soft alpha sorts badly -> alpha test, and both sides must be visible
                if (foliage && opacityRel != null) { blend = false; cutout = true; }
                if (blend) transparent++; else if (cutout) cutouts++;
                bool doubleSided = (m.HasProperty("_Cull") && Mathf.Approximately(m.GetFloat("_Cull"), 0f)) || foliage;

                // Plain string / number values (an array-valued "baseColorMap" made BeamNG ignore the texture).
                var stage = new JsonObj()
                    .Str("baseColorMap", c.VPath(colorRel))
                    .Arr("baseColorFactor", 1f, 1f, 1f, 1f)
                    .Num("metallicFactor", metallic)
                    .Num("roughnessFactor", 1f - smooth);
                if (opacityRel != null && (blend || cutout)) { stage.Str("opacityMap", c.VPath(opacityRel)); opacityMaps++; }
                // emission (e.g. lamps of traffic lights)
                if (m.IsKeywordEnabled("_EMISSION") && m.HasProperty("_EmissionColor"))
                {
                    var ec = m.GetColor("_EmissionColor");
                    if (ec.maxColorComponent > 0.01f)
                    {
                        var etex = FindTex(m, "_EmissionMap");
                        string erel = null; int esz;
                        if (etex != null) erel = tex.ExportColor(etex, "art/textures", out esz);
                        if (erel == null)
                        {
                            float mx = Mathf.Max(1f, ec.maxColorComponent);
                            erel = tex.ExportSolid(new Color(Mathf.Clamp01(ec.r / mx), Mathf.Clamp01(ec.g / mx), Mathf.Clamp01(ec.b / mx)), "art/textures");
                        }
                        stage.Str("emissiveMap", c.VPath(erel)).Arr("emissiveFactor", 1f, 1f, 1f, 1f);
                        emissive++;
                    }
                }
                var mat = new JsonObj()
                    .Str("name", name).Str("class", "Material").Str("mapTo", name).Num("version", 1.5f);
                if (blend) mat.Bool("translucent", true).Str("translucentBlendOp", "LerpAlpha").Bool("translucentZWrite", true);
                else if (cutout) mat.Bool("alphaTest", true).Num("alphaRef", Mathf.Round(Mathf.Clamp(m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") * 255f : 127f, 1f, 254f)));
                if (doubleSided) mat.Bool("doubleSided", true);
                mat.Raw("Stages", "[" + stage + ",{},{},{}]");
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
            if (normals > 0) c.Report.Add(ReportStatus.WARNING, "Material", $"{normals} material(s) with a normal map: normal maps are NOT transferred (encoding unreliable).", "Add normal maps manually as *.normal.png.");
            if (tiled > 0) c.Report.Add(ReportStatus.PASS, "Material", $"{tiled} material(s) with texture tiling/offset: baked into the DAE UVs.");
            if (opacityMaps > 0) c.Report.Add(ReportStatus.PASS, "Material", $"{opacityMaps} material(s) with an alpha texture: color PNG is opaque, alpha exported as separate opacityMap (*.dataR.png). BeamNG's texture cooker drops alpha of *.color.png (black background otherwise).");
            if (noAlpha > 0) c.Report.Add(ReportStatus.WARNING, "Material", $"{noAlpha} material(s) use a transparent render mode but their texture has no alpha channel: exported opaque.", "Use a PNG with a transparent background (or set the material to Opaque).");
            if (transparent > 0) c.Report.Add(ReportStatus.WARNING, "Material", $"{transparent} transparent material(s) exported with alpha blending (translucent/LerpAlpha, e.g. road markings).", "");
            if (emissive > 0) c.Report.Add(ReportStatus.WARNING, "Material", $"{emissive} material(s) with emission exported (emissiveMap/emissiveFactor).", "");
            if (tinted > 0) c.Report.Add(ReportStatus.WARNING, "Material", $"{tinted} material(s) with a color tint: the tint is not baked into the texture.", "");
        }
    }
}
