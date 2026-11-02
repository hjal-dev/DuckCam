using System;
using System.Globalization;
using UnityEngine;

namespace DuckCam.UI
{
    internal static class ThemeColors
    {
        public static string ToHex(Color c)
        {
            Color32 b = c;
            return b.a == 255 ? $"#{b.r:X2}{b.g:X2}{b.b:X2}" : $"#{b.r:X2}{b.g:X2}{b.b:X2}{b.a:X2}";
        }

        public static string ToRgb(Color c)
        {
            Color32 b = c;
            return c.a >= 0.999f
                ? $"rgb({b.r}, {b.g}, {b.b})"
                : $"rgba({b.r}, {b.g}, {b.b}, {c.a.ToString("0.##", CultureInfo.InvariantCulture)})";
        }

        public static string ToHsl(Color c)
        {
            RgbToHsl(c, out float h, out float s, out float l);
            string body = $"{Mathf.RoundToInt(h * 360f)}, {Mathf.RoundToInt(s * 100f)}%, {Mathf.RoundToInt(l * 100f)}%";
            return c.a >= 0.999f ? $"hsl({body})" : $"hsla({body}, {c.a.ToString("0.##", CultureInfo.InvariantCulture)})";
        }

        public static bool TryParse(string text, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            string s = text.Trim().TrimEnd(';').Trim().ToLowerInvariant();
            if (s.StartsWith("rgb", StringComparison.Ordinal))
            {
                return TryFunction(s, out float[] v) && v.Length >= 3 && Rgb(v, out color);
            }
            if (s.StartsWith("hsl", StringComparison.Ordinal))
            {
                return TryFunction(s, out float[] v) && v.Length >= 3 && Hsl(v, out color);
            }
            return TryHex(s, out color);
        }

        private static bool TryHex(string s, out Color color)
        {
            color = Color.white;
            s = s.TrimStart('#');
            if (s.Length == 3 || s.Length == 4)
            {
                var expanded = new char[s.Length * 2];
                for (int i = 0; i < s.Length; i++)
                {
                    expanded[i * 2] = s[i];
                    expanded[i * 2 + 1] = s[i];
                }
                s = new string(expanded);
            }
            if (s.Length != 6 && s.Length != 8)
            {
                return false;
            }
            if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
            {
                return false;
            }
            if (s.Length == 6)
            {
                value = (value << 8) | 0xFF;
            }
            color = new Color32((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
            return true;
        }

        private static bool TryFunction(string s, out float[] values)
        {
            values = null;
            int open = s.IndexOf('(');
            int close = s.LastIndexOf(')');
            if (open < 0 || close <= open)
            {
                return false;
            }
            string[] parts = s.Substring(open + 1, close - open - 1).Replace("/", ",").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            values = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                bool percent = p.EndsWith("%", StringComparison.Ordinal);
                p = p.TrimEnd('%').Replace("deg", "");
                if (!float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                {
                    return false;
                }
                values[i] = percent ? -v / 100f : v;
            }
            return true;
        }

        private static float Unit(float v, float max) => v < 0f ? Mathf.Clamp01(-v) : Mathf.Clamp01(v / max);

        private static float AlphaOf(float[] v) => v.Length >= 4 ? (v[3] < 0f ? Mathf.Clamp01(-v[3]) : Mathf.Clamp01(v[3] > 1f ? v[3] / 255f : v[3])) : 1f;

        private static bool Rgb(float[] v, out Color color)
        {
            color = new Color(Unit(v[0], 255f), Unit(v[1], 255f), Unit(v[2], 255f), AlphaOf(v));
            return true;
        }

        private static bool Hsl(float[] v, out Color color)
        {
            float h = Mathf.Repeat(Mathf.Abs(v[0]) / 360f, 1f);
            float s = v[1] < 0f ? Mathf.Clamp01(-v[1]) : Mathf.Clamp01(v[1] > 1f ? v[1] / 100f : v[1]);
            float l = v[2] < 0f ? Mathf.Clamp01(-v[2]) : Mathf.Clamp01(v[2] > 1f ? v[2] / 100f : v[2]);
            color = HslToRgb(h, s, l);
            color.a = AlphaOf(v);
            return true;
        }

        public static void RgbToHsl(Color c, out float h, out float s, out float l)
        {
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            l = (max + min) * 0.5f;
            float d = max - min;
            if (d < 1e-5f)
            {
                h = 0f;
                s = 0f;
                return;
            }
            s = l > 0.5f ? d / (2f - max - min) : d / (max + min);
            Color.RGBToHSV(c, out h, out _, out _);
        }

        public static Color HslToRgb(float h, float s, float l)
        {
            float v = l + s * Mathf.Min(l, 1f - l);
            float sv = v <= 0f ? 0f : 2f * (1f - l / v);
            return Color.HSVToRGB(h, sv, v);
        }

        public static Color Mix(Color a, Color b, float t)
        {
            Color la = a.linear;
            Color lb = b.linear;
            Color m = Color.Lerp(la, lb, t).gamma;
            m.a = Mathf.Lerp(a.a, b.a, t);
            return m;
        }

        public static Color Shade(Color c, float brightness, float alpha)
        {
            return new Color(c.r * brightness, c.g * brightness, c.b * brightness, alpha);
        }

        public static Color WithAlpha(Color c, float alpha) => new Color(c.r, c.g, c.b, alpha);

        public static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        public static Texture2D Gradient(Color from, Color to, float angle, int size = 64)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            float rad = angle * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            float reach = Mathf.Abs(dir.x) * 0.5f + Mathf.Abs(dir.y) * 0.5f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x + 0.5f) / size - 0.5f, (y + 0.5f) / size - 0.5f);
                    float t = Mathf.Clamp01((Vector2.Dot(p, dir) / Mathf.Max(0.0001f, reach) + 1f) * 0.5f);
                    px[y * size + x] = Mix(from, to, t);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }
    }
}
