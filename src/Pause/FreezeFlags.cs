namespace DuckCam.Pause
{
    internal static class FreezeFlags
    {
        public static int DamageBlocks;
        public static int InputBlocks;
        public static bool BotsFrozen;
        public static bool RaidTimerFrozen;
        public static bool WorldTickFrozen;
        public static bool CameraOwned;

        public static bool PlayerLookBlocked;

        public static bool PlayerCommandsBlocked;

        public static bool PlayerMoveBlocked;

        public static bool BsgCullingDisabled;

        public static bool BotStatues;

        public static bool DamageBlocked => DamageBlocks > 0;

        public static bool AnyBotFreeze => BotsFrozen || BotStatues;
        public static bool InputBlocked => InputBlocks > 0;

        public static void ClearAll()
        {
            DamageBlocks = 0;
            InputBlocks = 0;
            BotsFrozen = false;
            RaidTimerFrozen = false;
            WorldTickFrozen = false;
            CameraOwned = false;
            PlayerLookBlocked = false;
            PlayerCommandsBlocked = false;
            PlayerMoveBlocked = false;
            BsgCullingDisabled = false;
        }
    }
}
