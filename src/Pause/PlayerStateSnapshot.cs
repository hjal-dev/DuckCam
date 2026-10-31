using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.InventoryLogic;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Pause
{
    internal sealed class PlayerStateSnapshot
    {
        private static readonly EBodyPart[] _parts =
        {
            EBodyPart.Head, EBodyPart.Chest, EBodyPart.Stomach, EBodyPart.LeftArm,
            EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg
        };

        private Vector3 _position;
        private float _yaw;
        private readonly Dictionary<EBodyPart, float> _health = new Dictionary<EBodyPart, float>();
        private float _energy;
        private float _hydration;
        private float _stamina;
        private int _itemCount;
        private double _pastSeconds;

        public static PlayerStateSnapshot Capture(Player player)
        {
            var s = new PlayerStateSnapshot();
            s._position = player.Transform.position;
            s._yaw = player.Yaw;
            foreach (EBodyPart part in _parts)
            {
                s._health[part] = player.HealthController.GetBodyPartHealth(part).Current;
            }
            s._energy = player.HealthController.Energy.Current;
            s._hydration = player.HealthController.Hydration.Current;
            s._stamina = player.Physical?.Stamina?.Current ?? -1f;
            s._itemCount = CountItems(player);
            s._pastSeconds = GameContext.Game?.GameTimer?.PastTime.TotalSeconds ?? -1;
            return s;
        }

        public List<string> Diff(Player player, double maxClockDriftSeconds = 2.0)
        {
            var diffs = new List<string>();
            if (player == null)
            {
                diffs.Add("player no longer exists");
                return diffs;
            }

            float moved = Vector3.Distance(_position, player.Transform.position);
            if (moved > 0.05f)
            {
                diffs.Add($"position moved {moved:F3} m");
            }
            if (Mathf.Abs(Mathf.DeltaAngle(_yaw, player.Yaw)) > 1f)
            {
                diffs.Add($"yaw changed {_yaw:F1} -> {player.Yaw:F1}");
            }
            foreach (KeyValuePair<EBodyPart, float> kv in _health)
            {
                float now = player.HealthController.GetBodyPartHealth(kv.Key).Current;
                if (Mathf.Abs(now - kv.Value) > 0.01f)
                {
                    diffs.Add($"health {kv.Key} {kv.Value:F1} -> {now:F1}");
                }
            }
            Compare(diffs, "energy", _energy, player.HealthController.Energy.Current);
            Compare(diffs, "hydration", _hydration, player.HealthController.Hydration.Current);
            if (_stamina >= 0f)
            {
                Compare(diffs, "stamina", _stamina, player.Physical?.Stamina?.Current ?? -1f);
            }
            int items = CountItems(player);
            if (items != _itemCount)
            {
                diffs.Add($"equipment item count {_itemCount} -> {items}");
            }
            double past = GameContext.Game?.GameTimer?.PastTime.TotalSeconds ?? -1;
            if (_pastSeconds >= 0 && past >= 0 && Math.Abs(past - _pastSeconds) > maxClockDriftSeconds)
            {
                diffs.Add($"raid clock advanced {past - _pastSeconds:F1}s across the pause");
            }
            return diffs;
        }

        private static void Compare(List<string> diffs, string name, float before, float after)
        {
            if (Mathf.Abs(after - before) > 0.01f)
            {
                diffs.Add($"{name} {before:F2} -> {after:F2}");
            }
        }

        private static int CountItems(Player player)
        {
            try
            {
                return player.Profile?.Inventory?.Equipment?.GetAllItems().Count() ?? -1;
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }
}
