using UnityEngine;

namespace UnityToBeamNG
{
    /// <summary>Procedural speed sign face: white disc with a red ring and the limit as digits (no fonts needed).</summary>
    public static class BeamNGSpeedSignTexture
    {
        // 3x5 bitmap digits
        static readonly string[] Digits =
        {
            "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
            "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111"
        };

        public static Texture2D Make(int limit, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f, rOuter = size * 0.5f - 1f, rInner = rOuter * 0.78f;
            string text = Mathf.Clamp(limit, 0, 999).ToString();
            int n = text.Length;
            float cell = Mathf.Min(rInner * 1.5f / (n * 3.6f), rInner * 1.1f / 5f);   // size of one bitmap pixel
            float totalW = n * 3f * cell + (n - 1) * cell * 0.6f, totalH = 5f * cell;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - c, dy = y - c, d = Mathf.Sqrt(dx * dx + dy * dy);
                    Color32 col = new Color32(255, 255, 255, 0);
                    if (d <= rOuter) col = d > rInner ? new Color32(205, 20, 20, 255) : new Color32(250, 250, 250, 255);
                    if (d <= rInner)
                    {
                        float lx = dx + totalW * 0.5f, ly = totalH * 0.5f - dy;          // image y is up: flip into the digit grid (row 0 at the top)
                        if (lx >= 0f && lx < totalW && ly >= 0f && ly < totalH)
                        {
                            float stride = 3f * cell + cell * 0.6f;
                            int di = Mathf.Min(n - 1, (int)(lx / stride));
                            float inX = lx - di * stride;
                            if (inX < 3f * cell)
                            {
                                int gx = (int)(inX / cell), gy = (int)(ly / cell);
                                if (gx < 3 && gy < 5 && Digits[text[di] - '0'][gy * 3 + gx] == '1') col = new Color32(15, 15, 15, 255);
                            }
                        }
                    }
                    px[y * size + x] = col;
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
