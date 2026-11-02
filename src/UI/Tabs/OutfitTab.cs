using System.Linq;
using DuckCam.Actors;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed partial class DuckCamUI
    {
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

            string[] names = actors.Actors.Select(x => x.Name).ToArray();
            actors.Selected = _kit.Selector("Character", names, Mathf.Clamp(actors.Selected, 0, names.Length - 1));
            PhotoActor a = actors.SelectedActor;
            if (a == null || a.Status != ActorStatus.Ready)
            {
                _kit.Value("Status", a == null ? "None" : (a.Status == ActorStatus.Failed ? "Failed: " + a.Error : "Loading..."));
                return;
            }

            if (_kit.Action("Wardrobe", "Open"))
            {
                Spawner.ShowForActor(a);
            }
            _kit.Note("The Wardrobe has every piece of clothing and every item in the game. Pick Head, Upper Body or Lower Body for clothes, or a gear slot for gear. "
                      + "Helmets and other gear with attachment slots (NVGs, mounts, visors) get tabs for those slots at the top. Modded clothing and items appear automatically.");
            _kit.Value("Status", a.Reloading ? "Loading outfit..." : (a.ReloadError != null ? "Failed: " + a.ReloadError : "Ready"));

            DrawGear(a);

            if (a.OriginalCustomization != null && _kit.Action("Reset Clothes", "Reset"))
            {
                a.ChangeCustomization(a.OriginalCustomization);
            }
            if (a.OriginalEquipment != null && _kit.Action("Reset Gear", "Reset"))
            {
                a.ResetGear();
            }
            _kit.Note("Clothes and gear are display-only copies, so nobody's real inventory changes. The pose and position stay while a new outfit loads. Saving the character keeps the outfit.");
        }

        private void DrawGear(PhotoActor a)
        {
            _kit.Section("Show / hide gear");
            if (a.Descriptor?.Equipment == null)
            {
                _kit.Note("This character has no gear data.");
                return;
            }
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
        }
    }
}
