using System.Collections.Generic;
using UnityEngine;

namespace DuckCam.Rendering
{
    internal enum DecorMask
    {
        None,
        Oval,
        Circle,
        Rounded
    }

    internal enum BorderStyle
    {
        None,
        Solid,
        Polaroid
    }

    internal sealed class DecorText
    {
        public string Text = "DuckCam";
        public float X = 0.5f;
        public float Y = 0.9f;
        public float Size = 0.05f;
        public int Color;
        public float Opacity = 1f;
        public bool Shadow = true;
        public bool Bold;
        public int Align = 1;
    }

    internal sealed class PhotoDecor
    {
        public static readonly string[] ColorNames = { "White", "Black", "Tarkov Khaki", "Yellow", "Red", "Cyan", "Orange", "Green" };
        public static readonly Color[] Colors =
        {
            Color.white, Color.black, new Color(0.78f, 0.75f, 0.62f), new Color(1f, 0.86f, 0.2f),
            new Color(0.9f, 0.18f, 0.16f), new Color(0.3f, 0.9f, 0.95f), new Color(1f, 0.55f, 0.1f), new Color(0.45f, 0.8f, 0.35f)
        };

        public readonly List<DecorText> Texts = new List<DecorText>();
        public int SelectedText;

        public BorderStyle Border = BorderStyle.None;
        public float BorderWidth = 0.03f;
        public int BorderColor;

        public DecorMask Mask = DecorMask.None;
        public float MaskSize = 0.85f;
        public float MaskSoftness = 0.25f;
        public int MaskColor = 1;
        public float MaskOpacity = 1f;
        public float MaskCorner = 0.15f;

        private Texture2D _maskTex;
        private string _maskKey;
        private GUIStyle _textStyle;
        private Font _font;

        public bool Active => Texts.Count > 0 || Border != BorderStyle.None || Mask != DecorMask.None;

        public DecorText SelectedTextItem => SelectedText >= 0 && SelectedText < Texts.Count ? Texts[SelectedText] : null;

        public void Reset()
        {
            var d = new PhotoDecor();
            Texts.Clear();
            SelectedText = 0;
            Border = d.Border;
            BorderWidth = d.BorderWidth;
            BorderColor = d.BorderColor;
            Mask = d.Mask;
            MaskSize = d.MaskSize;
            MaskSoftness = d.MaskSoftness;
            MaskColor = d.MaskColor;
            MaskOpacity = d.MaskOpacity;
            MaskCorner = d.MaskCorner;
        }

        public void AddText()
        {
            Texts.Add(new DecorText { Y = Texts.Count == 0 ? 0.9f : Mathf.Clamp01(0.9f - Texts.Count * 0.08f) });
            SelectedText = Texts.Count - 1;
        }

        public void RemoveSelectedText()
        {
            if (SelectedTextItem == null)
            {
                return;
            }
            Texts.RemoveAt(SelectedText);
            SelectedText = Mathf.Clamp(SelectedText, 0, Mathf.Max(0, Texts.Count - 1));
        }

        public void Draw(Rect frame)
        {
            if (!Active || Event.current.type != EventType.Repaint)
            {
                return;
            }
            Color prev = GUI.color;
            try
            {
                if (Mask != DecorMask.None)
                {
                    DrawMask(frame);
                }
                if (Border != BorderStyle.None)
                {
                    DrawBorder(frame);
                }
                foreach (DecorText t in Texts)
                {
                    DrawText(frame, t);
                }
            }
            finally
            {
                GUI.color = prev;
            }
        }

        private void DrawBorder(Rect frame)
        {
            float w = Mathf.Max(1f, Mathf.Min(frame.width, frame.height) * BorderWidth);
            float bottom = Border == BorderStyle.Polaroid ? w * 3.5f : w;
            Color c = Colors[Mathf.Clamp(BorderColor, 0, Colors.Length - 1)];
            Fill(new Rect(frame.x, frame.y, frame.width, w), c);
            Fill(new Rect(frame.x, frame.yMax - bottom, frame.width, bottom), c);
            Fill(new Rect(frame.x, frame.y, w, frame.height), c);
            Fill(new Rect(frame.xMax - w, frame.y, w, frame.height), c);
        }

        private void DrawText(Rect frame, DecorText t)
        {
            if (string.IsNullOrEmpty(t.Text))
            {
                return;
            }
            if (_textStyle == null)
            {
                _textStyle = new GUIStyle(GUI.skin.label) { wordWrap = false, richText = false, clipping = TextClipping.Overflow };
            }
            _font = _textStyle.font != null ? _textStyle.font : GUI.skin.font;
            _textStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(frame.height * t.Size), 6, 400);
            _textStyle.fontStyle = t.Bold ? FontStyle.Bold : FontStyle.Normal;
            _textStyle.alignment = t.Align == 0 ? TextAnchor.MiddleLeft : (t.Align == 2 ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter);
            var content = new GUIContent(t.Text);
            Vector2 size = _textStyle.CalcSize(content);
            float x = frame.x + t.X * frame.width;
            float y = frame.y + t.Y * frame.height;
            float left = t.Align == 0 ? x : (t.Align == 2 ? x - size.x : x - size.x * 0.5f);
            var rect = new Rect(left, y - size.y * 0.5f, size.x, size.y);

            Color c = Colors[Mathf.Clamp(t.Color, 0, Colors.Length - 1)];
            if (t.Shadow)
            {
                float o = Mathf.Max(1f, _textStyle.fontSize * 0.06f);
                Color shadow = t.Color == 1 ? new Color(1f, 1f, 1f, 0.6f * t.Opacity) : new Color(0f, 0f, 0f, 0.65f * t.Opacity);
                _textStyle.normal.textColor = shadow;
                GUI.color = Color.white;
                GUI.Label(new Rect(rect.x + o, rect.y + o, rect.width, rect.height), content, _textStyle);
            }
            _textStyle.normal.textColor = new Color(c.r, c.g, c.b, t.Opacity);
            GUI.color = Color.white;
            GUI.Label(rect, content, _textStyle);
        }

        private void DrawMask(Rect frame)
        {
            int w = Mathf.Clamp(Mathf.RoundToInt(frame.width / 4f), 32, 640);
            int h = Mathf.Clamp(Mathf.RoundToInt(frame.height / 4f), 32, 640);
            string key = $"{Mask}|{MaskSize:0.000}|{MaskSoftness:0.000}|{MaskCorner:0.000}|{w}x{h}";
            if (_maskTex == null || key != _maskKey)
            {
                BuildMask(w, h);
                _maskKey = key;
            }
            Color c = Colors[Mathf.Clamp(MaskColor, 0, Colors.Length - 1)];
            GUI.color = new Color(c.r, c.g, c.b, MaskOpacity);
            GUI.DrawTexture(frame, _maskTex, ScaleMode.StretchToFill, true);
        }

        private void BuildMask(int w, int h)
        {
            if (_maskTex == null || _maskTex.width != w || _maskTex.height != h)
            {
                if (_maskTex != null)
                {
                    UnityEngine.Object.Destroy(_maskTex);
                }
                _maskTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            }
            var px = new Color32[w * h];
            float aspect = (float)w / h;
            float soft = Mathf.Max(0.001f, MaskSoftness * 0.5f);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * 2f - 1f;
                    float v = (y + 0.5f) / h * 2f - 1f;
                    float d = SignedDistance(u, v, aspect);
                    float a = Mathf.Clamp01((d + soft) / (2f * soft));
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            _maskTex.SetPixels32(px);
            _maskTex.Apply(false);
        }

        private float SignedDistance(float u, float v, float aspect)
        {
            float s = Mathf.Max(0.05f, MaskSize);
            switch (Mask)
            {
                case DecorMask.Circle:
                {
                    float du = aspect >= 1f ? u * aspect : u;
                    float dv = aspect >= 1f ? v : v / aspect;
                    return Mathf.Sqrt(du * du + dv * dv) - s;
                }
                case DecorMask.Rounded:
                {
                    float hu = s;
                    float hv = s;
                    float rad = Mathf.Clamp(MaskCorner, 0f, 0.5f) * Mathf.Min(hu, hv) * 2f;
                    float qu = Mathf.Abs(u) - hu + rad;
                    float qv = Mathf.Abs(v) - hv + rad;
                    float outside = new Vector2(Mathf.Max(qu, 0f), Mathf.Max(qv, 0f)).magnitude;
                    return outside + Mathf.Min(Mathf.Max(qu, qv), 0f) - rad;
                }
                default:
                    return Mathf.Sqrt(u * u + v * v) - s;
            }
        }

        public void Bake(Texture2D photo, RectInt region)
        {
            if (!Active || region.width <= 0 || region.height <= 0)
            {
                return;
            }
            int width = photo.width;
            Color32[] dst = photo.GetPixels32();
            if (Mask != DecorMask.None)
            {
                BakeMask(dst, width, region);
            }
            if (Border != BorderStyle.None)
            {
                BakeBorder(dst, width, region);
            }
            foreach (DecorText t in Texts)
            {
                BakeText(dst, width, region, t);
            }
            photo.SetPixels32(dst);
            photo.Apply();
        }

        private void BakeMask(Color32[] dst, int width, RectInt region)
        {
            float aspect = (float)region.width / region.height;
            float soft = Mathf.Max(0.001f, MaskSoftness * 0.5f);
            Color c = Colors[Mathf.Clamp(MaskColor, 0, Colors.Length - 1)];
            for (int y = 0; y < region.height; y++)
            {
                float v = (y + 0.5f) / region.height * 2f - 1f;
                int row = (region.y + y) * width + region.x;
                for (int x = 0; x < region.width; x++)
                {
                    float u = (x + 0.5f) / region.width * 2f - 1f;
                    float a = Mathf.Clamp01((SignedDistance(u, v, aspect) + soft) / (2f * soft)) * MaskOpacity;
                    if (a > 0f)
                    {
                        Blend(ref dst[row + x], c, a);
                    }
                }
            }
        }

        private void BakeBorder(Color32[] dst, int width, RectInt region)
        {
            int w = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(region.width, region.height) * BorderWidth));
            int bottom = Border == BorderStyle.Polaroid ? Mathf.RoundToInt(w * 3.5f) : w;
            Color c = Colors[Mathf.Clamp(BorderColor, 0, Colors.Length - 1)];
            for (int y = 0; y < region.height; y++)
            {
                bool full = y < bottom || y >= region.height - w;
                int row = (region.y + y) * width + region.x;
                for (int x = 0; x < region.width; x++)
                {
                    if (full || x < w || x >= region.width - w)
                    {
                        Blend(ref dst[row + x], c, 1f);
                    }
                }
            }
        }

        private void BakeText(Color32[] dst, int width, RectInt region, DecorText t)
        {
            Font font = _font != null ? _font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (string.IsNullOrEmpty(t.Text) || font == null)
            {
                return;
            }
            int size = Mathf.Clamp(Mathf.RoundToInt(region.height * t.Size), 6, 1200);
            FontStyle style = t.Bold ? FontStyle.Bold : FontStyle.Normal;
            font.RequestCharactersInTexture(t.Text + "Hg", size, style);
            float textWidth = 0f;
            foreach (char ch in t.Text)
            {
                if (font.GetCharacterInfo(ch, out CharacterInfo info, size, style))
                {
                    textWidth += info.advance;
                }
            }
            font.GetCharacterInfo('H', out CharacterInfo cap, size, style);
            font.GetCharacterInfo('g', out CharacterInfo desc, size, style);
            int pad = Mathf.CeilToInt(size * 0.5f);
            int rw = Mathf.CeilToInt(textWidth) + pad * 2;
            int rh = cap.maxY - desc.minY + pad * 2;
            if (rw <= 0 || rh <= 0)
            {
                return;
            }
            int baseline = pad - desc.minY;
            Texture2D coverage = RenderCoverage(font, t.Text, size, style, rw, rh, pad, baseline);
            if (coverage == null)
            {
                return;
            }
            try
            {
                Color32[] cov = coverage.GetPixels32();
                float cx = region.x + t.X * region.width;
                float cy = region.y + (1f - t.Y) * region.height;
                float left = t.Align == 0 ? cx : (t.Align == 2 ? cx - textWidth : cx - textWidth * 0.5f);
                int ox = Mathf.RoundToInt(left) - pad;
                int oy = Mathf.RoundToInt(cy - (cap.maxY + desc.minY) * 0.5f) - baseline;
                if (t.Shadow)
                {
                    int o = Mathf.Max(1, Mathf.RoundToInt(size * 0.06f));
                    Color shadow = t.Color == 1 ? new Color(1f, 1f, 1f, 0.6f) : new Color(0f, 0f, 0f, 0.65f);
                    Stamp(dst, width, region, cov, rw, rh, ox + o, oy - o, shadow, shadow.a * t.Opacity);
                }
                Stamp(dst, width, region, cov, rw, rh, ox, oy, Colors[Mathf.Clamp(t.Color, 0, Colors.Length - 1)], t.Opacity);
            }
            finally
            {
                UnityEngine.Object.Destroy(coverage);
            }
        }

        private static Texture2D RenderCoverage(Font font, string text, int size, FontStyle style, int rw, int rh, int penX, int baseline)
        {
            Material mat = font.material;
            if (mat == null)
            {
                return null;
            }
            RenderTexture rt = RenderTexture.GetTemporary(rw, rh, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture prev = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
                GL.PushMatrix();
                GL.LoadPixelMatrix(0f, rw, 0f, rh);
                mat.SetPass(0);
                GL.Begin(GL.QUADS);
                GL.Color(Color.white);
                float pen = penX;
                foreach (char ch in text)
                {
                    if (!font.GetCharacterInfo(ch, out CharacterInfo g, size, style))
                    {
                        continue;
                    }
                    float x0 = pen + g.minX;
                    float x1 = pen + g.maxX;
                    float y0 = baseline + g.minY;
                    float y1 = baseline + g.maxY;
                    GL.TexCoord(g.uvBottomLeft);
                    GL.Vertex3(x0, y0, 0f);
                    GL.TexCoord(g.uvTopLeft);
                    GL.Vertex3(x0, y1, 0f);
                    GL.TexCoord(g.uvTopRight);
                    GL.Vertex3(x1, y1, 0f);
                    GL.TexCoord(g.uvBottomRight);
                    GL.Vertex3(x1, y0, 0f);
                    pen += g.advance;
                }
                GL.End();
                GL.PopMatrix();
                var tex = new Texture2D(rw, rh, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, rw, rh), 0, 0, false);
                tex.Apply(false);
                return tex;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static void Stamp(Color32[] dst, int width, RectInt region, Color32[] cov, int rw, int rh, int ox, int oy, Color c, float opacity)
        {
            int x0 = Mathf.Max(ox, region.x);
            int x1 = Mathf.Min(ox + rw, region.xMax);
            int y0 = Mathf.Max(oy, region.y);
            int y1 = Mathf.Min(oy + rh, region.yMax);
            for (int y = y0; y < y1; y++)
            {
                int srcRow = (y - oy) * rw;
                int dstRow = y * width;
                for (int x = x0; x < x1; x++)
                {
                    float a = cov[srcRow + x - ox].r / 255f * opacity;
                    if (a > 0f)
                    {
                        Blend(ref dst[dstRow + x], c, a);
                    }
                }
            }
        }

        private static void Blend(ref Color32 p, Color c, float a)
        {
            p.r = (byte)Mathf.RoundToInt(Mathf.Lerp(p.r, c.r * 255f, a));
            p.g = (byte)Mathf.RoundToInt(Mathf.Lerp(p.g, c.g * 255f, a));
            p.b = (byte)Mathf.RoundToInt(Mathf.Lerp(p.b, c.b * 255f, a));
        }

        private static void Fill(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
        }

        public void Dispose()
        {
            if (_maskTex != null)
            {
                UnityEngine.Object.Destroy(_maskTex);
                _maskTex = null;
            }
        }
    }
}
