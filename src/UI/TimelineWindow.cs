using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DuckCam.Actors;
using DuckCam.Animation;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed class TimelineWindow
    {
        private const float H = 196f;
        private const float RowH = 24f;
        private const float NameW = 150f;

        private static readonly Color KeyColor = new Color(1f, 0.82f, 0.25f, 1f);
        private static readonly Color CamKeyColor = new Color(0.4f, 0.85f, 1f, 1f);
        private static readonly string[] EaseNames = { "SMOOTH", "LINEAR", "EASE IN", "EASE OUT", "STEP" };

        private readonly DuckCamController _c;
        private Rect _screenRect;
        private int _lastDrawFrame;
        private bool _scrubbing;
        private TimelineKey _selected;
        private TimelineKey _dragKey;
        private float _dragOffset;
        private int _rowScroll;
        private string _sceneName = "scene1";
        private string _message = "";
        private float _messageUntil;
        private bool _wasPlaying;
        private float _settleAt = -1f;
        private readonly Dictionary<PhotoActor, AnimatedPose> _poses = new Dictionary<PhotoActor, AnimatedPose>();

        public bool Open { get; private set; }

        public SceneTimeline Timeline { get; } = new SceneTimeline();

        public TimelineWindow(DuckCamController controller)
        {
            _c = controller;
        }

        public void Toggle()
        {
            Open = !Open;
            if (!Open)
            {
                Stop(settle: true);
            }
        }

        public void Close()
        {
            Stop(settle: false);
            Open = false;
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
                return _screenRect.Contains(new Vector2(m.x, Screen.height - m.y));
            }
        }

        public void Scroll(float notches)
        {
            _rowScroll = Mathf.Max(0, _rowScroll - Mathf.RoundToInt(Mathf.Sign(notches)));
        }

        private void Say(string text)
        {
            _message = text;
            _messageUntil = Time.unscaledTime + 4f;
        }

        public bool Tick(out Vector3 camPos, out Quaternion camRot, out float camFov)
        {
            camPos = Vector3.zero;
            camRot = Quaternion.identity;
            camFov = 60f;
            SceneTimeline t = Timeline;
            t.Relink(_c.Actors.Actors);
            t.Advance(PhotoTime.Unscaled);
            bool live = Open && (t.Playing || _scrubbing);
            if (_wasPlaying && !t.Playing && !_scrubbing)
            {
                Settle();
            }
            _wasPlaying = t.Playing || _scrubbing;
            if (_settleAt > 0f && Time.unscaledTime >= _settleAt && !live)
            {
                _settleAt = -1f;
                Settle();
            }
            if (!live)
            {
                ReleaseOverrides();
                return false;
            }
            foreach (ActorTrack track in t.Tracks)
            {
                if (track.Actor?.Rig == null)
                {
                    continue;
                }
                if (!_poses.TryGetValue(track.Actor, out AnimatedPose pose))
                {
                    _poses[track.Actor] = pose = new AnimatedPose();
                }
                if (t.EvaluateActor(track, t.Time, pose, out Vector4 place))
                {
                    track.Actor.Rig.Override = pose;
                    Place(track.Actor, place);
                }
            }
            return t.EvaluateCamera(t.Time, out camPos, out camRot, out camFov);
        }

        private static void Place(PhotoActor actor, Vector4 place)
        {
            actor.OffsetRight = place.x;
            actor.OffsetForward = place.y;
            actor.OffsetUp = place.z;
            actor.Yaw = place.w;
            actor.ApplyTransform();
        }

        private void ReleaseOverrides()
        {
            foreach (PhotoActor a in _c.Actors.Actors)
            {
                if (a.Rig != null && a.Rig.Override != null)
                {
                    a.Rig.Override = null;
                }
            }
        }

        private void Settle()
        {
            SceneTimeline t = Timeline;
            foreach (ActorTrack track in t.Tracks)
            {
                PhotoActor actor = track.Actor;
                if (actor?.Rig == null || track.Keys.Count == 0)
                {
                    continue;
                }
                var pose = new AnimatedPose();
                if (!t.EvaluateActor(track, t.Time, pose, out Vector4 place))
                {
                    continue;
                }
                PoseEdit edit = actor.PoseEditor;
                edit.Checkpoint();
                var state = new PoseState
                {
                    FullPose = pose.Bones.ToDictionary(kv => kv.Key, kv => SceneTimeline.A(kv.Value)),
                    FullHips = pose.Hips.HasValue ? SceneTimeline.A(pose.Hips.Value) : null
                };
                edit.Restore(state);
                Place(actor, place);
            }
            ReleaseOverrides();
            if (t.CameraKeys.Count > 0 && t.EvaluateCamera(t.Time, out Vector3 p, out Quaternion r, out float f))
            {
                _c.Camera.Mode = DuckCam.Camera.CameraMode.Free;
                Vector3 e = r.eulerAngles;
                _c.Camera.SetPose(new DuckCam.Camera.CameraPose { Position = p, Yaw = e.y, Pitch = DuckCam.Camera.PhotoCameraController.NormalizePitch(e.x), Roll = DuckCam.Camera.PhotoCameraController.NormalizePitch(e.z), Fov = f });
            }
        }

        public void PlayPause()
        {
            SceneTimeline t = Timeline;
            if (!t.HasKeys)
            {
                Say("Add keys first: pose the scene, press KEY ALL, move the playhead, change the pose, KEY ALL again.");
                return;
            }
            if (t.Playing)
            {
                t.Playing = false;
                return;
            }
            if (t.Time >= t.Duration - 0.001f)
            {
                t.Time = 0f;
            }
            t.Playing = true;
        }

        private void Stop(bool settle)
        {
            Timeline.Playing = false;
            _scrubbing = false;
            if (settle)
            {
                Settle();
            }
            ReleaseOverrides();
        }

        private void Seek(float time, bool settleSoon)
        {
            Timeline.Time = Mathf.Clamp(time, 0f, Timeline.Duration);
            if (settleSoon)
            {
                _settleAt = Time.unscaledTime;
            }
        }

        private void KeyAll()
        {
            SceneTimeline t = Timeline;
            int n = 0;
            if (t.CameraEnabled)
            {
                _selected = t.KeyCamera(_c.Camera.Position, _c.Camera.Rotation, _c.Camera.Fov);
                n++;
            }
            foreach (PhotoActor a in _c.Actors.Actors.Where(a => a.Status == ActorStatus.Ready && a.Rig != null))
            {
                _selected = t.KeyActor(a) ?? _selected;
                n++;
            }
            Say(n > 0 ? $"Keyed {n} track(s) at {t.Time:0.00} s." : "Nothing to key: add a character (CHARACTERS) or turn the camera track on.");
        }

        public void Draw(float vw, float vh, float left, float right)
        {
            if (!Open)
            {
                return;
            }
            _lastDrawFrame = Time.frameCount;
            SceneTimeline t = Timeline;
            var win = new Rect(left, vh - H - 64f, Mathf.Max(600f, right - left), H);
            Vector2 tl = GUIUtility.GUIToScreenPoint(new Vector2(win.x, win.y));
            Vector2 br = GUIUtility.GUIToScreenPoint(new Vector2(win.xMax, win.yMax));
            _screenRect = new Rect(tl, br - tl);
            HudTheme.Fill(win, new Color(0.03f, 0.03f, 0.035f, 0.93f));
            HudTheme.Border(win, HudTheme.P.LineBright);

            float x = win.x + 8f;
            float y = win.y + 6f;
            if (Btn(ref x, y, 34f, "|<", false)) { Stop(false); Seek(0f, true); }
            if (Btn(ref x, y, 34f, "<K", false)) JumpKey(-1);
            if (Btn(ref x, y, 70f, t.Playing ? "PAUSE" : "PLAY", t.Playing)) PlayPause();
            if (Btn(ref x, y, 34f, "K>", false)) JumpKey(1);
            if (Btn(ref x, y, 34f, ">|", false)) { Stop(false); Seek(t.Duration, true); }
            if (Btn(ref x, y, 60f, "LOOP", t.Loop)) t.Loop = !t.Loop;
            GUI.Label(new Rect(x + 4f, y, 150f, 26f), $"{t.Time:0.00} / {t.Duration:0.0} s", SpawnerWindow.Left(HudTheme.Value, wrap: false));
            x += 156f;
            if (Btn(ref x, y, 26f, "-", false)) t.Duration = Mathf.Max(1f, t.Duration - 1f);
            if (Btn(ref x, y, 26f, "+", false)) t.Duration = Mathf.Min(600f, t.Duration + 1f);
            x += 10f;
            if (Btn(ref x, y, 80f, "KEY ALL", false)) KeyAll();
            if (Btn(ref x, y, 90f, "KEY CAMERA", false))
            {
                _selected = t.KeyCamera(_c.Camera.Position, _c.Camera.Rotation, _c.Camera.Fov);
                Say($"Camera keyed at {t.Time:0.00} s.");
            }
            PhotoActor sel = _c.Actors.SelectedActor;
            if (Btn(ref x, y, 120f, "KEY CHARACTER", false))
            {
                if (sel?.Rig != null)
                {
                    _selected = t.KeyActor(sel);
                    Say($"{sel.Name} keyed at {t.Time:0.00} s.");
                }
                else
                {
                    Say("Select a character in CHARACTERS (or the pose editor) first.");
                }
            }
            if (_selected != null)
            {
                if (Btn(ref x, y, 96f, EaseNames[(int)_selected.Ease], false))
                {
                    _selected.Ease = (KeyEase)(((int)_selected.Ease + 1) % EaseNames.Length);
                }
                if (Btn(ref x, y, 70f, "DELETE", false))
                {
                    DeleteSelected();
                }
            }
            if (Btn(ref x, y, 100f, t.CameraEnabled ? "CAMERA ON" : "CAMERA OFF", t.CameraEnabled)) t.CameraEnabled = !t.CameraEnabled;
            if (SpawnerWindow.Button(new Rect(win.xMax - 34f, y, 26f, 26f), "X", false))
            {
                Toggle();
                return;
            }

            y += 30f;
            x = win.x + 8f;
            GUI.SetNextControlName(HudKit.FieldPrefix + "SceneName");
            _sceneName = GUI.TextField(new Rect(x, y + 2f, 150f, 22f), _sceneName ?? "", 40, HudTheme.Field);
            x += 156f;
            if (Btn(ref x, y, 60f, "SAVE", false))
            {
                try
                {
                    Say($"Saved scene '{t.Save(_sceneName)}'.");
                }
                catch (Exception ex)
                {
                    Say("Save failed: " + ex.Message);
                }
            }
            if (Btn(ref x, y, 60f, "LOAD", false))
            {
                string error = t.Load(_sceneName, _c.Actors.Actors);
                _selected = null;
                Say(error == null ? $"Loaded scene '{_sceneName}'." : error);
                Seek(0f, true);
            }
            if (Btn(ref x, y, 60f, "NEW", false))
            {
                Stop(false);
                t.Clear();
                _selected = null;
                Say("Empty timeline.");
            }
            string status = Time.unscaledTime < _messageUntil ? _message
                : t.HasKeys ? $"{t.CameraKeys.Count} camera key(s), {t.Tracks.Sum(k => k.Keys.Count)} pose key(s). Drag keys to retime; click the ruler to scrub."
                : "Pose the scene, KEY ALL, move the playhead, change the pose, KEY ALL again, then PLAY. Record it with " + DuckCamConfig.VideoRecordKey.Value + ".";
            GUI.Label(new Rect(x + 8f, y, win.xMax - x - 16f, 26f), status, SpawnerWindow.Left(HudTheme.Note, wrap: false));

            y += 32f;
            var lane = new Rect(win.x + 8f + NameW, y, win.width - 16f - NameW, win.yMax - y - 6f);
            DrawRuler(new Rect(lane.x, lane.y, lane.width, 18f));
            var rows = new List<(string name, List<TimelineKey> keys, Color color)>();
            rows.Add(("Camera" + (t.CameraEnabled ? "" : " (off)"), t.CameraKeys.Cast<TimelineKey>().ToList(), CamKeyColor));
            foreach (ActorTrack track in t.Tracks)
            {
                rows.Add(((track.Actor != null ? track.Actor.Name : track.ActorName + " (missing)"), track.Keys.Cast<TimelineKey>().ToList(), KeyColor));
            }
            foreach (PhotoActor a in _c.Actors.Actors.Where(a => t.TrackFor(a, false) == null))
            {
                rows.Add((a.Name, new List<TimelineKey>(), KeyColor));
            }
            int visible = Mathf.Max(1, Mathf.FloorToInt((lane.height - 20f) / RowH));
            _rowScroll = Mathf.Clamp(_rowScroll, 0, Mathf.Max(0, rows.Count - visible));
            float ry = lane.y + 20f;
            for (int i = _rowScroll; i < rows.Count && i < _rowScroll + visible; i++)
            {
                var row = new Rect(lane.x, ry, lane.width, RowH - 2f);
                HudTheme.Fill(row, HudTheme.P.RowFill);
                GUI.Label(new Rect(win.x + 8f, ry, NameW - 6f, RowH - 2f), rows[i].name, SpawnerWindow.Left(HudTheme.Label, wrap: false));
                DrawKeys(row, rows[i].keys, rows[i].color);
                ry += RowH;
            }
            DrawPlayhead(lane);
            HandleLaneInput(lane);
        }

        private bool Btn(ref float x, float y, float w, string text, bool active)
        {
            bool hit = SpawnerWindow.Button(new Rect(x, y, w, 26f), text, active);
            x += w + 4f;
            return hit;
        }

        private float TimeToX(Rect lane, float time) => lane.x + Mathf.Clamp01(time / Mathf.Max(0.01f, Timeline.Duration)) * lane.width;

        private float XToTime(Rect lane, float x) => Mathf.Clamp01((x - lane.x) / Mathf.Max(1f, lane.width)) * Timeline.Duration;

        private void DrawRuler(Rect r)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            HudTheme.Fill(r, HudTheme.P.TabFill);
            float d = Timeline.Duration;
            float step = d <= 10f ? 1f : d <= 30f ? 2f : d <= 120f ? 10f : 30f;
            for (float s = 0f; s <= d + 0.001f; s += step)
            {
                float x = TimeToX(r, s);
                HudTheme.Fill(new Rect(x, r.y + 10f, 1f, 8f), HudTheme.P.LineBright);
                GUI.Label(new Rect(x + 2f, r.y, 40f, 14f), s.ToString("0", CultureInfo.InvariantCulture), HudTheme.HudSmall);
            }
        }

        private void DrawKeys(Rect row, List<TimelineKey> keys, Color color)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            foreach (TimelineKey k in keys)
            {
                float x = TimeToX(row, k.Time);
                var c = new Vector2(x, row.center.y);
                Color col = k == _selected ? Color.white : color;
                Matrix4x4 saved = GUI.matrix;
                GUIUtility.RotateAroundPivot(45f, c);
                HudTheme.Fill(new Rect(c.x - 6f, c.y - 6f, 12f, 12f), new Color(0f, 0f, 0f, 0.7f));
                HudTheme.Fill(new Rect(c.x - 4.5f, c.y - 4.5f, 9f, 9f), col);
                GUI.matrix = saved;
                if (k.Ease == KeyEase.Step)
                {
                    HudTheme.Fill(new Rect(c.x + 7f, c.y - 1f, 6f, 2f), col);
                }
            }
        }

        private void DrawPlayhead(Rect lane)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            float x = TimeToX(lane, Timeline.Time);
            HudTheme.Fill(new Rect(x - 1f, lane.y, 2f, lane.height), HudTheme.P.Highlight);
            HudTheme.Fill(new Rect(x - 5f, lane.y, 10f, 6f), HudTheme.P.Highlight);
        }

        private void HandleLaneInput(Rect lane)
        {
            Event e = Event.current;
            var hitArea = new Rect(lane.x - 8f, lane.y, lane.width + 16f, lane.height);
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && hitArea.Contains(e.mousePosition):
                {
                    TimelineKey hit = KeyAt(lane, e.mousePosition);
                    Timeline.Playing = false;
                    if (hit != null)
                    {
                        _selected = hit;
                        _dragKey = hit;
                        _dragOffset = hit.Time - XToTime(lane, e.mousePosition.x);
                        Seek(hit.Time, false);
                    }
                    else
                    {
                        _scrubbing = true;
                        Seek(XToTime(lane, e.mousePosition.x), false);
                    }
                    e.Use();
                    break;
                }
                case EventType.MouseDrag when _dragKey != null:
                    _dragKey.Time = Mathf.Clamp(XToTime(lane, e.mousePosition.x) + _dragOffset, 0f, Timeline.Duration);
                    Seek(_dragKey.Time, false);
                    e.Use();
                    break;
                case EventType.MouseDrag when _scrubbing:
                    Seek(XToTime(lane, e.mousePosition.x), false);
                    e.Use();
                    break;
                case EventType.MouseUp when _dragKey != null || _scrubbing:
                    if (_dragKey != null)
                    {
                        Timeline.CameraKeys.Sort((a, b) => a.Time.CompareTo(b.Time));
                        foreach (ActorTrack t in Timeline.Tracks)
                        {
                            t.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
                        }
                    }
                    _dragKey = null;
                    _scrubbing = false;
                    _settleAt = Time.unscaledTime;
                    e.Use();
                    break;
            }
        }

        private TimelineKey KeyAt(Rect lane, Vector2 mouse)
        {
            SceneTimeline t = Timeline;
            int row = Mathf.FloorToInt((mouse.y - (lane.y + 20f)) / RowH) + _rowScroll;
            List<TimelineKey> keys = null;
            if (row == 0)
            {
                keys = t.CameraKeys.Cast<TimelineKey>().ToList();
            }
            else if (row > 0 && row - 1 < t.Tracks.Count)
            {
                keys = t.Tracks[row - 1].Keys.Cast<TimelineKey>().ToList();
            }
            if (keys == null || mouse.y < lane.y + 20f)
            {
                return null;
            }
            return keys.Where(k => Mathf.Abs(TimeToX(lane, k.Time) - mouse.x) < 8f).OrderBy(k => Mathf.Abs(TimeToX(lane, k.Time) - mouse.x)).FirstOrDefault();
        }

        private void JumpKey(int dir)
        {
            List<float> times = Timeline.AllTimes();
            float now = Timeline.Time;
            float? target = dir > 0 ? times.Where(x => x > now + 0.001f).Cast<float?>().FirstOrDefault() : times.Where(x => x < now - 0.001f).Cast<float?>().LastOrDefault();
            if (target.HasValue)
            {
                Timeline.Playing = false;
                Seek(target.Value, true);
            }
        }

        private void DeleteSelected()
        {
            if (_selected is CameraKey ck)
            {
                Timeline.CameraKeys.Remove(ck);
            }
            else if (_selected is PoseKey pk)
            {
                foreach (ActorTrack t in Timeline.Tracks)
                {
                    t.Keys.Remove(pk);
                }
            }
            _selected = null;
            Say("Key deleted.");
        }
    }
}
