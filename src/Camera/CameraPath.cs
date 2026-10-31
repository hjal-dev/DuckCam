using System.Collections.Generic;
using UnityEngine;

namespace DuckCam.Camera
{
    internal sealed class PathKeyframe
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public float Fov;

        public bool ManualTangent;
        public bool BrokenTangent;
        public Vector3 OutTangent;
        public Vector3 InTangent;
        public float Tension = 1f;

        public float Speed = 1f;
        public float Accel;
        public float Decel;
        public float Hold;

        public PathKeyframe Clone() => (PathKeyframe)MemberwiseClone();
    }

    internal sealed class CameraPath
    {
        public const float MinDuration = 1f;
        public const float MaxDuration = 3600f;
        private const int SamplesPerSegment = 32;

        private readonly List<float> _table = new List<float>();
        private float[] _keyU = new float[0];
        private float _totalLength;
        private AnimationCurve _pitch;
        private AnimationCurve _yaw;
        private AnimationCurve _roll;
        private AnimationCurve _fov;
        private bool _dirty = true;
        private float _duration = 5f;

        public List<PathKeyframe> Keys { get; } = new List<PathKeyframe>();
        public bool Loop { get; set; }
        public bool Playing { get; private set; }
        public bool Paused { get; set; }
        public float Time { get; private set; }
        private bool _constantSpeed = true;

        public bool ConstantSpeed
        {
            get => _constantSpeed;
            set
            {
                if (_constantSpeed != value)
                {
                    _constantSpeed = value;
                    MarkDirty();
                }
            }
        }
        public float Length { get { Rebuild(); return _totalLength; } }

        public float Duration
        {
            get => PerPointTiming ? TimelineTotal() : _duration;
            set => _duration = Mathf.Clamp(value, MinDuration, MaxDuration);
        }

        private bool _perPoint;

        public bool PerPointTiming
        {
            get => _perPoint;
            set
            {
                if (_perPoint != value)
                {
                    _perPoint = value;
                    MarkDirty();
                }
            }
        }

        public float BaseSpeed { get; set; } = 3f;

        private bool UsesArc => ConstantSpeed || PerPointTiming;

        private struct Stretch
        {
            public float Length;
            public float Speed;
            public float Accel;
            public float Decel;
            public float Time;
        }

        private Stretch StretchAt(int segment)
        {
            Rebuild();
            var st = new Stretch
            {
                Length = _table[(segment + 1) * SamplesPerSegment] - _table[segment * SamplesPerSegment],
                Speed = Mathf.Max(0.01f, BaseSpeed * Mathf.Max(0.01f, Keys[segment].Speed)),
                Accel = Mathf.Max(0f, Keys[segment].Accel),
                Decel = Mathf.Max(0f, Keys[segment + 1].Decel)
            };
            float ramps = st.Speed * (st.Accel + st.Decel) * 0.5f;
            if (ramps > st.Length && ramps > 1e-5f)
            {
                float k = st.Length / ramps;
                st.Accel *= k;
                st.Decel *= k;
                st.Time = st.Accel + st.Decel;
            }
            else
            {
                st.Time = (st.Length - ramps) / st.Speed + st.Accel + st.Decel;
            }
            return st;
        }

        private float TimelineTotal()
        {
            if (Keys.Count < 2)
            {
                return MinDuration;
            }
            float total = 0f;
            for (int i = 0; i < Keys.Count - 1; i++)
            {
                total += Mathf.Max(0f, Keys[i].Hold) + StretchAt(i).Time;
            }
            total += Mathf.Max(0f, Keys[Keys.Count - 1].Hold);
            return Mathf.Max(0.01f, total);
        }

        private float ProgressAtTime(float time)
        {
            Rebuild();
            if (Keys.Count < 2 || _totalLength <= 1e-4f)
            {
                return 0f;
            }
            float t = time;
            for (int i = 0; i < Keys.Count - 1; i++)
            {
                float start = _table[i * SamplesPerSegment];
                float hold = Mathf.Max(0f, Keys[i].Hold);
                if (t < hold)
                {
                    return start / _totalLength;
                }
                t -= hold;
                Stretch st = StretchAt(i);
                if (t < st.Time)
                {
                    float d;
                    if (st.Accel > 0f && t < st.Accel)
                    {
                        d = 0.5f * st.Speed / st.Accel * t * t;
                    }
                    else if (st.Decel > 0f && t > st.Time - st.Decel)
                    {
                        float r = st.Time - t;
                        d = st.Length - 0.5f * st.Speed / st.Decel * r * r;
                    }
                    else
                    {
                        d = 0.5f * st.Speed * st.Accel + st.Speed * (t - st.Accel);
                    }
                    return Mathf.Clamp01((start + Mathf.Clamp(d, 0f, st.Length)) / _totalLength);
                }
                t -= st.Time;
            }
            return 1f;
        }

        public Vector3 OutHandle(int i)
        {
            PathKeyframe k = Keys[i];
            if (k.ManualTangent)
            {
                return k.OutTangent;
            }
            return AutoTangent(i) * k.Tension;
        }

        public Vector3 InHandle(int i)
        {
            PathKeyframe k = Keys[i];
            if (k.ManualTangent)
            {
                return k.BrokenTangent ? k.InTangent : -k.OutTangent;
            }
            return -AutoTangent(i) * k.Tension;
        }

        private Vector3 AutoTangent(int i)
        {
            int last = Keys.Count - 1;
            return (Keys[Mathf.Min(i + 1, last)].Position - Keys[Mathf.Max(i - 1, 0)].Position) / 6f;
        }

        public void MakeManual(int i)
        {
            PathKeyframe k = Keys[i];
            if (k.ManualTangent)
            {
                return;
            }
            Vector3 o = OutHandle(i);
            Vector3 n = InHandle(i);
            k.ManualTangent = true;
            k.OutTangent = o;
            k.InTangent = n;
            MarkDirty();
        }

        public bool CanPlay => Keys.Count >= 2;

        public int Version { get; private set; }

        public void MarkDirty()
        {
            _dirty = true;
            Version++;
        }

        public void Add(PathKeyframe key)
        {
            Keys.Add(key);
            MarkDirty();
        }

        public void RemoveAt(int index)
        {
            if (index >= 0 && index < Keys.Count)
            {
                FollowKey = -1;
                Keys.RemoveAt(index);
                if (SelectedNode > index || SelectedNode >= Keys.Count)
                {
                    SelectedNode--;
                }
                MarkDirty();
            }
        }

        public void Clear()
        {
            Keys.Clear();
            FollowKey = -1;
            SelectedNode = -1;
            Stop();
            MarkDirty();
        }

        public string Name { get; set; } = "Path";
        public bool EaseIn { get; set; }
        public bool EaseOut { get; set; }
        public bool UseCountdown { get; set; }
        public float Countdown { get; private set; }
        public int SelectedNode { get; set; } = -1;

        public int FollowKey { get; set; } = -1;

        public void Play()
        {
            if (!CanPlay)
            {
                return;
            }
            FollowKey = -1;
            if (!Playing || Time >= Duration)
            {
                Time = 0f;
                Countdown = UseCountdown ? 3f : 0f;
            }
            Playing = true;
            Paused = false;
        }

        public void PlayFrom(float progress)
        {
            if (!CanPlay)
            {
                return;
            }
            Time = Mathf.Clamp01(progress) * Duration;
            Countdown = 0f;
            Playing = true;
            Paused = false;
        }

        public void Stop()
        {
            Playing = false;
            Paused = false;
            Time = 0f;
            Countdown = 0f;
        }

        public float EasedProgress
        {
            get
            {
                if (PerPointTiming)
                {
                    return ProgressAtTime(Time);
                }
                float u = Progress;
                if (EaseIn && EaseOut)
                {
                    return (1f - Mathf.Cos(u * Mathf.PI)) * 0.5f;
                }
                if (EaseIn)
                {
                    return 1f - Mathf.Cos(u * Mathf.PI * 0.5f);
                }
                if (EaseOut)
                {
                    return Mathf.Sin(u * Mathf.PI * 0.5f);
                }
                return u;
            }
        }

        public void InsertAt(int index, PathKeyframe key)
        {
            Keys.Insert(Mathf.Clamp(index, 0, Keys.Count), key);
            MarkDirty();
        }

        public void Replace(int index, PathKeyframe key)
        {
            if (index >= 0 && index < Keys.Count)
            {
                Keys[index] = key;
                MarkDirty();
            }
        }

        public bool Tick(float dt)
        {
            if (!Playing)
            {
                return false;
            }
            if (Countdown > 0f && !Paused)
            {
                Countdown = Mathf.Max(0f, Countdown - UnityEngine.Time.unscaledDeltaTime);
                return true;
            }
            if (!Paused)
            {
                Time += dt;
            }
            float duration = Duration;
            if (Time >= duration)
            {
                if (Loop)
                {
                    Time %= duration;
                }
                else
                {
                    Time = duration;
                    Playing = false;
                }
            }
            return true;
        }

        public float Progress => Duration > 0f ? Mathf.Clamp01(Time / Duration) : 0f;

        public void Evaluate(float u, out Vector3 position, out Quaternion rotation, out float fov)
        {
            Rebuild();
            int n = Keys.Count;
            if (n == 0)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                fov = 60f;
                return;
            }
            if (n == 1)
            {
                position = Keys[0].Position;
                rotation = Keys[0].Rotation;
                fov = Keys[0].Fov;
                return;
            }

            u = Mathf.Clamp01(u);
            SegmentAt(u, out int segment, out float s);
            position = SegmentPoint(segment, s);
            rotation = Quaternion.Euler(_pitch.Evaluate(u), _yaw.Evaluate(u), _roll.Evaluate(u));
            fov = _fov.Evaluate(u);
        }

        public List<Vector3> Polyline(int samplesPerSegment = 12)
        {
            var points = new List<Vector3>();
            int segments = Keys.Count - 1;
            for (int seg = 0; seg < segments; seg++)
            {
                for (int i = 0; i < samplesPerSegment; i++)
                {
                    points.Add(SegmentPoint(seg, i / (float)samplesPerSegment));
                }
            }
            if (Keys.Count > 0)
            {
                points.Add(Keys[Keys.Count - 1].Position);
            }
            return points;
        }

        private void SegmentAt(float u, out int segment, out float s)
        {
            int segments = Keys.Count - 1;
            if (!UsesArc || _totalLength <= 1e-4f)
            {
                float f = u * segments;
                segment = Mathf.Min((int)f, segments - 1);
                s = f - segment;
                return;
            }

            float target = u * _totalLength;
            int lo = 0;
            int hi = _table.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (_table[mid] < target)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }
            int k = Mathf.Max(1, lo);
            float a = _table[k - 1];
            float b = _table[k];
            float frac = b - a > 1e-6f ? (target - a) / (b - a) : 0f;
            float sample = (k - 1) + frac;
            segment = Mathf.Min((int)(sample / SamplesPerSegment), segments - 1);
            s = Mathf.Clamp01(sample / SamplesPerSegment - segment);
        }

        private Vector3 SegmentPoint(int segment, float s)
        {
            int last = Keys.Count - 1;
            int next = Mathf.Min(segment + 1, last);
            Vector3 p1 = Keys[segment].Position;
            Vector3 p2 = Keys[next].Position;

            Vector3 b1 = p1 + OutHandle(segment);
            Vector3 b2 = p2 + InHandle(next);
            float t = s;
            float it = 1f - t;
            return it * it * it * p1 + 3f * it * it * t * b1 + 3f * it * t * t * b2 + t * t * t * p2;
        }

        private void Rebuild()
        {
            if (!_dirty)
            {
                return;
            }
            _dirty = false;

            int n = Keys.Count;
            _table.Clear();
            _totalLength = 0f;
            _keyU = new float[n];
            if (n < 2)
            {
                return;
            }

            _table.Add(0f);
            Vector3 prev = Keys[0].Position;
            for (int seg = 0; seg < n - 1; seg++)
            {
                for (int i = 1; i <= SamplesPerSegment; i++)
                {
                    Vector3 p = SegmentPoint(seg, i / (float)SamplesPerSegment);
                    _totalLength += Vector3.Distance(prev, p);
                    _table.Add(_totalLength);
                    prev = p;
                }
            }
            for (int i = 0; i < n; i++)
            {
                _keyU[i] = UsesArc && _totalLength > 1e-4f
                    ? _table[i * SamplesPerSegment] / _totalLength
                    : i / (float)(n - 1);
            }
            for (int i = 1; i < n; i++)
            {
                if (_keyU[i] <= _keyU[i - 1])
                {
                    _keyU[i] = _keyU[i - 1] + 1e-4f;
                }
            }

            _pitch = new AnimationCurve();
            _yaw = new AnimationCurve();
            _roll = new AnimationCurve();
            _fov = new AnimationCurve();
            Vector3 last = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                Vector3 e = Keys[i].Rotation.eulerAngles;
                e = new Vector3(PhotoCameraController.NormalizePitch(e.x), e.y, PhotoCameraController.NormalizePitch(e.z));
                if (i > 0)
                {
                    e = new Vector3(
                        last.x + Mathf.DeltaAngle(last.x, e.x),
                        last.y + Mathf.DeltaAngle(last.y, e.y),
                        last.z + Mathf.DeltaAngle(last.z, e.z));
                }
                last = e;
                _pitch.AddKey(new Keyframe(_keyU[i], e.x));
                _yaw.AddKey(new Keyframe(_keyU[i], e.y));
                _roll.AddKey(new Keyframe(_keyU[i], e.z));
                _fov.AddKey(new Keyframe(_keyU[i], Keys[i].Fov));
            }
            for (int i = 0; i < n; i++)
            {
                _pitch.SmoothTangents(i, 0f);
                _yaw.SmoothTangents(i, 0f);
                _roll.SmoothTangents(i, 0f);
                _fov.SmoothTangents(i, 0f);
            }
        }
    }
}
