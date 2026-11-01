using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Lighting
{
    internal sealed class RigLight
    {
        public string Name;
        public int Type;
        public float Intensity;
        public float Range;
        public float SpotAngle;
        public bool Shadows;
        public bool Enabled = true;
        public float[] Color;
        public float[] LocalPosition;
        public float[] LocalRotation;
        public float[] WorldPosition;
        public float[] WorldRotation;
    }

    internal sealed class LightRigFile
    {
        public int Version = 1;
        public string Anchor;
        public string SavedOnMap;
        public List<RigLight> Lights = new List<RigLight>();
    }

    internal static class LightRigs
    {
        public static readonly string[] AnchorNames = { "Camera", "Selected Character" };

        public static string Directory => Path.Combine(DuckCamPlugin.DataRoot, "LightRigs");

        public static string[] List()
        {
            if (!System.IO.Directory.Exists(Directory))
            {
                return Array.Empty<string>();
            }
            return ContentPacks.List(Directory);
        }

        public static LightRigFile Capture(IEnumerable<StudioLight> lights, Transform anchor, string anchorName)
        {
            var file = new LightRigFile { Anchor = anchorName, SavedOnMap = GameContext.LocationId };
            foreach (StudioLight s in lights)
            {
                Light l = s?.Light;
                if (l == null)
                {
                    continue;
                }
                Transform t = l.transform;
                Vector3 localPos = anchor != null ? anchor.InverseTransformPoint(t.position) : t.position;
                Quaternion localRot = anchor != null ? Quaternion.Inverse(anchor.rotation) * t.rotation : t.rotation;
                file.Lights.Add(new RigLight
                {
                    Name = s.Name,
                    Type = (int)l.type,
                    Intensity = l.intensity,
                    Range = l.range,
                    SpotAngle = l.spotAngle,
                    Shadows = l.shadows != LightShadows.None,
                    Enabled = l.enabled,
                    Color = new[] { l.color.r, l.color.g, l.color.b },
                    LocalPosition = V(localPos),
                    LocalRotation = Q(localRot),
                    WorldPosition = V(t.position),
                    WorldRotation = Q(t.rotation)
                });
            }
            return file;
        }

        public static int Apply(LightRigFile file, LightingStudio studio, RestoreStack stack, Transform anchor, bool worldPoses)
        {
            int created = 0;
            if (file?.Lights == null)
            {
                return 0;
            }
            var temp = new GameObject("DuckCam_RigPlacement");
            try
            {
                foreach (RigLight r in file.Lights)
                {
                    Vector3 pos;
                    Quaternion rot;
                    if (worldPoses && r.WorldPosition != null && r.WorldRotation != null)
                    {
                        pos = V(r.WorldPosition);
                        rot = Q(r.WorldRotation);
                    }
                    else if (anchor != null && r.LocalPosition != null && r.LocalRotation != null)
                    {
                        pos = anchor.TransformPoint(V(r.LocalPosition));
                        rot = anchor.rotation * Q(r.LocalRotation);
                    }
                    else
                    {
                        continue;
                    }
                    temp.transform.SetPositionAndRotation(pos, rot);
                    Color color = r.Color != null && r.Color.Length >= 3 ? new Color(r.Color[0], r.Color[1], r.Color[2]) : Color.white;
                    StudioLight s = studio.Create(stack, r.Name, (LightType)r.Type, r.Intensity, r.Range, color, r.SpotAngle, r.Shadows, temp.transform);
                    s.Light.enabled = r.Enabled;
                    created++;
                }
            }
            finally
            {
                UnityEngine.Object.Destroy(temp);
            }
            return created;
        }

        public static string Save(LightRigFile file, string name, string pack = null)
        {
            string path = ContentPacks.SaveFile(Directory, pack, Sanitize(name));
            File.WriteAllText(path, JsonConvert.SerializeObject(file, Formatting.Indented));
            ContentPacks.Invalidate(Directory);
            return path;
        }

        public static LightRigFile Load(string name, out string error)
        {
            error = null;
            try
            {
                return JsonConvert.DeserializeObject<LightRigFile>(File.ReadAllText(ContentPacks.FileOf(Directory, name)));
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        public static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "rig";
            }
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Trim();
        }

        public static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
        public static float[] Q(Quaternion q) => new[] { q.x, q.y, q.z, q.w };
        public static Vector3 V(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
        public static Quaternion Q(float[] a) => a != null && a.Length >= 4 ? new Quaternion(a[0], a[1], a[2], a[3]).normalized : Quaternion.identity;
    }
}
