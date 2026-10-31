using System.Collections.Generic;
using EFT;
using EFT.Ballistics;
using EFT.CameraControl;
using EFT.HealthSystem;
using EFT.InputSystem;
using HarmonyLib;
using DuckCam.Core;
using DuckCam.Pause;

namespace DuckCam.Patches
{
    internal static class PatchIds
    {
        public const string WorldTick = "P1.WorldTick";
        public const string OtherWorldTick = "P2.OtherWorldTick";
        public const string Bots = "P3.Bots";
        public const string EndByTimer = "P4.EndByTimer";
        public const string ApplyDamage = "P5.ApplyDamage";
        public const string Kill = "P6.Kill";
        public const string ChangeHealth = "P7a.ChangeHealth";
        public const string ChangeEnergy = "P7b.ChangeEnergy";
        public const string ChangeHydration = "P7c.ChangeHydration";
        public const string DoBleed = "P8a.DoBleed";
        public const string DoFracture = "P8b.DoFracture";
        public const string HandleFall = "P9.HandleFall";
        public const string ApplyShot = "P10.ApplyShot";
        public const string Input = "P11.InputDispatch";
        public const string CameraSnap = "P12.CameraForceSetPosition";
        public const string CullingUpdate = "P13.CullingManagerUpdate";
        public const string CullingRender = "P14.CullingManagerCameraRender";
        public const string BotFixedUpdate = "P15.BotFixedUpdate";
        public const string BotBody = "P16.BotBody";
        public const string BotHearing = "P17.BotHearing";
        public const string BotBodyFound = "P18.BotBodyFound";
        public const string BotGetHit = "P19.BotGetHit";
        public const string BotKilledBy = "P20.BotKilledBy";
        public const string BotSay = "P21.BotSay";
        public const string StateMove = "P24.StateMove";
        public const string PhysicsUpdate = "P25.PhysicsUpdate";
        public const string BotsAddGuard = "P26.BotsAddGuard";
        public const string BotLateMove = "P22.BotLateMove";

        public static readonly string[] BotOnlyLayer = { Bots, BotFixedUpdate, BotBody, BotHearing };

        public static readonly string[] DamageLayer = { ApplyDamage, Kill, ChangeHealth, ChangeEnergy, ChangeHydration, DoBleed, DoFracture, HandleFall, ApplyShot };
    }

    internal static class FreezePatches
    {
        public static void Bind()
        {
            var t = typeof(FreezePatches);

            PatchRegistry.Prefix(PatchIds.WorldTick, "World freeze", typeof(GameWorld), nameof(GameWorld.DoWorldTick), new[] { typeof(float) }, t, nameof(WorldTickPrefix));
            PatchRegistry.Prefix(PatchIds.OtherWorldTick, "World freeze", typeof(GameWorld), nameof(GameWorld.DoOtherWorldTick), new[] { typeof(float) }, t, nameof(WorldTickPrefix));
            PatchRegistry.Prefix(PatchIds.Bots, "Bot freeze", typeof(BotsController), nameof(BotsController.UpdateByUnity), System.Type.EmptyTypes, t, nameof(BotsPrefix));
            PatchRegistry.Prefix(PatchIds.BotFixedUpdate, "Freeze bots only", typeof(BotOwner), nameof(BotOwner.FixedUpdate), System.Type.EmptyTypes, t, nameof(BotsPrefix));
            PatchRegistry.Prefix(PatchIds.BotBody, "Freeze bots only", typeof(Player), nameof(Player.ComplexUpdate), new[] { typeof(EUpdateQueue), typeof(float) }, t, nameof(BotBodyPrefix));
            PatchRegistry.Prefix(PatchIds.BotHearing, "Freeze bots only", typeof(BotHearingSensor), nameof(BotHearingSensor.OnSoundPlayed), new[] { typeof(IPlayer), typeof(UnityEngine.Vector3), typeof(float), typeof(AISoundType) }, t, nameof(BotsPrefix));
            PatchRegistry.Prefix(PatchIds.BotBodyFound, "Freeze bots only", typeof(BotOwner), nameof(BotOwner.OnBodyBotDead), new[] { typeof(UnityEngine.Vector3) }, t, nameof(BotsPrefix));
            PatchRegistry.Prefix(PatchIds.BotGetHit, "Freeze bots only", typeof(BotOwner), nameof(BotOwner.OnGetHit), new[] { typeof(DamageInfo), typeof(EBodyPart), typeof(float) }, t, nameof(BotsPrefix));
            PatchRegistry.Prefix(PatchIds.BotKilledBy, "Freeze bots only", typeof(BotOwner), nameof(BotOwner.OnBeenKilledByAggressor), new[] { typeof(Player), typeof(IPlayer), typeof(DamageInfo), typeof(EBodyPart) }, t, nameof(BotsPrefix));
            PatchRegistry.Prefix(PatchIds.BotLateMove, "Freeze bots only", typeof(Player), nameof(Player.ComplexLateUpdate), new[] { typeof(EUpdateQueue), typeof(float) }, t, nameof(BotBodyPrefix));
            PatchRegistry.Prefix(PatchIds.StateMove, "World freeze", typeof(MovementState), nameof(MovementState.OnStateMove), new[] { typeof(float) }, t, nameof(StateMovePrefix));
            PatchRegistry.Prefix(PatchIds.PhysicsUpdate, "World freeze", typeof(PhysicsExtensions.Simulation), nameof(PhysicsExtensions.Simulation.Update), System.Type.EmptyTypes, t, nameof(PhysicsUpdatePrefix));
            PatchRegistry.Prefix(PatchIds.BotsAddGuard, "Bot list safety", typeof(BotsList), nameof(BotsList.AddFromList), System.Type.EmptyTypes, t, nameof(BotsAddGuardPrefix));
            PatchRegistry.Prefix(PatchIds.BotSay, "Freeze bots only", typeof(Player), nameof(Player.Say), new[] { typeof(EPhraseTrigger), typeof(bool), typeof(float), typeof(ETagStatus), typeof(int), typeof(bool) }, t, nameof(BotSayPrefix));
            PatchRegistry.Prefix(PatchIds.EndByTimer, "Raid timer pause", typeof(EndByTimerScenario), "Update", System.Type.EmptyTypes, t, nameof(EndByTimerPrefix));

            var health = typeof(ActiveHealthController);
            PatchRegistry.Prefix(PatchIds.ApplyDamage, "Damage protection", health, nameof(ActiveHealthController.ApplyDamage), new[] { typeof(EBodyPart), typeof(float), typeof(DamageInfo) }, t, nameof(ReturnZeroPrefix));
            PatchRegistry.Prefix(PatchIds.Kill, "Damage protection", health, nameof(ActiveHealthController.Kill), new[] { typeof(EDamageType) }, t, nameof(SkipWhenBlockedPrefix));
            PatchRegistry.Prefix(PatchIds.ChangeHealth, "Damage protection", health, nameof(ActiveHealthController.ChangeHealth), new[] { typeof(EBodyPart), typeof(float), typeof(DamageInfo) }, t, nameof(NegativeValuePrefix));
            PatchRegistry.Prefix(PatchIds.ChangeEnergy, "Damage protection", health, nameof(ActiveHealthController.ChangeEnergy), new[] { typeof(float) }, t, nameof(NegativeValuePrefix));
            PatchRegistry.Prefix(PatchIds.ChangeHydration, "Damage protection", health, nameof(ActiveHealthController.ChangeHydration), new[] { typeof(float) }, t, nameof(NegativeValuePrefix));
            PatchRegistry.Prefix(PatchIds.DoBleed, "Damage protection", health, nameof(ActiveHealthController.DoBleed), new[] { typeof(bool), typeof(EBodyPart) }, t, nameof(SkipWhenBlockedPrefix));
            PatchRegistry.Prefix(PatchIds.DoFracture, "Damage protection", health, nameof(ActiveHealthController.DoFracture), new[] { typeof(EBodyPart) }, t, nameof(SkipWhenBlockedPrefix));
            PatchRegistry.Prefix(PatchIds.HandleFall, "Damage protection", health, nameof(ActiveHealthController.HandleFall), new[] { typeof(float) }, t, nameof(ReturnZeroPrefix));
            PatchRegistry.Prefix(PatchIds.ApplyShot, "Damage protection", typeof(Player), nameof(Player.ApplyShot), new[] { typeof(DamageInfo), typeof(EBodyPart), typeof(EBodyPartColliderType), typeof(EArmorPlateCollider), typeof(ShotId) }, t, nameof(ApplyShotPrefix));

            PatchRegistry.Prefix(PatchIds.Input, "Input lockout", typeof(InputManager), nameof(InputManager.DispatchInput), new[] { typeof(List<ECommand>), typeof(float[]) }, t, nameof(DispatchInputPrefix));
            PatchRegistry.Prefix(PatchIds.CullingUpdate, "BSG culling toggle", typeof(CullingManager), nameof(CullingManager.Update), System.Type.EmptyTypes, t, nameof(CullingManagerPrefix));
            PatchRegistry.Prefix(PatchIds.CullingRender, "BSG culling toggle", typeof(CullingManager), nameof(CullingManager.CameraRender), new[] { typeof(UnityEngine.Camera) }, t, nameof(CullingManagerPrefix));
            PatchRegistry.Prefix(PatchIds.CameraSnap, "Camera ownership", typeof(CameraManager), nameof(CameraManager.ForceSetPosition), new[] { typeof(UnityEngine.Vector3) }, t, nameof(ForceSetPositionPrefix));
        }

        public static bool WorldTickPrefix() => !FreezeFlags.WorldTickFrozen;

        public static bool BotsPrefix() => !FreezeFlags.AnyBotFreeze;

        public static bool BotBodyPrefix(Player __instance) => !(FreezeFlags.BotStatues && __instance.IsAI);

        public static void BotsAddGuardPrefix(BotsList __instance)
        {
            List<BotOwner> queue = __instance._botsToAdd;
            if (queue == null || queue.Count == 0)
            {
                return;
            }
            int removed = queue.RemoveAll(b => b == null || b.GetPlayer == null || b.IsDead || __instance._botOwners.Contains(b));
            if (removed > 0)
            {
                PhotoLog.Warn($"Dropped {removed} dead or duplicate bot(s) from the game's add queue.");
            }
        }

        public static bool PhysicsUpdatePrefix()
        {
            return !FreezeFlags.WorldTickFrozen;
        }

        private static readonly AccessTools.FieldRef<MovementContext, Player> ContextPlayer = AccessToolsFieldRef();

        private static AccessTools.FieldRef<MovementContext, Player> AccessToolsFieldRef()
        {
            try
            {
                return AccessTools.FieldRefAccess<MovementContext, Player>("_player");
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        public static bool StateMovePrefix(MovementState __instance)
        {
            if (FreezeFlags.WorldTickFrozen)
            {
                return false;
            }
            if (!FreezeFlags.BotStatues)
            {
                return true;
            }
            Player player = null;
            try
            {
                MovementContext context = __instance.MovementContext;
                player = context != null && ContextPlayer != null ? ContextPlayer(context) : null;
            }
            catch (System.Exception)
            {
            }
            return !(player != null && player.IsAI);
        }

        public static bool BotSayPrefix(Player __instance) => !(FreezeFlags.AnyBotFreeze && __instance.IsAI);

        public static bool EndByTimerPrefix() => !FreezeFlags.RaidTimerFrozen;

        private static bool Protected(Player player) =>
            FreezeFlags.DamageBlocked
            || (FreezeFlags.BotStatues && (DuckCamConfig.ProtectFrozenBots?.Value ?? true) && player != null && player.IsAI);

        public static bool SkipWhenBlockedPrefix(ActiveHealthController __instance) => !Protected(__instance.Player);

        public static bool ReturnZeroPrefix(ActiveHealthController __instance, ref float __result)
        {
            if (!Protected(__instance.Player))
            {
                return true;
            }
            __result = 0f;
            return false;
        }

        public static bool NegativeValuePrefix(ActiveHealthController __instance, float value) => !(value < 0f && Protected(__instance.Player));

        public static bool ApplyShotPrefix(Player __instance, ref PlayerHitInfo __result)
        {
            if (!Protected(__instance))
            {
                return true;
            }
            __result = null;
            return false;
        }

        public static void DispatchInputPrefix(List<ECommand> commandsList, float[] axesList)
        {
            if (FreezeFlags.InputBlocked)
            {
                commandsList?.Clear();
                if (axesList != null)
                {
                    System.Array.Clear(axesList, 0, axesList.Length);
                }
                return;
            }

            if (FreezeFlags.PlayerCommandsBlocked)
            {
                commandsList?.Clear();
            }
            if (FreezeFlags.PlayerLookBlocked && axesList != null)
            {
                ZeroAxis(axesList, EAxis.TurnX);
                ZeroAxis(axesList, EAxis.TurnY);
                ZeroAxis(axesList, EAxis.LookX);
                ZeroAxis(axesList, EAxis.LookY);
            }
            if (FreezeFlags.PlayerMoveBlocked && axesList != null)
            {
                ZeroAxis(axesList, EAxis.MoveX);
                ZeroAxis(axesList, EAxis.MoveY);
                ZeroAxis(axesList, EAxis.LeanX);
            }
        }

        private static void ZeroAxis(float[] axes, EAxis axis)
        {
            int i = (int)axis;
            if (i >= 0 && i < axes.Length)
            {
                axes[i] = 0f;
            }
        }

        public static bool CullingManagerPrefix() => !FreezeFlags.BsgCullingDisabled;

        public static bool ForceSetPositionPrefix() => !FreezeFlags.CameraOwned;
    }
}
