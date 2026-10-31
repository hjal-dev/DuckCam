using System;
using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Actors
{
    internal sealed class StanceSnapshot
    {
        public readonly Dictionary<string, Quaternion> Bones = new Dictionary<string, Quaternion>();
        public Vector3 HipsRootPosition;
        public Quaternion HipsRootRotation = Quaternion.identity;
        public bool HasHips;

        public bool HasWeapon;
        public Vector3 WeaponChestPosition;
        public Quaternion WeaponChestRotation = Quaternion.identity;
        public readonly Dictionary<string, (Vector3 position, Quaternion rotation)> WeaponParts = new Dictionary<string, (Vector3, Quaternion)>();
        public string WeaponName;
        public string WeaponPrefabName;

        public static StanceSnapshot Capture(Player player)
        {
            if (player == null || player.PlayerBones == null)
            {
                return null;
            }
            try
            {
                Transform root = player.Transform.Original;
                PlayerBones b = player.PlayerBones;
                Transform pelvis = b.Pelvis?.Original;
                if (root == null || pelvis == null)
                {
                    return null;
                }
                var s = new StanceSnapshot();
                Dictionary<string, Transform> bones = PoseSkeleton.PickBones(root, pelvis.parent ?? pelvis, out _);
                foreach (KeyValuePair<string, Transform> kv in bones)
                {
                    if (kv.Value != null)
                    {
                        s.Bones[kv.Key] = kv.Value.localRotation;
                    }
                }
                if (bones.TryGetValue("Base HumanPelvis", out Transform hips) && hips != null)
                {
                    s.HipsRootPosition = root.InverseTransformPoint(hips.position);
                    s.HipsRootRotation = Quaternion.Inverse(root.rotation) * hips.rotation;
                    s.HasHips = true;
                }

                Item held = player.HandsController?.Item;
                GameObject weapon = player.HandsController?.ControllerGameObject;
                Transform chest = b.Ribcage?.Original;
                if (held != null && !(held is EmptyHands) && weapon != null && chest != null)
                {
                    s.HasWeapon = true;
                    s.WeaponName = held.ShortName.Localized();
                    s.WeaponPrefabName = weapon.name;
                    s.WeaponChestPosition = chest.InverseTransformPoint(weapon.transform.position);
                    s.WeaponChestRotation = Quaternion.Inverse(chest.rotation) * weapon.transform.rotation;
                    CollectParts(weapon.transform, "", s.WeaponParts);
                }
                return s;
            }
            catch (Exception ex)
            {
                PhotoLog.Warn("Stance snapshot failed: " + ex.Message);
                return null;
            }
        }

        private static void CollectParts(Transform t, string path, Dictionary<string, (Vector3, Quaternion)> parts)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                Transform c = t.GetChild(i);
                string p = path.Length == 0 ? c.name : path + "/" + c.name;
                if (!parts.ContainsKey(p))
                {
                    parts[p] = (c.localPosition, c.localRotation);
                }
                CollectParts(c, p, parts);
            }
        }

        public int PlaceWeapon(Transform weapon, Transform chest, bool parts)
        {
            if (weapon == null || chest == null)
            {
                return 0;
            }
            weapon.SetPositionAndRotation(chest.TransformPoint(WeaponChestPosition), chest.rotation * WeaponChestRotation);
            int matched = 0;
            if (parts)
            {
                ApplyParts(weapon, "", ref matched);
            }
            return matched;
        }

        private void ApplyParts(Transform t, string path, ref int matched)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                Transform c = t.GetChild(i);
                string p = path.Length == 0 ? c.name : path + "/" + c.name;
                if (WeaponParts.TryGetValue(p, out (Vector3 position, Quaternion rotation) v))
                {
                    c.localPosition = v.position;
                    c.localRotation = v.rotation;
                    matched++;
                }
                ApplyParts(c, p, ref matched);
            }
        }
    }
}
