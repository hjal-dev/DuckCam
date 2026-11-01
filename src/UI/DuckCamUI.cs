using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using DuckCam.Camera;
using DuckCam.Core;
using DuckCam.Rendering;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed partial class DuckCamUI
    {
        private sealed class TabDef
        {
            public string Title;
            public string Icon;
            public Action Draw;
        }

        private const float Margin = 26f;
        private const float RowsWidth = 760f;
        private const float TabHeight = 44f;
        private const float TabWidth = 66f;
        private const float BottomReserve = 74f;
        private const float AnimSeconds = 0.16f;
        private const float FlashSeconds = 0.3f;
        private const float ScrollbarWidth = 12f;
        private const float ScrollbarGap = 8f;
        private const float WheelStep = (HudKit.RowHeight + HudKit.RowGap) * 1.5f;
        private static readonly int ScrollbarHint = "DuckCamPanelScrollbar".GetHashCode();

        private readonly DuckCamController _c;
        private readonly HudKit _kit = new HudKit();
        private readonly List<TabDef> _allTabs = new List<TabDef>();
        private int _tab;
        private Vector2 _scroll;
        private float _measured = 300f;
        private float _viewH = 300f;
        private float _scrollTarget = -1f;
        private float _scrollGrab;
        private float _flashAt = -10f;
        private Rect _uiScreenRect;
        private float _shownAt;
        private int _lastDrawFrame = -10;
        private int _editKey = -1;

        private int _poseCategory;
        private int _poseLibrary;
        private int _bodyGroup;
        private bool _bodyPartial;
        private string _poseSearch = "";
        private string _poseError;

        private string _lightName = "Key light";
        private int _lightType = 1;
        private float _lightIntensity = 2f;
        private float _lightRange = 10f;
        private float _lightSpot = 45f;
        private Color _lightColor = new Color(1f, 0.95f, 0.85f);
        private bool _lightShadows;

        private string _fileName = "path1";
        private string _fileMessage;

        public DuckCamUI(DuckCamController controller)
        {
            _c = controller;
            Spawner = new SpawnerWindow(controller, controller.BotSpawns);
            PoseEditor = new PoseEditorWindow(controller);
            Timeline = new TimelineWindow(controller);
            Addons = new AddonCreatorWindow();
            Register("CAMERA", "camera", DrawCamera);
            Register("MOTION", "motion", DrawMotion);
            Register("TIME", "time", DrawTime);
            Register("PATHS", "paths", DrawPaths);
            Register("DEPTH OF FIELD", "dof", DrawDof);
            Register("EFFECTS", "effects", DrawEffects);
            Register("LIGHTS", "lights", DrawLights);
            Register("ENVIRONMENT", "environment", DrawEnvironment);
            Register("CHARACTERS", "characters", DrawCharacters);
            Register("OUTFIT & GEAR", "outfit", DrawOutfit);
            Register("OVERLAYS", "overlays", DrawOverlays);
            Register("FRAME", "frame", DrawFrame);
            Register("PRESETS", "presets", DrawPresets);
            Register("ADD-ONS", "addons", DrawAddons);
            Register("CAPTURE", "capture", DrawCapture);
            Register("DEBUG", "debug", DrawDebug);
        }

        private void Register(string title, string icon, Action draw, int insertBefore = -1)
        {
            var def = new TabDef { Title = title, Icon = icon, Draw = draw };
            if (insertBefore >= 0 && insertBefore < _allTabs.Count)
            {
                _allTabs.Insert(insertBefore, def);
            }
            else
            {
                _allTabs.Add(def);
            }
        }

        public bool PointerOverUi
        {
            get
            {
                if (Time.frameCount - _lastDrawFrame > 2)
                {
                    return false;
                }
                Vector3 m = Input.mousePosition;
                return Spawner.PointerOver || Addons.PointerOver || PoseEditor.PointerOver || Timeline.PointerOver || (!Spawner.Open && !Addons.Open && !PoseEditor.Open && _uiScreenRect.Contains(new Vector2(m.x, Screen.height - m.y)));
            }
        }

        public void HandleKeys()
        {
            if (DuckCamConfig.UiCategoryNext.Value.IsDown() || DuckCamConfig.NextCategory.Value.IsDown()) SwitchTab(1);
            if (DuckCamConfig.UiCategoryPrev.Value.IsDown() || DuckCamConfig.PrevCategory.Value.IsDown()) SwitchTab(-1);
            int move = (Down(DuckCamConfig.UiOptionDown, DuckCamConfig.MenuDown) ? 1 : 0) - (Down(DuckCamConfig.UiOptionUp, DuckCamConfig.MenuUp) ? 1 : 0);
            int step = (Down(DuckCamConfig.UiValueRight, DuckCamConfig.MenuRight) ? 1 : 0) - (Down(DuckCamConfig.UiValueLeft, DuckCamConfig.MenuLeft) ? 1 : 0);
            bool activate = Down(DuckCamConfig.UiActivate, DuckCamConfig.MenuSelect) || Input.GetKeyDown(KeyCode.KeypadEnter);
            if (move != 0 || step != 0 || activate)
            {
                _kit.Queue(move, step, activate);
            }
        }

        private static bool Down(ConfigEntry<KeyboardShortcut> a, ConfigEntry<KeyboardShortcut> b) => a.Value.IsDown() || b.Value.IsDown();

        private void SwitchTab(int delta)
        {
            int n = Tabs.Count;
            SelectTab(((_tab + delta) % n + n) % n);
        }

        private void SelectTab(int index)
        {
            _tab = index;
            _scroll = Vector2.zero;
            _scrollTarget = -1f;
            _kit.ResetFocus();
            _editKey = -1;
            InputContext.LeaveField();
        }

        public void Flash()
        {
            _flashAt = Time.unscaledTime;
        }

        private static void HandleFieldFocus()
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Tab || e.character == '\t')
                {
                    InputContext.LeaveField();
                    e.Use();
                }
                else if (InputContext.Typing && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape))
                {
                    InputContext.LeaveField();
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseDown)
            {
                InputContext.LeaveField();
            }
        }

        public void Draw()
        {
            HudTheme.Ensure();
            HandleFieldFocus();
            if (Time.frameCount - _lastDrawFrame > 2)
            {
                _shownAt = Time.unscaledTime;
            }
            _lastDrawFrame = Time.frameCount;

            float scale = Screen.height / 1080f * DuckCamConfig.UiScale.Value;
            float vw = Screen.width / scale;
            float vh = Screen.height / scale;

            Matrix4x4 savedMatrix = GUI.matrix;
            Color savedColor = GUI.color;
            try
            {
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
                float sinceFlash = Time.unscaledTime - _flashAt;
                if (sinceFlash < FlashSeconds && Event.current.type == EventType.Repaint)
                {
                    HudTheme.Fill(new Rect(0f, 0f, vw, vh), new Color(1f, 1f, 1f, 0.45f * (1f - sinceFlash / FlashSeconds)));
                }
                float ease = 1f - Mathf.Pow(1f - Mathf.Clamp01((Time.unscaledTime - _shownAt) / AnimSeconds), 3f);
                GUI.color = new Color(1f, 1f, 1f, ease);

                if (DuckCamConfig.EdgeGlow.Value && Event.current.type == EventType.Repaint)
                {
                    HudTheme.DrawEdgeGlow(vw, vh, 22f);
                }
                if (DuckCamConfig.HudDecorations.Value)
                {
                    DrawHud(vw, vh);
                }
                if (_c.Path.Countdown > 0f && Event.current.type == EventType.Repaint)
                {
                    var big = new GUIStyle(HudTheme.HudBrand) { fontSize = 140, alignment = TextAnchor.MiddleCenter };
                    GUI.Label(new Rect(0f, vh * 0.5f - 100f, vw, 200f), Mathf.CeilToInt(_c.Path.Countdown).ToString(CultureInfo.InvariantCulture), big);
                }

                bool right = DuckCamConfig.PanelSide.Value == UiSide.Right;
                if (DuckCamConfig.ShowKeyHints.Value)
                {
                    if (!Timeline.Open)
                    {
                        DrawLegend(vw, vh, right);
                    }
                    DrawHintBar(vw, vh, right);
                }
                if (Spawner.Open)
                {
                    Spawner.Draw(vw, vh);
                }
                else if (Addons.Open)
                {
                    Addons.Draw(vw, vh);
                }
                else
                {
                    bool panel = !PoseEditor.Open;
                    if (PoseEditor.Open)
                    {
                        PoseEditor.Draw(vw, vh);
                    }
                    else
                    {
                        DrawPanel(vw, vh, right, scale, (1f - ease) * 30f);
                    }
                    float left = PoseEditor.Open ? 500f : (panel && !right ? Margin + RowsWidth + 16f : 20f);
                    float rightEdge = panel && right ? vw - Margin - RowsWidth - 16f : vw - 20f;
                    Timeline.Draw(vw, vh, left, rightEdge);
                }
            }
            finally
            {
                GUI.matrix = savedMatrix;
                GUI.color = savedColor;
                InputContext.RecordFieldFocus();
            }
        }

        private void DrawPanel(float vw, float vh, bool right, float scale, float slide)
        {
            float rowsX = right ? vw - Margin - RowsWidth + slide : Margin - slide;
            float bottom = vh - BottomReserve - 40f;
            float minTop = 170f + TabHeight + 60f;
            float viewH = Mathf.Max(HudKit.RowHeight, Mathf.Min(DuckCamConfig.PanelRows.Value * (HudKit.RowHeight + HudKit.RowGap), bottom - minTop));
            _viewH = viewH;
            float top = bottom - viewH;

            float glyph = 44f;
            float barW = TabWidth * Tabs.Count + glyph * 2f;
            float barX = right ? rowsX + RowsWidth - barW : rowsX;
            float barY = top - TabHeight - 10f;
            DrawTabs(new Rect(barX, barY, barW, TabHeight), glyph);

            TabDef active = Tabs[_tab];
            var titleRect = new Rect(barX, barY - 48f, barW, 44f);
            if (Event.current.type == EventType.Repaint)
            {
                float titleW = HudTheme.Title.CalcSize(new GUIContent(Core.Locale.T(active.Title))).x;
                float tx = right ? titleRect.xMax - titleW - 6f : titleRect.x + 40f;
                HudTheme.Tex(new Rect(tx - 38f, titleRect.y + 6f, 32f, 32f), HudTheme.Icon(active.Icon), HudTheme.P.Highlight);
                GUI.Label(new Rect(tx, titleRect.y, titleW + 4f, titleRect.height), Core.Locale.T(active.Title), HudTheme.Title);
            }
            if (!string.IsNullOrEmpty(_c.LastMessage))
            {
                GUI.Label(new Rect(rowsX, barY - 78f, RowsWidth, 24f), _c.LastMessage, HudTheme.Message);
            }

            var area = new Rect(rowsX, top, RowsWidth, viewH);
            float maxScroll = Mathf.Max(0f, _measured - viewH);
            bool scrollable = maxScroll > 1f;
            var track = new Rect(right ? area.xMax + ScrollbarGap : area.xMin - ScrollbarGap - ScrollbarWidth, area.y, ScrollbarWidth, area.height);

            var hit = new Rect(area.x, area.y, area.width, Mathf.Min(_measured, viewH));
            if (scrollable)
            {
                hit = Rect.MinMaxRect(Mathf.Min(hit.xMin, track.xMin), hit.yMin, Mathf.Max(hit.xMax, track.xMax), track.yMax);
            }
            Rect union = Rect.MinMaxRect(Mathf.Min(hit.xMin, barX), titleRect.y, Mathf.Max(hit.xMax, barX + barW), hit.yMax);
            _uiScreenRect = new Rect(union.x * scale, union.y * scale, union.width * scale, union.height * scale);

            Event e = Event.current;
            if (e.type == EventType.ScrollWheel && hit.Contains(e.mousePosition))
            {
                e.Use();
            }
            if (e.type == EventType.Repaint && _scrollTarget >= 0f)
            {
                _scroll.y = Mathf.Lerp(_scroll.y, _scrollTarget, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 16f));
                if (Mathf.Abs(_scroll.y - _scrollTarget) < 0.5f)
                {
                    _scroll.y = _scrollTarget;
                    _scrollTarget = -1f;
                }
            }
            _scroll.y = Mathf.Clamp(_scroll.y, 0f, maxScroll);

            GUILayout.BeginArea(area);
            _scroll = GUILayout.BeginScrollView(_scroll, GUIStyle.none, GUIStyle.none);
            GUILayout.BeginVertical(GUILayout.Width(RowsWidth - 4f));
            _kit.BeginContent();
            DrawHowTo(active);
            active.Draw();
            _kit.EndContent();
            GUILayout.EndVertical();
            if (Event.current.type == EventType.Repaint)
            {
                _measured = Mathf.Max(HudKit.RowHeight, GUILayoutUtility.GetLastRect().height);
                if (_kit.ScrollToFocus)
                {
                    Rect f = _kit.FocusRect;
                    if (f.yMin < _scroll.y) _scroll.y = f.yMin;
                    if (f.yMax > _scroll.y + viewH) _scroll.y = f.yMax - viewH;
                    _scrollTarget = -1f;
                    _kit.ScrollToFocus = false;
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            DrawScrollbar(track, viewH);
        }

        public SpawnerWindow Spawner { get; }

        public PoseEditorWindow PoseEditor { get; }

        public TimelineWindow Timeline { get; }

        public AddonCreatorWindow Addons { get; }

        public void Scroll(float notches)
        {
            if (Spawner.Open)
            {
                Spawner.Scroll(notches);
                return;
            }
            if (Addons.Open)
            {
                Addons.Scroll(notches);
                return;
            }
            if (Timeline.PointerOver)
            {
                Timeline.Scroll(notches);
                return;
            }
            if (PoseEditor.Open)
            {
                PoseEditor.Scroll(notches);
                return;
            }
            float max = Mathf.Max(0f, _measured - _viewH);
            if (Mathf.Abs(notches) < 0.01f || max <= 1f)
            {
                return;
            }
            float from = _scrollTarget >= 0f ? _scrollTarget : _scroll.y;
            _scrollTarget = Mathf.Clamp(from - notches * WheelStep, 0f, max);
        }

        private void DrawScrollbar(Rect track, float viewH)
        {
            Event e = Event.current;
            int id = GUIUtility.GetControlID(ScrollbarHint, FocusType.Passive);
            float max = Mathf.Max(0f, _measured - viewH);
            if (max <= 1f)
            {
                if (GUIUtility.hotControl == id)
                {
                    GUIUtility.hotControl = 0;
                }
                return;
            }

            float thumbH = Mathf.Clamp(track.height * viewH / _measured, 36f, track.height);
            float travel = Mathf.Max(1f, track.height - thumbH);
            float thumbY = track.y + travel * Mathf.Clamp01(_scroll.y / max);
            var thumb = new Rect(track.x, thumbY, track.width, thumbH);

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && track.Contains(e.mousePosition))
                    {
                        if (!thumb.Contains(e.mousePosition))
                        {
                            thumbY = Mathf.Clamp(e.mousePosition.y - thumbH * 0.5f, track.y, track.yMax - thumbH);
                            _scroll.y = (thumbY - track.y) / travel * max;
                        }
                        _scrollTarget = -1f;
                        _scrollGrab = e.mousePosition.y - thumbY;
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        float y = Mathf.Clamp(e.mousePosition.y - _scrollGrab, track.y, track.yMax - thumbH);
                        _scroll.y = (y - track.y) / travel * max;
                        _scrollTarget = -1f;
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
                    bool dragging = GUIUtility.hotControl == id;
                    bool hover = track.Contains(e.mousePosition);
                    float cx = Mathf.Round(track.center.x);
                    HudTheme.Fill(new Rect(cx - 1f, track.y, 2f, track.height), HudTheme.P.Line);
                    HudTheme.Fill(new Rect(track.x, track.y, track.width, 2f), HudTheme.P.LineBright);
                    HudTheme.Fill(new Rect(track.x, track.yMax - 2f, track.width, 2f), HudTheme.P.LineBright);
                    float w = dragging || hover ? track.width : 6f;
                    Color c = dragging ? HudTheme.P.Highlight : (hover ? HudTheme.P.Value : HudTheme.P.LineBright);
                    HudTheme.Fill(new Rect(cx - w * 0.5f, thumb.y + 3f, w, thumb.height - 6f), c);
                    break;
            }
        }

        private void DrawTabs(Rect bar, float glyph)
        {
            var leftKey = new Rect(bar.x, bar.y, glyph, bar.height);
            var rightKey = new Rect(bar.xMax - glyph, bar.y, glyph, bar.height);
            if (GUI.Button(leftKey, GUIContent.none, GUIStyle.none)) SwitchTab(-1);
            if (GUI.Button(rightKey, GUIContent.none, GUIStyle.none)) SwitchTab(1);
            if (Event.current.type == EventType.Repaint)
            {
                HudTheme.DrawKeyCap(leftKey.x + 10f, leftKey.center.y - 13f, KeyName(DuckCamConfig.UiCategoryPrev), 26f);
                HudTheme.DrawKeyCap(rightKey.x + 8f, rightKey.center.y - 13f, KeyName(DuckCamConfig.UiCategoryNext), 26f);
            }

            for (int i = 0; i < Tabs.Count; i++)
            {
                var r = new Rect(bar.x + glyph + i * TabWidth, bar.y, TabWidth, bar.height);
                bool active = _tab == i;
                if (GUI.Button(r, new GUIContent("", Core.Locale.T(Tabs[i].Title)), GUIStyle.none) && !active)
                {
                    SelectTab(i);
                }
                if (Event.current.type != EventType.Repaint)
                {
                    continue;
                }
                var inner = new Rect(r.x + 3f, r.y + 2f, r.width - 6f, r.height - 4f);
                bool hover = r.Contains(Event.current.mousePosition);
                HudTheme.Fill(inner, active ? HudTheme.P.TabFillActive : HudTheme.P.TabFill);
                if (active)
                {
                    HudTheme.SelectedFrame(inner, HudTheme.P.LineBright);
                }
                else
                {
                    HudTheme.Border(inner, hover ? HudTheme.P.LineBright : HudTheme.P.Line);
                }
                Color chip = active ? HudTheme.P.Value : HudTheme.P.Dim;
                HudTheme.Tex(new Rect(inner.x + 5f, inner.center.y - 8f, 16f, 16f), HudTheme.Chip, new Color(chip.r, chip.g, chip.b, chip.a * 0.7f));
                HudTheme.Tex(new Rect(inner.x + 25f, inner.center.y - 13f, 26f, 26f), HudTheme.Icon(Tabs[i].Icon), active ? HudTheme.P.Value : HudTheme.P.Label);
            }
        }

        private void DrawLegend(float vw, float vh, bool panelRight)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            const float lineH = 36f;
            float x0 = panelRight ? 40f : vw - 330f;
            var lines = new List<Action<float, float>>
            {
                (x, y) => { x += HudTheme.DrawGlyphCap(x, y, HudTheme.Mouse[2], 26f, false); Text(x, y, "Rotate Camera"); },
                (x, y) => { x += Caps(x, y, KeyName(DuckCamConfig.UiCamForward), KeyName(DuckCamConfig.UiCamLeft), KeyName(DuckCamConfig.UiCamBack), KeyName(DuckCamConfig.UiCamRight)); Text(x, y, "Move Camera"); },
                (x, y) => { x += Caps(x, y, KeyName(DuckCamConfig.UiCamDown), KeyName(DuckCamConfig.UiCamUp)); Text(x, y, "Camera Down/Up"); },
                (x, y) => { x += HudTheme.DrawGlyphCap(x, y, HudTheme.Mouse[3], 26f, false); Text(x, y, "FOV / Scroll Menu"); },
                (x, y) => { x += HudTheme.DrawGlyphCap(x, y + 2f, HudTheme.ArrowUp) + 6f; x += HudTheme.DrawGlyphCap(x, y + 2f, HudTheme.ArrowDown); Text(x, y, "Select Option"); },
                (x, y) => { x += HudTheme.DrawGlyphCap(x, y + 2f, HudTheme.ArrowLeft) + 6f; x += HudTheme.DrawGlyphCap(x, y + 2f, HudTheme.ArrowRight); Text(x, y, "Change Value"); },
            };
            float yy = vh - BottomReserve - lines.Count * lineH;
            foreach (Action<float, float> line in lines)
            {
                line(x0, yy);
                yy += lineH;
            }
        }

        private static float Caps(float x, float y, params string[] keys)
        {
            float start = x;
            foreach (string k in keys)
            {
                x += HudTheme.DrawKeyCap(x, y + 2f, k) + 4f;
            }
            return x - start;
        }

        private static void Text(float x, float y, string text)
        {
            GUI.Label(new Rect(x + 10f, y, 260f, 30f), Core.Locale.T(text), HudTheme.Legend);
        }

        private void DrawHintBar(float vw, float vh, bool panelRight)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            var items = new List<(string[] keys, string text)>
            {
                (new[] { KeyName(DuckCamConfig.UiCategoryPrev), KeyName(DuckCamConfig.UiCategoryNext) }, "Select Category"),
                (new[] { KeyName(DuckCamConfig.UiPhoto) }, "Take Screenshot"),
                (new[] { KeyName(DuckCamConfig.UiHide) }, "Show/Hide UI"),
            };
            if (!_c.Frozen)
            {
                items.Add((new[] { KeyName(DuckCamConfig.ToggleKey) }, "Freeze"));
                items.Add((new[] { KeyName(DuckCamConfig.ChangeGameSpeedKey) }, "Slow Motion"));
            }
            else if (_c.StartedLive)
            {
                items.Add((new[] { KeyName(DuckCamConfig.ToggleKey) }, "Unfreeze"));
            }
            items.Add((new[] { KeyName(DuckCamConfig.UiExit) }, "Exit DuckCam"));

            float total = 0f;
            foreach ((string[] keys, string text) in items)
            {
                foreach (string k in keys)
                {
                    total += Mathf.Max(22f, HudTheme.KeyCap.CalcSize(new GUIContent(k)).x + 10f) + 4f;
                }
                total += HudTheme.Legend.CalcSize(new GUIContent(Core.Locale.T(text))).x + 30f;
            }
            float x = panelRight ? vw - Margin - total : Margin;
            float y = vh - BottomReserve + 14f;
            foreach ((string[] keys, string text) in items)
            {
                foreach (string k in keys)
                {
                    x += HudTheme.DrawKeyCap(x, y + 4f, k) + 4f;
                }
                string shown = Core.Locale.T(text);
                float tw = HudTheme.Legend.CalcSize(new GUIContent(shown)).x;
                GUI.Label(new Rect(x + 4f, y, tw + 4f, 30f), shown, HudTheme.Legend);
                x += tw + 30f;
            }
        }

        private void DrawHud(float vw, float vh)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            PhotoCameraController cam = _c.Camera;
            Vector3 p = cam.Position;
            string loc = (GameContext.LocationId ?? "N/A").ToUpperInvariant();
            var left = new GUIStyle(HudTheme.HudSmall) { alignment = TextAnchor.MiddleLeft };
            var fine = new GUIStyle(HudTheme.HudSmall) { fontSize = 10, alignment = TextAnchor.UpperLeft };

            GUI.Label(new Rect(0f, 8f, vw, 24f), $"CAM / {p.x:0.000} / {p.y:0.000} / {p.z:0.000} / {loc}", HudTheme.HudSmall);

            string status = _c.Recording.Recording ? "REC" : (_c.Path.Playing || _c.Recording.Playing ? "PLAY" : "OK");
            string mode = _c.Frozen ? "FROZEN" : (_c.Dilation.Active ? $"LIVE {_c.Dilation.Scale * 100f:0}%" : "LIVE");
            GUI.Label(new Rect(40f, 36f, 600f, 22f), $"MODE  {mode}    STATUS  {status}", left);
            GUI.Label(new Rect(40f, 58f, 600f, 22f), $"FOV {cam.EffectiveFov:0.0}  DST {cam.DistanceFromAnchor:0.0}M  {cam.Mode.ToString().ToUpperInvariant()}", left);

            TimeSpan past = GameContext.Game?.GameTimer?.PastTime ?? TimeSpan.Zero;
            Matrix4x4 saved = GUI.matrix;
            var pivot = new Vector2(16f, vh * 0.5f);
            GUIUtility.RotateAroundPivot(-90f, pivot);
            GUI.Label(new Rect(pivot.x - 250f, pivot.y - 9f, 500f, 18f),
                $"{(int)past.TotalHours:00} {past.Minutes:00} {past.Seconds:00} {loc}  //  {GameContext.MapDisplayName.ToUpperInvariant()}",
                new GUIStyle(fine) { alignment = TextAnchor.MiddleCenter });
            GUI.matrix = saved;

            string footer = $"{Time.frameCount % 100000:00000}  " + DateTime.Now.ToString("HH mm ss", CultureInfo.InvariantCulture)
                            + $"  {loc}  {CompositionOverlay.AspectNames[(int)DuckCamConfig.FrameAspectRatio.Value]}";
            float fw = fine.CalcSize(new GUIContent(footer)).x;
            float fx = (vw - fw) * 0.5f;
            GUI.Label(new Rect(fx, vh - 22f, fw + 4f, 18f), footer, fine);
            HudTheme.Fill(new Rect(fx + fw + 10f, vh - 15f, 22f, 1f), HudTheme.P.Hud);
            HudTheme.Tex(new Rect(fx + fw + 6f, vh - 19f, 9f, 9f), HudTheme.TriLeft, HudTheme.P.Hud);
        }

        private int _effectPreset;
        private string _filterName = "My Filter";
        private string _deleteFilter;

        private readonly Core.PackFilter _filterPacks = new Core.PackFilter();

        private readonly Core.PackFilter _rigPacks = new Core.PackFilter();

        private readonly Core.PackFilter _pathPacks = new Core.PackFilter();

        private readonly Core.PackFilter _presetPacks = new Core.PackFilter();
        private int _overlayCategory;
        private int _overlayPick;

        public static string KeyName(ConfigEntry<KeyboardShortcut> entry)
        {
            KeyboardShortcut s = entry.Value;
            if (s.MainKey == KeyCode.None)
            {
                return "-";
            }
            return string.Join("+", s.Modifiers.Select(Pretty).Concat(new[] { Pretty(s.MainKey) }));
        }

        private static string Pretty(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.BackQuote: return "`";
                case KeyCode.Minus: return "-";
                case KeyCode.Equals: return "=";
                case KeyCode.Plus: return "+";
                case KeyCode.Less: return "<";
                case KeyCode.Greater: return ">";
                case KeyCode.Slash: return "/";
                case KeyCode.Backslash: return "\\";
                case KeyCode.LeftBracket: return "[";
                case KeyCode.RightBracket: return "]";
                case KeyCode.KeypadPlus: return "Num+";
                case KeyCode.UpArrow: return "Up";
                case KeyCode.DownArrow: return "Down";
                case KeyCode.LeftArrow: return "Left";
                case KeyCode.RightArrow: return "Right";
                case KeyCode.LeftControl: return "Ctrl";
                case KeyCode.LeftShift: return "Shift";
                case KeyCode.LeftAlt: return "Alt";
                case KeyCode.Backspace: return "Bksp";
                case KeyCode.PageUp: return "PgUp";
                case KeyCode.PageDown: return "PgDn";
                case KeyCode.Insert: return "Ins";
                case KeyCode.Delete: return "Del";
            }
            string name = key.ToString();
            if (name.StartsWith("Keypad", StringComparison.Ordinal))
            {
                return "Num" + name.Substring(6);
            }
            if (name.StartsWith("Alpha", StringComparison.Ordinal))
            {
                return name.Substring(5);
            }
            return name;
        }
    }
}
