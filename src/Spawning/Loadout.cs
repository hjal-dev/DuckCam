using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.InventoryLogic;
using DuckCam.Core;

namespace DuckCam.Spawning
{
    internal static class Loadout
    {
        public static readonly EquipmentSlot[] WeaponSlots = { EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster };
        private static readonly EquipmentSlot[] Containers = { EquipmentSlot.TacticalVest, EquipmentSlot.Pockets, EquipmentSlot.Backpack };

        public static string Replace(InventoryEquipment equipment, EquipmentSlot slotName, Item item)
        {
            Slot slot = equipment?.GetSlot(slotName);
            if (slot == null)
            {
                return "no " + slotName + " slot";
            }
            var carried = new List<Item>();
            Item old = slot.ContainedItem;
            if (old is CompoundItem oldContainer && oldContainer.Grids != null)
            {
                foreach (Grid grid in oldContainer.Grids)
                {
                    foreach (Item stored in grid.Items.ToList())
                    {
                        if (grid.RemoveWithoutRestrictions(stored).Succeeded)
                        {
                            carried.Add(stored);
                        }
                    }
                }
            }
            if (old != null)
            {
                slot.RemoveItemWithoutRestrictions();
            }
            if (item != null)
            {
                var added = slot.AddWithoutRestrictions(item);
                if (added.Failed)
                {
                    return "does not fit: " + added.Error;
                }
            }
            int lost = carried.Count(stored => !Place(equipment, stored));
            if (lost > 0)
            {
                PhotoLog.Verbose($"Loadout: {lost} stored item(s) had no room after replacing {slotName}.");
            }
            return null;
        }

        public static bool Place(InventoryEquipment equipment, Item item)
        {
            foreach (EquipmentSlot c in Containers)
            {
                if (!(equipment.GetSlot(c)?.ContainedItem is CompoundItem container) || container.Grids == null)
                {
                    continue;
                }
                foreach (Grid grid in container.Grids)
                {
                    if (!grid.CheckCompatibility(item))
                    {
                        continue;
                    }
                    LocationInGrid spot = grid.FindFreeSpace(item);
                    if (spot != null && grid.AddItemWithoutRestrictions(item, spot).Succeeded)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static void ArmWeapons(InventoryEquipment equipment, int spares)
        {
            foreach (EquipmentSlot s in WeaponSlots)
            {
                if (!(equipment.GetSlot(s)?.ContainedItem is Weapon weapon))
                {
                    continue;
                }
                try
                {
                    string ammo = AmmoFor(weapon);
                    if (ammo == null)
                    {
                        PhotoLog.Warn($"Loadout: no ammo found for {weapon.LocalizedName()}.");
                        continue;
                    }
                    Magazine mag = weapon.GetCurrentMagazine();
                    if (mag == null)
                    {
                        mag = FitMagazine(weapon);
                    }
                    if (mag != null)
                    {
                        TopUp(mag, ammo);
                    }
                    Chamber(weapon, ammo);
                    bool tube = mag == null || weapon.ReloadMode == Weapon.EReloadMode.InternalMagazine;
                    for (int i = 0; i < spares; i++)
                    {
                        Item spare = tube || mag == null ? Rounds(ammo, mag?.MaxCount ?? 10) : MagazineCopy(mag, ammo);
                        if (spare == null || !Place(equipment, spare))
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    PhotoLog.Warn($"Loadout: arming {weapon.LocalizedName()} failed: {ex.Message}");
                }
            }
        }

        public static void Refill(InventoryEquipment equipment)
        {
            foreach (EquipmentSlot s in WeaponSlots)
            {
                if (!(equipment.GetSlot(s)?.ContainedItem is Weapon weapon))
                {
                    continue;
                }
                string ammo = AmmoFor(weapon);
                if (ammo == null)
                {
                    continue;
                }
                Slot magSlot = weapon.GetMagazineSlot();
                Magazine inWeapon = weapon.GetCurrentMagazine();
                bool tube = inWeapon == null || weapon.ReloadMode == Weapon.EReloadMode.InternalMagazine;
                int spareMags = 0;
                int looseRounds = 0;
                foreach (Item stored in Stored(equipment))
                {
                    if (!tube && stored is Magazine m && m != inWeapon && magSlot != null && magSlot.CheckCompatibility(m))
                    {
                        spareMags++;
                        TopUp(m, ammo);
                    }
                    else if (tube && stored is Ammo a && a.StringTemplateId == ammo)
                    {
                        looseRounds += a.StackObjectsCount;
                    }
                }
                if (!tube && spareMags == 0 && inWeapon != null)
                {
                    Place(equipment, MagazineCopy(inWeapon, ammo));
                }
                int want = Math.Max(10, (inWeapon?.MaxCount ?? 10) * 2);
                if (tube && looseRounds < want)
                {
                    Place(equipment, Rounds(ammo, want - looseRounds));
                }
            }
        }

        private static IEnumerable<Item> Stored(InventoryEquipment equipment)
        {
            foreach (EquipmentSlot c in Containers)
            {
                if (equipment.GetSlot(c)?.ContainedItem is CompoundItem container && container.Grids != null)
                {
                    foreach (Grid grid in container.Grids)
                    {
                        foreach (Item item in grid.Items.ToList())
                        {
                            yield return item;
                        }
                    }
                }
            }
        }

        public static string AmmoFor(Weapon weapon)
        {
            Magazine mag = weapon.GetCurrentMagazine();
            Item loaded = mag?.Cartridges?.Items?.FirstOrDefault();
            if (loaded != null)
            {
                return loaded.StringTemplateId;
            }
            foreach (Slot chamber in weapon.Chambers ?? Array.Empty<Slot>())
            {
                if (chamber.ContainedItem != null)
                {
                    return chamber.ContainedItem.StringTemplateId;
                }
            }
            string def = weapon.Template?.defAmmo;
            if (!string.IsNullOrEmpty(def) && (mag == null || Accepts(mag, def)))
            {
                return def;
            }
            ItemFactory factory = ItemCatalog.Factory;
            foreach (ItemFilter f in mag?.Cartridges?.Filters ?? Array.Empty<ItemFilter>())
            {
                foreach (MongoID id in f.Filter ?? Array.Empty<MongoID>())
                {
                    if (factory != null && factory.ItemTemplates.TryGetValue(id, out ItemTemplate t) && t is AmmoTemplate)
                    {
                        return id.ToString();
                    }
                }
            }
            return def;
        }

        private static Magazine FitMagazine(Weapon weapon)
        {
            Slot slot = weapon.GetMagazineSlot();
            ItemFactory factory = ItemCatalog.Factory;
            if (slot == null || slot.ContainedItem != null || factory == null)
            {
                return null;
            }
            foreach (ItemFilter f in slot.Filters ?? Array.Empty<ItemFilter>())
            {
                foreach (MongoID id in f.Filter ?? Array.Empty<MongoID>())
                {
                    if (!factory.ItemTemplates.TryGetValue(id, out ItemTemplate t) || !(t is MagazineTemplate))
                    {
                        continue;
                    }
                    if (factory.CreateItem(factory.NextId, id.ToString(), null) is Magazine mag && slot.AddWithoutRestrictions(mag).Succeeded)
                    {
                        return mag;
                    }
                }
            }
            return null;
        }

        private static bool Accepts(Magazine mag, string ammoTpl)
        {
            Item probe = Rounds(ammoTpl, 1);
            return probe != null && mag.Cartridges.CheckCompatibility(probe);
        }

        private static void TopUp(Magazine mag, string ammo)
        {
            for (int guard = 0; guard < 16; guard++)
            {
                int missing = mag.MaxCount - mag.Count;
                if (missing <= 0)
                {
                    return;
                }
                Item rounds = Rounds(ammo, missing);
                if (rounds == null || mag.Cartridges.Add(rounds, false).Failed)
                {
                    return;
                }
            }
        }

        private static void Chamber(Weapon weapon, string ammo)
        {
            foreach (Slot chamber in weapon.Chambers ?? Array.Empty<Slot>())
            {
                if (chamber.ContainedItem == null)
                {
                    Item round = Rounds(ammo, 1);
                    if (round != null)
                    {
                        chamber.AddWithoutRestrictions(round);
                    }
                }
            }
        }

        private static Item MagazineCopy(Magazine template, string ammo)
        {
            Magazine copy = ItemCatalog.Factory?.CreateItem(ItemCatalog.Factory.NextId, template.StringTemplateId, null) as Magazine;
            if (copy != null)
            {
                TopUp(copy, ammo);
            }
            return copy;
        }

        private static Item Rounds(string ammoTpl, int count)
        {
            ItemFactory factory = ItemCatalog.Factory;
            if (factory == null || count <= 0)
            {
                return null;
            }
            Item item = factory.CreateItem(factory.NextId, ammoTpl, null);
            if (item == null)
            {
                return null;
            }
            item.StackObjectsCount = Math.Max(1, Math.Min(count, Math.Max(1, item.StackMaxSize)));
            return item;
        }
    }
}
