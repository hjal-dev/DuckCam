using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed partial class DuckCamUI
    {
        private static readonly string[] _timePresetNames = { "Pick...", "Midnight 00:00", "Dawn 05:00", "Sunrise 06:30", "Morning 09:00", "Noon 12:00", "Afternoon 15:00", "Golden Hour 19:00", "Dusk 21:00", "Night 23:00" };

        private static readonly float[] _timePresetHours = { -1f, 0f, 5f, 6.5f, 9f, 12f, 15f, 19f, 21f, 23f };

        private int _weatherPreset = -1;

        private int _timePreset;

        private void DrawEnvironment()
        {
            DuckCam.Environment.TimeOfDayController tod = _c.TimeOfDay;
            DuckCam.Environment.WeatherOverride weather = _c.Weather;

            if (!tod.Available)
            {
                _kit.Value("Time of Day", "No game clock here");
            }
            else
            {
                float hour = tod.Hour;
                _kit.Value("Current Time", tod.Now.ToString("HH:mm", CultureInfo.InvariantCulture) + (_c.Frozen ? "  (held)" : ""));
                float picked = _kit.Slider("Time of Day", hour, 0f, 23.99f, 0.25f, "0.00", " h");
                if (Mathf.Abs(picked - hour) > 0.01f)
                {
                    tod.SetHour(picked, _c.Restores);
                }
                int preset = _kit.Selector("Time Preset", _timePresetNames, _timePreset);
                if (preset != _timePreset)
                {
                    _timePreset = preset;
                    if (preset > 0)
                    {
                        tod.SetHour(_timePresetHours[preset], _c.Restores);
                    }
                }
                if (tod.Overridden && _kit.Action("Keep Time After Exit", "Keep"))
                {
                    tod.KeepAfterExit();
                    _c.LastMessage = "The new time stays in the raid after DuckCam.";
                }
                _kit.Value("Clock", tod.Status);
            }

            _kit.Section("Weather");
            if (!DuckCam.Environment.WeatherOverride.Available)
            {
                _kit.Value("Weather", "Not on this map");
                _kit.Note("This map has no dynamic weather (Factory, Labs and the Hideout do not).");
            }
            else
            {
                bool on = _kit.Toggle("Weather Override", weather.Enabled);
                if (on != weather.Enabled)
                {
                    weather.SetEnabled(on, _c.Restores);
                }
                string[] presets = new[] { "Pick..." }.Concat(DuckCam.Environment.WeatherOverride.PresetNames).ToArray();
                int wp = _kit.Selector("Weather Preset", presets, _weatherPreset + 1);
                if (wp - 1 != _weatherPreset)
                {
                    _weatherPreset = wp - 1;
                    if (_weatherPreset >= 0)
                    {
                        weather.ApplyPreset(_weatherPreset, _c.Restores);
                    }
                }
                if (weather.Enabled)
                {
                    float clouds = _kit.Slider("Clouds", weather.Clouds, -1f, 1f, 0.05f);
                    float fog = _kit.Slider("Fog", weather.Fog, 0.001f, 0.255f, 0.002f, "0.000");
                    float rain = _kit.Slider("Rain", weather.Rain, 0f, 1f, 0.05f);
                    float lightning = _kit.Slider("Lightning", weather.Lightning, 0f, 1f, 0.05f);
                    float wind = _kit.Slider("Wind", weather.Wind, 0f, 1f, 0.05f);
                    int direction = _kit.Selector("Wind Direction", DuckCam.Environment.WeatherOverride.Directions, weather.WindDirection);
                    float temperature = _kit.Slider("Temperature", weather.Temperature, -50f, 50f, 1f, "0", " C");
                    if (clouds != weather.Clouds || fog != weather.Fog || rain != weather.Rain || lightning != weather.Lightning
                        || wind != weather.Wind || direction != weather.WindDirection || temperature != weather.Temperature)
                    {
                        weather.Set(_c.Restores, clouds, fog, rain, lightning, wind, temperature, direction);
                        _weatherPreset = -1;
                    }
                }
                if (weather.Overridden && _kit.Action("Keep Weather After Exit", "Keep"))
                {
                    weather.KeepAfterExit();
                    _c.LastMessage = "The new weather stays in the raid after DuckCam.";
                }
                if (DuckCam.Environment.WeatherOverride.OtherWeatherModLoaded)
                {
                    _kit.Note("TimeWeatherChanger is also installed; both change the same game weather, so the last change wins.");
                }
            }
            _kit.Note("Time and weather return to the raid's own values when DuckCam ends, unless you choose Keep. While the scene is frozen the clock is held, so the sun does not move.");
        }
    }
}
