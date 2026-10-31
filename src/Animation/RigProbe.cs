using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EFT;
using UnityEngine;

namespace DuckCam.Animation
{
    internal static class RigProbe
    {
        private static readonly string[] _keyBones =
        {
            "Base HumanPelvis", "Base HumanSpine2", "Base HumanSpine3", "Base HumanHead",
            "Base HumanLPalm", "Base HumanRPalm", "Base HumanLFoot", "Base HumanRFoot"
        };

        public static string LastReport { get; private set; } = "Not run.";

        public static void Run(Player player, Animator doubleAnimator)
        {
            var sb = new StringBuilder();
            try
            {
                sb.AppendLine($"UseBodyFastAnimator: {AppEnvironment.Config?.UseBodyFastAnimator.ToString() ?? "config null"}");
                sb.AppendLine($"Body update mode: {player.BodyUpdateMode}, arms update mode: {player.ArmsUpdateMode}");

                Animator[] live = player.GetComponentsInChildren<Animator>(true);
                sb.AppendLine($"Animators under live player: {live.Length}");
                Animator liveBody = null;
                int bestCount = -1;
                foreach (Animator a in live)
                {
                    int count = a.GetComponentsInChildren<Transform>(true).Length;
                    sb.AppendLine("  " + Describe(a) + $", transforms={count}");
                    if (count > bestCount)
                    {
                        bestCount = count;
                        liveBody = a;
                    }
                }

                if (doubleAnimator != null)
                {
                    sb.AppendLine("Double: " + Describe(doubleAnimator));
                }

                if (liveBody != null && doubleAnimator != null)
                {
                    HashSet<string> livePaths = Paths(liveBody.transform);
                    HashSet<string> doublePaths = Paths(doubleAnimator.transform);
                    int common = livePaths.Count(doublePaths.Contains);
                    sb.AppendLine($"Bone paths: live={livePaths.Count}, double={doublePaths.Count}, common={common}");
                    foreach (string bone in _keyBones)
                    {
                        string liveHit = livePaths.FirstOrDefault(p => p.EndsWith("/" + bone, StringComparison.Ordinal) || p == bone);
                        string doubleHit = doublePaths.FirstOrDefault(p => p.EndsWith("/" + bone, StringComparison.Ordinal) || p == bone);
                        string verdict = liveHit != null && liveHit == doubleHit ? "same path" : (liveHit != null && doubleHit != null ? "DIFFERENT path" : "missing");
                        sb.AppendLine($"  {bone}: {verdict}");
                        if (verdict == "DIFFERENT path")
                        {
                            sb.AppendLine($"    live:   {liveHit}");
                            sb.AppendLine($"    double: {doubleHit}");
                        }
                    }
                }
                else if (doubleAnimator == null)
                {
                    sb.AppendLine("Create the photo double (Gear Rack tab) to compare bone paths.");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("Probe error: " + ex.Message);
            }
            LastReport = sb.ToString();
        }

        private static string Describe(Animator a)
        {
            RuntimeAnimatorController c = a.runtimeAnimatorController;
            Avatar avatar = a.avatar;
            return $"{a.name}: controller={(c != null ? c.name : "none")}, layers={(c != null ? a.layerCount : 0)}, "
                   + $"avatar={(avatar != null ? avatar.name : "none")} human={(avatar != null && avatar.isHuman)} valid={(avatar != null && avatar.isValid)}, "
                   + $"enabled={a.enabled}, update={a.updateMode}, culling={a.cullingMode}, rootMotion={a.applyRootMotion}";
        }

        private static HashSet<string> Paths(Transform root)
        {
            var result = new HashSet<string>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root)
                {
                    continue;
                }
                var parts = new List<string>();
                for (Transform p = t; p != null && p != root; p = p.parent)
                {
                    parts.Add(p.name);
                }
                parts.Reverse();
                result.Add(string.Join("/", parts));
            }
            return result;
        }
    }
}
