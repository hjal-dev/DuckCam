using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Customization;
using DuckCam.Core;

namespace DuckCam.Actors
{
    internal enum OutfitScope
    {
        MatchingSide,
        AllSides,
        Everything
    }

    internal sealed class OutfitOption
    {
        public string MainId;
        public string Name;
        public string InternalName;
        public Action<Dictionary<EBodyModelPart, MongoID>> Apply;
    }

    internal static class OutfitCatalog
    {
        public static readonly string[] ScopeNames = { "Matching Side", "All Sides", "Everything" };

        public static bool Available => Singleton<CustomizationSolver>.Instantiated;

        public static List<OutfitOption> Heads(EPlayerSide side, OutfitScope scope)
        {
            var list = new List<OutfitOption>();
            if (!Available)
            {
                return list;
            }
            foreach (CustomizationHead head in Singleton<CustomizationSolver>.Instance._headTemplates.Values)
            {
                if (head == null || head.Prefab == null || !InScope(head, side, scope))
                {
                    continue;
                }
                MongoID id = head.Id;
                list.Add(new OutfitOption
                {
                    MainId = id.ToString(),
                    Name = NameOf(head),
                    InternalName = head.Name,
                    Apply = c => c[EBodyModelPart.Head] = id
                });
            }
            return Sorted(list);
        }

        public static List<OutfitOption> Tops(EPlayerSide side, OutfitScope scope)
        {
            var list = new List<OutfitOption>();
            if (!Available)
            {
                return list;
            }
            var used = new HashSet<string>();
            foreach (UpperBodySuit suite in Singleton<CustomizationSolver>.Instance._customizationSuiteTemplates.Values.OfType<UpperBodySuit>())
            {
                if (!InScope(suite, side, scope) || !used.Add(suite.Body.ToString()))
                {
                    continue;
                }
                MongoID body = suite.Body;
                MongoID hands = suite.Hands;
                list.Add(new OutfitOption
                {
                    MainId = body.ToString(),
                    Name = NameOf(suite),
                    InternalName = suite.Name,
                    Apply = c =>
                    {
                        c[EBodyModelPart.Body] = body;
                        c[EBodyModelPart.Hands] = hands;
                    }
                });
            }
            if (scope == OutfitScope.Everything)
            {
                AddLoosePieces(list, used, EBodyModelPart.Body);
            }
            return Sorted(list);
        }

        public static List<OutfitOption> Lowers(EPlayerSide side, OutfitScope scope)
        {
            var list = new List<OutfitOption>();
            if (!Available)
            {
                return list;
            }
            var used = new HashSet<string>();
            foreach (LowerBodySuit suite in Singleton<CustomizationSolver>.Instance._customizationSuiteTemplates.Values.OfType<LowerBodySuit>())
            {
                if (!InScope(suite, side, scope) || (suite.DisableForMannequin && scope != OutfitScope.Everything) || !used.Add(suite.Feet.ToString()))
                {
                    continue;
                }
                MongoID feet = suite.Feet;
                list.Add(new OutfitOption
                {
                    MainId = feet.ToString(),
                    Name = NameOf(suite),
                    InternalName = suite.Name,
                    Apply = c => c[EBodyModelPart.Feet] = feet
                });
            }
            if (scope == OutfitScope.Everything)
            {
                AddLoosePieces(list, used, EBodyModelPart.Feet);
            }
            return Sorted(list);
        }

        public static OutfitOption Current(EBodyModelPart part, MongoID id)
        {
            CustomizationClothing item = null;
            try
            {
                item = Available ? Singleton<CustomizationSolver>.Instance.GetAnyCustomizationItem(id) as CustomizationClothing : null;
            }
            catch (Exception)
            {
            }
            return new OutfitOption
            {
                MainId = id.ToString(),
                Name = item != null ? NameOf(item) : "Current (" + id + ")",
                InternalName = item?.Name ?? id.ToString(),
                Apply = c => c[part] = id
            };
        }

        private static void AddLoosePieces(List<OutfitOption> list, HashSet<string> used, EBodyModelPart part)
        {
            foreach (CustomizationClothing item in Singleton<CustomizationSolver>.Instance._customizationClothingTemplates.Values)
            {
                if (item == null || item is CustomizationHead || item.BodyPart != part || item.Prefab == null || !used.Add(item.Id.ToString()))
                {
                    continue;
                }
                MongoID id = item.Id;
                list.Add(new OutfitOption
                {
                    MainId = id.ToString(),
                    Name = NameOf(item),
                    InternalName = item.Name,
                    Apply = c => c[part] = id
                });
            }
        }

        private static bool InScope(BaseCustomizationItem item, EPlayerSide side, OutfitScope scope)
        {
            if (scope != OutfitScope.MatchingSide)
            {
                return true;
            }
            return item.Side == null || item.Side.Length == 0 || item.Side.Contains(side);
        }

        public static string NameOf(BaseCustomizationItem item)
        {
            try
            {
                string key = item.NameLocalizationKey;
                string text = key.Localized();
                if (!string.IsNullOrWhiteSpace(text) && text != key)
                {
                    return text.Trim();
                }
            }
            catch (Exception ex)
            {
                PhotoLog.Debug("Outfit name lookup failed: " + ex.Message);
            }
            return string.IsNullOrEmpty(item.Name) ? item.Id.ToString() : item.Name;
        }

        private static List<OutfitOption> Sorted(List<OutfitOption> list)
        {
            return list.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
