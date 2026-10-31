using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EFT;
using DuckCam.Core;

namespace DuckCam.Spawning
{
    internal sealed class SpawnRole
    {
        public WildSpawnType Role;
        public string Name;
        public string Group;
        public EPlayerSide Side;
        public bool Modded;
    }

    internal static class BotRoles
    {
        public static readonly string[] GroupOrder = { "PMC", "Scav", "Boss", "Follower", "Raider", "Rogue", "Cultist", "Infected", "Modded", "Other" };

        private static List<SpawnRole> _cache;
        private static HashSet<int> _moreBots;

        public static void Invalidate()
        {
            _cache = null;
        }

        public static List<SpawnRole> All()
        {
            if (_cache != null)
            {
                return _cache;
            }
            Dictionary<int, string> custom = MoreBotsRoles();
            _moreBots = new HashSet<int>(custom.Keys);
            var byValue = new Dictionary<int, SpawnRole>();

            foreach (WildSpawnType role in Enum.GetValues(typeof(WildSpawnType)).Cast<WildSpawnType>())
            {
                string name = role.ToString();
                if (name.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("tutorial", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }
                byValue[(int)role] = Make(role, custom.TryGetValue((int)role, out string n) ? n : null);
            }
            foreach (KeyValuePair<int, string> kv in custom)
            {
                if (!byValue.ContainsKey(kv.Key))
                {
                    byValue[kv.Key] = Make((WildSpawnType)kv.Key, kv.Value);
                }
            }
            try
            {
                GameWorld world = GameContext.World;
                if (world != null)
                {
                    foreach (Player p in world.AllPlayersEverExisted)
                    {
                        WildSpawnType role = p?.Profile?.Info?.Settings?.Role ?? WildSpawnType.marksman;
                        if (p != null && p.IsAI && !byValue.ContainsKey((int)role))
                        {
                            byValue[(int)role] = Make(role, null);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PhotoLog.Debug("Role scan of raid players failed: " + ex.Message);
            }

            _cache = byValue.Values
                .OrderBy(r => Array.IndexOf(GroupOrder, r.Group))
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return _cache;
        }

        private static SpawnRole Make(WildSpawnType role, string customName)
        {
            bool modded = customName != null || !Enum.IsDefined(typeof(WildSpawnType), role);
            return new SpawnRole
            {
                Role = role,
                Modded = modded,
                Name = customName ?? Pretty(role),
                Group = modded ? "Modded" : GroupOf(role),
                Side = role == WildSpawnType.pmcUSEC ? EPlayerSide.Usec : role == WildSpawnType.pmcBEAR ? EPlayerSide.Bear : EPlayerSide.Savage
            };
        }

        public static string GroupOf(WildSpawnType role)
        {
            if (_moreBots != null && _moreBots.Contains((int)role))
            {
                return "Modded";
            }
            if (role == WildSpawnType.pmcUSEC || role == WildSpawnType.pmcBEAR) return "PMC";
            string r = role.ToString();
            if (r.StartsWith("boss", StringComparison.OrdinalIgnoreCase)) return "Boss";
            if (r.StartsWith("follower", StringComparison.OrdinalIgnoreCase)) return "Follower";
            if (r.StartsWith("sectant", StringComparison.OrdinalIgnoreCase)) return "Cultist";
            if (r == "pmcBot") return "Raider";
            if (r == "exUsec") return "Rogue";
            if (r.IndexOf("infected", StringComparison.OrdinalIgnoreCase) >= 0) return "Infected";
            if (r == "assault" || r == "marksman" || r.IndexOf("assault", StringComparison.OrdinalIgnoreCase) >= 0) return "Scav";
            return "Other";
        }

        public static string Pretty(WildSpawnType role)
        {
            if (role == WildSpawnType.pmcBEAR) return "PMC BEAR";
            if (role == WildSpawnType.pmcUSEC) return "PMC USEC";
            try
            {
                string key = role.GetScavRoleKey();
                string text = key.Localized();
                if (!string.IsNullOrWhiteSpace(text) && text != key)
                {
                    string raw = role.ToString();
                    return text.Equals(raw, StringComparison.OrdinalIgnoreCase) ? text : $"{text} ({raw})";
                }
            }
            catch (Exception)
            {
            }
            return role.ToString();
        }

        private static Dictionary<int, string> MoreBotsRoles()
        {
            var result = new Dictionary<int, string>();
            try
            {
                Type manager = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a =>
                    {
                        try
                        {
                            return a.GetTypes().FirstOrDefault(t => t.Name == "CustomWildSpawnTypeManager");
                        }
                        catch (Exception)
                        {
                            return null;
                        }
                    })
                    .FirstOrDefault(t => t != null);
                if (manager == null)
                {
                    return result;
                }
                var method = manager.GetMethod("GetCustomWildSpawnTypes", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (!(method?.Invoke(null, null) is IEnumerable list))
                {
                    return result;
                }
                foreach (object entry in list)
                {
                    Type t = entry.GetType();
                    object value = t.GetProperty("WildSpawnTypeValue")?.GetValue(entry) ?? t.GetField("WildSpawnTypeValue")?.GetValue(entry);
                    object name = t.GetProperty("WildSpawnTypeName")?.GetValue(entry) ?? t.GetField("WildSpawnTypeName")?.GetValue(entry);
                    if (value is int v)
                    {
                        result[v] = name as string ?? ("role " + v);
                    }
                }
                PhotoLog.Info($"MoreBots: {result.Count} custom role(s) found for the spawner.");
            }
            catch (Exception ex)
            {
                PhotoLog.Debug("MoreBots role lookup failed: " + ex.Message);
            }
            return result;
        }
    }
}
