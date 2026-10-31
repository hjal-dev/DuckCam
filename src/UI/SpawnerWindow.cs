using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EFT;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using DuckCam.Actors;
using DuckCam.Core;
using DuckCam.Spawning;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed class SpawnerWindow
    {
        private const float W = 1240f;
        private const float H = 700f;
        private const float Card = 104f;
        private const float CardH = 122f;

        private readonly DuckCamController _c;
        private readonly BotSpawnService _service;

        private List<SpawnRole> _roles;
        private int _roleIndex;
        private BotDraft _draft;
        private bool _generating;
        private int _generation;
        private readonly Dictionary<EquipmentSlot, string> _choices = new Dictionary<EquipmentSlot, string>();
        private EquipmentSlot _slot = EquipmentSlot.FirstPrimaryWeapon;
        private string _search = "";
        private float _scroll;
        private bool _kitsTab;
        private string _kitName = "My kit";
        private List<BotKit> _kits;
        private string _confirmDelete;
        private int _count = 1;
        private bool _sessionOnly = true;
        private bool _infiniteAmmo = true;
        private int _spareMags = 3;
        private string _message = "";
        private Rect _screenRect;
        private int _lastDrawFrame;

        private PhotoActor _preview;
        private RenderTexture _previewRt;
        private GameObject _previewCam;
        private float _previewYaw = 180f;
        private readonly Dictionary<Item, ItemIcon> _slotIcons = new Dictionary<Item, ItemIcon>();

        public bool Open { get; private set; }

        public SpawnerWindow(DuckCamController controller, BotSpawnService service)
        {
            _c = controller;
            _service = service;
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

        public void Show()
        {
            if (!BotSpawnService.Available(out string reason))
            {
                _c.LastMessage = "Bot spawner unavailable: " + reason;
                return;
            }
            _target = null;
            _targetEq = null;
            Open = true;
            BotRoles.Invalidate();
            ItemCatalog.Invalidate();
            _roles = BotRoles.All();
            if (_roleIndex >= _roles.Count)
            {
                _roleIndex = 0;
            }
            _kits = BotKits.LoadAll();
            if (_draft == null)
            {
                Regenerate();
            }
            else if (_preview == null)
            {
                RebuildPreview();
            }
        }

        public void Close()
        {
            Open = false;
            _target = null;
            _targetEq = null;
            InputContext.LeaveField();
            DestroyPreview();
        }

        private PhotoActor _target;
        private InventoryEquipment _targetEq;

        private bool TargetMode => _target != null;

        public void ShowForActor(PhotoActor actor)
        {
            if (actor == null || actor.Status != ActorStatus.Ready)
            {
                _c.LastMessage = "That character isn't ready yet.";
                return;
            }
            DestroyPreview();
            _target = actor;
            _targetEq = null;
            _picking = false;
            _kitsTab = false;
            _scroll = 0f;
            _previewYaw = 180f;
            ItemCatalog.Invalidate();
            _slotIcons.Clear();
            _message = $"Editing {actor.Name}. Pick a slot, then an item.";
            Open = true;
        }

        private InventoryEquipment Equipment
        {
            get
            {
                if (!TargetMode)
                {
                    return _draft?.Equipment;
                }
                if (_targetEq == null && _target.Descriptor?.Equipment != null)
                {
                    try
                    {
                        _targetEq = _target.Descriptor.Equipment.ToEquipment();
                    }
                    catch (Exception ex)
                    {
                        PhotoLog.WarnOnce("geartarget", "Character equipment unavailable: " + ex.Message);
                    }
                }
                return _targetEq;
            }
        }

        private void ChooseForTarget(EquipmentSlot slot, string key)
        {
            Item item = null;
            if (key != BotKits.None)
            {
                Slot s = Equipment?.GetSlot(slot);
                CatalogEntry entry = s != null ? ItemCatalog.Find(slot, s, key) : null;
                item = entry != null ? ItemCatalog.CreateItem(entry) : null;
                if (item == null)
                {
                    _message = "That item couldn't be created.";
                    return;
                }
            }
            _target.ChangeGear(slot, item);
            _targetEq = null;
            _slotIcons.Clear();
            _message = _target.ReloadError != null
                ? $"{GearCatalog.SlotLabel(slot)}: {_target.ReloadError}"
                : $"{GearCatalog.SlotLabel(slot)} {(item == null ? "emptied" : "set")}.";
        }

        public void Scroll(float notches)
        {
            _scroll = Mathf.Max(0f, _scroll - notches * 60f);
        }

        private SpawnRole Role => _roles != null && _roles.Count > 0 ? _roles[Mathf.Clamp(_roleIndex, 0, _roles.Count - 1)] : null;

        private async void Regenerate()
        {
            SpawnRole role = Role;
            if (role == null)
            {
                return;
            }
            int generation = ++_generation;
            _generating = true;
            _message = $"Generating {role.Name}...";
            BotDraft draft = await _service.Generate(role);
            if (generation != _generation)
            {
                return;
            }
            _generating = false;
            if (draft == null)
            {
                _message = _service.Status;
                return;
            }
            _draft = draft;
            foreach (KeyValuePair<EquipmentSlot, string> choice in _choices.ToList())
            {
                string error = BotSpawnService.ApplyChoice(draft.Equipment, choice.Key, choice.Value);
                if (error != null)
                {
                    _message = $"{GearCatalog.SlotLabel(choice.Key)}: {error}";
                }
            }
            _slotIcons.Clear();
            _message = $"{draft.Profile?.Nickname} ({role.Name}) ready.";
            RebuildPreview();
        }

        private void RebuildPreview()
        {
            if (_draft?.Profile == null || !Open)
            {
                return;
            }
            PlayerVisualRepresentation visual;
            try
            {
                visual = _draft.Profile.GetVisualEquipmentState(true);
            }
            catch (Exception ex)
            {
                _message = "Preview failed: " + ex.Message;
                return;
            }
            if (_preview != null && _preview.Status == ActorStatus.Ready)
            {
                _preview.ReloadVisual(visual);
                return;
            }
            DestroyPreview();
            Player pmc = _c.Player;
            Vector3 basePos = pmc != null ? pmc.Transform.position : Vector3.zero;
            _preview = new PhotoActor
            {
                Name = "Spawner preview",
                SpawnPosition = basePos + Vector3.down * 600f,
                SpawnRotation = Quaternion.identity
            };
            _preview.Begin(visual, pmc != null ? ActorManager.LayerOf(pmc) : 0, null, _c.Restores);
        }

        private void DestroyPreview()
        {
            _preview?.Remove();
            _preview = null;
            if (_previewCam != null)
            {
                UnityEngine.Object.Destroy(_previewCam);
                _previewCam = null;
            }
            if (_previewRt != null)
            {
                _previewRt.Release();
                UnityEngine.Object.Destroy(_previewRt);
                _previewRt = null;
            }
        }

        public void LateTick()
        {
            if (!Open || _picking)
            {
                return;
            }
            Transform subjectRoot;
            int subjectLayer;
            if (TargetMode)
            {
                if (_target.Status != ActorStatus.Ready || _target.Root == null)
                {
                    return;
                }
                subjectRoot = _target.Root;
                subjectLayer = _target.Layer;
            }
            else
            {
                if (_preview == null)
                {
                    return;
                }
                _preview.Tick();
                if (_preview.Status != ActorStatus.Ready || _preview.Root == null)
                {
                    return;
                }
                _preview.Yaw = _previewYaw;
                _preview.ApplyTransform();
                subjectRoot = _preview.Root;
                subjectLayer = _preview.Layer;
            }
            if (_previewRt == null)
            {
                _previewRt = new RenderTexture(400, 600, 24, RenderTextureFormat.ARGB32) { name = "DuckCam_SpawnerPreview" };
                _previewRt.Create();
            }
            if (_previewCam == null)
            {
                _previewCam = new GameObject("DuckCam_SpawnerPreviewCamera");
                var cam = _previewCam.AddComponent<UnityEngine.Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.05f, 0.05f, 0.06f, 1f);
                cam.fieldOfView = 30f;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 12f;
                var light = new GameObject("DuckCam_SpawnerPreviewLight").AddComponent<Light>();
                light.transform.SetParent(_previewCam.transform, false);
                light.type = LightType.Point;
                light.range = 10f;
                light.intensity = 1.6f;
            }
            var c = _previewCam.GetComponent<UnityEngine.Camera>();
            c.cullingMask = 1 << subjectLayer;
            c.targetTexture = _previewRt;
            Transform root = subjectRoot;
            Vector3 focus = root.position + Vector3.up * 0.95f;
            Vector3 front = Vector3.forward;
            if (TargetMode)
            {
                Vector3 facing = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
                front = Quaternion.AngleAxis(_previewYaw - 180f, Vector3.up) * (facing.sqrMagnitude > 0.01f ? facing : Vector3.forward);
            }
            Vector3 eye = focus + front * 4.2f + Vector3.up * 0.1f;
            _previewCam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye, Vector3.up));
            try
            {
                c.Render();
            }
            catch (Exception ex)
            {
                PhotoLog.WarnOnce("spawnerpreview", "Spawner preview render failed: " + ex.Message);
            }
        }

        public void Draw(float vw, float vh)
        {
            if (!Open)
            {
                return;
            }
            if (_picking)
            {
                DrawPicker(vw, vh);
                return;
            }
            _lastDrawFrame = Time.frameCount;
            var win = new Rect(Mathf.Round((vw - W) * 0.5f), Mathf.Round((vh - H) * 0.5f), W, H);
            Vector2 tl = GUIUtility.GUIToScreenPoint(new Vector2(win.x, win.y));
            Vector2 br = GUIUtility.GUIToScreenPoint(new Vector2(win.xMax, win.yMax));
            _screenRect = new Rect(tl, br - tl);

            HudTheme.Fill(win, new Color(0.03f, 0.03f, 0.035f, 0.97f));
            HudTheme.Border(win, HudTheme.P.LineBright);
            var title = new Rect(win.x, win.y, win.width, 40f);
            HudTheme.Fill(title, HudTheme.P.TabFill);
            GUI.Label(new Rect(title.x + 14f, title.y + 6f, 700f, 30f), TargetMode ? "GEAR · " + _target.Name.ToUpperInvariant() : "BOT SPAWNER", Left(HudTheme.Title, wrap: false));
            if (Button(new Rect(win.xMax - 40f, win.y + 6f, 30f, 28f), "X", false))
            {
                Close();
                return;
            }

            float top = win.y + 50f;
            float bottom = win.yMax - 70f;
            DrawPreview(new Rect(win.x + 12f, top, 300f, bottom - top));
            DrawSlots(new Rect(win.x + 324f, top, 300f, bottom - top));
            var right = new Rect(win.x + 636f, top, win.width - 648f, bottom - top);
            if (TargetMode)
            {
                DrawItems(right);
                var bar = new Rect(win.x + 12f, bottom + 10f, win.width - 24f, 50f);
                HudTheme.Fill(bar, HudTheme.P.TabFill);
                GUI.Label(new Rect(bar.x + 12f, bar.y + 5f, bar.width - 24f, 20f), _message, Left(HudTheme.Note, wrap: false));
                GUI.Label(new Rect(bar.x + 12f, bar.y + 25f, bar.width - 24f, 20f), "Items are display-only copies; nobody's inventory changes. Esc or Done closes.", Left(HudTheme.Note, wrap: false));
                return;
            }
            if (Button(new Rect(right.x, right.y, 140f, 28f), "ITEMS", !_kitsTab)) _kitsTab = false;
            if (Button(new Rect(right.x + 146f, right.y, 140f, 28f), $"KITS ({_kits?.Count ?? 0})", _kitsTab)) _kitsTab = true;
            var body = new Rect(right.x, right.y + 36f, right.width, right.height - 36f);
            if (_kitsTab)
            {
                DrawKits(body);
            }
            else
            {
                DrawItems(body);
            }
            DrawBottom(new Rect(win.x + 12f, bottom + 10f, win.width - 24f, 50f));
        }

        private void DrawPreview(Rect r)
        {
            HudTheme.Fill(r, HudTheme.P.RowFill);
            if (TargetMode)
            {
                var full = new Rect(r.x, r.y, r.width, r.height);
                if (_previewRt != null)
                {
                    GUI.DrawTexture(full, _previewRt, ScaleMode.ScaleAndCrop);
                    if (_target.Reloading)
                    {
                        GUI.Label(new Rect(full.x + 8f, full.y + 6f, full.width, 22f), "Updating...", HudTheme.HudSmall);
                    }
                }
                Event te = Event.current;
                if (te.type == EventType.MouseDrag && full.Contains(te.mousePosition))
                {
                    _previewYaw -= te.delta.x * 0.6f;
                    te.Use();
                }
                GUI.Label(new Rect(full.x + 8f, full.yMax - 24f, full.width, 20f), "Drag to look around", HudTheme.HudSmall);
                return;
            }
            var view = new Rect(r.x, r.y, r.width, r.height - 96f);
            if (_previewRt != null && _preview?.Status == ActorStatus.Ready)
            {
                GUI.DrawTexture(view, _previewRt, ScaleMode.ScaleAndCrop);
                if (_preview.Reloading)
                {
                    GUI.Label(new Rect(view.x + 8f, view.y + 6f, view.width, 22f), "Updating...", HudTheme.HudSmall);
                }
            }
            else
            {
                string text = _generating ? "Generating bot..." : _preview?.Status == ActorStatus.Loading ? "Loading model..." : (_preview?.Error ?? "No preview");
                GUI.Label(new Rect(view.x, view.center.y - 12f, view.width, 24f), text, Centered(HudTheme.Note));
            }
            Event e = Event.current;
            if (e.type == EventType.MouseDrag && view.Contains(e.mousePosition))
            {
                _previewYaw -= e.delta.x * 0.6f;
                e.Use();
            }
            GUI.Label(new Rect(view.x + 8f, view.yMax - 24f, view.width, 20f), "Drag to turn", HudTheme.HudSmall);

            float y = view.yMax + 8f;
            SpawnRole role = Role;
            GUI.Label(new Rect(r.x + 8f, y, r.width - 16f, 20f), role != null ? role.Group.ToUpperInvariant() : "", HudTheme.HudSmall);
            if (Button(new Rect(r.x + 8f, y + 22f, 30f, 28f), "<", false)) StepRole(-1);
            GUI.Label(new Rect(r.x + 42f, y + 24f, r.width - 84f, 26f), role?.Name ?? "-", Centered(HudTheme.Value));
            if (Button(new Rect(r.xMax - 38f, y + 22f, 30f, 28f), ">", false)) StepRole(1);
            if (Button(new Rect(r.x + 8f, y + 56f, r.width * 0.5f - 12f, 28f), "Group >", false)) StepGroup();
            if (Button(new Rect(r.x + r.width * 0.5f + 4f, y + 56f, r.width * 0.5f - 12f, 28f), _generating ? "..." : "Reroll", false) && !_generating)
            {
                Regenerate();
            }
        }

        private void StepRole(int dir)
        {
            if (_roles == null || _roles.Count == 0)
            {
                return;
            }
            _roleIndex = (_roleIndex + dir + _roles.Count) % _roles.Count;
            Regenerate();
        }

        private void StepGroup()
        {
            if (_roles == null || _roles.Count == 0)
            {
                return;
            }
            string current = Role.Group;
            for (int i = 1; i <= _roles.Count; i++)
            {
                int idx = (_roleIndex + i) % _roles.Count;
                if (_roles[idx].Group != current)
                {
                    _roleIndex = idx;
                    break;
                }
            }
            Regenerate();
        }

        private void DrawSlots(Rect r)
        {
            HudTheme.Fill(r, HudTheme.P.RowFill);
            float rowH = (r.height - 70f) / GearCatalog.Slots.Length;
            float y = r.y + 4f;
            InventoryEquipment eq = Equipment;
            foreach (EquipmentSlot s in GearCatalog.Slots)
            {
                var row = new Rect(r.x + 4f, y, r.width - 8f, rowH - 3f);
                bool selected = s == _slot;
                HudTheme.Fill(row, selected ? HudTheme.P.RowFillSelected : new Color(0f, 0f, 0f, 0.25f));
                if (selected)
                {
                    HudTheme.Border(row, HudTheme.P.Highlight);
                }
                Item item = eq?.GetSlot(s)?.ContainedItem;
                var iconRect = new Rect(row.x + 4f, row.y + 3f, rowH * 1.4f, row.height - 6f);
                HudTheme.Fill(iconRect, new Color(0f, 0f, 0f, 0.35f));
                if (item != null)
                {
                    DrawSprite(iconRect, SlotIcon(item));
                }
                float tx = iconRect.xMax + 8f;
                string state = _choices.TryGetValue(s, out string key) ? (key == BotKits.None ? "  · empty" : "  · chosen") : "";
                GUI.Label(new Rect(tx, row.y + 1f, row.xMax - tx - 4f, 16f), GearCatalog.SlotLabel(s).ToUpperInvariant() + state, Left(HudTheme.HudSmall, wrap: false));
                GUI.Label(new Rect(tx, row.y + 16f, row.xMax - tx - 4f, row.height - 17f), item != null ? item.LocalizedName() : "(empty)", Left(HudTheme.Note, wrap: false));
                if (GUI.Button(row, GUIContent.none, GUIStyle.none))
                {
                    _slot = s;
                    _scroll = 0f;
                    _kitsTab = false;
                }
                y += rowH;
            }
            float by = r.yMax - 62f;
            if (Button(new Rect(r.x + 4f, by, r.width * 0.5f - 6f, 28f), "Empty Slot", false))
            {
                Choose(_slot, BotKits.None);
            }
            if (TargetMode)
            {
                if (Button(new Rect(r.x + r.width * 0.5f + 2f, by, r.width * 0.5f - 6f, 28f), "Reset Gear", false))
                {
                    _target.ResetGear();
                    _targetEq = null;
                    _slotIcons.Clear();
                    _message = "Gear back to how this character started.";
                }
                if (Button(new Rect(r.x + 4f, by + 32f, r.width - 8f, 28f), "Done", true))
                {
                    Close();
                }
                return;
            }
            if (Button(new Rect(r.x + r.width * 0.5f + 2f, by, r.width * 0.5f - 6f, 28f), "Use Generated", false))
            {
                _choices.Remove(_slot);
                Regenerate();
            }
            if (Button(new Rect(r.x + 4f, by + 32f, r.width - 8f, 28f), "Reset All Slots", false))
            {
                _choices.Clear();
                Regenerate();
            }
        }

        private void DrawItems(Rect r)
        {
            HudTheme.Fill(r, HudTheme.P.RowFill);
            var searchRect = new Rect(r.xMax - 268f, r.y + 5f, 260f, 26f);
            HudTheme.Fill(searchRect, new Color(0f, 0f, 0f, 0.55f));
            HudTheme.Border(searchRect, HudTheme.P.LineBright);
            GUI.SetNextControlName(HudKit.FieldPrefix + "SpawnerSearch");
            string search = GUI.TextField(new Rect(searchRect.x + 6f, searchRect.y, searchRect.width - 12f, searchRect.height), _search ?? "", 64, Left(HudTheme.Field, wrap: false));
            if (search != _search)
            {
                _search = search;
                _scroll = 0f;
            }
            if (string.IsNullOrEmpty(_search) && GUI.GetNameOfFocusedControl() != HudKit.FieldPrefix + "SpawnerSearch")
            {
                GUI.Label(new Rect(searchRect.x + 8f, searchRect.y, searchRect.width - 16f, searchRect.height), "Search...", Left(HudTheme.Note, wrap: false));
            }

            Slot slot = Equipment?.GetSlot(_slot);
            if (slot == null)
            {
                GUI.Label(new Rect(r.x + 8f, r.y + 40f, r.width, 22f), _generating ? "Waiting for the bot..." : "This bot has no such slot.", HudTheme.Note);
                return;
            }
            List<CatalogEntry> all = ItemCatalog.For(_slot, slot);
            IEnumerable<CatalogEntry> filtered = all;
            if (!string.IsNullOrWhiteSpace(_search))
            {
                string q = _search.Trim();
                filtered = all.Where(e => (e.Name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || (e.Detail ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            List<CatalogEntry> list = filtered.ToList();

            var grid = new Rect(r.x + 6f, r.y + 38f, r.width - 12f, r.height - 44f);
            int cols = Mathf.Max(1, Mathf.FloorToInt(grid.width / (Card + 6f)));
            int rows = Mathf.CeilToInt(list.Count / (float)cols);
            float content = rows * (CardH + 6f);
            _scroll = Mathf.Clamp(_scroll, 0f, Mathf.Max(0f, content - grid.height));
            int firstRow = Mathf.FloorToInt(_scroll / (CardH + 6f));
            int visibleRows = Mathf.CeilToInt(grid.height / (CardH + 6f)) + 1;
            _choices.TryGetValue(_slot, out string chosen);

            GUI.BeginGroup(grid);
            for (int row = firstRow; row < Mathf.Min(rows, firstRow + visibleRows); row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    int i = row * cols + col;
                    if (i >= list.Count)
                    {
                        break;
                    }
                    CatalogEntry entry = list[i];
                    var card = new Rect(col * (Card + 6f), row * (CardH + 6f) - _scroll, Card, CardH);
                    bool isChosen = entry.Key == chosen;
                    HudTheme.Fill(card, isChosen ? HudTheme.P.RowFillSelected : new Color(0f, 0f, 0f, 0.4f));
                    HudTheme.Border(card, isChosen ? HudTheme.P.Highlight : HudTheme.P.Line);
                    DrawSprite(new Rect(card.x + 6f, card.y + 6f, card.width - 12f, 62f), entry.Icon);
                    if (!string.IsNullOrEmpty(entry.Detail))
                    {
                        GUI.Label(new Rect(card.x + 4f, card.y + 2f, card.width - 8f, 16f), entry.Detail, entry.UserBuild ? HudTheme.HudSmall : HudTheme.Note);
                    }
                    GUI.Label(new Rect(card.x + 4f, card.y + 70f, card.width - 8f, CardH - 72f), entry.Name, CardText);
                    if (GUI.Button(card, new GUIContent("", entry.Name), GUIStyle.none))
                    {
                        Choose(_slot, entry.Key);
                    }
                }
            }
            GUI.EndGroup();
            if (list.Count == 0)
            {
                GUI.Label(new Rect(grid.x, grid.y + 10f, grid.width, 22f), "Nothing matches.", HudTheme.Note);
            }
            GUI.Label(new Rect(r.x + 8f, r.y + 5f, r.width - 290f, 26f), $"ITEM DATABASE · {GearCatalog.SlotLabel(_slot).ToUpperInvariant()} · {list.Count} items", Left(HudTheme.HudSmall, wrap: false));
        }

        private void Choose(EquipmentSlot slot, string key)
        {
            if (TargetMode)
            {
                ChooseForTarget(slot, key);
                return;
            }
            _choices[slot] = key;
            if (_draft?.Equipment == null)
            {
                return;
            }
            string error = BotSpawnService.ApplyChoice(_draft.Equipment, slot, key);
            _message = error == null ? $"{GearCatalog.SlotLabel(slot)} set." : $"{GearCatalog.SlotLabel(slot)}: {error}";
            _slotIcons.Clear();
            RebuildPreview();
        }

        private void DrawKits(Rect r)
        {
            HudTheme.Fill(r, HudTheme.P.RowFill);
            GUI.Label(new Rect(r.x + 8f, r.y + 6f, 120f, 22f), "KIT NAME", HudTheme.HudSmall);
            GUI.SetNextControlName(HudKit.FieldPrefix + "SpawnerKitName");
            _kitName = GUI.TextField(new Rect(r.x + 100f, r.y + 4f, r.width - 250f, 26f), _kitName ?? "", 40, HudTheme.Field);
            if (Button(new Rect(r.xMax - 142f, r.y + 3f, 134f, 28f), "Save Current", false))
            {
                SaveKit();
            }
            if (_kits == null || _kits.Count == 0)
            {
                GUI.Label(new Rect(r.x + 8f, r.y + 44f, r.width - 16f, 60f), "No kits yet. Set up a bot (role and gear), name it, then Save Current. Favourites (*) stay at the top.", HudTheme.Note);
                return;
            }
            float y = r.y + 40f - _scroll;
            GUI.BeginGroup(new Rect(r.x, r.y + 40f, r.width, r.height - 44f));
            y -= r.y + 40f;
            foreach (BotKit kit in _kits.ToList())
            {
                var row = new Rect(6f, y, r.width - 12f, 40f);
                y += 44f;
                if (row.yMax < 0f || row.y > r.height)
                {
                    continue;
                }
                HudTheme.Fill(row, new Color(0f, 0f, 0f, 0.35f));
                if (Button(new Rect(row.x + 4f, row.y + 6f, 30f, 28f), kit.Favorite ? "*" : "-", kit.Favorite))
                {
                    kit.Favorite = !kit.Favorite;
                    TrySave(kit);
                    _kits = BotKits.LoadAll();
                }
                GUI.Label(new Rect(row.x + 42f, row.y + 2f, row.width - 360f, 20f), kit.Name, HudTheme.Value);
                GUI.Label(new Rect(row.x + 42f, row.y + 20f, row.width - 360f, 18f), $"{kit.RoleName} · {kit.Slots.Count} slot(s){(kit.InfiniteAmmo ? " · infinite ammo" : "")}", HudTheme.Note);
                float bx = row.xMax - 312f;
                if (Button(new Rect(bx, row.y + 6f, 70f, 28f), "Load", false)) LoadKit(kit);
                if (Button(new Rect(bx + 76f, row.y + 6f, 130f, 28f), $"Place x{_count}", false)) SpawnKit(kit);
                bool confirming = _confirmDelete == kit.Name;
                if (Button(new Rect(bx + 212f, row.y + 6f, 96f, 28f), confirming ? "Sure?" : "Delete", confirming))
                {
                    if (confirming)
                    {
                        BotKits.Delete(kit);
                        _confirmDelete = null;
                        _kits = BotKits.LoadAll();
                        _message = $"Kit '{kit.Name}' deleted.";
                    }
                    else
                    {
                        _confirmDelete = kit.Name;
                    }
                }
            }
            GUI.EndGroup();
        }

        private void DrawBottom(Rect r)
        {
            HudTheme.Fill(r, HudTheme.P.TabFill);
            float x = r.x + 10f;
            GUI.Label(new Rect(x, r.y + 14f, 60f, 22f), "COUNT", HudTheme.HudSmall);
            x += 58f;
            if (Button(new Rect(x, r.y + 11f, 28f, 28f), "-", false)) _count = Mathf.Max(1, _count - 1);
            GUI.Label(new Rect(x + 30f, r.y + 13f, 36f, 24f), _count.ToString(CultureInfo.InvariantCulture), Centered(HudTheme.Value));
            if (Button(new Rect(x + 68f, r.y + 11f, 28f, 28f), "+", false)) _count = Mathf.Min(10, _count + 1);
            x += 112f;
            _sessionOnly = Check(new Rect(x, r.y + 11f, 190f, 28f), "Only in this session", _sessionOnly);
            x += 198f;
            _infiniteAmmo = Check(new Rect(x, r.y + 11f, 150f, 28f), "Infinite ammo", _infiniteAmmo);
            x += 158f;
            GUI.Label(new Rect(x, r.y + 14f, 90f, 22f), "SPARE MAGS", HudTheme.HudSmall);
            x += 92f;
            if (Button(new Rect(x, r.y + 11f, 28f, 28f), "-", false)) _spareMags = Mathf.Max(0, _spareMags - 1);
            GUI.Label(new Rect(x + 30f, r.y + 13f, 30f, 24f), _spareMags.ToString(CultureInfo.InvariantCulture), Centered(HudTheme.Value));
            if (Button(new Rect(x + 62f, r.y + 11f, 28f, 28f), "+", false)) _spareMags = Mathf.Min(8, _spareMags + 1);
            x += 100f;
            var status = new Rect(x, r.y + 5f, r.xMax - x - 370f, 20f);
            GUI.Label(status, new GUIContent(_message, _message), Left(HudTheme.Note, wrap: false));
            if (!string.IsNullOrEmpty(_service.Status) && _service.Status != _message)
            {
                GUI.Label(new Rect(status.x, status.y + 20f, status.width, 20f), new GUIContent(_service.Status, _service.Status), Left(HudTheme.Note, wrap: false));
            }
            if (Button(new Rect(r.xMax - 360f, r.y + 8f, 170f, 34f), "ADD TO PHOTO", false))
            {
                AddToPhoto();
            }
            if (Button(new Rect(r.xMax - 180f, r.y + 8f, 170f, 34f), _count > 1 ? $"PLACE x{_count}" : "PLACE & SPAWN", true))
            {
                SpawnCurrent();
            }
        }

        private BotKit CurrentKit(string name)
        {
            SpawnRole role = Role;
            return new BotKit
            {
                Name = name,
                Role = role != null ? (int)role.Role : (int)WildSpawnType.assault,
                RoleName = role?.Name,
                Slots = _choices.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                InfiniteAmmo = _infiniteAmmo,
                SpareMags = _spareMags,
                SessionOnly = _sessionOnly
            };
        }

        private bool _picking;
        private BotKit _pickKit;

        public bool Picking => Open && _picking;

        public void Back()
        {
            if (_picking)
            {
                _picking = false;
                _message = "Placing cancelled.";
                return;
            }
            Close();
        }

        private void SpawnCurrent()
        {
            if (_draft == null || _generating)
            {
                _message = "Wait for the bot to finish generating.";
                return;
            }
            _pickKit = null;
            _pickPhoto = false;
            _picking = true;
        }

        private void AddToPhoto()
        {
            if (_draft == null || _generating)
            {
                _message = "Wait for the bot to finish generating.";
                return;
            }
            _pickKit = null;
            _pickPhoto = true;
            _picking = true;
        }

        private bool _pickPhoto;

        private void SpawnKit(BotKit kit)
        {
            _pickKit = kit;
            _pickPhoto = false;
            _picking = true;
        }

        private void SpawnAt(Vector3 point)
        {
            if (_pickPhoto)
            {
                if (!_c.Frozen)
                {
                    _c.SetFrozen(true);
                    if (!_c.Frozen)
                    {
                        _message = "Could not freeze the scene for the photo character.";
                        return;
                    }
                }
                Vector3 face = _c.Camera.Camera != null ? _c.Camera.Camera.transform.position : point + Vector3.forward;
                _service.AddToPhoto(_draft, point, face, _c);
                _message = "Adding to the photo...";
                return;
            }
            if (_pickKit != null)
            {
                BotKit kit = _pickKit;
                SpawnRole role = _roles?.FirstOrDefault(r => (int)r.Role == kit.Role)
                                 ?? new SpawnRole { Role = (WildSpawnType)kit.Role, Name = kit.RoleName ?? kit.Role.ToString(CultureInfo.InvariantCulture), Group = "Other", Side = EPlayerSide.Savage };
                int n = _count;
                bool sessionOnly = _sessionOnly;
                _service.SpawnKit(kit, role, point, n, sessionOnly);
                _message = $"Spawning {n} x '{kit.Name}'...";
                return;
            }
            if (_draft == null)
            {
                _message = "Wait for the bot to finish generating.";
                return;
            }
            Loadout.ArmWeapons(_draft.Equipment, _spareMags);
            BotDraft draft = _draft;
            bool session = _sessionOnly;
            bool ammo = _infiniteAmmo;
            int extra = _count - 1;
            BotKit more = extra > 0 ? CurrentKit("current") : null;
            SpawnRole extraRole = Role;
            Action spawn = () =>
            {
                _service.Spawn(draft, point, session, ammo);
                if (more != null)
                {
                    _service.SpawnKit(more, extraRole, point, extra, session);
                }
            };
            spawn();
            _message = _c.Frozen ? "Spawning into the frozen scene..." : "Spawning...";
            _draft = null;
            Regenerate();
        }

        private void DrawPicker(float vw, float vh)
        {
            UnityEngine.Camera cam = _c.Camera.Camera;
            var banner = new Rect(vw * 0.5f - 360f, 60f, 720f, 34f);
            HudTheme.Fill(banner, new Color(0f, 0f, 0f, 0.7f));
            HudTheme.Border(banner, HudTheme.P.LineBright);
            string what = _pickPhoto ? "the photo-only character" : _pickKit != null ? $"{_count} x '{_pickKit.Name}'" : (_count > 1 ? $"{_count} bots" : "the bot");
            GUI.Label(banner, $"Click where {what} should {(_pickPhoto ? "stand" : "spawn")}  ·  Shift+click: keep placing  ·  Right mouse: look  ·  Esc: cancel", Centered(HudTheme.Value));
            if (cam == null || Input.GetMouseButton(1))
            {
                return;
            }
            Ray ray = ScreenSpace.ScreenRay(cam, Input.mousePosition);
            bool ok = BotSpawnService.TryPlace(ray, out Vector3 point, out string error, _pickPhoto);
            Vector2 at;
            if (ok)
            {
                Vector3 sp = ScreenSpace.WorldToScreen(cam, point);
                at = GUIUtility.ScreenToGUIPoint(new Vector2(sp.x, Screen.height - sp.y));
            }
            else
            {
                at = Event.current.mousePosition;
            }
            if (Event.current.type == EventType.Repaint)
            {
                Color c = ok ? new Color(0.35f, 1f, 0.45f, 0.95f) : new Color(1f, 0.3f, 0.3f, 0.95f);
                const float r = 16f;
                HudTheme.Fill(new Rect(at.x - r, at.y - 1f, r * 2f, 2f), c);
                HudTheme.Fill(new Rect(at.x - 1f, at.y - r, 2f, r * 2f), c);
                HudTheme.Border(new Rect(at.x - r * 0.6f, at.y - r * 0.6f, r * 1.2f, r * 1.2f), c, 2f);
                if (!ok)
                {
                    GUI.Label(new Rect(at.x + 20f, at.y - 12f, 320f, 24f), error, Left(HudTheme.Value, wrap: false));
                }
            }
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (ok)
                {
                    bool keep = e.shift;
                    if (!keep)
                    {
                        _picking = false;
                    }
                    SpawnAt(point);
                }
                else
                {
                    _message = "Can't spawn there: " + error;
                }
                e.Use();
            }
        }

        private void LoadKit(BotKit kit)
        {
            int idx = _roles?.FindIndex(r => (int)r.Role == kit.Role) ?? -1;
            if (idx >= 0)
            {
                _roleIndex = idx;
            }
            _choices.Clear();
            foreach (KeyValuePair<string, string> s in kit.Slots)
            {
                if (Enum.TryParse(s.Key, out EquipmentSlot slot))
                {
                    _choices[slot] = s.Value;
                }
            }
            _infiniteAmmo = kit.InfiniteAmmo;
            _spareMags = kit.SpareMags;
            _kitName = kit.Name;
            _message = $"Kit '{kit.Name}' loaded.";
            Regenerate();
        }

        private void SaveKit()
        {
            string name = string.IsNullOrWhiteSpace(_kitName) ? "Kit" : _kitName.Trim();
            BotKit existing = _kits?.FirstOrDefault(k => string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase));
            BotKit kit = CurrentKit(name);
            kit.Favorite = existing?.Favorite ?? false;
            if (TrySave(kit))
            {
                _kits = BotKits.LoadAll();
                _message = $"Kit '{name}' saved.";
            }
        }

        private bool TrySave(BotKit kit)
        {
            try
            {
                BotKits.Save(kit);
                return true;
            }
            catch (Exception ex)
            {
                _message = "Saving the kit failed: " + ex.Message;
                return false;
            }
        }

        private Sprite SlotIcon(Item item)
        {
            if (!_slotIcons.TryGetValue(item, out ItemIcon icon))
            {
                try
                {
                    icon = ItemViewFactory.LoadItemIcon(item);
                }
                catch (Exception)
                {
                    icon = null;
                }
                _slotIcons[item] = icon;
            }
            return icon?.Sprite;
        }

        private static void DrawSprite(Rect area, Sprite sprite)
        {
            if (sprite == null || sprite.texture == null || Event.current.type != EventType.Repaint)
            {
                return;
            }
            Rect tr = sprite.textureRect;
            float aspect = tr.width / Mathf.Max(1f, tr.height);
            float w = area.width;
            float h = w / aspect;
            if (h > area.height)
            {
                h = area.height;
                w = h * aspect;
            }
            var fit = new Rect(area.x + (area.width - w) * 0.5f, area.y + (area.height - h) * 0.5f, w, h);
            var uv = new Rect(tr.x / sprite.texture.width, tr.y / sprite.texture.height, tr.width / sprite.texture.width, tr.height / sprite.texture.height);
            GUI.DrawTextureWithTexCoords(fit, sprite.texture, uv);
        }

        internal static bool Button(Rect r, string text, bool active)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            if (Event.current.type == EventType.Repaint)
            {
                HudTheme.Fill(r, active ? HudTheme.P.Highlight : hover ? HudTheme.P.RowFillSelected : HudTheme.P.TabFillActive);
                HudTheme.Border(r, active ? HudTheme.P.Highlight : HudTheme.P.Line);
            }
            GUI.Label(r, Core.Locale.T(text), Centered(active ? HudTheme.BoxTextHi : HudTheme.BoxText));
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        private static bool Check(Rect r, string text, bool value)
        {
            var box = new Rect(r.x, r.y + 5f, 18f, 18f);
            HudTheme.Fill(box, value ? HudTheme.P.Highlight : new Color(0f, 0f, 0f, 0.5f));
            HudTheme.Border(box, HudTheme.P.LineBright);
            GUI.Label(new Rect(r.x + 26f, r.y + 3f, r.width - 26f, 24f), text, HudTheme.Label);
            return GUI.Button(r, GUIContent.none, GUIStyle.none) ? !value : value;
        }

        private static readonly Dictionary<GUIStyle, GUIStyle> CenteredCache = new Dictionary<GUIStyle, GUIStyle>();

        internal static GUIStyle Centered(GUIStyle s)
        {
            if (!CenteredCache.TryGetValue(s, out GUIStyle c))
            {
                c = new GUIStyle(s) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
                CenteredCache[s] = c;
            }
            return c;
        }

        private static readonly Dictionary<(GUIStyle, bool), GUIStyle> LeftCache = new Dictionary<(GUIStyle, bool), GUIStyle>();

        internal static GUIStyle Left(GUIStyle s, bool wrap)
        {
            if (!LeftCache.TryGetValue((s, wrap), out GUIStyle c))
            {
                c = new GUIStyle(s) { alignment = wrap ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft, wordWrap = wrap, clipping = TextClipping.Clip };
                LeftCache[(s, wrap)] = c;
            }
            return c;
        }

        private static GUIStyle _cardText;

        private static GUIStyle CardText => _cardText ?? (_cardText = new GUIStyle(HudTheme.Note) { alignment = TextAnchor.UpperCenter, wordWrap = true, fontSize = Math.Max(10, HudTheme.Note.fontSize - 1), clipping = TextClipping.Clip });
    }
}
