using System;
using System.Linq;
using DuckCam.Rendering;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed partial class DuckCamUI
    {
        private void DrawEffects()
        {
            PhotoEffects fx = _c.Effects;
            if (fx.Params.Count == 0 && !fx.FilterAvailable)
            {
                _kit.Value("Effects", "Unavailable");
                _kit.Note(fx.Discovery);
                return;
            }
            string[] mine = CustomFilters.List();
            string[] presets = new[] { "Custom" }.Concat(PhotoEffects.PresetNames).Concat(mine.Select(n => "* " + n)).ToArray();
            if (_effectPreset >= presets.Length)
            {
                _effectPreset = 0;
            }
            int preset = _kit.Selector("Preset", presets, _effectPreset);
            if (preset != _effectPreset)
            {
                _effectPreset = preset;
                int builtIn = PhotoEffects.PresetNames.Length;
                if (preset > 0 && preset <= builtIn)
                {
                    fx.ApplyPreset(preset - 1);
                }
                else if (preset > builtIn)
                {
                    string error = CustomFilters.Apply(fx, mine[preset - builtIn - 1]);
                    _c.LastMessage = error == null ? $"Filter '{mine[preset - builtIn - 1]}' applied." : "Filter could not be loaded: " + error;
                }
            }
            if (fx.FilterAvailable)
            {
                int current = (int)fx.Filter;
                int picked = _kit.Selector("Filter", PhotoEffects.FilterNames, current);
                if (picked != current)
                {
                    fx.SetFilter((CC_Vintage.Filter)picked);
                    _effectPreset = 0;
                }
            }
            foreach (PhotoEffects.Param p in fx.Params)
            {
                if (p.Section != null)
                {
                    _kit.Section(p.Section);
                }
                float v = fx.Get(p);
                float nv = _kit.Slider(p.Label, v, p.Min, p.Max, p.Step, p.Format);
                if (Mathf.Abs(nv - v) > 0.0001f)
                {
                    fx.Set(p, nv);
                    _effectPreset = 0;
                }
            }
            if (_kit.Action("Reset Effects", "Reset"))
            {
                fx.ResetAll();
                _effectPreset = 0;
            }

            _kit.Section("My filters · saved looks");
            _filterName = _kit.Text("Filter Name", _filterName);
            if (_kit.Action("Save Look As Filter", "Save"))
            {
                try
                {
                    string saved = CustomFilters.Save(fx, _filterName);
                    _effectPreset = PhotoEffects.PresetNames.Length + 1 + Array.IndexOf(CustomFilters.List(), saved);
                    _c.LastMessage = $"Filter '{saved}' saved.";
                }
                catch (Exception ex)
                {
                    _c.LastMessage = "Filter save failed: " + ex.Message;
                }
            }
            foreach (string name in mine)
            {
                bool confirming = _deleteFilter == name;
                int clicked = _kit.ListRow("* " + name, "", "Apply", confirming ? "Sure?" : "Delete");
                if (clicked == 0)
                {
                    string error = CustomFilters.Apply(fx, name);
                    _effectPreset = error == null ? PhotoEffects.PresetNames.Length + 1 + Array.IndexOf(mine, name) : 0;
                    _c.LastMessage = error == null ? $"Filter '{name}' applied." : "Filter could not be loaded: " + error;
                    _deleteFilter = null;
                }
                else if (clicked == 1)
                {
                    if (confirming)
                    {
                        CustomFilters.Delete(name);
                        _effectPreset = 0;
                        _deleteFilter = null;
                        _c.LastMessage = $"Filter '{name}' deleted.";
                    }
                    else
                    {
                        _deleteFilter = name;
                    }
                }
            }
            if (mine.Length == 0)
            {
                _kit.Note("Tweak the sliders (or start from a preset), name the look and press Save. It then shows in the Preset list with a star. Files: " + CustomFilters.Directory);
            }
            _kit.Note("Uses the effects the game already renders (PostFX grading and filters, vignette, chromatic aberration, grain/bloom, and a temporary color-grading override when available). Everything returns to your settings when DuckCam ends.");
        }
    }
}
