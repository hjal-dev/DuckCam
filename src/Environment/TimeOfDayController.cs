using System;
using Comfort.Common;
using EFT;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Environment
{
    internal sealed class TimeOfDayController
    {
        private RestoreHandle _overrideHandle;
        private DateTime _naturalStart;
        private double _liveSecondsSinceOverride;

        public bool Available => Clock != null;
        public bool Overridden => _overrideHandle != null && !_overrideHandle.Done;
        public string Status { get; private set; } = "untouched";

        private static GameDateTime Clock
        {
            get
            {
                GameWorld world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
                return world != null ? world.GameDateTime : null;
            }
        }

        private static GameDateTime SkyClock => TODSkyProvider.IsAvailable ? TODSkyProvider.Instance.CurrentTime?.GameDateTime : null;

        public DateTime Now => Clock?.Calculate() ?? DateTime.MinValue;

        public float Hour
        {
            get
            {
                DateTime t = Now;
                return t == DateTime.MinValue ? 0f : (float)t.TimeOfDay.TotalHours;
            }
        }

        public void Hold(RestoreStack freezeStack)
        {
            GameDateTime clock = Clock;
            if (clock == null)
            {
                return;
            }
            GameDateTime sky = SkyClock;
            float clockMod = clock.TimeFactorMod;
            float skyMod = sky != null ? sky.TimeFactorMod : 1f;

            clock.ResetForce(clock.Calculate());
            clock.TimeFactorMod = 0f;
            if (sky != null && !ReferenceEquals(sky, clock))
            {
                sky.ResetForce(sky.Calculate());
                sky.TimeFactorMod = 0f;
            }

            freezeStack.Push("time of day hold", () =>
            {
                if (clock != null)
                {
                    clock.ResetForce(clock.Calculate());
                    clock.TimeFactorMod = clockMod;
                }
                if (sky != null && !ReferenceEquals(sky, clock))
                {
                    sky.ResetForce(sky.Calculate());
                    sky.TimeFactorMod = skyMod;
                }
            });
        }

        public void Tick(bool frozen)
        {
            if (Overridden && !frozen)
            {
                _liveSecondsSinceOverride += Time.unscaledDeltaTime;
            }
        }

        public void SetHour(float hour, RestoreStack sessionStack)
        {
            GameDateTime clock = Clock;
            if (clock == null)
            {
                Status = "no game clock on this map";
                return;
            }
            hour = Mathf.Repeat(hour, 24f);
            DateTime now = clock.Calculate();

            if (!Overridden)
            {
                _naturalStart = now;
                _liveSecondsSinceOverride = 0;
                GameDateTime restoreClock = clock;
                _overrideHandle = sessionStack.Push("time of day override", () =>
                {
                    if (restoreClock == null)
                    {
                        return;
                    }
                    double factor = restoreClock.TimeFactor;
                    DateTime natural = _naturalStart.AddSeconds(_liveSecondsSinceOverride * factor);
                    Apply(restoreClock, natural);
                    Status = "restored to " + natural.ToString("HH:mm");
                });
            }

            DateTime target = now.Date.AddHours(hour);
            Apply(clock, target);
            Status = "set to " + target.ToString("HH:mm");
        }

        public void KeepAfterExit()
        {
            if (Overridden)
            {
                _overrideHandle.Dismiss();
                _overrideHandle = null;
                Status = "kept after exit";
            }
        }

        public void Revert()
        {
            if (Overridden)
            {
                _overrideHandle.Run();
            }
            _overrideHandle = null;
            _liveSecondsSinceOverride = 0;
        }

        public void ResetSession()
        {
            _overrideHandle = null;
            _liveSecondsSinceOverride = 0;
            Status = "untouched";
        }

        private static void Apply(GameDateTime clock, DateTime time)
        {
            clock.ResetForce(time);
            GameDateTime sky = SkyClock;
            if (sky != null && !ReferenceEquals(sky, clock))
            {
                sky.ResetForce(time);
            }
            try
            {
                if (TODSkyProvider.IsAvailable)
                {
                    TODSkyProvider.Instance.CurrentTime.AddHours(0f, false);
                }
            }
            catch (Exception ex)
            {
                PhotoLog.WarnOnce("tod-refresh", "Sky refresh after time change failed: " + ex.Message);
            }
        }
    }
}
