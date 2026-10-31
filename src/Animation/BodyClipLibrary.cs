using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AnimationSystem.RootMotionTable;
using Comfort.Common;
using Diz.Resources;
using EFT;
using DuckCam.Actors;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Animation
{
    internal enum BodyCoverage
    {
        Untested,
        None,
        FullBody,
        UpperBody,
        LowerBody
    }

    internal static class BodyClipLibrary
    {
        public const string Prefix = "body:";
        private const int ProbesPerFrame = 12;

        public static readonly string[] Groups =
        {
            "Idle", "Crouch", "Prone", "Aim", "Move", "Sprint", "Reload", "Lean", "Grenade", "Melee",
            "Injured", "Jump & Climb", "Interact", "Gesture", "Other"
        };

        private static readonly List<GearRackPose> _poses = new List<GearRackPose>();
        private static int _probeIndex;

        public static IReadOnlyList<GearRackPose> Poses => _poses;
        public static string Source { get; private set; } = "not collected";
        public static bool Testing { get; private set; }
        public static int Tested => _poses.Count(p => p.Coverage != BodyCoverage.Untested);
        public static int Compatible => _poses.Count(p => p.Coverage != BodyCoverage.Untested && p.Coverage != BodyCoverage.None);

        public static string Status => _poses.Count == 0
            ? "No body animations found (" + Source + ")"
            : Testing
                ? $"Testing animations on your character: {Tested}/{_poses.Count}"
                : Tested == 0
                    ? $"{_poses.Count} animations found; add a character to test them"
                    : $"{Compatible} of {_poses.Count} animations work on characters";

        public static void Refresh(Player player)
        {
            var clips = new Dictionary<string, (AnimationClip clip, int layer)>(StringComparer.Ordinal);
            var sources = new List<string>();
            try
            {
                if (player?.BodyAnimatorCommon is AnimationSystem.UnityAnimatorWrapper wrapper && wrapper.Animator != null
                    && wrapper.Animator.runtimeAnimatorController != null)
                {
                    int before = clips.Count;
                    foreach (AnimationClip c in wrapper.Animator.runtimeAnimatorController.animationClips)
                    {
                        if (c != null && !clips.ContainsKey(c.name))
                        {
                            clips[c.name] = (c, -1);
                        }
                    }
                    sources.Add($"body animator '{wrapper.Animator.runtimeAnimatorController.name}' ({clips.Count - before})");
                }
            }
            catch (Exception ex)
            {
                PhotoLog.Warn("Pose library: body animator clips unavailable: " + ex.Message);
            }

            try
            {
                CharacterClipsKeeper keeper = Singleton<IEasyAssets>.Instantiated
                    ? Singleton<IEasyAssets>.Instance.GetAsset<CharacterClipsKeeper>(InGameBundles.PLAYER_ANIMATION_CLIPS_KEEPER)
                    : null;
                if (keeper?.Clips != null)
                {
                    int before = clips.Count;
                    for (int layer = 0; layer < keeper.Clips.Length; layer++)
                    {
                        CharacterClipsKeeper.AnimationClipData[] data = keeper.Clips[layer].clips;
                        if (data == null)
                        {
                            continue;
                        }
                        foreach (CharacterClipsKeeper.AnimationClipData d in data)
                        {
                            if (d.clip != null && !clips.ContainsKey(d.clip.name))
                            {
                                clips[d.clip.name] = (d.clip, layer);
                            }
                        }
                    }
                    sources.Add($"clip keeper ({clips.Count - before})");
                }
            }
            catch (Exception ex)
            {
                PhotoLog.Debug("Pose library: clip keeper unavailable: " + ex.Message);
            }

            Dictionary<string, BodyCoverage> known = _poses.ToDictionary(p => p.PoseName, p => p.Coverage);
            _poses.Clear();
            foreach (KeyValuePair<string, (AnimationClip clip, int layer)> kv in clips.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                string poseName = Prefix + kv.Key;
                known.TryGetValue(poseName, out BodyCoverage coverage);
                _poses.Add(new GearRackPose
                {
                    Id = poseName,
                    PoseName = poseName,
                    DisplayName = Pretty(kv.Key),
                    Unlocked = true,
                    ModClip = kv.Value.clip,
                    ModSource = kv.Value.layer >= 0 ? "body animation, layer " + kv.Value.layer : "body animation",
                    Group = Classify(kv.Key),
                    Coverage = coverage,
                    StateExists = coverage == BodyCoverage.Untested ? (bool?)null : coverage != BodyCoverage.None
                });
            }
            Source = sources.Count > 0 ? string.Join(", ", sources) : "no body animator found";
            _probeIndex = 0;
            Testing = _poses.Any(p => p.Coverage == BodyCoverage.Untested);
            PhotoLog.Info($"Pose library: {_poses.Count} body animations from {Source}.");
        }

        public static void TickTesting(PhotoActor actor)
        {
            if (!Testing || actor == null || actor.Status != ActorStatus.Ready || actor.Reloading || actor.Animate)
            {
                return;
            }
            int done = 0;
            try
            {
                while (_probeIndex < _poses.Count && done < ProbesPerFrame)
                {
                    GearRackPose pose = _poses[_probeIndex++];
                    if (pose.Coverage != BodyCoverage.Untested)
                    {
                        continue;
                    }
                    pose.Coverage = actor.ProbeClip(pose.ModClip);
                    pose.StateExists = pose.Coverage != BodyCoverage.None;
                    done++;
                }
            }
            finally
            {
                if (done > 0)
                {
                    actor.RestoreCurrentPose();
                }
            }
            if (_probeIndex >= _poses.Count)
            {
                Testing = false;
                PhotoLog.Info($"Pose library: {Compatible} of {_poses.Count} body animations move a photo actor "
                              + $"({_poses.Count(p => p.Coverage == BodyCoverage.FullBody)} full body, "
                              + $"{_poses.Count(p => p.Coverage == BodyCoverage.UpperBody)} upper body, "
                              + $"{_poses.Count(p => p.Coverage == BodyCoverage.LowerBody)} legs).");
            }
        }

        public static GearRackPose Find(string poseName)
        {
            return _poses.FirstOrDefault(p => p.PoseName == poseName);
        }

        public static string CoverageText(BodyCoverage c)
        {
            switch (c)
            {
                case BodyCoverage.FullBody: return "full body";
                case BodyCoverage.UpperBody: return "upper body";
                case BodyCoverage.LowerBody: return "legs";
                case BodyCoverage.None: return "no effect";
                default: return "untested";
            }
        }

        public static string Classify(string clipName)
        {
            string n = clipName.ToLowerInvariant();
            if (Has(n, "prone")) return "Prone";
            if (Has(n, "crouch", "squat", "sit", "duck", "kneel")) return "Crouch";
            if (Has(n, "reload", "magazine", "_mag", "chamber", "bolt", "pump", "ammo", "unload")) return "Reload";
            if (Has(n, "grenade", "throw")) return "Grenade";
            if (Has(n, "knife", "melee", "kick", "punch", "bayonet", "stab")) return "Melee";
            if (Has(n, "lean", "tilt", "peek", "blind")) return "Lean";
            if (Has(n, "sprint")) return "Sprint";
            if (Has(n, "jump", "fall", "land", "vault", "climb", "ladder", "step_over", "overcome")) return "Jump & Climb";
            if (Has(n, "hurt", "injur", "pain", "limp", "hit_", "_hit", "death", "die", "dead", "stun", "fracture", "tremor", "contusion")) return "Injured";
            if (Has(n, "walk", "run", "move", "strafe", "jog", "locomotion", "forward", "backward", "turn")) return "Move";
            if (Has(n, "aim", "ads", "shoot", "fire", "recoil", "shot", "point")) return "Aim";
            if (Has(n, "door", "loot", "open", "close", "interact", "use", "heal", "med", "eat", "drink", "inject", "bandage", "splint", "search", "pickup", "pick_up", "plant", "keycard", "switch")) return "Interact";
            if (Has(n, "gesture", "wave", "hello", "emote", "hands_up", "salute", "thumb")) return "Gesture";
            if (Has(n, "idle", "stand", "stance", "pose", "rest", "ready", "lowered", "lower")) return "Idle";
            return "Other";
        }

        private static bool Has(string name, params string[] keys)
        {
            foreach (string k in keys)
            {
                if (name.Contains(k))
                {
                    return true;
                }
            }
            return false;
        }

        public static string Pretty(string clipName)
        {
            string s = clipName.Replace('_', ' ').Replace('-', ' ').Trim();
            while (s.Contains("  "))
            {
                s = s.Replace("  ", " ");
            }
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
        }
    }
}
