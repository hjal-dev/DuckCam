using System.Linq;
using DuckCam.Actors;
using DuckCam.Core;
using DuckCam.Lighting;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed partial class DuckCamUI
    {
        private void DrawLights()
        {
            _lightName = _kit.Text("Light Name", _lightName);
            _lightType = _kit.Selector("Light Type", new[] { "Point", "Spot", "Directional" }, _lightType);
            _lightIntensity = _kit.Slider("Intensity", _lightIntensity, 0f, 20f, 0.1f);
            if (_lightType != 2)
            {
                _lightRange = _kit.Slider("Range", _lightRange, 0.5f, 100f, 0.5f, "0.0", " m");
            }
            if (_lightType == 1)
            {
                _lightSpot = _kit.Slider("Spot Angle", _lightSpot, 1f, 179f, 1f, "0", "°");
            }
            _lightColor = _kit.ColorRows("Color", _lightColor);
            _lightShadows = _kit.Toggle("Shadows", _lightShadows);
            if (_kit.Action("Create Light", "At Camera"))
            {
                LightType type = _lightType == 0 ? LightType.Point : (_lightType == 1 ? LightType.Spot : LightType.Directional);
                _c.Lights.Create(_c.Restores, _lightName, type, _lightIntensity, _lightRange, _lightColor, _lightSpot, _lightShadows, _c.Camera.Camera.transform);
            }

            foreach (StudioLight light in _c.Lights.Lights.ToList())
            {
                if (light.Light == null)
                {
                    continue;
                }
                _kit.Section($"{light.Name} · {light.Light.type}");
                light.Light.enabled = _kit.Toggle("Enabled", light.Light.enabled);
                light.Light.intensity = _kit.Slider("Intensity", light.Light.intensity, 0f, 20f, 0.1f);
                if (light.Light.type != LightType.Directional)
                {
                    light.Light.range = _kit.Slider("Range", light.Light.range, 0.5f, 100f, 0.5f, "0.0", " m");
                }
                if (light.Light.type == LightType.Spot)
                {
                    light.Light.spotAngle = _kit.Slider("Spot Angle", light.Light.spotAngle, 1f, 179f, 1f, "0", "°");
                }
                light.Light.color = _kit.ColorRows("Color", light.Light.color);
                int b = _kit.Buttons("Move To Camera", "Remove");
                if (b == 0)
                {
                    _c.Lights.MoveToCamera(light, _c.Camera.Camera.transform);
                }
                else if (b == 1)
                {
                    _c.Lights.Remove(light);
                }
            }
            _kit.Section("Camera flash");
            LightingStudio studio = _c.Lights;
            studio.FlashEnabled = _kit.Toggle("Flash", studio.FlashEnabled);
            if (studio.FlashEnabled)
            {
                studio.FlashPreview = _kit.Toggle("Preview Flash", studio.FlashPreview);
                studio.FlashIntensity = _kit.Slider("Flash Intensity", studio.FlashIntensity, 0.1f, 20f, 0.1f);
                studio.FlashRange = _kit.Slider("Flash Range", studio.FlashRange, 1f, 60f, 0.5f, "0.0", " m");
                studio.FlashAngle = _kit.Slider("Flash Spread", studio.FlashAngle, 10f, 170f, 1f, "0", "°");
                studio.FlashWarmth = _kit.Slider("Flash Warmth", studio.FlashWarmth, 0f, 1f, 0.05f);
                studio.FlashShadows = _kit.Toggle("Flash Shadows", studio.FlashShadows);
            }

            _kit.Section("Light rigs");
            _rigAnchor = _kit.Selector("Rig Anchor", LightRigs.AnchorNames, _rigAnchor);
            Transform anchor = RigAnchor();
            _rigName = _kit.Text("Rig Name", _rigName);
            if (studio.Lights.Count > 0 && _kit.Action("Save Rig", "Save"))
            {
                if (anchor == null)
                {
                    _rigMessage = "Pick a character in CHARACTERS first, or use the Camera anchor.";
                }
                else
                {
                    string path = LightRigs.Save(LightRigs.Capture(studio.Lights, anchor, LightRigs.AnchorNames[_rigAnchor]), _rigName);
                    _rigMessage = "Saved " + System.IO.Path.GetFileName(path);
                }
            }
            foreach (string rig in _kit.Packs(_rigPacks, LightRigs.Directory, LightRigs.List()))
            {
                if (_kit.ListRow(_rigPacks.Show(rig), "", "Load") == 0)
                {
                    LightRigFile file = LightRigs.Load(rig, out string error);
                    if (file == null)
                    {
                        _rigMessage = rig + ": " + error;
                    }
                    else if (anchor == null)
                    {
                        _rigMessage = "Pick a character in CHARACTERS first, or use the Camera anchor.";
                    }
                    else
                    {
                        int n = LightRigs.Apply(file, studio, _c.Restores, anchor, false);
                        _rigMessage = $"Loaded {n} lights from '{ContentPacks.Display(rig)}' around the {LightRigs.AnchorNames[_rigAnchor].ToLowerInvariant()}.";
                    }
                }
            }
            if (_rigMessage != null)
            {
                _kit.Note(_rigMessage);
            }
            _kit.Note("Temporary lights; removed when the session ends. Scene lights are never changed. The flash lights only the captured frames unless Preview Flash is on. "
                      + "Rigs are saved relative to the anchor, so a rig made around one character can be reused on any character or map.");
        }

        private int _rigAnchor;

        private string _rigName = "rig1";

        private string _rigMessage;

        private Transform RigAnchor()
        {
            if (_rigAnchor == 0)
            {
                return _c.Camera.Camera != null ? _c.Camera.Camera.transform : null;
            }
            PhotoActor actor = _c.Actors.SelectedActor;
            return actor != null && actor.Status == ActorStatus.Ready ? actor.Root : null;
        }
    }
}
