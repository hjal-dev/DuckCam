using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Camera
{
    internal static class CameraCollision
    {
        private const float Skin = 0.02f;

        public static Vector3 Resolve(Vector3 from, Vector3 to, CameraCollisionMode mode, float radius)
        {
            if (mode == CameraCollisionMode.Off)
            {
                return to;
            }

            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 1e-5f)
            {
                return to;
            }

            int mask = LayersMaskController.HighPolyWithTerrainMask;
            Vector3 dir = delta / distance;
            if (!Physics.SphereCast(from, radius, dir, out RaycastHit hit, distance, mask, QueryTriggerInteraction.Ignore))
            {
                return to;
            }

            Vector3 allowed = from + dir * Mathf.Max(0f, hit.distance - Skin);
            if (mode == CameraCollisionMode.Strict)
            {
                return allowed;
            }

            Vector3 slide = Vector3.ProjectOnPlane(to - allowed, hit.normal);
            float slideDistance = slide.magnitude;
            if (slideDistance < 1e-5f)
            {
                return allowed;
            }

            Vector3 slideDir = slide / slideDistance;
            if (Physics.SphereCast(allowed, radius, slideDir, out RaycastHit hit2, slideDistance, mask, QueryTriggerInteraction.Ignore))
            {
                return allowed + slideDir * Mathf.Max(0f, hit2.distance - Skin);
            }
            return allowed + slide;
        }
    }
}
