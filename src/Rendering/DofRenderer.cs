using System;
using System.Collections;
using System.Collections.Generic;
using DuckCam.Camera;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Rendering
{
    internal sealed class DofRenderer
    {
        private bool _cancel;

        public int Rings { get; set; } = 3;
        public int InnerPoints { get; set; } = 6;
        public float ApertureRadius { get; set; } = 0.025f;
        public int SettleFrames { get; set; } = 4;
        public float HighlightBoost { get; set; } = 1f;
        public float HighlightThreshold { get; set; } = 0.8f;

        public void ResetSettings()
        {
            Rings = 3;
            InnerPoints = 6;
            ApertureRadius = 0.025f;
            SettleFrames = 4;
            HighlightBoost = 1f;
            HighlightThreshold = 0.8f;
        }

        public bool Rendering { get; private set; }
        public bool HideOverlay { get; private set; }
        public float Progress { get; private set; }
        public string Status { get; private set; } = "idle";

        public int SampleCount
        {
            get
            {
                int n = 1;
                for (int k = 1; k <= Rings; k++)
                {
                    n += InnerPoints * k;
                }
                return n;
            }
        }

        public void Cancel()
        {
            _cancel = true;
        }

        public void Abort(PhotoCameraController camera, ScreenshotController shots)
        {
            if (!Rendering)
            {
                return;
            }
            HideOverlay = false;
            camera.ProjectionOverride = null;
            shots.ExternalCapture = false;
            Rendering = false;
            Status = "aborted";
        }

        public List<Vector2> Pattern()
        {
            var points = new List<Vector2> { Vector2.zero };
            for (int k = 1; k <= Rings; k++)
            {
                int count = InnerPoints * k;
                float radius = (float)k / Rings;
                float offset = k * 0.5f * Mathf.PI * 2f / count;
                for (int i = 0; i < count; i++)
                {
                    float a = offset + i * Mathf.PI * 2f / count;
                    points.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
                }
            }
            return points;
        }

        public IEnumerator Render(PhotoCameraController camera, DepthOfFieldController liveDof, ScreenshotController shots, Func<OverlayController> overlays, Func<DuckCam.Actors.ActorManager> actors)
        {
            if (Rendering)
            {
                yield break;
            }
            UnityEngine.Camera cam = camera.Camera;
            if (cam == null)
            {
                Status = "no camera";
                yield break;
            }

            Rendering = true;
            _cancel = false;
            Progress = 0f;
            shots.ExternalCapture = true;

            Vector3 basePos = camera.Position;
            Quaternion baseRot = camera.Rotation;
            float fov = camera.EffectiveFov;
            float focus = FocusDistance(cam, liveDof);
            bool liveDofWasOn = liveDof.Enabled;
            liveDof.SetEnabled(false);

            List<Vector2> pattern = Pattern();
            float[] acc = null;
            int width = 0;
            int height = 0;
            int done = 0;
            float[] toLinear = new float[256];
            for (int i = 0; i < 256; i++)
            {
                toLinear[i] = Mathf.Pow(i / 255f, 2.2f);
            }

            Status = $"rendering {pattern.Count} samples, focus {focus:0.00} m";
            PhotoLog.Info("DoF render: " + Status);
            try
            {
                foreach (Vector2 p in pattern)
                {
                    if (_cancel)
                    {
                        Status = "cancelled";
                        break;
                    }

                    Vector2 offset = p * ApertureRadius;
                    Vector3 pos = basePos + baseRot * new Vector3(offset.x, offset.y, 0f);
                    camera.DriveExternally(pos, baseRot, fov);
                    camera.ProjectionOverride = ShiftedProjection(cam, offset, focus);

                    for (int f = 0; f < SettleFrames; f++)
                    {
                        yield return null;
                    }
                    HideOverlay = true;
                    yield return new WaitForEndOfFrame();
                    Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                    HideOverlay = false;

                    Color32[] px = shot.GetPixels32();
                    if (acc == null)
                    {
                        width = shot.width;
                        height = shot.height;
                        acc = new float[px.Length * 3];
                    }
                    UnityEngine.Object.Destroy(shot);
                    if (px.Length * 3 != acc.Length)
                    {
                        Status = "resolution changed during render";
                        _cancel = true;
                        continue;
                    }
                    Accumulate(acc, px, toLinear);
                    done++;
                    Progress = (float)done / pattern.Count;
                }

                if (!_cancel && acc != null && done > 0)
                {
                    Texture2D result = Resolve(acc, done, width, height);
                    var extra = new Dictionary<string, object>
                    {
                        ["supersample"] = 1,
                        ["dofRender"] = new Dictionary<string, object>
                        {
                            ["samples"] = done,
                            ["rings"] = Rings,
                            ["innerPoints"] = InnerPoints,
                            ["apertureRadiusM"] = ApertureRadius,
                            ["focusDistanceM"] = focus,
                            ["settleFrames"] = SettleFrames,
                            ["highlightBoost"] = HighlightBoost
                        }
                    };
                    string path = shots.Save(result, camera, actors(), overlays(), extra);
                    Status = path != null ? "saved " + System.IO.Path.GetFileName(path) : "save failed: " + shots.LastError;
                }
            }
            finally
            {
                HideOverlay = false;
                camera.ProjectionOverride = null;
                camera.DriveExternally(basePos, baseRot, fov);
                camera.EndExternalDrive();
                if (liveDofWasOn)
                {
                    liveDof.SetEnabled(true);
                }
                shots.ExternalCapture = false;
                Rendering = false;
                PhotoLog.Info("DoF render finished: " + Status);
            }
        }

        private static float FocusDistance(UnityEngine.Camera cam, DepthOfFieldController liveDof)
        {
            if (liveDof.Enabled || liveDof.Mode == FocusMode.Manual)
            {
                return Mathf.Max(0.1f, liveDof.FocusDistance);
            }
            Transform t = cam.transform;
            return Physics.Raycast(t.position, t.forward, out RaycastHit hit, 2000f, ~0, QueryTriggerInteraction.Ignore)
                ? Mathf.Max(0.1f, hit.distance)
                : Mathf.Max(0.1f, liveDof.FocusDistance);
        }

        private static Matrix4x4 ShiftedProjection(UnityEngine.Camera cam, Vector2 offset, float focus)
        {
            Matrix4x4 p = Matrix4x4.Perspective(cam.fieldOfView, cam.aspect, cam.nearClipPlane, cam.farClipPlane);
            p.m02 -= p.m00 * offset.x / focus;
            p.m12 -= p.m11 * offset.y / focus;
            return p;
        }

        private void Accumulate(float[] acc, Color32[] px, float[] toLinear)
        {
            float boost = HighlightBoost;
            float threshold = HighlightThreshold;
            for (int i = 0, j = 0; i < px.Length; i++, j += 3)
            {
                Color32 c = px[i];
                float r = toLinear[c.r];
                float g = toLinear[c.g];
                float b = toLinear[c.b];
                if (boost > 0f)
                {
                    float m = Mathf.Max(r, Mathf.Max(g, b));
                    if (m > threshold)
                    {
                        float k = 1f + boost * Mathf.SmoothStep(0f, 1f, (m - threshold) / Mathf.Max(1e-4f, 1f - threshold));
                        r *= k;
                        g *= k;
                        b *= k;
                    }
                }
                acc[j] += r;
                acc[j + 1] += g;
                acc[j + 2] += b;
            }
        }

        private static Texture2D Resolve(float[] acc, int samples, int width, int height)
        {
            var px = new Color32[width * height];
            float inv = 1f / samples;
            const float invGamma = 1f / 2.2f;
            for (int i = 0, j = 0; i < px.Length; i++, j += 3)
            {
                px[i] = new Color32(
                    ToByte(Mathf.Pow(acc[j] * inv, invGamma)),
                    ToByte(Mathf.Pow(acc[j + 1] * inv, invGamma)),
                    ToByte(Mathf.Pow(acc[j + 2] * inv, invGamma)),
                    255);
            }
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        private static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
    }
}
