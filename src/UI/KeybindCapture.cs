using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.UI
{
    internal static class KeybindCapture
    {
        private static readonly KeyCode[] Modifiers =
        {
            KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftAlt, KeyCode.RightAlt
        };

        private static readonly KeyCode[] Candidates = ((KeyCode[])Enum.GetValues(typeof(KeyCode)))
            .Where(k => k != KeyCode.None && k != KeyCode.Escape && k != KeyCode.Return && k != KeyCode.KeypadEnter
                        && !Modifiers.Contains(k) && !k.ToString().StartsWith("Mouse", StringComparison.Ordinal)
                        && !k.ToString().StartsWith("Joystick", StringComparison.Ordinal))
            .Distinct()
            .ToArray();

        private static ConfigEntry<KeyboardShortcut> _target;
        private static KeyboardShortcut? _candidate;
        private static int _openedFrame;
        private static List<string> _conflicts = new List<string>();
        private static Dictionary<string, string[]> _otherConfigs;

        public static bool IsOpen => _target != null;

        public static string LastMessage { get; private set; }

        public static void Begin(ConfigEntry<KeyboardShortcut> entry)
        {
            if (entry == null)
            {
                return;
            }
            _target = entry;
            _candidate = null;
            _conflicts.Clear();
            _openedFrame = Time.frameCount;
            _otherConfigs = null;
        }

        public static void Cancel()
        {
            _target = null;
            _candidate = null;
            _conflicts.Clear();
        }

        public static void Tick()
        {
            if (_target == null || Time.frameCount <= _openedFrame + 1)
            {
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cancel();
                return;
            }
            if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) && _candidate.HasValue)
            {
                Save();
                return;
            }
            foreach (KeyCode k in Candidates)
            {
                if (!Input.GetKeyDown(k))
                {
                    continue;
                }
                KeyCode[] held = Modifiers.Where(Input.GetKey).ToArray();
                _candidate = new KeyboardShortcut(k, held);
                FindConflicts(_candidate.Value);
                return;
            }
        }

        private static void Save()
        {
            if (_target == null || !_candidate.HasValue)
            {
                return;
            }
            _target.Value = _candidate.Value;
            LastMessage = $"{_target.Definition.Key}: {Pretty(_candidate.Value)}";
            Cancel();
        }

        private static void Unbind()
        {
            if (_target == null)
            {
                return;
            }
            _target.Value = KeyboardShortcut.Empty;
            LastMessage = $"{_target.Definition.Key}: unbound";
            Cancel();
        }

        private static void ResetDefault()
        {
            if (_target == null)
            {
                return;
            }
            _target.Value = (KeyboardShortcut)_target.DefaultValue;
            LastMessage = $"{_target.Definition.Key}: {Pretty(_target.Value)} (default)";
            Cancel();
        }

        public static string Pretty(KeyboardShortcut s)
        {
            if (s.MainKey == KeyCode.None)
            {
                return "-";
            }
            return string.Join("+", s.Modifiers.Select(m => m.ToString().Replace("Left", "").Replace("Right", "R")).Concat(new[] { s.MainKey.ToString() }));
        }

        private static bool Same(KeyboardShortcut a, KeyboardShortcut b)
        {
            return a.MainKey == b.MainKey && new HashSet<KeyCode>(a.Modifiers).SetEquals(b.Modifiers);
        }

        private static void FindConflicts(KeyboardShortcut s)
        {
            _conflicts.Clear();
            if (DuckCamConfig.File != null)
            {
                foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> kv in DuckCamConfig.File)
                {
                    if (kv.Value is ConfigEntry<KeyboardShortcut> other && other != _target && other.Value.MainKey != KeyCode.None && Same(other.Value, s))
                    {
                        _conflicts.Add("DuckCam: " + Locale.T(kv.Key.Key));
                    }
                }
            }
            string text = TomlTypeConverter.ConvertToString(s, typeof(KeyboardShortcut));
            foreach (KeyValuePair<string, string[]> cfg in OtherConfigs())
            {
                foreach (string line in cfg.Value)
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (string.Equals(line.Substring(eq + 1).Trim(), text, StringComparison.OrdinalIgnoreCase))
                    {
                        _conflicts.Add(cfg.Key + ": " + line.Substring(0, eq).Trim());
                    }
                }
            }
        }

        private static Dictionary<string, string[]> OtherConfigs()
        {
            if (_otherConfigs != null)
            {
                return _otherConfigs;
            }
            _otherConfigs = new Dictionary<string, string[]>();
            try
            {
                string own = DuckCamConfig.File != null ? Path.GetFullPath(DuckCamConfig.File.ConfigFilePath) : "";
                foreach (string file in Directory.GetFiles(BepInEx.Paths.ConfigPath, "*.cfg"))
                {
                    if (string.Equals(Path.GetFullPath(file), own, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    _otherConfigs[Path.GetFileNameWithoutExtension(file)] = File.ReadAllLines(file);
                }
            }
            catch (Exception ex)
            {
                PhotoLog.WarnOnce("keybind-scan", "Keybind conflict scan failed: " + ex.Message);
            }
            return _otherConfigs;
        }

        public static void Draw()
        {
            if (_target == null)
            {
                return;
            }
            HudTheme.Ensure();
            float w = 560f;
            float h = 250f + Mathf.Min(6, _conflicts.Count) * 20f;
            var box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            if (Event.current.type == EventType.Repaint)
            {
                HudTheme.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.35f));
                HudTheme.Fill(box, HudTheme.P.TabFill);
                HudTheme.Border(box, HudTheme.P.Highlight, 2f);
            }
            float x = box.x + 20f;
            float y = box.y + 14f;
            GUI.Label(new Rect(x, y, w - 40f, 32f), Locale.T("Change Keybind"), HudTheme.Title);
            y += 38f;
            GUI.Label(new Rect(x, y, w - 40f, 22f), Locale.T(_target.Definition.Key), HudTheme.Value);
            y += 26f;
            GUI.Label(new Rect(x, y, w - 40f, 22f), Locale.T("Current") + ": " + Pretty(_target.Value), HudTheme.Note);
            y += 22f;
            string prompt = _candidate.HasValue
                ? Locale.T("New") + ": " + Pretty(_candidate.Value) + "   (" + Locale.T("Enter or SAVE to keep it, press another key to change it") + ")"
                : Locale.T("Press the new key now. Hold Ctrl, Shift or Alt for a combination. Esc cancels.");
            GUI.Label(new Rect(x, y, w - 40f, 40f), prompt, HudTheme.Note);
            y += 42f;
            if (_conflicts.Count > 0)
            {
                GUI.Label(new Rect(x, y, w - 40f, 20f), Locale.T("Already used by:"), HudTheme.Note);
                y += 20f;
                foreach (string c in _conflicts.Take(6))
                {
                    GUI.Label(new Rect(x + 10f, y, w - 50f, 20f), c, HudTheme.Note);
                    y += 20f;
                }
            }
            float bw = (w - 40f - 18f) / 4f;
            float by = box.yMax - 46f;
            if (SpawnerWindow.Button(new Rect(x, by, bw, 32f), "SAVE", _candidate.HasValue) && _candidate.HasValue)
            {
                Save();
            }
            else if (SpawnerWindow.Button(new Rect(x + (bw + 6f), by, bw, 32f), "UNBIND", false))
            {
                Unbind();
            }
            else if (SpawnerWindow.Button(new Rect(x + (bw + 6f) * 2f, by, bw, 32f), "DEFAULT", false))
            {
                ResetDefault();
            }
            else if (SpawnerWindow.Button(new Rect(x + (bw + 6f) * 3f, by, bw, 32f), "CANCEL", false))
            {
                Cancel();
            }
        }
    }
}
