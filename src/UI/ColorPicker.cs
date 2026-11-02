using System.Collections.Generic;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed class ColorPicker
    {
        private static readonly string[] FormatNames = { "HEX", "RGB", "HSL" };

        private static Texture2D _hueTex;
        private static Texture2D _alphaTex;
        private static Texture2D _checker;

        private Texture2D _svTex;
        private float _svHue = -1f;
        private float _h;
        private float _s;
        private float _v;
        private float _a = 1f;
        private int _drag;
        private int _format;
        private string _text = "";
        private readonly string _control;

        public ColorPicker(string control)
        {
            _control = HudKit.FieldPrefix + control;
        }

        public Color Color
        {
            get
            {
                Color c = Color.HSVToRGB(_h, _s, _v);
                c.a = _a;
                return c;
            }
        }

        public void SetColor(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (s > 0.001f && v > 0.001f)
            {
                _h = h;
            }
            _s = s;
            _v = v;
            _a = c.a;
            _text = Format(c);
        }

        private string Format(Color c) => _format == 1 ? ThemeColors.ToRgb(c) : _format == 2 ? ThemeColors.ToHsl(c) : ThemeColors.ToHex(c);

        public bool Draw(Rect r, IList<Color> swatches)
        {
            Ensure();
            bool changed = false;
            Event e = Event.current;
            var sv = new Rect(r.x, r.y, r.width, Mathf.Round(r.width * 0.62f));
            var hue = new Rect(r.x, sv.yMax + 12f, r.width, 14f);
            var alpha = new Rect(r.x, hue.yMax + 10f, r.width, 14f);
            var field = new Rect(r.x, alpha.yMax + 14f, r.width - 70f, 32f);
            var format = new Rect(field.xMax + 6f, field.y, 64f, 32f);

            if (_svTex == null || Mathf.Abs(_svHue - _h) > 0.0005f)
            {
                BuildSv(_h);
            }
            if (e.type == EventType.Repaint)
            {
                GUI.DrawTexture(sv, _svTex, ScaleMode.StretchToFill);
                HudTheme.Border(sv, HudTheme.P.Line);
                Ring(new Vector2(sv.x + _s * sv.width, sv.y + (1f - _v) * sv.height));
                GUI.DrawTexture(hue, _hueTex, ScaleMode.StretchToFill);
                Thumb(new Rect(hue.x + _h * hue.width - 3f, hue.y - 3f, 6f, hue.height + 6f));
                GUI.DrawTextureWithTexCoords(alpha, _checker, new Rect(0f, 0f, alpha.width / 8f, alpha.height / 8f));
                Color prev = GUI.color;
                Color solid = Color;
                solid.a = 1f;
                GUI.color = solid;
                GUI.DrawTexture(alpha, _alphaTex, ScaleMode.StretchToFill, true);
                GUI.color = prev;
                HudTheme.Border(alpha, HudTheme.P.Line);
                Thumb(new Rect(alpha.x + _a * alpha.width - 3f, alpha.y - 3f, 6f, alpha.height + 6f));
            }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                _drag = sv.Contains(e.mousePosition) ? 1 : hue.Contains(e.mousePosition) ? 2 : alpha.Contains(e.mousePosition) ? 3 : 0;
            }
            if (_drag != 0 && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
            {
                switch (_drag)
                {
                    case 1:
                        _s = Mathf.Clamp01((e.mousePosition.x - sv.x) / sv.width);
                        _v = 1f - Mathf.Clamp01((e.mousePosition.y - sv.y) / sv.height);
                        break;
                    case 2:
                        _h = Mathf.Clamp(Mathf.Clamp01((e.mousePosition.x - hue.x) / hue.width), 0f, 0.9999f);
                        break;
                    case 3:
                        _a = Mathf.Clamp01((e.mousePosition.x - alpha.x) / alpha.width);
                        break;
                }
                _text = Format(Color);
                changed = true;
                e.Use();
            }
            if (e.rawType == EventType.MouseUp)
            {
                _drag = 0;
            }

            bool focused = GUI.GetNameOfFocusedControl() == _control;
            if (!focused && _drag == 0 && e.type == EventType.Layout)
            {
                _text = Format(Color);
            }
            GUI.SetNextControlName(_control);
            string text = GUI.TextField(new Rect(field.x, field.y, field.width, field.height), _text ?? "", 64, HudTheme.Field);
            if (text != _text)
            {
                _text = text;
                if (ThemeColors.TryParse(text, out Color parsed))
                {
                    string keep = _text;
                    SetColor(parsed);
                    _text = keep;
                    changed = true;
                }
            }
            if (SpawnerWindow.Button(format, FormatNames[_format], false))
            {
                _format = (_format + 1) % FormatNames.Length;
                _text = Format(Color);
            }

            if (swatches != null && swatches.Count > 0)
            {
                float y = field.yMax + 12f;
                float size = Mathf.Min(34f, (r.width - (swatches.Count - 1) * 6f) / swatches.Count);
                for (int i = 0; i < swatches.Count; i++)
                {
                    var box = new Rect(r.x + i * (size + 6f), y, size, size);
                    Swatch(box, swatches[i]);
                    if (GUI.Button(box, new GUIContent("", ThemeColors.ToHex(swatches[i])), GUIStyle.none))
                    {
                        SetColor(swatches[i]);
                        changed = true;
                    }
                }
            }
            return changed;
        }

        public static float Height(float width) => Mathf.Round(width * 0.62f) + 12f + 14f + 10f + 14f + 14f + 32f + 12f + 34f;

        public static void Swatch(Rect r, Color c)
        {
            Ensure();
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            GUI.DrawTextureWithTexCoords(r, _checker, new Rect(0f, 0f, r.width / 8f, r.height / 8f));
            HudTheme.Fill(r, c);
            HudTheme.Border(r, HudTheme.P.Line);
        }

        private static void Ring(Vector2 p)
        {
            var outer = new Rect(p.x - 6f, p.y - 6f, 12f, 12f);
            HudTheme.Border(outer, Color.black, 2f);
            HudTheme.Border(new Rect(outer.x + 2f, outer.y + 2f, 8f, 8f), Color.white, 2f);
        }

        private static void Thumb(Rect r)
        {
            HudTheme.Fill(r, Color.white);
            HudTheme.Border(r, Color.black);
        }

        private void BuildSv(float h)
        {
            const int size = 64;
            if (_svTex == null)
            {
                _svTex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            }
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    px[y * size + x] = Color.HSVToRGB(h, x / (size - 1f), y / (size - 1f));
                }
            }
            _svTex.SetPixels32(px);
            _svTex.Apply(false);
            _svHue = h;
        }

        private static void Ensure()
        {
            if (_hueTex == null)
            {
                _hueTex = new Texture2D(256, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                for (int x = 0; x < 256; x++)
                {
                    _hueTex.SetPixel(x, 0, Color.HSVToRGB(x / 255f, 1f, 1f));
                }
                _hueTex.Apply(false);
            }
            if (_alphaTex == null)
            {
                _alphaTex = new Texture2D(256, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                for (int x = 0; x < 256; x++)
                {
                    _alphaTex.SetPixel(x, 0, new Color(1f, 1f, 1f, x / 255f));
                }
                _alphaTex.Apply(false);
            }
            if (_checker == null)
            {
                _checker = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
                Color light = new Color(0.55f, 0.55f, 0.58f);
                Color dark = new Color(0.32f, 0.32f, 0.35f);
                _checker.SetPixels(new[] { light, dark, dark, light });
                _checker.Apply(false);
            }
        }

        public void Dispose()
        {
            if (_svTex != null)
            {
                UnityEngine.Object.Destroy(_svTex);
                _svTex = null;
            }
        }
    }
}
