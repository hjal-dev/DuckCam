using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DuckCam.Actors;
using DuckCam.Animation;
using DuckCam.Camera;
using DuckCam.Lighting;
using DuckCam.Rendering;
using DuckCam.Spawning;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DuckCam.Core
{
    internal sealed class ContentKind
    {
        public string Title;
        public Func<string> Root;
        public Func<string[]> Keys;
        public Action Refresh;
    }

    internal static class AddonCreator
    {
        public static readonly ContentKind[] Kinds =
        {
            new ContentKind { Title = "Poses", Root = () => PoseLibrary.Directory, Keys = PoseLibrary.List, Refresh = PoseLibrary.Refresh },
            new ContentKind { Title = "Filters", Root = () => CustomFilters.Directory, Keys = CustomFilters.List, Refresh = CustomFilters.Refresh },
            new ContentKind { Title = "Presets", Root = () => ScenePresets.Directory, Keys = ScenePresets.List },
            new ContentKind { Title = "Light Rigs", Root = () => LightRigs.Directory, Keys = LightRigs.List },
            new ContentKind { Title = "Paths", Root = () => PathStorage.Directory, Keys = PathStorage.List },
            new ContentKind { Title = "Scenes", Root = () => SceneTimeline.Directory, Keys = SceneTimeline.List },
            new ContentKind { Title = "Characters", Root = () => SavedActors.Directory, Keys = SavedActors.List },
            new ContentKind { Title = "Bot Kits", Root = () => BotKits.Dir, Keys = () => ContentPacks.List(BotKits.Dir) }
        };

        public static void Refresh(ContentKind kind)
        {
            ContentPacks.Invalidate(kind.Root());
            kind.Refresh?.Invoke();
        }

        public static string[] Own(ContentKind kind) => kind.Keys().Where(k => !ContentPacks.IsPack(k)).ToArray();

        public static string[] InPack(ContentKind kind, string pack) => pack == null
            ? Array.Empty<string>()
            : kind.Keys().Where(k => string.Equals(ContentPacks.PackOf(k), pack, StringComparison.OrdinalIgnoreCase)).ToArray();

        public static string[] AllPacks()
        {
            var packs = new List<string>();
            foreach (ContentKind kind in Kinds)
            {
                try
                {
                    packs.AddRange(ContentPacks.Packs(kind.Keys()));
                    packs.AddRange(ContentPacks.Folders(kind.Root()));
                }
                catch (Exception ex)
                {
                    PhotoLog.WarnOnce("addons:" + kind.Title, kind.Title + " could not be listed: " + ex.Message);
                }
            }
            return packs.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static int Count(string pack) => Kinds.Sum(k => InPack(k, pack).Length);

        public static PackInfo Info(string pack)
        {
            foreach (ContentKind kind in Kinds)
            {
                PackInfo info = ContentPacks.Info(kind.Root(), pack);
                if (info != null)
                {
                    return info;
                }
            }
            return new PackInfo { Name = pack, Version = "1.0" };
        }

        public static void SaveInfo(string pack, PackInfo info)
        {
            info.Name = pack;
            foreach (ContentKind kind in Kinds)
            {
                string dir = Path.Combine(kind.Root(), pack);
                if (Directory.Exists(dir))
                {
                    WriteInfo(dir, info);
                    ContentPacks.Invalidate(kind.Root());
                }
            }
        }

        public static string NewPack(ContentKind kind, string name, string author)
        {
            string pack = ContentPacks.CleanPackName(name);
            if (pack.Length == 0)
            {
                throw new InvalidOperationException("Type a pack name first.");
            }
            if (AllPacks().Contains(pack, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"A pack called '{pack}' already exists.");
            }
            ContentPacks.Create(kind.Root(), pack, author);
            return pack;
        }

        public static string RenamePack(string pack, string newName)
        {
            string target = ContentPacks.CleanPackName(newName);
            if (target.Length == 0)
            {
                throw new InvalidOperationException("Type a pack name first.");
            }
            if (string.Equals(target, pack, StringComparison.Ordinal))
            {
                return pack;
            }
            bool caseOnly = string.Equals(target, pack, StringComparison.OrdinalIgnoreCase);
            if (!caseOnly && AllPacks().Contains(target, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"A pack called '{target}' already exists.");
            }
            PackInfo info = Info(pack);
            foreach (ContentKind kind in Kinds)
            {
                string from = Path.Combine(kind.Root(), pack);
                if (!Directory.Exists(from))
                {
                    continue;
                }
                string to = Path.Combine(kind.Root(), target);
                if (caseOnly)
                {
                    string temp = from + ".rename";
                    Directory.Move(from, temp);
                    Directory.Move(temp, to);
                }
                else
                {
                    Directory.Move(from, to);
                }
                Refresh(kind);
            }
            SaveInfo(target, info);
            return target;
        }

        public static int Transfer(ContentKind kind, IEnumerable<string> keys, string pack, bool copy)
        {
            string root = kind.Root();
            if (pack != null)
            {
                EnsurePack(root, pack);
            }
            int done = 0;
            foreach (string key in keys.ToList())
            {
                string src = ContentPacks.FileOf(root, key);
                if (!File.Exists(src))
                {
                    continue;
                }
                string dst = FreeFile(root, pack, ContentPacks.NameOf(key));
                string newName = Path.GetFileNameWithoutExtension(dst);
                if (newName == ContentPacks.NameOf(key))
                {
                    if (copy)
                    {
                        File.Copy(src, dst);
                    }
                    else
                    {
                        File.Move(src, dst);
                    }
                }
                else
                {
                    File.WriteAllText(dst, Renamed(File.ReadAllText(src), newName));
                    if (!copy)
                    {
                        File.Delete(src);
                    }
                }
                done++;
            }
            Refresh(kind);
            return done;
        }

        public static string Rename(ContentKind kind, string key, string newName)
        {
            string name = ContentPacks.CleanPackName(newName);
            if (name.Length == 0)
            {
                throw new InvalidOperationException("Type a new name first.");
            }
            string root = kind.Root();
            string pack = ContentPacks.PackOf(key);
            string newKey = pack == null ? name : pack + "/" + name;
            string src = ContentPacks.FileOf(root, key);
            string dst = ContentPacks.FileOf(root, newKey);
            if (string.Equals(src, dst, StringComparison.Ordinal))
            {
                return key;
            }
            if (!string.Equals(src, dst, StringComparison.OrdinalIgnoreCase) && File.Exists(dst))
            {
                throw new InvalidOperationException($"'{name}' already exists there.");
            }
            File.WriteAllText(dst + ".tmp", Renamed(File.ReadAllText(src), name));
            File.Delete(src);
            File.Move(dst + ".tmp", dst);
            Refresh(kind);
            return newKey;
        }

        private static string Renamed(string json, string name)
        {
            try
            {
                JObject obj = JObject.Parse(json);
                if (obj["Name"] != null && obj["Name"].Type == JTokenType.String)
                {
                    obj["Name"] = name;
                    return obj.ToString(Formatting.Indented);
                }
            }
            catch (JsonException)
            {
            }
            return json;
        }

        private static void EnsurePack(string root, string pack)
        {
            string dir = Path.Combine(root, pack);
            Directory.CreateDirectory(dir);
            if (!File.Exists(Path.Combine(dir, ContentPacks.PackFile)))
            {
                WriteInfo(dir, Info(pack));
            }
        }

        private static void WriteInfo(string dir, PackInfo info)
        {
            File.WriteAllText(Path.Combine(dir, ContentPacks.PackFile), JsonConvert.SerializeObject(info, Formatting.Indented, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
        }

        private static string FreeFile(string root, string pack, string name)
        {
            string dir = pack == null ? root : Path.Combine(root, pack);
            string path = Path.Combine(dir, name + ".json");
            for (int n = 2; File.Exists(path); n++)
            {
                path = Path.Combine(dir, $"{name} {n}.json");
            }
            return path;
        }
    }
}
