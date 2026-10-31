using System.Collections.Generic;
using UnityEngine;

namespace DuckCam.Camera
{
    internal sealed class PathSet
    {
        private readonly List<CameraPath> _paths = new List<CameraPath>();

        public PathSet()
        {
            _paths.Add(new CameraPath { Name = "Path 1" });
        }

        public IReadOnlyList<CameraPath> Paths => _paths;
        public int ActiveIndex { get; private set; }
        public CameraPath Active => _paths[ActiveIndex];

        public void Select(int index)
        {
            if (index < 0 || index >= _paths.Count || index == ActiveIndex)
            {
                return;
            }
            Active.Stop();
            ActiveIndex = index;
        }

        public bool CutTo(int index)
        {
            if (index < 0 || index >= _paths.Count || !_paths[index].CanPlay)
            {
                return false;
            }
            float progress = Active.Playing ? Active.Progress : 0f;
            bool loop = Active.Loop;
            Active.Stop();
            ActiveIndex = index;
            Active.Loop = Active.Loop || loop;
            Active.PlayFrom(progress);
            return true;
        }

        public CameraPath Add()
        {
            var path = new CameraPath { Name = "Path " + (_paths.Count + 1) };
            _paths.Add(path);
            Select(_paths.Count - 1);
            return path;
        }

        public CameraPath Duplicate()
        {
            CameraPath source = Active;
            var copy = new CameraPath
            {
                Name = source.Name + " copy",
                Duration = source.Duration,
                Loop = source.Loop,
                ConstantSpeed = source.ConstantSpeed,
                EaseIn = source.EaseIn,
                EaseOut = source.EaseOut,
                UseCountdown = source.UseCountdown,
                PerPointTiming = source.PerPointTiming,
                BaseSpeed = source.BaseSpeed
            };
            foreach (PathKeyframe k in source.Keys)
            {
                copy.Add(k.Clone());
            }
            _paths.Add(copy);
            Select(_paths.Count - 1);
            return copy;
        }

        public void RemoveActive()
        {
            Active.Stop();
            if (_paths.Count == 1)
            {
                Active.Clear();
                return;
            }
            _paths.RemoveAt(ActiveIndex);
            ActiveIndex = Mathf.Clamp(ActiveIndex, 0, _paths.Count - 1);
            for (int i = 0; i < _paths.Count; i++)
            {
                if (_paths[i].Name.StartsWith("Path ") && int.TryParse(_paths[i].Name.Substring(5), out _))
                {
                    _paths[i].Name = "Path " + (i + 1);
                }
            }
        }

        public void StopAll()
        {
            foreach (CameraPath p in _paths)
            {
                p.Stop();
            }
        }
    }
}
