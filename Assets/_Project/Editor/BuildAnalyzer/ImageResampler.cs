using UnityEngine;

namespace Project.EditorTools.BuildAnalyzer
{
    /// <summary>
    /// CPU resampler for the Make-POT action. Separable (rows, then columns), works in premultiplied
    /// alpha so transparent pixels never bleed dark fringes into the art. Downscaling uses an area
    /// (box) filter — every source pixel contributes with its coverage, so nothing is skipped — and
    /// upscaling uses linear interpolation.
    /// </summary>
    internal static class ImageResampler
    {
        public static Color32[] Resample(Color32[] source, int width, int height, int newWidth, int newHeight)
        {
            if (newWidth == width && newHeight == height)
                return (Color32[])source.Clone();

            // Premultiplied float RGBA.
            var src = new float[width * height * 4];
            for (int i = 0; i < source.Length; i++)
            {
                Color32 c = source[i];
                float alpha = c.a / 255f;
                src[i * 4 + 0] = c.r / 255f * alpha;
                src[i * 4 + 1] = c.g / 255f * alpha;
                src[i * 4 + 2] = c.b / 255f * alpha;
                src[i * 4 + 3] = alpha;
            }

            float[] horizontal = newWidth == width ? src : ResampleAxis(src, width, height, newWidth, horizontal: true);
            float[] both = newHeight == height ? horizontal : ResampleAxis(horizontal, newWidth, height, newHeight, horizontal: false);

            var result = new Color32[newWidth * newHeight];
            for (int i = 0; i < result.Length; i++)
            {
                float alpha = both[i * 4 + 3];
                if (alpha <= 0.0001f)
                {
                    result[i] = new Color32(0, 0, 0, 0);
                    continue;
                }

                result[i] = new Color32(
                    ToByte(both[i * 4 + 0] / alpha),
                    ToByte(both[i * 4 + 1] / alpha),
                    ToByte(both[i * 4 + 2] / alpha),
                    ToByte(alpha));
            }

            return result;
        }

        /// <summary>Resamples one axis. Layout is row-major RGBA; "lines" are the axis not being resampled.</summary>
        private static float[] ResampleAxis(float[] src, int width, int height, int newLength, bool horizontal)
        {
            int length = horizontal ? width : height;
            int lines = horizontal ? height : width;
            int outWidth = horizontal ? newLength : width;
            int outHeight = horizontal ? height : newLength;
            var dst = new float[outWidth * outHeight * 4];

            bool downscale = newLength < length;
            float ratio = (float)length / newLength;

            for (int line = 0; line < lines; line++)
            {
                for (int i = 0; i < newLength; i++)
                {
                    float r = 0f, g = 0f, b = 0f, a = 0f;

                    if (downscale)
                    {
                        // Box filter over [start, end) with fractional coverage at both ends.
                        float start = i * ratio;
                        float end = start + ratio;
                        int first = Mathf.FloorToInt(start);
                        int last = Mathf.Min(length - 1, Mathf.CeilToInt(end) - 1);
                        float total = 0f;
                        for (int s = first; s <= last; s++)
                        {
                            float coverage = Mathf.Min(end, s + 1) - Mathf.Max(start, s);
                            if (coverage <= 0f) continue;
                            int idx = Index(s, line, width, horizontal) * 4;
                            r += src[idx + 0] * coverage;
                            g += src[idx + 1] * coverage;
                            b += src[idx + 2] * coverage;
                            a += src[idx + 3] * coverage;
                            total += coverage;
                        }

                        if (total > 0f)
                        {
                            r /= total; g /= total; b /= total; a /= total;
                        }
                    }
                    else
                    {
                        // Linear interpolation between the two nearest source samples.
                        float pos = (i + 0.5f) * ratio - 0.5f;
                        int s0 = Mathf.Clamp(Mathf.FloorToInt(pos), 0, length - 1);
                        int s1 = Mathf.Min(length - 1, s0 + 1);
                        float t = Mathf.Clamp01(pos - s0);
                        int i0 = Index(s0, line, width, horizontal) * 4;
                        int i1 = Index(s1, line, width, horizontal) * 4;
                        r = Mathf.Lerp(src[i0 + 0], src[i1 + 0], t);
                        g = Mathf.Lerp(src[i0 + 1], src[i1 + 1], t);
                        b = Mathf.Lerp(src[i0 + 2], src[i1 + 2], t);
                        a = Mathf.Lerp(src[i0 + 3], src[i1 + 3], t);
                    }

                    int o = (horizontal ? line * outWidth + i : i * outWidth + line) * 4;
                    dst[o + 0] = r;
                    dst[o + 1] = g;
                    dst[o + 2] = b;
                    dst[o + 3] = a;
                }
            }

            return dst;
        }

        private static int Index(int along, int line, int width, bool horizontal) =>
            horizontal ? line * width + along : along * width + line;

        private static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
    }
}
