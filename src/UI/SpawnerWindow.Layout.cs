using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DuckCam.Actors;
using DuckCam.Core;
using DuckCam.Spawning;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed partial class SpawnerWindow
    {
        private enum WardrobeTab
        {
            Clothes,
            Gear,
            Weapons
        }

        private enum Hover
        {
            None,
            Grid,
            Preview,
            Overlay
        }

        private static readonly string[] TabNames = { "CLOTHES", "GEAR", "WEAPONS" };

        private static readonly EquipmentSlot[] GearSlots =
        {
            EquipmentSlot.Headwear, EquipmentSlot.Earpiece, EquipmentSlot.FaceCover, EquipmentSlot.Eyewear,
            EquipmentSlot.ArmorVest, EquipmentSlot.TacticalVest, EquipmentSlot.Backpack, EquipmentSlot.ArmBand
        };

        private static readonly EquipmentSlot[] WeaponSlots =
        {
            EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster, EquipmentSlot.Scabbard
        };

        private WardrobeTab _tab = WardrobeTab.Weapons;
        private const int Columns = 4;
        private bool _equippedOnly;
        private bool _overlay;
        private Hover _hover;
        private float _previewDist = 4.2f;
        private string _lookKey;
        private int _changes;
        private Dictionary<EBodyModelPart, MongoID> _revertCustomization;
        private InventoryEquipmentDescriptor _revertEquipment;
        private string _outfitName = "My outfit";
        private string _confirmOutfit;
        private string[] _outfits = new string[0];
        private readonly PackFilter _outfitPacks = new PackFilter();

        private void BeginLook(PhotoActor actor)
        {
            _lookKey = null;
            _changes = 0;
            _overlay = false;
            _equippedOnly = false;
            _tab = WardrobeTab.Clothes;
            _outfits = Outfits.List();
            try
            {
                _revertCustomization = actor.Descriptor?.Customization != null ? new Dictionary<EBodyModelPart, MongoID>(actor.Descriptor.Customization) : null;
                _revertEquipment = actor.Descriptor?.Equipment != null ? new InventoryEquipmentDescriptor(actor.Descriptor.Equipment.ToEquipment()) : null;
            }
            catch (Exception ex)
            {
                _revertEquipment = null;
                PhotoLog.Warn("Wardrobe: the starting look could not be stored for Revert: " + ex.Message);
            }
        }

        public void Draw(float vw, float vh)
        {
            if (!Open)
            {
                return;
            }
            if (_picking)
            {
                DrawPicker(vw, vh);
                return;
            }
            _lastDrawFrame = Time.frameCount;
            _hover = Hover.None;
            if (Event.current.type == EventType.Repaint)
            {
                WarmCounts();
            }
            var win = new Rect(24f, 24f, vw - 48f, vh - 48f);
            Vector2 tl = GUIUtility.GUIToScreenPoint(new Vector2(win.x, win.y));
            Vector2 br = GUIUtility.GUIToScreenPoint(new Vector2(win.xMax, win.yMax));
            _screenRect = new Rect(tl, br - tl);

            HudTheme.Window(win);
            HudTheme.Border(win, HudTheme.P.LineBright);
            GUI.Label(new Rect(win.x + 24f, win.y + 10f, 900f, 36f), TargetMode ? "WARDROBE · " + _target.Name.ToUpperInvariant() : "BOT SPAWNER", Left(HudTheme.Title, wrap: false));
            if (Button(new Rect(win.xMax - 46f, win.y + 12f, 32f, 30f), "X", false))
            {
                Close();
                return;
            }
            HudTheme.Fill(new Rect(win.x + 16f, win.y + 52f, win.width - 32f, 2f), HudTheme.P.Line);

            float top = win.y + 66f;
            float bottom = win.yMax - 78f;
            float leftW = Mathf.Clamp(win.width * 0.2f, 300f, 380f);
            float rightW = Mathf.Clamp(win.width * 0.47f, 640f, 1000f);
            var left = new Rect(win.x + 16f, top, leftW, bottom - top);
            var right = new Rect(win.xMax - 16f - rightW, top, rightW, bottom - top);
            var center = new Rect(left.xMax + 12f, top, right.x - left.xMax - 24f, bottom - top);

            DrawWearing(left);
            DrawStage(center);
            DrawCatalog(right);
            DrawBar(new Rect(win.x + 16f, bottom + 12f, win.width - 32f, 54f));
        }

        public void Scroll(float notches)
        {
            switch (_hover)
            {
                case Hover.Preview:
                    _previewDist = Mathf.Clamp(_previewDist - notches * 0.35f, 1.1f, 6f);
                    break;
                default:
                    _scroll = Mathf.Max(0f, _scroll - notches * 60f);
                    break;
            }
        }

        private IEnumerable<WardrobeTab> Tabs()
        {
            if (TargetMode)
            {
                yield return WardrobeTab.Clothes;
            }
            yield return WardrobeTab.Gear;
            yield return WardrobeTab.Weapons;
        }

        private void SelectTab(WardrobeTab tab)
        {
            _tab = tab;
            _modPath = null;
            _scroll = 0f;
            _overlay = false;
            if (tab == WardrobeTab.Clothes)
            {
                _outfitPart = _outfitPart ?? EBodyModelPart.Body;
                return;
            }
            _outfitPart = null;
            EquipmentSlot[] slots = tab == WardrobeTab.Gear ? GearSlots : WeaponSlots;
            if (!slots.Contains(_slot))
            {
                _slot = slots[0];
            }
        }

        private void SelectSlot(EquipmentSlot slot)
        {
            _tab = WeaponSlots.Contains(slot) ? WardrobeTab.Weapons : WardrobeTab.Gear;
            _slot = slot;
            _outfitPart = null;
            _modPath = null;
            _scroll = 0f;
            _overlay = false;
        }

        private void SelectPart(EBodyModelPart part)
        {
            _tab = WardrobeTab.Clothes;
            _outfitPart = part;
            _modPath = null;
            _scroll = 0f;
            _overlay = false;
        }

        private void DrawSearch(Rect r)
        {
            const string control = HudKit.FieldPrefix + "SpawnerSearch";
            bool focused = GUI.GetNameOfFocusedControl() == control;
            bool hasText = !string.IsNullOrEmpty(_search);
            if (Event.current.type == EventType.Repaint)
            {
                HudTheme.Fill(r, new Color(0f, 0f, 0f, 0.6f));
                HudTheme.Border(r, focused ? HudTheme.P.Highlight : HudTheme.P.LineBright);
                HudTheme.Tex(new Rect(r.x + 9f, r.y + (r.height - 18f) * 0.5f, 18f, 18f), HudTheme.Icon("search"), focused ? HudTheme.P.Highlight : HudTheme.P.Label);
            }
            var clear = new Rect(r.xMax - r.height, r.y, r.height, r.height);
            if (hasText)
            {
                bool hover = clear.Contains(Event.current.mousePosition);
                if (Event.current.type == EventType.Repaint)
                {
                    if (hover)
                    {
                        HudTheme.Fill(new Rect(clear.x + 3f, clear.y + 3f, clear.width - 6f, clear.height - 6f), HudTheme.P.TabFillActive);
                    }
                    HudTheme.Tex(new Rect(clear.x + (clear.width - 14f) * 0.5f, clear.y + (clear.height - 14f) * 0.5f, 14f, 14f), HudTheme.Icon("clear"), hover ? HudTheme.P.Highlight : HudTheme.P.Label);
                }
                if (GUI.Button(clear, new GUIContent("", "Clear the search"), GUIStyle.none))
                {
                    _search = "";
                    _scroll = 0f;
                    GUI.FocusControl(null);
                    InputContext.LeaveField();
                    return;
                }
            }
            var field = new Rect(r.x + 34f, r.y + 2f, r.width - 34f - (hasText ? r.height : 8f), r.height - 4f);
            GUI.SetNextControlName(control);
            string search = GUI.TextField(field, _search ?? "", 64, Left(HudTheme.Field, wrap: false));
            if (search != _search)
            {
                _search = search;
                _scroll = 0f;
            }
            if (!hasText && !focused)
            {
                GUI.Label(new Rect(field.x + 2f, r.y, field.width, r.height), "SEARCH BY NAME", Left(HudTheme.Note, wrap: false));
            }
        }

        private void DrawWearing(Rect r)
        {
            HudTheme.Fill(r, new Color(0f, 0f, 0f, 0.3f));
            float x = r.x + 14f;
            float w = r.width - 28f;
            float y = r.y + 10f;
            if (!TargetMode)
            {
                y = DrawRoleBlock(x, y, w);
            }
            GUI.Label(new Rect(x, y, w, 22f), "WEARING", Left(HudTheme.Section, wrap: false));
            y += 30f;
            InventoryEquipment eq = Equipment;
            const float tile = 70f;
            if (TargetMode)
            {
                y = WearingGroup(x, y, w, "CLOTHES", OutfitParts.Length, OutfitParts.Length, i =>
                {
                    EBodyModelPart part = OutfitParts[i];
                    return (ShortPart[i], OutfitName(part), null, _tab == WardrobeTab.Clothes && _outfitPart == part, (Action)(() => SelectPart(part)));
                }, (w - 24f) / 3f, 76f);
            }
            y = SlotGroup(x, y, w, "GEAR", GearSlots, eq, tile);
            SlotGroup(x, y, w, "WEAPONS", WeaponSlots, eq, tile);
        }

        private float SlotGroup(float x, float y, float w, string title, EquipmentSlot[] slots, InventoryEquipment eq, float tile)
        {
            int worn = slots.Count(s => eq?.GetSlot(s)?.ContainedItem != null);
            return WearingGroup(x, y, w, title, slots.Length, worn, i =>
            {
                EquipmentSlot s = slots[i];
                Item item = eq?.GetSlot(s)?.ContainedItem;
                bool selected = _tab != WardrobeTab.Clothes && _slot == s;
                return (ShortSlot(s), (string)null, item, selected, (Action)(() => SelectSlot(s)));
            }, tile, tile);
        }

        private float WearingGroup(float x, float y, float w, string title, int count, int worn, Func<int, (string Label, string Text, Item Item, bool Selected, Action Click)> cell, float tileW, float tileH)
        {
            GUI.Label(new Rect(x - 2f, y, w * 0.6f, 30f), title, GroupTitle);
            GUI.Label(new Rect(x + w * 0.6f, y + 6f, w * 0.4f, 20f), $"{worn} WORN", RightSmall);
            y += 36f;
            float gapX = 12f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((w + gapX + 1f) / (tileW + gapX)));
            if (perRow > 1)
            {
                gapX = Mathf.Max(12f, (w - perRow * tileW) / (perRow - 1));
            }
            GUIStyle labelStyle = TileLabelFor(70f + Mathf.Max(12f, (w - 4f * 70f) / 3f) - 2f);
            for (int i = 0; i < count; i++)
            {
                (string label, string text, Item item, bool selected, Action click) = cell(i);
                var t = new Rect(x + (i % perRow) * (tileW + gapX), y + (i / perRow) * (tileH + 26f), tileW, tileH);
                bool hover = t.Contains(Event.current.mousePosition);
                if (Event.current.type == EventType.Repaint)
                {
                    HudTheme.Fill(t, selected ? HudTheme.P.RowFillSelected : hover ? HudTheme.P.TabFillActive : HudTheme.P.Card);
                    HudTheme.Border(t, selected ? HudTheme.P.Highlight : HudTheme.P.Line);
                }
                if (item != null)
                {
                    DrawSprite(new Rect(t.x + 5f, t.y + 5f, t.width - 10f, t.height - 10f), SlotIcon(item));
                }
                else if (text != null)
                {
                    GUI.Label(new Rect(t.x + 6f, t.y + 4f, t.width - 12f, t.height - 8f), text, t.width > 90f ? ClothesText : TileText);
                }
                else
                {
                    GUI.Label(t, "+", Centered(HudTheme.Title));
                }
                var labelRect = new Rect(t.x - gapX * 0.5f + 1f, t.yMax + 3f, t.width + gapX - 2f, 20f);
                GUI.Label(labelRect, label, labelStyle);
                if (GUI.Button(t, new GUIContent("", item != null ? item.LocalizedName() : text ?? label), GUIStyle.none))
                {
                    click();
                }
            }
            int rows = Mathf.CeilToInt(count / (float)perRow);
            return y + rows * (tileH + 26f) + 14f;
        }

        private float DrawRoleBlock(float x, float y, float w)
        {
            SpawnRole role = Role;
            GUI.Label(new Rect(x, y, w, 20f), role != null ? role.Group.ToUpperInvariant() : "ROLE", Left(HudTheme.HudSmall, wrap: false));
            y += 22f;
            if (Button(new Rect(x, y, 34f, 30f), "<", false)) StepRole(-1);
            GUI.Label(new Rect(x + 38f, y + 2f, w - 76f, 26f), role?.Name ?? "-", Centered(HudTheme.Value));
            if (Button(new Rect(x + w - 34f, y, 34f, 30f), ">", false)) StepRole(1);
            y += 36f;
            if (Button(new Rect(x, y, w * 0.5f - 4f, 30f), "GROUP >", false)) StepGroup();
            if (Button(new Rect(x + w * 0.5f + 4f, y, w * 0.5f - 4f, 30f), _generating ? "..." : "REROLL", false) && !_generating)
            {
                Regenerate();
            }
            y += 36f;
            if (Button(new Rect(x, y, w * 0.5f - 4f, 30f), "USE GENERATED", false))
            {
                _choices.Remove(_slot);
                Regenerate();
            }
            if (Button(new Rect(x + w * 0.5f + 4f, y, w * 0.5f - 4f, 30f), "RESET ALL", false))
            {
                _choices.Clear();
                Regenerate();
            }
            return y + 46f;
        }

        private void DrawStage(Rect r)
        {
            if (Event.current.type == EventType.Repaint)
            {
                RenderPreview();
            }
            if (r.Contains(Event.current.mousePosition))
            {
                _hover = Hover.Preview;
            }
            Texture rt = _previewRt;
            bool ready = _stage.Ready || (TargetMode ? _target.Status == ActorStatus.Ready : _preview?.Status == ActorStatus.Ready);
            if (rt != null && ready && Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(r, rt, ScaleMode.ScaleAndCrop);
            }
            bool reloading = _stage.Loading || (TargetMode ? _target.Reloading : _preview != null && _preview.Reloading);
            if (!ready || rt == null)
            {
                string text = _generating ? "Generating bot..." : !TargetMode && _preview?.Status == ActorStatus.Loading ? "Loading model..." : (!TargetMode ? _preview?.Error : null) ?? "Loading...";
                GUI.Label(new Rect(r.x, r.center.y - 12f, r.width, 24f), text, Centered(HudTheme.Note));
            }
            else if (reloading)
            {
                GUI.Label(new Rect(r.x + 10f, r.y + 8f, r.width, 22f), "UPDATING...", HudTheme.HudSmall);
            }
            Event e = Event.current;
            if (e.type == EventType.MouseDrag && r.Contains(e.mousePosition))
            {
                _previewYaw -= e.delta.x * 0.6f;
                e.Use();
            }
            GUI.Label(new Rect(r.x, r.yMax - 24f, r.width, 20f), "Drag to rotate · Wheel to zoom", Centered(HudTheme.Note));
        }

        private void DrawCatalog(Rect r)
        {
            HudTheme.Fill(new Rect(r.x, r.y, r.width, 3f), HudTheme.P.Highlight);
            float x = r.x;
            float y = r.y + 12f;

            string title;
            string sub;
            CatalogHeader(out title, out sub);
            GUI.Label(new Rect(x, y, r.width - 330f, 40f), title, Left(HudTheme.Title, wrap: false));
            GUI.Label(new Rect(x, y + 38f, r.width - 330f, 20f), sub, Left(HudTheme.Note, wrap: false));

            DrawSearch(new Rect(r.xMax - 320f, y + 4f, 320f, 34f));
            y += 66f;

            float tx = x;
            foreach (WardrobeTab tab in Tabs())
            {
                if (Button(new Rect(tx, y, 110f, 32f), TabNames[(int)tab], _tab == tab && !_overlay))
                {
                    SelectTab(tab);
                }
                tx += 114f;
            }
            float bx = r.xMax;
            bx -= 150f;
            if (Button(new Rect(bx, y, 150f, 32f), "EQUIPPED ONLY", _equippedOnly))
            {
                _equippedOnly = !_equippedOnly;
                _scroll = 0f;
            }
            if (TargetMode && _tab == WardrobeTab.Gear)
            {
                List<TogglableComponent> flips = Togglables();
                for (int i = flips.Count - 1; i >= 0; i--)
                {
                    bool down = flips[i].On;
                    bx -= 174f;
                    if (Button(new Rect(bx, y, 170f, 32f), FlipName(flips[i]) + (down ? ": DOWN" : ": UP"), down))
                    {
                        Flip(i);
                    }
                }
            }
            if (TargetMode && _tab == WardrobeTab.Clothes)
            {
                bx -= 194f;
                if (Button(new Rect(bx, y, 190f, 32f), "SHOW: " + OutfitCatalog.ScopeNames[_outfitScope].ToUpperInvariant(), false))
                {
                    _outfitScope = (_outfitScope + 1) % OutfitCatalog.ScopeNames.Length;
                    _scroll = 0f;
                }
            }
            y += 42f;

            var body = new Rect(r.x, y, r.width, r.yMax - y);
            if (_overlay)
            {
                if (body.Contains(Event.current.mousePosition))
                {
                    _hover = Hover.Overlay;
                }
                if (TargetMode)
                {
                    DrawOutfits(body);
                }
                else
                {
                    DrawKits(body);
                }
                return;
            }
            var list = new Rect(body.x, body.y, 210f, body.height);
            var grid = new Rect(list.xMax + 10f, body.y, body.width - 220f, body.height);
            if (grid.Contains(Event.current.mousePosition))
            {
                _hover = Hover.Grid;
            }
            DrawSubList(list);
            if (TargetMode && _tab == WardrobeTab.Clothes)
            {
                DrawClothesGrid(grid);
            }
            else
            {
                DrawItemGrid(grid);
            }
        }

        private void CatalogHeader(out string title, out string sub)
        {
            if (_overlay)
            {
                title = TargetMode ? "OUTFITS" : "BOT KITS";
                sub = TargetMode ? $"{_outfits.Length} SAVED · THE WHOLE LOOK, CLOTHES AND GEAR" : $"{_kits?.Count ?? 0} SAVED · ROLE AND GEAR";
                return;
            }
            if (TargetMode && _tab == WardrobeTab.Clothes && _outfitPart.HasValue)
            {
                int p = Array.IndexOf(OutfitParts, _outfitPart.Value);
                title = OutfitPartNames[p].ToUpperInvariant();
                sub = $"{OutfitOptions(_outfitPart.Value).Count} ITEMS · WEARING {OutfitName(_outfitPart.Value).ToUpperInvariant()}";
                return;
            }
            Item item = Equipment?.GetSlot(_slot)?.ContainedItem;
            string mod = null;
            if (_modPath != null && item != null)
            {
                mod = ModSlots(item).FirstOrDefault(m => m.Path == _modPath).Label;
            }
            title = GearCatalog.SlotLabel(_slot).ToUpperInvariant() + (mod != null ? " / " + mod.ToUpperInvariant() : "");
            int cached = ItemCatalog.CachedCount(_slot);
            sub = (cached >= 0 ? cached + " ITEMS · " : "") + "WEARING " + (item != null ? item.LocalizedName().ToUpperInvariant() : "NOTHING");
        }

        private void DrawSubList(Rect r)
        {
            float y = r.y;
            if (TargetMode && _tab == WardrobeTab.Clothes)
            {
                for (int i = 0; i < OutfitParts.Length; i++)
                {
                    EBodyModelPart part = OutfitParts[i];
                    if (SubRow(new Rect(r.x, y, r.width, 32f), OutfitPartNames[i].ToUpperInvariant(), OutfitOptions(part).Count.ToString(CultureInfo.InvariantCulture), _outfitPart == part, 0f))
                    {
                        SelectPart(part);
                    }
                    y += 36f;
                }
                return;
            }
            InventoryEquipment eq = Equipment;
            foreach (EquipmentSlot s in _tab == WardrobeTab.Weapons ? WeaponSlots : GearSlots)
            {
                int cached = ItemCatalog.CachedCount(s);
                if (SubRow(new Rect(r.x, y, r.width, 32f), GearCatalog.SlotLabel(s).ToUpperInvariant(), cached >= 0 ? cached.ToString(CultureInfo.InvariantCulture) : "", _slot == s && _modPath == null, 0f))
                {
                    SelectSlot(s);
                }
                y += 36f;
                Item item = eq?.GetSlot(s)?.ContainedItem;
                if (s != _slot || item == null || ItemCatalog.IsWeaponSlot(s))
                {
                    continue;
                }
                foreach ((string path, string label, Slot _) in ModSlots(item))
                {
                    if (y + 30f > r.yMax)
                    {
                        break;
                    }
                    int modCount = ItemCatalog.CachedModCount(item.TemplateId + "|" + path);
                    if (SubRow(new Rect(r.x, y, r.width, 28f), label.ToUpperInvariant(), modCount >= 0 ? modCount.ToString(CultureInfo.InvariantCulture) : "", _modPath == path, 16f))
                    {
                        _modPath = path;
                        _scroll = 0f;
                    }
                    y += 32f;
                }
            }
        }

        private static bool SubRow(Rect r, string text, string count, bool selected, float indent)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            if (Event.current.type == EventType.Repaint)
            {
                HudTheme.Fill(r, selected ? HudTheme.P.RowFillSelected : hover ? HudTheme.P.TabFillActive : HudTheme.P.Card);
                if (selected)
                {
                    HudTheme.Fill(new Rect(r.x, r.y, 3f, r.height), HudTheme.P.Highlight);
                }
            }
            GUI.Label(new Rect(r.x + 10f + indent, r.y, r.width - 60f - indent, r.height), (indent > 0f ? "> " : "") + text, Left(selected ? HudTheme.ValueSelected : HudTheme.Value, wrap: false));
            GUI.Label(new Rect(r.xMax - 56f, r.y, 48f, r.height), count, RightSmall);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        private void DrawItemGrid(Rect r)
        {
            Slot slot = Equipment?.GetSlot(_slot);
            if (slot == null)
            {
                GUI.Label(new Rect(r.x + 8f, r.y + 10f, r.width, 22f), _generating ? "Waiting for the bot..." : "This character has no such slot.", HudTheme.Note);
                return;
            }
            Slot modSlot = null;
            if (_modPath != null && slot.ContainedItem != null)
            {
                modSlot = ModSlots(slot.ContainedItem).FirstOrDefault(m => m.Path == _modPath).Slot;
                if (modSlot == null)
                {
                    _modPath = null;
                }
            }
            List<CatalogEntry> all = modSlot != null ? ItemCatalog.ForMod(slot.ContainedItem.TemplateId + "|" + _modPath, modSlot) : ItemCatalog.For(_slot, slot);
            Item worn = modSlot != null ? modSlot.ContainedItem : slot.ContainedItem;
            string wornTpl = worn?.TemplateId.ToString();
            _choices.TryGetValue(_slot, out string chosen);
            if (modSlot != null || TargetMode)
            {
                chosen = null;
            }
            IEnumerable<CatalogEntry> filtered = all;
            string[] tokens = SearchTokens();
            if (tokens.Length > 0)
            {
                filtered = filtered.Where(e => MatchesAll((e.Name ?? "") + " " + (e.Detail ?? ""), tokens));
            }
            if (_equippedOnly)
            {
                filtered = filtered.Where(e => e.Key == chosen || (wornTpl != null && e.TemplateId == wornTpl));
            }
            List<CatalogEntry> list = filtered.ToList();
            bool empty = string.IsNullOrWhiteSpace(_search) && (!_equippedOnly || worn == null);
            int offset = empty ? 1 : 0;
            Grid(r, list.Count + offset, 1.42f, (card, i) =>
            {
                if (empty && i == 0)
                {
                    Card(card, worn == null && (chosen == null || chosen == BotKits.None), null, "Empty", false);
                    GUI.Label(new Rect(card.x, card.y + 20f, card.width, card.width * 0.78f), "-", Centered(HudTheme.Title));
                    if (GUI.Button(card, new GUIContent("", "Empty this slot"), GUIStyle.none))
                    {
                        if (modSlot != null)
                        {
                            AttachMod(_modPath, null);
                        }
                        else
                        {
                            Choose(_slot, BotKits.None);
                        }
                    }
                    return;
                }
                CatalogEntry entry = list[i - offset];
                bool wearing = entry.Key == chosen || (chosen == null && wornTpl != null && entry.TemplateId == wornTpl);
                Card(card, wearing, entry.Detail, entry.Name, entry.UserBuild, tokens);
                DrawSprite(new Rect(card.x + 10f, card.y + 24f, card.width - 20f, card.width * 0.78f - 10f), entry.Icon);
                if (GUI.Button(card, new GUIContent("", entry.Name), GUIStyle.none))
                {
                    if (modSlot != null)
                    {
                        AttachMod(_modPath, entry);
                    }
                    else
                    {
                        Choose(_slot, entry.Key);
                    }
                }
            });
        }

        private void DrawClothesGrid(Rect r)
        {
            if (!_outfitPart.HasValue)
            {
                return;
            }
            EBodyModelPart part = _outfitPart.Value;
            Dictionary<EBodyModelPart, MongoID> customization = _target.Descriptor?.Customization;
            if (!OutfitCatalog.Available || customization == null)
            {
                GUI.Label(new Rect(r.x + 8f, r.y + 10f, r.width, 22f), "The game's clothing data isn't available for this character.", HudTheme.Note);
                return;
            }
            string currentId = customization.TryGetValue(part, out MongoID cur) ? cur.ToString() : null;
            IEnumerable<OutfitOption> filtered = OutfitOptions(part);
            string[] tokens = SearchTokens();
            if (tokens.Length > 0)
            {
                filtered = filtered.Where(o => MatchesAll((o.Name ?? "") + " " + (o.InternalName ?? ""), tokens));
            }
            if (_equippedOnly)
            {
                filtered = filtered.Where(o => o.MainId == currentId);
            }
            List<OutfitOption> list = filtered.ToList();
            Grid(r, list.Count, 0.62f, (card, i) =>
            {
                OutfitOption option = list[i];
                bool wearing = option.MainId == currentId;
                Card(card, wearing, null, null, false);
                var nameRect = new Rect(card.x + 8f, card.y + 18f, card.width - 16f, card.height - 30f);
                Highlight(nameRect, option.Name, CardTitle, tokens);
                GUI.Label(nameRect, option.Name, CardTitle);
                if (GUI.Button(card, new GUIContent("", option.InternalName), GUIStyle.none) && !wearing)
                {
                    var next = new Dictionary<EBodyModelPart, MongoID>(customization);
                    option.Apply(next);
                    _target.ChangeCustomization(next);
                    MarkStageDirty();
                    _changes++;
                    _message = $"{OutfitPartNames[Array.IndexOf(OutfitParts, part)]}: {option.Name}. Loading...";
                }
            });
        }

        private void Grid(Rect r, int count, float aspect, Action<Rect, int> draw)
        {
            const float gap = 8f;
            int cols = Columns;
            float cw = (r.width - 14f - (cols - 1) * gap) / cols;
            float ch = cw * aspect;
            int rows = Mathf.CeilToInt(count / (float)cols);
            _scroll = Mathf.Clamp(_scroll, 0f, Mathf.Max(0f, rows * (ch + gap) - r.height));
            int firstRow = Mathf.FloorToInt(_scroll / (ch + gap));
            int visibleRows = Mathf.CeilToInt(r.height / (ch + gap)) + 1;
            GUI.BeginGroup(r);
            for (int row = firstRow; row < Mathf.Min(rows, firstRow + visibleRows); row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    int i = row * cols + col;
                    if (i >= count)
                    {
                        break;
                    }
                    draw(new Rect(col * (cw + gap), row * (ch + gap) - _scroll, cw, ch), i);
                }
            }
            GUI.EndGroup();
            if (count == 0)
            {
                GUI.Label(new Rect(r.x + 4f, r.y + 10f, r.width, 22f), _equippedOnly ? "Nothing equipped here." : "Nothing matches.", HudTheme.Note);
            }
            if (rows * (ch + gap) > r.height && Event.current.type == EventType.Repaint)
            {
                float trackH = r.height;
                float thumbH = Mathf.Max(30f, trackH * r.height / (rows * (ch + gap)));
                float t = _scroll / Mathf.Max(1f, rows * (ch + gap) - r.height);
                HudTheme.Fill(new Rect(r.xMax - 4f, r.y, 3f, trackH), HudTheme.P.Line);
                HudTheme.Fill(new Rect(r.xMax - 5f, r.y + (trackH - thumbH) * t, 5f, thumbH), HudTheme.P.Highlight);
            }
        }

        private static void Card(Rect card, bool wearing, string detail, string name, bool userBuild, string[] tokens = null)
        {
            bool hover = card.Contains(Event.current.mousePosition);
            if (Event.current.type == EventType.Repaint)
            {
                HudTheme.Fill(card, wearing ? HudTheme.P.RowFillSelected : hover ? HudTheme.P.TabFillActive : HudTheme.P.Card);
                HudTheme.Fill(new Rect(card.x, card.y, 4f, card.height), wearing ? HudTheme.P.Highlight : userBuild ? new Color(0.95f, 0.55f, 0.1f, 0.9f) : new Color(0.95f, 0.55f, 0.1f, 0.45f));
                HudTheme.Border(card, wearing ? HudTheme.P.Highlight : HudTheme.P.Line);
            }
            float badgeW = 0f;
            if (wearing)
            {
                badgeW = Mathf.Min(card.width - 12f, BadgeText.CalcSize(new GUIContent("WEARING")).x + 12f);
                var badge = new Rect(card.xMax - badgeW - 5f, card.y + 5f, badgeW, 16f);
                HudTheme.Fill(badge, HudTheme.P.Highlight);
                GUI.Label(badge, "WEARING", BadgeText);
            }
            if (!string.IsNullOrEmpty(detail))
            {
                var detailRect = new Rect(card.x + 10f, card.y + 5f, card.width - badgeW - 22f, 16f);
                Highlight(detailRect, detail.ToUpperInvariant(), userBuild ? SmallHi : SmallNote, tokens);
                GUI.Label(detailRect, detail.ToUpperInvariant(), userBuild ? SmallHi : SmallNote);
            }
            if (name != null)
            {
                float top = card.y + 24f + card.width * 0.78f;
                var nameRect = new Rect(card.x + 6f, top, card.width - 12f, card.yMax - top - 4f);
                Highlight(nameRect, name.ToUpperInvariant(), CardText, tokens);
                GUI.Label(nameRect, name.ToUpperInvariant(), CardText);
            }
        }

        private static readonly string[] ShortPart = { "HEAD", "UPPER", "LOWER" };

        private static string ShortSlot(EquipmentSlot slot)
        {
            switch (slot)
            {
                case EquipmentSlot.Headwear: return "HELMET";
                case EquipmentSlot.Earpiece: return "HEADSET";
                case EquipmentSlot.FaceCover: return "FACE";
                case EquipmentSlot.Eyewear: return "EYES";
                case EquipmentSlot.ArmorVest: return "ARMOR";
                case EquipmentSlot.TacticalVest: return "RIG";
                case EquipmentSlot.Backpack: return "BACKPACK";
                case EquipmentSlot.ArmBand: return "ARMBAND";
                case EquipmentSlot.FirstPrimaryWeapon: return "PRIMARY";
                case EquipmentSlot.SecondPrimaryWeapon: return "SECOND";
                case EquipmentSlot.Holster: return "HOLSTER";
                case EquipmentSlot.Scabbard: return "MELEE";
                default: return GearCatalog.SlotLabel(slot).ToUpperInvariant();
            }
        }

        private void WarmCounts()
        {
            InventoryEquipment eq = Equipment;
            if (eq == null)
            {
                return;
            }
            foreach (EquipmentSlot s in GearSlots.Concat(WeaponSlots))
            {
                if (ItemCatalog.CachedCount(s) < 0)
                {
                    Slot slot = eq.GetSlot(s);
                    if (slot != null)
                    {
                        ItemCatalog.For(s, slot);
                        return;
                    }
                }
            }
            Item item = _tab != WardrobeTab.Clothes ? eq.GetSlot(_slot)?.ContainedItem : null;
            if (item == null || ItemCatalog.IsWeaponSlot(_slot))
            {
                return;
            }
            foreach ((string path, string _, Slot slot) in ModSlots(item))
            {
                string key = item.TemplateId + "|" + path;
                if (ItemCatalog.CachedModCount(key) < 0)
                {
                    ItemCatalog.ForMod(key, slot);
                    return;
                }
            }
        }

        private static GUIStyle _tileText;
        private static GUIStyle _tileLabel;
        private static GUIStyle _rightSmall;
        private static GUIStyle _badgeText;
        private static GUIStyle _smallNote;
        private static GUIStyle _smallHi;

        private static GUIStyle TileText => _tileText ?? (_tileText = new GUIStyle(HudTheme.Note) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = Math.Max(9, HudTheme.Note.fontSize - 2), clipping = TextClipping.Clip });

        private static GUIStyle _clothesText;

        private static GUIStyle ClothesText => _clothesText ?? (_clothesText = new GUIStyle(HudTheme.Note) { alignment = TextAnchor.MiddleCenter, wordWrap = true, clipping = TextClipping.Clip });

        private static GUIStyle TileLabel => _tileLabel ?? (_tileLabel = new GUIStyle(HudTheme.Note) { alignment = TextAnchor.UpperCenter, wordWrap = false, fontSize = Math.Max(9, HudTheme.Note.fontSize - 2), clipping = TextClipping.Clip });

        private static GUIStyle _groupTitle;

        private static GUIStyle GroupTitle => _groupTitle ?? (_groupTitle = new GUIStyle(Left(HudTheme.Title, wrap: false)) { padding = new RectOffset(0, 2, 1, 1) });

        private static GUIStyle _tileLabelFit;
        private static float _tileLabelWidth = -1f;

        private static GUIStyle TileLabelFor(float width)
        {
            if (_tileLabelFit == null || Mathf.Abs(width - _tileLabelWidth) > 0.5f)
            {
                var style = new GUIStyle(TileLabel) { fontSize = HudTheme.Note.fontSize + 1, clipping = TextClipping.Overflow };
                while (style.fontSize > 8 && style.CalcSize(new GUIContent("BACKPACK")).x > width - 4f)
                {
                    style.fontSize--;
                }
                _tileLabelFit = style;
                _tileLabelWidth = width;
            }
            return _tileLabelFit;
        }


        private static GUIStyle RightSmall => _rightSmall ?? (_rightSmall = new GUIStyle(HudTheme.Note) { alignment = TextAnchor.MiddleRight, wordWrap = false, clipping = TextClipping.Clip });

        private static GUIStyle BadgeText => _badgeText ?? (_badgeText = new GUIStyle(HudTheme.BoxTextHi) { alignment = TextAnchor.MiddleCenter, wordWrap = false, fontSize = Math.Max(9, HudTheme.Note.fontSize - 2), clipping = TextClipping.Overflow });

        private static GUIStyle SmallNote => _smallNote ?? (_smallNote = new GUIStyle(HudTheme.Note) { alignment = TextAnchor.MiddleLeft, wordWrap = false, fontSize = Math.Max(9, HudTheme.Note.fontSize - 2), clipping = TextClipping.Clip });

        private static GUIStyle SmallHi => _smallHi ?? (_smallHi = new GUIStyle(HudTheme.HudSmall) { alignment = TextAnchor.MiddleLeft, wordWrap = false, fontSize = Math.Max(9, HudTheme.Note.fontSize - 2), clipping = TextClipping.Clip });

        private List<TogglableComponent> Togglables()
        {
            Item root = Equipment?.GetSlot(_slot)?.ContainedItem;
            if (root == null)
            {
                return new List<TogglableComponent>();
            }
            try
            {
                return root.GetItemComponentsInChildren<TogglableComponent>(false).Where(t => t.Item != null).ToList();
            }
            catch (Exception)
            {
                return new List<TogglableComponent>();
            }
        }

        private static string FlipName(TogglableComponent toggle)
        {
            Item item = toggle.Item;
            if (item.GetItemComponent<NightVisionComponent>() != null || item.GetItemComponent<ThermalVisionComponent>() != null)
            {
                return "NVG";
            }
            if (item.GetItemComponent<FaceShieldComponent>() != null)
            {
                return "VISOR";
            }
            string name = item.ShortName.Localized();
            return string.IsNullOrWhiteSpace(name) ? "FLIP" : name.ToUpperInvariant();
        }

        private void Flip(int index)
        {
            InventoryEquipment eq = Equipment;
            Item root = eq?.GetSlot(_slot)?.ContainedItem;
            List<TogglableComponent> flips = Togglables();
            if (root == null || index < 0 || index >= flips.Count)
            {
                return;
            }
            TogglableComponent toggle = flips[index];
            bool down = !toggle.On;
            try
            {
                if (toggle.Set(down, false, true).Failed)
                {
                    toggle.On = down;
                }
            }
            catch (Exception)
            {
                toggle.On = down;
            }
            _target.ChangeGear(_slot, root);
            _changes++;
            _targetEq = null;
            _slotIcons.Clear();
            MarkStageDirty();
            _message = $"{FlipName(toggle)} flipped {(down ? "down" : "up")}. Only the look changes; nobody gets night vision or a visor effect.";
        }

        private static readonly Color MatchColor = new Color(1f, 1f, 1f, 0.2f);

        private string[] SearchTokens() => (_search ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        private static bool MatchesAll(string text, string[] tokens)
        {
            foreach (string token in tokens)
            {
                if (text.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }
            return true;
        }

        private static void Highlight(Rect rect, string text, GUIStyle style, string[] tokens)
        {
            if (tokens == null || tokens.Length == 0 || string.IsNullOrEmpty(text) || Event.current.type != EventType.Repaint)
            {
                return;
            }
            var marked = new bool[text.Length];
            bool any = false;
            foreach (string token in tokens)
            {
                for (int at = text.IndexOf(token, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(token, at + 1, StringComparison.OrdinalIgnoreCase))
                {
                    for (int k = at; k < at + token.Length && k < marked.Length; k++)
                    {
                        marked[k] = true;
                        any = true;
                    }
                }
            }
            if (!any)
            {
                return;
            }
            var content = new GUIContent(text);
            float lineHeight = style.lineHeight;
            for (int k = 0; k < text.Length; k++)
            {
                if (!marked[k] || text[k] == '\n')
                {
                    continue;
                }
                Vector2 p = style.GetCursorPixelPosition(rect, content, k);
                Vector2 q = style.GetCursorPixelPosition(rect, content, k + 1);
                float width = Mathf.Abs(q.y - p.y) < 0.5f ? q.x - p.x : style.CalcSize(new GUIContent(text[k].ToString())).x;
                if (width <= 0f)
                {
                    continue;
                }
                HudTheme.Fill(new Rect(p.x, p.y, width, lineHeight), MatchColor);
            }
        }

        private static GUIStyle _cardTitle;

        private static GUIStyle CardTitle => _cardTitle ?? (_cardTitle = new GUIStyle(HudTheme.Value) { alignment = TextAnchor.MiddleCenter, wordWrap = true, clipping = TextClipping.Clip });

        private void DrawOutfits(Rect r)
        {
            HudTheme.Fill(r, new Color(0f, 0f, 0f, 0.3f));
            float y = r.y + 8f;
            GUI.Label(new Rect(r.x + 10f, y + 4f, 80f, 24f), "NAME", HudTheme.HudSmall);
            GUI.SetNextControlName(HudKit.FieldPrefix + "WardrobeOutfitName");
            _outfitName = GUI.TextField(new Rect(r.x + 70f, y, r.width - 70f - 190f, 30f), _outfitName ?? "", 40, HudTheme.Field);
            if (Button(new Rect(r.xMax - 180f, y, 172f, 30f), "SAVE OUTFIT", true))
            {
                try
                {
                    _lookKey = Outfits.Save(_target, _outfitName);
                    _changes = 0;
                    _outfits = Outfits.List();
                    _message = $"Outfit '{ContentPacks.Display(_lookKey)}' saved.";
                }
                catch (Exception ex)
                {
                    _message = "Saving the outfit failed: " + ex.Message;
                }
            }
            y += 40f;
            string[] packs = _outfitPacks.Options(_outfits, Outfits.Directory);
            if (_outfitPacks.Active)
            {
                if (Button(new Rect(r.x + 10f, y, 220f, 28f), "PACK: " + _outfitPacks.Label.ToUpperInvariant(), false))
                {
                    _outfitPacks.Cycle(_outfits, Outfits.Directory);
                    _scroll = 0f;
                }
                string info = _outfitPacks.Info(Outfits.Directory);
                if (info != null)
                {
                    GUI.Label(new Rect(r.x + 240f, y, r.width - 250f, 28f), info, Left(HudTheme.Note, wrap: false));
                }
                y += 36f;
            }
            string[] shown = _outfitPacks.Apply(_outfits);
            if (shown.Length == 0)
            {
                GUI.Label(new Rect(r.x + 10f, y + 6f, r.width - 20f, 60f), "No outfits yet. Dress the character, type a name and press SAVE OUTFIT. Outfits work on any character.", Left(HudTheme.Note, wrap: true));
                return;
            }
            var area = new Rect(r.x + 6f, y, r.width - 12f, r.yMax - y - 6f);
            _scroll = Mathf.Clamp(_scroll, 0f, Mathf.Max(0f, shown.Length * 46f - area.height));
            GUI.BeginGroup(area);
            float ry = -_scroll;
            foreach (string key in shown)
            {
                var row = new Rect(0f, ry, area.width, 42f);
                ry += 46f;
                if (row.yMax < 0f || row.y > area.height)
                {
                    continue;
                }
                bool current = key == _lookKey;
                HudTheme.Fill(row, current ? HudTheme.P.RowFillSelected : HudTheme.P.Card);
                if (current)
                {
                    HudTheme.Fill(new Rect(row.x, row.y, 3f, row.height), HudTheme.P.Highlight);
                }
                GUI.Label(new Rect(row.x + 14f, row.y, row.width - 260f, row.height), _outfitPacks.Show(key).ToUpperInvariant(), Left(current ? HudTheme.ValueSelected : HudTheme.Value, wrap: false));
                if (Button(new Rect(row.xMax - 230f, row.y + 6f, 110f, 30f), "WEAR", false))
                {
                    string error = Outfits.Apply(_target, key);
                    MarkStageDirty();
                    _targetEq = null;
                    _slotIcons.Clear();
                    if (error == null)
                    {
                        _lookKey = key;
                        _changes = 0;
                        _outfitName = ContentPacks.NameOf(key);
                        _message = $"Wearing '{ContentPacks.Display(key)}'. Loading...";
                    }
                    else
                    {
                        _message = "That outfit couldn't be worn: " + error;
                    }
                }
                bool confirming = _confirmOutfit == key;
                if (Button(new Rect(row.xMax - 114f, row.y + 6f, 108f, 30f), confirming ? "SURE?" : "DELETE", confirming))
                {
                    if (confirming)
                    {
                        Outfits.Delete(key);
                        _confirmOutfit = null;
                        if (_lookKey == key)
                        {
                            _lookKey = null;
                        }
                        _outfits = Outfits.List();
                        _message = $"Outfit '{ContentPacks.Display(key)}' deleted.";
                    }
                    else
                    {
                        _confirmOutfit = key;
                    }
                }
            }
            GUI.EndGroup();
        }

        private void DrawBar(Rect r)
        {
            HudTheme.Fill(r, HudTheme.P.TabFill);
            HudTheme.Fill(new Rect(r.x, r.y, r.width, 2f), HudTheme.P.Line);
            if (TargetMode)
            {
                DrawLookBar(r);
            }
            else
            {
                DrawSpawnBar(r);
            }
        }

        private void DrawLookBar(Rect r)
        {
            GUI.Label(new Rect(r.x + 14f, r.y + 5f, 180f, 22f), "CURRENT LOOK", Left(HudTheme.Section, wrap: false));
            string state = (_lookKey != null ? ContentPacks.Display(_lookKey).ToUpperInvariant() : "UNSAVED") + (_changes > 0 ? $" · EDITED · {_changes} CHANGE{(_changes == 1 ? "" : "S")}" : "");
            GUI.Label(new Rect(r.x + 14f, r.y + 27f, 420f, 20f), state, Left(HudTheme.Note, wrap: false));
            GUI.Label(new Rect(r.x + 450f, r.y + 5f, r.width - 450f - 600f, 44f), new GUIContent(_message, _message), Left(HudTheme.Note, wrap: true));
            float x = r.xMax - 590f;
            if (Button(new Rect(x, r.y + 9f, 140f, 36f), "REVERT", false))
            {
                if (_revertEquipment != null || _revertCustomization != null)
                {
                    _target.ApplyLook(_revertCustomization, _revertEquipment != null ? new InventoryEquipmentDescriptor(_revertEquipment.ToEquipment()) : null);
                    MarkStageDirty();
                    _targetEq = null;
                    _slotIcons.Clear();
                    _changes = 0;
                    _message = "Back to how this character looked when the Wardrobe opened.";
                }
            }
            x += 146f;
            if (Button(new Rect(x, r.y + 9f, 140f, 36f), "UPDATE", false))
            {
                if (_lookKey == null)
                {
                    _overlay = true;
                    _scroll = 0f;
                    _message = "This look isn't saved yet. Type a name and press SAVE OUTFIT.";
                }
                else
                {
                    try
                    {
                        Outfits.Save(_target, _lookKey);
                        _changes = 0;
                        _message = $"Outfit '{ContentPacks.Display(_lookKey)}' updated.";
                    }
                    catch (Exception ex)
                    {
                        _message = "Updating the outfit failed: " + ex.Message;
                    }
                }
            }
            x += 146f;
            if (Button(new Rect(x, r.y + 9f, 150f, 36f), $"OUTFITS ({_outfits.Length})", _overlay))
            {
                _overlay = !_overlay;
                _scroll = 0f;
                _outfits = Outfits.List();
            }
            x += 156f;
            if (Button(new Rect(x, r.y + 9f, 140f, 36f), "DONE", true))
            {
                Close();
            }
        }

        private void DrawSpawnBar(Rect r)
        {
            float x = r.x + 12f;
            GUI.Label(new Rect(x, r.y + 16f, 60f, 22f), "COUNT", HudTheme.HudSmall);
            x += 58f;
            if (Button(new Rect(x, r.y + 12f, 30f, 30f), "-", false)) _count = Mathf.Max(1, _count - 1);
            GUI.Label(new Rect(x + 32f, r.y + 14f, 36f, 26f), _count.ToString(CultureInfo.InvariantCulture), Centered(HudTheme.Value));
            if (Button(new Rect(x + 70f, r.y + 12f, 30f, 30f), "+", false)) _count = Mathf.Min(10, _count + 1);
            x += 116f;
            _sessionOnly = Check(new Rect(x, r.y + 12f, 190f, 30f), "Only in this session", _sessionOnly);
            x += 198f;
            _infiniteAmmo = Check(new Rect(x, r.y + 12f, 150f, 30f), "Infinite ammo", _infiniteAmmo);
            x += 158f;
            GUI.Label(new Rect(x, r.y + 16f, 90f, 22f), "SPARE MAGS", HudTheme.HudSmall);
            x += 92f;
            if (Button(new Rect(x, r.y + 12f, 30f, 30f), "-", false)) _spareMags = Mathf.Max(0, _spareMags - 1);
            GUI.Label(new Rect(x + 32f, r.y + 14f, 30f, 26f), _spareMags.ToString(CultureInfo.InvariantCulture), Centered(HudTheme.Value));
            if (Button(new Rect(x + 64f, r.y + 12f, 30f, 30f), "+", false)) _spareMags = Mathf.Min(8, _spareMags + 1);
            x += 108f;
            var status = new Rect(x, r.y + 6f, r.xMax - x - 560f, 22f);
            GUI.Label(status, new GUIContent(_message, _message), Left(HudTheme.Note, wrap: false));
            if (!string.IsNullOrEmpty(_service.Status) && _service.Status != _message)
            {
                GUI.Label(new Rect(status.x, status.y + 22f, status.width, 22f), new GUIContent(_service.Status, _service.Status), Left(HudTheme.Note, wrap: false));
            }
            float bx = r.xMax - 550f;
            if (Button(new Rect(bx, r.y + 9f, 150f, 36f), $"KITS ({_kits?.Count ?? 0})", _overlay))
            {
                _overlay = !_overlay;
                _scroll = 0f;
            }
            if (Button(new Rect(bx + 156f, r.y + 9f, 180f, 36f), "ADD TO PHOTO", false))
            {
                AddToPhoto();
            }
            if (Button(new Rect(bx + 342f, r.y + 9f, 196f, 36f), _count > 1 ? $"PLACE x{_count}" : "PLACE & SPAWN", true))
            {
                SpawnCurrent();
            }
        }
    }
}
