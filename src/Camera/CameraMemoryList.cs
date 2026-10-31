using System.Collections.Generic;

namespace DuckCam.Camera
{
    internal sealed class CameraMemoryList
    {
        private readonly List<CameraPose> _poses = new List<CameraPose>();
        private int _index = -1;

        public int Count => _poses.Count;
        public int Index => _index;

        public void Add(CameraPose pose)
        {
            _poses.Add(pose);
        }

        public bool Advance(out CameraPose pose)
        {
            pose = default;
            if (_poses.Count == 0)
            {
                return false;
            }
            _index = (_index + 1) % _poses.Count;
            pose = _poses[_index];
            return true;
        }

        public void Clear()
        {
            _poses.Clear();
            _index = -1;
        }
    }
}
