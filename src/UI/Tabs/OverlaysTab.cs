using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DuckCam.Rendering;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed partial class DuckCamUI
    {
        private void DrawOverlays()
        {
            DrawDecor();
            _kit.Section("Image overlays (PNG files)");
            OverlayController o = _c.Overlays;
            IReadOnlyList<OverlayAsset> all = OverlayLibrary.Assets;
            _kit.Value("Overlay Files", all.Count.ToString(CultureInfo.InvariantCulture));
            if (_kit.Action("Overlay Folder", "Rescan"))
            {
                OverlayLibrary.Refresh();
                o.Clear();
            }
            if (all.Count == 0)
            {
                _kit.Note("Put PNG frames or stickers in " + OverlayLibrary.Directory + " (subfolders become categories), then press Rescan.");
                return;
            }

            string[] frameNames = new[] { "None" }.Concat(all.Select(a => a.Category + " / " + a.Name)).ToArray();
            int frameIndex = o.Frame == null ? 0 : all.ToList().IndexOf(o.Frame) + 1;
            int newFrame = _kit.Selector("Frame", frameNames, Mathf.Max(0, frameIndex));
            if (newFrame != frameIndex)
            {
                o.Frame = newFrame == 0 ? null : all[newFrame - 1];
            }
            if (o.Frame != null)
            {
                o.FrameOpacity = _kit.Slider("Frame Opacity", o.FrameOpacity * 100f, 0f, 100f, 5f, "0") / 100f;
            }

            _kit.Section("Stickers");
            string[] categories = OverlayLibrary.Categories;
            _overlayCategory = _kit.Selector("Category", categories, Mathf.Min(_overlayCategory, categories.Length - 1));
            List<OverlayAsset> inCategory = OverlayLibrary.InCategory(categories[Mathf.Min(_overlayCategory, categories.Length - 1)]);
            if (inCategory.Count > 0)
            {
                _overlayPick = _kit.Selector("Sticker", inCategory.Select(a => a.Name).ToArray(), Mathf.Min(_overlayPick, inCategory.Count - 1));
                if (_kit.Action("Add Sticker", "Add"))
                {
                    o.AddSticker(inCategory[Mathf.Min(_overlayPick, inCategory.Count - 1)]);
                }
            }

            if (o.Stickers.Count > 0)
            {
                string[] placed = o.Stickers.Select((s, i) => $"#{i + 1} {s.Asset?.Name}").ToArray();
                o.Selected = _kit.Selector("Edit Sticker", placed, Mathf.Clamp(o.Selected, 0, placed.Length - 1));
                Sticker s = o.SelectedSticker;
                if (s != null)
                {
                    s.Position.x = _kit.Slider("Left / Right", s.Position.x, 0f, 1f, 0.01f);
                    s.Position.y = _kit.Slider("Up / Down", s.Position.y, 0f, 1f, 0.01f);
                    s.Scale = _kit.Slider("Size", s.Scale, 0.02f, 1.5f, 0.01f);
                    s.Rotation = _kit.Slider("Rotate", s.Rotation, -180f, 180f, 1f, "0");
                    s.Opacity = _kit.Slider("Opacity", s.Opacity * 100f, 0f, 100f, 5f, "0") / 100f;
                    if (_kit.Action("Remove Sticker", "Remove"))
                    {
                        o.RemoveSelected();
                    }
                }
                if (_kit.Action("All Overlays", "Clear"))
                {
                    o.Clear();
                }
            }
            _kit.Note("Overlays are part of the photo: they are composited into the saved PNG inside the frame, exactly as previewed.");
        }

        private void DrawDecor()
        {
            PhotoDecor d = _c.Decor;

            _kit.Section("Text");
            if (d.Texts.Count > 0)
            {
                string[] names = d.Texts.Select((t, i) => $"{i + 1}: {(string.IsNullOrEmpty(t.Text) ? "(empty)" : t.Text)}").ToArray();
                d.SelectedText = _kit.Selector("Edit Text", names, Mathf.Clamp(d.SelectedText, 0, names.Length - 1));
                DecorText t = d.SelectedTextItem;
                if (t != null)
                {
                    t.Text = _kit.Text("Text", t.Text);
                    t.Size = _kit.Slider("Size", t.Size * 100f, 1f, 25f, 0.5f, "0.0", "%") / 100f;
                    t.X = _kit.Slider("Left / Right", t.X, 0f, 1f, 0.01f);
                    t.Y = _kit.Slider("Up / Down", t.Y, 0f, 1f, 0.01f);
                    t.Align = _kit.Selector("Align", new[] { "Left", "Center", "Right" }, t.Align);
                    t.Color = _kit.Selector("Color", PhotoDecor.ColorNames, t.Color);
                    t.Opacity = _kit.Slider("Opacity", t.Opacity * 100f, 5f, 100f, 5f, "0") / 100f;
                    t.Bold = _kit.Toggle("Bold", t.Bold);
                    t.Shadow = _kit.Toggle("Shadow", t.Shadow);
                }
            }
            int tb = _kit.Buttons("Add Text", d.Texts.Count > 0 ? "Remove Text" : "-");
            if (tb == 0)
            {
                d.AddText();
                _kit.ClearBuffers();
            }
            else if (tb == 1 && d.Texts.Count > 0)
            {
                d.RemoveSelectedText();
                _kit.ClearBuffers();
            }

            _kit.Section("Border");
            d.Border = _kit.Enum("Border", d.Border, new[] { "None", "Solid", "Polaroid" });
            if (d.Border != BorderStyle.None)
            {
                d.BorderWidth = _kit.Slider("Border Width", d.BorderWidth * 100f, 0.5f, 15f, 0.5f, "0.0", "%") / 100f;
                d.BorderColor = _kit.Selector("Border Color", PhotoDecor.ColorNames, d.BorderColor);
            }

            _kit.Section("Mask");
            d.Mask = _kit.Enum("Mask", d.Mask, new[] { "None", "Oval", "Circle", "Rounded" });
            if (d.Mask != DecorMask.None)
            {
                d.MaskSize = _kit.Slider("Mask Size", d.MaskSize, 0.2f, 1.4f, 0.01f);
                d.MaskSoftness = _kit.Slider("Softness", d.MaskSoftness, 0f, 1f, 0.02f);
                if (d.Mask == DecorMask.Rounded)
                {
                    d.MaskCorner = _kit.Slider("Corner Radius", d.MaskCorner, 0f, 0.5f, 0.01f);
                }
                d.MaskColor = _kit.Selector("Mask Color", PhotoDecor.ColorNames, d.MaskColor);
                d.MaskOpacity = _kit.Slider("Mask Opacity", d.MaskOpacity * 100f, 5f, 100f, 5f, "0") / 100f;
            }
            _kit.Note("Text, border and mask are drawn into the photo inside the frame, exactly as previewed (they show with the menu hidden too). With high-res capture above 1x, they may not scale with the photo.");
        }
    }
}
