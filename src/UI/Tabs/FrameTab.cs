using System;
using System.Collections.Generic;
using DuckCam.Core;
using DuckCam.Rendering;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed partial class DuckCamUI
    {
        private void DrawFrame()
        {
            DuckCamConfig.Guides.Value = _kit.Enum("Grid", DuckCamConfig.Guides.Value, CompositionOverlay.GuideNames);
            DuckCamConfig.GuideOpacity.Value = _kit.Slider("Grid Opacity", DuckCamConfig.GuideOpacity.Value, 0.05f, 1f, 0.05f);
            DuckCamConfig.HorizonLine.Value = _kit.Toggle("Horizon Line", DuckCamConfig.HorizonLine.Value);
            DuckCamConfig.GuidesWhenUiHidden.Value = _kit.Toggle("Grid When UI Hidden", DuckCamConfig.GuidesWhenUiHidden.Value);
            DuckCamConfig.FrameAspectRatio.Value = _kit.Enum("Frame", DuckCamConfig.FrameAspectRatio.Value, CompositionOverlay.AspectNames);
            DuckCamConfig.LetterboxOpacity.Value = _kit.Slider("Letterbox Opacity", DuckCamConfig.LetterboxOpacity.Value, 0.2f, 1f, 0.05f);
            DuckCamConfig.CropToFrame.Value = _kit.Toggle("Crop Photo To Frame", DuckCamConfig.CropToFrame.Value);
            RectInt frame = CompositionOverlay.FrameRect(Screen.width, Screen.height, DuckCamConfig.FrameAspectRatio.Value);
            _kit.Value("Photo Size", DuckCamConfig.CropToFrame.Value ? $"{frame.width} x {frame.height}" : $"{Screen.width} x {Screen.height}");

            _kit.Section("Interface");
            DuckCamConfig.Palette.Value = _kit.Enum("Color Scheme", DuckCamConfig.Palette.Value, new[] { "Neon Red", "Acid", "Ice", "Tarkov" });
            DuckCamConfig.PanelSide.Value = _kit.Enum("Panel Side", DuckCamConfig.PanelSide.Value, new[] { "Right", "Left" });
            DuckCamConfig.UiScale.Value = _kit.Slider("UI Scale", DuckCamConfig.UiScale.Value, 0.6f, 1.8f, 0.05f);
            DuckCamConfig.PanelRows.Value = _kit.IntSlider("Panel Height", DuckCamConfig.PanelRows.Value, 6, 24, " rows");
            DuckCamConfig.HudDecorations.Value = _kit.Toggle("HUD Decorations", DuckCamConfig.HudDecorations.Value);
            DuckCamConfig.EdgeGlow.Value = _kit.Toggle("Edge Glow", DuckCamConfig.EdgeGlow.Value);
            DuckCamConfig.ShowKeyHints.Value = _kit.Toggle("Controls Legend", DuckCamConfig.ShowKeyHints.Value);
            _kit.Note("Grid, frame and HUD are previews only and never appear in photos. With cropping on, the photo is exactly the area inside the frame.");
            DrawInterfaceExtras();
            DrawLanguage();
        }

        private void DrawLanguage()
        {
            _kit.Section("Language");
            IReadOnlyList<(string id, string name, string path)> langs = Core.Locale.Available();
            var ids = new List<string> { Core.Locale.BuiltIn };
            var names = new List<string> { Core.Locale.BuiltIn };
            foreach ((string id, string name, string path) l in langs)
            {
                ids.Add(l.id);
                names.Add(l.name);
            }
            int current = Mathf.Max(0, ids.FindIndex(i => string.Equals(i, DuckCamConfig.Language.Value, StringComparison.OrdinalIgnoreCase)));
            int picked = _kit.Selector("Language", names.ToArray(), current);
            if (picked != current)
            {
                DuckCamConfig.Language.Value = ids[picked];
            }
            int b = _kit.Buttons("Rescan Folder", "Write Template");
            if (b == 0)
            {
                Core.Locale.Refresh();
                Core.Locale.Load(DuckCamConfig.Language.Value);
            }
            else if (b == 1)
            {
                try
                {
                    _c.LastMessage = "Template written: " + Core.Locale.WriteTemplate();
                }
                catch (Exception ex)
                {
                    _c.LastMessage = "Template failed: " + ex.Message;
                }
            }
            if (Core.Locale.LastError != null)
            {
                _kit.Note("Language failed: " + Core.Locale.LastError);
            }
            _kit.Value("Strings", $"{Core.Locale.Count} translated, {Core.Locale.Seen} seen");
            _kit.Note("For translators: open every tab and window so DuckCam sees its text, then Write Template. Copy " + Core.Locale.TemplateName + ".json to a code like de.json in " + Core.Locale.UserDirectory + " (or ship it in BepInEx/plugins/DuckCam/Locales), fill in the values and Rescan. Full guide: duckcam.wiki/Translating");
        }
    }
}
