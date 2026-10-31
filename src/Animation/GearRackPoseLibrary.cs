using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.Customization;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Animation
{
    internal sealed class GearRackPose
    {
        public string Id;
        public string PoseName;
        public string DisplayName;
        public bool Unlocked;
        public bool? StateExists;
        public AnimationClip ModClip;
        public string ModSource;
        public string Group;
        public BodyCoverage Coverage;
        public bool IsBodyAnimation => PoseName != null && PoseName.StartsWith(BodyClipLibrary.Prefix, StringComparison.Ordinal);
    }

    internal static class GearRackPoseLibrary
    {
        public const string ControllerPath = "Hideout/EquipmentPresetsStand/Mannequin_animator_controller";

        public const string OverrideClipName = "standing";

        private static readonly List<GearRackPose> _poses = new List<GearRackPose>();

        public static RuntimeAnimatorController Controller { get; private set; }
        public static string Error { get; private set; }
        public static IReadOnlyList<GearRackPose> Poses => _poses;
        public static string[] ControllerClipNames { get; private set; } = Array.Empty<string>();
        public static int ModPoseCount => _poses.Count(p => p.ModClip != null);

        public static bool Loaded => Controller != null && _poses.Count > 0;

        public static bool EnsureLoaded()
        {
            return Loaded || Refresh();
        }

        public static bool Refresh()
        {
            Error = null;
            try
            {
                if (Controller == null)
                {
                    Controller = Resources.Load<RuntimeAnimatorController>(ControllerPath);
                    if (Controller == null)
                    {
                        Error = "Mannequin controller not found at Resources/" + ControllerPath;
                        PhotoLog.Warn("Gear Rack: " + Error);
                        return false;
                    }
                    ControllerClipNames = Controller.animationClips.Select(c => c != null ? c.name : "<null>").Distinct().ToArray();
                }

                if (!Singleton<CustomizationSolver>.Instantiated)
                {
                    Error = "CustomizationSolver not available";
                    return false;
                }

                CustomizationSolver solver = Singleton<CustomizationSolver>.Instance;
                var unlocked = new HashSet<string>();
                try
                {
                    foreach (CustomizationHideoutMannequinPose p in solver.GetAvailableHideoutMannequinPoses())
                    {
                        unlocked.Add(p.Id.ToString());
                    }
                }
                catch (Exception ex)
                {
                    PhotoLog.Warn("Gear Rack: unlocked pose list unavailable, showing all poses: " + ex.Message + "");
                }

                _poses.Clear();
                foreach (CustomizationHideoutMannequinPose p in solver.HideoutMannequinPoses)
                {
                    if (p == null || string.IsNullOrEmpty(p.PoseName))
                    {
                        continue;
                    }
                    string id = p.Id.ToString();
                    _poses.Add(new GearRackPose
                    {
                        Id = id,
                        PoseName = p.PoseName,
                        DisplayName = Localize(p.PoseName),
                        Unlocked = unlocked.Count == 0 || unlocked.Contains(id)
                    });
                }

                ResolveModClips();

                PhotoLog.Info($"Gear Rack: controller '{Controller.name}' with {ControllerClipNames.Length} clips; {_poses.Count} poses ({ModPoseCount} from mods): "
                              + string.Join(", ", _poses.Select(p => $"{p.PoseName}=\"{p.DisplayName}\"{(p.ModClip != null ? " [" + p.ModSource + "]" : "")}")));
                if (_poses.Count == 0)
                {
                    Error = "CustomizationSolver returned no mannequin poses";
                }
                return _poses.Count > 0;
            }
            catch (Exception ex)
            {
                Error = ex.GetType().Name + ": " + ex.Message;
                PhotoLog.Error("Gear Rack load failed: " + ex);
                return false;
            }
        }

        public static GearRackPose Find(string poseName)
        {
            return _poses.FirstOrDefault(p => p.PoseName == poseName) ?? BodyClipLibrary.Find(poseName);
        }

        public static void Validate(Animator animator)
        {
            if (animator == null)
            {
                return;
            }
            foreach (GearRackPose pose in _poses)
            {
                pose.StateExists = animator.HasState(0, Animator.StringToHash(pose.PoseName)) || pose.ModClip != null;
            }
        }

        private static void ResolveModClips()
        {
            var clips = new Dictionary<string, (AnimationClip clip, string source)>(StringComparer.Ordinal);
            foreach (BepInEx.PluginInfo info in Chainloader.PluginInfos.Values)
            {
                if (info?.Instance == null)
                {
                    continue;
                }
                Assembly asm = info.Instance.GetType().Assembly;
                if (asm == typeof(GearRackPoseLibrary).Assembly)
                {
                    continue;
                }
                foreach (Type t in SafeTypes(asm))
                {
                    if (t.ContainsGenericParameters)
                    {
                        continue;
                    }
                    FieldInfo[] fields;
                    try
                    {
                        fields = t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    foreach (FieldInfo f in fields)
                    {
                        if (!typeof(IDictionary<string, AnimationClip>).IsAssignableFrom(f.FieldType))
                        {
                            continue;
                        }
                        try
                        {
                            if (f.GetValue(null) is IDictionary<string, AnimationClip> dict)
                            {
                                foreach (KeyValuePair<string, AnimationClip> kv in dict)
                                {
                                    if (kv.Key != null && kv.Value != null && !clips.ContainsKey(kv.Key))
                                    {
                                        clips[kv.Key] = (kv.Value, info.Metadata.Name);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            PhotoLog.Debug($"Gear Rack: could not read {t.FullName}.{f.Name}: {ex.Message}");
                        }
                    }
                }
            }

            var vanillaClips = new HashSet<string>(ControllerClipNames, StringComparer.Ordinal);
            var wanted = new HashSet<string>(_poses.Where(p => !vanillaClips.Contains(p.PoseName) && !clips.ContainsKey(p.PoseName)).Select(p => p.PoseName), StringComparer.Ordinal);
            if (wanted.Count > 0)
            {
                foreach (AnimationClip c in Resources.FindObjectsOfTypeAll<AnimationClip>())
                {
                    if (c != null && wanted.Contains(c.name) && !clips.ContainsKey(c.name))
                    {
                        clips[c.name] = (c, "loaded clip");
                    }
                }
            }

            foreach (GearRackPose pose in _poses)
            {
                if (!vanillaClips.Contains(pose.PoseName) && clips.TryGetValue(pose.PoseName, out (AnimationClip clip, string source) found))
                {
                    pose.ModClip = found.clip;
                    pose.ModSource = found.source;
                }
            }
        }

        private static IEnumerable<Type> SafeTypes(Assembly asm)
        {
            try
            {
                return asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null);
            }
            catch (Exception)
            {
                return Array.Empty<Type>();
            }
        }

        private static string Localize(string poseName)
        {
            string key = "Hideout/Mannequin/Pose/" + poseName;
            try
            {
                string text = key.Localized();
                return string.IsNullOrEmpty(text) || text == key ? poseName : text;
            }
            catch (Exception)
            {
                return poseName;
            }
        }
    }
}
