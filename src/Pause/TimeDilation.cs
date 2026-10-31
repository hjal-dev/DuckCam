using System;
using EFT;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Pause
{
    internal sealed class TimeDilation
    {
        private float _originalFixedDelta = -1f;
        private RestoreHandle _handle;

        public bool Active => _handle != null && !_handle.Done;
        public float Scale { get; private set; } = 1f;
        public TimeSpan ClockCompensation { get; private set; }

        public void Set(float scale, float baseScale, RestoreStack stack)
        {
            scale = Mathf.Clamp(scale, 0.01f, 1f);
            if (!Active)
            {
                if (Mathf.Approximately(scale, 1f))
                {
                    return;
                }
                float previousScale = baseScale;
                _originalFixedDelta = Time.fixedDeltaTime;
                float originalFixed = _originalFixedDelta;
                _handle = stack.Push("game speed", () =>
                {
                    Time.timeScale = previousScale;
                    Time.fixedDeltaTime = originalFixed;
                    Scale = 1f;
                });
            }

            Scale = scale;
            if (!FreezeFlags.WorldTickFrozen)
            {
                Time.timeScale = scale;
            }
            Time.fixedDeltaTime = _originalFixedDelta * scale;
        }

        public void Reapply()
        {
            if (Active && !FreezeFlags.WorldTickFrozen)
            {
                Time.timeScale = Scale;
            }
        }

        public void Clear()
        {
            _handle?.Run();
            _handle = null;
            Scale = 1f;
        }

        public void Tick()
        {
            if (!Active || FreezeFlags.WorldTickFrozen || !DuckCamConfig.RaidClockFollowsGameSpeed.Value)
            {
                return;
            }
            float lost = Time.unscaledDeltaTime - Time.deltaTime;
            if (lost <= 0f)
            {
                return;
            }
            GameTimer timer = GameContext.Game?.GameTimer;
            if (RaidTimerPause.Shift(timer, TimeSpan.FromSeconds(lost)))
            {
                ClockCompensation += TimeSpan.FromSeconds(lost);
            }
        }
    }
}
