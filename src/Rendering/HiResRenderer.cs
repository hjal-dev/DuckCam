using System;
using System.Collections;
using System.Collections.Generic;
using EFT;
using DuckCam.Actors;
using DuckCam.Camera;
using DuckCam.Core;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace DuckCam.Rendering
{
    public enum CaptureResolution
    {
        Screen,
        P1080,
        P1440,
        P2160,
        P4320,
        P8640
    }

    public enum CutoutSubjects
    {
        AllCharacters,
        PhotoCharacters
    }

    internal sealed class HiResRenderer
    {
        public static readonly string[] ResolutionNames = { "Screen", "1080p", "1440p", "4K · 2160p", "8K · 4320p", "16K · 8640p" };
        private static readonly int[] Heights = { 0, 1080, 1440, 2160, 4320, 8640 };

        private const int MaxSide = 16384;

        public struct Request
        {
            public CaptureResolution Resolution;
            public int Supersample;
            public bool Transparent;
            public CutoutSubjects Subjects;
            public bool KeepScene;
        }

        public Texture2D Result { get; private set; }
        public string Error { get; private set; }
        public Dictionary<string, object> Info { get; } = new Dictionary<string, object>();

        public static Vector2Int OutputSize(CaptureResolution resolution)
        {
            int h = Heights[Mathf.Clamp((int)resolution, 0, Heights.Length - 1)];
            if (h == 0)
            {
                return new Vector2Int(Screen.width, Screen.height);
            }
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            int w = Mathf.RoundToInt(h * aspect);
            if (w > MaxSide)
            {
                w = MaxSide;
                h = Mathf.RoundToInt(w / aspect);
            }
            return new Vector2Int(w, Mathf.Min(h, MaxSide));
        }

        public static int EffectiveSupersample(Request request)
        {
            Vector2Int size = OutputSize(request.Resolution);
            int k = Mathf.Clamp(request.Supersample, 1, 4);
            if (request.Transparent)
            {
                k = Mathf.Max(k, 2);
            }
            while (k > 1 && (size.x * k > MaxSide || size.y * k > MaxSide))
            {
                k--;
            }
            return k;
        }

        private const int GuardMinPercent = 5;
        private const int SettleFrames = 12;

        private struct Layout
        {
            public int Tiles;
            public int ScreenWidth;
            public int ScreenHeight;
            public int CoreWidth;
            public int CoreHeight;
            public int GuardX;
            public int GuardY;
            public int Width => Tiles * CoreWidth;
            public int Height => Tiles * CoreHeight;
        }

        private static Layout Plan(int targetWidth)
        {
            int sw = Mathf.Max(16, Screen.width);
            int sh = Mathf.Max(16, Screen.height);
            var l = new Layout { Tiles = 1, ScreenWidth = sw, ScreenHeight = sh, CoreWidth = sw, CoreHeight = sh };
            if (targetWidth <= sw)
            {
                return l;
            }
            int gx = PickGuard(sw, sh);
            int cw = sw - 2 * gx;
            int ch = Mathf.RoundToInt(sh * (float)cw / sw);
            int maxTiles = Mathf.Max(2, Mathf.Min(MaxSide / cw, MaxSide / Mathf.Max(1, ch)));
            l.Tiles = Mathf.Clamp(Mathf.CeilToInt(targetWidth / (float)cw), 2, maxTiles);
            l.CoreWidth = cw;
            l.CoreHeight = ch;
            l.GuardX = gx;
            l.GuardY = (sh - ch) / 2;
            return l;
        }

        private static int PickGuard(int sw, int sh)
        {
            int start = Mathf.CeilToInt(sw * GuardMinPercent / 100f);
            for (int g = start; g <= sw / 10; g++)
            {
                int cw = sw - 2 * g;
                if ((sh * cw) % sw == 0 && (sh - sh * cw / sw) % 2 == 0)
                {
                    return g;
                }
            }
            return start;
        }

        public static int TileCount(int width) => Plan(width).Tiles;

        public bool ScreenIsComposite { get; private set; }

        private sealed class CameraState
        {
            public int Mask;
            public CameraClearFlags Clear;
            public Color Background;
            public bool Occlusion;

            public CameraState(UnityEngine.Camera c)
            {
                Mask = c.cullingMask;
                Clear = c.clearFlags;
                Background = c.backgroundColor;
                Occlusion = c.useOcclusionCulling;
            }

            public void Restore(UnityEngine.Camera c)
            {
                c.cullingMask = Mask;
                c.clearFlags = Clear;
                c.backgroundColor = Background;
                c.useOcclusionCulling = Occlusion;
            }
        }

        public IEnumerator Render(PhotoCameraController camera, ActorManager actors, Request request)
        {
            UnityEngine.Camera cam = camera?.Camera;
            if (cam == null)
            {
                throw new InvalidOperationException("no camera.");
            }

            Vector2Int output = OutputSize(request.Resolution);
            int k = EffectiveSupersample(request);
            Layout l = Plan(output.x * k);
            bool tiled = l.Tiles > 1;
            bool hideWorld = request.Transparent && !request.KeepScene;
            Info["outputResolution"] = new[] { output.x, output.y };
            Info["supersample"] = k;
            Info["tiles"] = l.Tiles * l.Tiles;
            Info["compositeResolution"] = new[] { l.Width, l.Height };
            Info["transparent"] = request.Transparent;
            PhotoLog.Info($"High-res capture: {output.x}x{output.y}, supersample {k}x, {l.Tiles}x{l.Tiles} screen tile(s), core {l.CoreWidth}x{l.CoreHeight}, guard {l.GuardX}x{l.GuardY}, composite {l.Width}x{l.Height}, transparent {request.Transparent}.");

            var state = new CameraState(cam);
            var restores = new List<Action>();
            PostProcessLayer pp = cam.GetComponent<PostProcessLayer>();
            GameObject maskGo = null;
            UnityEngine.Camera maskCam = null;
            List<Renderer> hidden = null;
            int subjectMask = 0;
            Texture2D composite = null;
            try
            {
                ScreenIsComposite = tiled || request.Transparent;
                if (tiled)
                {
                    DisableLensEffects(cam, restores);
                }
                if (request.Transparent)
                {
                    List<Transform> roots = SubjectRoots(actors, request.Subjects);
                    subjectMask = SubjectLayers(roots);
                    if (roots.Count == 0 || subjectMask == 0)
                    {
                        throw new InvalidOperationException(request.Subjects == CutoutSubjects.PhotoCharacters
                            ? "no visible photo characters to cut out."
                            : "no visible characters to cut out.");
                    }
                    hidden = OtherRenderers(roots, subjectMask);
                    maskGo = new GameObject("DuckCam_CutoutMaskCamera");
                    maskCam = maskGo.AddComponent<UnityEngine.Camera>();
                    maskCam.enabled = false;
                    if (hideWorld)
                    {
                        List<Renderer> h = hidden;
                        SetHidden(h, true);
                        restores.Add(() => SetHidden(h, false));
                        HideTerrain(restores);
                    }
                    Info["cutoutSubjects"] = request.Subjects.ToString();
                    Info["cutoutKeepScene"] = request.KeepScene;
                    Info["hiddenRenderers"] = hidden.Count;
                }

                composite = new Texture2D(l.Width, l.Height, TextureFormat.RGBA32, false);

                const float sensorHeight = 24f;
                float fov = camera.EffectiveFov;
                float focal = sensorHeight / (2f * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
                float aspect = (float)l.ScreenWidth / l.ScreenHeight;
                float footprint = (float)l.ScreenWidth / l.CoreWidth / l.Tiles;
                bool settle = tiled || hideWorld;

                for (int j = 0; j < l.Tiles; j++)
                {
                    for (int i = 0; i < l.Tiles; i++)
                    {
                        if (tiled)
                        {
                            camera.LensOverride = new PhotoCameraController.Lens
                            {
                                Sensor = new Vector2(sensorHeight * aspect * footprint, sensorHeight * footprint),
                                Focal = focal,
                                Shift = new Vector2(((i + 0.5f) / l.Tiles - 0.5f) / footprint, ((j + 0.5f) / l.Tiles - 0.5f) / footprint)
                            };
                        }
                        if (settle)
                        {
                            pp?.ResetHistory();
                        }
                        int frames = settle ? SettleFrames : 1;
                        for (int f = 0; f < frames; f++)
                        {
                            if (hideWorld)
                            {
                                cam.cullingMask = subjectMask;
                                cam.clearFlags = CameraClearFlags.SolidColor;
                                cam.backgroundColor = Color.black;
                                cam.useOcclusionCulling = false;
                            }
                            yield return new WaitForEndOfFrame();
                        }

                        NativeArray<byte> dst = composite.GetRawTextureData<byte>();
                        Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                        try
                        {
                            CopyCore(shot, l, i, j, dst);
                        }
                        finally
                        {
                            UnityEngine.Object.Destroy(shot);
                        }

                        if (request.Transparent)
                        {
                            if (request.KeepScene)
                            {
                                SetHidden(hidden, true);
                            }
                            try
                            {
                                RenderAlpha(maskCam, cam, subjectMask, l, i, j, dst);
                            }
                            finally
                            {
                                if (request.KeepScene)
                                {
                                    SetHidden(hidden, false);
                                }
                            }
                        }
                    }
                }

                camera.LensOverride = null;
                state.Restore(cam);
                Texture2D result = Resample(composite, output, request.Transparent);
                if (result != composite)
                {
                    UnityEngine.Object.Destroy(composite);
                }
                composite = null;
                if (request.Transparent)
                {
                    FixEdgeColours(result);
                }
                Result = result;
            }
            finally
            {
                ScreenIsComposite = false;
                camera.LensOverride = null;
                state.Restore(cam);
                for (int r = restores.Count - 1; r >= 0; r--)
                {
                    try
                    {
                        restores[r]();
                    }
                    catch (Exception ex)
                    {
                        PhotoLog.Warn("High-res capture restore step failed: " + ex.Message);
                    }
                }
                if (maskGo != null)
                {
                    UnityEngine.Object.Destroy(maskGo);
                }
                if (composite != null)
                {
                    UnityEngine.Object.Destroy(composite);
                }
                pp?.ResetHistory();
            }
        }

        private static void CopyCore(Texture2D shot, Layout l, int i, int j, NativeArray<byte> dst)
        {
            Color32[] px = shot.GetPixels32();
            int sw = shot.width;
            int sh = shot.height;
            int cols = Mathf.Min(l.CoreWidth, sw - l.GuardX);
            int rows = Mathf.Min(l.CoreHeight, sh - l.GuardY);
            int stride = l.Width;
            for (int y = 0; y < rows; y++)
            {
                int src = (y + l.GuardY) * sw + l.GuardX;
                int d = ((j * l.CoreHeight + y) * stride + i * l.CoreWidth) * 4;
                for (int x = 0; x < cols; x++)
                {
                    Color32 c = px[src + x];
                    dst[d] = c.r;
                    dst[d + 1] = c.g;
                    dst[d + 2] = c.b;
                    dst[d + 3] = 255;
                    d += 4;
                }
            }
        }

        private static Texture2D Read(RenderTexture rt)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            RenderTexture.active = previous;
            return tex;
        }

        private static void RenderAlpha(UnityEngine.Camera maskCam, UnityEngine.Camera main, int subjectMask, Layout l, int i, int j, NativeArray<byte> dst)
        {
            maskCam.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
            maskCam.nearClipPlane = main.nearClipPlane;
            maskCam.farClipPlane = main.farClipPlane;
            maskCam.layerCullDistances = main.layerCullDistances;
            maskCam.layerCullSpherical = main.layerCullSpherical;
            maskCam.useOcclusionCulling = false;
            maskCam.allowMSAA = false;
            maskCam.allowHDR = false;
            maskCam.cullingMask = subjectMask;
            maskCam.clearFlags = CameraClearFlags.SolidColor;
            maskCam.usePhysicalProperties = main.usePhysicalProperties;
            if (main.usePhysicalProperties)
            {
                maskCam.gateFit = main.gateFit;
                maskCam.sensorSize = main.sensorSize;
                maskCam.focalLength = main.focalLength;
                maskCam.lensShift = main.lensShift;
            }
            else
            {
                maskCam.fieldOfView = main.fieldOfView;
            }

            var rt = new RenderTexture(l.ScreenWidth, l.ScreenHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "DuckCam_CutoutMask" };
            if (!rt.Create())
            {
                UnityEngine.Object.Destroy(rt);
                throw new InvalidOperationException("couldn't allocate the cutout mask target (out of video memory?).");
            }
            Texture2D black = null;
            Texture2D white = null;
            try
            {
                maskCam.targetTexture = rt;
                maskCam.ResetAspect();
                maskCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                maskCam.Render();
                black = Read(rt);
                maskCam.backgroundColor = new Color(1f, 1f, 1f, 0f);
                maskCam.Render();
                white = Read(rt);
                maskCam.targetTexture = null;

                NativeArray<byte> b = black.GetRawTextureData<byte>();
                NativeArray<byte> w = white.GetRawTextureData<byte>();
                int stride = l.Width;
                for (int y = 0; y < l.CoreHeight; y++)
                {
                    int src = ((y + l.GuardY) * l.ScreenWidth + l.GuardX) * 4;
                    int row = ((j * l.CoreHeight + y) * stride + i * l.CoreWidth) * 4;
                    for (int x = 0; x < l.CoreWidth; x++)
                    {
                        int s = src + x * 4;
                        int diff = (w[s] - b[s]) + (w[s + 1] - b[s + 1]) + (w[s + 2] - b[s + 2]);
                        dst[row + x * 4 + 3] = (byte)(255 - Mathf.Clamp(diff, 0, 765) / 3);
                    }
                }
            }
            finally
            {
                maskCam.targetTexture = null;
                rt.Release();
                UnityEngine.Object.Destroy(rt);
                if (black != null)
                {
                    UnityEngine.Object.Destroy(black);
                }
                if (white != null)
                {
                    UnityEngine.Object.Destroy(white);
                }
            }
        }

        private static List<Transform> SubjectRoots(ActorManager actors, CutoutSubjects who)
        {
            var roots = new List<Transform>();
            if (actors != null)
            {
                foreach (PhotoActor a in actors.Actors)
                {
                    if (a.Status == ActorStatus.Ready && a.Visible && a.Root != null)
                    {
                        roots.Add(a.Root);
                    }
                }
            }
            if (who == CutoutSubjects.AllCharacters)
            {
                GameWorld world = GameContext.World;
                if (world?.AllAlivePlayersList != null)
                {
                    foreach (Player p in world.AllAlivePlayersList)
                    {
                        if (p != null)
                        {
                            roots.Add(p.Transform.Original);
                        }
                    }
                }
            }
            return roots;
        }

        private static int SubjectLayers(List<Transform> roots)
        {
            int mask = 0;
            foreach (Transform root in roots)
            {
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(false))
                {
                    if (r.enabled && !r.forceRenderingOff)
                    {
                        mask |= 1 << r.gameObject.layer;
                    }
                }
            }
            return mask;
        }

        private static List<Renderer> OtherRenderers(List<Transform> roots, int subjectMask)
        {
            var set = new HashSet<Transform>(roots);
            var others = new List<Renderer>();
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null || r.forceRenderingOff || (subjectMask & (1 << r.gameObject.layer)) == 0)
                {
                    continue;
                }
                bool subject = false;
                for (Transform t = r.transform; t != null; t = t.parent)
                {
                    if (set.Contains(t))
                    {
                        subject = true;
                        break;
                    }
                }
                if (!subject)
                {
                    others.Add(r);
                }
            }
            return others;
        }

        private static void SetHidden(List<Renderer> renderers, bool hidden)
        {
            foreach (Renderer r in renderers)
            {
                if (r != null)
                {
                    r.forceRenderingOff = hidden;
                }
            }
        }

        private static void HideTerrain(List<Action> restores)
        {
            foreach (Terrain t in Terrain.activeTerrains)
            {
                if (t == null)
                {
                    continue;
                }
                Terrain terrain = t;
                bool heightmap = terrain.drawHeightmap;
                bool trees = terrain.drawTreesAndFoliage;
                terrain.drawHeightmap = false;
                terrain.drawTreesAndFoliage = false;
                restores.Add(() =>
                {
                    if (terrain != null)
                    {
                        terrain.drawHeightmap = heightmap;
                        terrain.drawTreesAndFoliage = trees;
                    }
                });
            }
        }

        private static void DisableLensEffects(UnityEngine.Camera cam, List<Action> restores)
        {
            PrismEffects prism = cam.GetComponent<PrismEffects>();
            if (prism != null)
            {
                bool vignette = prism.useVignette;
                bool noise = prism.useNoise;
                bool chroma = prism.useChromaticAberration;
                float exposureSpeed = prism.exposureSpeed;
                prism.useVignette = false;
                prism.useNoise = false;
                prism.useChromaticAberration = false;
                prism.exposureSpeed = 0f;
                restores.Add(() =>
                {
                    if (prism != null)
                    {
                        prism.useVignette = vignette;
                        prism.useNoise = noise;
                        prism.useChromaticAberration = chroma;
                        prism.exposureSpeed = exposureSpeed;
                    }
                });
            }
            EFT.EnvironmentEffect.EnvironmentManager env = EFT.EnvironmentEffect.EnvironmentManager.Instance;
            if (env != null)
            {
                float speed = env.PrismExposureSpeed;
                env.PrismExposureSpeed = 0f;
                restores.Add(() =>
                {
                    if (env != null)
                    {
                        env.PrismExposureSpeed = speed;
                    }
                });
            }
            DisableBehaviour(cam.GetComponent<CC_FastVignette>(), restores);
            DisableBehaviour(cam.GetComponent<ChromaticAberration>(), restores);
        }

        private static void DisableBehaviour(Behaviour b, List<Action> restores)
        {
            if (b == null || !b.enabled)
            {
                return;
            }
            b.enabled = false;
            restores.Add(() =>
            {
                if (b != null)
                {
                    b.enabled = true;
                }
            });
        }

        private static Texture2D Resample(Texture2D src, Vector2Int output, bool alpha)
        {
            int sw = src.width;
            int sh = src.height;
            int ow = Mathf.Min(output.x, sw);
            int oh = Mathf.Min(output.y, sh);
            if (alpha && ow == sw && oh == sh)
            {
                return src;
            }
            int bpp = alpha ? 4 : 3;
            var result = new Texture2D(ow, oh, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
            NativeArray<byte> s = src.GetRawTextureData<byte>();
            NativeArray<byte> d = result.GetRawTextureData<byte>();
            double fx = (double)sw / ow;
            double fy = (double)sh / oh;
            for (int y = 0; y < oh; y++)
            {
                double y0 = y * fy;
                double y1 = y0 + fy;
                int ya = (int)y0;
                int yb = Math.Min(sh - 1, (int)Math.Ceiling(y1) - 1);
                for (int x = 0; x < ow; x++)
                {
                    double x0 = x * fx;
                    double x1 = x0 + fx;
                    int xa = (int)x0;
                    int xb = Math.Min(sw - 1, (int)Math.Ceiling(x1) - 1);
                    double r = 0, g = 0, b = 0, a = 0, total = 0;
                    for (int sy = ya; sy <= yb; sy++)
                    {
                        double wy = Math.Min(y1, sy + 1) - Math.Max(y0, sy);
                        if (wy <= 0)
                        {
                            continue;
                        }
                        int row = sy * sw * 4;
                        for (int sx = xa; sx <= xb; sx++)
                        {
                            double wgt = wy * (Math.Min(x1, sx + 1) - Math.Max(x0, sx));
                            if (wgt <= 0)
                            {
                                continue;
                            }
                            int p = row + sx * 4;
                            r += s[p] * wgt;
                            g += s[p + 1] * wgt;
                            b += s[p + 2] * wgt;
                            a += s[p + 3] * wgt;
                            total += wgt;
                        }
                    }
                    int o = (y * ow + x) * bpp;
                    double inv = total > 0 ? 1.0 / total : 0;
                    d[o] = (byte)Math.Min(255, (int)(r * inv + 0.5));
                    d[o + 1] = (byte)Math.Min(255, (int)(g * inv + 0.5));
                    d[o + 2] = (byte)Math.Min(255, (int)(b * inv + 0.5));
                    if (alpha)
                    {
                        d[o + 3] = (byte)Math.Min(255, (int)(a * inv + 0.5));
                    }
                }
            }
            result.Apply(false);
            return result;
        }

        private static void FixEdgeColours(Texture2D tex)
        {
            int w = tex.width;
            int h = tex.height;
            NativeArray<byte> px = tex.GetRawTextureData<byte>();
            var fixes = new List<KeyValuePair<int, Color32>>();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    byte a = px[i + 3];
                    if (a == 0 || a >= 250)
                    {
                        continue;
                    }
                    float wr = 0f, wg = 0f, wb = 0f, wsum = 0f;
                    for (int oy = -2; oy <= 2; oy++)
                    {
                        int ny = y + oy;
                        if (ny < 0 || ny >= h)
                        {
                            continue;
                        }
                        for (int ox = -2; ox <= 2; ox++)
                        {
                            int nx = x + ox;
                            if (nx < 0 || nx >= w)
                            {
                                continue;
                            }
                            int j = (ny * w + nx) * 4;
                            float na = px[j + 3] / 255f;
                            float weight = na * na * na * na;
                            wr += px[j] * weight;
                            wg += px[j + 1] * weight;
                            wb += px[j + 2] * weight;
                            wsum += weight;
                        }
                    }
                    if (wsum > 1e-4f)
                    {
                        fixes.Add(new KeyValuePair<int, Color32>(i, new Color32((byte)(wr / wsum), (byte)(wg / wsum), (byte)(wb / wsum), a)));
                    }
                }
            }
            foreach (KeyValuePair<int, Color32> f in fixes)
            {
                px[f.Key] = f.Value.r;
                px[f.Key + 1] = f.Value.g;
                px[f.Key + 2] = f.Value.b;
            }
            for (int i = 0; i < px.Length; i += 4)
            {
                if (px[i + 3] == 0)
                {
                    px[i] = 0;
                    px[i + 1] = 0;
                    px[i + 2] = 0;
                }
            }
            tex.Apply(false);
        }
    }
}
