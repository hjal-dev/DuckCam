using System.Globalization;
using System.Linq;
using DuckCam.Camera;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed partial class DuckCamUI
    {
        private bool _pathHandles = true;

        private void DrawKeyCurve(CameraPath path, int i)
        {
            PathKeyframe k = path.Keys[i];
            bool manual = _kit.Toggle("  Manual Handles", k.ManualTangent);
            if (manual != k.ManualTangent)
            {
                if (manual)
                {
                    path.MakeManual(i);
                }
                else
                {
                    k.ManualTangent = false;
                    path.MarkDirty();
                }
            }
            if (!k.ManualTangent)
            {
                float tension = _kit.Slider("  Curve Smoothness", k.Tension, 0f, 3f, 0.05f, "0.00");
                if (!Mathf.Approximately(tension, k.Tension))
                {
                    k.Tension = tension;
                    path.MarkDirty();
                }
                return;
            }
            bool broken = _kit.Toggle("  Separate In/Out Handles", k.BrokenTangent);
            if (broken != k.BrokenTangent)
            {
                k.InTangent = path.InHandle(i);
                k.BrokenTangent = broken;
                path.MarkDirty();
            }
            float length = k.OutTangent.magnitude;
            float newLength = _kit.Slider("  Handle Length", length, 0f, 50f, 0.1f, "0.0", " m");
            if (Mathf.Abs(newLength - length) > 0.001f)
            {
                Vector3 dir = length > 1e-4f ? k.OutTangent / length : _c.Camera.Rotation * Vector3.forward;
                k.OutTangent = dir * newLength;
                path.MarkDirty();
            }
            if (_kit.Action("  Aim Handle With Camera", "Aim"))
            {
                k.OutTangent = (_c.Camera.Rotation * Vector3.forward) * Mathf.Max(length, 1f);
                path.MarkDirty();
            }
        }

        private void DrawKeyTiming(CameraPath path, int i)
        {
            if (!path.PerPointTiming)
            {
                return;
            }
            PathKeyframe k = path.Keys[i];
            bool last = i == path.Keys.Count - 1;
            if (!last)
            {
                k.Speed = _kit.LogSlider("  Speed From Here", k.Speed, 0.05f, 10f, "0.00", "x");
                k.Accel = _kit.Slider("  Speed Up Leaving", k.Accel, 0f, 10f, 0.1f, "0.0", " s");
            }
            if (i > 0)
            {
                k.Decel = _kit.Slider("  Slow Down Arriving", k.Decel, 0f, 10f, 0.1f, "0.0", " s");
            }
            k.Hold = _kit.Slider(i == 0 ? "  Start Delay" : (last ? "  End Delay" : "  Wait Here"), k.Hold, 0f, 60f, 0.1f, "0.0", " s");
        }

        private int _dragKey = -1;

        private int _dragPart;

        private Vector3 _dragStart;

        public void DrawPathEditor()
        {
            CameraPath path = _c.Path;
            UnityEngine.Camera cam = _c.Camera.Camera;
            if (!_pathHandles || cam == null || Spawner.Open || PoseEditor.Open || _c.Playback || _tab >= Tabs.Count || Tabs[_tab].Title != "PATHS" || path.Keys.Count == 0)
            {
                _dragKey = -1;
                return;
            }
            Event e = Event.current;
            HudTheme.Ensure();
            int selected = path.SelectedNode;
            Color pointColor = HudTheme.P.Highlight;
            Color handleColor = new Color(0.35f, 0.9f, 1f, 0.95f);

            if (e.type == EventType.Repaint)
            {
                for (int i = 0; i < path.Keys.Count; i++)
                {
                    if (!ToGui(cam, path.Keys[i].Position, out Vector2 p))
                    {
                        continue;
                    }
                    bool isSelected = i == selected;
                    float s = isSelected ? 8f : 6f;
                    var dot = new Rect(p.x - s, p.y - s, s * 2f, s * 2f);
                    HudTheme.Fill(dot, isSelected ? pointColor : new Color(pointColor.r, pointColor.g, pointColor.b, 0.85f));
                    HudTheme.Border(dot, Color.black);
                    string text = (i == 0 ? "START " : (i == path.Keys.Count - 1 ? "END " : "")) + "#" + i.ToString(CultureInfo.InvariantCulture) + (path.FollowKey == i ? " · FOLLOWS CAMERA" : "");
                    GUIStyle style = isSelected ? HudTheme.BoxTextHi : HudTheme.ValueSelected;
                    float w = style.CalcSize(new GUIContent(text)).x + 12f;
                    var tag = new Rect(p.x + 10f, p.y - 30f, w, 22f);
                    HudTheme.Fill(tag, isSelected ? pointColor : new Color(0f, 0f, 0f, 0.75f));
                    HudTheme.Border(tag, isSelected ? Color.white : pointColor);
                    GUI.Label(tag, text, style);
                }
                if (selected >= 0 && selected < path.Keys.Count)
                {
                    Vector3 at = path.Keys[selected].Position;
                    for (int part = 1; part <= 2; part++)
                    {
                        if (ToGui(cam, at, out Vector2 a) && ToGui(cam, HandlePoint(path, selected, part), out Vector2 h))
                        {
                            DrawLine(a, h, handleColor);
                            HudTheme.Fill(new Rect(h.x - 5f, h.y - 5f, 10f, 10f), handleColor);
                        }
                    }
                }
            }

            Vector2 mouse = e.mousePosition;
            if (e.type == EventType.MouseDown && e.button == 0 && !_uiScreenRect.Contains(mouse))
            {
                int hitKey = -1;
                int hitPart = 0;
                float best = 14f;
                if (selected >= 0 && selected < path.Keys.Count)
                {
                    for (int part = 1; part <= 2; part++)
                    {
                        if (ToGui(cam, HandlePoint(path, selected, part), out Vector2 h) && Vector2.Distance(h, mouse) < best)
                        {
                            best = Vector2.Distance(h, mouse);
                            hitKey = selected;
                            hitPart = part;
                        }
                    }
                }
                for (int i = 0; i < path.Keys.Count; i++)
                {
                    if (ToGui(cam, path.Keys[i].Position, out Vector2 p) && Vector2.Distance(p, mouse) < best)
                    {
                        best = Vector2.Distance(p, mouse);
                        hitKey = i;
                        hitPart = 0;
                    }
                }
                if (hitKey >= 0)
                {
                    path.FollowKey = -1;
                    _dragKey = hitKey;
                    _dragPart = hitPart;
                    _dragStart = hitPart == 0 ? path.Keys[hitKey].Position : HandlePoint(path, hitKey, hitPart);
                    path.SelectedNode = hitKey;
                    if (hitPart != 0)
                    {
                        path.MakeManual(hitKey);
                        if (e.alt && !path.Keys[hitKey].BrokenTangent)
                        {
                            path.Keys[hitKey].InTangent = path.InHandle(hitKey);
                            path.Keys[hitKey].BrokenTangent = true;
                        }
                    }
                    _kit.ClearBuffers($"k{hitKey}");
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseDrag && _dragKey >= 0 && _dragKey < path.Keys.Count)
            {
                if (DragTarget(cam, mouse, e.shift, out Vector3 target))
                {
                    PathKeyframe k = path.Keys[_dragKey];
                    if (_dragPart == 0)
                    {
                        k.Position = target;
                    }
                    else if (_dragPart == 1)
                    {
                        k.OutTangent = target - k.Position;
                    }
                    else if (k.BrokenTangent)
                    {
                        k.InTangent = target - k.Position;
                    }
                    else
                    {
                        k.OutTangent = k.Position - target;
                    }
                    path.MarkDirty();
                    _kit.ClearBuffers($"k{_dragKey}");
                }
                e.Use();
            }
            else if (e.rawType == EventType.MouseUp && e.button == 0 && _dragKey >= 0)
            {
                _dragKey = -1;
                e.Use();
            }
        }

        private static Vector3 HandlePoint(CameraPath path, int key, int part)
        {
            return path.Keys[key].Position + (part == 1 ? path.OutHandle(key) : path.InHandle(key));
        }

        private bool DragTarget(UnityEngine.Camera cam, Vector2 mouse, bool vertical, out Vector3 target)
        {
            target = _dragStart;
            Ray ray = ScreenSpace.ScreenRay(cam, new Vector2(mouse.x, Screen.height - mouse.y));
            Vector3 normal = vertical ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up) : Vector3.up;
            if (normal.sqrMagnitude < 1e-4f)
            {
                normal = Vector3.forward;
            }
            var plane = new Plane(normal.normalized, _dragStart);
            if (!plane.Raycast(ray, out float distance) || distance > 2000f)
            {
                return false;
            }
            Vector3 hit = ray.GetPoint(distance);
            target = vertical ? new Vector3(_dragStart.x, hit.y, _dragStart.z) : new Vector3(hit.x, _dragStart.y, hit.z);
            return true;
        }

        private static bool ToGui(UnityEngine.Camera cam, Vector3 world, out Vector2 gui)
        {
            return ScreenSpace.WorldToGui(cam, world, out gui);
        }

        private static void DrawLine(Vector2 a, Vector2 b, Color color)
        {
            Vector2 d = b - a;
            float length = d.magnitude;
            if (length < 1f)
            {
                return;
            }
            Matrix4x4 saved = GUI.matrix;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(angle, a);
            HudTheme.Fill(new Rect(a.x, a.y - 1f, length, 2f), color);
            GUI.matrix = saved;
        }

        private void DrawPaths()
        {
            PathSet set = _c.Paths;
            if (_kit.Action("Animation Timeline", Timeline.Open ? "Close" : "Open"))
            {
                Timeline.Toggle();
            }
            _kit.Section("A · Keyframe path");
            string[] names = set.Paths.Select(p => $"{p.Name} ({p.Keys.Count})").ToArray();
            int pick = _kit.Selector("Active Path", names, set.ActiveIndex);
            if (pick != set.ActiveIndex)
            {
                set.Select(pick);
                _kit.ClearBuffers();
                _editKey = -1;
            }
            int ps = _kit.Buttons("New Path", "Duplicate", set.Paths.Count > 1 ? "Delete Path" : "Clear Path");
            if (ps == 0)
            {
                set.Add();
            }
            else if (ps == 1)
            {
                set.Duplicate();
            }
            else if (ps == 2)
            {
                set.RemoveActive();
            }
            if (ps >= 0)
            {
                _kit.ClearBuffers();
                _editKey = -1;
            }

            CameraPath path = _c.Path;
            int b = _kit.Buttons("Add Keyframe", path.Playing && !path.Paused ? "Pause" : "Play", "Stop");
            if (b == 0)
            {
                _c.AddKeyframe();
            }
            else if (b == 1)
            {
                if (path.Playing)
                {
                    path.Paused = !path.Paused;
                }
                else
                {
                    _c.PlayPath();
                }
            }
            else if (b == 2)
            {
                path.Stop();
            }

            int timing = _kit.Selector("Timing", new[] { "Whole Path Duration", "Per Point Speed" }, path.PerPointTiming ? 1 : 0);
            path.PerPointTiming = timing == 1;
            if (path.PerPointTiming)
            {
                path.BaseSpeed = _kit.LogSlider("Base Speed", path.BaseSpeed, 0.05f, 50f, "0.00", " m/s");
                _kit.Value("Path Duration", $"{path.Duration:0.0} s (from the points)");
            }
            else
            {
                path.Duration = _kit.LogSlider("Path Duration", path.Duration, CameraPath.MinDuration, CameraPath.MaxDuration, "0.0", " s");
            }
            path.Loop = _kit.Toggle("Loop", path.Loop);
            if (!path.PerPointTiming)
            {
                path.ConstantSpeed = _kit.Toggle("Constant Speed", path.ConstantSpeed);
                path.EaseIn = _kit.Toggle("Ease In", path.EaseIn);
                path.EaseOut = _kit.Toggle("Ease Out", path.EaseOut);
            }
            _pathHandles = _kit.Toggle("Edit Points On Screen", _pathHandles);
            path.UseCountdown = _kit.Toggle("3 s Countdown", path.UseCountdown);
            _c.PathVisuals.Enabled = _kit.Toggle("Show Path In World", _c.PathVisuals.Enabled);
            string playback = path.Countdown > 0f ? $"   starts in {path.Countdown:0.0} s" : (path.Playing ? $"   {path.Time:0.0}/{path.Duration:0.0} s" : "");
            _kit.Value("Keyframes / Length", $"{path.Keys.Count} / {path.Length:0.0} m" + playback);

            _kit.Section("Keyframes · Go = jump there, Edit = change it");
            bool hasSelection = path.SelectedNode >= 0 && path.SelectedNode < path.Keys.Count;
            _kit.Value("Selected", hasSelection ? $"Key #{path.SelectedNode}" : "None (new keys go to the end)");
            int nav = _kit.Buttons("First", "Previous", "Next", "Last");
            if (nav == 0)
            {
                _c.SelectKeyframe(0);
            }
            else if (nav == 1)
            {
                _c.SelectKeyframe(path.SelectedNode - 1);
            }
            else if (nav == 2)
            {
                _c.SelectKeyframe(path.SelectedNode + 1);
            }
            else if (nav == 3)
            {
                _c.SelectKeyframe(path.Keys.Count - 1);
            }
            int edit = _kit.Buttons("Insert Before", "Insert After", "Replace");
            if (edit == 0 || edit == 1)
            {
                _c.InsertKeyframe(before: edit == 0);
                _kit.ClearBuffers();
                _editKey = -1;
            }
            else if (edit == 2)
            {
                _c.ReplaceKeyframe();
                _kit.ClearBuffers();
            }
            _kit.Note($"Keys: {KeyName(DuckCamConfig.CreateKeyframe)} add, {KeyName(DuckCamConfig.PathDeleteNode)} delete selected, {KeyName(DuckCamConfig.PathPrevNode)} / {KeyName(DuckCamConfig.PathNextNode)} previous / next, {KeyName(DuckCamConfig.PathFirstNode)} / {KeyName(DuckCamConfig.PathLastNode)} first / last, {KeyName(DuckCamConfig.PathInsertBefore)} / {KeyName(DuckCamConfig.PathInsertAfter)} insert before / after, {KeyName(DuckCamConfig.PathReplaceNode)} replace."
                      + (set.Paths.Count > 1 && DuckCamConfig.PathCutKeys.Value ? " While a path plays, Alt+1-9 (number row) cut to that path at the same progress." : ""));

            for (int i = 0; i < path.Keys.Count; i++)
            {
                PathKeyframe k = path.Keys[i];
                int action = _kit.ListRow(i == path.SelectedNode ? $"» Key #{i}" : $"Key #{i}",$"{k.Position.x:0.0} {k.Position.y:0.0} {k.Position.z:0.0}  {k.Fov:0}°", "Go", _editKey == i ? "Close" : "Edit", "Del");
                if (action == 0)
                {
                    _c.SelectKeyframe(i);
                }
                else if (action == 1)
                {
                    _editKey = _editKey == i ? -1 : i;
                }
                else if (action == 2)
                {
                    path.RemoveAt(i);
                    _kit.ClearBuffers();
                    _editKey = -1;
                    break;
                }
                if (_editKey == i)
                {
                    bool following = path.FollowKey == i;
                    if (_kit.Action("  Follow Camera", following ? "Stop" : "Follow"))
                    {
                        path.FollowKey = following ? -1 : i;
                        if (!following)
                        {
                            _c.SelectKeyframe(i);
                            path.FollowKey = i;
                            _c.LastMessage = $"Key #{i} follows the camera: fly it into place (Tab hides the menu), then press Stop.";
                        }
                        _kit.ClearBuffers($"k{i}");
                    }
                    if (path.FollowKey == i)
                    {
                        _kit.Note("This key moves with the camera until you press Stop.");
                    }
                    else
                    {
                        float[] pos = { k.Position.x, k.Position.y, k.Position.z };
                        _kit.Floats("  Position X Y Z", $"k{i}p", pos);
                        var newPos = new Vector3(pos[0], pos[1], pos[2]);
                        if (newPos != k.Position)
                        {
                            k.Position = newPos;
                            path.MarkDirty();
                        }
                        Vector3 e = k.Rotation.eulerAngles;
                        float yaw = Mathf.DeltaAngle(0f, e.y);
                        float pitch = PhotoCameraController.NormalizePitch(e.x);
                        float roll = PhotoCameraController.NormalizePitch(e.z);
                        float nYaw = _kit.Slider("  Yaw (turn)", yaw, -180f, 180f, 1f, "0", "°");
                        float nPitch = _kit.Slider("  Pitch (up/down)", pitch, -89f, 89f, 1f, "0", "°");
                        float nRoll = _kit.Slider("  Roll (tilt)", roll, -180f, 180f, 1f, "0", "°");
                        if (Mathf.Abs(nYaw - yaw) > 0.01f || Mathf.Abs(nPitch - pitch) > 0.01f || Mathf.Abs(nRoll - roll) > 0.01f)
                        {
                            k.Rotation = Quaternion.Euler(nPitch, nYaw, nRoll);
                            path.MarkDirty();
                        }
                        float fov = _kit.Slider("  Field Of View", k.Fov, PhotoCameraController.MinFov, PhotoCameraController.MaxFov, 1f, "0", "°");
                        if (!Mathf.Approximately(fov, k.Fov))
                        {
                            k.Fov = fov;
                            path.MarkDirty();
                        }
                        int keyAction = _kit.Buttons("Set From Camera", "View From Key");
                        if (keyAction == 0)
                        {
                            k.Position = _c.Camera.Position;
                            k.Rotation = _c.Camera.Rotation;
                            k.Fov = _c.Camera.Fov;
                            _kit.ClearBuffers($"k{i}");
                            path.MarkDirty();
                        }
                        else if (keyAction == 1)
                        {
                            _c.SelectKeyframe(i);
                        }
                    }
                    DrawKeyCurve(path, i);
                    DrawKeyTiming(path, i);
                }
            }
            if (_pathHandles && path.Keys.Count > 0)
            {
                _kit.Note("On screen: drag a point (square) to move it, drag a handle (dot) to bend the curve; Shift+drag moves up/down. Dragging a handle switches that point to manual handles. Right mouse still looks around.");
            }
            if (path.Keys.Count > 0 && _kit.Action("Clear Path", "Clear"))
            {
                path.Clear();
                _editKey = -1;
            }

            _kit.Section("Handheld shake · while a path or recording plays");
            DrawShake(_c.Camera.Shake.Path);

            _kit.Section("B · Camera recording · fly by hand, play it back");
            CameraRecording rec = _c.Recording;
            _kit.Value("Take", $"{rec.Duration:0.0} s · {rec.Samples.Count} samples" + (rec.Recording ? " · REC" : "") + (rec.Playing ? $" · {rec.PlayTime:0.0} s" : ""));
            int r = _kit.Buttons(rec.Recording ? "Recording" : "Record", "Continue", "End");
            if (r == 0)
            {
                _c.BeginRecording(false);
            }
            else if (r == 1)
            {
                _c.BeginRecording(true);
            }
            else if (r == 2)
            {
                _c.EndRecording();
            }
            int pl = _kit.Buttons(rec.Playing && !rec.Paused ? "Pause" : "Play", "Stop");
            if (pl == 0)
            {
                if (rec.Playing)
                {
                    rec.Paused = !rec.Paused;
                }
                else
                {
                    _c.PlayRecording();
                }
            }
            else if (pl == 1)
            {
                rec.StopPlayback();
            }
            rec.Loop = _kit.Toggle("Loop Recording", rec.Loop);
            _kit.Note($"Keys: {KeyName(DuckCamConfig.BeginRecording)} record / stop, {KeyName(DuckCamConfig.ContinueRecording)} continue, {KeyName(DuckCamConfig.PlayRecording)} play / stop.");
            DuckCamConfig.PathUsesRealTime.Value = _kit.Toggle("Play In Real Time", DuckCamConfig.PathUsesRealTime.Value);

            _kit.Section("Load / save");
            _fileName = _kit.Text("File Name", _fileName);
            int s = _kit.Buttons("Save Path", "Save Recording");
            if (s == 0)
            {
                _fileMessage = "Saved " + System.IO.Path.GetFileName(PathStorage.SavePath(path, _fileName));
            }
            else if (s == 1)
            {
                _fileMessage = "Saved " + System.IO.Path.GetFileName(PathStorage.SaveRecording(rec, _fileName));
            }
            foreach (string file in PathStorage.List())
            {
                if (_kit.ListRow(file, "", "Load") == 0)
                {
                    string error = PathStorage.Load(file, path, rec);
                    _kit.ClearBuffers();
                    _editKey = -1;
                    _fileMessage = error == null ? "Loaded " + file : file + ": " + error;
                }
            }
            if (_fileMessage != null)
            {
                _kit.Note(_fileMessage);
            }
        }
    }
}
