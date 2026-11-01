using System.Linq;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed partial class DuckCamUI
    {
        private string[] _addonPacks;

        private float _addonPacksAt = -10f;

        private void DrawAddons()
        {
            _kit.Section("Add-on Creator");
            if (_kit.Action("Add-on Creator", "Open"))
            {
                Addons.Show();
            }
            _kit.Note("Build your own add-on pack from things you saved: poses, filters, presets, light rigs, paths, timeline scenes, characters and bot kits. Tick items to move or copy them into a pack, rename them, and set the pack's author and version.");

            if (_addonPacks == null || Time.unscaledTime - _addonPacksAt > 2f)
            {
                _addonPacks = AddonCreator.AllPacks();
                _addonPacksAt = Time.unscaledTime;
            }
            _kit.Section("Installed packs");
            if (_addonPacks.Length == 0)
            {
                _kit.Value("Packs", "None yet");
            }
            foreach (string pack in _addonPacks)
            {
                PackInfo info = AddonCreator.Info(pack);
                string by = string.IsNullOrWhiteSpace(info.Author) ? "" : " - by " + info.Author.Trim();
                _kit.Value(pack, $"{AddonCreator.Count(pack)} items{by}");
            }
            _kit.Note("A pack is a folder inside each content folder, for example DuckCam/Poses/Your Pack. To share it, zip those folders with the DuckCam/... structure. Every list in DuckCam has a Pack filter to show one pack at a time.");
        }
    }
}
