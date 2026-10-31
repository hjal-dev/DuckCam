using System.Collections.Generic;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Lighting
{
    internal sealed class StudioLight
    {
        public string Name;
        public Light Light;
    }

    internal sealed class LightingStudio
    {
        private GameObject _root;

        public List<StudioLight> Lights { get; } = new List<StudioLight>();

        public StudioLight Create(RestoreStack stack, string name, LightType type, float intensity, float range, Color color, float spotAngle, bool shadows, Transform placeAt)
        {
            EnsureRoot(stack);
            var go = new GameObject("DuckCamLight_" + (string.IsNullOrEmpty(name) ? "Light" : name));
            go.transform.SetParent(_root.transform, false);
            go.transform.SetPositionAndRotation(placeAt.position, placeAt.rotation);

            Light light = go.AddComponent<Light>();
            light.type = type;
            light.intensity = intensity;
            light.range = range;
            light.color = color;
            light.spotAngle = spotAngle;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;

            var studio = new StudioLight { Name = string.IsNullOrEmpty(name) ? "Light " + (Lights.Count + 1) : name, Light = light };
            Lights.Add(studio);
            PhotoLog.Verbose($"Created {type} light '{studio.Name}'.");
            return studio;
        }

        public void MoveToCamera(StudioLight light, Transform camera)
        {
            if (light?.Light != null)
            {
                light.Light.transform.SetPositionAndRotation(camera.position, camera.rotation);
            }
        }

        public void Remove(StudioLight light)
        {
            if (light?.Light != null)
            {
                Object.Destroy(light.Light.gameObject);
            }
            Lights.Remove(light);
        }

        public bool FlashEnabled { get; set; }
        public bool FlashPreview { get; set; }
        public float FlashIntensity { get; set; } = 3f;
        public float FlashRange { get; set; } = 15f;
        public float FlashAngle { get; set; } = 80f;
        public float FlashWarmth { get; set; } = 0.15f;
        public bool FlashShadows { get; set; } = true;
        private Light _flash;

        public void ResetFlash()
        {
            FlashEnabled = false;
            FlashPreview = false;
            FlashIntensity = 3f;
            FlashRange = 15f;
            FlashAngle = 80f;
            FlashWarmth = 0.15f;
            FlashShadows = true;
        }

        public void RemoveAll()
        {
            foreach (StudioLight light in Lights.ToArray())
            {
                Remove(light);
            }
        }

        public void UpdateFlash(RestoreStack stack, Transform camera, bool capturing)
        {
            bool on = FlashEnabled && camera != null && (capturing || FlashPreview);
            if (!on)
            {
                if (_flash != null)
                {
                    _flash.enabled = false;
                }
                return;
            }
            if (_flash == null)
            {
                EnsureRoot(stack);
                var go = new GameObject("DuckCam_Flash");
                go.transform.SetParent(_root.transform, false);
                _flash = go.AddComponent<Light>();
                _flash.type = LightType.Spot;
                _flash.renderMode = LightRenderMode.ForcePixel;
            }
            _flash.transform.SetPositionAndRotation(camera.position, camera.rotation);
            _flash.intensity = FlashIntensity;
            _flash.range = FlashRange;
            _flash.spotAngle = FlashAngle;
            _flash.color = Color.Lerp(Color.white, new Color(1f, 0.78f, 0.55f), FlashWarmth);
            _flash.shadows = FlashShadows ? LightShadows.Soft : LightShadows.None;
            _flash.enabled = true;
        }

        private void EnsureRoot(RestoreStack stack)
        {
            if (_root != null)
            {
                return;
            }
            _root = new GameObject("DuckCamLightingRoot");
            stack.Push("studio lights", DestroyAll);
        }

        private void DestroyAll()
        {
            if (_root != null)
            {
                Object.Destroy(_root);
            }
            _root = null;
            _flash = null;
            Lights.Clear();
        }
    }
}
