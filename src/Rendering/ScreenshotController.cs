using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using System.Linq;
using System.Threading.Tasks;
using DuckCam.Actors;
using DuckCam.Camera;
using DuckCam.Core;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace DuckCam.Rendering
{
    internal sealed class ScreenshotController
    {
        private int _sequence;

        public bool Capturing { get; private set; }
        public string LastPath { get; private set; }
        public string LastError { get; private set; }

        public static string Directory => Path.Combine(DuckCamPlugin.DataRoot, "Screenshots");

        public bool ExternalCapture { get; set; }

        public IEnumerator Capture(PhotoCameraController camera, ActorManager actors, OverlayController overlays)
        {
            if (Capturing || ExternalCapture)
            {
                yield break;
            }
            Capturing = true;
            LastError = null;
            HiResRenderer.Request request = CurrentRequest();

            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            bool screen = request.Resolution == CaptureResolution.Screen && request.Supersample <= 1 && !request.Transparent;
            var renderer = new HiResRenderer();
            IEnumerator render = screen ? null : renderer.Render(camera, actors, request);
            _renderer = renderer;
            while (render != null)
            {
                object step;
                try
                {
                    if (!render.MoveNext())
                    {
                        break;
                    }
                    step = render.Current;
                }
                catch (Exception ex)
                {
                    LastError = "High-res render failed: " + ex.Message;
                    PhotoLog.Error("High-res render failed: " + ex);
                    _renderer = null;
                    Capturing = false;
                    yield break;
                }
                yield return step;
            }
            try
            {
                if (screen)
                {
                    Save(ScreenCapture.CaptureScreenshotAsTexture(), camera, actors, overlays, null);
                }
                else if (renderer.Result == null)
                {
                    LastError = "High-res render failed: " + (renderer.Error ?? "unknown error");
                }
                else
                {
                    Save(renderer.Result, camera, actors, request.Transparent ? null : overlays, renderer.Info, request.Transparent);
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                PhotoLog.Error("Photo failed: " + ex);
            }
            Task pending = _pendingWrite;
            while (pending != null && !pending.IsCompleted)
            {
                yield return null;
            }
            _pendingWrite = null;
            if (_pendingError != null)
            {
                LastError = _pendingError;
                LastPath = null;
                _pendingError = null;
            }
            if (LastError != null)
            {
                PhotoLog.Warn("Photo failed: " + LastError);
            }
            _renderer = null;
            Capturing = false;
        }

        private HiResRenderer _renderer;

        public bool DecorOnScreen => !Capturing || _renderer == null || !_renderer.ScreenIsComposite;

        private Task _pendingWrite;
        private volatile string _pendingError;

        public static HiResRenderer.Request CurrentRequest()
        {
            return new HiResRenderer.Request
            {
                Resolution = DuckCamConfig.PhotoResolution.Value,
                Supersample = DuckCamConfig.PhotoSupersample.Value,
                Transparent = DuckCamConfig.TransparentBackground.Value,
                Subjects = DuckCamConfig.CutoutSubjects.Value,
                KeepScene = DuckCamConfig.CutoutKeepScene.Value
            };
        }

        public string Save(Texture2D texture, PhotoCameraController camera, ActorManager actors, OverlayController overlays, Dictionary<string, object> extra, bool transparent = false)
        {
            try
            {
                RectInt region = CompositionOverlay.FrameRect(texture.width, texture.height, DuckCamConfig.FrameAspectRatio.Value);
                overlays?.Composite(texture, region);
                texture = CropToFrame(texture);
                if (!transparent)
                {
                    texture = Opaque(texture);
                }
                System.IO.Directory.CreateDirectory(Directory);
                string path = NextPath();
                int width = texture.width;
                int height = texture.height;
                bool rawOk = texture.format == TextureFormat.RGBA32 || texture.format == TextureFormat.RGB24;
                byte[] png = null;
                if ((long)width * height > AsyncEncodePixels && rawOk)
                {
                    GraphicsFormat format = texture.format == TextureFormat.RGBA32 ? GraphicsFormat.R8G8B8A8_UNorm : GraphicsFormat.R8G8B8_UNorm;
                    byte[] raw = texture.GetRawTextureData();
                    try
                    {
                        png = ImageConversion.EncodeArrayToPNG(raw, format, (uint)width, (uint)height);
                    }
                    catch (Exception ex)
                    {
                        PhotoLog.Warn($"PNG array encode failed ({format}), using the texture encoder: {ex.Message}");
                    }
                }
                if (png == null)
                {
                    png = texture.EncodeToPNG();
                }
                if (png == null || png.Length == 0)
                {
                    throw new InvalidOperationException($"the {width}x{height} image could not be encoded to PNG.");
                }
                byte[] bytes = png;
                PhotoLog.Info($"Screenshot {width}x{height} encoded ({bytes.Length / 1048576f:0.0} MB), writing: {path}");
                _pendingError = null;
                _pendingWrite = Task.Run(() =>
                {
                    try
                    {
                        File.WriteAllBytes(path, bytes);
                        PhotoLog.Info("Screenshot written: " + path);
                    }
                    catch (Exception ex)
                    {
                        _pendingError = "writing the file failed: " + ex.Message;
                        PhotoLog.Error("Writing " + path + " failed: " + ex);
                    }
                });
                LastPath = path;

                if (DuckCamConfig.WriteSidecar.Value)
                {
                    WriteSidecar(Path.ChangeExtension(path, ".json"), camera, actors, width, height, extra);
                }
                return path;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                PhotoLog.Error("Screenshot failed: " + ex);
                return null;
            }
            finally
            {
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }
        }

        private const long AsyncEncodePixels = 3840L * 2160L;

        private static Texture2D CropToFrame(Texture2D source)
        {
            FrameAspect aspect = DuckCamConfig.FrameAspectRatio.Value;
            if (!DuckCamConfig.CropToFrame.Value || aspect == FrameAspect.Native)
            {
                return source;
            }
            RectInt r = CompositionOverlay.FrameRect(source.width, source.height, aspect);
            if (r.width == source.width && r.height == source.height)
            {
                return source;
            }
            int bpp = source.format == TextureFormat.RGBA32 ? 4 : source.format == TextureFormat.RGB24 ? 3 : 0;
            if (bpp == 0)
            {
                var plain = new Texture2D(r.width, r.height, TextureFormat.RGB24, false);
                plain.SetPixels(source.GetPixels(r.x, r.y, r.width, r.height));
                plain.Apply();
                UnityEngine.Object.Destroy(source);
                return plain;
            }
            var cropped = new Texture2D(r.width, r.height, source.format, false);
            NativeArray<byte> src = source.GetRawTextureData<byte>();
            NativeArray<byte> dst = cropped.GetRawTextureData<byte>();
            for (int y = 0; y < r.height; y++)
            {
                NativeArray<byte>.Copy(src, ((r.y + y) * source.width + r.x) * bpp, dst, y * r.width * bpp, r.width * bpp);
            }
            cropped.Apply(false);
            UnityEngine.Object.Destroy(source);
            return cropped;
        }

        private static Texture2D Opaque(Texture2D source)
        {
            if (source.format == TextureFormat.RGB24)
            {
                return source;
            }
            var opaque = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
            opaque.SetPixels32(source.GetPixels32());
            opaque.Apply();
            UnityEngine.Object.Destroy(source);
            return opaque;
        }

        private string NextPath()
        {
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string map = Sanitize(GameContext.MapDisplayName);
            string path;
            do
            {
                _sequence++;
                path = Path.Combine(Directory, $"DuckCam_{stamp}_{map}_{_sequence:000}.png");
            }
            while (File.Exists(path));
            return path;
        }

        private static void WriteSidecar(string path, PhotoCameraController camera, ActorManager actors, int width, int height, Dictionary<string, object> extra)
        {
            UnityEngine.Camera cam = camera.Camera;
            Transform t = cam != null ? cam.transform : null;
            var data = new Dictionary<string, object>
            {
                ["mod"] = DuckCamPlugin.Name + " " + DuckCamPlugin.Version,
                ["timestampLocal"] = DateTime.Now.ToString("o"),
                ["map"] = GameContext.MapDisplayName,
                ["locationId"] = GameContext.LocationId,
                ["resolution"] = new[] { width, height },
                ["frameAspect"] = CompositionOverlay.AspectNames[(int)DuckCamConfig.FrameAspectRatio.Value],
                ["croppedToFrame"] = DuckCamConfig.CropToFrame.Value && DuckCamConfig.FrameAspectRatio.Value != FrameAspect.Native,
                ["camera"] = new Dictionary<string, object>
                {
                    ["mode"] = camera.Mode.ToString(),
                    ["position"] = t != null ? new[] { t.position.x, t.position.y, t.position.z } : null,
                    ["rotationEuler"] = t != null ? new[] { t.eulerAngles.x, t.eulerAngles.y, t.eulerAngles.z } : null,
                    ["fov"] = cam != null ? cam.fieldOfView : 0f,
                    ["nearClip"] = cam != null ? cam.nearClipPlane : 0f,
                    ["farClip"] = cam != null ? cam.farClipPlane : 0f,
                    ["distanceFromAnchor"] = camera.DistanceFromAnchor
                },
                ["actors"] = actors == null ? null : actors.Actors.Select(a => new Dictionary<string, object>
                {
                    ["name"] = a.Name,
                    ["info"] = a.Info,
                    ["isPmc"] = a.IsPmc,
                    ["visible"] = a.Visible,
                    ["position"] = a.Root != null ? new[] { a.Root.position.x, a.Root.position.y, a.Root.position.z } : null,
                    ["yaw"] = a.Root != null ? a.Root.eulerAngles.y : 0f,
                    ["gearRackPose"] = a.CurrentPose?.PoseName,
                    ["poseDisplayName"] = a.CurrentPose?.DisplayName,
                    ["normalizedTime"] = a.NormalizedTime,
                    ["lookAtCamera"] = a.LookAtCamera,
                    ["inHandsWeaponVisible"] = a.WeaponVisible
                }).ToList()
            };
            if (extra != null)
            {
                foreach (KeyValuePair<string, object> kv in extra)
                {
                    data[kv.Key] = kv.Value;
                }
            }
            File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
        }

        private static string Sanitize(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                s = s.Replace(c, '_');
            }
            return s.Replace(' ', '_');
        }
    }
}
