using System;
using BepInEx;
using EFT;
using DuckCam.Core;
using DuckCam.Patches;

namespace DuckCam
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class DuckCamPlugin : BaseUnityPlugin
    {
        public const string Guid = "com.hj.duckcam";
        public const string Name = "DuckCam";

        public static string DataRoot => System.IO.Path.Combine(BepInEx.Paths.GameRootPath, "DuckCam");
        public const string Version = "1.0.0";

        private string _startupError;
        private bool _startupErrorShown;

        private void Awake()
        {
            PhotoLog.Init(Logger);
            try
            {
                DuckCamConfig.Bind(Config);
            }
            catch (Exception ex)
            {
                _startupError = ex.Message;
                Logger.LogError($"{Name} failed to start (config): {ex}");
                return;
            }
            PhotoLog.Level = DuckCamConfig.LogLevel.Value;
            DuckCamConfig.LogLevel.SettingChanged += (_, __) => PhotoLog.Level = DuckCamConfig.LogLevel.Value;
            Core.Locale.Load(DuckCamConfig.Language.Value);
            DuckCamConfig.Language.SettingChanged += (_, __) => Core.Locale.Load(DuckCamConfig.Language.Value);

            PatchRegistry.Init(Guid);
            try
            {
                FreezePatches.Bind();
            }
            catch (Exception ex)
            {
                PhotoLog.Error("Patch binding aborted: " + ex);
            }

            PhotoLog.Info($"{Name} {Version} loaded. Toggle: {DuckCamConfig.ToggleKey.Value}.");
        }

        private void Update()
        {
            if (_startupError != null)
            {
                if (!_startupErrorShown && GameContext.World != null)
                {
                    _startupErrorShown = true;
                    try
                    {
                        EFT.Communications.NotificationManager.DisplayMessageNotification($"{Name} failed to start: {_startupError} (see BepInEx/LogOutput.log)");
                    }
                    catch (Exception)
                    {
                    }
                }
                return;
            }
            if (DuckCamController.Instance != null)
            {
                return;
            }
            GameWorld world = GameContext.World;
            if (world != null && world.MainPlayer != null)
            {
                world.gameObject.AddComponent<DuckCamController>();
                PhotoLog.Verbose("DuckCam controller attached to GameWorld.");
            }
        }

        private void OnDestroy()
        {
            DuckCamController.Instance?.ForceExit("plugin destroyed");
            PatchRegistry.UnpatchAll();
        }
    }
}
