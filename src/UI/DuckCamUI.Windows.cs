using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed partial class DuckCamUI
    {
        private static readonly HashSet<string> AlwaysShown = new HashSet<string> { "frame" };

        private List<TabDef> _visibleTabs;
        private string _visibleKey;

        private List<TabDef> Tabs
        {
            get
            {
                string key = DuckCamConfig.HiddenTabs.Value ?? "";
                if (_visibleTabs == null || key != _visibleKey)
                {
                    string current = _visibleTabs != null && _tab >= 0 && _tab < _visibleTabs.Count ? _visibleTabs[_tab].Icon : null;
                    HashSet<string> hidden = HiddenSet();
                    _visibleTabs = _allTabs.Where(t => AlwaysShown.Contains(t.Icon) || !hidden.Contains(t.Icon)).ToList();
                    _visibleKey = key;
                    int keep = current != null ? _visibleTabs.FindIndex(t => t.Icon == current) : -1;
                    _tab = keep >= 0 ? keep : Mathf.Clamp(_tab, 0, _visibleTabs.Count - 1);
                }
                return _visibleTabs;
            }
        }

        private static HashSet<string> HiddenSet()
        {
            return new HashSet<string>((DuckCamConfig.HiddenTabs.Value ?? "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
        }

        private static void SetHidden(IEnumerable<string> icons)
        {
            DuckCamConfig.HiddenTabs.Value = string.Join(",", icons.Where(i => !AlwaysShown.Contains(i)).Distinct());
        }

        private readonly HashSet<string> _howToOpen = new HashSet<string>();

        private void DrawHowTo(TabDef tab)
        {
            if (!TabHelp.Has(tab.Icon))
            {
                return;
            }
            bool open = _howToOpen.Contains(tab.Icon);
            if (_kit.Action("How-To", open ? "Hide" : "Show"))
            {
                if (open)
                {
                    _howToOpen.Remove(tab.Icon);
                }
                else
                {
                    _howToOpen.Add(tab.Icon);
                }
                open = !open;
            }
            if (open)
            {
                _kit.Note(TabHelp.Get(tab.Icon));
            }
        }

        private void DrawInterfaceExtras()
        {
            int hiddenCount = HiddenSet().Count(h => _allTabs.Any(t => t.Icon == h));
            if (_kit.Action("Tabs", hiddenCount > 0 ? $"Choose ({hiddenCount} hidden)" : "Choose"))
            {
                _tabsWindow = true;
            }
            if (_kit.Action("Keybinds", "Change"))
            {
                _keybindsWindow = true;
                _keybindScroll = Vector2.zero;
            }
            if (_kit.Action("Beginner Guide", "Start"))
            {
                OpenGuide();
            }
            DuckCamConfig.ConfirmExit.Value = _kit.Toggle("Ask Before Leaving", DuckCamConfig.ConfirmExit.Value);
            if (!string.IsNullOrEmpty(KeybindCapture.LastMessage))
            {
                _kit.Value("Last Keybind", KeybindCapture.LastMessage);
            }
        }

        public bool ModalOpen => _tabsWindow || _keybindsWindow || GuideOpen || KeybindCapture.IsOpen;

        public bool ModalPointerOver => ModalOpen;

        public void DrawWindows()
        {
            if (_tabsWindow)
            {
                DrawTabsWindow();
            }
            if (_keybindsWindow)
            {
                DrawKeybindsWindow();
            }
            if (GuideOpen)
            {
                DrawGuide();
            }
            KeybindCapture.Draw();
        }

        public void TickWindows()
        {
            if (KeybindCapture.IsOpen)
            {
                KeybindCapture.Tick();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_keybindsWindow)
                {
                    _keybindsWindow = false;
                }
                else if (_tabsWindow)
                {
                    _tabsWindow = false;
                }
            }
        }

        private static Rect Centered(float w, float h)
        {
            w = Mathf.Min(w, Screen.width - 40f);
            h = Mathf.Min(h, Screen.height - 40f);
            return new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        }

        private static void WindowFrame(Rect box, string title)
        {
            HudTheme.Ensure();
            if (Event.current.type == EventType.Repaint)
            {
                HudTheme.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.35f));
                HudTheme.Fill(box, HudTheme.P.TabFill);
                HudTheme.Border(box, HudTheme.P.Highlight, 2f);
            }
            GUI.Label(new Rect(box.x + 20f, box.y + 12f, box.width - 90f, 32f), Locale.T(title), HudTheme.Title);
        }

        private bool _tabsWindow;

        private void DrawTabsWindow()
        {
            int rows = _allTabs.Count;
            Rect box = Centered(520f, 120f + rows * 30f + 60f);
            WindowFrame(box, "Visible Tabs");
            if (SpawnerWindow.Button(new Rect(box.xMax - 44f, box.y + 10f, 30f, 28f), "X", false))
            {
                _tabsWindow = false;
                return;
            }
            HashSet<string> hidden = HiddenSet();
            float y = box.y + 52f;
            GUI.Label(new Rect(box.x + 20f, y, box.width - 40f, 22f), Locale.T("Click a tab to show or hide it. FRAME always stays visible."), HudTheme.Note);
            y += 28f;
            bool changed = false;
            foreach (TabDef t in _allTabs)
            {
                bool locked = AlwaysShown.Contains(t.Icon);
                bool shown = locked || !hidden.Contains(t.Icon);
                var r = new Rect(box.x + 20f, y, box.width - 40f, 26f);
                if (SpawnerWindow.Button(r, (shown ? "[ON]  " : "[OFF] ") + Locale.T(t.Title), shown) && !locked)
                {
                    if (shown)
                    {
                        hidden.Add(t.Icon);
                    }
                    else
                    {
                        hidden.Remove(t.Icon);
                    }
                    changed = true;
                }
                y += 30f;
            }
            if (changed)
            {
                SetHidden(hidden);
            }
            float bw = (box.width - 52f) / 3f;
            float by = box.yMax - 46f;
            if (SpawnerWindow.Button(new Rect(box.x + 20f, by, bw, 32f), "SHOW ALL", false))
            {
                SetHidden(Array.Empty<string>());
            }
            else if (SpawnerWindow.Button(new Rect(box.x + 26f + bw, by, bw, 32f), "RUN GUIDE", false))
            {
                _tabsWindow = false;
                OpenGuide();
            }
            else if (SpawnerWindow.Button(new Rect(box.x + 32f + bw * 2f, by, bw, 32f), "DONE", true))
            {
                _tabsWindow = false;
            }
        }

        private bool _keybindsWindow;
        private Vector2 _keybindScroll;

        private void DrawKeybindsWindow()
        {
            Rect box = Centered(760f, Screen.height * 0.8f);
            WindowFrame(box, "Keybinds");
            if (SpawnerWindow.Button(new Rect(box.xMax - 44f, box.y + 10f, 30f, 28f), "X", false))
            {
                _keybindsWindow = false;
                return;
            }
            GUI.Label(new Rect(box.x + 20f, box.y + 48f, box.width - 40f, 22f), Locale.T("Click a key to change it. Esc closes this window."), HudTheme.Note);
            List<KeyValuePair<ConfigDefinition, ConfigEntryBase>> keys = DuckCamConfig.File == null
                ? new List<KeyValuePair<ConfigDefinition, ConfigEntryBase>>()
                : DuckCamConfig.File.Where(kv => kv.Value is ConfigEntry<KeyboardShortcut>).OrderBy(kv => kv.Key.Section).ThenBy(kv => kv.Key.Key).ToList();
            var view = new Rect(box.x + 14f, box.y + 76f, box.width - 28f, box.height - 90f);
            float rowH = 28f;
            int sections = keys.Select(k => k.Key.Section).Distinct().Count();
            var content = new Rect(0f, 0f, view.width - 20f, keys.Count * rowH + sections * 26f);
            _keybindScroll = GUI.BeginScrollView(view, _keybindScroll, content);
            float y = 0f;
            string section = null;
            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> kv in keys)
            {
                if (kv.Key.Section != section)
                {
                    section = kv.Key.Section;
                    GUI.Label(new Rect(4f, y + 2f, content.width, 22f), "// " + Locale.T(section).ToUpperInvariant(), HudTheme.Section);
                    y += 26f;
                }
                var entry = (ConfigEntry<KeyboardShortcut>)kv.Value;
                GUI.Label(new Rect(8f, y, content.width * 0.62f, rowH), Locale.T(kv.Key.Key), HudTheme.Value);
                if (SpawnerWindow.Button(new Rect(content.width * 0.64f, y + 2f, content.width * 0.36f - 6f, rowH - 4f), KeybindCapture.Pretty(entry.Value), false))
                {
                    KeybindCapture.Begin(entry);
                }
                y += rowH;
            }
            GUI.EndScrollView();
        }

        private static readonly (string Name, string Detail, string[] Tabs)[] GuideUses =
        {
            ("Screenshots and looks", "Camera, depth of field, effects, lights, environment, overlays, presets and capture.",
                new[] { "camera", "dof", "effects", "lights", "environment", "overlays", "frame", "presets", "capture" }),
            ("Posing characters", "Characters, outfit and gear, the pose editor and saved poses.",
                new[] { "characters", "outfit", "presets", "lights" }),
            ("Videos and camera moves", "Camera paths, recording, the animation timeline and camera motion.",
                new[] { "paths", "motion", "capture", "camera" }),
            ("Scenes with bots", "Bot spawner, photo-only bots and freezing bots.",
                new[] { "characters", "outfit" })
        };

        private static readonly string[] GuideAlways = { "camera", "time", "frame", "capture" };

        private bool _guideOpen;
        private int _guideStep;
        private readonly bool[] _guidePicks = new bool[GuideUses.Length];

        public bool GuideOpen => _guideOpen;

        public void OpenGuide()
        {
            _guideOpen = true;
            _guideStep = 0;
            _tabsWindow = false;
            _keybindsWindow = false;
        }

        private void FinishGuide(bool apply)
        {
            if (apply && _guidePicks.Any(p => p))
            {
                var keep = new HashSet<string>(GuideAlways);
                for (int i = 0; i < GuideUses.Length; i++)
                {
                    if (_guidePicks[i])
                    {
                        keep.UnionWith(GuideUses[i].Tabs);
                    }
                }
                SetHidden(_allTabs.Select(t => t.Icon).Where(i => !keep.Contains(i)));
            }
            DuckCamConfig.GuideDone.Value = true;
            _guideOpen = false;
        }

        private void DrawGuide()
        {
            Rect box = Centered(720f, 470f);
            WindowFrame(box, "Beginner Guide");
            GUI.Label(new Rect(box.xMax - 120f, box.y + 18f, 100f, 22f), $"{_guideStep + 1} / 4", HudTheme.Note);
            float x = box.x + 24f;
            float y = box.y + 56f;
            float w = box.width - 48f;
            switch (_guideStep)
            {
                case 0:
                    GUI.Label(new Rect(x, y, w, 220f), Locale.T(
                        "Welcome to DuckCam!\n\n"
                        + "DuckCam turns the raid into a photo studio: freeze time, fly the camera anywhere, pose characters, change the light and weather, and save photos or videos.\n\n"
                        + "The raid is frozen while this guide is open, so nothing can hurt you.\n\n"
                        + "DuckCam has a lot of features. The next page asks what you want to do, so it can hide the tabs you won't need. You can change that any time."), HudTheme.Note);
                    break;
                case 1:
                    GUI.Label(new Rect(x, y, w, 24f), Locale.T("What do you want to do with DuckCam? Pick everything that applies."), HudTheme.Value);
                    y += 34f;
                    for (int i = 0; i < GuideUses.Length; i++)
                    {
                        var r = new Rect(x, y, w, 52f);
                        if (SpawnerWindow.Button(r, "", _guidePicks[i]))
                        {
                            _guidePicks[i] = !_guidePicks[i];
                        }
                        GUI.Label(new Rect(r.x + 14f, r.y + 4f, r.width - 28f, 22f), (_guidePicks[i] ? "[X] " : "[  ] ") + Locale.T(GuideUses[i].Name), _guidePicks[i] ? HudTheme.BoxTextHi : HudTheme.BoxText);
                        GUI.Label(new Rect(r.x + 44f, r.y + 26f, r.width - 58f, 22f), Locale.T(GuideUses[i].Detail), HudTheme.Note);
                        y += 60f;
                    }
                    GUI.Label(new Rect(x, y, w, 22f), Locale.T("Pick nothing to keep every tab."), HudTheme.Note);
                    break;
                case 2:
                    GUI.Label(new Rect(x, y, w, 300f), Locale.T("The basics:") + "\n\n"
                        + "  " + KeyName(DuckCamConfig.ToggleKey) + "  " + Locale.T("freeze the raid and open DuckCam (press again to leave)") + "\n"
                        + "  " + KeyName(DuckCamConfig.UnsnapKey) + "  " + Locale.T("open DuckCam with the raid still running") + "\n"
                        + "  WASD / F R  " + Locale.T("fly the camera, down and up") + "\n"
                        + "  " + Locale.T("Right mouse (hold)") + "  " + Locale.T("look around") + "\n"
                        + "  " + KeyName(DuckCamConfig.UiCategoryPrev) + " / " + KeyName(DuckCamConfig.UiCategoryNext) + "  " + Locale.T("switch tabs") + "\n"
                        + "  " + KeyName(DuckCamConfig.UiHide) + "  " + Locale.T("show or hide the menu") + "\n"
                        + "  " + KeyName(DuckCamConfig.ScreenshotKey) + "  " + Locale.T("take a photo") + "\n"
                        + "  " + KeyName(DuckCamConfig.UiExit) + "  " + Locale.T("leave DuckCam") + "\n\n"
                        + Locale.T("Every tab starts with a How-To row. Change any key in FRAME > Interface > Keybinds."), HudTheme.Note);
                    break;
                default:
                    var keep = new HashSet<string>(GuideAlways);
                    for (int i = 0; i < GuideUses.Length; i++)
                    {
                        if (_guidePicks[i])
                        {
                            keep.UnionWith(GuideUses[i].Tabs);
                        }
                    }
                    bool any = _guidePicks.Any(p => p);
                    string shown = string.Join(", ", _allTabs.Where(t => !any || keep.Contains(t.Icon) || AlwaysShown.Contains(t.Icon)).Select(t => Locale.T(t.Title)));
                    string hiddenList = any ? string.Join(", ", _allTabs.Where(t => !keep.Contains(t.Icon) && !AlwaysShown.Contains(t.Icon)).Select(t => Locale.T(t.Title))) : "";
                    GUI.Label(new Rect(x, y, w, 300f), Locale.T("You're ready.") + "\n\n"
                        + Locale.T("Tabs you'll see") + ": " + shown + "\n\n"
                        + (hiddenList.Length > 0 ? Locale.T("Hidden for now") + ": " + hiddenList + "\n\n" : "")
                        + Locale.T("Bring hidden tabs back, or run this guide again, in FRAME > Interface > Tabs."), HudTheme.Note);
                    break;
            }
            float bw = (w - 18f) / 3f;
            float by = box.yMax - 50f;
            if (SpawnerWindow.Button(new Rect(x, by, bw, 34f), "SKIP GUIDE", false))
            {
                FinishGuide(false);
            }
            else if (_guideStep > 0 && SpawnerWindow.Button(new Rect(x + bw + 9f, by, bw, 34f), "BACK", false))
            {
                _guideStep--;
            }
            else if (SpawnerWindow.Button(new Rect(x + (bw + 9f) * 2f, by, bw, 34f), _guideStep < 3 ? "NEXT" : "FINISH", true))
            {
                if (_guideStep < 3)
                {
                    _guideStep++;
                }
                else
                {
                    FinishGuide(true);
                }
            }
        }
    }
}
