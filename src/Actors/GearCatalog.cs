using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.InventoryLogic;
using DuckCam.Core;

namespace DuckCam.Actors
{
    internal sealed class GearOption
    {
        public Item Item;
        public string Name;
        public string Owner;
        public string Key;
    }

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

        public static List<GearOption> Options(EquipmentSlot slot, Item current)
        {
            var list = new List<GearOption> { new GearOption { Item = null, Name = "None", Key = "none" } };
            var seen = new HashSet<string> { "none" };
            if (current != null)
            {
                GearOption c = Make(current, "current");
                seen.Add(c.Key);
                list.Add(c);
            }

            var found = new List<GearOption>();
            try
            {
                GameWorld world = GameContext.World;
                if (world != null)
                {
                    foreach (Player p in world.AllPlayersEverExisted)
                    {
                        Item item = p?.Profile?.Inventory?.Equipment?.GetSlot(slot)?.ContainedItem;
                        if (item == null)
                        {
                            continue;
                        }
                        GearOption o = Make(item, p.IsYourPlayer ? "you" : p.Profile.Nickname);
                        if (seen.Add(o.Key))
                        {
                            found.Add(o);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PhotoLog.WarnOnce("gear-catalog", "Gear list incomplete: " + ex.Message);
            }
            list.AddRange(found.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase));
            return list;
        }

        public static GearOption Make(Item item, string owner)
        {
            int parts = 0;
            try
            {
                parts = item.GetAllItems().Count();
            }
            catch (Exception)
            {
            }
            return new GearOption
            {
                Item = item,
                Name = NameOf(item),
                Owner = owner,
                Key = item.TemplateId + ":" + parts
            };
        }

        public static string NameOf(Item item)
        {
            try
            {
                string name = item.Name.Localized();
                if (!string.IsNullOrWhiteSpace(name) && name != item.Name)
                {
                    return name.Trim();
                }
                string shortName = item.ShortName.Localized();
                if (!string.IsNullOrWhiteSpace(shortName) && shortName != item.ShortName)
                {
                    return shortName.Trim();
                }
            }
            catch (Exception)
            {
            }
            return item.TemplateId.ToString();
        }
    }
}
