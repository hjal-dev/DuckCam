using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Pause
{
    internal sealed class PoseHold
    {
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly HashSet<int> _ids = new HashSet<int>();
        private bool _hooked;

        public PoseHold(bool pinRoot)
        {
            PinRoot = pinRoot;
        }

        public bool PinRoot { get; }
        public int Count => _entries.Count;

        public void Add(Player player)
        {
            if (player == null || !_ids.Add(player.GetInstanceID()))
            {
                return;
            }
            _entries.Add(new Entry(player, PinRoot));
            if (!_hooked)
            {
                Application.onBeforeRender += Apply;
                _hooked = true;
            }
        }

        public void Clear()
        {
            if (_hooked)
            {
                Application.onBeforeRender -= Apply;
                _hooked = false;
            }
            _entries.Clear();
            _ids.Clear();
        }

        private void Apply()
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                bool keep;
                try
                {
                    keep = _entries[i].Hold();
                }
                catch (Exception ex)
                {
                    PhotoLog.WarnOnce("posehold", "Pose hold failed for one character: " + ex.Message);
                    keep = false;
                }
                if (!keep)
                {
                    _entries.RemoveAt(i);
                }
            }
        }

        private sealed class Entry
        {
            private readonly Player _player;
            private readonly bool _pinRoot;
            private Transform _root;
            private Vector3 _rootPos;
            private Quaternion _rootRot;
            private Transform[] _bones;
            private Vector3[] _pos;
            private Quaternion[] _rot;

            public Entry(Player player, bool pinRoot)
            {
                _player = player;
                _pinRoot = pinRoot;
            }

            public bool Hold()
            {
                if (_player == null || _player.HealthController == null || !_player.HealthController.IsAlive)
                {
                    return false;
                }
                if (_bones == null)
                {
                    _root = _player.Transform?.Original;
                    if (_root == null)
                    {
                        return false;
                    }
                    _rootPos = _root.position;
                    _rootRot = _root.rotation;
                    var all = new List<Transform>(_root.GetComponentsInChildren<Transform>(true).Where(t => t != _root));
                    GameObject hands = null;
                    try
                    {
                        hands = _player.HandsController?.ControllerGameObject;
                    }
                    catch (Exception)
                    {
                    }
                    if (hands != null && !hands.transform.IsChildOf(_root))
                    {
                        all.AddRange(hands.GetComponentsInChildren<Transform>(true));
                    }
                    _bones = all.ToArray();
                    _pos = new Vector3[_bones.Length];
                    _rot = new Quaternion[_bones.Length];
                    for (int i = 0; i < _bones.Length; i++)
                    {
                        _pos[i] = _bones[i].localPosition;
                        _rot[i] = _bones[i].localRotation;
                    }
                    return true;
                }
                if (_pinRoot && _root != null && (_root.position != _rootPos || _root.rotation != _rootRot))
                {
                    _root.SetPositionAndRotation(_rootPos, _rootRot);
                }
                for (int i = 0; i < _bones.Length; i++)
                {
                    Transform t = _bones[i];
                    if (t != null)
                    {
                        t.localPosition = _pos[i];
                        t.localRotation = _rot[i];
                    }
                }
                return true;
            }
        }
    }
}
