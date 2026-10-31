using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using DuckCam.Core;
using DuckCam.Lighting;
using UnityEngine;

namespace DuckCam.Actors
{
    internal sealed class PinState
    {
        public PinMode Mode;
        public float[] Point;
        public float[] Offset = { 0f, 0f, 0f };
        public float Swivel;
        [JsonIgnore] public bool Repin;
    }

    internal sealed class PoseState
    {
        public Dictionary<string, float[]> Rot = new Dictionary<string, float[]>();
        public Dictionary<string, float[]> Loc = new Dictionary<string, float[]>();
        public Dictionary<string, PinState> Pins = new Dictionary<string, PinState>();
        public Dictionary<string, float[]> FullPose;
        public float[] FullHips;
        public float[] WeaponPos;
        public float[] WeaponRot;
        public bool RestBase;
        public List<string> Selected = new List<string>();
        public string Active;
    }

    internal sealed class PoseEdit
    {
        private const int UndoLimit = 200;
        private readonly List<string> _undo = new List<string>();
        private readonly List<string> _redo = new List<string>();
        private float _lastCheckpoint = -10f;
        private object _lastGesture;

        public PoseState State { get; private set; } = new PoseState();

        public bool Enabled { get; set; } = true;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public bool HasAnyEdit => State.Rot.Count > 0 || State.Loc.Count > 0 || State.Pins.Values.Any(p => p.Mode != PinMode.Off);

        public Quaternion Rot(string bone)
        {
            return State.Rot.TryGetValue(bone, out float[] q) && q.Length == 4 ? new Quaternion(q[0], q[1], q[2], q[3]) : Quaternion.identity;
        }

        public Vector3 Loc(string bone)
        {
            return State.Loc.TryGetValue(bone, out float[] v) && v.Length == 3 ? new Vector3(v[0], v[1], v[2]) : Vector3.zero;
        }

        public void SetRot(string bone, Quaternion q)
        {
            q = PoseMath.Normalized(q);
            if (!PoseMath.Finite(q) || Quaternion.Angle(q, Quaternion.identity) < 1e-4f)
            {
                State.Rot.Remove(bone);
                return;
            }
            State.Rot[bone] = new[] { q.x, q.y, q.z, q.w };
        }

        public void SetLoc(string bone, Vector3 v)
        {
            if (!PoseMath.Finite(v) || v.sqrMagnitude < 1e-12f)
            {
                State.Loc.Remove(bone);
                return;
            }
            v = Vector3.ClampMagnitude(v, 25f);
            State.Loc[bone] = new[] { v.x, v.y, v.z };
        }

        public bool IsEdited(string bone) => State.Rot.ContainsKey(bone) || State.Loc.ContainsKey(bone);

        public void Revert(string snapshot)
        {
            State = Deserialize(snapshot);
            if (_undo.Count > 0 && _undo[_undo.Count - 1] == snapshot)
            {
                _undo.RemoveAt(_undo.Count - 1);
            }
            _lastGesture = null;
        }

        public string Snapshot() => Serialize(State);

        public void Checkpoint(object gesture = null)
        {
            float now = Time.unscaledTime;
            if (gesture != null && Equals(gesture, _lastGesture) && now - _lastCheckpoint < 0.5f)
            {
                _lastCheckpoint = now;
                return;
            }
            _lastGesture = gesture;
            _lastCheckpoint = now;
            _undo.Add(Serialize(State));
            if (_undo.Count > UndoLimit)
            {
                _undo.RemoveAt(0);
            }
            _redo.Clear();
        }

        public void EndGesture() => _lastGesture = null;

        public bool Undo()
        {
            if (_undo.Count == 0)
            {
                return false;
            }
            _redo.Add(Serialize(State));
            State = Deserialize(_undo[_undo.Count - 1]);
            _undo.RemoveAt(_undo.Count - 1);
            _lastGesture = null;
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0)
            {
                return false;
            }
            _undo.Add(Serialize(State));
            State = Deserialize(_redo[_redo.Count - 1]);
            _redo.RemoveAt(_redo.Count - 1);
            _lastGesture = null;
            return true;
        }

        public Dictionary<string, float[]> BaseFull { get; private set; }

        public float[] BaseHips { get; private set; }

        public void SetBase(Dictionary<string, float[]> full, float[] hips)
        {
            BaseFull = full;
            BaseHips = hips;
        }

        public void ResetAll()
        {
            var keep = new PoseState { Selected = State.Selected, Active = State.Active, FullPose = BaseFull, FullHips = BaseHips };
            State = keep;
        }

        public PinState Pin(PoseLimb limb, bool create)
        {
            string key = limb.ToString();
            if (!State.Pins.TryGetValue(key, out PinState pin) && create)
            {
                pin = new PinState();
                State.Pins[key] = pin;
            }
            return pin;
        }

        public void Restore(PoseState state) => State = state;

        public static string Serialize(PoseState s) => JsonConvert.SerializeObject(s);

        public static PoseState Deserialize(string json) => JsonConvert.DeserializeObject<PoseState>(json) ?? new PoseState();
    }

    internal sealed class PoseFile
    {
        public string Format = "DuckCam.Pose";
        public int Version = 1;
        public string Name;
        public string Skeleton = "EFT";
        public string SavedAt;
        public Dictionary<string, float[]> Bones = new Dictionary<string, float[]>();
        public float[] Hips;
    }

    internal static class PoseLibrary
    {
        public static string Directory => Path.Combine(DuckCamPlugin.DataRoot, "Poses");

        private static string[] _cache;

        public static string[] List()
        {
            if (_cache != null)
            {
                return _cache;
            }
            try
            {
                _cache = System.IO.Directory.Exists(Directory)
                    ? System.IO.Directory.GetFiles(Directory, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray()
                    : Array.Empty<string>();
            }
            catch (Exception ex)
            {
                PhotoLog.WarnOnce("poselist", "Pose library could not be listed: " + ex.Message);
                _cache = Array.Empty<string>();
            }
            return _cache;
        }

        public static void Refresh() => _cache = null;

        public static string NextDefaultName()
        {
            string[] existing = List();
            for (int i = 1; i < 10000; i++)
            {
                string n = "Pose " + i.ToString("000");
                if (!existing.Contains(n, StringComparer.OrdinalIgnoreCase))
                {
                    return n;
                }
            }
            return "Pose";
        }

        public static string Clean(string name)
        {
            name = new string((name ?? "").Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_').ToArray()).Trim();
            return name.Length == 0 ? NextDefaultName() : LightRigs.Sanitize(name);
        }

        public static void Save(PoseFile file)
        {
            System.IO.Directory.CreateDirectory(Directory);
            file.SavedAt = DateTime.Now.ToString("o");
            File.WriteAllText(Path.Combine(Directory, file.Name + ".json"), JsonConvert.SerializeObject(file, Formatting.Indented));
            Refresh();
        }

        public static PoseFile Load(string name)
        {
            return JsonConvert.DeserializeObject<PoseFile>(File.ReadAllText(Path.Combine(Directory, name + ".json")));
        }

        public static bool Rename(string from, string to)
        {
            string src = Path.Combine(Directory, from + ".json");
            string dst = Path.Combine(Directory, to + ".json");
            if (!File.Exists(src) || File.Exists(dst))
            {
                return false;
            }
            PoseFile file = Load(from);
            if (file != null)
            {
                file.Name = to;
                File.WriteAllText(dst, JsonConvert.SerializeObject(file, Formatting.Indented));
                File.Delete(src);
            }
            Refresh();
            return true;
        }

        public static void Delete(string name)
        {
            File.Delete(Path.Combine(Directory, name + ".json"));
            Refresh();
        }
    }
}
