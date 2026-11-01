using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace DuckCam.Core
{
    internal sealed class PackInfo
    {
        public string Name { get; set; }
        public string Author { get; set; }
        public string Version { get; set; }
        public string Link { get; set; }
        public string Description { get; set; }
    }

    internal static class ContentPacks
    {
        public const string PackFile = "pack.json";

        private const float CacheSeconds = 2f;

        private sealed class Entry
        {
            public string[] Keys;
            public float At;
        }

        private static readonly Dictionary<string, Entry> _lists = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, PackInfo> _info = new Dictionary<string, PackInfo>(StringComparer.OrdinalIgnoreCase);

        public static string[] List(string root)
        {
            if (_lists.TryGetValue(root, out Entry e) && Time.unscaledTime - e.At < CacheSeconds)
            {
                return e.Keys;
            }
            string[] keys;
            try
            {
                keys = Scan(root, "*.json").Select(f => KeyOf(root, f)).ToArray();
            }
            catch (Exception ex)
            {
                PhotoLog.WarnOnce("packs:" + root, $"{Path.GetFileName(root)} folder could not be listed: " + ex.Message);
                keys = Array.Empty<string>();
            }
            _lists[root] = new Entry { Keys = keys, At = Time.unscaledTime };
            return keys;
        }

        public static IEnumerable<string> Scan(string root, string pattern)
        {
            if (!Directory.Exists(root))
            {
                return Array.Empty<string>();
            }
            var files = new List<string>(Directory.GetFiles(root, pattern).OrderBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase));
            foreach (string dir in Directory.GetDirectories(root).OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            {
                files.AddRange(Directory.GetFiles(dir, pattern)
                    .Where(f => !string.Equals(Path.GetFileName(f), PackFile, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase));
            }
            return files;
        }

        public static void Invalidate(string root)
        {
            _lists.Remove(root);
            foreach (string k in _info.Keys.Where(k => k.StartsWith(root, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                _info.Remove(k);
            }
        }

        public static string KeyOf(string root, string file)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            string dir = Path.GetDirectoryName(file);
            if (string.Equals(dir?.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
            return Path.GetFileName(dir) + "/" + name;
        }

        public static string FileOf(string root, string key)
        {
            return Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar) + ".json");
        }

        public static string PackOf(string key)
        {
            int i = key?.IndexOf('/') ?? -1;
            return i > 0 ? key.Substring(0, i) : null;
        }

        public static string NameOf(string key)
        {
            int i = key?.IndexOf('/') ?? -1;
            return i >= 0 ? key.Substring(i + 1) : key;
        }

        public static bool IsPack(string key) => PackOf(key) != null;

        public static string Display(string key)
        {
            string pack = PackOf(key);
            return pack == null ? key : $"[{pack}] {NameOf(key)}";
        }

        public static string[] Packs(IEnumerable<string> keys)
        {
            return keys.Select(PackOf).Where(p => p != null).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static string[] Folders(string root)
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    return Array.Empty<string>();
                }
                return Directory.GetDirectories(root)
                    .Where(d => File.Exists(Path.Combine(d, PackFile)))
                    .Select(Path.GetFileName)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        public static string SaveFile(string root, string pack, string name)
        {
            string dir = string.IsNullOrEmpty(pack) ? root : Path.Combine(root, pack);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, name + ".json");
        }

        public static string CleanPackName(string name)
        {
            string clean = new string((name ?? "").Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c != '/' && c != '\\').ToArray()).Trim().TrimEnd('.');
            return clean.Length > 40 ? clean.Substring(0, 40).Trim() : clean;
        }

        public static string Create(string root, string name, string author)
        {
            string pack = CleanPackName(name);
            if (pack.Length == 0)
            {
                throw new InvalidOperationException("Type a pack name first.");
            }
            string dir = Path.Combine(root, pack);
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, PackFile);
            if (!File.Exists(file))
            {
                var info = new PackInfo { Name = pack, Author = string.IsNullOrWhiteSpace(author) ? null : author.Trim(), Version = "1.0" };
                File.WriteAllText(file, JsonConvert.SerializeObject(info, Formatting.Indented, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
            }
            Invalidate(root);
            return pack;
        }

        public static PackInfo Info(string root, string pack)
        {
            string dir = Path.Combine(root, pack);
            if (_info.TryGetValue(dir, out PackInfo info))
            {
                return info;
            }
            info = null;
            try
            {
                string file = Path.Combine(dir, PackFile);
                if (File.Exists(file))
                {
                    info = JsonConvert.DeserializeObject<PackInfo>(File.ReadAllText(file));
                }
            }
            catch (Exception ex)
            {
                PhotoLog.WarnOnce("packinfo:" + dir, $"{pack}/{PackFile} could not be read: " + ex.Message);
            }
            _info[dir] = info;
            return info;
        }

        public static string InfoLine(string root, string pack)
        {
            PackInfo info = Info(root, pack);
            if (info == null)
            {
                return null;
            }
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(info.Author))
            {
                parts.Add("by " + info.Author.Trim());
            }
            if (!string.IsNullOrWhiteSpace(info.Version))
            {
                string v = info.Version.Trim();
                parts.Add(v.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? v : "v" + v);
            }
            string line = string.Join(", ", parts);
            if (!string.IsNullOrWhiteSpace(info.Name) && !string.Equals(info.Name.Trim(), pack, StringComparison.OrdinalIgnoreCase))
            {
                line = info.Name.Trim() + (line.Length > 0 ? " - " + line : "");
            }
            if (!string.IsNullOrWhiteSpace(info.Description))
            {
                line += (line.Length > 0 ? ". " : "") + info.Description.Trim();
            }
            if (!string.IsNullOrWhiteSpace(info.Link))
            {
                line += (line.Length > 0 ? " - " : "") + info.Link.Trim();
            }
            return line.Length > 0 ? line : null;
        }
    }

    internal sealed class PackFilter
    {
        private const string AllOption = "All";

        private const string OwnOption = "My Own";

        private string _pack;

        public bool Active { get; private set; }

        public string Selected => _pack;

        public string[] Options(string[] keys, string root = null)
        {
            string[] packs = root == null ? ContentPacks.Packs(keys) : ContentPacks.Packs(keys).Concat(ContentPacks.Folders(root)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            Active = packs.Length > 0;
            if (_pack != null && _pack.Length > 0 && !packs.Contains(_pack, StringComparer.OrdinalIgnoreCase))
            {
                _pack = null;
            }
            return new[] { AllOption, OwnOption }.Concat(packs).ToArray();
        }

        public int Index(string[] options)
        {
            if (_pack == null)
            {
                return 0;
            }
            if (_pack.Length == 0)
            {
                return 1;
            }
            return Math.Max(0, Array.FindIndex(options, o => string.Equals(o, _pack, StringComparison.OrdinalIgnoreCase)));
        }

        public void Pick(string[] options, int index)
        {
            _pack = index <= 0 ? null : index == 1 ? "" : options[index];
        }

        public string Cycle(string[] keys, string root = null)
        {
            string[] options = Options(keys, root);
            Pick(options, (Index(options) + 1) % options.Length);
            return Label;
        }

        public string Label => _pack == null ? AllOption : _pack.Length == 0 ? OwnOption : _pack;

        public string[] Apply(string[] keys)
        {
            if (_pack == null)
            {
                return keys;
            }
            return keys.Where(k => string.Equals(ContentPacks.PackOf(k) ?? "", _pack, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        public string Show(string key) => _pack == null ? ContentPacks.Display(key) : ContentPacks.NameOf(key);

        public string Info(string root) => string.IsNullOrEmpty(_pack) ? null : ContentPacks.InfoLine(root, _pack);
    }
}
