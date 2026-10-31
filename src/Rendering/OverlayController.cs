using System.Collections.Generic;
using UnityEngine;

namespace DuckCam.Rendering
{
    internal sealed class Sticker
    {
        public OverlayAsset Asset;
        public Vector2 Position = new Vector2(0.5f, 0.5f);
        public float Scale = 0.2f;
        public float Rotation;
        public float Opacity = 1f;
    }

    internal sealed class OverlayController
    {
        public OverlayAsset Frame { get; set; }
        public float FrameOpacity { get; set; } = 1f;
        public List<Sticker> Stickers { get; } = new List<Sticker>();
        public int Selected { get; set; } = -1;

        public bool HasContent => (Frame != null && Frame.Texture != null) || Stickers.Count > 0;

        public Sticker SelectedSticker => Selected >= 0 && Selected < Stickers.Count ? Stickers[Selected] : null;

        public void AddSticker(OverlayAsset asset)
        {
            Stickers.Add(new Sticker { Asset = asset });
            Selected = Stickers.Count - 1;
        }

        public void RemoveSelected()
        {
            if (SelectedSticker != null)
            {
                Stickers.RemoveAt(Selected);
                Selected = Mathf.Min(Selected, Stickers.Count - 1);
            }
        }

        public void Clear()
        {
            Frame = null;
            Stickers.Clear();
            Selected = -1;
        }

        public void DrawOnScreen(Rect frame)
        {
            Color prev = GUI.color;
            if (Frame != null && Frame.Texture != null)
            {
                GUI.color = new Color(1f, 1f, 1f, FrameOpacity);
                GUI.DrawTexture(frame, Frame.Texture, ScaleMode.StretchToFill, true);
            }
            foreach (Sticker s in Stickers)
            {
                Texture2D tex = s.Asset?.Texture;
                if (tex == null)
                {
                    continue;
                }
                float h = frame.height * s.Scale;
                float w = h * tex.width / Mathf.Max(1, tex.height);
                var center = new Vector2(frame.x + s.Position.x * frame.width, frame.y + s.Position.y * frame.height);
                Matrix4x4 saved = GUI.matrix;
                GUIUtility.RotateAroundPivot(s.Rotation, center);
                GUI.color = new Color(1f, 1f, 1f, s.Opacity);
                GUI.DrawTexture(new Rect(center.x - w * 0.5f, center.y - h * 0.5f, w, h), tex, ScaleMode.StretchToFill, true);
                GUI.matrix = saved;
            }
            GUI.color = prev;
        }

        public void Composite(Texture2D photo, RectInt region)
        {
            if (!HasContent)
            {
                return;
            }
            int width = photo.width;
            int height = photo.height;
            Color32[] dst = photo.GetPixels32();

            if (Frame != null && Frame.Texture != null)
            {
                Blit(dst, width, height, Frame.Texture, region.x + region.width * 0.5f, region.y + region.height * 0.5f, region.width, region.height, 0f, FrameOpacity);
            }
            foreach (Sticker s in Stickers)
            {
                Texture2D tex = s.Asset?.Texture;
                if (tex == null)
                {
                    continue;
                }
                float h = region.height * s.Scale;
                float w = h * tex.width / Mathf.Max(1, tex.height);
                Blit(dst, width, height, tex, region.x + s.Position.x * region.width, region.y + (1f - s.Position.y) * region.height, w, h, -s.Rotation, s.Opacity);
            }
            photo.SetPixels32(dst);
            photo.Apply();
        }

        private static void Blit(Color32[] dst, int width, int height, Texture2D src, float cx, float cy, float w, float h, float rotationDeg, float opacity)
        {
            Color32[] pixels = src.GetPixels32();
            int sw = src.width;
            int sh = src.height;
            float rad = rotationDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            float halfW = w * 0.5f;
            float halfH = h * 0.5f;
            float extent = Mathf.Sqrt(halfW * halfW + halfH * halfH);
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - extent));
            int x1 = Mathf.Min(width - 1, Mathf.CeilToInt(cx + extent));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - extent));
            int y1 = Mathf.Min(height - 1, Mathf.CeilToInt(cy + extent));

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - cx;
                    float dy = y + 0.5f - cy;
                    float lx = dx * cos + dy * sin;
                    float ly = -dx * sin + dy * cos;
                    if (lx < -halfW || lx >= halfW || ly < -halfH || ly >= halfH)
                    {
                        continue;
                    }
                    float u = (lx + halfW) / w * sw - 0.5f;
                    float v = (ly + halfH) / h * sh - 0.5f;
                    Color32 c = Sample(pixels, sw, sh, u, v);
                    float a = c.a / 255f * opacity;
                    if (a <= 0.001f)
                    {
                        continue;
                    }
                    int i = y * width + x;
                    Color32 d = dst[i];
                    dst[i] = new Color32(
                        (byte)(d.r + (c.r - d.r) * a),
                        (byte)(d.g + (c.g - d.g) * a),
                        (byte)(d.b + (c.b - d.b) * a),
                        255);
                }
            }
        }

        private static Color32 Sample(Color32[] p, int w, int h, float u, float v)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(u), 0, w - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(v), 0, h - 1);
            int x1 = Mathf.Min(x0 + 1, w - 1);
            int y1 = Mathf.Min(y0 + 1, h - 1);
            float fx = Mathf.Clamp01(u - x0);
            float fy = Mathf.Clamp01(v - y0);
            Color32 a = p[y0 * w + x0];
            Color32 b = p[y0 * w + x1];
            Color32 c = p[y1 * w + x0];
            Color32 d = p[y1 * w + x1];
            return Color32.Lerp(Color32.Lerp(a, b, fx), Color32.Lerp(c, d, fx), fy);
        }
    }
}
