using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using DuckCam.Core;
using DuckCam.Lighting;

namespace DuckCam.Rendering
{
    internal sealed class CustomFilterFile
    {
        public int Version = 1;
        public string Filter;
        public Dictionary<string, float> Effects = new Dictionary<string, float>();
    }

    internal static class CustomFilters
    {
        public static string Directory => Path.Combine(DuckCamPlugin.DataRoot, "Filters");

        private static string[] _cache;

        public static string[] List()
        {
            if (_cache != null)
            {
                return _cache;
            }
            try
            {
                _cache = System.IO.Directory.Exists(Directory)
                    ? System.IO.Directory.GetFiles(Directory, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray()
                    : Array.Empty<string>();
            }
            catch (Exception ex)
            {
                PhotoLog.WarnOnce("customfilters", "Custom filters could not be listed: " + ex.Message);
                _cache = Array.Empty<string>();
            }
            return _cache;
        }

        public static void Refresh() => _cache = null;

        public static string Save(PhotoEffects fx, string name)
        {
            name = LightRigs.Sanitize(string.IsNullOrWhiteSpace(name) ? "My Filter" : name.Trim());
            var file = new CustomFilterFile
            {
                Filter = fx.FilterAvailable ? fx.Filter.ToString() : null,
                Effects = fx.SnapshotValues()
            };
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(Path.Combine(Directory, name + ".json"), JsonConvert.SerializeObject(file, Formatting.Indented));
            Refresh();
            return name;
        }

        public static string Apply(PhotoEffects fx, string name)
        {
            try
            {
                CustomFilterFile file = JsonConvert.DeserializeObject<CustomFilterFile>(File.ReadAllText(Path.Combine(Directory, name + ".json")));
                if (file == null)
                {
                    return "empty file";
                }
                fx.RestoreValues(file.Effects, file.Filter);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public static bool Delete(string name)
        {
            try
            {
                File.Delete(Path.Combine(Directory, name + ".json"));
                Refresh();
                return true;
            }
            catch (Exception ex)
            {
                PhotoLog.Warn($"Custom filter '{name}' could not be deleted: " + ex.Message);
                return false;
            }
        }
    }
}
