using UnityEngine;

namespace DuckCam.Core
{
    internal static class ScreenSpace
    {
        public static Vector3 WorldToScreen(UnityEngine.Camera cam, Vector3 world)
        {
            Vector3 v = cam.WorldToViewportPoint(world);
            return new Vector3(v.x * Screen.width, v.y * Screen.height, v.z);
        }

        public static bool WorldToGui(UnityEngine.Camera cam, Vector3 world, out Vector2 gui)
        {
            Vector3 s = WorldToScreen(cam, world);
            gui = new Vector2(s.x, Screen.height - s.y);
            return s.z > 0.05f;
        }

        public static Ray ScreenRay(UnityEngine.Camera cam, Vector2 screen)
        {
            return cam.ViewportPointToRay(new Vector3(screen.x / Mathf.Max(1, Screen.width), screen.y / Mathf.Max(1, Screen.height), 0f));
        }
    }
}
