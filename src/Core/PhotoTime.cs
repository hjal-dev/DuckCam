using UnityEngine;

namespace DuckCam.Core
{
    internal static class PhotoTime
    {
        public static float FixedStep;

        public static float Unscaled => FixedStep > 0f ? FixedStep : Time.unscaledDeltaTime;

        public static float Scaled => FixedStep > 0f ? FixedStep * Time.timeScale : Time.deltaTime;
    }
}
