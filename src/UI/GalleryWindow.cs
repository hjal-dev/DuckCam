using System;
using System.Collections.Generic;
using System.Linq;
using DuckCam.Core;
using DuckCam.Rendering;
using UnityEngine;
using UnityEngine.Video;

namespace DuckCam.UI
{
    internal sealed class GalleryWindow
    {
        private const float W = 1240f;
        private const float H = 700f;
        private const float CardW = 220f;
        private const float CardH = 168f;
        private const float Gap = 10f;

        private enum Mode
        {
            Grid,
            View,
            Compare
        }

        private static readonly string[] KindNames = { "ALL", "PHOTOS", "VIDEOS" };

        private readonly Gallery _gallery = new Gallery();
        private readonly HashSet<GalleryItem> _ticked = new HashSet<GalleryItem>();
        private Mode _mode;
        private int _kind;
        private string _map;
        private float _scroll;
        private GalleryItem _current;
        private Texture2D _view;
        private GalleryItem _compareA;
        private GalleryItem _compareB;
        private Texture2D _viewA;
        private Texture2D _viewB;
        private bool _confirmDelete;
        private float _confirmUntil;
        private string _message = "";
        private Rect _screenRect;
        private int _lastDrawFrame;

        private GameObject _videoGo;
        private VideoPlayer _player;
        private bool _muted;

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
            _mode = Mode.Grid;
            _scroll = 0f;
            _ticked.Clear();
            try
            {
                _gallery.Scan();
                _message = _gallery.Items.Count == 0 ? "No photos or videos yet. Take some first." : "Click a photo to view it. Tick two photos to compare them.";
            }
            catch (Exception ex)
            {
                _message = "The capture folders could not be read: " + ex.Message;
            }
        }

        public void Close()
        {
            Open = false;
            ClearView();
            _gallery.Clear();
            _ticked.Clear();
            InputContext.LeaveField();
        }

        public void Back()
        {
            if (_mode != Mode.Grid)
            {
                ClearView();
                _mode = Mode.Grid;
                return;
            }
            Close();
        }

        public void Scroll(float notches)
        {
            if (_mode == Mode.Grid)
            {
                _scroll = Mathf.Max(0f, _scroll - notches * (CardH + Gap) * 0.5f);
            }
        }

        private List<GalleryItem> Shown()
        {
            return _gallery.Items.Where(i => (_kind == 0 || (_kind == 1) != i.Video) && (_map == null || i.Map == _map)).ToList();
        }

        public void Draw(float vw, float vh)
        {
            if (!Open)
            {
                return;
            }
            _lastDrawFrame = Time.frameCount;
            if (Event.current.type == EventType.Repaint)
            {
                _gallery.Tick();
            }
            if (_confirmDelete && Time.unscaledTime > _confirmUntil)
            {
                _confirmDelete = false;
            }
            var win = new Rect(Mathf.Round((vw - W) * 0.5f), Mathf.Round((vh - H) * 0.5f), W, H);
            Vector2 tl = GUIUtility.GUIToScreenPoint(new Vector2(win.x, win.y));
            Vector2 br = GUIUtility.GUIToScreenPoint(new Vector2(win.xMax, win.yMax));
            _screenRect = new Rect(tl, br - tl);

            HudTheme.Fill(win, new Color(0.03f, 0.03f, 0.035f, 0.97f));
            HudTheme.Border(win, HudTheme.P.LineBright);
            var title = new Rect(win.x, win.y, win.width, 40f);
            HudTheme.Fill(title, HudTheme.P.TabFill);
            GUI.Label(new Rect(title.x + 14f, title.y + 6f, 400f, 30f), "GALLERY", SpawnerWindow.Left(HudTheme.Title, wrap: false));
            if (SpawnerWindow.Button(new Rect(win.xMax - 40f, win.y + 6f, 30f, 28f), "X", false))
            {
                Close();
                return;
            }

            var body = new Rect(win.x + 12f, win.y + 50f, win.width - 24f, win.height - 112f);
            if (_mode == Mode.Grid)
            {
                DrawGrid(win, body);
            }
            else if (_mode == Mode.View)
            {
                DrawView(body);
            }
            else
            {
                DrawCompare(body);
            }

            var bar = new Rect(win.x + 12f, win.yMax - 54f, win.width - 24f, 44f);
            HudTheme.Fill(bar, HudTheme.P.TabFill);
            GUI.Label(new Rect(bar.x + 12f, bar.y + 3f, bar.width - 24f, 20f), _message ?? "", SpawnerWindow.Left(HudTheme.Note, wrap: false));
            GUI.Label(new Rect(bar.x + 12f, bar.y + 22f, bar.width - 24f, 20f), "Photos: " + ScreenshotController.Directory + "   Videos: " + VideoRecorder.Directory + "   Esc goes back.", SpawnerWindow.Left(HudTheme.Note, wrap: false));
        }

        private void DrawGrid(Rect win, Rect body)
        {
            float x = win.x + 200f;
            for (int i = 0; i < KindNames.Length; i++)
            {
                if (SpawnerWindow.Button(new Rect(x, win.y + 6f, 90f, 28f), KindNames[i], _kind == i))
                {
                    _kind = i;
                    _scroll = 0f;
                }
                x += 94f;
            }
            string[] maps = _gallery.Items.Select(i => i.Map).Where(m => m.Length > 0).Distinct().OrderBy(m => m).ToArray();
            if (SpawnerWindow.Button(new Rect(x + 6f, win.y + 6f, 170f, 28f), "MAP: " + (_map ?? "ALL").ToUpperInvariant(), false))
            {
                int at = _map == null ? -1 : Array.IndexOf(maps, _map);
                _map = at + 1 < maps.Length ? maps[at + 1] : null;
                _scroll = 0f;
            }

            List<GalleryItem> shown = Shown();
            _ticked.RemoveWhere(t => !_gallery.Items.Contains(t));
            float right = win.xMax - 48f;
            if (SpawnerWindow.Button(new Rect(right - 120f, win.y + 6f, 120f, 28f), "OPEN FOLDER", false))
            {
                Gallery.ShowInFolder(_kind == 2 ? VideoRecorder.Directory : ScreenshotController.Directory);
            }
            if (SpawnerWindow.Button(new Rect(right - 226f, win.y + 6f, 100f, 28f), "REFRESH", false))
            {
                Show();
                return;
            }
            string del = _confirmDelete ? $"SURE? ({_ticked.Count})" : $"DELETE ({_ticked.Count})";
            if (SpawnerWindow.Button(new Rect(right - 362f, win.y + 6f, 130f, 28f), del, _confirmDelete))
            {
                DeleteTicked();
            }
            if (SpawnerWindow.Button(new Rect(right - 478f, win.y + 6f, 110f, 28f), "COMPARE", _ticked.Count == 2))
            {
                StartCompare();
            }

            int cols = Mathf.Max(1, Mathf.FloorToInt((body.width + Gap) / (CardW + Gap)));
            float cardW = (body.width - (cols - 1) * Gap) / cols;
            int rows = Mathf.CeilToInt(shown.Count / (float)cols);
            float content = rows * (CardH + Gap);
            _scroll = Mathf.Clamp(_scroll, 0f, Mathf.Max(0f, content - body.height));
            if (shown.Count == 0)
            {
                GUI.Label(body, "Nothing here yet.", SpawnerWindow.Left(HudTheme.Note, wrap: true));
                return;
            }
            GUI.BeginGroup(body);
            for (int i = 0; i < shown.Count; i++)
            {
                var card = new Rect((i % cols) * (cardW + Gap), (i / cols) * (CardH + Gap) - _scroll, cardW, CardH);
                if (card.yMax < 0f || card.y > body.height)
                {
                    continue;
                }
                DrawCard(card, shown[i]);
            }
            GUI.EndGroup();
        }

        private void DrawCard(Rect card, GalleryItem item)
        {
            bool ticked = _ticked.Contains(item);
            bool hover = card.Contains(Event.current.mousePosition);
            var thumb = new Rect(card.x, card.y, card.width, card.height - 44f);
            if (Event.current.type == EventType.Repaint)
            {
                HudTheme.Fill(card, hover ? HudTheme.P.TabFillActive : HudTheme.P.RowFill);
                if (item.Thumb != null)
                {
                    GUI.DrawTexture(thumb, item.Thumb, ScaleMode.ScaleAndCrop);
                }
                HudTheme.Border(card, ticked ? HudTheme.P.Highlight : HudTheme.P.Line);
            }
            if (item.Thumb == null)
            {
                GUI.Label(thumb, item.ThumbFailed ? (item.Video ? "VIDEO" : "NO PREVIEW") : "LOADING...", SpawnerWindow.Left(HudTheme.HudSmall, wrap: false));
            }
            if (item.Video)
            {
                GUI.Label(new Rect(thumb.xMax - 64f, thumb.y + 4f, 60f, 20f), "VIDEO", HudTheme.HudSmall);
            }
            GUI.Label(new Rect(card.x + 6f, thumb.yMax + 2f, card.width - 12f, 20f), item.Map.Length > 0 ? item.Map : item.Name, SpawnerWindow.Left(HudTheme.Value, wrap: false));
            GUI.Label(new Rect(card.x + 6f, thumb.yMax + 22f, card.width - 12f, 18f), item.Time.ToString("yyyy-MM-dd HH:mm") + "  " + Gallery.Size(item.Bytes), SpawnerWindow.Left(HudTheme.Note, wrap: false));

            var box = new Rect(card.x + 6f, card.y + 6f, 24f, 24f);
            if (Event.current.type == EventType.Repaint)
            {
                HudTheme.Fill(box, new Color(0f, 0f, 0f, 0.6f));
                HudTheme.Border(box, ticked ? HudTheme.P.Highlight : HudTheme.P.Line);
                if (ticked)
                {
                    HudTheme.Fill(new Rect(box.x + 5f, box.y + 5f, 14f, 14f), HudTheme.P.Highlight);
                }
            }
            if (GUI.Button(box, GUIContent.none, GUIStyle.none))
            {
                if (!_ticked.Remove(item))
                {
                    _ticked.Add(item);
                }
                _confirmDelete = false;
                return;
            }
            if (GUI.Button(card, GUIContent.none, GUIStyle.none))
            {
                OpenItem(item);
            }
        }

        private void OpenItem(GalleryItem item)
        {
            ClearView();
            _current = item;
            _mode = Mode.View;
            _confirmDelete = false;
            try
            {
                if (item.Video)
                {
                    PlayVideo(item);
                    _message = item.Name;
                }
                else
                {
                    _view = Gallery.LoadForView(item);
                    _message = _view == null ? "This photo could not be opened." : item.Name;
                }
            }
            catch (Exception ex)
            {
                _message = "Could not open " + item.Name + ": " + ex.Message;
            }
        }

        private void DrawView(Rect body)
        {
            GalleryItem item = _current;
            if (item == null)
            {
                _mode = Mode.Grid;
                return;
            }
            var image = new Rect(body.x, body.y, body.width - 300f, body.height);
            var side = new Rect(image.xMax + 10f, body.y, 290f, body.height);
            HudTheme.Fill(image, Color.black);
            Texture tex = item.Video ? (_player != null ? _player.texture : null) : _view;
            var picture = item.Video ? new Rect(image.x, image.y, image.width, image.height - 40f) : image;
            if (tex != null && Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(picture, tex, ScaleMode.ScaleToFit);
            }
            if (item.Video)
            {
                DrawVideoControls(new Rect(image.x + 8f, image.yMax - 34f, image.width - 16f, 28f));
            }

            Event e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.RightArrow))
            {
                Step(e.keyCode == KeyCode.RightArrow ? 1 : -1);
                e.Use();
                return;
            }

            HudTheme.Fill(side, HudTheme.P.RowFill);
            float y = side.y + 8f;
            float x = side.x + 8f;
            float w = side.width - 16f;
            float hw = (w - 6f) * 0.5f;
            if (SpawnerWindow.Button(new Rect(x, y, hw, 28f), "< PREV", false))
            {
                Step(-1);
                return;
            }
            if (SpawnerWindow.Button(new Rect(x + hw + 6f, y, hw, 28f), "NEXT >", false))
            {
                Step(1);
                return;
            }
            y += 36f;
            foreach (string line in Details(item))
            {
                GUI.Label(new Rect(x, y, w, 20f), line, SpawnerWindow.Left(HudTheme.Note, wrap: false));
                y += 20f;
            }
            float by = side.yMax - 8f - 28f;
            if (SpawnerWindow.Button(new Rect(x, by, w, 28f), _confirmDelete ? "SURE? CLICK AGAIN" : "DELETE", _confirmDelete))
            {
                if (_confirmDelete)
                {
                    DeleteCurrent();
                }
                else
                {
                    _confirmDelete = true;
                    _confirmUntil = Time.unscaledTime + 4f;
                }
                return;
            }
            by -= 34f;
            if (SpawnerWindow.Button(new Rect(x, by, w, 28f), "SHOW IN FOLDER", false))
            {
                Gallery.ShowInFolder(item.Path);
            }
            by -= 34f;
            if (SpawnerWindow.Button(new Rect(x, by, w, 28f), "BACK TO GRID", false))
            {
                Back();
            }
        }

        private IEnumerable<string> Details(GalleryItem item)
        {
            yield return item.Name;
            yield return item.Time.ToString("yyyy-MM-dd  HH:mm:ss");
            if (item.Map.Length > 0)
            {
                yield return "Map: " + item.Map;
            }
            yield return "Size: " + Gallery.Size(item.Bytes);
            if (item.Video)
            {
                if (_player != null && _player.isPrepared)
                {
                    yield return $"Video: {_player.width} x {_player.height}, {_player.length:0.0} s";
                }
                yield break;
            }
            if (_view != null)
            {
                yield return $"Shown at: {_view.width} x {_view.height}";
            }
            Dictionary<string, object> meta = Gallery.Sidecar(item);
            if (meta == null)
            {
                yield break;
            }
            if (meta.TryGetValue("resolution", out object res) && res is Newtonsoft.Json.Linq.JArray r && r.Count == 2)
            {
                yield return $"Photo: {r[0]} x {r[1]}";
            }
            if (meta.TryGetValue("frameAspect", out object aspect))
            {
                yield return "Frame: " + aspect;
            }
            if (meta.TryGetValue("camera", out object camObj) && camObj is Newtonsoft.Json.Linq.JObject cam)
            {
                if (cam["fov"] != null)
                {
                    yield return $"FOV: {(float)cam["fov"]:0.0}";
                }
                if (cam["mode"] != null)
                {
                    yield return "Camera: " + cam["mode"];
                }
            }
            if (meta.TryGetValue("mod", out object mod))
            {
                yield return "Made with " + mod;
            }
        }

        private void Step(int dir)
        {
            List<GalleryItem> shown = Shown();
            if (shown.Count == 0 || _current == null)
            {
                return;
            }
            int at = shown.IndexOf(_current);
            int next = ((at < 0 ? 0 : at + dir) % shown.Count + shown.Count) % shown.Count;
            OpenItem(shown[next]);
        }

        private void DeleteCurrent()
        {
            GalleryItem item = _current;
            List<GalleryItem> shown = Shown();
            int at = shown.IndexOf(item);
            ClearView();
            try
            {
                _gallery.Delete(item);
                _ticked.Remove(item);
                _message = "Deleted " + item.Name + ".";
            }
            catch (Exception ex)
            {
                _message = "Delete failed: " + ex.Message;
                _mode = Mode.Grid;
                return;
            }
            shown.Remove(item);
            if (shown.Count == 0)
            {
                _mode = Mode.Grid;
                return;
            }
            OpenItem(shown[Mathf.Clamp(at, 0, shown.Count - 1)]);
            _message = "Deleted " + item.Name + ".";
        }

        private void DeleteTicked()
        {
            if (_ticked.Count == 0)
            {
                _message = "Tick photos or videos first.";
                return;
            }
            if (!_confirmDelete)
            {
                _confirmDelete = true;
                _confirmUntil = Time.unscaledTime + 4f;
                _message = $"Click again to delete {_ticked.Count} file(s) for good.";
                return;
            }
            _confirmDelete = false;
            int done = 0;
            foreach (GalleryItem item in _ticked.ToList())
            {
                try
                {
                    _gallery.Delete(item);
                    done++;
                }
                catch (Exception ex)
                {
                    PhotoLog.Warn("Gallery delete failed for " + item.Name + ": " + ex.Message);
                }
            }
            _ticked.Clear();
            _message = $"Deleted {done} file(s).";
        }

        private void StartCompare()
        {
            if (_ticked.Count != 2)
            {
                _message = "Tick exactly two photos to compare them.";
                return;
            }
            if (_ticked.Any(t => t.Video))
            {
                _message = "Compare works with photos only.";
                return;
            }
            ClearView();
            GalleryItem[] pair = _ticked.OrderBy(t => t.Time).ToArray();
            _compareA = pair[0];
            _compareB = pair[1];
            try
            {
                _viewA = Gallery.LoadForView(_compareA);
                _viewB = Gallery.LoadForView(_compareB);
                _mode = Mode.Compare;
                _message = "Older photo on the left, newer on the right.";
            }
            catch (Exception ex)
            {
                ClearView();
                _message = "Could not open the photos: " + ex.Message;
            }
        }

        private void DrawCompare(Rect body)
        {
            float half = (body.width - 10f) * 0.5f;
            DrawSide(new Rect(body.x, body.y, half, body.height), _compareA, _viewA);
            DrawSide(new Rect(body.x + half + 10f, body.y, half, body.height), _compareB, _viewB);
            if (SpawnerWindow.Button(new Rect(body.xMax - 150f, body.y + 6f, 140f, 28f), "BACK TO GRID", false))
            {
                Back();
            }
        }

        private static void DrawSide(Rect r, GalleryItem item, Texture2D tex)
        {
            HudTheme.Fill(r, Color.black);
            var image = new Rect(r.x, r.y, r.width, r.height - 26f);
            if (tex != null && Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(image, tex, ScaleMode.ScaleToFit);
            }
            if (item != null)
            {
                GUI.Label(new Rect(r.x + 6f, image.yMax + 3f, r.width - 12f, 20f), item.Name + "  " + item.Time.ToString("yyyy-MM-dd HH:mm"), SpawnerWindow.Left(HudTheme.Note, wrap: false));
            }
        }

        private void PlayVideo(GalleryItem item)
        {
            _videoGo = new GameObject("DuckCam_GalleryVideo") { hideFlags = HideFlags.HideAndDontSave };
            _player = _videoGo.AddComponent<VideoPlayer>();
            _player.playOnAwake = false;
            _player.source = VideoSource.Url;
            _player.url = item.Path;
            _player.renderMode = VideoRenderMode.APIOnly;
            _player.timeReference = VideoTimeReference.Freerun;
            _player.isLooping = true;
            _player.audioOutputMode = VideoAudioOutputMode.Direct;
            _player.errorReceived += (p, msg) => _message = "Video could not play: " + msg;
            _player.prepareCompleted += p =>
            {
                p.SetDirectAudioMute(0, _muted);
                p.Play();
            };
            _player.Prepare();
        }

        private void DrawVideoControls(Rect r)
        {
            if (_player == null)
            {
                return;
            }
            if (SpawnerWindow.Button(new Rect(r.x, r.y, 80f, r.height), _player.isPlaying ? "PAUSE" : "PLAY", false))
            {
                if (_player.isPlaying)
                {
                    _player.Pause();
                }
                else
                {
                    _player.Play();
                }
            }
            if (SpawnerWindow.Button(new Rect(r.x + 86f, r.y, 80f, r.height), _muted ? "UNMUTE" : "MUTE", _muted))
            {
                _muted = !_muted;
                if (_player.isPrepared)
                {
                    _player.SetDirectAudioMute(0, _muted);
                }
            }
            var track = new Rect(r.x + 176f, r.y + 8f, r.width - 176f - 90f, r.height - 16f);
            double length = _player.length;
            float t = length > 0.0 ? (float)(_player.time / length) : 0f;
            if (Event.current.type == EventType.Repaint)
            {
                HudTheme.Fill(track, HudTheme.P.TabFill);
                HudTheme.Fill(new Rect(track.x, track.y, track.width * Mathf.Clamp01(t), track.height), HudTheme.P.Highlight);
            }
            GUI.Label(new Rect(track.xMax + 8f, r.y, 84f, r.height), $"{_player.time:0.0} / {length:0.0}s", SpawnerWindow.Left(HudTheme.Note, wrap: false));
            Event e = Event.current;
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && track.Contains(e.mousePosition) && length > 0.0 && _player.canSetTime)
            {
                _player.time = Mathf.Clamp01((e.mousePosition.x - track.x) / track.width) * length;
                e.Use();
            }
        }

        private void ClearView()
        {
            if (_player != null)
            {
                _player.Stop();
            }
            if (_videoGo != null)
            {
                UnityEngine.Object.Destroy(_videoGo);
            }
            _videoGo = null;
            _player = null;
            DestroyTex(ref _view);
            DestroyTex(ref _viewA);
            DestroyTex(ref _viewB);
            _current = null;
            _compareA = null;
            _compareB = null;
            _confirmDelete = false;
        }

        private static void DestroyTex(ref Texture2D tex)
        {
            if (tex != null)
            {
                UnityEngine.Object.Destroy(tex);
            }
            tex = null;
        }
    }
}
