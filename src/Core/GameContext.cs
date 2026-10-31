using System.Collections.Generic;
using Comfort.Common;
using EFT;

namespace DuckCam.Core
{
    internal static class GameContext
    {
        private static readonly Dictionary<string, string> _mapNames = new Dictionary<string, string>
        {
            { "bigmap", "Customs" },
            { "woods", "Woods" },
            { "interchange", "Interchange" },
            { "rezervbase", "Reserve" },
            { "tarkovstreets", "Streets" },
            { "sandbox", "GroundZero" },
            { "sandbox_high", "GroundZero" },
            { "shoreline", "Shoreline" },
            { "factory4_day", "Factory" },
            { "factory4_night", "Factory" },
            { "laboratory", "Labs" },
            { "lighthouse", "Lighthouse" },
            { "labyrinth", "Labyrinth" },
        };

        public static GameWorld World => Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;

        public static Player MainPlayer => World?.MainPlayer;

        public static AbstractGame Game => Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance : null;

        public static bool RaidRunning
        {
            get
            {
                Player player = MainPlayer;
                AbstractGame game = Game;
                return player != null && game != null && game.Status == GameStatus.Started
                       && player.HealthController != null && player.HealthController.IsAlive;
            }
        }

        public static string LocationId => World?.LocationId ?? MainPlayer?.Location ?? "unknown";

        public static string MapDisplayName
        {
            get
            {
                string id = LocationId ?? "unknown";
                return _mapNames.TryGetValue(id.ToLowerInvariant(), out string name) ? name : id;
            }
        }
    }
}
