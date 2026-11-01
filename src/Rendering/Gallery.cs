using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Rendering
{
    internal sealed class GalleryItem
    {
        public string Path;
        public string Name;
        public bool Video;
        public DateTime Time;
        public long Bytes;
        public string Map;
        public Texture2D Thumb;
        public bool ThumbFailed;
    }

    internal sealed class Gallery
    {
        public const int ThumbWidth = 320;

        public const int MaxViewWidth = 4096;

        public static string ThumbDirectory => System.IO.Path.Combine(DuckCamPlugin.DataRoot, "Thumbnails");

        public readonly List<GalleryItem> Items = new List<GalleryItem>();

        private readonly Queue<GalleryItem> _queue = new Queue<GalleryItem>();
        private const int MaxJobs = 3;

        private readonly List<(GalleryItem Item, Process Proc)> _jobs = new List<(GalleryItem Item, Process Proc)>();
        private GalleryItem _working;
        private Task<byte[]> _reading;
        private string _ffmpegPath;
        private int _tickFrame = -1;

        public void Scan()
        {
            Clear();
            _ffmpegPath = VideoRecorder.FindFfmpeg();
            Add(ScreenshotController.Directory, "*.png", false);
            Add(ScreenshotController.Directory, "*.jpg", false);
            Add(VideoRecorder.Directory, "*.mp4", true);
            Items.Sort((a, b) => b.Time.CompareTo(a.Time));
            foreach (GalleryItem item in Items)
            {
                _queue.Enqueue(item);
            }
        }

        private void Add(string dir, string pattern, bool video)
        {
            if (!Directory.Exists(dir))
            {
                return;
            }
            foreach (string file in Directory.GetFiles(dir, pattern))
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(file);
                if (video && (name.EndsWith(".video", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".audio", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                var info = new FileInfo(file);
                Items.Add(new GalleryItem
                {
                    Path = file,
                    Name = System.IO.Path.GetFileName(file),
                    Video = video,
                    Time = info.LastWriteTime,
                    Bytes = info.Length,
                    Map = MapOf(name)
                });
            }
        }

        private static string MapOf(string name)
        {
            const string prefix = "DuckCam_";
            const int stamp = 19;
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.Length <= prefix.Length + stamp + 1)
            {
                return "";
            }
            string rest = name.Substring(prefix.Length + stamp + 1);
            int last = rest.LastIndexOf('_');
            if (last > 0 && rest.Substring(last + 1).All(char.IsDigit))
            {
                rest = rest.Substring(0, last);
            }
            return rest.Replace('_', ' ');
        }

        public void Tick()
        {
            if (_tickFrame == UnityEngine.Time.frameCount)
            {
                return;
            }
            _tickFrame = UnityEngine.Time.frameCount;
            var budget = Stopwatch.StartNew();
            try
            {
                FinishJobs();
                FinishReading();
                while (_queue.Count > 0 && budget.ElapsedMilliseconds < 4)
                {
                    GalleryItem next = _queue.Peek();
                    if (next.Thumb != null || next.ThumbFailed || !Items.Contains(next))
                    {
                        _queue.Dequeue();
                        continue;
                    }
                    if (TryCached(next))
                    {
                        _queue.Dequeue();
                        continue;
                    }
                    if (!Begin(next))
                    {
                        break;
                    }
                    _queue.Dequeue();
                }
            }
            catch (Exception ex)
            {
                PhotoLog.Warn("Gallery thumbnails: " + ex.Message);
            }
        }

        private void FinishJobs()
        {
            for (int i = _jobs.Count - 1; i >= 0; i--)
            {
                (GalleryItem item, Process proc) = _jobs[i];
                if (!proc.HasExited)
                {
                    continue;
                }
                proc.Dispose();
                _jobs.RemoveAt(i);
                if (!TryCached(item))
                {
                    item.ThumbFailed = true;
                }
            }
        }

        private void FinishReading()
        {
            if (_reading == null || !_reading.IsCompleted)
            {
                return;
            }
            GalleryItem photo = _working;
            byte[] bytes = _reading.Status == TaskStatus.RanToCompletion ? _reading.Result : null;
            _reading = null;
            _working = null;
            Texture2D full = bytes == null ? null : Decode(bytes);
            if (full == null)
            {
                photo.ThumbFailed = true;
                return;
            }
            photo.Thumb = Downscale(full, ThumbWidth);
            UnityEngine.Object.Destroy(full);
            byte[] jpg = photo.Thumb.EncodeToJPG(85);
            string thumbPath = ThumbPath(photo);
            Task.Run(() =>
            {
                try
                {
                    Directory.CreateDirectory(ThumbDirectory);
                    File.WriteAllBytes(thumbPath, jpg);
                }
                catch (Exception ex)
                {
                    PhotoLog.Warn("Gallery thumbnail could not be saved: " + ex.Message);
                }
            });
        }

        private bool Begin(GalleryItem item)
        {
            if (_ffmpegPath != null)
            {
                if (_jobs.Count >= MaxJobs)
                {
                    return false;
                }
                Directory.CreateDirectory(ThumbDirectory);
                string seek = item.Video ? "-ss 0.5 " : "";
                Process proc = Process.Start(new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = $"-hide_banner -loglevel error -y {seek}-i \"{item.Path}\" -frames:v 1 -vf scale={ThumbWidth}:-2 -q:v 4 \"{ThumbPath(item)}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (proc == null)
                {
                    item.ThumbFailed = true;
                    return true;
                }
                _jobs.Add((item, proc));
                return true;
            }
            if (item.Video)
            {
                item.ThumbFailed = true;
                return true;
            }
            if (_working != null)
            {
                return false;
            }
            _working = item;
            string path = item.Path;
            _reading = Task.Run(() => File.ReadAllBytes(path));
            return true;
        }

        private static bool TryCached(GalleryItem item)
        {
            string path = ThumbPath(item);
            if (!File.Exists(path) || File.GetLastWriteTime(path) < item.Time)
            {
                return false;
            }
            Texture2D tex = Decode(File.ReadAllBytes(path));
            if (tex == null)
            {
                return false;
            }
            item.Thumb = tex;
            return true;
        }

        private static string ThumbPath(GalleryItem item) => System.IO.Path.Combine(ThumbDirectory, (item.Video ? "v_" : "p_") + System.IO.Path.GetFileNameWithoutExtension(item.Name) + ".jpg");

        public static Texture2D Decode(byte[] bytes)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            if (!ImageConversion.LoadImage(tex, bytes, false))
            {
                UnityEngine.Object.Destroy(tex);
                return null;
            }
            return tex;
        }

        public static Texture2D Downscale(Texture2D source, int maxWidth)
        {
            int w = Mathf.Min(maxWidth, source.width);
            int h = Mathf.Max(1, Mathf.RoundToInt(source.height * (float)w / source.width));
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            RenderTexture prev = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                tex.Apply(false);
                return tex;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        public static Texture2D LoadForView(GalleryItem item)
        {
            Texture2D full = Decode(File.ReadAllBytes(item.Path));
            if (full == null || full.width <= MaxViewWidth)
            {
                return full;
            }
            Texture2D small = Downscale(full, MaxViewWidth);
            UnityEngine.Object.Destroy(full);
            return small;
        }

        public static Dictionary<string, object> Sidecar(GalleryItem item)
        {
            string path = System.IO.Path.ChangeExtension(item.Path, ".json");
            if (item.Video || !File.Exists(path))
            {
                return null;
            }
            try
            {
                return Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }

        public void Delete(GalleryItem item)
        {
            File.Delete(item.Path);
            string sidecar = System.IO.Path.ChangeExtension(item.Path, ".json");
            if (!item.Video && File.Exists(sidecar))
            {
                File.Delete(sidecar);
            }
            string thumb = ThumbPath(item);
            if (File.Exists(thumb))
            {
                File.Delete(thumb);
            }
            if (item.Thumb != null)
            {
                UnityEngine.Object.Destroy(item.Thumb);
            }
            Items.Remove(item);
        }

        public static void ShowInFolder(string path)
        {
            if (File.Exists(path))
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            else if (Directory.Exists(path))
            {
                Process.Start("explorer.exe", $"\"{path}\"");
            }
        }

        public static string Size(long bytes) => bytes >= 1048576 ? (bytes / 1048576f).ToString("0.0", CultureInfo.InvariantCulture) + " MB" : (bytes / 1024f).ToString("0", CultureInfo.InvariantCulture) + " KB";

        public void Clear()
        {
            foreach (GalleryItem item in Items)
            {
                if (item.Thumb != null)
                {
                    UnityEngine.Object.Destroy(item.Thumb);
                }
            }
            Items.Clear();
            _queue.Clear();
            _working = null;
            _reading = null;
            foreach ((GalleryItem _, Process proc) in _jobs)
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill();
                    }
                }
                catch
                {
                }
                proc.Dispose();
            }
            _jobs.Clear();
        }
    }
}
