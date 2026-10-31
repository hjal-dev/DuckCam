using UnityEngine;

namespace DuckCam.Camera
{
    internal struct CameraPose
    {
        public Vector3 Position;
        public float Yaw;
        public float Pitch;
        public float Roll;
        public float Fov;
    }

    internal sealed class CameraBookmarks
    {
        public const int Count = 9;

        private readonly CameraPose?[] _slots = new CameraPose?[Count];

        public bool Has(int index) => index >= 0 && index < Count && _slots[index].HasValue;

        public void Store(int index, CameraPose pose)
        {
            if (index >= 0 && index < Count)
            {
                _slots[index] = pose;
            }
        }

        public bool TryGet(int index, out CameraPose pose)
        {
            pose = default;
            if (!Has(index))
            {
                return false;
            }
            pose = _slots[index].Value;
            return true;
        }

        public void Clear()
        {
            for (int i = 0; i < Count; i++)
            {
                _slots[i] = null;
            }
        }
    }
}
