using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using DuckCam.Camera;
using DuckCam.Lighting;
using DuckCam.Rendering;
using UnityEngine;

namespace DuckCam.Core
{
    internal sealed class ScenePresetFile
    {
        public int Version = 1;
        public string SavedOnMap;
        public string SavedAt;

        public Dictionary<string, float> Effects = new Dictionary<string, float>();
        public string Filter;
        public bool DofEnabled;
        public int DofMode;
        public float DofFocus;
        public float DofAperture;
        public float DofFocalSize;
        public float DofMaxBlur;
        public string FrameAspect;
        public bool CropToFrame;
        public string Guide;

        public float? Hour;
        public float[] Weather;

        public List<DecorText> Texts = new List<DecorText>();
        public int Border;
        public float BorderWidth;
        public int BorderColor;
        public int Mask;
        public float MaskSize;
        public float MaskSoftness;
        public int MaskColor;
        public float MaskOpacity;
        public float MaskCorner;

        public bool FlashEnabled;
        public float FlashIntensity;
        public float FlashRange;
        public float FlashAngle;
        public float FlashWarmth;

        public float[] CameraPosition;
        public float CameraYaw;
        public float CameraPitch;
        public float CameraRoll;
        public float CameraFov;
        public LightRigFile Lights;
    }

    internal static class ScenePresets
    {
        public static string Directory => Path.Combine(DuckCamPlugin.DataRoot, "Presets");

        public static string[] List()
        {
            if (!System.IO.Directory.Exists(Directory))
            {
                return Array.Empty<string>();
            }
            return System.IO.Directory.GetFiles(Directory, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToArray();
        }

        public static string Save(DuckCamController c, string name)
        {
            DepthOfFieldController dof = c.Dof;
            PhotoDecor d = c.Decor;
            var file = new ScenePresetFile
            {
                SavedOnMap = GameContext.LocationId,
                SavedAt = DateTime.Now.ToString("o"),
                Effects = c.Effects.SnapshotValues(),
                Filter = c.Effects.FilterAvailable ? c.Effects.Filter.ToString() : null,
                DofEnabled = dof.Enabled,
                DofMode = (int)dof.Mode,
                DofFocus = dof.FocusDistance,
                DofAperture = dof.Aperture,
                DofFocalSize = dof.FocalSize,
                DofMaxBlur = dof.MaxBlur,
                FrameAspect = DuckCamConfig.FrameAspectRatio.Value.ToString(),
                CropToFrame = DuckCamConfig.CropToFrame.Value,
                Guide = DuckCamConfig.Guides.Value.ToString(),
                Hour = c.TimeOfDay.Overridden ? c.TimeOfDay.Hour : (float?)null,
                Weather = c.Weather.Overridden && c.Weather.Enabled
                    ? new[] { c.Weather.Clouds, c.Weather.Fog, c.Weather.Rain, c.Weather.Lightning, c.Weather.Wind, c.Weather.Temperature, c.Weather.WindDirection }
                    : null,
                Texts = d.Texts.ToList(),
                Border = (int)d.Border,
                BorderWidth = d.BorderWidth,
                BorderColor = d.BorderColor,
                Mask = (int)d.Mask,
                MaskSize = d.MaskSize,
                MaskSoftness = d.MaskSoftness,
                MaskColor = d.MaskColor,
                MaskOpacity = d.MaskOpacity,
                MaskCorner = d.MaskCorner,
                FlashEnabled = c.Lights.FlashEnabled,
                FlashIntensity = c.Lights.FlashIntensity,
                FlashRange = c.Lights.FlashRange,
                FlashAngle = c.Lights.FlashAngle,
                FlashWarmth = c.Lights.FlashWarmth
            };
            CameraPose pose = c.Camera.CurrentPose;
            file.CameraPosition = LightRigs.V(pose.Position);
            file.CameraYaw = pose.Yaw;
            file.CameraPitch = pose.Pitch;
            file.CameraRoll = pose.Roll;
            file.CameraFov = pose.Fov;
            file.Lights = LightRigs.Capture(c.Lights.Lights, c.Camera.Camera != null ? c.Camera.Camera.transform : null, "Camera");

            System.IO.Directory.CreateDirectory(Directory);
            string path = Path.Combine(Directory, LightRigs.Sanitize(name) + ".json");
            File.WriteAllText(path, JsonConvert.SerializeObject(file, Formatting.Indented));
            return path;
        }

        public static string Load(DuckCamController c, string name, bool applyCamera, bool applyLights)
        {
            ScenePresetFile file;
            try
            {
                file = JsonConvert.DeserializeObject<ScenePresetFile>(File.ReadAllText(Path.Combine(Directory, name + ".json")));
            }
            catch (Exception ex)
            {
                return "could not read: " + ex.Message;
            }
            if (file == null)
            {
                return "empty preset";
            }

            var applied = new List<string>();
            c.Effects.RestoreValues(file.Effects, file.Filter);
            applied.Add("effects");

            DepthOfFieldController dof = c.Dof;
            if (dof.Available)
            {
                dof.Mode = (FocusMode)Mathf.Clamp(file.DofMode, 0, 3);
                dof.FocusDistance = file.DofFocus > 0f ? file.DofFocus : dof.FocusDistance;
                dof.Aperture = file.DofAperture > 0f ? file.DofAperture : dof.Aperture;
                dof.FocalSize = file.DofFocalSize > 0f ? file.DofFocalSize : dof.FocalSize;
                dof.MaxBlur = file.DofMaxBlur > 0f ? file.DofMaxBlur : dof.MaxBlur;
                dof.SetEnabled(file.DofEnabled);
                applied.Add("depth of field");
            }

            if (Enum.TryParse(file.FrameAspect, out FrameAspect aspect))
            {
                DuckCamConfig.FrameAspectRatio.Value = aspect;
            }
            DuckCamConfig.CropToFrame.Value = file.CropToFrame;
            if (Enum.TryParse(file.Guide, out CompositionGuide guide))
            {
                DuckCamConfig.Guides.Value = guide;
            }
            applied.Add("frame");

            if (file.Hour.HasValue && c.TimeOfDay.Available)
            {
                c.TimeOfDay.SetHour(file.Hour.Value, c.Restores);
                applied.Add("time");
            }
            if (file.Weather != null && file.Weather.Length >= 7 && Environment.WeatherOverride.Available)
            {
                float[] w = file.Weather;
                c.Weather.Set(c.Restores, w[0], w[1], w[2], w[3], w[4], w[5], Mathf.RoundToInt(w[6]));
                applied.Add("weather");
            }

            PhotoDecor d = c.Decor;
            d.Texts.Clear();
            if (file.Texts != null)
            {
                d.Texts.AddRange(file.Texts.Where(t => t != null));
            }
            d.SelectedText = 0;
            d.Border = (BorderStyle)Mathf.Clamp(file.Border, 0, 2);
            d.BorderWidth = file.BorderWidth > 0f ? file.BorderWidth : d.BorderWidth;
            d.BorderColor = file.BorderColor;
            d.Mask = (DecorMask)Mathf.Clamp(file.Mask, 0, 3);
            d.MaskSize = file.MaskSize > 0f ? file.MaskSize : d.MaskSize;
            d.MaskSoftness = file.MaskSoftness;
            d.MaskColor = file.MaskColor;
            d.MaskOpacity = file.MaskOpacity > 0f ? file.MaskOpacity : d.MaskOpacity;
            d.MaskCorner = file.MaskCorner;
            applied.Add("text/border/mask");

            c.Lights.FlashEnabled = file.FlashEnabled;
            if (file.FlashIntensity > 0f)
            {
                c.Lights.FlashIntensity = file.FlashIntensity;
                c.Lights.FlashRange = file.FlashRange;
                c.Lights.FlashAngle = file.FlashAngle;
                c.Lights.FlashWarmth = file.FlashWarmth;
            }

            bool sameMap = file.SavedOnMap == GameContext.LocationId;
            if (applyCamera)
            {
                if (sameMap && file.CameraPosition != null)
                {
                    c.Camera.Mode = CameraMode.Free;
                    c.Camera.SetPose(new CameraPose
                    {
                        Position = LightRigs.V(file.CameraPosition),
                        Yaw = file.CameraYaw,
                        Pitch = file.CameraPitch,
                        Roll = file.CameraRoll,
                        Fov = file.CameraFov
                    });
                    applied.Add("camera");
                }
                else
                {
                    applied.Add("camera skipped (saved on " + file.SavedOnMap + ")");
                }
            }
            if (applyLights && file.Lights?.Lights != null && file.Lights.Lights.Count > 0)
            {
                if (sameMap)
                {
                    int n = LightRigs.Apply(file.Lights, c.Lights, c.Restores, null, true);
                    applied.Add(n + " lights");
                }
                else
                {
                    int n = LightRigs.Apply(file.Lights, c.Lights, c.Restores, c.Camera.Camera != null ? c.Camera.Camera.transform : null, false);
                    applied.Add(n + " lights (around the camera)");
                }
            }
            return string.Join(", ", applied);
        }
    }
}
