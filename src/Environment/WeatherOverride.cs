using BepInEx.Bootstrap;
using EFT.Weather;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Environment
{
    internal sealed class WeatherOverride
    {
        public static readonly string[] Directions = { "East", "North", "West", "South", "South-East", "South-West", "North-West", "North-East" };
        public static readonly string[] PresetNames = { "Clear", "Partly Cloudy", "Overcast", "Foggy", "Light Rain", "Storm" };

        private RestoreHandle _handle;

        public static bool Available => WeatherController.Instance != null && WeatherController.Instance.WeatherDebug != null;

        public static bool OtherWeatherModLoaded => Chainloader.PluginInfos.ContainsKey("com.samswat.timeweatherchanger");

        public bool Overridden => _handle != null && !_handle.Done;

        private static WeatherDebug Debug => WeatherController.Instance.WeatherDebug;

        public bool Enabled => Available && Debug.Enabled;
        public float Clouds => Available ? Debug.CloudDensity : 0f;
        public float Fog => Available ? Debug.Fog : 0f;
        public float Rain => Available ? Debug.Rain : 0f;
        public float Lightning => Available ? Debug.LightningThunderProbability : 0f;
        public float Wind => Available ? Debug.WindMagnitude : 0f;
        public float Temperature => Available ? Debug.Temperature : 0f;
        public int WindDirection => Available ? Mathf.Clamp((int)Debug.WindDirection - 1, 0, 7) : 1;

        public void SetEnabled(bool on, RestoreStack stack)
        {
            if (!Available)
            {
                return;
            }
            Touch(stack);
            Debug.Enabled = on;
        }

        public void Set(RestoreStack stack, float? clouds = null, float? fog = null, float? rain = null, float? lightning = null, float? wind = null, float? temperature = null, int? windDirection = null)
        {
            if (!Available)
            {
                return;
            }
            Touch(stack);
            WeatherDebug d = Debug;
            d.Enabled = true;
            if (clouds.HasValue) d.CloudDensity = Mathf.Clamp(clouds.Value, -1f, 1f);
            if (fog.HasValue) d.Fog = Mathf.Clamp(fog.Value, 0.001f, 0.255f);
            if (rain.HasValue) d.Rain = Mathf.Clamp01(rain.Value);
            if (lightning.HasValue) d.LightningThunderProbability = Mathf.Clamp01(lightning.Value);
            if (wind.HasValue) d.WindMagnitude = Mathf.Clamp01(wind.Value);
            if (temperature.HasValue) d.Temperature = Mathf.Clamp(temperature.Value, -50f, 50f);
            if (windDirection.HasValue) d.WindDirection = (WeatherDebug.Direction)(Mathf.Clamp(windDirection.Value, 0, 7) + 1);
        }

        public void ApplyPreset(int index, RestoreStack stack)
        {
            switch (index)
            {
                case 0: Set(stack, -1f, 0f, 0f, 0f, 0.1f); break;
                case 1: Set(stack, 0f, 0.004f, 0f, 0f, 0.25f); break;
                case 2: Set(stack, 0.75f, 0.01f, 0f, 0f, 0.35f); break;
                case 3: Set(stack, 0.3f, 0.2f, 0f, 0f, 0.05f); break;
                case 4: Set(stack, 0.6f, 0.02f, 0.35f, 0f, 0.4f); break;
                case 5: Set(stack, 1f, 0.05f, 1f, 0.7f, 0.9f); break;
            }
        }

        public void KeepAfterExit()
        {
            if (Overridden)
            {
                _handle.Dismiss();
                _handle = null;
            }
        }

        public void Revert()
        {
            if (Overridden)
            {
                _handle.Run();
            }
            _handle = null;
        }

        public void ResetSession()
        {
            _handle = null;
        }

        private void Touch(RestoreStack stack)
        {
            if (Overridden)
            {
                return;
            }
            WeatherDebug d = Debug;
            bool enabled = d.Enabled;
            float clouds = d.CloudDensity;
            float fog = d.Fog;
            float rain = d.Rain;
            float lightning = d.LightningThunderProbability;
            float wind = d.WindMagnitude;
            float temperature = d.Temperature;
            WeatherDebug.Direction direction = d.WindDirection;
            Vector2 topWind = d.TopWindDirection;
            _handle = stack.Push("weather override", () =>
            {
                if (WeatherController.Instance == null || WeatherController.Instance.WeatherDebug != d)
                {
                    return;
                }
                d.CloudDensity = clouds;
                d.Fog = fog;
                d.Rain = rain;
                d.LightningThunderProbability = lightning;
                d.WindMagnitude = wind;
                d.Temperature = temperature;
                d.WindDirection = direction;
                d.TopWindDirection = topWind;
                d.Enabled = enabled;
            });
        }
    }
}
