using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DuckCam.Actors;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.UI
{
    internal enum PoseTool
    {
        Tweak,
        Move,
        Rotate
    }

    internal sealed partial class PoseEditorWindow
    {
        private const float W = 440f;
        private const float RowH = 22f;
        private const int PosesPerPage = 5;
        private const string Prefix = "Base Human";

        private static readonly Color EditedBlue = new Color(0.35f, 0.65f, 1f, 1f);
        private static readonly float[] Opacities = { 1f, 0.75f, 0.5f, 0.25f };
        private static readonly (BoneSet set, string label)[] SetLabels =
        {
            (BoneSet.Torso, "TORSO"), (BoneSet.Head, "HEAD"), (BoneSet.ArmL, "ARM.L"), (BoneSet.ArmR, "ARM.R"), (BoneSet.FingersL, "FING.L"),
            (BoneSet.FingersR, "FING.R"), (BoneSet.LegL, "LEG.L"), (BoneSet.LegR, "LEG.R"), (BoneSet.Twist, "TWIST")
        };

        private readonly DuckCamController _c;
        private PhotoActor _actor;

        private PoseTool _tool = PoseTool.Rotate;
        private bool _local;
        private bool _xMirror;
        private bool _gizmos = true;
        private bool _autoIk;
        private bool _euler;
        private int _opacity;
        private bool _help;

        private readonly HashSet<BoneSet> _hiddenSets = new HashSet<BoneSet> { BoneSet.Twist, BoneSet.Gear };
        private readonly Dictionary<PhotoActor, HashSet<string>> _hiddenBones = new Dictionary<PhotoActor, HashSet<string>>();
        private readonly HashSet<string> _collapsed = new HashSet<string>();
        private readonly HashSet<string> _closedSections = new HashSet<string> { "LIBRARY" };
        private string _search = "";

        private float _treeScroll;
        private float _treeContent;
        private float _propScroll;
        private float _propContent;
        private Rect _screenRect;
        private Rect _treeScreenRect;
        private int _lastDrawFrame;
        private string _message = "";
        private float _messageUntil;
        private float _resetAllAt = -10f;
        private string _confirmDelete;
        private string _poseName = "";
        private string _selectedPose;
        private int _page;

        private string _editKey;
        private string _editText = "";
        private bool _valueDragMoved;

        public bool Open { get; private set; }

        public PoseEditorWindow(DuckCamController controller)
        {
            _c = controller;
        }

        public bool PointerOver
        {
            get
            {
                if (!Open || Time.frameCount - _lastDrawFrame > 2)
                {
                    return false;
                }
                Vector3 m = Input.mousePosition;
                var g = new Vector2(m.x, Screen.height - m.y);
                return _screenRect.Contains(g) || (_menu != null && _menuRect.Contains(g)) || _statusRect.Contains(g) || (_help && _helpRect.Contains(g));
            }
        }

        public void Show(PhotoActor actor)
        {
            if (!_c.Frozen)
            {
                _c.LastMessage = "Pose Mode works on characters in a frozen scene: press F6 first.";
                return;
            }
            actor = actor ?? _c.Actors.SelectedActor ?? _c.Actors.Actors.FirstOrDefault(a => a.Status == ActorStatus.Ready);
            if (actor == null || actor.Status != ActorStatus.Ready)
            {
                if (!_c.Actors.Actors.Any(a => a.Status == ActorStatus.Loading))
                {
                    _c.AddPmcActor();
                }
                if (!_c.Actors.Actors.Any(a => a.Status == ActorStatus.Loading || a.Status == ActorStatus.Ready))
                {
                    _c.LastMessage = "Pose Mode needs a photo character: add one in the CHARACTERS tab (your PMC, a copy of a bot, or a photo-only bot).";
                    return;
                }
                _actor = null;
                Open = true;
                if (!_helpShownThisRun)
                {
                    _helpShownThisRun = true;
                    _help = true;
                }
                _c.LastMessage = "Adding your PMC as a posable photo character; Pose Mode opens when it's ready.";
                return;
            }
            SwitchActor(actor);
            Open = true;
            if (Usable)
            {
                SkelBone pelvis = Skel.Find(Prefix + "Pelvis");
                PhotoLog.Info($"Pose Mode on '{actor.Name}': {Skel.Map.Count} bones, root {Rig.Root.position}, pelvis {(pelvis != null ? pelvis.Head.ToString() : "missing")}, screen {Screen.width}x{Screen.height}, camera rect {_c.Camera.Camera?.pixelRect}. {Skel.AxisReport}");
            }
            if (!_helpShownThisRun)
            {
                _helpShownThisRun = true;
                _help = true;
            }
            PoseLibrary.Refresh();
            if (string.IsNullOrEmpty(_poseName))
            {
                _poseName = PoseLibrary.NextDefaultName();
            }
            Say("Pose Mode: " + actor.Name + ". " + PoseKeymap.Of(PoseAction.Help) + " lists the shortcuts.");
        }

        public void Close()
        {
            CancelModal();
            _menu = null;
            _box = null;
            _editKey = null;
            Open = false;
        }

        public void Back()
        {
            if (_modal != null)
            {
                CancelModal();
                return;
            }
            if (_menu != null)
            {
                _menu = null;
                return;
            }
            if (_box != null || _boxArmed)
            {
                _box = null;
                _boxArmed = false;
                return;
            }
            if (_help)
            {
                _help = false;
                return;
            }
            Close();
            _c.LastMessage = "Left Pose Mode.";
        }

        public void Scroll(float notches)
        {
            Vector3 m = Input.mousePosition;
            if (_treeScreenRect.Contains(new Vector2(m.x, Screen.height - m.y)))
            {
                _treeScroll = Mathf.Max(0f, _treeScroll - notches * 3f * RowH);
            }
            else
            {
                _propScroll = Mathf.Max(0f, _propScroll - notches * 45f);
            }
        }

        private PoseEdit Edit => _actor?.PoseEditor;
        private ActorRig Rig => _actor?.Rig;
        private PoseSkeleton Skel => Rig?.Skeleton;

        private bool Usable => _actor != null && _actor.Status == ActorStatus.Ready && Rig != null && Skel != null && _c.Actors.Actors.Contains(_actor);

        private void SwitchActor(PhotoActor actor)
        {
            if (actor == _actor)
            {
                return;
            }
            CancelModal();
            _menu = null;
            _actor = actor;
            _treeScroll = 0f;
            _propScroll = 0f;
        }

        private HashSet<string> Hidden
        {
            get
            {
                if (!_hiddenBones.TryGetValue(_actor, out HashSet<string> h))
                {
                    _hiddenBones[_actor] = h = new HashSet<string>();
                }
                return h;
            }
        }

        private bool Visible(SkelBone b) => !_hiddenSets.Contains(b.Set) && !Hidden.Contains(b.Name);

        private SkelBone Active => Usable ? Skel.Find(Edit.State.Active) : null;

        private bool IsSelected(SkelBone b) => Edit.State.Selected.Contains(b.Name);

        private List<SkelBone> SelectedBones => Skel.Map.Where(IsSelected).ToList();

        private void Say(string text)
        {
            _message = text;
            _messageUntil = Time.unscaledTime + 5f;
        }

        private void SetSelection(IEnumerable<SkelBone> bones, SkelBone active, bool checkpoint = true)
        {
            List<string> names = bones.Where(b => b != null).Select(b => b.Name).Distinct().ToList();
            string act = active?.Name ?? (names.Contains(Edit.State.Active) ? Edit.State.Active : names.LastOrDefault());
            if (names.SequenceEqual(Edit.State.Selected) && act == Edit.State.Active)
            {
                return;
            }
            if (checkpoint)
            {
                Edit.Checkpoint();
            }
            Edit.State.Selected = names;
            Edit.State.Active = act;
        }

        private void ClickSelect(SkelBone b, bool extend)
        {
            if (b == null)
            {
                if (!extend)
                {
                    SetSelection(Enumerable.Empty<SkelBone>(), null);
                }
                return;
            }
            if (!extend)
            {
                SetSelection(new[] { b }, b);
                return;
            }
            List<SkelBone> sel = SelectedBones;
            if (IsSelected(b) && Edit.State.Active == b.Name)
            {
                sel.Remove(b);
                SetSelection(sel, sel.LastOrDefault());
            }
            else
            {
                if (!sel.Contains(b))
                {
                    sel.Add(b);
                }
                SetSelection(sel, b);
            }
        }

        public void Draw(float vw, float vh)
        {
            if (!Open)
            {
                return;
            }
            if (!Usable)
            {
                PhotoActor next = _c.Actors.Actors.FirstOrDefault(a => a.Status == ActorStatus.Ready);
                if (next == null && _c.Frozen && _c.Actors.Actors.Any(a => a.Status == ActorStatus.Loading))
                {
                    var wait = new Rect(20f, 64f, W, 70f);
                    _lastDrawFrame = Time.frameCount;
                    _screenRect = ToScreen(wait);
                    HudTheme.Fill(wait, new Color(0.03f, 0.03f, 0.035f, 0.95f));
                    HudTheme.Border(wait, HudTheme.P.LineBright);
                    GUI.Label(new Rect(wait.x + 12f, wait.y + 8f, W - 24f, 26f), "POSE MODE", SpawnerWindow.Left(HudTheme.Title, wrap: false));
                    GUI.Label(new Rect(wait.x + 12f, wait.y + 38f, W - 24f, 24f), "Loading your character...", SpawnerWindow.Left(HudTheme.Note, wrap: false));
                    return;
                }
                if (next == null || !_c.Frozen)
                {
                    Close();
                    _c.LastMessage = "Pose Mode closed: no posable character.";
                    return;
                }
                SwitchActor(next);
                if (!Usable)
                {
                    return;
                }
            }
            _lastDrawFrame = Time.frameCount;
            Color savedColor = GUI.color;
            GUI.color = new Color(savedColor.r, savedColor.g, savedColor.b, savedColor.a * Opacities[_opacity]);
            try
            {
                var win = new Rect(20f, 64f, W, vh - 64f - 120f);
                _screenRect = ToScreen(win);
                HudTheme.Fill(win, new Color(0.03f, 0.03f, 0.035f, 0.95f));
                HudTheme.Border(win, HudTheme.P.LineBright);
                float y = DrawHeader(win);
                float treeH = Mathf.Max(160f, (win.yMax - y) * 0.42f);
                y = DrawOutliner(new Rect(win.x + 10f, y, win.width - 20f, treeH));
                var view = new Rect(win.x + 10f, y + 6f, win.width - 20f, win.yMax - y - 14f);
                DrawScrolled(view, ref _propScroll, ref _propContent, DrawProperties);
            }
            finally
            {
                GUI.color = savedColor;
            }
        }

        private static Rect ToScreen(Rect r)
        {
            Vector2 tl = GUIUtility.GUIToScreenPoint(new Vector2(r.x, r.y));
            Vector2 br = GUIUtility.GUIToScreenPoint(new Vector2(r.xMax, r.yMax));
            return new Rect(tl, br - tl);
        }

        private static void DrawScrolled(Rect view, ref float scroll, ref float content, Func<Rect, float> body)
        {
            content = Mathf.Max(content, 1f);
            scroll = Mathf.Clamp(scroll, 0f, Mathf.Max(0f, content - view.height));
            GUI.BeginGroup(view);
            try
            {
                float end = body(new Rect(0f, -scroll, view.width, 0f));
                content = end + scroll + 6f;
            }
            finally
            {
                GUI.EndGroup();
            }
            if (content > view.height && Event.current.type == EventType.Repaint)
            {
                float h = Mathf.Max(24f, view.height * view.height / content);
                float t = scroll / Mathf.Max(1f, content - view.height);
                HudTheme.Fill(new Rect(view.xMax + 4f, view.y + (view.height - h) * t, 3f, h), HudTheme.P.LineBright);
            }
        }

        private float DrawHeader(Rect win)
        {
            var title = new Rect(win.x, win.y, win.width, 34f);
            HudTheme.Fill(title, HudTheme.P.TabFill);
            GUI.Label(new Rect(title.x + 12f, title.y + 3f, win.width - 110f, 28f), "POSE MODE · " + _actor.Name.ToUpperInvariant(), SpawnerWindow.Left(HudTheme.Title, wrap: false));
            if (SpawnerWindow.Button(new Rect(win.xMax - 72f, win.y + 4f, 28f, 26f), "?", _help))
            {
                _help = !_help;
            }
            if (SpawnerWindow.Button(new Rect(win.xMax - 38f, win.y + 4f, 28f, 26f), "X", false))
            {
                Close();
                _c.LastMessage = "Left Pose Mode.";
            }
            float y = win.y + 40f;
            float x = win.x + 10f;
            float w = win.width - 20f;
            string[] tools = { "TWEAK", "MOVE", "ROTATE" };
            float tw = 70f;
            for (int i = 0; i < tools.Length; i++)
            {
                if (SpawnerWindow.Button(new Rect(x + i * (tw + 3f), y, tw, 26f), tools[i], (int)_tool == i))
                {
                    _tool = (PoseTool)i;
                }
            }
            float rx = x + 3f * (tw + 3f) + 8f;
            float rw = (x + w - rx - 6f) / 3f;
            if (SpawnerWindow.Button(new Rect(rx, y, rw, 26f), _local ? "LOCAL" : "GLOBAL", false))
            {
                _local = !_local;
            }
            if (SpawnerWindow.Button(new Rect(rx + rw + 3f, y, rw, 26f), "MIRROR", _xMirror))
            {
                _xMirror = !_xMirror;
                Say(_xMirror ? "X-Axis Mirror on: edits copy to the bone on the other side." : "X-Axis Mirror off.");
            }
            if (SpawnerWindow.Button(new Rect(rx + (rw + 3f) * 2f, y, rw, 26f), "GIZMO", _gizmos))
            {
                _gizmos = !_gizmos;
            }
            y += 30f;
            float uw = (w - 9f) / 4f;
            if (SpawnerWindow.Button(new Rect(x, y, uw, 24f), "UNDO", false))
            {
                DoUndo();
            }
            if (SpawnerWindow.Button(new Rect(x + uw + 3f, y, uw, 24f), "REDO", false))
            {
                DoRedo();
            }
            if (SpawnerWindow.Button(new Rect(x + (uw + 3f) * 2f, y, uw, 24f), "POSE " + (Edit.Enabled ? "ON" : "OFF"), Edit.Enabled))
            {
                Edit.Enabled = !Edit.Enabled;
            }
            bool armed = Time.unscaledTime - _resetAllAt < 4f;
            if (SpawnerWindow.Button(new Rect(x + (uw + 3f) * 3f, y, uw, 24f), armed ? "SURE?" : "RESET ALL", armed))
            {
                if (armed)
                {
                    Edit.Checkpoint();
                    Edit.ResetAll();
                    _resetAllAt = -10f;
                    Say("Every edit cleared; back to the pose underneath.");
                }
                else
                {
                    _resetAllAt = Time.unscaledTime;
                    Say("Click RESET ALL again to clear every edit.");
                }
            }
            y += 28f;
            string msg = Time.unscaledTime < _messageUntil ? _message : (Edit.Enabled ? "Click a bone · G move · R rotate · A all · Alt+A none · " + PoseKeymap.Of(PoseAction.Help) + " keys" : "Pose OFF: showing the pose without your edits.");
            GUI.Label(new Rect(x, y, w, 20f), msg, SpawnerWindow.Left(HudTheme.Note, wrap: false));
            return y + 22f;
        }

        private float DrawOutliner(Rect area)
        {
            float x = area.x;
            float w = area.width;
            float y = area.y;
            GUI.Label(new Rect(x, y, 60f, 22f), "// BONES", SpawnerWindow.Left(HudTheme.Section, wrap: false));
            GUI.SetNextControlName(HudKit.FieldPrefix + "PoseSearch");
            _search = GUI.TextField(new Rect(x + 70f, y, w - 70f - 60f, 22f), _search ?? "", 32, HudTheme.Field);
            if (SpawnerWindow.Button(new Rect(x + w - 56f, y, 56f, 22f), "CLEAR", false))
            {
                _search = "";
            }
            y += 26f;
            float cw = (w - 4f * 4f) / 5f;
            for (int i = 0; i < SetLabels.Length; i++)
            {
                (BoneSet set, string label) = SetLabels[i];
                var r = new Rect(x + (i % 5) * (cw + 4f), y + (i / 5) * 24f, cw, 21f);
                bool shown = !_hiddenSets.Contains(set) && Skel.Map.Any(b => b.Set == set && !Hidden.Contains(b.Name));
                if (SpawnerWindow.Button(r, label, shown))
                {
                    if (Event.current.shift)
                    {
                        SetSelection(Skel.Map.Where(b => b.Set == set && !Hidden.Contains(b.Name)), null);
                        _hiddenSets.Remove(set);
                    }
                    else if (shown)
                    {
                        _hiddenSets.Add(set);
                    }
                    else
                    {
                        _hiddenSets.Remove(set);
                        Hidden.RemoveWhere(n => Skel.Find(n)?.Set == set);
                    }
                }
            }
            y += 50f;
            var view = new Rect(x, y, w - 8f, area.yMax - y);
            HudTheme.Fill(view, new Color(0f, 0f, 0f, 0.35f));
            _treeScreenRect = ToScreen(view);
            DrawScrolled(view, ref _treeScroll, ref _treeContent, DrawTree);
            return area.yMax;
        }

        private float DrawTree(Rect area)
        {
            float y = area.y;
            var rows = new List<(SkelBone bone, int indent)>();
            string q = (_search ?? "").Trim();
            if (q.Length > 0)
            {
                foreach (SkelBone b in Skel.Map)
                {
                    if (b.Set != BoneSet.Gear && b.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        rows.Add((b, 0));
                    }
                }
            }
            else
            {
                foreach (SkelBone root in Skel.MapRoots)
                {
                    AddRows(root, 0, rows);
                }
            }
            float viewTop = -area.y;
            foreach ((SkelBone b, int indent) in rows)
            {
                if (y + RowH >= 0f && y <= viewTop + 2000f)
                {
                    DrawTreeRow(new Rect(area.x, y, area.width, RowH - 1f), b, indent, q.Length > 0);
                }
                y += RowH;
            }
            if (rows.Count == 0)
            {
                GUI.Label(new Rect(area.x + 6f, y, area.width, 20f), "No bones match.", SpawnerWindow.Left(HudTheme.Note, wrap: false));
                y += 22f;
            }
            return y;
        }

        private void AddRows(SkelBone b, int indent, List<(SkelBone, int)> rows)
        {
            if (b.Set == BoneSet.Gear)
            {
                return;
            }
            rows.Add((b, indent));
            if (_collapsed.Contains(b.Name))
            {
                return;
            }
            foreach (SkelBone c in b.Children)
            {
                AddRows(c, indent + 1, rows);
            }
        }

        private void DrawTreeRow(Rect r, SkelBone b, int indent, bool flat)
        {
            bool sel = IsSelected(b);
            bool active = Edit.State.Active == b.Name;
            Event e = Event.current;
            if (e.type == EventType.Repaint)
            {
                if (active)
                {
                    HudTheme.Fill(r, new Color(0.25f, 0.5f, 0.85f, 0.55f));
                }
                else if (sel)
                {
                    HudTheme.Fill(r, new Color(0.2f, 0.35f, 0.6f, 0.4f));
                }
            }
            float ix = r.x + 4f + Mathf.Min(indent, 14) * 12f;
            if (!flat && b.Children.Count > 0)
            {
                bool collapsed = _collapsed.Contains(b.Name);
                if (GUI.Button(new Rect(ix, r.y + 2f, 16f, r.height - 4f), collapsed ? "+" : "-", SpawnerWindow.Centered(HudTheme.Value)))
                {
                    if (collapsed)
                    {
                        _collapsed.Remove(b.Name);
                    }
                    else
                    {
                        _collapsed.Add(b.Name);
                    }
                }
            }
            ix += 18f;
            bool vis = Visible(b);
            string label = flat ? b.Path.Replace(Prefix, "") : b.Short;
            GUIStyle style = SpawnerWindow.Left(active ? HudTheme.LabelSelected : HudTheme.Value, wrap: false);
            Color saved = GUI.color;
            if (!vis)
            {
                GUI.color = new Color(saved.r, saved.g, saved.b, saved.a * 0.45f);
            }
            var labelRect = new Rect(ix, r.y, r.xMax - ix - 26f, r.height);
            GUI.Label(labelRect, label, style);
            GUI.color = saved;
            if (Edit.IsEdited(b.Name) && e.type == EventType.Repaint)
            {
                HudTheme.Fill(new Rect(r.xMax - 18f, r.y + r.height * 0.5f - 3f, 6f, 6f), EditedBlue);
            }
            if (e.type == EventType.MouseDown && e.button == 0 && labelRect.Contains(e.mousePosition))
            {
                ClickSelect(b, e.shift || e.control);
                if (!vis)
                {
                    Hidden.Remove(b.Name);
                    _hiddenSets.Remove(b.Set);
                }
                e.Use();
            }
        }

        private void Section(ref float y, float x, float w, string title, out bool open)
        {
            open = !_closedSections.Contains(title);
            if (GUI.Button(new Rect(x, y, w, 22f), (open ? "- " : "+ ") + title, SpawnerWindow.Left(HudTheme.Section, wrap: false)))
            {
                if (open)
                {
                    _closedSections.Add(title);
                }
                else
                {
                    _closedSections.Remove(title);
                }
                open = !open;
            }
            y += 26f;
        }

        private float DrawProperties(Rect area)
        {
            float x = area.x;
            float w = area.width - 8f;
            float y = area.y;
            PoseEdit e = Edit;
            SkelBone b = Active;

            List<PhotoActor> actors = _c.Actors.Actors.Where(a => a.Status == ActorStatus.Ready).ToList();
            if (actors.Count > 1)
            {
                int idx = Mathf.Max(0, actors.IndexOf(_actor));
                if (SpawnerWindow.Button(new Rect(x, y, 28f, 24f), "<", false))
                {
                    SwitchActor(actors[(idx - 1 + actors.Count) % actors.Count]);
                }
                if (SpawnerWindow.Button(new Rect(x + w - 28f, y, 28f, 24f), ">", false))
                {
                    SwitchActor(actors[(idx + 1) % actors.Count]);
                }
                GUI.Label(new Rect(x + 32f, y, w - 64f, 24f), $"{_actor.Name}  ({idx + 1}/{actors.Count})", SpawnerWindow.Centered(HudTheme.Value));
                y += 30f;
                e = Edit;
                b = Active;
            }

            Section(ref y, x, w, "BONE", out bool open);
            if (open)
            {
                y = b == null ? Note(x, y, w, "Click a bone in the viewport or the list above.") : DrawBone(b, x, y, w);
            }

            Section(ref y, x, w, "OPTIONS", out open);
            if (open)
            {
                y = DrawOptions(x, y, w);
            }

            Section(ref y, x, w, "POSE TOOLS", out open);
            if (open)
            {
                y = DrawTools(x, y, w);
            }

            Section(ref y, x, w, "LIBRARY", out open);
            if (open)
            {
                y = DrawLibrary(x, y, w);
            }
            return y;
        }

        private static float Note(float x, float y, float w, string text)
        {
            float h = HudTheme.Note.CalcHeight(new GUIContent(text), w);
            GUI.Label(new Rect(x, y, w, h), text, HudTheme.Note);
            return y + h + 6f;
        }

        private float DrawBone(SkelBone b, float x, float y, float w)
        {
            PoseEdit e = Edit;
            GUI.Label(new Rect(x, y, w - 90f, 24f), b.Short + (e.IsEdited(b.Name) ? "  •" : ""), SpawnerWindow.Left(HudTheme.LabelSelected, wrap: false));
            GUI.Label(new Rect(x + w - 150f, y, 150f, 24f), b.Parent != null ? "parent " + b.Parent.Short : "root", SpawnerWindow.Left(HudTheme.Note, wrap: false));
            y += 26f;
            int count = e.State.Selected.Count;
            if (count > 1)
            {
                y = Note(x, y, w, $"{count} bones selected; the fields show the active one. G / R / Alt+R act on all of them.");
            }

            if (SpawnerWindow.Button(new Rect(x, y, (w - 4f) / 2f, 24f), "QUATERNION (WXYZ)", !_euler))
            {
                _euler = false;
            }
            if (SpawnerWindow.Button(new Rect(x + (w + 4f) / 2f, y, (w - 4f) / 2f, 24f), "XYZ EULER", _euler))
            {
                _euler = true;
            }
            y += 28f;

            Quaternion display = Quaternion.Inverse(b.Frame) * e.Rot(b.Name) * b.Frame;
            if (_euler)
            {
                Vector3 eul = PoseMath.ToEulerXyz(display);
                string[] axes = { "X", "Y", "Z" };
                for (int i = 0; i < 3; i++)
                {
                    float v = ValueRow(new Rect(x, y, w, 24f), "Rotation " + axes[i], eul[i], 0.4f, "0.0", "°", "eul" + i, out bool changed);
                    if (changed)
                    {
                        e.Checkpoint(b.Name + "eul" + i);
                        eul[i] = v;
                        SetDisplayRot(b, PoseMath.FromEulerXyz(eul));
                    }
                    y += 27f;
                }
            }
            else
            {
                float[] comps = { display.w, display.x, display.y, display.z };
                string[] names = { "W", "X", "Y", "Z" };
                for (int i = 0; i < 4; i++)
                {
                    float v = ValueRow(new Rect(x, y, w, 24f), "Quaternion " + names[i], comps[i], 0.004f, "0.000", "", "q" + i, out bool changed, i == 0 ? 1f : 0f);
                    if (changed)
                    {
                        e.Checkpoint(b.Name + "q" + i);
                        comps[i] = v;
                        var q = new Quaternion(comps[1], comps[2], comps[3], comps[0]);
                        SetDisplayRot(b, q);
                    }
                    y += 27f;
                }
            }

            if (b.LocationUnlocked)
            {
                Quaternion toDisplay = Quaternion.Inverse(b.BindLocal * b.Frame);
                Vector3 loc = toDisplay * e.Loc(b.Name);
                string[] axes = { "X", "Y", "Z" };
                for (int i = 0; i < 3; i++)
                {
                    float v = ValueRow(new Rect(x, y, w, 24f), "Location " + axes[i], loc[i], 0.002f, "0.000", " m", "loc" + i, out bool changed);
                    if (changed)
                    {
                        e.Checkpoint(b.Name + "loc" + i);
                        loc[i] = v;
                        e.SetLoc(b.Name, b.BindLocal * b.Frame * loc);
                    }
                    y += 27f;
                }
            }
            else
            {
                y = Note(x, y, w, "Location and scale are locked on game bones; only the Hips (Pelvis) can move.");
            }

            float bw = (w - 4f) / 2f;
            if (SpawnerWindow.Button(new Rect(x, y, bw, 24f), "CLEAR ROTATION (" + PoseKeymap.Of(PoseAction.ClearRotation) + ")", false))
            {
                ClearRotation();
            }
            if (SpawnerWindow.Button(new Rect(x + bw + 4f, y, bw, 24f), "CLEAR LOCATION (" + PoseKeymap.Of(PoseAction.ClearLocation) + ")", false))
            {
                ClearLocation();
            }
            y += 30f;

            PoseLimb? limb = b.Part?.Limb;
            if (limb.HasValue && Rig.Chain(limb.Value) != null)
            {
                y = DrawPin(limb.Value, x, y, w);
            }
            if ((b.Short == "Head" || b.Short == "Neck") && _actor.Pose.Look != LookMode.Off)
            {
                y = Note(x, y, w, "Look At (CHARACTERS tab) is on: it turns the head after these edits.");
            }
            return y + 4f;
        }

        private void SetDisplayRot(SkelBone b, Quaternion display)
        {
            Quaternion offset = b.Frame * PoseMath.Normalized(display) * Quaternion.Inverse(b.Frame);
            Edit.SetRot(b.Name, offset);
            MirrorEdit(b, offset);
        }

        private void MirrorEdit(SkelBone b, Quaternion offset)
        {
            if (!_xMirror || b.Mirror == null || IsSelected(b.Mirror))
            {
                return;
            }
            Edit.SetRot(b.Mirror.Name, ActorRig.MirrorOffset(b, b.Mirror, offset));
        }

        private float DrawPin(PoseLimb limb, float x, float y, float w)
        {
            PinState pin = Edit.Pin(limb, false);
            PinMode mode = pin?.Mode ?? PinMode.Off;
            GUI.Label(new Rect(x, y, w, 20f), "IK PIN (temporary; real IK with poles arrives in stage 4)", SpawnerWindow.Left(HudTheme.Note, wrap: false));
            y += 22f;
            string[] modes = { "PIN OFF", "WORLD", "BODY" };
            float mw = (w - 8f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                if (SpawnerWindow.Button(new Rect(x + i * (mw + 4f), y, mw, 24f), modes[i], (int)mode == i))
                {
                    SetPin(limb, (PinMode)i);
                }
            }
            y += 28f;
            if (pin != null && pin.Mode != PinMode.Off)
            {
                bool foot = limb == PoseLimb.LeftFoot || limb == PoseLimb.RightFoot;
                float s = ValueRow(new Rect(x, y, w, 24f), foot ? "Knee swivel" : "Elbow swivel", pin.Swivel, 0.5f, "0", "°", "swivel", out bool sc);
                if (sc)
                {
                    Edit.Checkpoint("swivel");
                    pin.Swivel = Mathf.DeltaAngle(0f, s);
                }
                y += 28f;
            }
            return y;
        }

        private void SetPin(PoseLimb limb, PinMode mode)
        {
            PinState pin = Edit.Pin(limb, true);
            if (pin.Mode == mode)
            {
                return;
            }
            Edit.Checkpoint();
            if (mode == PinMode.Off)
            {
                Transform[] chain = Rig.Chain(limb);
                if (chain != null)
                {
                    Rig.Bake(new[] { chain[0], chain[1] }, Edit);
                }
                pin.Mode = PinMode.Off;
                pin.Point = null;
                return;
            }
            Rig.SetPinMode(limb, pin, mode);
        }

        private float DrawOptions(float x, float y, float w)
        {
            PoseEdit e = Edit;
            float hw = (w - 4f) / 2f;
            GUI.Label(new Rect(x, y, 90f, 24f), "BASE", SpawnerWindow.Left(HudTheme.Label, wrap: false));
            float bx = x + 90f;
            float bw = (w - 90f - 4f) / 2f;
            if (SpawnerWindow.Button(new Rect(bx, y, bw, 24f), "ANIMATION", !e.State.RestBase))
            {
                if (e.State.RestBase)
                {
                    e.Checkpoint();
                    e.State.RestBase = false;
                }
            }
            if (SpawnerWindow.Button(new Rect(bx + bw + 4f, y, bw, 24f), "REST POSE", e.State.RestBase))
            {
                if (!e.State.RestBase)
                {
                    e.Checkpoint();
                    e.State.RestBase = true;
                    Say("Rest pose: edits now sit on the model's bind pose, like Blender's rest position.");
                }
            }
            y += 28f;
            if (SpawnerWindow.Button(new Rect(x, y, hw, 24f), "WRIST TWIST SPREAD", Rig.SpreadWristTwist))
            {
                Rig.SpreadWristTwist = !Rig.SpreadWristTwist;
                Say(Rig.SpreadWristTwist ? "Hand twist spreads 18 / 20 / 27% over the forearm, like both Blender rigs." : "Hand twist stays on the hand.");
            }
            if (SpawnerWindow.Button(new Rect(x + hw + 4f, y, hw, 24f), "AUTO IK (PINS)", _autoIk))
            {
                _autoIk = !_autoIk;
                Say(_autoIk ? "Auto IK on: G on a hand or foot moves it and pins it." : "Auto IK off.");
            }
            y += 28f;
            if (SpawnerWindow.Button(new Rect(x, y, hw, 24f), $"WINDOW {Opacities[_opacity] * 100f:0}%", false))
            {
                _opacity = (_opacity + 1) % Opacities.Length;
            }
            bool eyes = _c.Camera.ActorSubject == _actor;
            if (SpawnerWindow.Button(new Rect(x + hw + 4f, y, hw, 24f), "1ST PERSON", eyes))
            {
                ToggleEyes();
            }
            y += 28f;
            if (_actor?.HeldWeapon != null)
            {
                if (SpawnerWindow.Button(new Rect(x, y, hw, 24f), "WEAPON GIZMO", _weaponMode))
                {
                    CancelModal();
                    _weaponMode = !_weaponMode;
                    Say(_weaponMode ? "Weapon gizmo: G moves and R rotates the weapon; it follows the right hand. Click a bone to go back to bones." : "Gizmo back on bones.");
                }
                if (SpawnerWindow.Button(new Rect(x + hw + 4f, y, hw, 24f), "RESET WEAPON", false))
                {
                    Edit.Checkpoint();
                    Edit.State.WeaponPos = null;
                    Edit.State.WeaponRot = null;
                    _actor.ResetGrip();
                    Say("Weapon back in the hand where the pose put it.");
                }
                y += 28f;
            }
            else
            {
                _weaponMode = false;
            }
            y += 2f;
            return y;
        }

        private void ToggleEyes()
        {
            if (_c.Camera.ActorSubject == _actor)
            {
                _c.Camera.StopSpectating();
                _c.LastMessage = "Back to the free camera.";
            }
            else
            {
                _c.Camera.ViewThroughActor(_actor);
                _c.LastMessage = "First person from " + _actor.Name + ". Press 1ST PERSON again to leave.";
            }
        }

        private float DrawTools(float x, float y, float w)
        {
            float tw = (w - 8f) / 3f;
            if (SpawnerWindow.Button(new Rect(x, y, tw, 24f), "COPY POSE", false))
            {
                CopyPose();
            }
            if (SpawnerWindow.Button(new Rect(x + tw + 4f, y, tw, 24f), "PASTE", false))
            {
                PastePose(false);
            }
            if (SpawnerWindow.Button(new Rect(x + (tw + 4f) * 2f, y, tw, 24f), "PASTE FLIPPED", false))
            {
                PastePose(true);
            }
            y += 28f;
            float hw = (w - 4f) / 2f;
            if (SpawnerWindow.Button(new Rect(x, y, hw, 24f), "T POSE", false))
            {
                LoadFull(Rig.Neutral(aPose: false), "T pose. RESET ALL goes back to the pose underneath.");
            }
            if (SpawnerWindow.Button(new Rect(x + hw + 4f, y, hw, 24f), "A POSE", false))
            {
                LoadFull(Rig.Neutral(aPose: true), "A pose. RESET ALL goes back to the pose underneath.");
            }
            y += 28f;
            SkelBone a = Active;
            bool? left = a == null ? (bool?)null
                : a.Set == BoneSet.ArmL || a.Set == BoneSet.FingersL ? true
                : a.Set == BoneSet.ArmR || a.Set == BoneSet.FingersR ? false : (bool?)null;
            if (left.HasValue)
            {
                GUI.Label(new Rect(x, y, w, 20f), "HAND PRESETS · " + (left.Value ? "LEFT" : "RIGHT"), SpawnerWindow.Left(HudTheme.Label, wrap: false));
                y += 22f;
                string[] presets = { "Open", "Relaxed", "Fist", "Point", "Grip" };
                float pw = (w - 16f) / presets.Length;
                for (int i = 0; i < presets.Length; i++)
                {
                    if (SpawnerWindow.Button(new Rect(x + i * (pw + 4f), y, pw, 24f), presets[i].ToUpperInvariant(), false))
                    {
                        HandPreset(left.Value, presets[i]);
                    }
                }
                y += 28f;
            }
            else
            {
                y = Note(x, y, w, "Select an arm or finger bone for hand presets.");
            }
            return y;
        }

        private void LoadFull(PoseFile file, string what)
        {
            if (file?.Bones == null || file.Bones.Count == 0)
            {
                Say("That pose is empty.");
                return;
            }
            var bad = file.Bones.Where(kv => kv.Value == null || kv.Value.Length != 4 || kv.Value.Any(v => float.IsNaN(v) || float.IsInfinity(v))).Select(kv => kv.Key).ToList();
            int known = file.Bones.Keys.Count(k => Skel.Find(k) != null);
            if (known == 0)
            {
                Say("That pose has no bones this character has.");
                return;
            }
            Edit.Checkpoint();
            PoseState s = Edit.State;
            s.FullPose = file.Bones.Where(kv => !bad.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            s.FullHips = file.Hips != null && file.Hips.Length == 3 && file.Hips.All(v => !float.IsNaN(v) && !float.IsInfinity(v) && Mathf.Abs(v) < 100f) ? file.Hips : null;
            s.Rot.Clear();
            s.Loc.Clear();
            s.Pins.Clear();
            s.RestBase = false;
            int missing = Skel.Map.Count(m => !file.Bones.ContainsKey(m.Name));
            Say(what + (bad.Count > 0 ? $" {bad.Count} invalid bone(s) skipped." : "") + (missing > 0 ? $" {missing} bone(s) not in the file keep the pose underneath." : ""));
        }

        private void HandPreset(bool left, string preset)
        {
            if (_actor.Fingers.Count == 0)
            {
                Say("This character has no finger data to build hand presets from.");
                return;
            }
            Edit.Checkpoint();
            int count = 0;
            foreach (SkelBone b in Skel.Map.Where(x => x.Set == (left ? BoneSet.FingersL : BoneSet.FingersR)))
            {
                (Transform bone, Quaternion rest, Quaternion grip) f = _actor.Fingers.FirstOrDefault(x => x.bone == b.Bone);
                if (f.bone == null || b.Short.Length < 7)
                {
                    continue;
                }
                int finger = b.Short[b.Short.Length - 2] - '0';
                float t;
                switch (preset)
                {
                    case "Open": t = 0f; break;
                    case "Relaxed": t = 0.35f; break;
                    case "Grip": t = 1f; break;
                    case "Point": t = finger == 2 ? 0f : finger == 1 ? 1f : 1.5f; break;
                    default: t = 1.5f; break;
                }
                Quaternion desired = Quaternion.SlerpUnclamped(f.rest, f.grip, t);
                Quaternion offset = Quaternion.Inverse(Rig.BaseUnder(b, Edit.State)) * desired;
                Edit.SetRot(b.Name, offset);
                MirrorEdit(b, offset);
                count++;
            }
            Say(count > 0 ? $"{preset} ({(left ? "left" : "right")} hand)." : "No finger bones found on that hand.");
        }

        private float DrawLibrary(float x, float y, float w)
        {
            GUI.Label(new Rect(x, y, 60f, 24f), "NAME", SpawnerWindow.Left(HudTheme.Label, wrap: false));
            GUI.SetNextControlName(HudKit.FieldPrefix + "PoseName");
            _poseName = GUI.TextField(new Rect(x + 60f, y + 1f, w - 60f - 112f, 22f), _poseName ?? "", 40, HudTheme.Field);
            if (SpawnerWindow.Button(new Rect(x + w - 106f, y, 106f, 24f), "SAVE NEW", false))
            {
                string name = PoseLibrary.Clean(_poseName);
                string unique = name;
                for (int n = 2; PoseLibrary.List().Contains(unique, StringComparer.OrdinalIgnoreCase); n++)
                {
                    unique = $"{name} {n}";
                }
                try
                {
                    PoseLibrary.Save(Rig.Capture(unique));
                    _selectedPose = unique;
                    _poseName = PoseLibrary.NextDefaultName();
                    Say($"Saved '{unique}'.");
                }
                catch (Exception ex)
                {
                    Say("Save failed: " + ex.Message);
                }
            }
            y += 28f;
            string[] poses = PoseLibrary.List();
            int pages = Mathf.Max(1, Mathf.CeilToInt(poses.Length / (float)PosesPerPage));
            _page = Mathf.Clamp(_page, 0, pages - 1);
            if (poses.Length == 0)
            {
                y = Note(x, y, w, "No saved poses yet.");
            }
            foreach (string pose in poses.Skip(_page * PosesPerPage).Take(PosesPerPage))
            {
                if (SpawnerWindow.Button(new Rect(x, y, w, 24f), pose, pose == _selectedPose))
                {
                    _selectedPose = pose == _selectedPose ? null : pose;
                    _confirmDelete = null;
                }
                y += 27f;
            }
            if (pages > 1)
            {
                if (SpawnerWindow.Button(new Rect(x, y, 36f, 24f), "<", false))
                {
                    _page = (_page - 1 + pages) % pages;
                }
                GUI.Label(new Rect(x + 40f, y, w - 80f, 24f), $"PAGE {_page + 1} / {pages}", SpawnerWindow.Centered(HudTheme.Value));
                if (SpawnerWindow.Button(new Rect(x + w - 36f, y, 36f, 24f), ">", false))
                {
                    _page = (_page + 1) % pages;
                }
                y += 28f;
            }
            if (_selectedPose != null && poses.Contains(_selectedPose))
            {
                string[] acts = { "LOAD", "OVERWRITE", "RENAME", _confirmDelete == _selectedPose ? "SURE?" : "DELETE" };
                float aw = (w - 12f) / acts.Length;
                for (int i = 0; i < acts.Length; i++)
                {
                    if (!SpawnerWindow.Button(new Rect(x + i * (aw + 4f), y, aw, 24f), acts[i], i == 3 && _confirmDelete == _selectedPose))
                    {
                        continue;
                    }
                    try
                    {
                        switch (i)
                        {
                            case 0:
                                LoadFull(PoseLibrary.Load(_selectedPose), $"Loaded '{_selectedPose}'.");
                                break;
                            case 1:
                                PoseLibrary.Save(Rig.Capture(_selectedPose));
                                Say($"Overwrote '{_selectedPose}'.");
                                break;
                            case 2:
                                string to = PoseLibrary.Clean(_poseName);
                                if (PoseLibrary.Rename(_selectedPose, to))
                                {
                                    Say($"Renamed to '{to}'.");
                                    _selectedPose = to;
                                }
                                else
                                {
                                    Say("Type a new, unused name in NAME first.");
                                }
                                break;
                            case 3:
                                if (_confirmDelete == _selectedPose)
                                {
                                    PoseLibrary.Delete(_selectedPose);
                                    Say($"Deleted '{_selectedPose}'.");
                                    _selectedPose = null;
                                    _confirmDelete = null;
                                }
                                else
                                {
                                    _confirmDelete = _selectedPose;
                                    Say("Click again to delete.");
                                }
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Say("Pose file error: " + ex.Message);
                    }
                }
                y += 28f;
            }
            if (SpawnerWindow.Button(new Rect(x, y, w, 24f), "OPEN POSES FOLDER", false))
            {
                try
                {
                    System.IO.Directory.CreateDirectory(PoseLibrary.Directory);
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + PoseLibrary.Directory + "\"");
                }
                catch (Exception ex)
                {
                    Say("Could not open the folder: " + ex.Message);
                }
            }
            y += 28f;
            return Note(x, y, w, "A saved pose keeps every bone of the visible pose (IK included) and loads on any character.");
        }

        private float ValueRow(Rect r, string label, float value, float perPixel, string format, string suffix, string key, out bool changed, float reset = 0f)
        {
            changed = false;
            HudTheme.Fill(r, HudTheme.P.RowFill);
            GUI.Label(new Rect(r.x + 8f, r.y, 140f, r.height), label.ToUpperInvariant(), SpawnerWindow.Left(HudTheme.Label, wrap: false));
            var box = new Rect(r.x + 150f, r.y + 2f, r.width - 150f - 34f, r.height - 4f);
            string fieldKey = (Edit.State.Active ?? "") + "." + key;
            Event e = Event.current;
            if (_editKey == fieldKey)
            {
                string name = HudKit.FieldPrefix + "PoseValue";
                GUI.SetNextControlName(name);
                _editText = GUI.TextField(box, _editText ?? "", 16, HudTheme.Field);
                if (GUI.GetNameOfFocusedControl() != name && e.type == EventType.Repaint)
                {
                    GUI.FocusControl(name);
                }
                if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
                {
                    if (float.TryParse((_editText ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float typed) && !float.IsNaN(typed) && !float.IsInfinity(typed))
                    {
                        value = typed;
                        changed = true;
                    }
                    _editKey = null;
                    InputContext.LeaveField();
                    e.Use();
                }
                else if (e.type == EventType.MouseDown && !box.Contains(e.mousePosition))
                {
                    _editKey = null;
                }
            }
            else
            {
                int id = GUIUtility.GetControlID(FocusType.Passive);
                switch (e.GetTypeForControl(id))
                {
                    case EventType.MouseDown:
                        if (e.button == 0 && box.Contains(e.mousePosition))
                        {
                            GUIUtility.hotControl = id;
                            _valueDragMoved = false;
                            e.Use();
                        }
                        break;
                    case EventType.MouseDrag:
                        if (GUIUtility.hotControl == id)
                        {
                            value += e.delta.x * perPixel * (e.shift ? 0.1f : 1f);
                            _valueDragMoved |= Mathf.Abs(e.delta.x) > 0f;
                            changed = true;
                            e.Use();
                        }
                        break;
                    case EventType.MouseUp:
                        if (GUIUtility.hotControl == id)
                        {
                            GUIUtility.hotControl = 0;
                            Edit?.EndGesture();
                            if (!_valueDragMoved)
                            {
                                _editKey = fieldKey;
                                _editText = value.ToString(format, CultureInfo.InvariantCulture);
                            }
                            e.Use();
                        }
                        break;
                    case EventType.Repaint:
                        bool hot = GUIUtility.hotControl == id;
                        HudTheme.Fill(box, hot ? HudTheme.P.Highlight : HudTheme.P.ValueBox);
                        GUI.Label(box, value.ToString(format, CultureInfo.InvariantCulture) + suffix, SpawnerWindow.Centered(hot ? HudTheme.BoxTextHi : HudTheme.BoxText));
                        break;
                }
            }
            if (SpawnerWindow.Button(new Rect(box.xMax + 4f, r.y + 2f, 30f, r.height - 4f), reset.ToString("0", CultureInfo.InvariantCulture), false))
            {
                value = reset;
                changed = true;
            }
            return value;
        }
    }
}
