using System;
using System.Collections.Generic;
using System.Linq;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed class AddonCreatorWindow
    {
        private const float W = 1240f;
        private const float H = 700f;
        private const float RowH = 26f;

        private string[] _packs = new string[0];
        private string _pack;
        private int _kind;
        private readonly HashSet<string> _ownPicked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _packPicked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private float _ownScroll;
        private float _packScroll;
        private float _packListScroll;
        private int _hoverList = -1;
        private string _newPack = "";
        private string _newAuthor = "";
        private string _infoName = "";
        private string _infoAuthor = "";
        private string _infoVersion = "";
        private string _infoDescription = "";
        private string _infoLink = "";
        private string _renameKey;
        private string _renameText = "";
        private string _message = "";
        private Rect _screenRect;
        private int _lastDrawFrame;

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
            foreach (ContentKind kind in AddonCreator.Kinds)
            {
                AddonCreator.Refresh(kind);
            }
            ReloadPacks();
            _message = "Pick or create a pack on the left, then tick your own items and add them to it.";
        }

        public void Close()
        {
            Open = false;
            InputContext.LeaveField();
        }

        public void Back() => Close();

        public void Scroll(float notches)
        {
            float step = notches * RowH * 2f;
            if (_hoverList == 0)
            {
                _ownScroll = Mathf.Max(0f, _ownScroll - step);
            }
            else if (_hoverList == 1)
            {
                _packScroll = Mathf.Max(0f, _packScroll - step);
            }
            else if (_hoverList == 2)
            {
                _packListScroll = Mathf.Max(0f, _packListScroll - step);
            }
        }

        private ContentKind Kind => AddonCreator.Kinds[Mathf.Clamp(_kind, 0, AddonCreator.Kinds.Length - 1)];

        private void ReloadPacks()
        {
            _packs = AddonCreator.AllPacks();
            if (_pack != null && !_packs.Contains(_pack, StringComparer.OrdinalIgnoreCase))
            {
                _pack = null;
            }
            if (_pack == null && _packs.Length > 0)
            {
                SelectPack(_packs[0]);
            }
        }

        private void SelectPack(string pack)
        {
            _pack = pack;
            _packPicked.Clear();
            _packScroll = 0f;
            PackInfo info = pack == null ? null : AddonCreator.Info(pack);
            _infoName = pack ?? "";
            _infoAuthor = info?.Author ?? "";
            _infoVersion = info?.Version ?? "1.0";
            _infoDescription = info?.Description ?? "";
            _infoLink = info?.Link ?? "";
            _renameKey = null;
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
            _hoverList = -1;

            HudTheme.Fill(win, new Color(0.03f, 0.03f, 0.035f, 0.97f));
            HudTheme.Border(win, HudTheme.P.LineBright);
            var title = new Rect(win.x, win.y, win.width, 40f);
            HudTheme.Fill(title, HudTheme.P.TabFill);
            GUI.Label(new Rect(title.x + 14f, title.y + 6f, 700f, 30f), "ADD-ON CREATOR", SpawnerWindow.Left(HudTheme.Title, wrap: false));
            if (SpawnerWindow.Button(new Rect(win.xMax - 40f, win.y + 6f, 30f, 28f), "X", false))
            {
                Close();
                return;
            }

            float top = win.y + 50f;
            float bottom = win.yMax - 60f;
            DrawPacks(new Rect(win.x + 12f, top, 340f, bottom - top));
            DrawContent(new Rect(win.x + 364f, top, win.width - 376f, bottom - top));

            var bar = new Rect(win.x + 12f, bottom + 8f, win.width - 24f, 44f);
            HudTheme.Fill(bar, HudTheme.P.TabFill);
            GUI.Label(new Rect(bar.x + 12f, bar.y + 3f, bar.width - 24f, 20f), _message ?? "", SpawnerWindow.Left(HudTheme.Note, wrap: false));
            GUI.Label(new Rect(bar.x + 12f, bar.y + 22f, bar.width - 24f, 20f), "To share a pack, zip the DuckCam folder with only your pack folders inside. Esc or X closes.", SpawnerWindow.Left(HudTheme.Note, wrap: false));
        }

        private void DrawPacks(Rect r)
        {
            HudTheme.Fill(r, HudTheme.P.RowFill);
            float x = r.x + 8f;
            float w = r.width - 16f;
            float y = r.y + 6f;
            GUI.Label(new Rect(x, y, w, 22f), "YOUR PACKS", SpawnerWindow.Left(HudTheme.Section, wrap: false));
            y += 26f;

            var list = new Rect(x, y, w, 170f);
            if (list.Contains(Event.current.mousePosition))
            {
                _hoverList = 2;
            }
            if (_packs.Length == 0)
            {
                GUI.Label(list, "No packs yet. Make one below.", SpawnerWindow.Left(HudTheme.Note, wrap: true));
            }
            float maxScroll = Mathf.Max(0f, _packs.Length * RowH - list.height);
            _packListScroll = Mathf.Clamp(_packListScroll, 0f, maxScroll);
            GUI.BeginGroup(list);
            for (int i = 0; i < _packs.Length; i++)
            {
                var row = new Rect(0f, i * RowH - _packListScroll, list.width, RowH - 2f);
                if (row.yMax < 0f || row.y > list.height)
                {
                    continue;
                }
                string p = _packs[i];
                if (SpawnerWindow.Button(row, $"{p}  ({AddonCreator.Count(p)})", string.Equals(p, _pack, StringComparison.OrdinalIgnoreCase)))
                {
                    SelectPack(p);
                }
            }
            GUI.EndGroup();
            y += list.height + 8f;

            GUI.Label(new Rect(x, y, w, 22f), "NEW PACK", SpawnerWindow.Left(HudTheme.Section, wrap: false));
            y += 24f;
            _newPack = Field(new Rect(x, y, w, 24f), "Name", "AddonNewName", _newPack);
            y += 28f;
            _newAuthor = Field(new Rect(x, y, w, 24f), "Author", "AddonNewAuthor", _newAuthor);
            y += 28f;
            if (SpawnerWindow.Button(new Rect(x, y, w, 26f), "CREATE PACK", false))
            {
                Try(() =>
                {
                    string pack = AddonCreator.NewPack(Kind, _newPack, _newAuthor);
                    _newPack = "";
                    ReloadPacks();
                    SelectPack(pack);
                    _message = $"Pack '{pack}' created. Tick your own items and press MOVE or COPY to add them.";
                });
            }
            y += 36f;

            if (_pack == null)
            {
                return;
            }
            GUI.Label(new Rect(x, y, w, 22f), "PACK INFO", SpawnerWindow.Left(HudTheme.Section, wrap: false));
            y += 24f;
            _infoName = Field(new Rect(x, y, w, 24f), "Name", "AddonInfoName", _infoName);
            y += 28f;
            _infoAuthor = Field(new Rect(x, y, w, 24f), "Author", "AddonInfoAuthor", _infoAuthor);
            y += 28f;
            _infoVersion = Field(new Rect(x, y, w, 24f), "Version", "AddonInfoVersion", _infoVersion);
            y += 28f;
            _infoDescription = Field(new Rect(x, y, w, 24f), "About", "AddonInfoDescription", _infoDescription);
            y += 28f;
            _infoLink = Field(new Rect(x, y, w, 24f), "Link", "AddonInfoLink", _infoLink);
            y += 28f;
            if (SpawnerWindow.Button(new Rect(x, y, w, 26f), "SAVE INFO", false))
            {
                Try(() =>
                {
                    string pack = AddonCreator.RenamePack(_pack, _infoName);
                    AddonCreator.SaveInfo(pack, new PackInfo
                    {
                        Author = Blank(_infoAuthor),
                        Version = Blank(_infoVersion),
                        Description = Blank(_infoDescription),
                        Link = Blank(_infoLink)
                    });
                    ReloadPacks();
                    SelectPack(pack);
                    _message = $"Saved the info for '{pack}'.";
                });
            }
        }

        private void DrawContent(Rect r)
        {
            int count = AddonCreator.Kinds.Length;
            float bw = (r.width - (count - 1) * 4f) / count;
            for (int i = 0; i < count; i++)
            {
                if (SpawnerWindow.Button(new Rect(r.x + i * (bw + 4f), r.y, bw, 28f), AddonCreator.Kinds[i].Title.ToUpperInvariant(), i == _kind))
                {
                    if (_kind != i)
                    {
                        _kind = i;
                        _ownPicked.Clear();
                        _packPicked.Clear();
                        _ownScroll = 0f;
                        _packScroll = 0f;
                        _renameKey = null;
                    }
                }
            }

            ContentKind kind = Kind;
            string[] own;
            string[] inPack;
            try
            {
                own = AddonCreator.Own(kind);
                inPack = AddonCreator.InPack(kind, _pack);
            }
            catch (Exception ex)
            {
                _message = kind.Title + " could not be listed: " + ex.Message;
                return;
            }
            _ownPicked.RemoveWhere(k => !own.Contains(k, StringComparer.OrdinalIgnoreCase));
            _packPicked.RemoveWhere(k => !inPack.Contains(k, StringComparer.OrdinalIgnoreCase));

            float top = r.y + 38f;
            float colW = (r.width - 12f) * 0.5f;
            float listH = r.height - 38f - 104f;
            var ownRect = new Rect(r.x, top, colW, listH);
            var packRect = new Rect(r.x + colW + 12f, top, colW, listH);
            DrawList(ownRect, $"MY OWN {kind.Title.ToUpperInvariant()} ({own.Length})", own, _ownPicked, ref _ownScroll, 0);
            DrawList(packRect, _pack == null ? "NO PACK PICKED" : $"IN {_pack.ToUpperInvariant()} ({inPack.Length})", inPack, _packPicked, ref _packScroll, 1);

            float y = top + listH + 6f;
            float hw = (colW - 6f) * 0.5f;
            if (SpawnerWindow.Button(new Rect(ownRect.x, y, hw, 26f), "SELECT ALL", false))
            {
                _ownPicked.UnionWith(own);
            }
            if (SpawnerWindow.Button(new Rect(ownRect.x + hw + 6f, y, hw, 26f), "SELECT NONE", false))
            {
                _ownPicked.Clear();
            }
            if (SpawnerWindow.Button(new Rect(packRect.x, y, hw, 26f), "SELECT ALL", false))
            {
                _packPicked.UnionWith(inPack);
            }
            if (SpawnerWindow.Button(new Rect(packRect.x + hw + 6f, y, hw, 26f), "SELECT NONE", false))
            {
                _packPicked.Clear();
            }
            y += 32f;
            if (SpawnerWindow.Button(new Rect(ownRect.x, y, hw, 26f), "MOVE TO PACK >", false))
            {
                Transfer(kind, _ownPicked, _pack, copy: false);
            }
            if (SpawnerWindow.Button(new Rect(ownRect.x + hw + 6f, y, hw, 26f), "COPY TO PACK >", false))
            {
                Transfer(kind, _ownPicked, _pack, copy: true);
            }
            if (SpawnerWindow.Button(new Rect(packRect.x, y, hw, 26f), "< MOVE TO MY OWN", false))
            {
                Transfer(kind, _packPicked, null, copy: false);
            }
            if (SpawnerWindow.Button(new Rect(packRect.x + hw + 6f, y, hw, 26f), "< COPY TO MY OWN", false))
            {
                Transfer(kind, _packPicked, null, copy: true);
            }
            y += 34f;
            DrawRename(new Rect(r.x, y, r.width, 26f), kind);
        }

        private void DrawList(Rect r, string header, string[] keys, HashSet<string> picked, ref float scroll, int id)
        {
            HudTheme.Fill(r, HudTheme.P.RowFill);
            GUI.Label(new Rect(r.x + 8f, r.y + 4f, r.width - 16f, 22f), header, SpawnerWindow.Left(HudTheme.Section, wrap: false));
            var list = new Rect(r.x + 6f, r.y + 30f, r.width - 12f, r.height - 36f);
            if (list.Contains(Event.current.mousePosition))
            {
                _hoverList = id;
            }
            if (keys.Length == 0)
            {
                GUI.Label(list, id == 0 ? "Nothing saved yet." : _pack == null ? "Pick or create a pack on the left." : "Empty. Add items from the left.", SpawnerWindow.Left(HudTheme.Note, wrap: true));
                return;
            }
            scroll = Mathf.Clamp(scroll, 0f, Mathf.Max(0f, keys.Length * RowH - list.height));
            GUI.BeginGroup(list);
            for (int i = 0; i < keys.Length; i++)
            {
                var row = new Rect(0f, i * RowH - scroll, list.width, RowH - 2f);
                if (row.yMax < 0f || row.y > list.height)
                {
                    continue;
                }
                string key = keys[i];
                bool on = picked.Contains(key);
                bool hover = row.Contains(Event.current.mousePosition);
                if (Event.current.type == EventType.Repaint)
                {
                    HudTheme.Fill(row, on ? HudTheme.P.RowFillSelected : hover ? HudTheme.P.TabFillActive : HudTheme.P.TabFill);
                    var box = new Rect(row.x + 4f, row.y + 3f, 18f, 18f);
                    HudTheme.Border(box, on ? HudTheme.P.Highlight : HudTheme.P.Line);
                    if (on)
                    {
                        HudTheme.Fill(new Rect(box.x + 4f, box.y + 4f, 10f, 10f), HudTheme.P.Highlight);
                    }
                }
                GUI.Label(new Rect(row.x + 30f, row.y, row.width - 34f, row.height), ContentPacks.NameOf(key), SpawnerWindow.Left(on ? HudTheme.ValueSelected : HudTheme.Value, wrap: false));
                if (GUI.Button(row, GUIContent.none, GUIStyle.none))
                {
                    if (!picked.Remove(key))
                    {
                        picked.Add(key);
                    }
                }
            }
            GUI.EndGroup();
        }

        private void DrawRename(Rect r, ContentKind kind)
        {
            string only = _ownPicked.Count + _packPicked.Count == 1 ? _ownPicked.Concat(_packPicked).First() : null;
            if (only != _renameKey)
            {
                _renameKey = only;
                _renameText = only == null ? "" : ContentPacks.NameOf(only);
            }
            if (only == null)
            {
                GUI.Label(r, "Tick exactly one item to rename it.", SpawnerWindow.Left(HudTheme.Note, wrap: false));
                return;
            }
            GUI.Label(new Rect(r.x, r.y, 90f, r.height), "RENAME", SpawnerWindow.Left(HudTheme.Label, wrap: false));
            GUI.SetNextControlName(HudKit.FieldPrefix + "AddonRename");
            _renameText = GUI.TextField(new Rect(r.x + 90f, r.y + 1f, r.width - 90f - 146f, r.height - 2f), _renameText ?? "", 60, HudTheme.Field);
            if (SpawnerWindow.Button(new Rect(r.xMax - 140f, r.y, 140f, r.height), "RENAME", false))
            {
                Try(() =>
                {
                    bool inPack = _packPicked.Contains(only);
                    string newKey = AddonCreator.Rename(kind, only, _renameText);
                    (inPack ? _packPicked : _ownPicked).Clear();
                    (inPack ? _packPicked : _ownPicked).Add(newKey);
                    _renameKey = newKey;
                    _message = $"Renamed to '{ContentPacks.NameOf(newKey)}'.";
                });
            }
        }

        private void Transfer(ContentKind kind, HashSet<string> picked, string pack, bool copy)
        {
            if (picked.Count == 0)
            {
                _message = "Tick some items first.";
                return;
            }
            if (picked == _ownPicked && _pack == null)
            {
                _message = "Pick or create a pack on the left first.";
                return;
            }
            Try(() =>
            {
                int n = AddonCreator.Transfer(kind, picked, pack, copy);
                picked.Clear();
                ReloadPacks();
                string where = pack == null ? "your own items" : "'" + pack + "'";
                _message = $"{(copy ? "Copied" : "Moved")} {n} {kind.Title.ToLowerInvariant()} to {where}.";
            });
        }

        private static string Field(Rect r, string label, string id, string value)
        {
            GUI.Label(new Rect(r.x, r.y, 70f, r.height), label.ToUpperInvariant(), SpawnerWindow.Left(HudTheme.Label, wrap: false));
            GUI.SetNextControlName(HudKit.FieldPrefix + id);
            return GUI.TextField(new Rect(r.x + 74f, r.y + 1f, r.width - 74f, r.height - 2f), value ?? "", 80, HudTheme.Field);
        }

        private static string Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private void Try(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _message = ex is InvalidOperationException ? ex.Message : "Failed: " + ex.Message;
                if (!(ex is InvalidOperationException))
                {
                    PhotoLog.Warn("Add-on creator: " + ex);
                }
            }
        }
    }
}
