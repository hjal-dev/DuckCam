using System.Collections.Generic;
using System.Linq;
using DuckCam.Actors;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed partial class DuckCamUI
    {
        private int _outfitScope;

        private void DrawOutfit()
        {
            ActorManager actors = _c.Actors;
            if (actors.Actors.Count == 0)
            {
                _kit.Note("Outfits are changed on characters. Add your PMC (or a copy of anyone in the raid) in the CHARACTERS tab.");
                if (actors.CanAdd && _kit.Action("Your PMC", "Add"))
                {
                    _c.AddPmcActor();
                }
                return;
            }
            if (!OutfitCatalog.Available)
            {
                _kit.Note("The game's clothing data is not loaded, so outfits cannot be changed here.");
                return;
            }

            string[] names = actors.Actors.Select(x => x.Name).ToArray();
            actors.Selected = _kit.Selector("Character", names, Mathf.Clamp(actors.Selected, 0, names.Length - 1));
            PhotoActor a = actors.SelectedActor;
            if (a == null || a.Status != ActorStatus.Ready)
            {
                _kit.Value("Status", a == null ? "None" : (a.Status == ActorStatus.Failed ? "Failed: " + a.Error : "Loading..."));
                return;
            }
            Dictionary<EFT.EBodyModelPart, EFT.MongoID> customization = a.Descriptor?.Customization;
            if (customization == null)
            {
                _kit.Note("This character has no clothing data to change.");
                return;
            }

            _outfitScope = _kit.Selector("Show", OutfitCatalog.ScopeNames, _outfitScope);
            var scope = (OutfitScope)_outfitScope;
            EFT.EPlayerSide side = a.Descriptor.Info != null ? a.Descriptor.Info.Side : EFT.EPlayerSide.Usec;

            List<OutfitOption> heads = OutfitCatalog.Heads(side, scope);
            List<OutfitOption> tops = OutfitCatalog.Tops(side, scope);
            List<OutfitOption> lowers = OutfitCatalog.Lowers(side, scope);
            OutfitPart("Head", heads, customization, EFT.EBodyModelPart.Head, a);
            OutfitPart("Top", tops, customization, EFT.EBodyModelPart.Body, a);
            OutfitPart("Lower", lowers, customization, EFT.EBodyModelPart.Feet, a);

            DrawGear(a);

            _kit.Value("Status", a.Reloading ? "Loading outfit..." : (a.ReloadError != null ? "Failed: " + a.ReloadError : "Ready"));
            if (a.OriginalCustomization != null && _kit.Action("Reset Outfit", "Reset"))
            {
                a.ChangeCustomization(a.OriginalCustomization);
            }
            _kit.Note($"{side} character · {heads.Count} heads, {tops.Count} tops, {lowers.Count} lowers. "
                      + "Names are the game's own (Ragman / Character screen); modded clothing appears automatically. "
                      + "Matching Side shows what this side can wear, All Sides adds the other faction's outfits, Everything adds bot and boss pieces. "
                      + "The pose and position stay while the new outfit loads. Saving the character keeps the outfit.");
        }

        private object _gearKey;

        private float _gearBuilt;

        private readonly Dictionary<EFT.InventoryLogic.EquipmentSlot, List<GearOption>> _gearOptions = new Dictionary<EFT.InventoryLogic.EquipmentSlot, List<GearOption>>();

        private void DrawGear(PhotoActor a)
        {
            _kit.Section("Gear");
            if (a.Descriptor?.Equipment == null)
            {
                _kit.Note("This character has no gear data to change.");
                return;
            }
            if (_kit.Action("Item Database", "Open"))
            {
                Spawner.ShowForActor(a);
            }
            _kit.Note("Item Database has every item in the game. The quick pickers below only list gear worn in this raid.");

            object key = a.Descriptor.Equipment;
            if (!ReferenceEquals(key, _gearKey) || Time.unscaledTime - _gearBuilt > 3f)
            {
                _gearKey = key;
                _gearBuilt = Time.unscaledTime;
                _gearOptions.Clear();
                EFT.InventoryLogic.InventoryEquipment equipment = a.CurrentEquipment();
                foreach (EFT.InventoryLogic.EquipmentSlot slot in GearCatalog.Slots)
                {
                    _gearOptions[slot] = GearCatalog.Options(slot, equipment?.GetSlot(slot)?.ContainedItem);
                }
            }

            foreach (EFT.InventoryLogic.EquipmentSlot slot in GearCatalog.Slots)
            {
                if (!_gearOptions.TryGetValue(slot, out List<GearOption> options))
                {
                    continue;
                }
                int index = options.Count > 1 && options[1].Owner == "current" ? 1 : 0;
                string[] labels = options.Select(o => o.Item == null ? "None" : o.Owner == "current" || o.Owner == null ? o.Name : o.Name + "  (" + o.Owner + ")").ToArray();
                int pick = _kit.Selector(GearCatalog.SlotLabel(slot), labels, index);
                if (pick != index)
                {
                    a.ChangeGear(slot, options[pick].Item);
                    _gearKey = null;
                }
            }
            _kit.Section("Show / hide gear");
            int toggles = 0;
            foreach (EFT.InventoryLogic.EquipmentSlot slot in GearCatalog.Slots)
            {
                bool hidden = a.IsSlotHidden(slot);
                if (!hidden && !a.SlotHasModel(slot))
                {
                    continue;
                }
                toggles++;
                bool shown = _kit.Toggle(GearCatalog.SlotLabel(slot), !hidden);
                if (shown == hidden)
                {
                    a.SetSlotHidden(slot, !shown);
                }
            }
            if (toggles == 0)
            {
                _kit.Note("This character shows no gear that can be hidden.");
            }
            bool inHands = _kit.Toggle("Weapon In Hands", a.WeaponVisible);
            if (inHands != a.WeaponVisible)
            {
                a.SetWeaponVisible(inHands);
            }

            if (a.OriginalEquipment != null && _kit.Action("Reset Gear", "Reset"))
            {
                a.ResetGear();
                _gearKey = null;
            }
            _kit.Note("Gear choices are everything worn by anyone in this raid (alive or dead, you included), shown with who wears it. "
                      + "Items are display-only copies, so nobody's real inventory changes. Weapons show in the hands with Weapon In Hands on (CHARACTERS).");
        }

        private void OutfitPart(string label, List<OutfitOption> options, Dictionary<EFT.EBodyModelPart, EFT.MongoID> customization, EFT.EBodyModelPart part, PhotoActor actor)
        {
            if (!customization.TryGetValue(part, out EFT.MongoID current))
            {
                _kit.Value(label, "None");
                return;
            }
            string currentId = current.ToString();
            int index = options.FindIndex(o => o.MainId == currentId);
            if (index < 0)
            {
                options.Insert(0, OutfitCatalog.Current(part, current));
                index = 0;
            }
            string[] labels = options.Select(o => o.Name).ToArray();
            int pick = _kit.Selector(label, labels, index);
            if (pick != index)
            {
                var next = new Dictionary<EFT.EBodyModelPart, EFT.MongoID>(customization);
                options[pick].Apply(next);
                actor.ChangeCustomization(next);
            }
            _kit.Value("  Item", options[pick].InternalName);
        }
    }
}
