using System;
using System.Collections.Generic;
using System.Threading;
using DuckCam.Core;
using EFT;
using EFT.UI;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed class WardrobeStage
    {
        private GameObject _root;
        private PlayerModelLoader _loader;
        private CancellationTokenSource _cts;
        private int _generation;

        public static int Layer => LayersMaskController.WeaponPreview;

        public Transform Model => _loader?.ModelPlayerPoser != null ? _loader.ModelPlayerPoser.transform : null;

        public bool Ready => Model != null && Model.gameObject.activeInHierarchy;

        public bool Loading { get; private set; }

        public string Error { get; private set; }

        public async void Show(PlayerVisualRepresentation visual, Vector3 position)
        {
            if (visual == null)
            {
                return;
            }
            int generation = ++_generation;
            if (_root == null)
            {
                _root = new GameObject("DuckCam_WardrobeStage");
                UnityEngine.Object.DontDestroyOnLoad(_root);
            }
            _root.transform.SetPositionAndRotation(position, Quaternion.identity);
            _cts?.Cancel();
            var cts = new CancellationTokenSource();
            _cts = cts;
            var loader = new PlayerModelLoader();
            Loading = true;
            Error = null;
            try
            {
                await loader.Load(visual, null, 0f, ThirdPersonCustomizationFilter.Default, null, cts.Token, Vector3.zero, _root.transform, Layer, true);
            }
            catch (Exception ex)
            {
                SafeClear(loader);
                if (generation == _generation)
                {
                    Loading = false;
                    Error = ex.Message;
                    PhotoLog.Warn("Wardrobe preview model failed: " + ex.Message);
                }
                return;
            }
            if (generation != _generation || _root == null || loader.ModelPlayerPoser == null)
            {
                SafeClear(loader);
                return;
            }
            foreach (Animator animator in loader.ModelPlayerPoser.GetComponentsInChildren<Animator>(true))
            {
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            }
            Sharpen(loader.ModelPlayerPoser.gameObject);
            SafeClear(_loader);
            _loader = loader;
            _nextRandom = Time.unscaledTime + 4f;
            Loading = false;
        }

        private HashSet<Texture2D> _sharp = new HashSet<Texture2D>();

        private void Sharpen(GameObject model)
        {
            var next = new HashSet<Texture2D>();
            try
            {
                foreach (LODGroup group in model.GetComponentsInChildren<LODGroup>(true))
                {
                    group.ForceLOD(0);
                }
                var ids = new List<int>();
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null)
                        {
                            continue;
                        }
                        ids.Clear();
                        material.GetTexturePropertyNameIDs(ids);
                        foreach (int id in ids)
                        {
                            if (material.GetTexture(id) is Texture2D tex && tex.streamingMipmaps && next.Add(tex))
                            {
                                tex.requestedMipmapLevel = 0;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PhotoLog.WarnOnce("stagesharp", "Wardrobe preview detail: " + ex.Message);
            }
            ReleaseSharp(next);
            _sharp = next;
        }

        private void ReleaseSharp(HashSet<Texture2D> keep)
        {
            foreach (Texture2D tex in _sharp)
            {
                if (tex != null && (keep == null || !keep.Contains(tex)))
                {
                    tex.ClearRequestedMipmapLevel();
                }
            }
        }

        public void Clear()
        {
            _generation++;
            ReleaseSharp(null);
            _sharp.Clear();
            _cts?.Cancel();
            _cts = null;
            SafeClear(_loader);
            _loader = null;
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
            }
            _root = null;
            Loading = false;
            Error = null;
        }

        private float _nextRandom;

        public void Tick(float dt, bool frozen)
        {
            MenuPlayerPoser poser = _loader?.ModelPlayerPoser;
            if (poser == null || poser.PlayerAnimatorController == null || poser.PBones == null)
            {
                return;
            }
            try
            {
                if (Time.unscaledTime >= _nextRandom)
                {
                    _nextRandom = Time.unscaledTime + UnityEngine.Random.Range(6f, 11f);
                    poser.OnRandomizeAnimation();
                }
                if (!frozen)
                {
                    return;
                }
                float weight = poser.PlayerAnimatorController.GetFloat(PlayerAnimator.WEAPON_ROOT_3RD);
                if (weight > 0.01f)
                {
                    poser.PBones.ShiftWeaponRoot(dt, EPointOfView.ThirdPerson, weight);
                }
            }
            catch (Exception ex)
            {
                PhotoLog.WarnOnce("stageweapon", "Wardrobe preview weapon update: " + ex.Message);
            }
        }

        private static void SafeClear(PlayerModelLoader loader)
        {
            if (loader == null)
            {
                return;
            }
            try
            {
                if (loader._weaponPrefab != null)
                {
                    GameObject weapon = loader._weaponPrefab.gameObject;
                    loader._weaponPrefab = null;
                    UnityEngine.Object.Destroy(weapon);
                }
                loader.Clear();
            }
            catch (Exception ex)
            {
                PhotoLog.Verbose("Wardrobe preview cleanup: " + ex.Message);
            }
        }
    }
}
