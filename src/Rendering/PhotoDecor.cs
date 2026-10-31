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
