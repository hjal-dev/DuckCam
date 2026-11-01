using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed class HudKit
    {
        public const string FieldPrefix = "DuckCam.Field.";
        public const float RowHeight = 40f;
        public const float RowGap = 4f;
        private const float LabelFraction = 0.44f;
        private const float ValueBoxWidth = 64f;

        private readonly List<Rect> _rects = new List<Rect>();
        private readonly Dictionary<int, int> _segmentFocus = new Dictionary<int, int>();
        private readonly Dictionary<string, string> _buffers = new Dictionary<string, string>();

        private int _row;
        private int _lastCount;
        private int _move;
        private int _step;
        private bool _activate;
        private Vector2 _prevMouse = new Vector2(-1f, -1f);
        private bool _mouseMoved;
        private bool _focused;

        public int Focus { get; private set; }
        public Rect FocusRect { get; private set; }
        public bool ScrollToFocus { get; set; }

        public void ResetFocus()
        {
            Focus = 0;
            ScrollToFocus = true;
            _buffers.Clear();
            _segmentFocus.Clear();
        }

        public void Queue(int move, int step, bool activate)
        {
            _move += move;
            _step += step;
            _activate |= activate;
        }

        public void BeginContent()
        {
            _row = 0;
            Event e = Event.current;
            if (e.type == EventType.Repaint)
            {
                _rects.Clear();
                Vector2 mouse = e.mousePosition;
                _mouseMoved = (mouse - _prevMouse).sqrMagnitude > 0.25f && !Input.GetMouseButton(1);
                _prevMouse = mouse;
            }
            if (e.type == EventType.Layout && _move != 0 && _lastCount > 0)
            {
                Focus = Mathf.Clamp(Focus + _move, 0, _lastCount - 1);
                _move = 0;
                ScrollToFocus = true;
            }
        }

        public void EndContent()
        {
            Event e = Event.current;
            if (e.type == EventType.Repaint)
            {
                _lastCount = _row;
                if (Focus >= _lastCount && _lastCount > 0)
                {
                    Focus = _lastCount - 1;
                }
            }
            if (e.type == EventType.Layout)
            {
                _step = 0;
                _activate = false;
            }
        }

        private Rect NextRow(float height = RowHeight)
        {
            Rect r = GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true));
            int index = _row++;
            Event e = Event.current;
            if (e.type == EventType.Repaint)
            {
                _rects.Add(r);
                if (_mouseMoved && r.Contains(e.mousePosition))
                {
                    Focus = index;
                }
            }
            else if (e.type == EventType.MouseDown && r.Contains(e.mousePosition))
            {
                Focus = index;
            }

            _focused = index == Focus;
            if (_focused && e.type == EventType.Repaint)
            {
                FocusRect = r;
            }
            if (e.type == EventType.Repaint)
            {
                HudTheme.Fill(r, _focused ? HudTheme.P.RowFillSelected : HudTheme.P.RowFill);
                if (_focused)
                {
                    HudTheme.SelectedFrame(r, HudTheme.P.LineBright);
                }
                else
                {
                    HudTheme.Border(r, HudTheme.P.Line);
                }
            }
            GUILayout.Space(RowGap);
            return r;
        }

        private bool KeyStep(out int step)
        {
            step = 0;
            if (_focused && Event.current.type == EventType.Layout && _step != 0)
            {
                step = _step;
                return true;
            }
            return false;
        }

        private bool KeyActivate() => _focused && Event.current.type == EventType.Layout && _activate;

        private void DrawLabel(Rect r, string label)
        {
            GUI.Label(new Rect(r.x + 14f, r.y, r.width * LabelFraction - 14f, r.height), Core.Locale.T(label).ToUpperInvariant(), _focused ? HudTheme.LabelSelected : HudTheme.Label);
        }

        private static Rect ControlRect(Rect r) => new Rect(r.x + r.width * LabelFraction, r.y, r.width * (1f - LabelFraction) - 14f, r.height);

        public void Section(string title)
        {
            GUILayout.Label("// " + Core.Locale.T(title).ToUpperInvariant(), HudTheme.Section);
        }

        public void Note(string text, bool mono = false)
        {
            GUILayout.Label(mono ? text : Core.Locale.T(text), mono ? HudTheme.NoteMono : HudTheme.Note);
        }

        public void Value(string label, string value)
        {
            Rect r = NextRow();
            DrawLabel(r, label);
            GUI.Label(ControlRect(r), Core.Locale.Show(value), _focused ? HudTheme.ValueSelected : HudTheme.Value);
        }

        public float Slider(string label, float value, float min, float max, float step, string format = "0.00", string suffix = "")
        {
            Rect r = NextRow();
            DrawLabel(r, label);
            Rect c = ControlRect(r);
            if (KeyStep(out int s))
            {
                value = Mathf.Clamp(value + s * step, min, max);
            }
            value = Track(c, value, min, max, value.ToString(format, CultureInfo.InvariantCulture) + suffix);
            return value;
        }

        public float LogSlider(string label, float value, float min, float max, string format = "0.0", string suffix = "")
        {
            Rect r = NextRow();
            DrawLabel(r, label);
            Rect c = ControlRect(r);
            float lv = Mathf.Log10(Mathf.Max(value, min));
            if (KeyStep(out int s))
            {
                lv = Mathf.Clamp(lv + s * 0.05f, Mathf.Log10(min), Mathf.Log10(max));
            }
            lv = Track(c, lv, Mathf.Log10(min), Mathf.Log10(max), value.ToString(format, CultureInfo.InvariantCulture) + suffix);
            return Mathf.Pow(10f, lv);
        }

        public int IntSlider(string label, int value, int min, int max, string suffix = "")
        {
            Rect r = NextRow();
            DrawLabel(r, label);
            Rect c = ControlRect(r);
            if (KeyStep(out int s))
            {
                value = Mathf.Clamp(value + s, min, max);
            }
            return Mathf.RoundToInt(Track(c, value, min, max, value.ToString(CultureInfo.InvariantCulture) + suffix));
        }

        private float Track(Rect c, float value, float min, float max, string text)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            float x0 = c.x + 8f + ValueBoxWidth * 0.5f;
            float x1 = c.xMax - 8f - ValueBoxWidth * 0.5f;
            Event e = Event.current;

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && c.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        value = FromX(e.mousePosition.x, x0, x1, min, max);
                        GUI.changed = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        value = FromX(e.mousePosition.x, x0, x1, min, max);
                        GUI.changed = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
                case EventType.Repaint:
                    float t = Mathf.Approximately(max, min) ? 0f : Mathf.Clamp01((value - min) / (max - min));
                    float cy = c.center.y;
                    HudTheme.Fill(new Rect(c.x + 8f, cy, c.width - 16f, 1f), HudTheme.P.Line);
                    float bx = Mathf.Lerp(x0, x1, t) - ValueBoxWidth * 0.5f;
                    var box = new Rect(bx, c.y + 4f, ValueBoxWidth, c.height - 8f);
                    bool hi = _focused || GUIUtility.hotControl == id;
                    HudTheme.Fill(box, hi ? HudTheme.P.Highlight : HudTheme.P.ValueBox);
                    GUI.Label(box, text, hi ? HudTheme.BoxTextHi : HudTheme.BoxText);
                    break;
            }
            return Mathf.Clamp(value, Mathf.Min(min, max), Mathf.Max(min, max));
        }

        private static float FromX(float x, float x0, float x1, float min, float max)
        {
            return Mathf.Lerp(min, max, Mathf.InverseLerp(x0, x1, x));
        }

        public int Selector(string label, string[] options, int index)
        {
            index = Mathf.Clamp(index, 0, options.Length - 1);
            Rect r = NextRow();
            DrawLabel(r, label);
            Rect c = ControlRect(r);
            if (KeyStep(out int s))
            {
                index = Wrap(index + s, options.Length);
            }

            float arrow = 20f;
            var left = new Rect(c.x + 10f, c.center.y - arrow * 0.5f, arrow, arrow);
            var right = new Rect(c.xMax - 10f - arrow, c.center.y - arrow * 0.5f, arrow, arrow);
            var mid = new Rect(left.xMax + 6f, c.y, right.x - left.xMax - 12f, c.height);

            if (GUI.Button(Expand(left, 8f), GUIContent.none, GUIStyle.none))
            {
                index = Wrap(index - 1, options.Length);
            }
            if (GUI.Button(Expand(right, 8f), GUIContent.none, GUIStyle.none) || GUI.Button(mid, GUIContent.none, GUIStyle.none))
            {
                index = Wrap(index + 1, options.Length);
            }
            if (Event.current.type == EventType.Repaint)
            {
                Color arrowColor = _focused ? HudTheme.P.LineBright : HudTheme.P.Label;
                HudTheme.Tex(left, _focused ? HudTheme.TriLeft : HudTheme.TriLeftHollow, arrowColor);
                HudTheme.Tex(right, _focused ? HudTheme.TriRight : HudTheme.TriRightHollow, arrowColor);
                GUI.Label(mid, Core.Locale.T(options[index]), _focused ? HudTheme.ValueSelected : HudTheme.Value);
            }
            return index;
        }

        public string[] Packs(Core.PackFilter filter, string root, string[] keys)
        {
            string[] options = filter.Options(keys, root);
            if (!filter.Active)
            {
                return keys;
            }
            filter.Pick(options, Selector("Pack", options, filter.Index(options)));
            string info = filter.Info(root);
            if (info != null)
            {
                Note(info);
            }
            return filter.Apply(keys);
        }

        public bool Toggle(string label, bool value)
        {
            return Selector(label, new[] { "Off", "On" }, value ? 1 : 0) == 1;
        }

        public TEnum Enum<TEnum>(string label, TEnum value, string[] names) where TEnum : struct, System.Enum
        {
            int i = Selector(label, names, Convert.ToInt32(value));
            return (TEnum)System.Enum.ToObject(typeof(TEnum), i);
        }

        public bool Action(string label, string button)
        {
            Rect r = NextRow();
            DrawLabel(r, label);
            Rect c = ControlRect(r);
            var box = new Rect(c.x + 10f, c.y + 5f, c.width - 20f, c.height - 10f);
            bool clicked = GUI.Button(r, GUIContent.none, GUIStyle.none) || KeyActivate();
            if (Event.current.type == EventType.Repaint)
            {
                HudTheme.Fill(box, _focused ? HudTheme.P.Highlight : HudTheme.P.ValueBox);
                GUI.Label(box, Core.Locale.T(button).ToUpperInvariant(), _focused ? HudTheme.BoxTextHi : HudTheme.BoxText);
            }
            return clicked;
        }

        public int Buttons(params string[] labels)
        {
            Rect r = NextRow();
            int rowIndex = _row - 1;
            _segmentFocus.TryGetValue(rowIndex, out int seg);
            if (KeyStep(out int s))
            {
                seg = Wrap(seg + s, labels.Length);
                _segmentFocus[rowIndex] = seg;
            }

            int result = KeyActivate() ? seg : -1;
            float pad = 6f;
            float w = (r.width - pad * (labels.Length + 1)) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                var box = new Rect(r.x + pad + i * (w + pad), r.y + 5f, w, r.height - 10f);
                if (GUI.Button(box, GUIContent.none, GUIStyle.none))
                {
                    result = i;
                    _segmentFocus[rowIndex] = i;
                }
                if (Event.current.type == EventType.Repaint)
                {
                    bool hover = box.Contains(Event.current.mousePosition);
                    bool hi = _focused && (seg == i || hover);
                    HudTheme.Fill(box, hi ? HudTheme.P.Highlight : HudTheme.P.ValueBox);
                    GUI.Label(box, Core.Locale.T(labels[i]).ToUpperInvariant(), hi ? HudTheme.BoxTextHi : HudTheme.BoxText);
                }
            }
            return result;
        }

        internal struct Slot
        {
            public Texture2D Icon { get; set; }
            public string Text { get; set; }
            public bool IsAdd { get; set; }
        }

        public int Slots(string label, IList<Slot> slots, int selected)
        {
            Rect r = NextRow(76f);
            DrawLabel(r, label);
            Rect c = ControlRect(r);
            int rowIndex = _row - 1;
            if (!_segmentFocus.TryGetValue(rowIndex, out int seg))
            {
                seg = Mathf.Max(0, selected);
            }
            if (KeyStep(out int s) && slots.Count > 0)
            {
                seg = Wrap(seg + s, slots.Count);
                _segmentFocus[rowIndex] = seg;
            }
            int result = KeyActivate() ? seg : -1;

            const float size = 60f;
            const float gap = 12f;
            for (int i = 0; i < slots.Count; i++)
            {
                var box = new Rect(c.x + 8f + i * (size + gap), r.y + 8f, size, size);
                if (box.xMax > c.xMax)
                {
                    break;
                }
                if (GUI.Button(box, GUIContent.none, GUIStyle.none))
                {
                    result = i;
                    _segmentFocus[rowIndex] = i;
                }
                if (Event.current.type != EventType.Repaint)
                {
                    continue;
                }

                bool hover = box.Contains(Event.current.mousePosition);
                bool isSelected = i == selected;
                bool keyFocus = _focused && seg == i;
                Color frame = isSelected ? HudTheme.P.Highlight : (hover || keyFocus ? HudTheme.P.LineBright : HudTheme.P.Line);
                Slot slot = slots[i];
                if (slot.IsAdd)
                {
                    Brackets(box, frame);
                    HudTheme.Fill(new Rect(box.center.x - 10f, box.center.y - 1f, 20f, 2f), frame);
                    HudTheme.Fill(new Rect(box.center.x - 1f, box.center.y - 10f, 2f, 20f), frame);
                    continue;
                }
                HudTheme.Fill(box, HudTheme.P.RowFill);
                if (!string.IsNullOrEmpty(slot.Text))
                {
                    GUI.Label(box, slot.Text, HudTheme.BoxText);
                }
                else if (slot.Icon != null)
                {
                    HudTheme.Tex(new Rect(box.x + 12f, box.y + 12f, box.width - 24f, box.height - 24f), slot.Icon, isSelected ? HudTheme.P.Highlight : HudTheme.P.Label);
                }
                HudTheme.Border(box, frame, isSelected ? 2f : 1f);
            }
            return result;
        }

        private static void Brackets(Rect b, Color c)
        {
            const float l = 10f;
            HudTheme.Fill(new Rect(b.x, b.y, l, 1f), c);
            HudTheme.Fill(new Rect(b.x, b.y, 1f, l), c);
            HudTheme.Fill(new Rect(b.xMax - l, b.y, l, 1f), c);
            HudTheme.Fill(new Rect(b.xMax - 1f, b.y, 1f, l), c);
            HudTheme.Fill(new Rect(b.x, b.yMax - 1f, l, 1f), c);
            HudTheme.Fill(new Rect(b.x, b.yMax - l, 1f, l), c);
            HudTheme.Fill(new Rect(b.xMax - l, b.yMax - 1f, l, 1f), c);
            HudTheme.Fill(new Rect(b.xMax - 1f, b.yMax - l, 1f, l), c);
        }

        public int ListRow(string label, string info, params string[] buttons)
        {
            Rect r = NextRow();
            int rowIndex = _row - 1;
            DrawLabel(r, label);
            Rect c = ControlRect(r);
            _segmentFocus.TryGetValue(rowIndex, out int seg);
            if (KeyStep(out int s))
            {
                seg = Wrap(seg + s, buttons.Length);
                _segmentFocus[rowIndex] = seg;
            }
            int result = KeyActivate() ? seg : -1;

            const float bw = 58f;
            float bx = c.xMax - buttons.Length * (bw + 4f);
            if (Event.current.type == EventType.Repaint && !string.IsNullOrEmpty(info))
            {
                GUI.Label(new Rect(c.x, c.y, bx - c.x - 6f, c.height), Core.Locale.Show(info), _focused ? HudTheme.ValueSelected : HudTheme.Value);
            }
            for (int i = 0; i < buttons.Length; i++)
            {
                var box = new Rect(bx + i * (bw + 4f), c.y + 6f, bw, c.height - 12f);
                if (GUI.Button(box, GUIContent.none, GUIStyle.none))
                {
                    result = i;
                }
                if (Event.current.type == EventType.Repaint)
                {
                    bool hi = _focused && (seg == i || box.Contains(Event.current.mousePosition));
                    HudTheme.Fill(box, hi ? HudTheme.P.Highlight : HudTheme.P.ValueBox);
                    GUI.Label(box, Core.Locale.T(buttons[i]).ToUpperInvariant(), hi ? HudTheme.BoxTextHi : HudTheme.BoxText);
                }
            }
            return result;
        }

        public string Text(string label, string value)
        {
            Rect r = NextRow();
            DrawLabel(r, label);
            Rect c = ControlRect(r);
            GUI.SetNextControlName(FieldPrefix + label);
            return GUI.TextField(new Rect(c.x + 10f, c.y + 6f, c.width - 20f, c.height - 12f), value ?? "", HudTheme.Field);
        }

        public void Floats(string label, string keyPrefix, float[] values)
        {
            Rect r = NextRow();
            DrawLabel(r, label);
            Rect c = ControlRect(r);
            float pad = 4f;
            float w = (c.width - pad * (values.Length + 1)) / values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                var field = new Rect(c.x + pad + i * (w + pad), c.y + 6f, w, c.height - 12f);
                values[i] = FloatField(field, keyPrefix + i, values[i]);
            }
        }

        public void ClearBuffers(string prefix = null)
        {
            if (prefix == null)
            {
                _buffers.Clear();
                return;
            }
            var remove = new List<string>();
            foreach (string k in _buffers.Keys)
            {
                if (k.StartsWith(prefix, StringComparison.Ordinal))
                {
                    remove.Add(k);
                }
            }
            foreach (string k in remove)
            {
                _buffers.Remove(k);
            }
        }

        private float FloatField(Rect rect, string key, float value)
        {
            if (_buffers.TryGetValue(key, out string text))
            {
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float buffered) && !Mathf.Approximately(buffered, value))
                {
                    text = value.ToString("0.###", CultureInfo.InvariantCulture);
                }
            }
            else
            {
                text = value.ToString("0.###", CultureInfo.InvariantCulture);
            }
            GUI.SetNextControlName(FieldPrefix + key);
            string edited = GUI.TextField(rect, text, HudTheme.Field);
            _buffers[key] = edited;
            return float.TryParse(edited, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ? parsed : value;
        }

        public Color ColorRows(string label, Color color)
        {
            color.r = IntSlider(label + " R", Mathf.RoundToInt(color.r * 255f), 0, 255) / 255f;
            color.g = IntSlider(label + " G", Mathf.RoundToInt(color.g * 255f), 0, 255) / 255f;
            color.b = IntSlider(label + " B", Mathf.RoundToInt(color.b * 255f), 0, 255) / 255f;
            return color;
        }

        private static int Wrap(int i, int n) => ((i % n) + n) % n;

        private static Rect Expand(Rect r, float by) => new Rect(r.x - by, r.y - by, r.width + by * 2f, r.height + by * 2f);
    }
}
