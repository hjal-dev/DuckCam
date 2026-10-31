using System.Collections.Generic;
using UnityEngine;

namespace DuckCam.Camera
{
    internal sealed class CameraRecording
    {
        public struct Sample
        {
            public float Time;
            public Vector3 Position;
            public Quaternion Rotation;
            public float Fov;
        }

        private const float SampleInterval = 1f / 60f;

        private float _clock;
        private float _lastSampleTime = float.NegativeInfinity;

        public List<Sample> Samples { get; } = new List<Sample>();
        public bool Recording { get; private set; }
        public bool Playing { get; private set; }
        public bool Paused { get; set; }
        public bool Loop { get; set; }
        public float PlayTime { get; private set; }
        public float Duration => Samples.Count > 0 ? Samples[Samples.Count - 1].Time : 0f;

        public void Begin(Vector3 position, Quaternion rotation, float fov)
        {
            StopPlayback();
            Samples.Clear();
            _clock = 0f;
            _lastSampleTime = float.NegativeInfinity;
            Recording = true;
            Add(position, rotation, fov);
        }

        public void Continue(Vector3 position, Quaternion rotation, float fov)
        {
            StopPlayback();
            if (Samples.Count == 0)
            {
                Begin(position, rotation, fov);
                return;
            }
            _clock = Duration + SampleInterval;
            _lastSampleTime = float.NegativeInfinity;
            Recording = true;
            Add(position, rotation, fov);
        }

        public void End()
        {
            Recording = false;
        }

        public void TickRecord(float dt, Vector3 position, Quaternion rotation, float fov)
        {
            if (!Recording)
            {
                return;
            }
            _clock += dt;
            if (_clock - _lastSampleTime >= SampleInterval)
            {
                Add(position, rotation, fov);
            }
        }

        private void Add(Vector3 position, Quaternion rotation, float fov)
        {
            Samples.Add(new Sample { Time = _clock, Position = position, Rotation = rotation, Fov = fov });
            _lastSampleTime = _clock;
        }

        public void Play()
        {
            if (Samples.Count < 2)
            {
                return;
            }
            Recording = false;
            PlayTime = 0f;
            Playing = true;
            Paused = false;
        }

        public void StopPlayback()
        {
            Playing = false;
            Paused = false;
        }

        public bool Tick(float dt, out Vector3 position, out Quaternion rotation, out float fov)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            fov = 60f;
            if (!Playing)
            {
                return false;
            }
            if (!Paused)
            {
                PlayTime += dt;
            }
            if (PlayTime >= Duration)
            {
                if (Loop)
                {
                    PlayTime %= Mathf.Max(Duration, 1e-3f);
                }
                else
                {
                    PlayTime = Duration;
                    Playing = false;
                }
            }

            int hi = Samples.FindIndex(s => s.Time >= PlayTime);
            if (hi <= 0)
            {
                Sample first = hi == 0 ? Samples[0] : Samples[Samples.Count - 1];
                position = first.Position;
                rotation = first.Rotation;
                fov = first.Fov;
                return true;
            }
            Sample a = Samples[hi - 1];
            Sample b = Samples[hi];
            float t = b.Time - a.Time > 1e-6f ? (PlayTime - a.Time) / (b.Time - a.Time) : 0f;
            position = Vector3.Lerp(a.Position, b.Position, t);
            rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t);
            fov = Mathf.Lerp(a.Fov, b.Fov, t);
            return true;
        }
    }
}
