using System;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed partial class DuckCamUI
    {
        private string _presetName = "scene1";

        private string _presetMessage;

        private bool _presetCamera = true;

        private bool _presetLights = true;

        private int _resetConfirm = -1;

        private float _resetConfirmUntil;

        private void DrawResetRows()
        {
            if (Time.unscaledTime > _resetConfirmUntil)
            {
                _resetConfirm = -1;
            }
            string[] labels = { "Reset Everything To Default", "Reset + Clear Scene" };
            for (int i = 0; i < labels.Length; i++)
            {
                if (!_kit.Action(labels[i], _resetConfirm == i ? "Sure?" : "Reset"))
                {
                    continue;
                }
                if (_resetConfirm != i)
                {
                    _resetConfirm = i;
                    _resetConfirmUntil = Time.unscaledTime + 4f;
                    continue;
                }
                _resetConfirm = -1;
                _c.ResetToDefaults(clearScene: i == 1);
                _effectPreset = 0;
                _timePreset = 0;
                _weatherPreset = -1;
                _deleteFilter = null;
                _kit.ClearBuffers();
            }
        }

        private void DrawPresets()
        {
            _kit.Section("Reset · stays in DuckCam");
            DrawResetRows();
            _kit.Note("Puts the lens, shake, effects and filter, depth of field, overlays, text/border/mask, frame and grid, flash, time of day, weather, slow motion and LOD boost back to default. The camera stays where it is; freeze, paths and bookmarks are kept. Reset + Clear Scene also removes characters, props, studio lights and DuckCam-only bots. Click twice to confirm.");
            _kit.Section("Scene presets");
            _presetName = _kit.Text("Preset Name", _presetName);
            if (_kit.Action("Save Scene Preset", "Save"))
            {
                try
                {
                    _presetMessage = "Saved " + System.IO.Path.GetFileName(ScenePresets.Save(_c, _presetName));
                }
                catch (Exception ex)
                {
                    _presetMessage = "Save failed: " + ex.Message;
                }
            }
            _kit.Section("Load");
            _presetCamera = _kit.Toggle("Load Camera Position", _presetCamera);
            _presetLights = _kit.Toggle("Load Lights", _presetLights);
            string[] presets = ScenePresets.List();
            if (presets.Length == 0)
            {
                _kit.Note("No presets yet. Set up a shot and press Save.");
            }
            foreach (string preset in presets)
            {
                if (_kit.ListRow(preset, "", "Load") == 0)
                {
                    _presetMessage = $"'{preset}': " + ScenePresets.Load(_c, preset, _presetCamera, _presetLights);
                    _kit.ClearBuffers();
                }
            }
            if (_presetMessage != null)
            {
                _kit.Note(_presetMessage);
            }
            _kit.Note("A preset stores the whole look: effects and filter, depth of field, frame and grid, time and weather (if you changed them), text/border/mask and the flash. "
                      + "It also stores the camera position and the studio lights; those load in place on the same map, and lights are placed around the camera on other maps. "
                      + "Saved in " + ScenePresets.Directory);
        }
    }
}
