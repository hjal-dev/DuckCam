using System;
using System.Collections.Generic;
using System.Linq;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed class ThemeEditorWindow
    {
        private const float W = 1520f;
        private const float H = 860f;
        private const float RowH = 30f;
        private const int Primary = -2;
        private const int Accent = -1;

        private static readonly UiPalette[] Bases = { UiPalette.NeonRed, UiPalette.Acid, UiPalette.Ice, UiPalette.Tarkov };
        private static readonly string[] BaseNames = { "NEON RED", "ACID", "ICE", "TARKOV" };

        private readonly ColorPicker _picker = new ColorPicker("ThemePicker");
        private HudPalette _work;
        private UiPalette _base;
        private Color _primary;
        private Color _accent;
        private int _selected = Primary;
        private string _name = "My theme";
        private string _message = "";
        private string _confirmDelete;
        private string[] _themes = new string[0];
        private float _listScroll;
        private Rect _screenRect;
        private int _lastDrawFrame;
        private bool _dirty;

        public bool Open { get; private set; }

        public bool PointerOver
        {
            get
            {
                if (!Open || Time.frameCount - _lastDrawFrame > 2)
                {
                    return false;
                }
                Vector3 m = Input.mousePosition;
                return _screenRect.Contains(new Vector2(m.x, Screen.height - m.y));
            }
        }

        public void Show()
        {
            Open = true;
            HudTheme.Ensure();
            string custom = DuckCamConfig.CustomTheme.Value ?? "";
            ThemeFile file = custom.Length > 0 ? Themes.Load(custom) : null;
            _base = file != null ? Themes.BaseOf(file) : DuckCamConfig.Palette.Value;
            _work = HudTheme.P.Clone();
            _name = file != null ? ContentPacks.NameOf(custom) : "My theme";
            _themes = Themes.List();
            _dirty = false;
            TakeQuick();
            Select(Primary);
            HudTheme.SetPreview(_work);
            _message = "Pick a colour on the left. Primary and Accent recolour everything at once; the list below them changes single colours.";
        }

        public void Close()
        {
            Open = false;
            HudTheme.SetPreview(null);
            InputContext.LeaveField();
            _picker.Dispose();
        }

        public void Back() => Close();

        public void Scroll(float notches)
        {
            _listScroll = Mathf.Max(0f, _listScroll - notches * 40f);
        }

        private void TakeQuick()
        {
            _primary = _work.Label;
            _accent = _work.Highlight;
        }

        private void Select(int index)
        {
            _selected = index;
            _picker.SetColor(Current());
        }

        private Color Current()
        {
            if (_selected == Primary)
            {
                return _primary;
            }
            if (_selected == Accent)
            {
                return _accent;
            }
            return Themes.Roles[_selected].Get(_work);
        }

        private void Apply(Color c)
        {
            if (_selected == Primary || _selected == Accent)
            {
                if (_selected == Primary)
                {
                    _primary = c;
                }
                else
                {
                    _accent = c;
                }
                Themes.Derive(_work, _primary, _accent);
            }
            else
            {
                Themes.Roles[_selected].Set(_work, c);
            }
            Changed();
        }

        private void Changed()
        {
            _dirty = true;
            HudTheme.SetPreview(_work);
        }

        public void Draw(float vw, float vh)
        {
            if (!Open)
            {
                return;
            }
            _lastDrawFrame = Time.frameCount;
            var win = new Rect(Mathf.Round((vw - W) * 0.5f), Mathf.Round((vh - H) * 0.5f), W, H);
            Vector2 tl = GUIUtility.GUIToScreenPoint(new Vector2(win.x, win.y));
            Vector2 br = GUIUtility.GUIToScreenPoint(new Vector2(win.xMax, win.yMax));
            _screenRect = new Rect(tl, br - tl);

            HudTheme.Window(win);
            HudTheme.Border(win, HudTheme.P.LineBright);
            GUI.Label(new Rect(win.x + 16f, win.y + 8f, 600f, 34f), "THEME EDITOR" + (_dirty ? " *" : ""), SpawnerWindow.Left(HudTheme.Title, wrap: false));
            if (SpawnerWindow.Button(new Rect(win.xMax - 42f, win.y + 8f, 32f, 30f), "X", false))
            {
                Close();
                return;
            }

            float top = win.y + 52f;
            float bottom = win.yMax - 58f;
            DrawColours(new Rect(win.x + 14f, top, 500f, bottom - top));
            DrawPicker(new Rect(win.x + 528f, top, 420f, bottom - top));
            DrawThemes(new Rect(win.x + 962f, top, win.width - 976f, bottom - top));

            var bar = new Rect(win.x + 14f, bottom + 8f, win.width - 28f, 42f);
            HudTheme.Fill(bar, HudTheme.P.TabFill);
            GUI.Label(new Rect(bar.x + 12f, bar.y + 2f, bar.width - 24f, 20f), _message ?? "", SpawnerWindow.Left(HudTheme.Note, wrap: false));
            GUI.Label(new Rect(bar.x + 12f, bar.y + 20f, bar.width - 24f, 20f), "Changes show live. Closing without SAVE THEME goes back to the theme you had. Esc or X closes.", SpawnerWindow.Left(HudTheme.Note, wrap: false));
        }

        private void DrawColours(Rect r)
        {
            HudTheme.Fill(r, HudTheme.P.RowFill);
            float x = r.x + 8f;
            float w = r.width - 16f;
            float y = r.y + 6f;
            GUI.Label(new Rect(x, y, w, 22f), "QUICK", SpawnerWindow.Left(HudTheme.Section, wrap: false));
            y += 26f;
            y = ColourRow(x, y, w, "PRIMARY", _primary, Primary);
            y = ColourRow(x, y, w, "ACCENT", _accent, Accent);
            y += 6f;
            GUI.Label(new Rect(x, y, w, 22f), "EVERY COLOUR", SpawnerWindow.Left(HudTheme.Section, wrap: false));
            y += 26f;
            var list = new Rect(x, y, w, r.yMax - y - 6f);
            float content = Themes.Roles.Length * (RowH + 2f);
            _listScroll = Mathf.Clamp(_listScroll, 0f, Mathf.Max(0f, content - list.height));
            GUI.BeginGroup(list);
            float ry = -_listScroll;
            for (int i = 0; i < Themes.Roles.Length; i++)
            {
                if (ry + RowH >= 0f && ry <= list.height)
                {
                    ColourRow(0f, ry, list.width, Themes.Roles[i].Name.ToUpperInvariant(), Themes.Roles[i].Get(_work), i);
                }
                ry += RowH + 2f;
            }
            GUI.EndGroup();
        }

        private float ColourRow(float x, float y, float w, string name, Color c, int index)
        {
            var row = new Rect(x, y, w, RowH);
            bool selected = _selected == index;
            bool hover = row.Contains(Event.current.mousePosition);
            if (Event.current.type == EventType.Repaint)
            {
                HudTheme.Fill(row, selected ? HudTheme.P.RowFillSelected : hover ? HudTheme.P.TabFillActive : HudTheme.P.TabFill);
                if (selected)
                {
                    HudTheme.Fill(new Rect(row.x, row.y, 3f, row.height), HudTheme.P.Highlight);
                }
            }
            ColorPicker.Swatch(new Rect(row.x + 10f, row.y + 5f, 34f, row.height - 10f), c);
            GUI.Label(new Rect(row.x + 54f, row.y, row.width - 190f, row.height), name, SpawnerWindow.Left(selected ? HudTheme.ValueSelected : HudTheme.Value, wrap: false));
            GUI.Label(new Rect(row.xMax - 132f, row.y, 122f, row.height), ThemeColors.ToHex(c), RightNote);
            if (GUI.Button(row, GUIContent.none, GUIStyle.none))
            {
                Select(index);
            }
            return y + RowH + 2f;
        }

        private static GUIStyle _rightNote;

        private static GUIStyle RightNote => _rightNote ?? (_rightNote = new GUIStyle(SpawnerWindow.Left(HudTheme.Note, wrap: false)) { alignment = TextAnchor.MiddleRight });

        private void DrawPicker(Rect r)
        {
            HudTheme.Fill(r, HudTheme.P.RowFill);
            float x = r.x + 12f;
            float w = r.width - 24f;
            float y = r.y + 8f;
            string title = _selected == Primary ? "PRIMARY" : _selected == Accent ? "ACCENT" : Themes.Roles[_selected].Name.ToUpperInvariant();
            GUI.Label(new Rect(x, y, w, 24f), title, SpawnerWindow.Left(HudTheme.Section, wrap: false));
            y += 30f;
            var swatches = new List<Color>();
            foreach (UiPalette b in Bases)
            {
                HudPalette p = HudTheme.PaletteFor(b);
                swatches.Add(_selected == Accent ? p.Highlight : _selected == Primary ? p.Label : Themes.Roles[_selected].Get(p));
            }
            swatches.Add(Color.white);
            swatches.Add(Color.black);
            var area = new Rect(x, y, w, ColorPicker.Height(w));
            if (_picker.Draw(area, swatches))
            {
                Apply(_picker.Color);
            }
            y = area.yMax + 12f;
            bool window = _selected >= 0 && Themes.Roles[_selected].Id.StartsWith("window", StringComparison.Ordinal);
            if (window)
            {
                GUI.Label(new Rect(x, y, 120f, 24f), "ANGLE", SpawnerWindow.Left(HudTheme.Label, wrap: false));
                float angle = GUI.HorizontalSlider(new Rect(x + 90f, y + 6f, w - 160f, 16f), _work.GradientAngle, 0f, 360f);
                GUI.Label(new Rect(x + w - 64f, y, 64f, 24f), Mathf.RoundToInt(angle) + " deg", RightNote);
                if (Mathf.Abs(angle - _work.GradientAngle) > 0.5f)
                {
                    _work.GradientAngle = Mathf.Round(angle / 5f) * 5f;
                    Changed();
                }
                y += 32f;
                if (SpawnerWindow.Button(new Rect(x, y, w, 28f), "MAKE BOTH THE SAME (NO GRADIENT)", false))
                {
                    Color c = Themes.Roles[_selected].Get(_work);
                    _work.WindowStart = c;
                    _work.WindowEnd = c;
                    Changed();
                }
                y += 34f;
                Note(x, y, w, "Two different colours make a gradient, like CSS linear-gradient. 0 deg runs bottom to top, 90 deg left to right.");
            }
            else if (_selected < 0)
            {
                Note(x, y, w, "Primary sets titles, text, lines and backgrounds; Accent sets highlights and key hints. Every other colour follows from these two until you change it in the list.");
            }
        }

        private static void Note(float x, float y, float w, string text)
        {
            GUIStyle style = SpawnerWindow.Left(HudTheme.Note, wrap: true);
            var content = new GUIContent(text);
            GUI.Label(new Rect(x, y, w, style.CalcHeight(content, w) + 4f), content, style);
        }

        private void DrawThemes(Rect r)
        {
            HudTheme.Fill(r, HudTheme.P.RowFill);
            float x = r.x + 10f;
            float w = r.width - 20f;
            float y = r.y + 8f;
            GUI.Label(new Rect(x, y, w, 22f), "START FROM", SpawnerWindow.Left(HudTheme.Section, wrap: false));
            y += 26f;
            float bw = (w - 18f) / 4f;
            for (int i = 0; i < Bases.Length; i++)
            {
                if (SpawnerWindow.Button(new Rect(x + i * (bw + 6f), y, bw, 28f), BaseNames[i], _base == Bases[i]))
                {
                    _base = Bases[i];
                    _work = HudTheme.PaletteFor(_base);
                    TakeQuick();
                    Select(_selected);
                    Changed();
                    _message = $"Started from {BaseNames[i]}. Change any colour on the left.";
                }
            }
            y += 40f;
            GUI.Label(new Rect(x, y, w, 22f), "SAVE", SpawnerWindow.Left(HudTheme.Section, wrap: false));
            y += 26f;
            GUI.SetNextControlName(HudKit.FieldPrefix + "ThemeName");
            _name = GUI.TextField(new Rect(x, y, w - 160f, 30f), _name ?? "", 40, HudTheme.Field);
            if (SpawnerWindow.Button(new Rect(x + w - 154f, y, 154f, 30f), "SAVE THEME", true))
            {
                try
                {
                    string key = Themes.Save(Themes.Capture(_work, _base, _name), _name);
                    DuckCamConfig.CustomTheme.Value = key;
                    HudTheme.ReloadCustom();
                    _themes = Themes.List();
                    _dirty = false;
                    _message = $"Saved '{ContentPacks.Display(key)}' and switched to it. It is a file in DuckCam/Themes, so it can go into an add-on pack.";
                }
                catch (Exception ex)
                {
                    _message = "Saving failed: " + ex.Message;
                }
            }
            y += 38f;
            float hw = (w - 6f) * 0.5f;
            if (SpawnerWindow.Button(new Rect(x, y, hw, 28f), "COPY AS CSS", false))
            {
                GUIUtility.systemCopyBuffer = Themes.ToCss(Themes.Capture(_work, _base, _name));
                _message = "Theme copied as CSS variables. Paste it anywhere to share it.";
            }
            if (SpawnerWindow.Button(new Rect(x + hw + 6f, y, hw, 28f), "PASTE CSS", false))
            {
                ThemeFile file = Themes.FromCss(GUIUtility.systemCopyBuffer, out int read);
                if (read == 0)
                {
                    _message = "No theme found in the clipboard. Copy text with lines like --accent: #5FF5FF; first.";
                }
                else
                {
                    _base = Themes.BaseOf(file);
                    _work = Themes.Build(file);
                    TakeQuick();
                    Select(_selected);
                    Changed();
                    _message = $"Pasted {read} value(s). SAVE THEME to keep it.";
                }
            }
            y += 40f;

            GUI.Label(new Rect(x, y, w, 22f), "SAVED THEMES", SpawnerWindow.Left(HudTheme.Section, wrap: false));
            y += 26f;
            string active = DuckCamConfig.CustomTheme.Value ?? "";
            if (SpawnerWindow.Button(new Rect(x, y, w, 28f), "USE THE COLOUR SCHEME ONLY", active.Length == 0))
            {
                DuckCamConfig.CustomTheme.Value = "";
                HudTheme.ReloadCustom();
                _base = DuckCamConfig.Palette.Value;
                _work = HudTheme.PaletteFor(_base);
                TakeQuick();
                Select(_selected);
                Changed();
                _dirty = false;
                _message = "Back to the plain colour scheme (FRAME > Interface > Color Scheme).";
            }
            y += 34f;
            var list = new Rect(x, y, w, r.yMax - y - 220f);
            if (_themes.Length == 0)
            {
                GUI.Label(list, "No saved themes yet.", SpawnerWindow.Left(HudTheme.Note, wrap: true));
            }
            GUI.BeginGroup(list);
            float ry = 0f;
            foreach (string key in _themes)
            {
                var row = new Rect(0f, ry, list.width, 30f);
                ry += 34f;
                if (row.y > list.height)
                {
                    break;
                }
                bool current = key == active;
                HudTheme.Fill(row, current ? HudTheme.P.RowFillSelected : HudTheme.P.TabFill);
                GUI.Label(new Rect(row.x + 10f, row.y, row.width - 200f, row.height), ContentPacks.Display(key), SpawnerWindow.Left(current ? HudTheme.ValueSelected : HudTheme.Value, wrap: false));
                if (SpawnerWindow.Button(new Rect(row.xMax - 186f, row.y + 2f, 90f, 26f), "USE", current))
                {
                    ThemeFile file = Themes.Load(key);
                    if (file != null)
                    {
                        DuckCamConfig.CustomTheme.Value = key;
                        HudTheme.ReloadCustom();
                        _base = Themes.BaseOf(file);
                        _work = Themes.Build(file);
                        _name = ContentPacks.NameOf(key);
                        TakeQuick();
                        Select(_selected);
                        Changed();
                        _dirty = false;
                        _message = $"Using '{ContentPacks.Display(key)}'.";
                    }
                }
                bool confirming = _confirmDelete == key;
                if (SpawnerWindow.Button(new Rect(row.xMax - 90f, row.y + 2f, 88f, 26f), confirming ? "SURE?" : "DELETE", confirming))
                {
                    if (confirming)
                    {
                        Themes.Delete(key);
                        if (key == active)
                        {
                            DuckCamConfig.CustomTheme.Value = "";
                            HudTheme.ReloadCustom();
                        }
                        _confirmDelete = null;
                        _themes = Themes.List();
                        _message = $"Theme '{ContentPacks.Display(key)}' deleted.";
                    }
                    else
                    {
                        _confirmDelete = key;
                    }
                }
            }
            GUI.EndGroup();
            DrawSample(new Rect(x, r.yMax - 210f, w, 200f));
        }

        private static void DrawSample(Rect r)
        {
            GUI.Label(new Rect(r.x, r.y, r.width, 22f), "PREVIEW", SpawnerWindow.Left(HudTheme.Section, wrap: false));
            var box = new Rect(r.x, r.y + 26f, r.width, r.height - 26f);
            HudTheme.Window(box);
            HudTheme.Border(box, HudTheme.P.LineBright);
            float x = box.x + 10f;
            float w = box.width - 20f;
            float y = box.y + 10f;
            var row = new Rect(x, y, w, 30f);
            HudTheme.Fill(row, HudTheme.P.RowFill);
            GUI.Label(new Rect(row.x + 10f, row.y, 160f, row.height), "FIELD OF VIEW", HudTheme.Label);
            var value = new Rect(row.xMax - 90f, row.y + 4f, 80f, row.height - 8f);
            HudTheme.Fill(value, HudTheme.P.ValueBox);
            GUI.Label(value, "45", HudTheme.BoxText);
            y += 36f;
            row = new Rect(x, y, w, 30f);
            HudTheme.Fill(row, HudTheme.P.RowFillSelected);
            HudTheme.Border(row, HudTheme.P.LineBright);
            GUI.Label(new Rect(row.x + 10f, row.y, 160f, row.height), "SELECTED ROW", HudTheme.LabelSelected);
            GUI.Label(new Rect(row.xMax - 170f, row.y, 160f, row.height), "Value", HudTheme.ValueSelected);
            y += 40f;
            float bw = (w - 12f) / 3f;
            var a = new Rect(x, y, bw, 30f);
            HudTheme.Fill(a, HudTheme.P.TabFill);
            HudTheme.Border(a, HudTheme.P.Line);
            GUI.Label(a, "BUTTON", HudTheme.BoxText);
            var b = new Rect(x + bw + 6f, y, bw, 30f);
            HudTheme.Fill(b, HudTheme.P.TabFillActive);
            HudTheme.Border(b, HudTheme.P.Line);
            GUI.Label(b, "HOVER", HudTheme.BoxText);
            var c = new Rect(x + (bw + 6f) * 2f, y, bw, 30f);
            HudTheme.Fill(c, HudTheme.P.Highlight);
            GUI.Label(c, "ACCENT", HudTheme.BoxTextHi);
            y += 40f;
            HudTheme.Fill(new Rect(x, y, 64f, 34f), HudTheme.P.Card);
            HudTheme.Border(new Rect(x, y, 64f, 34f), HudTheme.P.Line);
            GUI.Label(new Rect(x + 74f, y, w - 74f, 34f), "Faded note text and a card.", SpawnerWindow.Left(HudTheme.Note, wrap: false));
        }
    }
}
