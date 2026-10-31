using System;
using System.Collections.Generic;
using EFT;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Actors
{
    internal sealed class RelaxedArms
    {
        private sealed class Arm
        {
            public Transform Upper;
            public Transform Fore;
            public Transform Palm;
            public float Side;
        }

        private Player _player;
        private Transform _root;
        private readonly List<Arm> _arms = new List<Arm>();
        private readonly List<(Transform bone, Quaternion rest)> _rest = new List<(Transform, Quaternion)>();
        private bool _hooked;

        public bool Active => _hooked;

        public void Enable(Player player)
        {
            Disable();
            if (player == null)
            {
                return;
            }
            try
            {
                _player = player;
                _root = player.Transform?.Original;
                PlayerBones bones = player.PlayerBones;
                AddArm(bones?.LeftPalm, -1f);
                AddArm(bones?.RightPalm, 1f);
                if (_arms.Count == 0 || _root == null)
                {
                    PhotoLog.Debug("Relaxed arms: arm bones not found.");
                    return;
                }
                CaptureRest(player);
                Application.onBeforeRender += Apply;
                _hooked = true;
            }
            catch (Exception ex)
            {
                PhotoLog.Warn("Relaxed arms unavailable: " + ex.Message);
                Disable();
            }
        }

        public void Disable()
        {
            if (_hooked)
            {
                Application.onBeforeRender -= Apply;
                _hooked = false;
            }
            _arms.Clear();
            _rest.Clear();
            _player = null;
            _root = null;
        }

        private void AddArm(Transform palm, float side)
        {
            if (palm == null)
            {
                return;
            }
            Transform fore = null;
            for (Transform t = palm.parent; t != null; t = t.parent)
            {
                if (t.name.IndexOf("Forearm", StringComparison.Ordinal) >= 0)
                {
                    fore = t;
                }
                else if (fore != null)
                {
                    if (t.name.IndexOf("Upperarm", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _arms.Add(new Arm { Upper = t, Fore = fore, Palm = palm, Side = side });
                    }
                    return;
                }
            }
        }

        private void CaptureRest(Player player)
        {
            var wanted = new HashSet<Transform>();
            foreach (Arm a in _arms)
            {
                foreach (Transform t in a.Upper.GetComponentsInChildren<Transform>(true))
                {
                    wanted.Add(t);
                }
            }
            var rest = new Dictionary<Transform, Quaternion>();
            foreach (SkinnedMeshRenderer smr in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Transform[] bones = smr.bones;
                Matrix4x4[] bind = smr.sharedMesh != null ? smr.sharedMesh.bindposes : null;
                if (bones == null || bind == null || bind.Length != bones.Length)
                {
                    continue;
                }
                var index = new Dictionary<Transform, int>();
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] != null && !index.ContainsKey(bones[i]))
                    {
                        index[bones[i]] = i;
                    }
                }
                for (int i = 0; i < bones.Length; i++)
                {
                    Transform b = bones[i];
                    if (b == null || rest.ContainsKey(b) || !wanted.Contains(b) || b.parent == null || !index.TryGetValue(b.parent, out int parent))
                    {
                        continue;
                    }
                    rest[b] = (bind[parent] * bind[i].inverse).rotation;
                }
            }
            foreach (KeyValuePair<Transform, Quaternion> kv in rest)
            {
                _rest.Add((kv.Key, kv.Value));
            }
        }

        private void Apply()
        {
            try
            {
                if (_player == null || _root == null || !_player.HandsIsEmpty || _player.HealthController == null || !_player.HealthController.IsAlive)
                {
                    return;
                }
                foreach ((Transform bone, Quaternion rest) r in _rest)
                {
                    if (r.bone != null)
                    {
                        r.bone.localRotation = r.rest;
                    }
                }
                Vector3 up = _root.up;
                Vector3 right = _root.right;
                Arm left = _arms.Find(x => x.Side < 0f);
                Arm rightArm = _arms.Find(x => x.Side > 0f);
                if (left?.Upper != null && rightArm?.Upper != null)
                {
                    Vector3 across = Vector3.ProjectOnPlane(rightArm.Upper.position - left.Upper.position, up);
                    if (across.sqrMagnitude > 1e-4f)
                    {
                        right = across.normalized;
                    }
                }
                Vector3 fwd = Vector3.Cross(right, up).normalized;
                foreach (Arm a in _arms)
                {
                    if (a.Upper == null || a.Fore == null || a.Palm == null)
                    {
                        continue;
                    }
                    Vector3 hang = (-up * 0.96f + right * (a.Side * 0.16f) + fwd * 0.04f).normalized;
                    Vector3 dir = a.Fore.position - a.Upper.position;
                    a.Upper.rotation = Quaternion.FromToRotation(dir, hang) * a.Upper.rotation;
                    Vector3 bend = (hang + fwd * 0.28f - right * (a.Side * 0.04f)).normalized;
                    Vector3 dir2 = a.Palm.position - a.Fore.position;
                    a.Fore.rotation = Quaternion.FromToRotation(dir2, bend) * a.Fore.rotation;
                }
            }
            catch (Exception ex)
            {
                PhotoLog.WarnOnce("relaxedarms", "Relaxed arms failed: " + ex.Message);
                Disable();
            }
        }
    }
}
