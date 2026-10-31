using DuckCam.Core;
using DuckCam.UI;
using UnityEngine;

namespace DuckCam.Rendering
{
    public enum CompositionGuide
    {
        None,
        RuleOfThirds,
        GoldenRatio,
        CenterCross,
        Diagonals,
        SafeArea
    }

    public enum FrameAspect
    {
        Native,
        Ratio16x9,
        Ratio16x10,
        Ratio4x3,
        Ratio3x2,
        Ratio1x1,
        Ratio4x5,
        Ratio9x16,
        Ratio21x9,
        Ratio239x1
    }

    internal static class CompositionOverlay
    {
        public static readonly string[] GuideNames = { "None", "Rule of thirds", "Golden ratio", "Center cross", "Diagonals", "Safe area" };
        public static readonly string[] AspectNames = { "Native", "16:9", "16:10", "4:3", "3:2", "1:1", "4:5", "9:16", "21:9", "2.39:1" };

        public static float AspectValue(FrameAspect aspect)
        {
            switch (aspect)
            {
                case FrameAspect.Ratio16x9: return 16f / 9f;
                case FrameAspect.Ratio16x10: return 16f / 10f;
                case FrameAspect.Ratio4x3: return 4f / 3f;
                case FrameAspect.Ratio3x2: return 3f / 2f;
                case FrameAspect.Ratio1x1: return 1f;
                case FrameAspect.Ratio4x5: return 4f / 5f;
                case FrameAspect.Ratio9x16: return 9f / 16f;
                case FrameAspect.Ratio21x9: return 21f / 9f;
                case FrameAspect.Ratio239x1: return 2.39f;
                default: return 0f;
            }
        }

        public static RectInt FrameRect(int width, int height, FrameAspect aspect)
        {
            float a = AspectValue(aspect);
            if (a <= 0f)
            {
                return new RectInt(0, 0, width, height);
            }
            int w = width;
            int h = Mathf.RoundToInt(width / a);
            if (h > height)
            {
                h = height;
                w = Mathf.RoundToInt(height * a);
            }
            return new RectInt((width - w) / 2, (height - h) / 2, w, h);
        }

        public static Rect FrameScreenRect()
        {
            RectInt f = FrameRect(Screen.width, Screen.height, DuckCamConfig.FrameAspectRatio.Value);
            return new Rect(f.x, f.y, f.width, f.height);
        }

        public static void DrawLetterbox()
        {
            HudTheme.Ensure();
            if (DuckCamConfig.FrameAspectRatio.Value == FrameAspect.Native)
            {
                return;
            }
            Rect f = FrameScreenRect();
            var bar = new Color(0f, 0f, 0f, DuckCamConfig.LetterboxOpacity.Value);
            HudTheme.Fill(new Rect(0, 0, Screen.width, f.yMin), bar);
            HudTheme.Fill(new Rect(0, f.yMax, Screen.width, Screen.height - f.yMax), bar);
            HudTheme.Fill(new Rect(0, f.yMin, f.xMin, f.height), bar);
            HudTheme.Fill(new Rect(f.xMax, f.yMin, Screen.width - f.xMax, f.height), bar);
        }

        public static void DrawGuides()
        {
            HudTheme.Ensure();
            Rect f = FrameScreenRect();
            Color c = HudTheme.P.Hud;
            c.a *= DuckCamConfig.GuideOpacity.Value / 0.35f * 0.6f;
            c.a = Mathf.Clamp01(c.a);

            switch (DuckCamConfig.Guides.Value)
            {
                case CompositionGuide.RuleOfThirds:
                    Grid(f, 1f / 3f, 2f / 3f, c);
                    break;
                case CompositionGuide.GoldenRatio:
                    Grid(f, 0.382f, 0.618f, c);
                    break;
                case CompositionGuide.CenterCross:
                    HudTheme.Fill(new Rect(f.center.x - 30f, f.center.y, 60f, 1f), c);
                    HudTheme.Fill(new Rect(f.center.x, f.center.y - 30f, 1f, 60f), c);
                    break;
                case CompositionGuide.Diagonals:
                    Diagonal(new Vector2(f.xMin, f.yMin), new Vector2(f.xMax, f.yMax), c);
                    Diagonal(new Vector2(f.xMax, f.yMin), new Vector2(f.xMin, f.yMax), c);
                    break;
                case CompositionGuide.SafeArea:
                    DottedBox(new Rect(f.x + f.width * 0.05f, f.y + f.height * 0.05f, f.width * 0.9f, f.height * 0.9f), c);
                    DottedBox(new Rect(f.x + f.width * 0.1f, f.y + f.height * 0.1f, f.width * 0.8f, f.height * 0.8f), c);
                    break;
            }

            if (DuckCamConfig.HorizonLine.Value)
            {
                HudTheme.Fill(new Rect(f.xMin, Mathf.Round(f.center.y), f.width, 1f), new Color(HudTheme.P.Highlight.r, HudTheme.P.Highlight.g, HudTheme.P.Highlight.b, c.a));
            }
        }

        private static void Grid(Rect f, float a, float b, Color c)
        {
            float xa = f.x + f.width * a;
            float xb = f.x + f.width * b;
            float ya = f.y + f.height * a;
            float yb = f.y + f.height * b;
            HudTheme.DottedV(xa, f.yMin, f.yMax, c);
            HudTheme.DottedV(xb, f.yMin, f.yMax, c);
            HudTheme.DottedH(f.xMin, f.xMax, ya, c);
            HudTheme.DottedH(f.xMin, f.xMax, yb, c);

            foreach (float x in new[] { xa, xb })
            {
                foreach (float y in new[] { ya, yb })
                {
                    HudTheme.Fill(new Rect(x - 6f, y, 13f, 1f), c);
                    HudTheme.Fill(new Rect(x, y - 6f, 1f, 13f), c);
                }
            }
            int marker = 1;
            foreach (float y in new[] { ya, yb })
            {
                HudTheme.Fill(new Rect(f.xMin + 6f, y - 2f, 5f, 5f), c);
                HudTheme.Fill(new Rect(f.xMax - 11f, y - 2f, 5f, 5f), c);
                var style = new GUIStyle(HudTheme.GridLabel) { normal = { textColor = c } };
                GUI.Label(new Rect(f.xMin + 13f, y - 7f, 20f, 12f), marker.ToString(), style);
                GUI.Label(new Rect(f.xMax - 26f, y - 7f, 12f, 12f), marker.ToString(), style);
                marker++;
            }

            var label = new GUIStyle(HudTheme.GridLabel) { normal = { textColor = c } };
            foreach (float y in new[] { ya, yb })
            {
                foreach (float x in new[] { xa, xb })
                {
                    GUI.Label(new Rect(x + 6f, y + 6f, 40f, 24f), $"GR\n{(Mathf.RoundToInt(x) + Mathf.RoundToInt(y) * 7) % 10000:0000}", label);
                }
            }
        }

        private static void DottedBox(Rect r, Color c)
        {
            HudTheme.DottedH(r.xMin, r.xMax, r.yMin, c);
            HudTheme.DottedH(r.xMin, r.xMax, r.yMax, c);
            HudTheme.DottedV(r.xMin, r.yMin, r.yMax, c);
            HudTheme.DottedV(r.xMax, r.yMin, r.yMax, c);
        }

        private static void Diagonal(Vector2 a, Vector2 b, Color c)
        {
            Matrix4x4 saved = GUI.matrix;
            Vector2 d = b - a;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            HudTheme.DottedH(a.x, a.x + d.magnitude, a.y, c);
            GUI.matrix = saved;
        }
    }
}
