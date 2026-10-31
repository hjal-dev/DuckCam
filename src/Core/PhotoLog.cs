using System.Collections.Generic;
using BepInEx.Logging;

namespace DuckCam.Core
{
    public enum PhotoLogLevel
    {
        Normal,
        Verbose,
        Debug
    }

    internal static class PhotoLog
    {
        private static ManualLogSource _source;
        private static readonly HashSet<string> _onceKeys = new HashSet<string>();

        public static PhotoLogLevel Level { get; set; } = PhotoLogLevel.Normal;

        public static void Init(ManualLogSource source)
        {
            _source = source;
        }

        public static void Info(string message) => _source?.LogInfo(message);

        public static void Warn(string message) => _source?.LogWarning(message);

        public static void Error(string message) => _source?.LogError(message);

        public static void Verbose(string message)
        {
            if (Level >= PhotoLogLevel.Verbose)
            {
                _source?.LogInfo("[v] " + message);
            }
        }

        public static void Debug(string message)
        {
            if (Level >= PhotoLogLevel.Debug)
            {
                _source?.LogDebug("[d] " + message);
            }
        }

        public static void WarnOnce(string key, string message)
        {
            if (_onceKeys.Add(key))
            {
                Warn(message);
            }
        }
    }
}
