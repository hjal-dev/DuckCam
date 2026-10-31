using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DuckCam.Culling
{
    internal static class VisibilityDiagnostics
    {
        public static string LastReport { get; private set; } = "No scan yet. Use \"Scan visibility\" in the Diagnostics tab.";

        public static void Scan(UnityEngine.Camera camera, float radius)
        {
            if (camera == null)
            {
                LastReport = "No camera.";
                return;
            }

            Vector3 origin = camera.transform.position;
            Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(camera);
            float r2 = radius * radius;

            int lodGroups = 0;
            var lodHistogram = new Dictionary<int, int>();
            foreach (LODGroup group in Object.FindObjectsOfType<LODGroup>())
            {
                if ((group.transform.position - origin).sqrMagnitude > r2)
                {
                    continue;
                }
                lodGroups++;
                int current = CurrentLod(group);
                lodHistogram.TryGetValue(current, out int n);
                lodHistogram[current] = n + 1;
            }

            int inFrustum = 0;
            int invisibleInFrustum = 0;
            int forcedOff = 0;
            var examples = new List<string>();
            foreach (Renderer renderer in Object.FindObjectsOfType<Renderer>())
            {
                if (!renderer.enabled || (renderer.bounds.center - origin).sqrMagnitude > r2)
                {
                    continue;
                }
                if (!GeometryUtility.TestPlanesAABB(frustum, renderer.bounds))
                {
                    continue;
                }
                inFrustum++;
                if (renderer.forceRenderingOff)
                {
                    forcedOff++;
                }
                if (!renderer.isVisible && (camera.cullingMask & (1 << renderer.gameObject.layer)) != 0)
                {
                    invisibleInFrustum++;
                    if (examples.Count < 8)
                    {
                        examples.Add($"{renderer.name} ({LayerMask.LayerToName(renderer.gameObject.layer)}, {Vector3.Distance(origin, renderer.bounds.center):F0} m)");
                    }
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Scan at {origin} radius {radius:F0} m, lodBias {QualitySettings.lodBias:F2}, occlusion {(camera.useOcclusionCulling ? "on" : "off")}");
            sb.Append($"LODGroups in radius: {lodGroups}; current LOD histogram (-1 = culled/unknown):");
            foreach (KeyValuePair<int, int> kv in lodHistogram)
            {
                sb.Append($" [{kv.Key}]={kv.Value}");
            }
            sb.AppendLine();
            sb.AppendLine($"Enabled renderers in frustum: {inFrustum}, not visible last frame: {invisibleInFrustum} (occluded or culled), forceRenderingOff: {forcedOff}");
            foreach (string e in examples)
            {
                sb.AppendLine("  invisible: " + e);
            }
            LastReport = sb.ToString();
        }

        private static int CurrentLod(LODGroup group)
        {
            LOD[] lods = group.GetLODs();
            for (int i = 0; i < lods.Length; i++)
            {
                foreach (Renderer r in lods[i].renderers)
                {
                    if (r != null && r.isVisible)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }
    }
}
