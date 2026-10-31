using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using DuckCam.Core;

namespace DuckCam.Spawning
{
    internal sealed class BotKit
    {
        public string Name = "Kit";
        public bool Favorite;
        public int Role;
        public string RoleName;
        public Dictionary<string, string> Slots = new Dictionary<string, string>();
        public bool InfiniteAmmo = true;
        public int SpareMags = 3;
        public bool SessionOnly;
    }

    internal static class BotKits
    {
        public const string None = "none";

        private static string Dir => Path.Combine(DuckCamPlugin.DataRoot, "Kits");

        public static List<BotKit> LoadAll()
        {
            var list = new List<BotKit>();
            try
            {
                if (!Directory.Exists(Dir))
                {
                    return list;
                }
                foreach (string file in Directory.GetFiles(Dir, "*.json"))
                {
                    try
                    {
                        BotKit kit = JsonConvert.DeserializeObject<BotKit>(File.ReadAllText(file));
                        if (kit != null)
                        {
                            kit.Slots = kit.Slots ?? new Dictionary<string, string>();
                            list.Add(kit);
                        }
                    }
                    catch (Exception ex)
                    {
                        PhotoLog.Warn($"Kit {Path.GetFileName(file)} could not be read: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                PhotoLog.Warn("Kits folder could not be read: " + ex.Message);
            }
            return list.OrderByDescending(k => k.Favorite).ThenBy(k => k.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string Save(BotKit kit)
        {
            Directory.CreateDirectory(Dir);
            string path = Path.Combine(Dir, Sanitize(kit.Name) + ".json");
            File.WriteAllText(path, JsonConvert.SerializeObject(kit, Formatting.Indented));
            return path;
        }

        public static void Delete(BotKit kit)
        {
            string path = Path.Combine(Dir, Sanitize(kit.Name) + ".json");
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public static string Sanitize(string name)
        {
            string clean = new string((name ?? "Kit").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
            return string.IsNullOrEmpty(clean) ? "Kit" : clean;
        }
    }
}
