using System.Collections.Generic;
using System.Linq;
using EFT;
using DuckCam.Core;
using DuckCam.Patches;
using UnityEngine;

namespace DuckCam.Pause
{
    internal sealed class RaidFreezeController
    {
        private readonly RaidTimerPause _timer = new RaidTimerPause();

        public Dictionary<string, string> LayerStatus { get; } = new Dictionary<string, string>();

        public string TimerStatus => _timer.Status;

        public void TickTimer()
        {
            if (FreezeFlags.RaidTimerFrozen)
            {
                _timer.Tick();
            }
        }

        public void Reset()
        {
            LayerStatus.Clear();
        }

        public void EnableDamageBlock(RestoreStack stack, string label)
        {
            string[] missing = PatchIds.DamageLayer.Where(id => !PatchRegistry.IsBound(id)).ToArray();
            FreezeFlags.DamageBlocks++;
            stack.Push("damage protection (" + label + ")", () => FreezeFlags.DamageBlocks = Mathf.Max(0, FreezeFlags.DamageBlocks - 1));
            LayerStatus["Damage protection"] = missing.Length == 0
                ? "ON"
                : (missing.Length == PatchIds.DamageLayer.Length ? "UNAVAILABLE" : "PARTIAL (missing " + string.Join(", ", missing) + ")");
        }

        public void EnableInputBlock(RestoreStack stack)
        {
            if (PatchRegistry.IsBound(PatchIds.Input))
            {
                FreezeFlags.InputBlocks++;
                stack.Push("input lockout", () => FreezeFlags.InputBlocks = Mathf.Max(0, FreezeFlags.InputBlocks - 1));
                LayerStatus["Input lockout"] = "ON";
                return;
            }

            GamePlayerOwner.SetIgnoreInput(true);
            stack.Push("input lockout (fallback)", () => GamePlayerOwner.SetIgnoreInput(false));
            LayerStatus["Input lockout"] = "FALLBACK GamePlayerOwner.SetIgnoreInput";
        }

        public void FreezeBots(RestoreStack stack)
        {
            FreezeFlags.BotsFrozen = true;
            stack.Push("bot freeze", () => FreezeFlags.BotsFrozen = false);
            LayerStatus["Bot AI"] = PatchRegistry.IsBound(PatchIds.Bots) ? "FROZEN" : "UNAVAILABLE (bot bodies still frozen by world tick)";
        }

        public void PauseRaidTimer(RestoreStack stack)
        {
            bool began = _timer.Begin();
            FreezeFlags.RaidTimerFrozen = true;
            stack.Push("raid timer", () =>
            {
                FreezeFlags.RaidTimerFrozen = false;
                _timer.End();
            });
            LayerStatus["Raid timer"] = began
                ? (PatchRegistry.IsBound(PatchIds.EndByTimer) ? "PAUSED" : "SHIFT ONLY (end-by-timer not blocked)")
                : "N/A (no raid clock)";
        }

        public void FreezeWorld(RestoreStack stack)
        {
            bool tick = PatchRegistry.IsBound(PatchIds.WorldTick) && PatchRegistry.IsBound(PatchIds.OtherWorldTick);
            FreezeFlags.WorldTickFrozen = true;
            stack.Push("world tick", () => FreezeFlags.WorldTickFrozen = false);
            LayerStatus["World tick"] = tick ? "FROZEN" : "NOT FROZEN (patch missing; timeScale only)";

            float previousScale = Time.timeScale;
            Time.timeScale = 0f;
            stack.Push("time scale", () => Time.timeScale = previousScale);
            LayerStatus["Time scale"] = $"0 (was {previousScale:F2})";

            bool previousPhysics = PhysicsExtensions.Simulation.UpdateEnabled;
            PhysicsExtensions.Simulation.UpdateEnabled = false;
            stack.Push("physics simulation", () => PhysicsExtensions.Simulation.UpdateEnabled = previousPhysics);
            LayerStatus["Physics"] = "PAUSED";

            if (DuckCamConfig.PauseAudio.Value)
            {
                bool previousAudio = AudioListener.pause;
                AudioListener.pause = true;
                stack.Push("audio pause", () => AudioListener.pause = previousAudio);
                LayerStatus["Audio"] = "PAUSED";
            }
        }

        public void MarkUnfrozen()
        {
            foreach (string key in new[] { "Input lockout", "Bot AI", "Raid timer", "World tick", "Time scale", "Physics", "Audio" })
            {
                LayerStatus.Remove(key);
            }
            if (FreezeFlags.DamageBlocks == 0)
            {
                LayerStatus.Remove("Damage protection");
            }
        }
    }
}
