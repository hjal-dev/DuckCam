using System.Globalization;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed partial class DuckCamUI
    {
        private void DrawTime()
        {
            _kit.Value("Scene", _c.Frozen ? "Frozen" : "Live");
            if (_kit.Action(_c.Frozen ? "Resume Raid" : "Freeze Raid", _c.Frozen ? "Unfreeze" : "Freeze"))
            {
                _c.SetFrozen(!_c.Frozen);
            }
            if (_c.Frozen)
            {
                _c.FrameStepCount = _kit.IntSlider("Frame Step Count", _c.FrameStepCount, 1, 120, " frames");
                string stepKey = DuckCamConfig.FrameStepKey.Value.MainKey == KeyCode.None ? "Step" : KeyName(DuckCamConfig.FrameStepKey);
                if (_kit.Action("Step Frames", _c.Stepping ? "Stepping" : stepKey))
                {
                    _c.StepFrames(_c.FrameStepCount);
                }
            }
            string botKey = DuckCamConfig.BotFreezeKey.Value.MainKey == KeyCode.None ? "" : " (" + KeyName(DuckCamConfig.BotFreezeKey) + ")";
            if (_kit.Action("Freeze Bots Only" + botKey, _c.BotFreeze.Active ? $"Release {_c.BotFreeze.Count}" : "Freeze"))
            {
                _c.ToggleBotFreeze();
            }
            DuckCamConfig.ProtectFrozenBots.Value = _kit.Toggle("Frozen Bots Can't Be Hurt", DuckCamConfig.ProtectFrozenBots.Value);
            int pct = _kit.IntSlider("Game Speed", DuckCamConfig.GameSpeedPercent.Value, 1, 100, "%");
            if (pct != DuckCamConfig.GameSpeedPercent.Value)
            {
                DuckCamConfig.GameSpeedPercent.Value = pct;
                if (_c.GameSpeedOn)
                {
                    _c.SetGameSpeedEnabled(true);
                }
            }
            bool slow = _kit.Toggle("Slow Motion", _c.GameSpeedOn);
            if (slow != _c.GameSpeedOn)
            {
                _c.SetGameSpeedEnabled(slow);
            }
            DuckCamConfig.RaidClockFollowsGameSpeed.Value = _kit.Toggle("Raid Clock Follows Speed", DuckCamConfig.RaidClockFollowsGameSpeed.Value);
            _kit.Value("Raid Time Saved", _c.Dilation.ClockCompensation.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s");
            if (!_c.Frozen)
            {
                _c.CameraHasMouse = _kit.Toggle("Mouse Controls Camera", _c.CameraHasMouse);
            }
            DuckCamConfig.BlockPlayerCommandsWithMouse.Value = _kit.Toggle("Block PMC Keys", DuckCamConfig.BlockPlayerCommandsWithMouse.Value);
            _kit.Value("Live Damage Protection", DuckCamConfig.ProtectPlayerWhileUnsnapped.Value ? "On" : "Off");
            DrawResetRows();
            if (_kit.Action("Session", "End"))
            {
                _c.RequestExit("UI");
            }
            _kit.Note("Frame step lets players, physics and effects run for the chosen number of frames, then freezes again; bot AI, damage and the raid clock stay frozen. Freeze Bots Only stops every bot (pose, AI, hearing, sight) while you and the raid keep going, in or out of DuckCam; their AI isn't reset, and they don't remember anything that happened while frozen. Slow motion does not change audio pitch. Live damage protection is a config option and applies from the next session.");
        }
    }
}
