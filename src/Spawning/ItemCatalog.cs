using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI.Builds;
using EFT.UI.DragAndDrop;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Spawning
{
    internal sealed class CatalogEntry
    {
        public string Key;
        public string TemplateId;
        public string Name;
        public string Detail;
        public bool UserBuild;
        public WeaponBuild Build;

        private Item _sample;
        private ItemIcon _icon;
        private bool _iconFailed;

        public Item Sample
        {
            get
            {
                if (_sample == null && !_iconFailed)
                {
                    try
                    {
                        _sample = ItemCatalog.CreateItem(this);
                    }
                    catch (Exception)
                    {
                        _iconFailed = true;
                    }
                }
                return _sample;
            }
        }

        public Sprite Icon
        {
            get
            {
                if (_icon == null && !_iconFailed && Sample != null)
                {
                    try
                    {
                        _icon = ItemViewFactory.LoadItemIcon(Sample);
                    }
                    catch (Exception)
                    {
                        _iconFailed = true;
                    }
                }
                return _icon?.Sprite;
            }
        }
    }

    internal static class ItemCatalog
    {
        private static readonly Dictionary<EquipmentSlot, List<CatalogEntry>> Cache = new Dictionary<EquipmentSlot, List<CatalogEntry>>();

        public static bool IsWeaponSlot(EquipmentSlot slot) =>
            slot == EquipmentSlot.FirstPrimaryWeapon || slot == EquipmentSlot.SecondPrimaryWeapon || slot == EquipmentSlot.Holster;

        public static ItemFactory Factory => Singleton<ItemFactory>.Instantiated ? Singleton<ItemFactory>.Instance : null;

        public static void Invalidate()
        {
            Cache.Clear();
        }

        public static List<CatalogEntry> For(EquipmentSlot slotName, Slot slot)
        {
            if (Cache.TryGetValue(slotName, out List<CatalogEntry> cached))
            {
                return cached;
            }
            var list = new List<CatalogEntry>();
            ItemFactory factory = Factory;
            if (factory == null || slot == null)
            {
                return list;
            }

            try
            {
                if (IsWeaponSlot(slotName) || slotName == EquipmentSlot.Scabbard)
                {
                    var covered = new HashSet<string>();
                    if (slotName != EquipmentSlot.Scabbard)
                    {
                        foreach (WeaponBuild build in Builds())
                        {
                            Item root;
                            try
                            {
                                root = build.Item;
                            }
                            catch (Exception)
                            {
                                continue;
                            }
                            if (root == null || !Fits(factory, slot, root.TemplateId.ToString()))
                            {
                                continue;
                            }
                            covered.Add(root.TemplateId.ToString());
                            list.Add(new CatalogEntry
                            {
                                Key = "build:" + build.Id,
                                TemplateId = root.TemplateId.ToString(),
                                Name = string.IsNullOrWhiteSpace(build.HandbookName) ? root.LocalizedName() : build.HandbookName,
                                Detail = build.FromPreset ? "Preset" : "My preset",
                                UserBuild = !build.FromPreset,
                                Build = build
                            });
                        }
                    }
                    bool melee = slotName == EquipmentSlot.Scabbard;
                    foreach (ItemTemplate t in Templates(factory).Where(t => !covered.Contains(t._id.ToString())
                                 && (melee || (factory._itemPresets != null && factory._itemPresets.ContainsKey(t._id)))
                                 && Fits(factory, slot, t._id.ToString())))
                    {
                        list.Add(FromTemplate(t, "Default"));
                    }
                }
                else
                {
                    foreach (ItemTemplate t in Templates(factory).Where(t => Fits(factory, slot, t._id.ToString())))
                    {
                        list.Add(FromTemplate(t, null));
                    }
                }
            }
            catch (Exception ex)
            {
                PhotoLog.Warn($"Item catalog for {slotName} failed: {ex.Message}");
            }

            list = list
                .OrderByDescending(e => e.UserBuild)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Cache[slotName] = list;
            return list;
        }

        public static IEnumerable<WeaponBuild> Builds()
        {
            WeaponBuildsStorage storage = Factory?._weaponBuildsStorage;
            return storage?.WeaponBuilds?.Values ?? Enumerable.Empty<WeaponBuild>();
        }

        public static CatalogEntry Find(EquipmentSlot slotName, Slot slot, string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }
            CatalogEntry hit = For(slotName, slot).FirstOrDefault(e => e.Key == key);
            if (hit != null || !key.StartsWith("tpl:", StringComparison.Ordinal))
            {
                return hit;
            }
            ItemFactory factory = Factory;
            string tpl = key.Substring(4);
            return factory != null && factory.ItemTemplates.TryGetValue(new MongoID(tpl), out ItemTemplate t) ? FromTemplate(t, null) : null;
        }

        private static IEnumerable<ItemTemplate> Templates(ItemFactory factory) =>
            factory.ItemTemplates.Values.Where(t => t._type == NodeType.Item && !t.QuestItem);

        private static CatalogEntry FromTemplate(ItemTemplate t, string detail)
        {
            string name = t.NameLocalizationKey.Localized();
            if (string.IsNullOrWhiteSpace(name) || name == t.NameLocalizationKey)
            {
                name = t._name;
            }
            return new CatalogEntry
            {
                Key = "tpl:" + t._id,
                TemplateId = t._id.ToString(),
                Name = name,
                Detail = detail
            };
        }

        public static bool Fits(ItemFactory factory, Slot slot, string templateId)
        {
            ItemFilter[] filters = slot.Filters;
            if (filters == null || filters.Length == 0)
            {
                return false;
            }
            var chain = new List<string>();
            MongoID? id = new MongoID(templateId);
            for (int guard = 0; id.HasValue && guard < 32; guard++)
            {
                chain.Add(id.Value.ToString());
                id = factory.ItemTemplates.TryGetValue(id.Value, out ItemTemplate t) ? t.ParentId : null;
            }
            bool included = false;
            foreach (ItemFilter f in filters)
            {
                if (f.ExcludedFilter != null && f.ExcludedFilter.Any(x => chain.Contains(x.ToString())))
                {
                    return false;
                }
                if (f.Filter != null && f.Filter.Any(x => chain.Contains(x.ToString())))
                {
                    included = true;
                }
            }
            return included;
        }

        public static Item CreateItem(CatalogEntry entry)
        {
            ItemFactory factory = Factory;
            if (factory == null || entry == null)
            {
                return null;
            }
            if (entry.Build != null)
            {
                return entry.Build.Item.CloneItem();
            }
            var tpl = new MongoID(entry.TemplateId);
            return factory._itemPresets != null && factory._itemPresets.ContainsKey(tpl)
                ? factory.GetPresetItem(entry.TemplateId)
                : factory.CreateAndFillItem(tpl);
        }
    }
}
