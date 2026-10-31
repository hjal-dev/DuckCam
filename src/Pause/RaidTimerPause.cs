using System;
using System.Linq;
using EFT;
using HarmonyLib;
using DuckCam.Core;

namespace DuckCam.Pause
{
    internal sealed class RaidTimerPause
    {
        private GameTimer _timer;
        private DateTime? _lastUtc;
        private TimeSpan _total;

        public TimeSpan? LastShift { get; private set; }

        public string Status { get; private set; } = "not paused";

        public bool Begin()
        {
            _timer = GameContext.Game?.GameTimer;
            if (_timer == null)
            {
                Status = "no GameTimer, raid clock NOT paused";
                PhotoLog.Warn("Raid timer pause unavailable: AbstractGame.GameTimer not found.");
                return false;
            }

            _lastUtc = DateTime.UtcNow;
            FindPanels();
            _total = TimeSpan.Zero;
            Status = "held at PastTime " + _timer.PastTime;
            return true;
        }

        public void Tick()
        {
            if (_timer == null || !_lastUtc.HasValue)
            {
                return;
            }
            DateTime now = DateTime.UtcNow;
            TimeSpan step = now - _lastUtc.Value;
            _lastUtc = now;
            if (step > TimeSpan.Zero && Shift(_timer, step))
            {
                _total += step;
                ShiftUi(step);
            }
        }

        private static readonly AccessTools.FieldRef<EFT.UI.BattleTimer.TimerPanel, DateTime> PanelEnd = FieldRef<EFT.UI.BattleTimer.TimerPanel>("_dateTime");
        private static readonly AccessTools.FieldRef<EFT.UI.ExtractionTimersPanel, DateTime> PanelStart = FieldRef<EFT.UI.ExtractionTimersPanel>("_startTime");
        private EFT.UI.BattleTimer.TimerPanel[] _panels = Array.Empty<EFT.UI.BattleTimer.TimerPanel>();
        private EFT.UI.ExtractionTimersPanel[] _extraction = Array.Empty<EFT.UI.ExtractionTimersPanel>();
        private void FindPanels()
        {
            try
            {
                _panels = UnityEngine.Resources.FindObjectsOfTypeAll<EFT.UI.BattleTimer.TimerPanel>().Where(p => p != null && p.gameObject.scene.IsValid()).ToArray();
                _extraction = UnityEngine.Resources.FindObjectsOfTypeAll<EFT.UI.ExtractionTimersPanel>().Where(p => p != null && p.gameObject.scene.IsValid()).ToArray();
            }
            catch (Exception ex)
            {
                PhotoLog.WarnOnce("raidtimerscan", "Raid timer panels not found: " + ex.Message);
            }
        }

        private static AccessTools.FieldRef<T, DateTime> FieldRef<T>(string name)
        {
            try
            {
                return AccessTools.FieldRefAccess<T, DateTime>(name);
            }
            catch (Exception ex)
            {
                PhotoLog.Warn($"Raid timer UI hold unavailable ({typeof(T).Name}.{name}): {ex.Message}");
                return null;
            }
        }

        private void ShiftUi(TimeSpan step)
        {
            try
            {
                if (PanelEnd != null)
                {
                    foreach (EFT.UI.BattleTimer.TimerPanel panel in _panels)
                    {
                        if (panel != null && PanelEnd(panel) != default)
                        {
                            PanelEnd(panel) = PanelEnd(panel) + step;
                        }
                    }
                }
                if (PanelStart != null)
                {
                    foreach (EFT.UI.ExtractionTimersPanel panel in _extraction)
                    {
                        if (panel != null && PanelStart(panel) != default)
                        {
                            PanelStart(panel) = PanelStart(panel) + step;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PhotoLog.WarnOnce("raidtimerui", "Raid timer UI hold failed: " + ex.Message);
            }
        }

        public void End()
        {
            if (_timer == null || !_lastUtc.HasValue)
            {
                return;
            }
            Tick();
            _lastUtc = null;
            LastShift = _total;
            Status = _total > TimeSpan.Zero
                ? $"held for {_total.TotalSeconds:F1}s, PastTime now {_timer.PastTime}"
                : "not shifted (timer stopped or zero pause)";
            PhotoLog.Info("Raid clock " + Status);
        }

        public static bool Shift(GameTimer timer, TimeSpan amount)
        {
            if (timer == null || timer.Status == GameTimer.EGameTimerStatus.Stopped || !timer._startDateTime.HasValue)
            {
                return false;
            }
            timer._startDateTime = timer._startDateTime.Value + amount;
            if (timer._escapeDateTime.HasValue)
            {
                timer._escapeDateTime = timer._escapeDateTime.Value + amount;
            }
            return true;
        }
    }
}
