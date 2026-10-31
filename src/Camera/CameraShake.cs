using UnityEngine;

namespace DuckCam.Camera
{
    internal sealed class ShakeSettings
    {
        public bool Enabled;
        public float MoveFrequency = 1.5f;
        public float MoveStrength = 0.4f;
        public float RotationFrequency = 1.5f;
        public float RotationStrength = 0.4f;
    }

    internal sealed class CameraShake
    {
        private const float MetresPerStrength = 0.04f;
        private const float DegreesPerStrength = 1.2f;

        public ShakeSettings Manual { get; } = new ShakeSettings();
        public ShakeSettings Path { get; } = new ShakeSettings();

        public void Evaluate(bool pathPlayback, out Vector3 localOffset, out Quaternion rotationOffset)
        {
            ShakeSettings s = pathPlayback ? Path : Manual;
            if (!s.Enabled || (s.MoveStrength <= 0f && s.RotationStrength <= 0f))
            {
                localOffset = Vector3.zero;
                rotationOffset = Quaternion.identity;
                return;
            }

            float t = Time.unscaledTime;
            float mt = t * s.MoveFrequency;
            float rt = t * s.RotationFrequency;
            localOffset = new Vector3(Noise(mt, 0.1f), Noise(mt, 3.7f), Noise(mt, 7.3f) * 0.5f) * (s.MoveStrength * MetresPerStrength);
            rotationOffset = Quaternion.Euler(
                Noise(rt, 11.1f) * s.RotationStrength * DegreesPerStrength,
                Noise(rt, 17.9f) * s.RotationStrength * DegreesPerStrength,
                Noise(rt, 23.3f) * s.RotationStrength * DegreesPerStrength * 0.5f);
        }

        private static float Noise(float t, float seed) => (Mathf.PerlinNoise(t, seed) - 0.5f) * 2f;
    }
}
