using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Camera
{
    internal static class PathStorage
    {
        private sealed class PathFile
        {
            public int Version = 2;
            public string Kind;
            public string Map;
            public float Duration;
            public bool Loop;
            public bool ConstantSpeed;
            public bool EaseIn;
            public bool EaseOut;
            public bool Countdown;
            public bool PerPointTiming;
            public float BaseSpeed = 3f;
            public List<float[]> Keys = new List<float[]>();
        }

        public static string Directory => Path.Combine(DuckCamPlugin.DataRoot, "Paths");

        public static string[] List()
        {
            if (!System.IO.Directory.Exists(Directory))
            {
                return Array.Empty<string>();
            }
            return ContentPacks.List(Directory);
        }

        public static string SavePath(CameraPath path, string name, string pack = null)
        {
            var file = new PathFile
            {
                Kind = "bezier",
                Map = GameContext.LocationId,
                Duration = path.Duration,
                Loop = path.Loop,
                ConstantSpeed = path.ConstantSpeed,
                EaseIn = path.EaseIn,
                EaseOut = path.EaseOut,
                Countdown = path.UseCountdown,
                PerPointTiming = path.PerPointTiming,
                BaseSpeed = path.BaseSpeed,
                Keys = path.Keys.Select(PackKey).ToList()
            };
            return Write(name, file, pack);
        }

        public static string SaveRecording(CameraRecording recording, string name, string pack = null)
        {
            var file = new PathFile
            {
                Kind = "recording",
                Map = GameContext.LocationId,
                Duration = recording.Duration,
                Loop = recording.Loop,
                Keys = recording.Samples.Select(s => Pack(s.Position, s.Rotation, s.Fov, s.Time)).ToList()
            };
            return Write(name, file, pack);
        }

        public static string Load(string name, CameraPath path, CameraRecording recording)
        {
            try
            {
                PathFile file = JsonConvert.DeserializeObject<PathFile>(File.ReadAllText(ContentPacks.FileOf(Directory, name)));
                if (file?.Keys == null)
                {
                    return "empty or invalid file";
                }
                if (file.Kind == "recording")
                {
                    recording.Samples.Clear();
                    foreach (float[] k in file.Keys.Where(k => k != null && k.Length >= 9))
                    {
                        recording.Samples.Add(new CameraRecording.Sample { Position = new Vector3(k[0], k[1], k[2]), Rotation = new Quaternion(k[3], k[4], k[5], k[6]).normalized, Fov = k[7], Time = k[8] });
                    }
                    recording.Loop = file.Loop;
                }
                else
                {
                    path.Clear();
                    foreach (float[] k in file.Keys.Where(k => k != null && k.Length >= 8))
                    {
                        path.Add(UnpackKey(k));
                    }
                    path.Duration = file.Duration;
                    path.Loop = file.Loop;
                    path.ConstantSpeed = file.ConstantSpeed;
                    path.EaseIn = file.EaseIn;
                    path.EaseOut = file.EaseOut;
                    path.UseCountdown = file.Countdown;
                    path.BaseSpeed = file.BaseSpeed > 0f ? file.BaseSpeed : 3f;
                    path.PerPointTiming = file.PerPointTiming;
                }
                string mapNote = file.Map != null && file.Map != GameContext.LocationId ? $" (saved on {file.Map}, current map {GameContext.LocationId})" : "";
                PhotoLog.Info($"Loaded {file.Kind} '{name}' with {file.Keys.Count} keys{mapNote}.");
                return mapNote.Length > 0 ? "loaded, but" + mapNote : null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static string Write(string name, PathFile file, string pack)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            string full = ContentPacks.SaveFile(Directory, pack, name);
            File.WriteAllText(full, JsonConvert.SerializeObject(file, Formatting.Indented));
            ContentPacks.Invalidate(Directory);
            return full;
        }

        private static float[] PackKey(PathKeyframe k)
        {
            return new[]
            {
                k.Position.x, k.Position.y, k.Position.z, k.Rotation.x, k.Rotation.y, k.Rotation.z, k.Rotation.w, k.Fov, 0f,
                k.ManualTangent ? 1f : 0f, k.BrokenTangent ? 1f : 0f,
                k.OutTangent.x, k.OutTangent.y, k.OutTangent.z, k.InTangent.x, k.InTangent.y, k.InTangent.z,
                k.Tension, k.Speed, k.Accel, k.Decel, k.Hold
            };
        }

        private static PathKeyframe UnpackKey(float[] k)
        {
            var key = new PathKeyframe { Position = new Vector3(k[0], k[1], k[2]), Rotation = new Quaternion(k[3], k[4], k[5], k[6]).normalized, Fov = k[7] };
            if (k.Length >= 22)
            {
                key.ManualTangent = k[9] > 0.5f;
                key.BrokenTangent = k[10] > 0.5f;
                key.OutTangent = new Vector3(k[11], k[12], k[13]);
                key.InTangent = new Vector3(k[14], k[15], k[16]);
                key.Tension = k[17];
                key.Speed = k[18] > 0f ? k[18] : 1f;
                key.Accel = Mathf.Max(0f, k[19]);
                key.Decel = Mathf.Max(0f, k[20]);
                key.Hold = Mathf.Max(0f, k[21]);
            }
            return key;
        }

        private static float[] Pack(Vector3 p, Quaternion r, float fov, float time)
        {
            return new[] { p.x, p.y, p.z, r.x, r.y, r.z, r.w, fov, time };
        }
    }
}
