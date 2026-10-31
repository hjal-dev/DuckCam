using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using DuckCam.Actors;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Animation
{
    internal enum KeyEase
    {
        Smooth,
        Linear,
        EaseIn,
        EaseOut,
        Step
    }

    internal abstract class TimelineKey
    {
        public float Time;
        public KeyEase Ease = KeyEase.Smooth;
    }

    internal sealed class CameraKey : TimelineKey
    {
        public float[] Position;
        public float[] Rotation;
        public float Fov;
    }

    internal sealed class PoseKey : TimelineKey
    {
        public Dictionary<string, float[]> Bones = new Dictionary<string, float[]>();
        public float[] Hips;
        public float[] Place;
    }

    internal sealed class AnimatedPose
    {
        public readonly Dictionary<string, Quaternion> Bones = new Dictionary<string, Quaternion>();
        public Vector3? Hips;
    }

    internal sealed class ActorTrack
    {
        public string ActorName;
        [JsonIgnore] public PhotoActor Actor;
        public List<PoseKey> Keys = new List<PoseKey>();
    }

    internal sealed class SceneTimeline
    {
        public float Duration { get; set; } = 10f;
        public float Time { get; set; }
        public bool Playing { get; set; }
        public bool Loop { get; set; }
        public bool CameraEnabled { get; set; } = true;
        public int Fps { get; set; } = 30;

        public List<CameraKey> CameraKeys { get; } = new List<CameraKey>();
        public List<ActorTrack> Tracks { get; } = new List<ActorTrack>();

        public bool HasKeys => CameraKeys.Count > 0 || Tracks.Any(t => t.Keys.Count > 0);

        public static string Directory => Path.Combine(DuckCamPlugin.DataRoot, "Scenes");

        public ActorTrack TrackFor(PhotoActor actor, bool create)
        {
            ActorTrack track = Tracks.FirstOrDefault(t => t.Actor == actor);
            if (track == null && create)
            {
                track = new ActorTrack { Actor = actor, ActorName = actor.Name };
                Tracks.Add(track);
            }
            return track;
        }

        public void Relink(IReadOnlyList<PhotoActor> actors)
        {
            foreach (ActorTrack t in Tracks)
            {
                if (t.Actor == null || !actors.Contains(t.Actor))
                {
                    t.Actor = actors.FirstOrDefault(a => a.Name == t.ActorName && Tracks.All(o => o == t || o.Actor != a));
                }
            }
        }

        public void Advance(float dt)
        {
            if (!Playing)
            {
                return;
            }
            Time += dt;
            if (Time >= Duration)
            {
                if (Loop)
                {
                    Time %= Mathf.Max(0.01f, Duration);
                }
                else
                {
                    Time = Duration;
                    Playing = false;
                }
            }
        }

        public static T Upsert<T>(List<T> keys, T key) where T : TimelineKey
        {
            int existing = keys.FindIndex(k => Mathf.Abs(k.Time - key.Time) < 0.0005f);
            if (existing >= 0)
            {
                key.Ease = keys[existing].Ease;
                keys[existing] = key;
            }
            else
            {
                keys.Add(key);
                keys.Sort((a, b) => a.Time.CompareTo(b.Time));
            }
            return key;
        }

        public CameraKey KeyCamera(Vector3 position, Quaternion rotation, float fov)
        {
            return Upsert(CameraKeys, new CameraKey { Time = Time, Position = A(position), Rotation = A(rotation), Fov = fov });
        }

        public PoseKey KeyActor(PhotoActor actor)
        {
            if (actor?.Rig == null)
            {
                return null;
            }
            PoseFile pose = actor.Rig.Capture("key");
            var key = new PoseKey
            {
                Time = Time,
                Bones = pose.Bones,
                Hips = pose.Hips,
                Place = new[] { actor.OffsetRight, actor.OffsetForward, actor.OffsetUp, actor.Yaw }
            };
            return Upsert(TrackFor(actor, true).Keys, key);
        }

        public List<float> AllTimes()
        {
            return CameraKeys.Select(k => k.Time).Concat(Tracks.SelectMany(t => t.Keys.Select(k => k.Time))).Distinct().OrderBy(t => t).ToList();
        }

        public static float Shape(KeyEase ease, float u)
        {
            u = Mathf.Clamp01(u);
            switch (ease)
            {
                case KeyEase.Linear: return u;
                case KeyEase.EaseIn: return u * u;
                case KeyEase.EaseOut: return 1f - (1f - u) * (1f - u);
                case KeyEase.Step: return 0f;
                default: return u * u * (3f - 2f * u);
            }
        }

        private static bool Bracket<T>(List<T> keys, float t, out T a, out T b, out float blend) where T : TimelineKey
        {
            a = null;
            b = null;
            blend = 0f;
            if (keys.Count == 0)
            {
                return false;
            }
            if (t <= keys[0].Time)
            {
                a = b = keys[0];
                return true;
            }
            for (int i = 0; i < keys.Count - 1; i++)
            {
                if (t < keys[i + 1].Time)
                {
                    a = keys[i];
                    b = keys[i + 1];
                    float span = Mathf.Max(1e-5f, b.Time - a.Time);
                    blend = Shape(a.Ease, (t - a.Time) / span);
                    return true;
                }
            }
            a = b = keys[keys.Count - 1];
            return true;
        }

        public bool EvaluateCamera(float t, out Vector3 position, out Quaternion rotation, out float fov)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            fov = 60f;
            if (!CameraEnabled || !Bracket(CameraKeys, t, out CameraKey a, out CameraKey b, out float u))
            {
                return false;
            }
            int i = CameraKeys.IndexOf(a);
            Vector3 p1 = V(a.Position);
            Vector3 p2 = V(b.Position);
            if (a == b || a.Ease == KeyEase.Linear || a.Ease == KeyEase.Step)
            {
                position = Vector3.Lerp(p1, p2, u);
            }
            else
            {
                Vector3 p0 = i > 0 ? V(CameraKeys[i - 1].Position) : p1;
                Vector3 p3 = i + 2 < CameraKeys.Count ? V(CameraKeys[i + 2].Position) : p2;
                position = CatmullRom(p0, p1, p2, p3, u);
            }
            rotation = Quaternion.Slerp(Q(a.Rotation), Q(b.Rotation), u);
            fov = Mathf.Lerp(a.Fov, b.Fov, u);
            return true;
        }

        public bool EvaluateActor(ActorTrack track, float t, AnimatedPose into, out Vector4 place)
        {
            place = Vector4.zero;
            into.Bones.Clear();
            into.Hips = null;
            if (track == null || !Bracket(track.Keys, t, out PoseKey a, out PoseKey b, out float u))
            {
                return false;
            }
            foreach (KeyValuePair<string, float[]> kv in a.Bones)
            {
                Quaternion qa = Q(kv.Value);
                into.Bones[kv.Key] = b.Bones.TryGetValue(kv.Key, out float[] qb) ? Quaternion.Slerp(qa, Q(qb), u) : qa;
            }
            if (a.Hips != null)
            {
                into.Hips = b.Hips != null ? Vector3.Lerp(V(a.Hips), V(b.Hips), u) : V(a.Hips);
            }
            if (a.Place != null && a.Place.Length == 4)
            {
                float[] bp = b.Place != null && b.Place.Length == 4 ? b.Place : a.Place;
                place = new Vector4(
                    Mathf.Lerp(a.Place[0], bp[0], u),
                    Mathf.Lerp(a.Place[1], bp[1], u),
                    Mathf.Lerp(a.Place[2], bp[2], u),
                    Mathf.LerpAngle(a.Place[3], bp[3], u));
            }
            return true;
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        private sealed class SceneFile
        {
            public string Format = "DuckCam.Scene";
            public int Version = 1;
            public string Map;
            public float Duration;
            public bool Loop;
            public int Fps;
            public List<CameraKey> Camera;
            public List<ActorTrack> Tracks;
        }

        public static string[] List()
        {
            return System.IO.Directory.Exists(Directory)
                ? System.IO.Directory.GetFiles(Directory, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray()
                : Array.Empty<string>();
        }

        public string Save(string name)
        {
            System.IO.Directory.CreateDirectory(Directory);
            name = Lighting.LightRigs.Sanitize(string.IsNullOrWhiteSpace(name) ? "scene" : name.Trim());
            foreach (ActorTrack t in Tracks)
            {
                if (t.Actor != null)
                {
                    t.ActorName = t.Actor.Name;
                }
            }
            var file = new SceneFile { Map = GameContext.LocationId, Duration = Duration, Loop = Loop, Fps = Fps, Camera = CameraKeys, Tracks = Tracks.Where(t => t.Keys.Count > 0).ToList() };
            File.WriteAllText(Path.Combine(Directory, name + ".json"), JsonConvert.SerializeObject(file, Formatting.Indented));
            return name;
        }

        public string Load(string name, IReadOnlyList<PhotoActor> actors)
        {
            try
            {
                SceneFile file = JsonConvert.DeserializeObject<SceneFile>(File.ReadAllText(Path.Combine(Directory, name + ".json")));
                if (file == null)
                {
                    return "empty file";
                }
                Playing = false;
                Time = 0f;
                Duration = Mathf.Max(0.5f, file.Duration);
                Loop = file.Loop;
                Fps = file.Fps > 0 ? file.Fps : 30;
                CameraKeys.Clear();
                CameraKeys.AddRange(file.Camera ?? new List<CameraKey>());
                Tracks.Clear();
                Tracks.AddRange(file.Tracks ?? new List<ActorTrack>());
                Relink(actors);
                int missing = Tracks.Count(t => t.Actor == null);
                return missing > 0 ? $"{missing} character track(s) have no character named like that in this scene (add one with the same name)." : null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public void Clear()
        {
            Playing = false;
            Time = 0f;
            CameraKeys.Clear();
            Tracks.Clear();
        }

        internal static float[] A(Vector3 v) => new[] { v.x, v.y, v.z };
        internal static float[] A(Quaternion q) => new[] { q.x, q.y, q.z, q.w };
        internal static Vector3 V(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
        internal static Quaternion Q(float[] a) => a != null && a.Length >= 4 ? new Quaternion(a[0], a[1], a[2], a[3]) : Quaternion.identity;
    }
}
