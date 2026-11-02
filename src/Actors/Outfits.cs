using System;
using System.IO;
using System.Linq;
using DuckCam.Core;
using EFT;
using Newtonsoft.Json;

namespace DuckCam.Actors
{
    internal sealed class OutfitFile
    {
        public string Name;
        public string Side;
        public string DescriptorJson;
        public string SavedAt;
    }

    internal static class Outfits
    {
        public static string Directory => Path.Combine(DuckCamPlugin.DataRoot, "Outfits");

        public static string[] List() => System.IO.Directory.Exists(Directory) ? ContentPacks.List(Directory) : Array.Empty<string>();

        public static string Clean(string name)
        {
            string clean = new string((name ?? "").Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
            return clean.Length > 40 ? clean.Substring(0, 40).Trim() : clean;
        }

        public static string Save(PhotoActor actor, string key)
        {
            if (actor?.Descriptor == null)
            {
                throw new InvalidOperationException("this character's look can't be read.");
            }
            string pack = ContentPacks.PackOf(key);
            string name = Clean(ContentPacks.NameOf(key));
            if (name.Length == 0)
            {
                throw new InvalidOperationException("Type an outfit name first.");
            }
            string finalKey = pack == null ? name : pack + "/" + name;
            var file = new OutfitFile
            {
                Name = name,
                Side = actor.Descriptor.Info != null ? actor.Descriptor.Info.Side.ToString() : null,
                DescriptorJson = actor.Descriptor.ToJson(EftJsonConverters.Converters),
                SavedAt = DateTime.Now.ToString("o")
            };
            string path = ContentPacks.FileOf(Directory, finalKey);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonConvert.SerializeObject(file, Formatting.Indented));
            ContentPacks.Invalidate(Directory);
            return finalKey;
        }

        public static string Apply(PhotoActor actor, string key)
        {
            try
            {
                OutfitFile file = JsonConvert.DeserializeObject<OutfitFile>(File.ReadAllText(ContentPacks.FileOf(Directory, key)));
                if (file == null || string.IsNullOrEmpty(file.DescriptorJson))
                {
                    return "the outfit file is empty";
                }
                PlayerVisualRepresentationDescriptor look = file.DescriptorJson.ParseJsonTo<PlayerVisualRepresentationDescriptor>(EftJsonConverters.Converters);
                actor.ApplyLook(look.Customization, look.Equipment);
                return actor.ReloadError;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public static void Delete(string key)
        {
            string path = ContentPacks.FileOf(Directory, key);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            ContentPacks.Invalidate(Directory);
        }
    }
}
