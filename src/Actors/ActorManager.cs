using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using DuckCam.Animation;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Actors
{
    internal sealed class ActorManager
    {
        private readonly List<PhotoActor> _actors = new List<PhotoActor>();
        private readonly List<(Renderer renderer, bool forcedOff)> _hiddenNpcs = new List<(Renderer, bool)>();
        private RestoreHandle _npcHandle;

        public IReadOnlyList<PhotoActor> Actors => _actors;
        public int Selected { get; set; }
        public PhotoActor SelectedActor => Selected >= 0 && Selected < _actors.Count ? _actors[Selected] : null;
        public int Limit => DuckCamConfig.ActorLimit.Value;
        public bool CanAdd => _actors.Count < Limit;
        public bool SurroundingHidden => _npcHandle != null && !_npcHandle.Done;
        public PhotoActor Pmc => _actors.FirstOrDefault(a => a.IsPmc);

        public PhotoActor AddPmc(Player pmc, RestoreStack stack)
        {
            if (Pmc != null)
            {
                Selected = _actors.IndexOf(Pmc);
                return Pmc;
            }
            var actor = new PhotoActor
            {
                Name = "Your PMC",
                Info = pmc.Profile?.Nickname,
                IsPmc = true,
                SourceProfileId = pmc.ProfileId,
                SpawnPosition = pmc.Transform.position,
                SpawnRotation = pmc.Transform.rotation
            };
            KeepStance(actor, pmc);
            return Add(actor, pmc.Profile.GetVisualEquipmentState(true), LayerOf(pmc), pmc, stack);
        }

        public PhotoActor AddCopy(Player source, bool atCamera, Transform camera, bool hideOriginal, RestoreStack stack)
        {
            if (!CanAdd)
            {
                return null;
            }
            var actor = new PhotoActor
            {
                Name = UniqueName(Describe(source)),
                Info = RoleText(source),
                SourceProfileId = source.ProfileId
            };
            PlaceSpawn(actor, atCamera, camera, source.Transform.position, source.Transform.rotation);
            KeepStance(actor, source);
            return Add(actor, source.Profile.GetVisualEquipmentState(true), LayerOf(source), hideOriginal && !atCamera ? source : null, stack);
        }

        public PhotoActor AddProfile(Profile profile, string name, string info, Vector3 position, Vector3 faceTowards, int layer, RestoreStack stack)
        {
            if (!CanAdd || profile == null)
            {
                return null;
            }
            Vector3 look = Vector3.ProjectOnPlane(faceTowards - position, Vector3.up);
            var actor = new PhotoActor
            {
                Name = UniqueName(name),
                Info = info,
                SpawnPosition = position,
                SpawnRotation = look.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(look.normalized, Vector3.up) : Quaternion.identity
            };
            return Add(actor, profile.GetVisualEquipmentState(true), layer, null, stack);
        }

        public PhotoActor AddSaved(SavedActor saved, Transform camera, int layer, RestoreStack stack, out string error)
        {
            error = null;
            if (!CanAdd)
            {
                error = $"actor limit ({Limit}) reached";
                return null;
            }
            PlayerVisualRepresentation visual = SavedActors.ToVisual(saved, out error);
            if (visual == null)
            {
                return null;
            }
            var actor = new PhotoActor
            {
                Name = UniqueName(saved.Name),
                Info = saved.Info,
                Yaw = saved.Yaw
            };
            PlaceSpawn(actor, true, camera, camera.position, camera.rotation);
            PhotoActor added = Add(actor, visual, layer, null, stack);
            added.PendingPose = saved.PoseName;
            added.PendingPoseTime = saved.PoseTime;
            added.PendingWeapon = saved.WeaponVisible;
            added.PendingPoseState = saved.PoseState;
            added.PendingGrip = saved.Grip;
            added.PendingLoad = true;
            foreach (string slotName in saved.HiddenGear ?? new List<string>())
            {
                if (Enum.TryParse(slotName, out EFT.InventoryLogic.EquipmentSlot slot))
                {
                    added.SetSlotHidden(slot, true);
                }
            }
            return added;
        }

        public void Remove(PhotoActor actor)
        {
            if (actor == null)
            {
                return;
            }
            actor.Remove();
            _actors.Remove(actor);
            Selected = Mathf.Clamp(Selected, 0, Math.Max(0, _actors.Count - 1));
        }

        public void RemoveAll()
        {
            foreach (PhotoActor a in _actors.ToList())
            {
                a.Remove();
            }
            _actors.Clear();
            Selected = 0;
        }

        public void Tick()
        {
            foreach (PhotoActor a in _actors.ToList())
            {
                a.Tick();
                if (a == _actors.FirstOrDefault(x => x.Status == ActorStatus.Ready && !x.Reloading))
                {
                    BodyClipLibrary.TickTesting(a);
                }
                if (a.Status == ActorStatus.Ready && a.PendingLoad)
                {
                    GearRackPose pose = a.PendingPose != null ? GearRackPoseLibrary.Find(a.PendingPose) : null;
                    if (pose != null)
                    {
                        a.ApplyPose(pose, a.PendingPoseTime, out _);
                    }
                    a.SetWeaponVisible(a.PendingWeapon);
                    if (!string.IsNullOrEmpty(a.PendingPoseState))
                    {
                        a.RestorePoseState(a.PendingPoseState, a.PendingGrip);
                    }
                    a.ApplyTransform();
                    a.PendingPose = null;
                    a.PendingPoseState = null;
                    a.PendingGrip = null;
                    a.PendingLoad = false;
                }
            }
        }

        public void LateTick(UnityEngine.Camera camera)
        {
            foreach (PhotoActor a in _actors)
            {
                a.LateTick(camera);
            }
        }

        public void SetSurroundingHidden(bool hide, Player mainPlayer, RestoreStack stack)
        {
            if (hide == SurroundingHidden)
            {
                return;
            }
            if (!hide)
            {
                _npcHandle.Run();
                _npcHandle = null;
                return;
            }
            GameWorld world = GameContext.World;
            if (world == null)
            {
                return;
            }
            _hiddenNpcs.Clear();
            foreach (Player p in world.AllAlivePlayersList)
            {
                if (p == null || p == mainPlayer)
                {
                    continue;
                }
                foreach (Renderer r in p.GetComponentsInChildren<Renderer>(true))
                {
                    _hiddenNpcs.Add((r, r.forceRenderingOff));
                    r.forceRenderingOff = true;
                }
            }
            _npcHandle = stack.Push("hide surrounding NPCs", () =>
            {
                foreach ((Renderer renderer, bool forcedOff) in _hiddenNpcs)
                {
                    if (renderer != null)
                    {
                        renderer.forceRenderingOff = forcedOff;
                    }
                }
                _hiddenNpcs.Clear();
            });
        }

        public void ResetSession()
        {
            _actors.Clear();
            _npcHandle = null;
            Selected = 0;
        }

        public static List<Player> Candidates(Vector3 from, int max)
        {
            GameWorld world = GameContext.World;
            if (world == null)
            {
                return new List<Player>();
            }
            return world.AllAlivePlayersList
                .Where(p => p != null && p.Profile != null)
                .OrderBy(p => (p.Transform.position - from).sqrMagnitude)
                .Take(max)
                .ToList();
        }

        public static string Describe(Player p)
        {
            string nick = p.Profile?.Nickname;
            return string.IsNullOrEmpty(nick) ? (p.IsYourPlayer ? "You" : "Unknown") : nick;
        }

        public static string Faction(Player p)
        {
            if (p.IsYourPlayer)
            {
                return "You";
            }
            try
            {
                EPlayerSide side = p.Profile.Info.Side;
                if (side == EPlayerSide.Usec || side == EPlayerSide.Bear)
                {
                    return "PMC " + side.ToString().ToUpperInvariant();
                }
                WildSpawnType role = p.Profile.Info.Settings.Role;
                if (!Enum.IsDefined(typeof(WildSpawnType), role))
                {
                    return "Modded: " + ModdedRoleName(role);
                }
                string r = role.ToString();
                if (r.StartsWith("boss", StringComparison.OrdinalIgnoreCase)) return "Boss";
                if (r.StartsWith("follower", StringComparison.OrdinalIgnoreCase)) return "Follower";
                if (r.StartsWith("sectant", StringComparison.OrdinalIgnoreCase)) return "Cultist";
                if (r == "pmcBot") return "Raider";
                if (r == "exUsec") return "Rogue";
                if (r.IndexOf("infected", StringComparison.OrdinalIgnoreCase) >= 0) return "Infected";
                return "Scav";
            }
            catch (Exception)
            {
                return "Other";
            }
        }

        private static string ModdedRoleName(WildSpawnType role)
        {
            try
            {
                string key = role.GetScavRoleKey();
                string text = key.Localized();
                if (!string.IsNullOrWhiteSpace(text) && text != key)
                {
                    return text;
                }
            }
            catch (Exception)
            {
            }
            return "role " + ((int)role).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string RoleText(Player p)
        {
            if (p.IsYourPlayer)
            {
                return "You";
            }
            InfoClassSafe(p, out string side, out string role);
            return string.IsNullOrEmpty(role) || role == side ? side : $"{side} · {role}";
        }

        private static void InfoClassSafe(Player p, out string side, out string role)
        {
            side = "?";
            role = null;
            try
            {
                side = p.Profile.Info.Side.ToString();
                role = p.Profile.Info.Settings?.Role.ToString();
            }
            catch (Exception)
            {
            }
        }

        private PhotoActor Add(PhotoActor actor, PlayerVisualRepresentation visual, int layer, Player hideSource, RestoreStack stack)
        {
            _actors.Add(actor);
            Selected = _actors.Count - 1;
            actor.Begin(visual, layer, hideSource, stack);
            return actor;
        }

        private static void PlaceSpawn(PhotoActor actor, bool atCamera, Transform camera, Vector3 position, Quaternion rotation)
        {
            if (!atCamera || camera == null)
            {
                actor.SpawnPosition = position;
                actor.SpawnRotation = rotation;
                return;
            }
            Vector3 forward = Vector3.ProjectOnPlane(camera.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 1e-4f)
            {
                forward = Vector3.forward;
            }
            Vector3 spot = camera.position + forward * 3f;
            if (Physics.Raycast(spot + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 20f, LayersMaskController.HighPolyWithTerrainMask, QueryTriggerInteraction.Ignore))
            {
                spot = hit.point;
            }
            actor.SpawnPosition = spot;
            actor.SpawnRotation = Quaternion.LookRotation(-forward, Vector3.up);
        }

        private string UniqueName(string name)
        {
            string candidate = name;
            int n = 2;
            while (_actors.Any(a => a.Name == candidate))
            {
                candidate = $"{name} ({n++})";
            }
            return candidate;
        }

        private static void KeepStance(PhotoActor actor, Player source)
        {
            if (!DuckCamConfig.CopyKeepsStance.Value || source == null)
            {
                return;
            }
            actor.Stance = StanceSnapshot.Capture(source);
        }

        public static int LayerOf(Player player)
        {
            if (player.PlayerBody != null)
            {
                SkinnedMeshRenderer skin = player.PlayerBody.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (skin != null)
                {
                    return skin.gameObject.layer;
                }
            }
            int playerLayer = LayerMask.NameToLayer("Player");
            return playerLayer >= 0 ? playerLayer : 0;
        }
    }
}
