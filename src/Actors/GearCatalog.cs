using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.InventoryLogic;
using DuckCam.Core;

namespace DuckCam.Actors
{
    internal static class GearCatalog
    {
        public static readonly EquipmentSlot[] Slots =
        {
            EquipmentSlot.Headwear, EquipmentSlot.Earpiece, EquipmentSlot.FaceCover, EquipmentSlot.Eyewear,
            EquipmentSlot.ArmorVest, EquipmentSlot.TacticalVest, EquipmentSlot.Backpack, EquipmentSlot.ArmBand,
            EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster, EquipmentSlot.Scabbard
        };

        public static string SlotLabel(EquipmentSlot slot)
        {
            switch (slot)
            {
                case EquipmentSlot.Headwear: return "Headwear";
                case EquipmentSlot.Earpiece: return "Headset";
                case EquipmentSlot.FaceCover: return "Face Cover";
                case EquipmentSlot.Eyewear: return "Eyewear";
                case EquipmentSlot.ArmorVest: return "Body Armor";
                case EquipmentSlot.TacticalVest: return "Tactical Rig";
                case EquipmentSlot.Backpack: return "Backpack";
                case EquipmentSlot.ArmBand: return "Armband";
                case EquipmentSlot.FirstPrimaryWeapon: return "Primary Weapon";
                case EquipmentSlot.SecondPrimaryWeapon: return "Second Weapon";
                case EquipmentSlot.Holster: return "Holster";
                case EquipmentSlot.Scabbard: return "Melee";
                default: return slot.ToString();
            }
        }
    }
}
