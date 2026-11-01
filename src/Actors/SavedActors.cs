using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EFT;
using Newtonsoft.Json;
using DuckCam.Core;

namespace DuckCam.Actors
{
    internal sealed class SavedActor
    {
        public int Version = 1;
        public string Name;
        public string Info;
        public string DescriptorJson;
        public string PoseName;
        public float PoseTime;
        public float Yaw;
        public bool WeaponVisible;
        public List<string> HiddenGear = new List<string>();
        public string PoseState;
        public float[] Grip;
        public string SavedOnMap;
        public string SavedAt;
    }

    internal static class SavedActors
    {
        public static string Directory => Path.Combine(DuckCamPlugin.DataRoot, "Actors");

        public static string[] List()
        {
            if (!System.IO.Directory.Exists(Directory))
            {
                return Array.Empty<string>();
            }
            return ContentPacks.List(Directory);
        }

        public static string Save(PhotoActor actor, string pack = null)
        {
            if (actor.Descriptor == null)
            {
                return "this actor's appearance could not be captured";
            }
            try
            {
                var saved = new SavedActor
                {
                    Name = actor.Name,
                    Info = actor.Info,
                    DescriptorJson = actor.Descriptor.ToJson(EftJsonConverters.Converters),
                    PoseName = actor.CurrentPose?.PoseName,
                    PoseTime = actor.NormalizedTime,
                    Yaw = actor.Yaw,
                    WeaponVisible = actor.WeaponVisible,
                    PoseState = PoseEdit.Serialize(actor.PoseEditor.State),
                    Grip = actor.GripData,
                    HiddenGear = actor.HiddenSlots.Select(s => s.ToString()).ToList(),
                    SavedOnMap = GameContext.LocationId,
                    SavedAt = DateTime.Now.ToString("o")
                };
                File.WriteAllText(ContentPacks.SaveFile(Directory, pack, Sanitize(actor.Name)), JsonConvert.SerializeObject(saved, Formatting.Indented));
                ContentPacks.Invalidate(Directory);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public static SavedActor Load(string name, out string error)
        {
            error = null;
            try
            {
                SavedActor saved = JsonConvert.DeserializeObject<SavedActor>(File.ReadAllText(ContentPacks.FileOf(Directory, name)));
                if (saved == null || string.IsNullOrEmpty(saved.DescriptorJson))
                {
                    error = "empty or invalid file";
                    return null;
                }
                return saved;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        public static PlayerVisualRepresentation ToVisual(SavedActor saved, out string error)
        {
            error = null;
            try
            {
                PlayerVisualRepresentationDescriptor descriptor = saved.DescriptorJson.ParseJsonTo<PlayerVisualRepresentationDescriptor>(EftJsonConverters.Converters);
                return new PlayerVisualRepresentation(descriptor);
            }
            catch (Exception ex)
            {
                error = "could not recreate the appearance (missing item or customization?): " + ex.Message;
                return null;
            }
        }

        public static void Delete(string name)
        {
            try
            {
                File.Delete(ContentPacks.FileOf(Directory, name));
                ContentPacks.Invalidate(Directory);
            }
            catch (Exception ex)
            {
                PhotoLog.Warn("Delete saved actor failed: " + ex.Message);
            }
        }

        private static string Sanitize(string s)
        {
            s = string.IsNullOrEmpty(s) ? "actor" : s;
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                s = s.Replace(c, '_');
            }
            return s;
        }
    }
}
