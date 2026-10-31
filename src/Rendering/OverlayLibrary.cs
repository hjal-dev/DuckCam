using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Rendering
{
    internal sealed class OverlayAsset
    {
        public string Category;
        public string Name;
        public string Path;
        private Texture2D _texture;
        private bool _failed;

        public Texture2D Texture
        {
            get
            {
                if (_texture == null && !_failed)
                {
                    try
                    {
                        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                        if (!tex.LoadImage(File.ReadAllBytes(Path), markNonReadable: false))
                        {
                            throw new InvalidDataException("not a PNG/JPG");
                        }
                        _texture = tex;
                    }
                    catch (Exception ex)
                    {
                        _failed = true;
                        PhotoLog.Warn($"Overlay '{Path}' could not be loaded: {ex.Message}");
                    }
                }
                return _texture;
            }
        }

        public void Unload()
        {
            if (_texture != null)
            {
                UnityEngine.Object.Destroy(_texture);
                _texture = null;
            }
        }
    }

    internal static class OverlayLibrary
    {
        private static readonly List<OverlayAsset> _assets = new List<OverlayAsset>();

        public static string Directory => System.IO.Path.Combine(DuckCamPlugin.DataRoot, "Overlays");

        public static IReadOnlyList<OverlayAsset> Assets => _assets;

        public static string[] Categories => _assets.Select(a => a.Category).Distinct().OrderBy(c => c == "General" ? "" : c).ToArray();

        public static void Refresh()
        {
            foreach (OverlayAsset a in _assets)
            {
                a.Unload();
            }
            _assets.Clear();
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                string readme = System.IO.Path.Combine(Directory, "README.txt");
                if (!File.Exists(readme))
                {
                    File.WriteAllText(readme,
                        "Put PNG files here to use them as DuckCam frames or stickers.\r\n" +
                        "Files in this folder show up under 'General'; each subfolder becomes its own category.\r\n" +
                        "Frames are stretched over the photo frame, so use transparent PNGs at your screen's aspect ratio (e.g. 1920x1080 or 3840x2160).\r\n");
                }
                foreach (string file in System.IO.Directory.GetFiles(Directory, "*.*", SearchOption.AllDirectories))
                {
                    string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                    if (ext != ".png" && ext != ".jpg" && ext != ".jpeg")
                    {
                        continue;
                    }
                    string relativeDir = System.IO.Path.GetDirectoryName(file).Substring(Directory.Length).Trim('\\', '/');
                    _assets.Add(new OverlayAsset
                    {
                        Category = string.IsNullOrEmpty(relativeDir) ? "General" : relativeDir.Replace('\\', '/'),
                        Name = System.IO.Path.GetFileNameWithoutExtension(file),
                        Path = file
                    });
                }
                _assets.Sort((a, b) => string.Compare(a.Category + "/" + a.Name, b.Category + "/" + b.Name, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                PhotoLog.Warn("Overlay folder scan failed: " + ex.Message);
            }
        }

        public static List<OverlayAsset> InCategory(string category)
        {
            return _assets.Where(a => a.Category == category).ToList();
        }
    }
}
