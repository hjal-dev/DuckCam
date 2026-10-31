using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Comfort.Common;
using Diz.Jobs;
using EFT;
using EFT.Game.Spawning;
using EFT.InventoryLogic;
using DuckCam.Actors;
using DuckCam.Core;
using DuckCam.Pause;
using UnityEngine;
using UnityEngine.AI;

namespace DuckCam.Spawning
{
    internal sealed class BotDraft
    {
        public SpawnRole Role;
        public BotCreationData Data;
        public Profile Profile => Data?.Profiles?.FirstOrDefault();
        public InventoryEquipment Equipment => Profile?.Inventory?.Equipment;
    }

    internal sealed class SpawnedBot
    {
        public BotOwner Owner;
        public bool SessionOnly;
        public bool InfiniteAmmo;
        public string Name;
    }

    internal sealed class BotSpawnService
    {
        private readonly List<SpawnedBot> _spawned = new List<SpawnedBot>();
        private float _nextRefill;

        public string Status { get; private set; } = "";
        public int Pending { get; private set; }
        public IReadOnlyList<SpawnedBot> Spawned => _spawned;

        private static BotsController Controller =>
            Singleton<IBotGame>.Instantiated ? Singleton<IBotGame>.Instance?.BotsController : null;

        public static bool Available(out string reason)
        {
            BotsController c = Controller;
            reason = c == null ? "no bot game here (Hideout?)" : c.BotSpawner == null ? "bots are disabled in this raid" : null;
            return reason == null;
        }

        public async Task<BotDraft> Generate(SpawnRole role, BotDifficulty difficulty = BotDifficulty.normal)
        {
            if (!Available(out string reason))
            {
                Status = "Can't spawn: " + reason;
                return null;
            }
            BotSpawner spawner = Controller.BotSpawner;
            Pending++;
            try
            {
                var session = Singleton<ClientApplication<IEftSession>>.Instance?.GetClientBackEndSession();
                if (session == null)
                {
                    Status = "Can't spawn: no server session.";
                    return null;
                }
                Profile[] profiles = await session.LoadBots(new List<CountTypeBotWave> { new CountTypeBotWave(1, role.Role, difficulty) });
                Profile profile = profiles?.FirstOrDefault(p => p != null && p.Info?.Settings?.Role == role.Role)
                                  ?? profiles?.FirstOrDefault(p => p != null);
                if (profile == null)
                {
                    Status = $"The server returned no {role.Name} profile.";
                    PhotoLog.Warn($"Spawner: server returned no profile for {role.Role} ({difficulty}).");
                    return null;
                }
                var request = new GetProfileDataParams(profile.Info.Side, profile.Info.Settings.Role, profile.Info.Settings.BotDifficulty, 0f);
                BotCreationData data = BotCreationData.CreateWithoutProfile(request);
                data._botCreator = spawner._botCreator;
                data._tokenGetter = spawner;
                data.AddProfile(profile);
                PhotoLog.Verbose($"Spawner: got {profile.Nickname} ({profile.Info.Settings.Role}, {profile.Info.Side}, {profile.Info.Settings.BotDifficulty}).");
                return new BotDraft { Role = role, Data = data };
            }
            catch (Exception ex)
            {
                Status = $"{role.Name}: profile generation failed ({ex.Message}).";
                PhotoLog.Warn($"Bot profile generation failed for {role.Role}: {ex}");
                return null;
            }
            finally
            {
                Pending--;
            }
        }

        public static void ApplyKit(BotDraft draft, BotKit kit)
        {
            InventoryEquipment eq = draft?.Equipment;
            if (eq == null || kit == null)
            {
                return;
            }
            foreach (KeyValuePair<string, string> choice in kit.Slots)
            {
                if (!Enum.TryParse(choice.Key, out EquipmentSlot slotName))
                {
                    continue;
                }
                ApplyChoice(eq, slotName, choice.Value);
            }
            Loadout.ArmWeapons(eq, Mathf.Clamp(kit.SpareMags, 0, 8));
        }

        public static string ApplyChoice(InventoryEquipment eq, EquipmentSlot slotName, string key)
        {
            Slot slot = eq?.GetSlot(slotName);
            if (slot == null)
            {
                return "no " + slotName + " slot";
            }
            if (key == BotKits.None)
            {
                return Loadout.Replace(eq, slotName, null);
            }
            CatalogEntry entry = ItemCatalog.Find(slotName, slot, key);
            Item item = entry != null ? ItemCatalog.CreateItem(entry) : null;
            return item == null ? "item not found: " + key : Loadout.Replace(eq, slotName, item);
        }

        public static bool AimPoint(Transform camera, out Vector3 point, out string error)
        {
            point = default;
            error = null;
            if (camera == null)
            {
                error = "no camera";
                return false;
            }
            Vector3 target = Physics.Raycast(camera.position, camera.forward, out RaycastHit hit, 300f, LayersMaskController.HighPolyWithTerrainMask, QueryTriggerInteraction.Ignore)
                ? hit.point
                : camera.position + camera.forward * 5f;
            if (!NavMesh.SamplePosition(target, out NavMeshHit nav, 4f, NavMesh.AllAreas))
            {
                error = "no walkable ground there (aim at the floor where a bot could stand)";
                return false;
            }
            point = nav.position;
            return true;
        }

        public static bool TryPlace(Ray ray, out Vector3 point, out string error, bool photoOnly = false)
        {
            point = default;
            int mask = LayersMaskController.HighPolyWithTerrainMask;
            if (!Physics.Raycast(ray, out RaycastHit hit, 500f, mask, QueryTriggerInteraction.Ignore))
            {
                error = "Nothing to stand on there";
                return false;
            }
            if (hit.normal.y < 0.65f)
            {
                error = "Too steep (that's a wall)";
                return false;
            }
            if (photoOnly)
            {
                error = null;
                point = hit.point + Vector3.up * 0.02f;
                return true;
            }
            if (!NavMesh.SamplePosition(hit.point, out NavMeshHit nav, 1f, NavMesh.AllAreas) || Mathf.Abs(nav.position.y - hit.point.y) > 0.6f)
            {
                error = "Bots can't walk there";
                return false;
            }
            Vector3 feet = nav.position;
            if (Physics.CheckCapsule(feet + Vector3.up * 0.6f, feet + Vector3.up * 1.6f, 0.25f, mask, QueryTriggerInteraction.Ignore))
            {
                error = "No room to stand there";
                return false;
            }
            if (!Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, 1.5f, mask, QueryTriggerInteraction.Ignore))
            {
                error = "No floor under that spot";
                return false;
            }
            error = null;
            point = feet + Vector3.up * 0.05f;
            return true;
        }

        public void AddToPhoto(BotDraft draft, Vector3 position, Vector3 faceTowards, DuckCamController c)
        {
            if (draft?.Profile == null)
            {
                Status = "No bot prepared.";
                return;
            }
            if (!c.Actors.CanAdd)
            {
                Status = $"Character limit ({c.Actors.Limit}) reached; remove one in CHARACTERS or raise it in the config.";
                return;
            }
            string name = draft.Role?.Name ?? draft.Profile.Nickname;
            try
            {
                int layer = c.Player != null ? ActorManager.LayerOf(c.Player) : 0;
                PhotoActor actor = c.Actors.AddProfile(draft.Profile, name, draft.Profile.Nickname + " · photo only", position, faceTowards, layer, c.Restores);
                Status = actor != null ? $"Adding {name} to the photo (no AI). Pose and move it in CHARACTERS." : $"{name} could not be added.";
                PhotoLog.Info($"Spawner: photo-only character {name} ({draft.Profile.Info.Settings.Role}) at {position}.");
            }
            catch (Exception ex)
            {
                Status = $"{name}: adding to the photo failed ({ex.Message}).";
                PhotoLog.Warn($"Spawner: photo-only character {name} failed: {ex}");
            }
        }

        public async void Spawn(BotDraft draft, Vector3 position, bool sessionOnly, bool infiniteAmmo)
        {
            if (draft?.Data == null || draft.Profile == null || !Available(out string reason))
            {
                Status = "Can't spawn: " + (draft == null ? "no bot prepared" : "bots unavailable");
                return;
            }
            string name = draft.Profile.Nickname ?? draft.Role.Name;
            Pending++;
            try
            {
                Status = $"Loading {name}'s gear...";
                await Singleton<ObjectsFactory>.Instance.LoadBundlesAndCreatePools(
                    ObjectsFactory.PoolsCategory.Raid, ObjectsFactory.AssemblyType.Local,
                    draft.Profile.GetAllPrefabPaths(allCustomization: false).ToArray(),
                    JobYieldPriority.General, null, ObjectsFactory.DefaultCancellationToken);
            }
            catch (Exception ex)
            {
                Pending = Math.Max(0, Pending - 1);
                Status = $"{name}: loading the gear failed ({ex.Message}).";
                PhotoLog.Warn($"Spawner: bundle load for {name} failed: {ex}");
                return;
            }

            BotsController controller = Controller;
            BotSpawner spawner = controller?.BotSpawner;
            BotZone zone = spawner?.GetClosestZone(position, out float _);
            if (zone == null)
            {
                Pending = Math.Max(0, Pending - 1);
                Status = "Can't spawn: no bot zone on this map.";
                return;
            }
            int core = 0;
            try
            {
                core = controller.CoversData?.GetClosest(position)?.CorePointId ?? 0;
            }
            catch (Exception ex)
            {
                PhotoLog.Debug("Closest AI core point lookup failed: " + ex.Message);
            }
            var point = new SpawnPoint
            {
                Id = "DuckCam_" + Guid.NewGuid().ToString("N"),
                Name = "DuckCam spawn",
                Position = position,
                Rotation = Quaternion.identity,
                BotZone = zone,
                CorePointId = core,
                Sides = EPlayerSideMask.All,
                Categories = ESpawnCategoryMask.All,
                Infiltration = ""
            };
            bool done = false;
            Action<BotOwner> arrived = owner =>
            {
                done = true;
                Pending = Math.Max(0, Pending - 1);
                if (owner == null)
                {
                    Status = $"{name} failed to spawn.";
                    PhotoLog.Warn($"Spawner: {name} returned null.");
                    return;
                }
                _spawned.Add(new SpawnedBot { Owner = owner, SessionOnly = sessionOnly, InfiniteAmmo = infiniteAmmo, Name = name });
                bool joined = JoinFrozenScene(owner);
                Status = $"Spawned {name} ({draft.Role.Name}){(sessionOnly ? ", session only" : "")}.";
                PhotoLog.Verbose($"Spawned bot {name} ({draft.Role.Name}) at {position}." + (joined ? " Added to the frozen scene." : ""));
            };
            Status = $"Spawning {name}...";

            draft.Data.AddPosition(point.Position, point.CorePointId);
            spawner._inSpawnProcess++;
            try
            {
                await spawner._botCreator.ActivateBot(draft.Data, zone, false, spawner.GetGroupAndSetEnemies,
                    bot => ActivateSafely(spawner, bot, draft.Data, arrived),
                    spawner.GetCancelToken());
            }
            catch (Exception ex)
            {
                if (!done)
                {
                    spawner._inSpawnProcess = Math.Max(0, spawner._inSpawnProcess - 1);
                    Pending = Math.Max(0, Pending - 1);
                    done = true;
                    Status = $"{name} failed to spawn: {ex.GetType().Name}: {ex.Message}";
                }
                PhotoLog.Error($"Spawner: creating {name} failed: {ex}");
                return;
            }

            float started = Time.realtimeSinceStartup;
            while (!done && Time.realtimeSinceStartup - started < 20f)
            {
                await Task.Yield();
            }
            if (!done)
            {
                Pending = Math.Max(0, Pending - 1);
                Status = $"{name} didn't appear within 20 s.";
                PhotoLog.Warn($"Spawner: {name} not activated after 20 s. Bots: {Controller?.Bots?.BotOwners?.Count() ?? -1}.");
            }
        }

        private static readonly System.Reflection.FieldInfo BotCreatedEvent = HarmonyLib.AccessTools.Field(typeof(BotSpawner), "OnBotCreated");

        private static bool JoinFrozenScene(BotOwner owner)
        {
            if (owner == null || !(FreezeFlags.WorldTickFrozen || FreezeFlags.BotsFrozen))
            {
                return false;
            }
            try
            {
                BotsList bots = Controller?.Bots;
                if (bots == null || !bots._botsToAdd.Contains(owner))
                {
                    return false;
                }
                bots.AddFromList();
                return true;
            }
            catch (Exception ex)
            {
                PhotoLog.Warn("Spawner: adding the bot to the frozen scene failed: " + ex.Message);
                return false;
            }
        }

        private static void ActivateSafely(BotSpawner spawner, BotOwner bot, BotCreationData data, Action<BotOwner> arrived)
        {
            if (data.SpawnStopped || bot == null || BotCreatedEvent == null)
            {
                spawner.ActivateBotCallback(bot, data, arrived, false, System.Diagnostics.Stopwatch.StartNew());
                return;
            }
            bot.SpawnProfileData = data._profileData;
            spawner._bots.Add(bot);
            if (bot.Profile.Info.Settings.IsFollower())
            {
                spawner._followersBotsCount++;
            }
            else if (bot.Profile.Info.Settings.IsBoss())
            {
                spawner._bossBotsCount++;
            }
            spawner._allBotsCount++;
            if (BotCreatedEvent.GetValue(spawner) is Action<BotOwner> handlers)
            {
                foreach (Delegate handler in handlers.GetInvocationList())
                {
                    try
                    {
                        ((Action<BotOwner>)handler)(bot);
                    }
                    catch (Exception ex)
                    {
                        string owner = handler.Method.DeclaringType?.FullName ?? handler.Method.Name;
                        PhotoLog.WarnOnce("botcreated-" + owner, $"Spawner: another mod's bot-created handler failed ({owner}): {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            try
            {
                spawner.AfterCreation(bot);
            }
            catch (Exception ex)
            {
                PhotoLog.Warn("Spawner: AfterCreation failed: " + ex);
            }
            spawner._inSpawnProcess = Math.Max(0, spawner._inSpawnProcess - 1);
            arrived(bot);
        }

        public async void SpawnKit(BotKit kit, SpawnRole role, Vector3 center, int count, bool sessionOnly)
        {
            count = Mathf.Clamp(count, 1, 20);
            for (int i = 0; i < count; i++)
            {
                BotDraft draft = await Generate(role);
                if (draft == null)
                {
                    return;
                }
                ApplyKit(draft, kit);
                Vector3 spot = center;
                if (count > 1)
                {
                    float angle = i * Mathf.PI * 2f / count;
                    Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (0.8f + 0.15f * count);
                    if (NavMesh.SamplePosition(center + offset, out NavMeshHit nav, 2f, NavMesh.AllAreas))
                    {
                        spot = nav.position;
                    }
                }
                Spawn(draft, spot, sessionOnly, kit.InfiniteAmmo);
            }
        }

        private readonly List<Action> _queued = new List<Action>();

        public int Queued => _queued.Count;

        public void QueueUntilLive(Action spawn)
        {
            _queued.Add(spawn);
            Status = $"{_queued.Count} spawn(s) waiting: real bots join when the scene is live again (unfreeze).";
        }

        public void ClearQueue() => _queued.Clear();

        public void Tick()
        {
            if (_queued.Count > 0 && !FreezeFlags.WorldTickFrozen && !FreezeFlags.BotsFrozen)
            {
                List<Action> run = new List<Action>(_queued);
                _queued.Clear();
                PhotoLog.Info($"Spawner: running {run.Count} queued spawn(s).");
                foreach (Action spawn in run)
                {
                    try
                    {
                        spawn();
                    }
                    catch (Exception ex)
                    {
                        PhotoLog.Warn("Spawner: queued spawn failed: " + ex.Message);
                    }
                }
            }
            if (_spawned.Count == 0 || Time.unscaledTime < _nextRefill)
            {
                return;
            }
            _nextRefill = Time.unscaledTime + 1f;
            _spawned.RemoveAll(b => b.Owner == null || b.Owner.GetPlayer == null);
            foreach (SpawnedBot bot in _spawned)
            {
                if (!bot.InfiniteAmmo || bot.Owner.IsDead)
                {
                    continue;
                }
                try
                {
                    Loadout.Refill(bot.Owner.GetPlayer.Profile.Inventory.Equipment);
                }
                catch (Exception ex)
                {
                    PhotoLog.WarnOnce("refill", "Infinite ammo refill failed: " + ex.Message);
                }
            }
        }

        public void DespawnSessionBots()
        {
            int removed = 0;
            foreach (SpawnedBot bot in _spawned.Where(b => b.SessionOnly).ToList())
            {
                try
                {
                    if (bot.Owner != null)
                    {
                        Controller?.Bots?._botsToAdd?.Remove(bot.Owner);
                    }
                    if (bot.Owner != null && !bot.Owner.IsDead && bot.Owner.LeaveData != null)
                    {
                        bot.Owner.LeaveData.RemoveFromMap();
                        removed++;
                    }
                    else if (bot.Owner != null && !bot.Owner.IsDead)
                    {
                        PhotoLog.Warn($"Session bot {bot.Name} could not be removed (not fully active yet).");
                    }
                }
                catch (Exception ex)
                {
                    PhotoLog.Warn($"Could not remove session bot {bot.Name}: {ex.Message}");
                }
                _spawned.Remove(bot);
            }
            if (removed > 0)
            {
                PhotoLog.Info($"Removed {removed} session-only bot(s).");
            }
        }
    }
}
