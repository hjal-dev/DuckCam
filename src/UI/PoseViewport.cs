using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DuckCam.Actors;
using DuckCam.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuckCam.UI
{
    internal sealed partial class PoseEditorWindow
    {
        private const float GizmoRadius = 70f;
        private const float ViewRing = 84f;
        private const float PickDistance = 10f;

        private static readonly Color AxisX = new Color(1f, 0.2f, 0.32f, 1f);
        private static readonly Color AxisY = new Color(0.55f, 0.86f, 0.2f, 1f);
        private static readonly Color AxisZ = new Color(0.16f, 0.56f, 1f, 1f);
        private static readonly Color BoneIdle = new Color(0.78f, 0.78f, 0.78f, 0.8f);
        private static readonly Color BoneSelected = new Color(0.25f, 0.6f, 1f, 1f);
        private static readonly Color BoneActive = new Color(0.62f, 0.9f, 1f, 1f);
        private static readonly Color BoneHover = new Color(1f, 1f, 1f, 1f);

        private static Material _lineMaterial;
        private static Dictionary<string, Quaternion> _clipRot = new Dictionary<string, Quaternion>();
        private static Dictionary<string, Vector3> _clipLoc = new Dictionary<string, Vector3>();

        private enum ModalKind
        {
            Rotate,
            Trackball,
            Move
        }

        private enum Nav
        {
            None,
            Orbit,
            Pan,
            Zoom
        }

        private sealed class ModalBone
        {
            public SkelBone Bone;
            public Quaternion OffsetStart;
            public Quaternion WorldStart;
            public Vector3 LocStart;
            public Vector3[] Axes;
        }

        private sealed class ModalPin
        {
            public PoseLimb Limb;
            public Vector3 Start;
        }

        private sealed class Modal
        {
            public ModalKind Kind;
            public int Axis = -1;
            public bool Local;
            public bool Plane;
            public string Typed = "";
            public bool FromGizmo;
            public Vector3 Pivot;
            public Vector2 PivotGui;
            public Vector2 StartMouse;
            public Vector2 Virtual;
            public Vector2 LastMouse;
            public float Angle;
            public float LastRaw;
            public string Snapshot;
            public List<ModalBone> Bones = new List<ModalBone>();
            public List<ModalPin> Pins = new List<ModalPin>();
            public Vector3 CamForward;
            public Vector3 CamRight;
            public Vector3 CamUp;
            public Vector3[] ActiveAxes;
            public string Readout = "";
            public bool Weapon;
            public Vector3 WeaponPosStart;
            public Quaternion WeaponRotStart = Quaternion.identity;
        }

        private sealed class Box
        {
            public Vector2 Start;
            public Vector2 End;
            public int Mode;
        }

        private sealed class MenuItem
        {
            public string Label;
            public string Hint;
            public Action Run;
        }

        private Modal _modal;
        private Box _box;
        private bool _boxArmed;
        private List<MenuItem> _menu;
        private string _menuTitle;
        private Vector2 _menuPos;
        private Rect _menuRect;
        private Rect _statusRect;
        private Rect _helpRect;

        private Nav _nav;
        private Vector2 _navLast;
        private bool _pressing;
        private Vector2 _press;
        private SkelBone _pressBone;
        private SkelBone _hover;
        private int _hoverGizmo = -2;
        private ModalKind _hoverGizmoKind;

        private float _rmbDownAt;
        private float _rmbTravel;
        private bool _rmbFly;
        private bool _rmbBlocked;

        private readonly List<(Vector2 a, Vector2 b, Color c)> _lines = new List<(Vector2, Vector2, Color)>();

        public bool AllowsFly => Open && _modal == null && !_rmbBlocked && _rmbFly;

        private static Vector2 GuiMouse()
        {
            Vector3 m = Input.mousePosition;
            return new Vector2(m.x, Screen.height - m.y);
        }

        private static Vector2 GuiToScreen(Vector2 g) => new Vector2(g.x, Screen.height - g.y);

        private static Vector3 WorldAxis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

        private static Color AxisColor(int i) => i == 0 ? AxisX : i == 1 ? AxisY : AxisZ;

        private static string AxisName(int i) => i == 0 ? "X" : i == 1 ? "Y" : "Z";

        public void HandleKeys()
        {
            if (!Open || !Usable)
            {
                return;
            }
            UnityEngine.Camera cam = _c.Camera.Camera;
            if (cam == null)
            {
                return;
            }
            Vector2 mouse = GuiMouse();
            bool overUi = _c.PointerOverUi;
            TrackRmb(mouse, overUi);
            if (_modal != null)
            {
                TickModal(cam, mouse);
                return;
            }
            if (Input.GetMouseButton(1))
            {
                return;
            }
            if (TickNavigation(cam, mouse, overUi))
            {
                return;
            }
            if (_menu != null)
            {
                if ((Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(2)) && !overUi)
                {
                    _menu = null;
                }
                return;
            }
            _hover = overUi || _pressing || _box != null ? null : Pick(cam, mouse);
            _hoverGizmo = overUi || _pressing || _box != null ? -2 : GizmoHit(cam, mouse, out _hoverGizmoKind);
            TickMouse(cam, mouse, overUi);
            TickShortcuts(mouse);
        }

        private void TrackRmb(Vector2 mouse, bool overUi)
        {
            if (Input.GetMouseButtonDown(1))
            {
                _rmbDownAt = Time.unscaledTime;
                _rmbTravel = 0f;
                _rmbFly = false;
                _rmbBlocked = overUi;
                if (_modal != null)
                {
                    CancelModal();
                    _rmbBlocked = true;
                }
                if (_menu != null)
                {
                    _menu = null;
                    _rmbBlocked = true;
                }
                if (_box != null || _boxArmed)
                {
                    _box = null;
                    _boxArmed = false;
                    _rmbBlocked = true;
                }
            }
            if (Input.GetMouseButton(1))
            {
                _rmbTravel += Mathf.Abs(Input.GetAxisRaw("Mouse X")) + Mathf.Abs(Input.GetAxisRaw("Mouse Y"));
                if (!_rmbBlocked && (Time.unscaledTime - _rmbDownAt > 0.2f || _rmbTravel > 0.8f))
                {
                    _rmbFly = true;
                }
            }
            if (Input.GetMouseButtonUp(1))
            {
                if (!_rmbBlocked && !_rmbFly && !overUi && _modal == null)
                {
                    OpenContextMenu(mouse);
                }
                _rmbFly = false;
                _rmbBlocked = false;
            }
        }

        private Vector3 NavPivot()
        {
            List<SkelBone> sel = SelectedBones;
            if (sel.Count > 0)
            {
                Vector3 sum = Vector3.zero;
                foreach (SkelBone b in sel)
                {
                    sum += b.Head;
                }
                return sum / sel.Count;
            }
            SkelBone pelvis = Skel.Find(Prefix + "Pelvis");
            return pelvis != null ? pelvis.Head : Rig.Root.position + Vector3.up;
        }

        private bool TickNavigation(UnityEngine.Camera cam, Vector2 mouse, bool overUi)
        {
            if (_nav == Nav.None && !overUi && Input.GetMouseButtonDown(2))
            {
                _nav = Chord.CtrlHeld ? Nav.Zoom : Chord.ShiftHeld ? Nav.Pan : Nav.Orbit;
                _navLast = mouse;
                _menu = null;
            }
            if (_nav != Nav.None)
            {
                if (!Input.GetMouseButton(2))
                {
                    _nav = Nav.None;
                    return true;
                }
                Vector2 d = mouse - _navLast;
                _navLast = mouse;
                Vector3 pivot = NavPivot();
                switch (_nav)
                {
                    case Nav.Orbit:
                        _c.Camera.OrbitAround(pivot, d.x * 0.3f, d.y * 0.3f);
                        break;
                    case Nav.Pan:
                        _c.Camera.PanView(new Vector2(d.x, -d.y), Vector3.Dot(pivot - cam.transform.position, cam.transform.forward));
                        break;
                    case Nav.Zoom:
                        _c.Camera.DollyToward(pivot, -d.y * 0.006f);
                        break;
                }
                return true;
            }
            if (!overUi)
            {
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f)
                {
                    _c.Camera.DollyToward(NavPivot(), Mathf.Clamp(wheel, -3f, 3f) * 0.12f);
                }
            }
            return false;
        }

        private void TickMouse(UnityEngine.Camera cam, Vector2 mouse, bool overUi)
        {
            if (Input.GetMouseButtonDown(0) && !overUi)
            {
                InputContext.LeaveField();
                _editKey = null;
                if (_boxArmed)
                {
                    _boxArmed = false;
                    _box = new Box { Start = mouse, End = mouse, Mode = Chord.ShiftHeld ? 1 : Chord.CtrlHeld ? 2 : 0 };
                    return;
                }
                int g = GizmoHit(cam, mouse, out ModalKind kind);
                if (g > -2)
                {
                    StartModal(kind, g, true, mouse);
                    return;
                }
                _pressing = true;
                _press = mouse;
                _pressBone = Pick(cam, mouse);
                if (_pressBone != null)
                {
                    _weaponMode = false;
                }
            }
            if (_pressing)
            {
                if (Input.GetMouseButton(0) && (mouse - _press).magnitude > 5f)
                {
                    _pressing = false;
                    if (_pressBone != null)
                    {
                        if (!IsSelected(_pressBone))
                        {
                            ClickSelect(_pressBone, false);
                        }
                        StartModal(CanMove(Tops()) ? ModalKind.Move : ModalKind.Rotate, -1, true, _press);
                    }
                    else
                    {
                        _box = new Box { Start = _press, End = mouse, Mode = Chord.ShiftHeld ? 1 : Chord.CtrlHeld ? 2 : 0 };
                    }
                }
                else if (!Input.GetMouseButton(0))
                {
                    _pressing = false;
                    ClickSelect(_pressBone, Chord.ShiftHeld || Chord.CtrlHeld);
                }
            }
            if (_box != null)
            {
                _box.End = mouse;
                if (!Input.GetMouseButton(0))
                {
                    ApplyBox(cam, _box);
                    _box = null;
                }
            }
        }

        private void TickShortcuts(Vector2 mouse)
        {
            if (PoseKeymap.Down(PoseAction.Move)) StartModal(ModalKind.Move, -1, false, mouse);
            else if (PoseKeymap.Down(PoseAction.Rotate)) StartModal(ModalKind.Rotate, -1, false, mouse);
            else if (PoseKeymap.Down(PoseAction.Scale)) Say("Scale is locked on game bones (it would distort the mesh).");
            else if (PoseKeymap.Down(PoseAction.ClearRotation)) ClearRotation();
            else if (PoseKeymap.Down(PoseAction.ClearLocation)) ClearLocation();
            else if (PoseKeymap.Down(PoseAction.ClearScale)) Say("Nothing to clear: scale is locked on game bones.");
            else if (PoseKeymap.Down(PoseAction.SelectAll)) SetSelection(Skel.Map.Where(Visible), Active);
            else if (PoseKeymap.Down(PoseAction.DeselectAll)) SetSelection(Enumerable.Empty<SkelBone>(), null);
            else if (PoseKeymap.Down(PoseAction.InvertSelection)) SetSelection(Skel.Map.Where(b => Visible(b) && !IsSelected(b)), null);
            else if (PoseKeymap.Down(PoseAction.SelectParent)) SelectParent(false);
            else if (PoseKeymap.Down(PoseAction.SelectParentExtend)) SelectParent(true);
            else if (PoseKeymap.Down(PoseAction.SelectChild)) SelectChild(false);
            else if (PoseKeymap.Down(PoseAction.SelectChildExtend)) SelectChild(true);
            else if (PoseKeymap.Down(PoseAction.SelectMirror)) SelectMirror();
            else if (PoseKeymap.Down(PoseAction.BoxSelect))
            {
                _boxArmed = true;
                Say("Box select: drag with the left mouse (Shift adds, Ctrl removes).");
            }
            else if (PoseKeymap.Down(PoseAction.Hide)) HideSelected(false);
            else if (PoseKeymap.Down(PoseAction.HideUnselected)) HideSelected(true);
            else if (PoseKeymap.Down(PoseAction.Reveal)) Reveal();
            else if (PoseKeymap.Down(PoseAction.CopyPose)) CopyPose();
            else if (PoseKeymap.Down(PoseAction.PastePose)) PastePose(false);
            else if (PoseKeymap.Down(PoseAction.PastePoseFlipped)) PastePose(true);
            else if (PoseKeymap.Down(PoseAction.Undo)) DoUndo();
            else if (PoseKeymap.Down(PoseAction.Redo) || PoseKeymap.Down(PoseAction.RedoAlt)) DoRedo();
            else if (PoseKeymap.Down(PoseAction.ContextMenu)) OpenContextMenu(mouse);
            else if (PoseKeymap.Down(PoseAction.ViewMenu)) OpenViewMenu(mouse);
            else if (PoseKeymap.Down(PoseAction.ViewAll)) FrameAll();
            else if (PoseKeymap.Down(PoseAction.ViewSelected)) FrameSelected();
            else if (PoseKeymap.Down(PoseAction.OrientationMenu)) OpenOrientationMenu(mouse);
            else if (PoseKeymap.Down(PoseAction.ToggleGizmo)) _gizmos = !_gizmos;
            else if (PoseKeymap.Down(PoseAction.Help)) _help = !_help;
        }

        private void DoUndo()
        {
            CancelModal();
            Say(Edit.Undo() ? "Undone." : "Nothing to undo.");
        }

        private void DoRedo()
        {
            CancelModal();
            Say(Edit.Redo() ? "Redone." : "Nothing to redo.");
        }

        private List<SkelBone> Tops()
        {
            List<SkelBone> sel = SelectedBones;
            return sel.Where(b => !sel.Any(o => o != b && b.IsDescendantOf(o))).ToList();
        }

        private bool CanMove(List<SkelBone> tops) => tops.Any(b => b.LocationUnlocked) || (_autoIk && tops.Any(b => b.Part?.Limb != null && Rig.Chain(b.Part.Limb.Value) != null));

        private SkelBone Pick(UnityEngine.Camera cam, Vector2 mouse)
        {
            SkelBone best = null;
            float bestDist = PickDistance;
            float bestDepth = float.MaxValue;
            foreach (SkelBone b in Skel.Map)
            {
                if (!Visible(b) || !ScreenSpace.WorldToGui(cam, b.Head, out Vector2 h) || !ScreenSpace.WorldToGui(cam, b.Tail, out Vector2 t))
                {
                    continue;
                }
                float d = PoseMath.SegmentDistance(mouse, h, t);
                if (d > PickDistance)
                {
                    continue;
                }
                float depth = Vector3.Distance(cam.transform.position, b.Head);
                if (best == null || d < bestDist - 1.5f || (Mathf.Abs(d - bestDist) <= 1.5f && depth < bestDepth))
                {
                    best = b;
                    bestDist = d;
                    bestDepth = depth;
                }
            }
            return best;
        }

        private void ApplyBox(UnityEngine.Camera cam, Box box)
        {
            var r = Rect.MinMaxRect(Mathf.Min(box.Start.x, box.End.x), Mathf.Min(box.Start.y, box.End.y), Mathf.Max(box.Start.x, box.End.x), Mathf.Max(box.Start.y, box.End.y));
            var inside = new List<SkelBone>();
            foreach (SkelBone b in Skel.Map)
            {
                if (!Visible(b))
                {
                    continue;
                }
                bool h = ScreenSpace.WorldToGui(cam, b.Head, out Vector2 hg) && r.Contains(hg);
                bool t = ScreenSpace.WorldToGui(cam, b.Tail, out Vector2 tg) && r.Contains(tg);
                if (h || t)
                {
                    inside.Add(b);
                }
            }
            List<SkelBone> sel = SelectedBones;
            switch (box.Mode)
            {
                case 1:
                    SetSelection(sel.Union(inside), Active);
                    break;
                case 2:
                    SetSelection(sel.Except(inside), null);
                    break;
                default:
                    SetSelection(inside, inside.Contains(Active) ? Active : inside.LastOrDefault());
                    break;
            }
        }

        private Vector3 PivotOf(List<SkelBone> bones)
        {
            if (bones.Count == 0)
            {
                return NavPivot();
            }
            Vector3 sum = Vector3.zero;
            foreach (SkelBone b in bones)
            {
                sum += b.Head;
            }
            return sum / bones.Count;
        }

        private Vector3[] GizmoAxes(SkelBone active)
        {
            if (_local && active != null)
            {
                return new[] { active.Axis(0), active.Axis(1), active.Axis(2) };
            }
            return new[] { Vector3.right, Vector3.up, Vector3.forward };
        }

        private static float WorldPerPixel(UnityEngine.Camera cam, Vector3 at)
        {
            float depth = Mathf.Max(0.05f, Vector3.Dot(at - cam.transform.position, cam.transform.forward));
            return 2f * depth * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);
        }

        private bool GizmoFrame(UnityEngine.Camera cam, out Vector3 center, out Vector2 centerGui, out Vector3[] axes, out float radius)
        {
            center = Vector3.zero;
            centerGui = Vector2.zero;
            axes = null;
            radius = 0f;
            if (!_gizmos || _tool == PoseTool.Tweak || !Edit.Enabled)
            {
                return false;
            }
            if (WeaponTarget(out Transform weapon))
            {
                center = weapon.position;
                if (!ScreenSpace.WorldToGui(cam, center, out centerGui))
                {
                    return false;
                }
                axes = WeaponAxes(weapon);
                radius = GizmoRadius * WorldPerPixel(cam, center);
                return true;
            }
            List<SkelBone> tops = Tops();
            if (tops.Count == 0 || (_tool == PoseTool.Move && !CanMove(tops)))
            {
                return false;
            }
            center = _tool == PoseTool.Move ? MovePivot(tops) : PivotOf(tops);
            if (!ScreenSpace.WorldToGui(cam, center, out centerGui))
            {
                return false;
            }
            axes = GizmoAxes(Active);
            radius = GizmoRadius * WorldPerPixel(cam, center);
            return true;
        }

        private Vector3 MovePivot(List<SkelBone> tops)
        {
            var points = new List<Vector3>();
            foreach (SkelBone b in tops)
            {
                if (b.LocationUnlocked)
                {
                    points.Add(b.Head);
                }
                else if (_autoIk && b.Part?.Limb != null && Rig.Chain(b.Part.Limb.Value) != null)
                {
                    PinState pin = Edit.Pin(b.Part.Limb.Value, false);
                    points.Add(pin != null && pin.Mode != PinMode.Off ? Rig.PinTarget(b.Part.Limb.Value, pin) : b.Head);
                }
            }
            if (points.Count == 0)
            {
                return PivotOf(tops);
            }
            Vector3 sum = Vector3.zero;
            foreach (Vector3 p in points)
            {
                sum += p;
            }
            return sum / points.Count;
        }

        private int GizmoHit(UnityEngine.Camera cam, Vector2 mouse, out ModalKind kind)
        {
            kind = ModalKind.Rotate;
            if (!GizmoFrame(cam, out Vector3 center, out Vector2 cg, out Vector3[] axes, out float radius))
            {
                return -2;
            }
            if (_tool == PoseTool.Rotate)
            {
                int best = -2;
                float bestDist = 7f;
                for (int i = 0; i < 3; i++)
                {
                    float d = RingDistance(cam, center, axes[i], radius, mouse);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = i;
                    }
                }
                if (best >= 0)
                {
                    return best;
                }
                float fromCenter = Vector2.Distance(mouse, cg);
                if (Mathf.Abs(fromCenter - ViewRing) < 6f)
                {
                    return -1;
                }
                if (fromCenter < GizmoRadius - 8f)
                {
                    kind = ModalKind.Trackball;
                    return -1;
                }
                return -2;
            }
            kind = ModalKind.Move;
            for (int i = 0; i < 3; i++)
            {
                if (ScreenSpace.WorldToGui(cam, center + axes[i] * radius, out Vector2 tip) && PoseMath.SegmentDistance(mouse, cg + (tip - cg) * 0.2f, tip) < 7f)
                {
                    return i;
                }
            }
            return Vector2.Distance(mouse, cg) < 9f ? -1 : -2;
        }

        private static float RingDistance(UnityEngine.Camera cam, Vector3 center, Vector3 axis, float radius, Vector2 mouse)
        {
            float best = float.MaxValue;
            ForRing(cam, center, axis, radius, (a, b, front) =>
            {
                if (front)
                {
                    best = Mathf.Min(best, PoseMath.SegmentDistance(mouse, a, b));
                }
            });
            return best;
        }

        private static void ForRing(UnityEngine.Camera cam, Vector3 center, Vector3 axis, float radius, Action<Vector2, Vector2, bool> segment)
        {
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 v = Vector3.Cross(axis, u).normalized;
            Vector3 toCam = (cam.transform.position - center).normalized;
            const int n = 48;
            Vector3 prev = center + u * radius;
            bool prevOk = ScreenSpace.WorldToGui(cam, prev, out Vector2 pg);
            for (int i = 1; i <= n; i++)
            {
                float t = i * Mathf.PI * 2f / n;
                Vector3 p = center + (u * Mathf.Cos(t) + v * Mathf.Sin(t)) * radius;
                bool ok = ScreenSpace.WorldToGui(cam, p, out Vector2 g);
                if (ok && prevOk)
                {
                    bool front = Vector3.Dot(((p + prev) * 0.5f - center).normalized, toCam) > -0.05f;
                    segment(pg, g, front);
                }
                prev = p;
                pg = g;
                prevOk = ok;
            }
        }

        private void StartModal(ModalKind kind, int axis, bool fromGizmo, Vector2 mouse)
        {
            UnityEngine.Camera cam = _c.Camera.Camera;
            if (cam == null || !Edit.Enabled)
            {
                Say("Pose is OFF: turn POSE ON to edit.");
                return;
            }
            if (WeaponTarget(out Transform held))
            {
                StartWeaponModal(cam, kind, axis, fromGizmo, mouse, held);
                return;
            }
            List<SkelBone> tops = Tops();
            if (tops.Count == 0)
            {
                Say("Select a bone first (click it, or A for all).");
                return;
            }
            var m = new Modal { Kind = kind, Axis = axis, Local = _local, FromGizmo = fromGizmo };
            if (kind == ModalKind.Move)
            {
                if (!CanMove(tops))
                {
                    Say("Location is locked on game bones; only the Hips can move. Turn on AUTO IK to move hands and feet.");
                    return;
                }
                m.Bones = tops.Where(b => b.LocationUnlocked).Select(b => NewModalBone(b)).ToList();
                if (_autoIk)
                {
                    foreach (SkelBone b in tops.Where(b => !b.LocationUnlocked && b.Part?.Limb != null))
                    {
                        PoseLimb limb = b.Part.Limb.Value;
                        if (Rig.Chain(limb) == null)
                        {
                            continue;
                        }
                        m.Pins.Add(new ModalPin { Limb = limb });
                    }
                }
                m.Pivot = MovePivot(tops);
            }
            else
            {
                m.Bones = tops.Select(b => NewModalBone(b)).ToList();
                m.Pivot = PivotOf(tops);
            }
            m.Snapshot = Edit.Snapshot();
            Edit.Checkpoint();
            foreach (ModalPin p in m.Pins)
            {
                PinState pin = Edit.Pin(p.Limb, true);
                if (pin.Mode == PinMode.Off)
                {
                    Rig.SetPinMode(p.Limb, pin, PinMode.World);
                }
                p.Start = Rig.PinTarget(p.Limb, pin);
            }
            ScreenSpace.WorldToGui(cam, m.Pivot, out m.PivotGui);
            m.StartMouse = mouse;
            m.Virtual = mouse;
            m.LastMouse = mouse;
            m.LastRaw = Mathf.Atan2(mouse.y - m.PivotGui.y, mouse.x - m.PivotGui.x) * Mathf.Rad2Deg;
            m.CamForward = cam.transform.forward;
            m.CamRight = cam.transform.right;
            m.CamUp = cam.transform.up;
            m.ActiveAxes = GizmoAxes(Active ?? tops[0]);
            if (axis >= 0 && !_local)
            {
                m.Local = false;
            }
            _modal = m;
            _menu = null;
            _pressing = false;
        }

        private bool _weaponMode;

        private bool WeaponTarget(out Transform weapon)
        {
            weapon = _weaponMode ? _actor?.HeldWeapon : null;
            return weapon != null && _actor.WeaponGizmoFrame(out _);
        }

        private Vector3[] WeaponAxes(Transform weapon)
        {
            return _local ? new[] { weapon.right, weapon.up, weapon.forward } : new[] { Vector3.right, Vector3.up, Vector3.forward };
        }

        private void StartWeaponModal(UnityEngine.Camera cam, ModalKind kind, int axis, bool fromGizmo, Vector2 mouse, Transform weapon)
        {
            PoseState s = Edit.State;
            var m = new Modal
            {
                Kind = kind,
                Axis = axis,
                Local = _local,
                FromGizmo = fromGizmo,
                Weapon = true,
                Pivot = weapon.position,
                WeaponPosStart = s.WeaponPos != null && s.WeaponPos.Length == 3 ? new Vector3(s.WeaponPos[0], s.WeaponPos[1], s.WeaponPos[2]) : Vector3.zero,
                WeaponRotStart = s.WeaponRot != null && s.WeaponRot.Length == 4 ? PoseMath.Normalized(new Quaternion(s.WeaponRot[0], s.WeaponRot[1], s.WeaponRot[2], s.WeaponRot[3])) : Quaternion.identity
            };
            m.Snapshot = Edit.Snapshot();
            Edit.Checkpoint();
            ScreenSpace.WorldToGui(cam, m.Pivot, out m.PivotGui);
            m.StartMouse = mouse;
            m.Virtual = mouse;
            m.LastMouse = mouse;
            m.LastRaw = Mathf.Atan2(mouse.y - m.PivotGui.y, mouse.x - m.PivotGui.x) * Mathf.Rad2Deg;
            m.CamForward = cam.transform.forward;
            m.CamRight = cam.transform.right;
            m.CamUp = cam.transform.up;
            m.ActiveAxes = new[] { weapon.right, weapon.up, weapon.forward };
            if (axis >= 0 && !_local)
            {
                m.Local = false;
            }
            _modal = m;
            _menu = null;
            _pressing = false;
        }

        private void SetWeaponRot(Modal m, Quaternion worldDelta)
        {
            if (_actor == null || !_actor.WeaponGizmoFrame(out Quaternion grip))
            {
                return;
            }
            Quaternion ur = PoseMath.Normalized(Quaternion.Inverse(grip) * worldDelta * grip * m.WeaponRotStart);
            Edit.State.WeaponRot = new[] { ur.x, ur.y, ur.z, ur.w };
        }

        private ModalBone NewModalBone(SkelBone b)
        {
            return new ModalBone
            {
                Bone = b,
                OffsetStart = Edit.Rot(b.Name),
                WorldStart = b.Bone.rotation,
                LocStart = Edit.Loc(b.Name),
                Axes = new[] { b.Axis(0), b.Axis(1), b.Axis(2) }
            };
        }

        private void CancelModal()
        {
            if (_modal == null)
            {
                return;
            }
            Edit?.Revert(_modal.Snapshot);
            _modal = null;
            Say("Cancelled.");
        }

        private void ConfirmModal()
        {
            if (_modal == null)
            {
                return;
            }
            string done = _modal.Readout;
            _modal = null;
            Edit.EndGesture();
            Say(done);
        }

        private void TickModal(UnityEngine.Camera cam, Vector2 mouse)
        {
            Modal m = _modal;
            Vector2 d = mouse - m.LastMouse;
            m.LastMouse = mouse;
            m.Virtual += d * (Chord.ShiftHeld ? 0.1f : 1f);

            for (int i = 0; i < 3; i++)
            {
                if (Input.GetKeyDown(KeyCode.X + i))
                {
                    bool plane = Chord.ShiftHeld;
                    if (m.Axis != i || m.Plane != plane)
                    {
                        m.Axis = i;
                        m.Plane = plane;
                        m.Local = _local;
                    }
                    else if (m.Local == _local)
                    {
                        m.Local = !_local;
                    }
                    else
                    {
                        m.Axis = -1;
                        m.Plane = false;
                    }
                }
            }
            if (Input.GetKeyDown(KeyCode.C))
            {
                m.Axis = -1;
                m.Plane = false;
            }
            if (Input.GetKeyDown(KeyCode.R) && !Chord.AltHeld && !Chord.CtrlHeld)
            {
                SwitchKind(m, m.Kind == ModalKind.Rotate ? ModalKind.Trackball : ModalKind.Rotate);
            }
            if (Input.GetKeyDown(KeyCode.G) && !Chord.AltHeld && !Chord.CtrlHeld && m.Kind != ModalKind.Move)
            {
                if (m.Weapon || CanMove(Tops()))
                {
                    SwitchKind(m, ModalKind.Move);
                }
                else
                {
                    Say("Location is locked on these bones.");
                }
            }
            foreach (char ch in Input.inputString)
            {
                if (char.IsDigit(ch) || ch == '.' || ch == ',')
                {
                    m.Typed += ch == ',' ? '.' : ch;
                }
                else if (ch == '-')
                {
                    m.Typed = m.Typed.StartsWith("-", StringComparison.Ordinal) ? m.Typed.Substring(1) : "-" + m.Typed;
                }
                else if (ch == '\b' && m.Typed.Length > 0)
                {
                    m.Typed = m.Typed.Substring(0, m.Typed.Length - 1);
                }
            }

            if (m.Kind == ModalKind.Move)
            {
                ApplyMove(cam, m);
            }
            else
            {
                ApplyRotate(m);
            }

            bool confirm = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)
                           || (m.FromGizmo ? Input.GetMouseButtonUp(0) : Input.GetMouseButtonDown(0));
            if (confirm)
            {
                ConfirmModal();
            }
        }

        private void SwitchKind(Modal m, ModalKind kind)
        {
            Edit.Revert(m.Snapshot);
            m.Snapshot = Edit.Snapshot();
            Edit.Checkpoint();
            List<SkelBone> tops = Tops();
            if (m.Weapon)
            {
            }
            else if (kind == ModalKind.Move)
            {
                m.Bones = tops.Where(b => b.LocationUnlocked).Select(b => NewModalBone(b)).ToList();
                m.Pins.Clear();
                if (_autoIk)
                {
                    foreach (SkelBone b in tops.Where(b => !b.LocationUnlocked && b.Part?.Limb != null && Rig.Chain(b.Part.Limb.Value) != null))
                    {
                        PoseLimb limb = b.Part.Limb.Value;
                        PinState pin = Edit.Pin(limb, true);
                        if (pin.Mode == PinMode.Off)
                        {
                            Rig.SetPinMode(limb, pin, PinMode.World);
                        }
                        m.Pins.Add(new ModalPin { Limb = limb, Start = Rig.PinTarget(limb, pin) });
                    }
                }
            }
            else if (m.Kind == ModalKind.Move)
            {
                m.Bones = tops.Select(b => NewModalBone(b)).ToList();
                m.Pins.Clear();
            }
            m.Kind = kind;
            m.StartMouse = m.Virtual;
            m.Angle = 0f;
            m.LastRaw = Mathf.Atan2(m.Virtual.y - m.PivotGui.y, m.Virtual.x - m.PivotGui.x) * Mathf.Rad2Deg;
            m.Typed = "";
        }

        private bool Typed(Modal m, out float value)
        {
            value = 0f;
            string t = m.Typed;
            if (string.IsNullOrEmpty(t) || t == "-" || t == ".")
            {
                return false;
            }
            return float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private Vector3 ModalAxis(Modal m, ModalBone b)
        {
            if (m.Axis < 0)
            {
                return m.CamForward;
            }
            return m.Local ? (b != null ? b.Axes[m.Axis] : m.ActiveAxes[m.Axis]) : WorldAxis(m.Axis);
        }

        private string AxisText(Modal m)
        {
            if (m.Axis < 0)
            {
                return m.Kind == ModalKind.Move ? "view plane" : "view axis";
            }
            return (m.Plane ? "plane without " : "along ") + (m.Local ? "local " : "global ") + AxisName(m.Axis);
        }

        private void ApplyRotate(Modal m)
        {
            if (m.Kind == ModalKind.Trackball)
            {
                Vector2 dv = m.Virtual - m.StartMouse;
                Quaternion tb = Quaternion.AngleAxis(-dv.x * 0.5f, m.CamUp) * Quaternion.AngleAxis(-dv.y * 0.5f, m.CamRight);
                if (m.Weapon)
                {
                    SetWeaponRot(m, tb);
                }
                foreach (ModalBone b in m.Bones)
                {
                    SetModalRot(b, tb);
                }
                m.Readout = string.Format(CultureInfo.InvariantCulture, "Trackball {0:0.0}°, {1:0.0}°", dv.x * 0.5f, dv.y * 0.5f);
                return;
            }
            float raw = Mathf.Atan2(m.Virtual.y - m.PivotGui.y, m.Virtual.x - m.PivotGui.x) * Mathf.Rad2Deg;
            if ((m.Virtual - m.PivotGui).sqrMagnitude > 16f)
            {
                m.Angle += Mathf.DeltaAngle(m.LastRaw, raw);
            }
            m.LastRaw = raw;
            bool typed = Typed(m, out float value);
            float angle = typed ? value : m.Angle;
            if (!typed && Chord.CtrlHeld)
            {
                angle = Mathf.Round(angle / 5f) * 5f;
            }
            foreach (ModalBone b in m.Bones)
            {
                Vector3 axis = ModalAxis(m, b);
                float sign = typed || m.Axis < 0 ? 1f : (Vector3.Dot(axis, m.CamForward) >= 0f ? 1f : -1f);
                SetModalRot(b, Quaternion.AngleAxis(angle * sign, axis));
            }
            if (m.Weapon)
            {
                Vector3 axis = ModalAxis(m, null);
                float sign = typed || m.Axis < 0 ? 1f : (Vector3.Dot(axis, m.CamForward) >= 0f ? 1f : -1f);
                SetWeaponRot(m, Quaternion.AngleAxis(angle * sign, axis));
            }
            m.Readout = string.Format(CultureInfo.InvariantCulture, "Rotation {0:0.##}° {1}{2}", angle, AxisText(m), typed ? "  [typed " + m.Typed + "]" : "");
        }

        private void SetModalRot(ModalBone b, Quaternion worldDelta)
        {
            Quaternion offset = b.OffsetStart * Quaternion.Inverse(b.WorldStart) * worldDelta * b.WorldStart;
            Edit.SetRot(b.Bone.Name, offset);
            MirrorEdit(b.Bone, PoseMath.Normalized(offset));
        }

        private void ApplyMove(UnityEngine.Camera cam, Modal m)
        {
            Ray now = ScreenSpace.ScreenRay(cam, GuiToScreen(m.Virtual));
            Ray start = ScreenSpace.ScreenRay(cam, GuiToScreen(m.StartMouse));
            Vector3 delta = Vector3.zero;
            Vector3 axis = ModalAxis(m, null);
            bool typed = Typed(m, out float value);
            if (typed)
            {
                delta = (m.Axis < 0 ? Vector3.right : axis) * value;
            }
            else if (m.Axis < 0 || m.Plane)
            {
                var plane = new Plane(m.Axis < 0 ? -m.CamForward : axis, m.Pivot);
                if (plane.Raycast(now, out float dn) && plane.Raycast(start, out float ds) && dn < 500f && ds < 500f)
                {
                    delta = now.GetPoint(dn) - start.GetPoint(ds);
                }
            }
            else if (PoseMath.ClosestOnLine(now, m.Pivot, axis, out Vector3 pn) && PoseMath.ClosestOnLine(start, m.Pivot, axis, out Vector3 ps))
            {
                delta = Vector3.Project(pn - ps, axis);
            }
            if (!typed && Chord.CtrlHeld)
            {
                delta = new Vector3(Mathf.Round(delta.x / 0.05f) * 0.05f, Mathf.Round(delta.y / 0.05f) * 0.05f, Mathf.Round(delta.z / 0.05f) * 0.05f);
            }
            delta = Vector3.ClampMagnitude(delta, 20f);
            if (m.Weapon && _actor != null && _actor.WeaponGizmoFrame(out Quaternion grip))
            {
                Vector3 up = m.WeaponPosStart + Quaternion.Inverse(grip) * delta;
                Edit.State.WeaponPos = new[] { up.x, up.y, up.z };
            }
            foreach (ModalBone b in m.Bones)
            {
                Transform parent = b.Bone.Bone.parent;
                Vector3 local = parent != null ? parent.InverseTransformVector(delta) : delta;
                Edit.SetLoc(b.Bone.Name, b.LocStart + local);
            }
            foreach (ModalPin p in m.Pins)
            {
                PinState pin = Edit.Pin(p.Limb, true);
                Rig.SetPinTarget(p.Limb, pin, p.Start + delta);
            }
            m.Readout = string.Format(CultureInfo.InvariantCulture, "Move {0:0.000} m {1}{2}", m.Axis < 0 || m.Plane ? delta.magnitude : Vector3.Dot(delta, axis), AxisText(m), typed ? "  [typed " + m.Typed + "]" : "");
        }

        private void ClearRotation()
        {
            if (WeaponTarget(out _))
            {
                Edit.Checkpoint();
                Edit.State.WeaponRot = null;
                Say("Weapon rotation cleared.");
                return;
            }
            List<SkelBone> sel = SelectedBones;
            if (sel.Count == 0)
            {
                Say("Select bones first.");
                return;
            }
            Edit.Checkpoint();
            foreach (SkelBone b in sel)
            {
                Edit.State.Rot.Remove(b.Name);
                if (_xMirror && b.Mirror != null)
                {
                    Edit.State.Rot.Remove(b.Mirror.Name);
                }
            }
            Say($"Rotation cleared on {sel.Count} bone(s).");
        }

        private void ClearLocation()
        {
            if (WeaponTarget(out _))
            {
                Edit.Checkpoint();
                Edit.State.WeaponPos = null;
                Say("Weapon location cleared.");
                return;
            }
            List<SkelBone> sel = SelectedBones.Where(b => b.LocationUnlocked).ToList();
            bool pins = false;
            Edit.Checkpoint();
            foreach (SkelBone b in sel)
            {
                Edit.State.Loc.Remove(b.Name);
            }
            foreach (SkelBone b in SelectedBones.Where(b => b.Part?.Limb != null))
            {
                PinState pin = Edit.Pin(b.Part.Limb.Value, false);
                if (pin != null && pin.Mode != PinMode.Off)
                {
                    pin.Offset = new[] { 0f, 0f, 0f };
                    pin.Swivel = 0f;
                    pin.Mode = PinMode.Off;
                    pin.Point = null;
                    pins = true;
                }
            }
            Say(sel.Count > 0 || pins ? "Location cleared." : "Nothing to clear: only the Hips (and pinned hands or feet) have a location.");
        }

        private void SelectParent(bool extend)
        {
            List<SkelBone> sel = SelectedBones;
            List<SkelBone> parents = sel.Select(b => b.Parent).Where(p => p != null).ToList();
            if (parents.Count == 0)
            {
                return;
            }
            SkelBone act = Active?.Parent ?? parents.Last();
            SetSelection(extend ? sel.Union(parents) : parents, act);
        }

        private void SelectChild(bool extend)
        {
            List<SkelBone> sel = SelectedBones;
            List<SkelBone> kids = sel.Select(b => b.TailBone ?? b.Children.FirstOrDefault()).Where(p => p != null).ToList();
            if (kids.Count == 0)
            {
                return;
            }
            SkelBone a = Active;
            SkelBone act = a != null ? a.TailBone ?? a.Children.FirstOrDefault() : kids.Last();
            SetSelection(extend ? sel.Union(kids) : kids, act);
        }

        private void SelectMirror()
        {
            List<SkelBone> sel = SelectedBones;
            SetSelection(sel.Select(b => b.Mirror ?? b), Active?.Mirror ?? Active);
        }

        private void HideSelected(bool unselected)
        {
            List<SkelBone> target = unselected ? Skel.Map.Where(b => Visible(b) && !IsSelected(b)).ToList() : SelectedBones;
            if (target.Count == 0)
            {
                return;
            }
            Hidden.UnionWith(target.Select(b => b.Name));
            if (!unselected)
            {
                SetSelection(Enumerable.Empty<SkelBone>(), null);
            }
            Say($"{target.Count} bone(s) hidden. {PoseKeymap.Of(PoseAction.Reveal)} reveals them.");
        }

        private void Reveal()
        {
            List<SkelBone> revealed = Skel.Map.Where(b => Hidden.Contains(b.Name)).ToList();
            Hidden.Clear();
            if (revealed.Count > 0)
            {
                SetSelection(revealed, null);
            }
            Say($"{revealed.Count} bone(s) revealed.");
        }

        private void CopyPose()
        {
            List<SkelBone> sel = SelectedBones;
            if (sel.Count == 0)
            {
                Say("Select the bones to copy first.");
                return;
            }
            _clipRot = sel.ToDictionary(b => b.Name, b => b.Bone.localRotation);
            _clipLoc = sel.Where(b => b.LocationUnlocked).ToDictionary(b => b.Name, b => Edit.Loc(b.Name));
            Say($"Copied the pose of {sel.Count} bone(s).");
        }

        private void PastePose(bool flipped)
        {
            if (_clipRot.Count == 0)
            {
                Say("Nothing copied yet (" + PoseKeymap.Of(PoseAction.CopyPose) + ").");
                return;
            }
            Edit.Checkpoint();
            int n = 0;
            foreach (KeyValuePair<string, Quaternion> kv in _clipRot)
            {
                SkelBone src = Skel.Find(kv.Key);
                if (src == null)
                {
                    continue;
                }
                SkelBone dst = flipped ? src.Mirror ?? src : src;
                Quaternion desired = flipped
                    ? dst.BindLocal * ActorRig.MirrorOffset(src, dst, Quaternion.Inverse(src.BindLocal) * kv.Value)
                    : kv.Value;
                Edit.SetRot(dst.Name, PoseMath.Normalized(Quaternion.Inverse(Rig.BaseUnder(dst, Edit.State)) * desired));
                n++;
            }
            foreach (KeyValuePair<string, Vector3> kv in _clipLoc)
            {
                SkelBone b = Skel.Find(kv.Key);
                if (b == null || b.Bone.parent == null)
                {
                    continue;
                }
                Vector3 loc = kv.Value;
                if (flipped)
                {
                    Vector3 rootVec = Quaternion.Inverse(Rig.Root.rotation) * b.Bone.parent.TransformVector(loc);
                    loc = b.Bone.parent.InverseTransformVector(Rig.Root.rotation * PoseMath.Mirror(rootVec));
                }
                Edit.SetLoc(b.Name, loc);
            }
            Say(n > 0 ? $"Pasted{(flipped ? " flipped" : "")} onto {n} bone(s)." : "None of the copied bones exist on this character.");
        }

        private void FrameAll()
        {
            Frame(Skel.Map.Where(Visible).ToList());
        }

        private void FrameSelected()
        {
            List<SkelBone> sel = SelectedBones;
            Frame(sel.Count > 0 ? sel : Skel.Map.Where(Visible).ToList());
        }

        private void Frame(List<SkelBone> bones)
        {
            if (bones.Count == 0)
            {
                return;
            }
            var bounds = new Bounds(bones[0].Head, Vector3.one * 0.05f);
            foreach (SkelBone b in bones)
            {
                bounds.Encapsulate(b.Head);
                bounds.Encapsulate(b.Tail);
            }
            if (!_c.Camera.CanNavigate)
            {
                Say("The camera is attached, spectating or driven by a path; detach it to frame.");
                return;
            }
            _c.Camera.FrameBounds(bounds);
        }

        private void OpenMenu(string title, Vector2 at, List<MenuItem> items)
        {
            _menuTitle = title;
            _menuPos = at;
            _menu = items;
        }

        private void OpenContextMenu(Vector2 at)
        {
            OpenMenu("POSE", at, new List<MenuItem>
            {
                new MenuItem { Label = "Clear Rotation", Hint = PoseKeymap.Of(PoseAction.ClearRotation), Run = ClearRotation },
                new MenuItem { Label = "Clear Location", Hint = PoseKeymap.Of(PoseAction.ClearLocation), Run = ClearLocation },
                new MenuItem { Label = "Copy Pose", Hint = PoseKeymap.Of(PoseAction.CopyPose), Run = CopyPose },
                new MenuItem { Label = "Paste Pose", Hint = PoseKeymap.Of(PoseAction.PastePose), Run = () => PastePose(false) },
                new MenuItem { Label = "Paste Pose Flipped", Hint = PoseKeymap.Of(PoseAction.PastePoseFlipped), Run = () => PastePose(true) },
                new MenuItem { Label = "Select Parent", Hint = PoseKeymap.Of(PoseAction.SelectParent), Run = () => SelectParent(false) },
                new MenuItem { Label = "Select Child", Hint = PoseKeymap.Of(PoseAction.SelectChild), Run = () => SelectChild(false) },
                new MenuItem { Label = "Select Mirror", Hint = PoseKeymap.Of(PoseAction.SelectMirror), Run = SelectMirror },
                new MenuItem { Label = "Hide Selected", Hint = PoseKeymap.Of(PoseAction.Hide), Run = () => HideSelected(false) },
                new MenuItem { Label = "Hide Unselected", Hint = PoseKeymap.Of(PoseAction.HideUnselected), Run = () => HideSelected(true) },
                new MenuItem { Label = "Reveal Hidden", Hint = PoseKeymap.Of(PoseAction.Reveal), Run = Reveal },
                new MenuItem { Label = "Frame Selected", Hint = PoseKeymap.Of(PoseAction.ViewSelected), Run = FrameSelected },
                new MenuItem { Label = "Frame All", Hint = PoseKeymap.Of(PoseAction.ViewAll), Run = FrameAll }
            });
        }

        private void OpenViewMenu(Vector2 at)
        {
            OpenMenu("VIEW", at, new List<MenuItem>
            {
                new MenuItem { Label = "Frame Selected", Hint = PoseKeymap.Of(PoseAction.ViewSelected), Run = FrameSelected },
                new MenuItem { Label = "Frame All", Hint = PoseKeymap.Of(PoseAction.ViewAll), Run = FrameAll },
                new MenuItem { Label = _gizmos ? "Hide Gizmos" : "Show Gizmos", Hint = PoseKeymap.Of(PoseAction.ToggleGizmo), Run = () => _gizmos = !_gizmos }
            });
        }

        private void OpenOrientationMenu(Vector2 at)
        {
            OpenMenu("ORIENTATION", at, new List<MenuItem>
            {
                new MenuItem { Label = (!_local ? "• " : "  ") + "Global", Hint = "", Run = () => _local = false },
                new MenuItem { Label = (_local ? "• " : "  ") + "Local", Hint = "", Run = () => _local = true }
            });
        }

        public void DrawGizmos()
        {
            UnityEngine.Camera cam = _c.Camera.Camera;
            if (!Open || !Usable || cam == null)
            {
                return;
            }
            HudTheme.Ensure();
            Event e = Event.current;
            if (e.type == EventType.Repaint)
            {
                if (Edit.Enabled)
                {
                    DrawBones(cam);
                    DrawGizmo(cam);
                    DrawModalGuides(cam);
                }
                if (_box != null)
                {
                    Vector2 a = _box.Start;
                    Vector2 b = _box.End;
                    var c = new Color(1f, 1f, 1f, 0.9f);
                    Line(new Vector2(a.x, a.y), new Vector2(b.x, a.y), c);
                    Line(new Vector2(b.x, a.y), new Vector2(b.x, b.y), c);
                    Line(new Vector2(b.x, b.y), new Vector2(a.x, b.y), c);
                    Line(new Vector2(a.x, b.y), new Vector2(a.x, a.y), c);
                }
                FlushLines();
                if (Edit.Enabled)
                {
                    DrawJointDots(cam);
                }
            }
            DrawStatus();
            if (_help)
            {
                DrawHelp();
            }
            DrawMenu();
        }

        private void DrawBones(UnityEngine.Camera cam)
        {
            SkelBone active = Active;
            foreach (SkelBone b in Skel.Map)
            {
                if (!Visible(b))
                {
                    continue;
                }
                Vector3 h = b.Head;
                Vector3 t = b.Tail;
                float len = (t - h).magnitude;
                if (len < 1e-4f)
                {
                    continue;
                }
                Vector3 dir = (t - h) / len;
                Vector3 u = Vector3.ProjectOnPlane(b.Axis(0), dir).normalized;
                Vector3 v = Vector3.Cross(dir, u).normalized;
                Vector3 mid = h + dir * len * 0.1f;
                float r = len * 0.1f;
                Vector3[] ring = { mid + u * r, mid + v * r, mid - u * r, mid - v * r };
                if (!ScreenSpace.WorldToGui(cam, h, out Vector2 hg) || !ScreenSpace.WorldToGui(cam, t, out Vector2 tg))
                {
                    continue;
                }
                var rg = new Vector2[4];
                bool ok = true;
                for (int i = 0; i < 4 && ok; i++)
                {
                    ok = ScreenSpace.WorldToGui(cam, ring[i], out rg[i]);
                }
                if (!ok)
                {
                    continue;
                }
                bool sel = IsSelected(b);
                Color c = b == active && sel ? BoneActive : sel ? BoneSelected : BoneIdle;
                if (b == _hover && _modal == null)
                {
                    c = sel ? Color.Lerp(c, BoneHover, 0.4f) : BoneHover;
                }
                for (int i = 0; i < 4; i++)
                {
                    Line(hg, rg[i], c);
                    Line(rg[i], rg[(i + 1) % 4], c);
                    Line(rg[i], tg, c);
                }
                if (sel)
                {
                    var o = new Vector2(1f, 0f);
                    Line(hg + o, tg + o, c);
                    for (int i = 0; i < 4; i++)
                    {
                        Line(rg[i] + o, rg[(i + 1) % 4] + o, c);
                    }
                }
            }
        }

        private void DrawJointDots(UnityEngine.Camera cam)
        {
            SkelBone active = Active;
            foreach (SkelBone b in Skel.Map)
            {
                if (!Visible(b) || !ScreenSpace.WorldToGui(cam, b.Head, out Vector2 g))
                {
                    continue;
                }
                bool sel = IsSelected(b);
                float s = b.Set == BoneSet.FingersL || b.Set == BoneSet.FingersR ? 2f : 3f;
                Color c = b == active && sel ? BoneActive : sel ? BoneSelected : new Color(1f, 1f, 1f, 0.85f);
                HudTheme.Fill(new Rect(g.x - s - 1f, g.y - s - 1f, s * 2f + 2f, s * 2f + 2f), new Color(0f, 0f, 0f, 0.7f));
                HudTheme.Fill(new Rect(g.x - s, g.y - s, s * 2f, s * 2f), c);
            }
        }

        private void DrawGizmo(UnityEngine.Camera cam)
        {
            if (_modal != null || !GizmoFrame(cam, out Vector3 center, out Vector2 cg, out Vector3[] axes, out float radius))
            {
                return;
            }
            if (_tool == PoseTool.Rotate)
            {
                for (int i = 0; i < 3; i++)
                {
                    Color c = AxisColor(i);
                    if (_hoverGizmo == i && _hoverGizmoKind == ModalKind.Rotate)
                    {
                        c = Color.Lerp(c, Color.white, 0.5f);
                    }
                    ForRing(cam, center, axes[i], radius, (a, b, front) =>
                    {
                        if (front)
                        {
                            Line(a, b, c);
                            Line(a + Vector2.right, b + Vector2.right, c);
                        }
                    });
                }
                Color view = _hoverGizmo == -1 && _hoverGizmoKind == ModalKind.Rotate ? Color.white : new Color(1f, 1f, 1f, 0.6f);
                ScreenCircle(cg, ViewRing, view);
                if (_hoverGizmo == -1 && _hoverGizmoKind == ModalKind.Trackball)
                {
                    ScreenCircle(cg, GizmoRadius - 8f, new Color(1f, 1f, 1f, 0.25f));
                }
                return;
            }
            for (int i = 0; i < 3; i++)
            {
                if (!ScreenSpace.WorldToGui(cam, center + axes[i] * radius, out Vector2 tip))
                {
                    continue;
                }
                Color c = _hoverGizmo == i ? Color.Lerp(AxisColor(i), Color.white, 0.5f) : AxisColor(i);
                Vector2 from = cg + (tip - cg) * 0.2f;
                Line(from, tip, c);
                Line(from + Vector2.right, tip + Vector2.right, c);
                ScreenSquare(tip, 4f, c);
            }
            ScreenSquare(cg, 6f, _hoverGizmo == -1 ? Color.white : new Color(1f, 1f, 1f, 0.7f));
        }

        private void DrawModalGuides(UnityEngine.Camera cam)
        {
            Modal m = _modal;
            if (m == null)
            {
                return;
            }
            ScreenSpace.WorldToGui(cam, m.Pivot, out Vector2 pg);
            if (m.Kind != ModalKind.Move)
            {
                Dashed(pg, GuiMouse(), new Color(1f, 1f, 1f, 0.7f));
            }
            if (m.Axis >= 0)
            {
                Vector3 axis = ModalAxis(m, m.Bones.Count > 0 ? m.Bones[0] : null);
                float span = 400f * WorldPerPixel(cam, m.Pivot);
                if (m.Plane)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        if (i == m.Axis)
                        {
                            continue;
                        }
                        Vector3 other = m.Local ? m.ActiveAxes[i] : WorldAxis(i);
                        AxisLine(cam, m.Pivot, other, span, AxisColor(i));
                    }
                }
                else
                {
                    AxisLine(cam, m.Pivot, axis, span, AxisColor(m.Axis));
                }
            }
        }

        private void AxisLine(UnityEngine.Camera cam, Vector3 pivot, Vector3 axis, float span, Color c)
        {
            Vector3 a = pivot - axis * span;
            Vector3 b = pivot + axis * span;
            for (int i = 0; i < 16; i++)
            {
                Vector3 p0 = Vector3.Lerp(a, b, i / 16f);
                Vector3 p1 = Vector3.Lerp(a, b, (i + 1) / 16f);
                if (ScreenSpace.WorldToGui(cam, p0, out Vector2 g0) && ScreenSpace.WorldToGui(cam, p1, out Vector2 g1))
                {
                    Line(g0, g1, c);
                }
            }
        }

        private void ScreenCircle(Vector2 c, float r, Color color)
        {
            const int n = 48;
            Vector2 prev = c + new Vector2(r, 0f);
            for (int i = 1; i <= n; i++)
            {
                float t = i * Mathf.PI * 2f / n;
                Vector2 p = c + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * r;
                Line(prev, p, color);
                prev = p;
            }
        }

        private void ScreenSquare(Vector2 c, float s, Color color)
        {
            Line(c + new Vector2(-s, -s), c + new Vector2(s, -s), color);
            Line(c + new Vector2(s, -s), c + new Vector2(s, s), color);
            Line(c + new Vector2(s, s), c + new Vector2(-s, s), color);
            Line(c + new Vector2(-s, s), c + new Vector2(-s, -s), color);
        }

        private void Dashed(Vector2 a, Vector2 b, Color c)
        {
            float len = Vector2.Distance(a, b);
            int n = Mathf.Clamp((int)(len / 8f), 1, 200);
            for (int i = 0; i < n; i += 2)
            {
                Line(Vector2.Lerp(a, b, i / (float)n), Vector2.Lerp(a, b, Mathf.Min(1f, (i + 1) / (float)n)), c);
            }
        }

        private void Line(Vector2 a, Vector2 b, Color c) => _lines.Add((a, b, c));

        private static bool EnsureMaterial()
        {
            if (_lineMaterial != null)
            {
                return true;
            }
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                return false;
            }
            _lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _lineMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _lineMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _lineMaterial.SetInt("_Cull", (int)CullMode.Off);
            _lineMaterial.SetInt("_ZWrite", 0);
            _lineMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
            return true;
        }

        private void FlushLines()
        {
            if (_lines.Count == 0)
            {
                return;
            }
            try
            {
                if (EnsureMaterial())
                {
                    _lineMaterial.SetPass(0);
                    GL.PushMatrix();
                    GL.Viewport(new Rect(0f, 0f, Screen.width, Screen.height));
                    GL.LoadPixelMatrix(0f, Screen.width, 0f, Screen.height);
                    GL.Begin(GL.LINES);
                    float h = Screen.height;
                    foreach ((Vector2 a, Vector2 b, Color c) in _lines)
                    {
                        GL.Color(c);
                        GL.Vertex3(a.x, h - a.y, 0f);
                        GL.Vertex3(b.x, h - b.y, 0f);
                    }
                    GL.End();
                    GL.PopMatrix();
                }
                else
                {
                    foreach ((Vector2 a, Vector2 b, Color c) in _lines)
                    {
                        RectLine(a, b, c);
                    }
                }
            }
            catch (Exception ex)
            {
                PhotoLog.WarnOnce("poselines", "Pose Mode line drawing failed: " + ex.Message);
            }
            finally
            {
                _lines.Clear();
            }
        }

        private static void RectLine(Vector2 a, Vector2 b, Color color)
        {
            Vector2 d = b - a;
            float length = d.magnitude;
            if (length < 0.5f)
            {
                return;
            }
            Matrix4x4 saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            HudTheme.Fill(new Rect(a.x, a.y - 0.75f, length, 1.5f), color);
            GUI.matrix = saved;
        }

        private void DrawStatus()
        {
            string text;
            if (_modal != null)
            {
                text = _modal.Readout + "      " + (_modal.Kind == ModalKind.Move ? "X Y Z axis · Shift+X/Y/Z plane · C free · R rotate" : "X Y Z axis (twice = local) · C free · R trackball · G move")
                       + " · type a value · Ctrl snap · Shift fine · " + (_modal.FromGizmo ? "release" : "LMB / Enter") + " confirm · RMB / Esc cancel";
            }
            else if (_boxArmed || _box != null)
            {
                text = "Box select: drag · Shift add · Ctrl remove · Esc cancel";
            }
            else
            {
                string hover = _hover != null ? _hover.Short + "  ·  " : "";
                text = hover + "LMB select · Shift+LMB extend · drag empty: box · G move · R rotate · Alt+R/G clear · A / Alt+A all/none · H hide · MMB orbit · Shift+MMB pan · wheel zoom · hold RMB fly · RMB/W menu · F1 keys";
            }
            GUIStyle style = SpawnerWindow.Left(HudTheme.HudSmall, wrap: false);
            float w = Mathf.Min(Screen.width - 40f, style.CalcSize(new GUIContent(text)).x + 24f);
            _statusRect = new Rect((Screen.width - w) * 0.5f, 10f, w, 26f);
            if (Event.current.type == EventType.Repaint)
            {
                HudTheme.Fill(_statusRect, new Color(0f, 0f, 0f, 0.75f));
                HudTheme.Border(_statusRect, _modal != null ? HudTheme.P.Highlight : HudTheme.P.LineBright);
            }
            GUI.Label(new Rect(_statusRect.x + 12f, _statusRect.y + 3f, _statusRect.width - 24f, 22f), text, style);
        }

        private static readonly string[] GettingStarted =
        {
            "1. Press F6 to freeze the raid.",
            "2. Press Ctrl+Tab (or Ctrl+P, or CHARACTERS > Pose Bone By Bone). With no character yet,",
            "    your PMC is added as a posable photo copy (CHARACTERS tab can add bot copies too).",
            "3. Click a bone (a line or white dot) to select it. Shift+click adds more bones.",
            "4. Press R and move the mouse to rotate it. Press X, Y or Z to lock an axis,",
            "    type a number for exact degrees (R X 45), then click or Enter to keep it.",
            "    Right-click or Esc cancels. Only the Hips can move (G); other bones rotate.",
            "5. Or pick ROTATE in the panel and drag the coloured rings around the bone.",
            "6. Middle mouse orbits around the selection, Shift+middle pans, the wheel zooms,",
            "    hold the right mouse button to fly with WASD.",
            "7. Alt+R resets the selected bones, Ctrl+Z undoes. Save poses under LIBRARY.",
            "8. F11 hides everything, then P takes the photo. Ctrl+Tab leaves Pose Mode."
        };

        private static bool _helpShownThisRun;

        private void DrawHelp()
        {
            var rows = new List<(string key, string what)>
            {
                ("LMB", "select bone (Shift/Ctrl: extend)"),
                ("LMB drag", "on a bone: rotate (Hips: move) · on empty: box select"),
                (PoseKeymap.Of(PoseAction.BoxSelect), "box select"),
                (PoseKeymap.Of(PoseAction.Move) + " / " + PoseKeymap.Of(PoseAction.Rotate), "move / rotate selected (R R trackball)"),
                ("X Y Z", "constrain axis, twice = local · Shift+X/Y/Z plane · C free"),
                ("0-9 . -", "type a value while transforming · Enter confirm"),
                ("Ctrl / Shift", "snap 5° or 5 cm / fine control while transforming"),
                (PoseKeymap.Of(PoseAction.ClearRotation) + " " + PoseKeymap.Of(PoseAction.ClearLocation), "clear rotation / location"),
                (PoseKeymap.Of(PoseAction.SelectAll) + " " + PoseKeymap.Of(PoseAction.DeselectAll) + " " + PoseKeymap.Of(PoseAction.InvertSelection), "select all / none / invert"),
                (PoseKeymap.Of(PoseAction.SelectParent) + " " + PoseKeymap.Of(PoseAction.SelectChild), "parent / child (Shift: extend)"),
                (PoseKeymap.Of(PoseAction.SelectMirror), "select mirror bones"),
                (PoseKeymap.Of(PoseAction.Hide) + " " + PoseKeymap.Of(PoseAction.HideUnselected) + " " + PoseKeymap.Of(PoseAction.Reveal), "hide / hide others / reveal"),
                (PoseKeymap.Of(PoseAction.CopyPose) + " " + PoseKeymap.Of(PoseAction.PastePose), "copy / paste pose"),
                (PoseKeymap.Of(PoseAction.PastePoseFlipped), "paste flipped"),
                (PoseKeymap.Of(PoseAction.Undo) + " / " + PoseKeymap.Of(PoseAction.Redo), "undo / redo (Ctrl+Y too)"),
                ("MMB / Shift+MMB / Ctrl+MMB", "orbit / pan / zoom around the selection"),
                ("Wheel", "zoom · hold RMB: fly with WASD (wheel = speed)"),
                ("RMB click / " + PoseKeymap.Of(PoseAction.ContextMenu), "pose menu"),
                (PoseKeymap.Of(PoseAction.ViewMenu) + " " + PoseKeymap.Of(PoseAction.ViewAll), "view menu / frame all"),
                (PoseKeymap.Of(PoseAction.OrientationMenu) + " " + PoseKeymap.Of(PoseAction.ToggleGizmo), "orientation menu / gizmos"),
                ("Esc", "cancel, close menu, then leave Pose Mode"),
                (DuckCamConfig.PoseModeKey.Value.ToString(), "enter / leave Pose Mode")
            };
            const float rowH = 20f;
            float w = 620f;
            float h = 32f + GettingStarted.Length * rowH + 40f + rows.Count * rowH + 34f;
            _helpRect = new Rect(Screen.width - w - 20f, Mathf.Max(46f, (Screen.height - h) * 0.5f), w, h);
            if (Event.current.type == EventType.Repaint)
            {
                HudTheme.Fill(_helpRect, new Color(0.02f, 0.02f, 0.025f, 0.95f));
                HudTheme.Border(_helpRect, HudTheme.P.LineBright);
            }
            GUIStyle key = SpawnerWindow.Left(HudTheme.ValueSelected, wrap: false);
            GUIStyle what = SpawnerWindow.Left(HudTheme.HudSmall, wrap: false);
            GUI.Label(new Rect(_helpRect.x + 12f, _helpRect.y + 6f, w - 24f, 22f), "POSE MODE: GETTING STARTED", SpawnerWindow.Left(HudTheme.Section, wrap: false));
            float y = _helpRect.y + 32f;
            foreach (string line in GettingStarted)
            {
                GUI.Label(new Rect(_helpRect.x + 12f, y, w - 24f, rowH), line, what);
                y += rowH;
            }
            y += 10f;
            GUI.Label(new Rect(_helpRect.x + 12f, y, w - 24f, 22f), "SHORTCUTS (Blender 5.1 defaults)", SpawnerWindow.Left(HudTheme.Section, wrap: false));
            y += 28f;
            foreach ((string k, string d) in rows)
            {
                GUI.Label(new Rect(_helpRect.x + 12f, y, 220f, rowH), k, key);
                GUI.Label(new Rect(_helpRect.x + 236f, y, w - 248f, rowH), d, what);
                y += rowH;
            }
            y += 6f;
            GUI.Label(new Rect(_helpRect.x + 12f, y, w - 24f, rowH), "F1 or the ? button shows / hides this panel. Esc closes it.", what);
        }

        private void DrawMenu()
        {
            if (_menu == null)
            {
                return;
            }
            const float rowH = 26f;
            const float w = 340f;
            const float hintW = 120f;
            float h = 30f + _menu.Count * rowH + 6f;
            float x = Mathf.Clamp(_menuPos.x, 4f, Screen.width - w - 4f);
            float y = Mathf.Clamp(_menuPos.y, 4f, Screen.height - h - 4f);
            _menuRect = new Rect(x, y, w, h);
            if (Event.current.type == EventType.Repaint)
            {
                HudTheme.Fill(_menuRect, new Color(0.03f, 0.03f, 0.035f, 0.97f));
                HudTheme.Border(_menuRect, HudTheme.P.LineBright);
            }
            GUI.Label(new Rect(x + 10f, y + 4f, w - 20f, 22f), _menuTitle, SpawnerWindow.Left(HudTheme.Section, wrap: false));
            float ry = y + 30f;
            foreach (MenuItem item in _menu.ToList())
            {
                bool hasHint = !string.IsNullOrEmpty(item.Hint);
                var r = new Rect(x + 4f, ry, w - 8f - (hasHint ? hintW + 4f : 0f), rowH - 3f);
                bool run = SpawnerWindow.Button(r, item.Label, false);
                if (hasHint && SpawnerWindow.Button(new Rect(r.xMax + 4f, ry, hintW, rowH - 3f), item.Hint, false))
                {
                    run = true;
                }
                if (run)
                {
                    _menu = null;
                    item.Run();
                    Event.current.Use();
                    return;
                }
                ry += rowH;
            }
        }
    }
}
