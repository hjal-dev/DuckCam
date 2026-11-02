using DuckCam.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DuckCam.UI
{
    internal static class MenuGallery
    {
        private static GalleryWindow _window;
        private static EventSystem _blocked;

        public static bool Open => _window != null && _window.Open;

        public static bool Available => DuckCamController.Instance == null && GameContext.World == null;

        public static void Tick()
        {
            if (Available && DuckCamConfig.GalleryKey.Value.IsDown())
            {
                if (Open)
                {
                    Close();
                }
                else
                {
                    Show();
                }
            }
            if (Open && !Available)
            {
                Close();
            }
            if (!Open && _blocked != null)
            {
                Block(false);
            }
        }

        private static void Show()
        {
            if (_window == null)
            {
                _window = new GalleryWindow();
            }
            _window.Show();
            Block(true);
            PhotoLog.Info("Gallery opened from the main menu.");
        }

        private static void Close()
        {
            _window?.Close();
            Block(false);
        }

        private static void Block(bool block)
        {
            if (block)
            {
                EventSystem es = EventSystem.current;
                if (es != null && es.enabled)
                {
                    es.enabled = false;
                    _blocked = es;
                }
            }
            else if (_blocked != null)
            {
                _blocked.enabled = true;
                _blocked = null;
            }
        }

        public static void Draw()
        {
            if (!Open)
            {
                return;
            }
            HudTheme.Ensure();
            float scale = Screen.height / 1080f * DuckCamConfig.UiScale.Value;
            float vw = Screen.width / scale;
            float vh = Screen.height / scale;
            Event e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                _window.Back();
                e.Use();
            }
            else if (e.type == EventType.ScrollWheel)
            {
                _window.Scroll(-e.delta.y / 3f);
                e.Use();
            }
            Matrix4x4 saved = GUI.matrix;
            GUI.depth = -100;
            try
            {
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
                _window.Draw(vw, vh);
            }
            finally
            {
                GUI.matrix = saved;
            }
            if (!_window.Open)
            {
                Block(false);
            }
        }
    }
}
