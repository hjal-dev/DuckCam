using System;
using System.Collections.Generic;
using EFT;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Pause
{
    internal sealed class BotFreezer
    {
        private readonly Dictionary<Animator, float> _animators = new Dictionary<Animator, float>();
        private readonly HashSet<int> _players = new HashSet<int>();
        private readonly PoseHold _hold = new PoseHold(pinRoot: true);
        private float _nextSweep;

        public bool Active => FreezeFlags.BotStatues;
        public int Count => _players.Count;

        public void Toggle()
        {
            if (Active)
            {
                Release("user");
            }
            else
            {
                Freeze();
            }
        }

        public void Freeze()
        {
            if (Active)
            {
                return;
            }
            GameWorld world = GameContext.World;
            if (world == null)
            {
                return;
            }
            FreezeFlags.BotStatues = true;
            Sweep(world);
            PhotoLog.Info($"Bots frozen ({Count}).");
        }

        public void Tick()
        {
            if (!Active || Time.unscaledTime < _nextSweep)
            {
                return;
            }
            _nextSweep = Time.unscaledTime + 0.5f;
            GameWorld world = GameContext.World;
            if (world == null)
            {
                Release("no GameWorld");
                return;
            }
            Sweep(world);
        }

        private void Sweep(GameWorld world)
        {
            List<Player> players = world.AllAlivePlayersList;
            if (players == null)
            {
                return;
            }
            foreach (Player p in players)
            {
                if (p == null || !p.IsAI || !_players.Add(p.GetInstanceID()))
                {
                    continue;
                }
                foreach (Animator a in p.GetComponentsInChildren<Animator>(true))
                {
                    if (a != null && !_animators.ContainsKey(a))
                    {
                        _animators[a] = a.speed;
                        a.speed = 0f;
                    }
                }
                try
                {
                    p.Speaker?.Shut();
                }
                catch (Exception ex)
                {
                    PhotoLog.Debug("Speaker shut failed: " + ex.Message);
                }
                _hold.Add(p);
            }
        }

        public void Release(string reason)
        {
            if (!Active && _animators.Count == 0)
            {
                return;
            }
            FreezeFlags.BotStatues = false;
            _hold.Clear();
            foreach (KeyValuePair<Animator, float> kv in _animators)
            {
                if (kv.Key != null)
                {
                    kv.Key.speed = kv.Value;
                }
            }
            int count = _players.Count;
            _animators.Clear();
            _players.Clear();
            PhotoLog.Info($"Bots unfrozen ({count}, {reason}).");
        }
    }
}
