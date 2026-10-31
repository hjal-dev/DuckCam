using System;
using System.Collections.Generic;
using EFT;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Actors
{
    internal sealed class WeaponHider
    {
        private readonly Dictionary<Renderer, bool> _hidden = new Dictionary<Renderer, bool>();
        private RestoreHandle _handle;
        private float _nextSweep;

        public bool HidePmc { get; set; }
        public bool HideBots { get; set; }
        public Func<GameObject> PmcCopyWeapon { get; set; }
        public int HiddenCount => _hidden.Count;

        public void Tick(Player mainPlayer, RestoreStack stack)
        {
            if (!HidePmc && !HideBots)
            {
                if (_hidden.Count > 0)
                {
                    RestoreAll();
                }
                return;
            }
            if (_handle == null || _handle.Done)
            {
                _handle = stack.Push("hide weapons in hands", RestoreAll);
            }
            if (Time.unscaledTime < _nextSweep)
            {
                return;
            }
            _nextSweep = Time.unscaledTime + 0.5f;
            Sweep(mainPlayer);
        }

        private void Sweep(Player mainPlayer)
        {
            var want = new HashSet<Renderer>();
            GameWorld world = GameContext.World;
            if (world != null)
            {
                foreach (Player p in world.AllAlivePlayersList)
                {
                    if (p == null)
                    {
                        continue;
                    }
                    bool isPmc = p == mainPlayer || p.IsYourPlayer;
                    if (isPmc ? !HidePmc : !HideBots)
                    {
                        continue;
                    }
                    GameObject held = null;
                    try
                    {
                        held = p.HandsController?.ControllerGameObject;
                    }
                    catch (Exception ex)
                    {
                        PhotoLog.WarnOnce("hideweapon", "Weapon hider: hands controller not readable: " + ex.Message);
                    }
                    if (held == null)
                    {
                        continue;
                    }
                    foreach (Renderer r in held.GetComponentsInChildren<Renderer>(true))
                    {
                        want.Add(r);
                    }
                }
            }
            if (HidePmc)
            {
                GameObject copy = null;
                try
                {
                    copy = PmcCopyWeapon?.Invoke();
                }
                catch (Exception ex)
                {
                    PhotoLog.WarnOnce("hideweapon-copy", "Weapon hider: PMC copy weapon not readable: " + ex.Message);
                }
                if (copy != null)
                {
                    foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
                    {
                        want.Add(r);
                    }
                }
            }
            var back = new List<Renderer>();
            foreach (KeyValuePair<Renderer, bool> kv in _hidden)
            {
                if (kv.Key == null || !want.Contains(kv.Key))
                {
                    back.Add(kv.Key);
                }
            }
            foreach (Renderer r in back)
            {
                if (r != null)
                {
                    r.forceRenderingOff = _hidden[r];
                }
                _hidden.Remove(r);
            }
            foreach (Renderer r in want)
            {
                if (!_hidden.ContainsKey(r))
                {
                    _hidden[r] = r.forceRenderingOff;
                    r.forceRenderingOff = true;
                }
            }
        }

        public void RestoreAll()
        {
            foreach (KeyValuePair<Renderer, bool> kv in _hidden)
            {
                if (kv.Key != null)
                {
                    kv.Key.forceRenderingOff = kv.Value;
                }
            }
            _hidden.Clear();
            _handle?.Dismiss();
            _handle = null;
        }

        public void ResetSession()
        {
            _hidden.Clear();
            _handle = null;
            HidePmc = false;
            HideBots = false;
        }
    }
}
