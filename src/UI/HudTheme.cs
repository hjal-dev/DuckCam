using System;
using System.Linq;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.UI
{
    public enum UiPalette
    {
        NeonRed,
        Acid,
        Ice,
        Tarkov
    }

    internal sealed class HudPalette
    {
        public Color Label;
        public Color Value;
        public Color Dim;
        public Color Line;
        public Color LineBright;
        public Color RowFill;
        public Color RowFillSelected;
        public Color TabFill;
        public Color TabFillActive;
        public Color ValueBox;
        public Color ValueBoxText;
        public Color Highlight;
        public Color HighlightText;
        public Color KeyCap;
        public Color Hud;
    }

    internal static class HudTheme
    {
        public static HudPalette P { get; private set; }

        public static Font Regular { get; private set; }
        public static Font Bold { get; private set; }
        public static Font Mono { get; private set; }
        public static string FontSource { get; private set; } = "not loaded";

        public static Texture2D White;
        public static Texture2D TriLeft;
        public static Texture2D TriLeftHollow;
        public static Texture2D TriRight;
        public static Texture2D TriRightHollow;
        public static Texture2D Notch;
        public static Texture2D DotH;
        public static Texture2D DotV;
        public static Texture2D Chip;
        public static Texture2D GlowH;
        public static Texture2D GlowV;
        public static Texture2D[] Mouse;
        public static Texture2D ArrowUp;
        public static Texture2D ArrowDown;
        public static Texture2D ArrowLeft;
        public static Texture2D ArrowRight;

        private static readonly System.Collections.Generic.Dictionary<string, Texture2D> _icons = new System.Collections.Generic.Dictionary<string, Texture2D>();

        public static Texture2D Icon(string name) => _icons.TryGetValue(name, out Texture2D t) ? t : White;

        public static GUIStyle Label;
        public static GUIStyle LabelSelected;
        public static GUIStyle Value;
        public static GUIStyle ValueSelected;
        public static GUIStyle BoxText;
        public static GUIStyle BoxTextHi;
        public static GUIStyle Tab;
        public static GUIStyle TabActive;
        public static GUIStyle Legend;
        public static GUIStyle KeyCap;
        public static GUIStyle HudSmall;
        public static GUIStyle HudBrand;
        public static GUIStyle Note;
        public static GUIStyle NoteMono;
        public static GUIStyle Field;
        public static GUIStyle Section;
        public static GUIStyle Message;
        public static GUIStyle Title;
        public static GUIStyle GridLabel;

        private static bool _assetsReady;
        private static UiPalette _builtFor = (UiPalette)(-1);

        public static void Ensure()
        {
            if (!_assetsReady)
            {
                _assetsReady = true;
                LoadFonts();
                BuildTextures();
            }
            UiPalette wanted = DuckCamConfig.Palette.Value;
            if (wanted != _builtFor)
            {
                _builtFor = wanted;
                P = PaletteFor(wanted);
                BuildStyles();
            }
        }

        public static HudPalette PaletteFor(UiPalette palette)
        {
            switch (palette)
            {
                case UiPalette.Acid:
                    return new HudPalette
                    {
                        Label = new Color(0.90f, 0.89f, 0.36f), Value = new Color(0.95f, 0.94f, 0.72f), Dim = new Color(0.90f, 0.89f, 0.36f, 0.5f),
                        Line = new Color(0.90f, 0.89f, 0.36f, 0.25f), LineBright = new Color(0.92f, 0.92f, 0.40f, 0.95f),
                        RowFill = new Color(0.05f, 0.05f, 0.01f, 0.80f), RowFillSelected = new Color(0.22f, 0.22f, 0.05f, 0.75f),
                        TabFill = new Color(0.05f, 0.05f, 0.01f, 0.82f), TabFillActive = new Color(0.30f, 0.30f, 0.06f, 0.80f),
                        ValueBox = new Color(0.34f, 0.35f, 0.08f, 0.95f), ValueBoxText = new Color(0.96f, 0.94f, 0.50f),
                        Highlight = new Color(0.38f, 0.55f, 1f), HighlightText = Color.white, KeyCap = new Color(0.40f, 0.62f, 1f),
                        Hud = new Color(0.90f, 0.89f, 0.36f, 0.7f)
                    };
                case UiPalette.Ice:
                    return new HudPalette
                    {
                        Label = new Color(0.37f, 0.94f, 1f), Value = new Color(0.80f, 0.97f, 1f), Dim = new Color(0.37f, 0.94f, 1f, 0.5f),
                        Line = new Color(0.37f, 0.94f, 1f, 0.25f), LineBright = new Color(0.45f, 0.97f, 1f, 0.95f),
                        RowFill = new Color(0.01f, 0.05f, 0.07f, 0.80f), RowFillSelected = new Color(0.04f, 0.22f, 0.27f, 0.75f),
                        TabFill = new Color(0.01f, 0.05f, 0.07f, 0.82f), TabFillActive = new Color(0.05f, 0.28f, 0.33f, 0.80f),
                        ValueBox = new Color(0.06f, 0.30f, 0.36f, 0.95f), ValueBoxText = new Color(0.55f, 0.97f, 1f),
                        Highlight = new Color(1f, 0.38f, 0.34f), HighlightText = new Color(0.15f, 0.02f, 0.02f), KeyCap = new Color(1f, 0.45f, 0.40f),
                        Hud = new Color(0.37f, 0.94f, 1f, 0.7f)
                    };
                case UiPalette.Tarkov:
                    return new HudPalette
                    {
                        Label = new Color(0.80f, 0.71f, 0.50f), Value = new Color(0.90f, 0.88f, 0.82f), Dim = new Color(0.80f, 0.71f, 0.50f, 0.5f),
                        Line = new Color(0.80f, 0.71f, 0.50f, 0.25f), LineBright = new Color(0.86f, 0.78f, 0.58f, 0.95f),
                        RowFill = new Color(0.04f, 0.04f, 0.04f, 0.82f), RowFillSelected = new Color(0.18f, 0.16f, 0.11f, 0.80f),
                        TabFill = new Color(0.04f, 0.04f, 0.04f, 0.85f), TabFillActive = new Color(0.24f, 0.21f, 0.14f, 0.85f),
                        ValueBox = new Color(0.28f, 0.25f, 0.17f, 0.95f), ValueBoxText = new Color(0.92f, 0.84f, 0.62f),
                        Highlight = new Color(0.90f, 0.86f, 0.74f), HighlightText = new Color(0.08f, 0.08f, 0.06f), KeyCap = new Color(0.88f, 0.86f, 0.80f),
                        Hud = new Color(0.80f, 0.71f, 0.50f, 0.7f)
                    };
                default:
                    return new HudPalette
                    {
                        Label = new Color(1f, 0.38f, 0.34f), Value = new Color(1f, 0.84f, 0.80f), Dim = new Color(1f, 0.38f, 0.34f, 0.5f),
                        Line = new Color(1f, 0.33f, 0.29f, 0.26f), LineBright = new Color(1f, 0.36f, 0.31f, 0.95f),
                        RowFill = new Color(0.09f, 0.02f, 0.03f, 0.80f), RowFillSelected = new Color(0.30f, 0.06f, 0.07f, 0.72f),
                        TabFill = new Color(0.08f, 0.02f, 0.03f, 0.82f), TabFillActive = new Color(0.40f, 0.08f, 0.09f, 0.78f),
                        ValueBox = new Color(0.42f, 0.11f, 0.11f, 0.95f), ValueBoxText = new Color(1f, 0.45f, 0.40f),
                        Highlight = new Color(0.37f, 0.96f, 1f), HighlightText = new Color(0.03f, 0.15f, 0.17f), KeyCap = new Color(0.37f, 0.96f, 1f),
                        Hud = new Color(1f, 0.33f, 0.29f, 0.72f)
                    };
            }
        }

        public static void Fill(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, White);
            GUI.color = prev;
        }

        public static void Tex(Rect r, Texture2D tex, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, tex);
            GUI.color = prev;
        }

        public static void Border(Rect r, Color c, float w = 1f)
        {
            Fill(new Rect(r.x, r.y, r.width, w), c);
            Fill(new Rect(r.x, r.yMax - w, r.width, w), c);
            Fill(new Rect(r.x, r.y, w, r.height), c);
            Fill(new Rect(r.xMax - w, r.y, w, r.height), c);
        }

        public static void SelectedFrame(Rect r, Color c)
        {
            Border(r, c);
            Tex(new Rect(r.xMax - 9f, r.yMax - 9f, 9f, 9f), Notch, c);
        }

        public static void DottedH(float x0, float x1, float y, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTextureWithTexCoords(new Rect(x0, Mathf.Round(y), x1 - x0, 1f), DotH, new Rect(0f, 0f, (x1 - x0) / 8f, 1f));
            GUI.color = prev;
        }

        public static void DottedV(float x, float y0, float y1, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTextureWithTexCoords(new Rect(Mathf.Round(x), y0, 1f, y1 - y0), DotV, new Rect(0f, 0f, 1f, (y1 - y0) / 8f));
            GUI.color = prev;
        }

        public static float DrawKeyCap(float x, float y, string key, float height = 22f)
        {
            float w = Mathf.Max(height, KeyCap.CalcSize(new GUIContent(key)).x + 10f);
            var r = new Rect(x, y, w, height);
            Fill(r, new Color(P.KeyCap.r, P.KeyCap.g, P.KeyCap.b, 0.12f));
            Border(r, P.KeyCap);
            GUI.Label(r, key, KeyCap);
            return w;
        }

        public static float DrawGlyphCap(float x, float y, Texture2D glyph, float size = 22f, bool outlined = true)
        {
            var r = new Rect(x, y, size, size);
            if (outlined)
            {
                Fill(r, new Color(P.KeyCap.r, P.KeyCap.g, P.KeyCap.b, 0.12f));
                Border(r, P.KeyCap);
            }
            Tex(new Rect(r.x + 3f, r.y + 3f, size - 6f, size - 6f), glyph, P.KeyCap);
            return size;
        }

        public static void DrawEdgeGlow(float width, float height, float thickness)
        {
            Color c = new Color(P.LineBright.r, P.LineBright.g, P.LineBright.b, 0.55f);
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(0f, 0f, thickness, height), GlowH);
            GUI.DrawTextureWithTexCoords(new Rect(width - thickness, 0f, thickness, height), GlowH, new Rect(1f, 0f, -1f, 1f));
            GUI.DrawTexture(new Rect(0f, 0f, width, thickness), GlowV);
            GUI.DrawTextureWithTexCoords(new Rect(0f, height - thickness, width, thickness), GlowV, new Rect(0f, 1f, 1f, -1f));
            GUI.color = prev;
        }

        private static void BuildStyles()
        {
            Label = TextStyle(17, Bold, P.Label, TextAnchor.MiddleLeft);
            LabelSelected = TextStyle(17, Bold, P.Value, TextAnchor.MiddleLeft);
            Value = TextStyle(16, Regular, P.Value, TextAnchor.MiddleCenter);
            ValueSelected = TextStyle(16, Bold, P.Highlight, TextAnchor.MiddleCenter);
            BoxText = TextStyle(15, Bold, P.ValueBoxText, TextAnchor.MiddleCenter);
            BoxTextHi = TextStyle(15, Bold, P.HighlightText, TextAnchor.MiddleCenter);
            Tab = TextStyle(13, Bold, P.Label, TextAnchor.MiddleLeft);
            TabActive = TextStyle(13, Bold, P.Value, TextAnchor.MiddleLeft);
            Legend = TextStyle(17, Regular, P.Label, TextAnchor.MiddleLeft);
            KeyCap = TextStyle(12, Bold, P.KeyCap, TextAnchor.MiddleCenter);
            HudSmall = TextStyle(13, Mono ?? Regular, P.Hud, TextAnchor.MiddleCenter);
            HudBrand = TextStyle(34, Bold, P.Hud, TextAnchor.LowerLeft);
            Color noteColor = Color.Lerp(P.Label, P.Value, 0.45f);
            noteColor.a = 0.95f;
            Note = TextStyle(14, Bold, noteColor, TextAnchor.UpperLeft);
            Note.wordWrap = true;
            Note.padding = new RectOffset(14, 14, 2, 4);
            NoteMono = TextStyle(12, Mono ?? Bold, noteColor, TextAnchor.UpperLeft);
            NoteMono.wordWrap = true;
            NoteMono.padding = new RectOffset(14, 14, 2, 4);
            Section = TextStyle(13, Bold, new Color(P.Label.r, P.Label.g, P.Label.b, 0.9f), TextAnchor.LowerLeft);
            Section.padding = new RectOffset(2, 2, 8, 2);
            Message = TextStyle(15, Regular, P.Value, TextAnchor.MiddleRight);
            Title = TextStyle(30, Regular, P.Highlight, TextAnchor.MiddleLeft);
            GridLabel = TextStyle(9, Mono ?? Regular, P.Hud, TextAnchor.UpperLeft);

            Field = TextStyle(15, Regular, P.Value, TextAnchor.MiddleCenter);
            Field.normal.background = Solid(new Color(0f, 0f, 0f, 0.45f));
            Field.focused.background = Solid(new Color(P.Highlight.r, P.Highlight.g, P.Highlight.b, 0.25f));
            Field.focused.textColor = Color.white;
            Field.clipping = TextClipping.Clip;
        }

        private static GUIStyle TextStyle(int size, Font font, Color color, TextAnchor anchor)
        {
            return new GUIStyle
            {
                font = font,
                fontSize = size,
                alignment = anchor,
                richText = false,
                normal = { textColor = color },
                padding = new RectOffset(2, 2, 1, 1),
                margin = new RectOffset(0, 0, 0, 0),
                clipping = TextClipping.Overflow
            };
        }

        private static void LoadFonts()
        {
            try
            {
                Font[] fonts = Resources.FindObjectsOfTypeAll<Font>();
                Regular = fonts.FirstOrDefault(f => f.name.Equals("Bender", StringComparison.OrdinalIgnoreCase));
                Bold = fonts.FirstOrDefault(f => f.name.StartsWith("Bender", StringComparison.OrdinalIgnoreCase)
                                                 && f.name.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0
                                                 && f.name.IndexOf("Italic", StringComparison.OrdinalIgnoreCase) < 0);
                if (Regular != null)
                {
                    Bold = Bold ?? Regular;
                    FontSource = $"game font ({Regular.name} / {Bold.name})";
                }
            }
            catch (Exception ex)
            {
                PhotoLog.Debug("Font scan failed: " + ex.Message);
            }

            try
            {
                if (Regular == null)
                {
                    Regular = Font.CreateDynamicFontFromOSFont(new[] { "Bahnschrift SemiCondensed", "Bahnschrift", "Segoe UI" }, 16);
                    Bold = Font.CreateDynamicFontFromOSFont(new[] { "Bahnschrift SemiBold SemiConden", "Bahnschrift SemiBold", "Bahnschrift", "Segoe UI Semibold" }, 16);
                    FontSource = "OS font (Bahnschrift; the game's Bender font is not loaded as a Unity Font)";
                }
                Mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Lucida Console", "Courier New" }, 13);
            }
            catch (Exception ex)
            {
                FontSource = "Unity default (" + ex.Message + ")";
            }
        }

        private static void BuildTextures()
        {
            White = Solid(Color.white);
            TriLeft = Triangle(left: true, hollow: false);
            TriLeftHollow = Triangle(left: true, hollow: true);
            TriRight = Triangle(left: false, hollow: false);
            TriRightHollow = Triangle(left: false, hollow: true);
            Notch = Raster(16, (x, y) => x + y >= 15.5f ? 1f : 0f);

            DotH = new Texture2D(8, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            DotV = new Texture2D(1, 8, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            for (int i = 0; i < 8; i++)
            {
                Color c = i < 3 ? Color.white : new Color(1f, 1f, 1f, 0f);
                DotH.SetPixel(i, 0, c);
                DotV.SetPixel(0, i, c);
            }
            DotH.Apply();
            DotV.Apply();

            _icons["camera"] = Paint(p => { p.Line(8, 3, 8, 22, 2.4f); p.Line(8, 22, 27, 22, 2.4f); p.Line(3, 8, 22, 8, 2.4f); p.Line(22, 8, 22, 28, 2.4f); });
            _icons["motion"] = Paint(p => { p.Line(16, 4, 16, 28, 2.2f); p.Line(4, 16, 28, 16, 2.2f); p.Line(16, 4, 12, 8, 2.2f); p.Line(16, 4, 20, 8, 2.2f); p.Line(16, 28, 12, 24, 2.2f); p.Line(16, 28, 20, 24, 2.2f); p.Line(4, 16, 8, 12, 2.2f); p.Line(4, 16, 8, 20, 2.2f); p.Line(28, 16, 24, 12, 2.2f); p.Line(28, 16, 24, 20, 2.2f); });
            _icons["time"] = Paint(p => { p.Ring(16, 16, 11, 2.4f); p.Line(16, 16, 16, 9, 2.4f); p.Line(16, 16, 21, 19, 2.4f); });
            _icons["paths"] = Paint(p => { p.Line(5, 24, 12, 13, 2f); p.Line(12, 13, 20, 18, 2f); p.Line(20, 18, 27, 7, 2f); p.Disc(5, 24, 3.2f); p.Disc(12, 13, 3.2f); p.Disc(20, 18, 3.2f); p.Disc(27, 7, 3.2f); });
            _icons["dof"] = Paint(p => { p.Ring(16, 16, 11, 2.2f); for (int i = 0; i < 6; i++) { float a = i * Mathf.PI / 3f; p.Line(16 + Mathf.Cos(a) * 4f, 16 + Mathf.Sin(a) * 4f, 16 + Mathf.Cos(a + 1.2f) * 11f, 16 + Mathf.Sin(a + 1.2f) * 11f, 2f); } });
            _icons["effects"] = Paint(p => { p.Ring(16, 16, 11, 2.4f); for (int y = 6; y <= 26; y++) { float h = Mathf.Sqrt(Mathf.Max(0f, 121f - (y - 16f) * (y - 16f))); p.Line(16, y, 16 + h - 1f, y, 1.2f); } });
            _icons["lights"] = Paint(p => { p.Ring(16, 12, 7, 2.2f); p.Line(13, 19, 13, 25, 2.2f); p.Line(19, 19, 19, 25, 2.2f); p.Line(13, 25, 19, 25, 2.2f); p.Line(14, 28, 18, 28, 2.2f); });
            _icons["props"] = Paint(p => { p.Box(6, 11, 21, 26, 2.2f); p.Box(11, 6, 26, 21, 2.2f); p.Line(6, 11, 11, 6, 2.2f); p.Line(21, 11, 26, 6, 2.2f); p.Line(6, 26, 11, 21, 2.2f); p.Line(21, 26, 26, 21, 2.2f); });
            _icons["environment"] = Paint(p => { p.Disc(11, 11, 4.5f); for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4f; p.Line(11 + Mathf.Cos(a) * 7f, 11 + Mathf.Sin(a) * 7f, 11 + Mathf.Cos(a) * 9.5f, 11 + Mathf.Sin(a) * 9.5f, 1.8f); } p.Ring(19, 21, 6, 2.2f); p.Ring(12, 23, 4.5f, 2.2f); p.Line(8, 27, 25, 27, 2.2f); });
            _icons["characters"] = Paint(p => { p.Disc(16, 10, 5.5f); p.Ring(16, 31, 12, 2.6f); });
            _icons["outfit"] = Paint(p => { p.Line(12, 5, 4, 11, 2.2f); p.Line(4, 11, 8, 16, 2.2f); p.Line(8, 16, 10, 14, 2.2f); p.Line(10, 14, 10, 27, 2.2f); p.Line(10, 27, 22, 27, 2.2f); p.Line(22, 27, 22, 14, 2.2f); p.Line(22, 14, 24, 16, 2.2f); p.Line(24, 16, 28, 11, 2.2f); p.Line(28, 11, 20, 5, 2.2f); p.Line(12, 5, 16, 9, 2.2f); p.Line(16, 9, 20, 5, 2.2f); });
            _icons["overlays"] = Paint(p => { p.Box(9, 4, 28, 23, 2.2f); p.Box(4, 9, 23, 28, 2.2f); p.Line(16, 13, 18, 17, 2f); p.Line(18, 17, 22, 17, 2f); p.Line(22, 17, 19, 20, 2f); });
            _icons["frame"] = Paint(p => { p.Box(3, 7, 29, 25, 2.2f); p.Box(9, 11, 23, 21, 1.6f); });
            _icons["presets"] = Paint(p => { p.Box(5, 6, 27, 27, 2.2f); p.Line(10, 6, 10, 13, 2.2f); p.Line(10, 13, 22, 13, 2.2f); p.Line(22, 13, 22, 6, 2.2f); p.Line(10, 19, 22, 19, 2f); p.Line(10, 23, 18, 23, 2f); });
            _icons["addons"] = Paint(p => { p.Box(4, 9, 22, 27, 2.2f); p.Line(25, 4, 25, 16, 2.4f); p.Line(19, 10, 31, 10, 2.4f); p.Line(9, 16, 17, 16, 2f); p.Line(9, 21, 15, 21, 2f); });
            _icons["capture"] = Paint(p => { p.Box(4, 10, 28, 26, 2.2f); p.Ring(16, 18, 5, 2.2f); p.Line(11, 10, 13, 6, 2.2f); p.Line(13, 6, 19, 6, 2.2f); p.Line(19, 6, 21, 10, 2.2f); });
            _icons["debug"] = Paint(p => { p.Box(3, 6, 29, 26, 2f); p.Line(8, 12, 13, 16, 2.2f); p.Line(13, 16, 8, 20, 2.2f); p.Line(16, 21, 23, 21, 2.2f); });

            Chip = Paint(p => { p.Line(4, 9, 26, 9, 2.2f); p.Line(4, 15, 20, 15, 2.2f); p.Line(4, 21, 24, 21, 2.2f); p.Line(4, 27, 14, 27, 2.2f); });

            Mouse = new[]
            {
                MouseGlyph(0), MouseGlyph(1), MouseGlyph(2), MouseGlyph(3)
            };
            ArrowUp = Paint(p => { p.Line(16, 6, 16, 26, 2.6f); p.Line(16, 6, 9, 13, 2.6f); p.Line(16, 6, 23, 13, 2.6f); });
            ArrowDown = Paint(p => { p.Line(16, 6, 16, 26, 2.6f); p.Line(16, 26, 9, 19, 2.6f); p.Line(16, 26, 23, 19, 2.6f); });
            ArrowLeft = Paint(p => { p.Line(6, 16, 26, 16, 2.6f); p.Line(6, 16, 13, 9, 2.6f); p.Line(6, 16, 13, 23, 2.6f); });
            ArrowRight = Paint(p => { p.Line(6, 16, 26, 16, 2.6f); p.Line(26, 16, 19, 9, 2.6f); p.Line(26, 16, 19, 23, 2.6f); });

            GlowH = new Texture2D(32, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            GlowV = new Texture2D(1, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (int i = 0; i < 32; i++)
            {
                float a = Mathf.Pow(1f - i / 31f, 2.2f);
                GlowH.SetPixel(i, 0, new Color(1f, 1f, 1f, a));
                GlowV.SetPixel(0, 31 - i, new Color(1f, 1f, 1f, a));
            }
            GlowH.Apply();
            GlowV.Apply();
        }

        private static Texture2D MouseGlyph(int highlight)
        {
            return Paint(p =>
            {
                p.Line(9, 12, 9, 22, 2f);
                p.Line(23, 12, 23, 22, 2f);
                p.Ring(16, 12, 7, 2f, upperHalfOnly: true);
                p.Ring(16, 22, 7, 2f, lowerHalfOnly: true);
                p.Line(9, 13, 23, 13, 1.6f);
                p.Line(16, 5, 16, 13, 1.6f);
                if (highlight == 1) p.FillWhere((x, y) => x < 15.2f && y < 12.8f && (x - 16f) * (x - 16f) + (y - 12f) * (y - 12f) < 42f);
                if (highlight == 2) p.FillWhere((x, y) => x > 16.8f && y < 12.8f && (x - 16f) * (x - 16f) + (y - 12f) * (y - 12f) < 42f);
                if (highlight == 3) p.Line(16, 7, 16, 11, 3.2f);
            });
        }

        private sealed class Painter
        {
            public readonly float[,] A = new float[32, 32];

            private void Plot(Func<float, float, float> distance, float halfWidth)
            {
                for (int y = 0; y < 32; y++)
                {
                    for (int x = 0; x < 32; x++)
                    {
                        float d = distance(x + 0.5f, y + 0.5f);
                        A[x, y] = Mathf.Max(A[x, y], Mathf.Clamp01(halfWidth + 0.5f - d));
                    }
                }
            }

            public void Line(float x0, float y0, float x1, float y1, float w)
            {
                Plot((x, y) => SegmentDistance(x, y, x0, y0, x1, y1), w * 0.5f);
            }

            public void Ring(float cx, float cy, float r, float w, bool upperHalfOnly = false, bool lowerHalfOnly = false)
            {
                Plot((x, y) =>
                {
                    if ((upperHalfOnly && y > cy) || (lowerHalfOnly && y < cy))
                    {
                        return float.MaxValue;
                    }
                    return Mathf.Abs(Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r);
                }, w * 0.5f);
            }

            public void FillWhere(Func<float, float, bool> inside)
            {
                for (int y = 0; y < 32; y++)
                {
                    for (int x = 0; x < 32; x++)
                    {
                        if (inside(x + 0.5f, y + 0.5f))
                        {
                            A[x, y] = 1f;
                        }
                    }
                }
            }

            public void Disc(float cx, float cy, float r)
            {
                Plot((x, y) => Mathf.Max(0f, Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r), 0f);
            }

            public void Box(float x0, float y0, float x1, float y1, float w)
            {
                Line(x0, y0, x1, y0, w);
                Line(x1, y0, x1, y1, w);
                Line(x1, y1, x0, y1, w);
                Line(x0, y1, x0, y0, w);
            }
        }

        private static Texture2D Paint(Action<Painter> paint)
        {
            var p = new Painter();
            paint(p);
            return Raster(32, (x, y) => p.A[x, y]);
        }

        private static Texture2D Triangle(bool left, bool hollow)
        {
            Vector2 a = left ? new Vector2(4f, 16f) : new Vector2(28f, 16f);
            Vector2 b = left ? new Vector2(28f, 5f) : new Vector2(4f, 5f);
            Vector2 c = left ? new Vector2(28f, 27f) : new Vector2(4f, 27f);
            return Raster(32, (x, y) =>
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                float d = Mathf.Min(SegmentDistance(px, py, a.x, a.y, b.x, b.y), Mathf.Min(SegmentDistance(px, py, b.x, b.y, c.x, c.y), SegmentDistance(px, py, c.x, c.y, a.x, a.y)));
                bool inside = Inside(new Vector2(px, py), a, b, c);
                if (hollow)
                {
                    return Mathf.Clamp01(1.3f + 0.5f - d);
                }
                return inside ? 1f : Mathf.Clamp01(0.5f - d + 0.5f);
            });
        }

        private static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p, a, b);
            float d2 = Cross(p, b, c);
            float d3 = Cross(p, c, a);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0;
            bool pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        private static float Cross(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);

        private static float SegmentDistance(float px, float py, float x0, float y0, float x1, float y1)
        {
            float dx = x1 - x0;
            float dy = y1 - y0;
            float len2 = dx * dx + dy * dy;
            float t = len2 > 0f ? Mathf.Clamp01(((px - x0) * dx + (py - y0) * dy) / len2) : 0f;
            float cx = x0 + t * dx - px;
            float cy = y0 + t * dy - py;
            return Mathf.Sqrt(cx * cx + cy * cy);
        }

        private static Texture2D Raster(int size, Func<int, int, float> alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    tex.SetPixel(x, size - 1 - y, new Color(1f, 1f, 1f, alpha(x, y)));
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D Solid(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }
    }
}
