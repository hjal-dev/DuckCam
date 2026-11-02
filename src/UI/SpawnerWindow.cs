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
    internal sealed partial class SpawnerWindow
    {
        private readonly DuckCamController _c;
        private readonly BotSpawnService _service;

        private List<SpawnRole> _roles;
        private int _roleIndex;
        private BotDraft _draft;
        private bool _generating;
        private int _generation;
        private readonly Dictionary<EquipmentSlot, string> _choices = new Dictionary<EquipmentSlot, string>();
        private EquipmentSlot _slot = EquipmentSlot.FirstPrimaryWeapon;
        private EFT.EBodyModelPart? _outfitPart;
        private int _outfitScope;
        private string _modPath;
        private readonly Dictionary<string, List<OutfitOption>> _outfitCache = new Dictionary<string, List<OutfitOption>>();
        private static readonly EFT.EBodyModelPart[] OutfitParts = { EFT.EBodyModelPart.Head, EFT.EBodyModelPart.Body, EFT.EBodyModelPart.Feet };
        private static readonly string[] OutfitPartNames = { "Head", "Upper Body", "Lower Body" };
        private string _search = "";
        private float _scroll;
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
            _overlay = false;
            _outfitPart = null;
            _tab = WeaponSlots.Contains(_slot) ? WardrobeTab.Weapons : WardrobeTab.Gear;
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
            _stage.Clear();
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
            _stage.Clear();
            _target = actor;
            _targetEq = null;
            _picking = false;
            _scroll = 0f;
            _previewYaw = 180f;
            ItemCatalog.Invalidate();
            _slotIcons.Clear();
            _modPath = null;
            _outfitCache.Clear();
            BeginLook(actor);
            MarkStageDirty();
            _outfitPart = EFT.EBodyModelPart.Body;
            _message = $"Editing {actor.Name}. Pick clothes or gear on the left or with the tabs, then an item.";
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
            _changes++;
            MarkStageDirty();
            _targetEq = null;
            _slotIcons.Clear();
            _message = _target.ReloadError != null
                ? $"{GearCatalog.SlotLabel(slot)}: {_target.ReloadError}"
                : $"{GearCatalog.SlotLabel(slot)} {(item == null ? "emptied" : "set")}.";
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
            MarkStageDirty();
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

        private readonly WardrobeStage _stage = new WardrobeStage();
        private float _stageDirtyAt = -1f;

        private void MarkStageDirty()
        {
            _stageDirtyAt = Time.unscaledTime + 0.25f;
        }

        private void RefreshStage()
        {
            PlayerVisualRepresentation visual = null;
            try
            {
                visual = TargetMode
                    ? (_target.Descriptor != null ? new PlayerVisualRepresentation(_target.Descriptor) : null)
                    : _draft?.Profile?.GetVisualEquipmentState(true);
            }
            catch (Exception ex)
            {
                PhotoLog.Warn("Wardrobe preview: the look could not be read: " + ex.Message);
            }
            if (visual == null)
            {
                return;
            }
            try
            {
                visual = new PlayerVisualRepresentation(visual.Info, visual.Customization, visual.Equipment.CloneItem());
            }
            catch (Exception ex)
            {
                PhotoLog.Warn("Wardrobe preview: the look could not be copied: " + ex.Message);
                return;
            }
            Player pmc = _c.Player;
            Vector3 basePos = pmc != null ? pmc.Transform.position : Vector3.zero;
            _stage.Show(visual, basePos + Vector3.down * 640f);
        }

        public void LateTick()
        {
            if (!Open || _picking)
            {
                return;
            }
            if (_stageDirtyAt > 0f && Time.unscaledTime >= _stageDirtyAt)
            {
                _stageDirtyAt = -1f;
                RefreshStage();
            }
            Transform subjectRoot;
            int subjectLayer;
            bool staged = _stage.Ready;
            if (staged)
            {
                _preview?.Tick();
                subjectRoot = _stage.Model;
                subjectLayer = WardrobeStage.Layer;
            }
            else if (TargetMode)
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
                _previewRt = new RenderTexture(1080, 1620, 24, RenderTextureFormat.ARGB32) { name = "DuckCam_SpawnerPreview" };
                _previewRt.Create();
            }
            if (_previewCam == null)
            {
                _previewCam = new GameObject("DuckCam_SpawnerPreviewCamera");
                var cam = _previewCam.AddComponent<UnityEngine.Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.035f, 0.035f, 0.04f, 1f);
                cam.fieldOfView = 30f;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 12f;
                _previewLights = new[]
                {
                    StudioLight("Key", new Vector3(28f, 32f, 0f), 1.35f, new Color(1f, 0.95f, 0.88f)),
                    StudioLight("Fill", new Vector3(12f, -48f, 0f), 0.5f, new Color(0.8f, 0.87f, 1f)),
                    StudioLight("Rim", new Vector3(18f, 165f, 0f), 1.1f, Color.white)
                };
            }
            var c = _previewCam.GetComponent<UnityEngine.Camera>();
            c.cullingMask = 1 << subjectLayer;
            c.targetTexture = _previewRt;
            Transform root = subjectRoot;
            float close = Mathf.InverseLerp(4.2f, 1.1f, _previewDist);
            Vector3 focus = root.position + Vector3.up * Mathf.Lerp(0.95f, 1.55f, close);
            Vector3 front = Vector3.forward;
            if (TargetMode || staged)
            {
                Vector3 facing = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
                front = Quaternion.AngleAxis(_previewYaw - 180f, Vector3.up) * (facing.sqrMagnitude > 0.01f ? facing : Vector3.forward);
            }
            Vector3 eye = focus + front * _previewDist + Vector3.up * 0.1f;
            _previewCam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye, Vector3.up));
            _renderLayer = subjectLayer;
            _renderPending = true;
        }

        private int _renderLayer;
        private bool _renderPending;
        private int _renderedFrame = -1;

        private void RenderPreview()
        {
            if (!_renderPending || _renderedFrame == Time.frameCount || _previewCam == null)
            {
                return;
            }
            _renderedFrame = Time.frameCount;
            if (_stage.Ready)
            {
                _stage.Tick(Time.unscaledDeltaTime, Time.deltaTime <= 0f);
            }
            var c = _previewCam.GetComponent<UnityEngine.Camera>();
            UnityEngine.Rendering.AmbientMode ambientMode = RenderSettings.ambientMode;
            Color ambientLight = RenderSettings.ambientLight;
            float ambientIntensity = RenderSettings.ambientIntensity;
            try
            {
                foreach (Light l in _previewLights)
                {
                    l.cullingMask = 1 << _renderLayer;
                    l.enabled = true;
                }
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.32f, 0.32f, 0.34f);
                RenderSettings.ambientIntensity = 1f;
                c.Render();
            }
            catch (Exception ex)
            {
                PhotoLog.WarnOnce("spawnerpreview", "Spawner preview render failed: " + ex.Message);
            }
            finally
            {
                foreach (Light l in _previewLights)
                {
                    l.enabled = false;
                }
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambientLight;
                RenderSettings.ambientIntensity = ambientIntensity;
            }
        }

        private Light[] _previewLights = new Light[0];

        private Light StudioLight(string name, Vector3 euler, float intensity, Color color)
        {
            var light = new GameObject("DuckCam_SpawnerPreview" + name).AddComponent<Light>();
            light.transform.SetParent(_previewCam.transform, false);
            light.transform.localRotation = Quaternion.Euler(euler);
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.shadows = LightShadows.None;
            light.enabled = false;
            return light;
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

        private readonly PackFilter _kitPacks = new PackFilter();

        private static string KitKey(BotKit kit) => kit.Pack == null ? kit.Name : kit.Pack + "/" + kit.Name;

        private void DrawKits(Rect r)
        {
            HudTheme.Fill(r, HudTheme.P.RowFill);
            GUI.Label(new Rect(r.x + 8f, r.y + 6f, 120f, 22f), "KIT NAME", HudTheme.HudSmall);
            GUI.SetNextControlName(HudKit.FieldPrefix + "SpawnerKitName");
            _kitPacks.Options(_kits?.Select(k => KitKey(k)).ToArray() ?? new string[0], BotKits.Dir);
            float packW = _kitPacks.Active ? 150f : 0f;
            _kitName = GUI.TextField(new Rect(r.x + 100f, r.y + 4f, r.width - 250f - packW, 26f), _kitName ?? "", 40, HudTheme.Field);
            if (_kitPacks.Active && Button(new Rect(r.xMax - 142f - packW, r.y + 3f, packW - 6f, 28f), "Pack: " + _kitPacks.Label, false))
            {
                _kitPacks.Cycle(_kits.Select(k => KitKey(k)).ToArray(), BotKits.Dir);
                _message = _kitPacks.Info(BotKits.Dir) ?? _message;
                _scroll = 0f;
            }
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
            foreach (BotKit kit in _kits.Where(k => _kitPacks.Apply(new[] { KitKey(k) }).Length > 0).ToList())
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
                GUI.Label(new Rect(row.x + 42f, row.y + 2f, row.width - 360f, 20f), _kitPacks.Show(KitKey(kit)), HudTheme.Value);
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
            if (_overlay && !_picking)
            {
                _overlay = false;
                return;
            }
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
            kit.Pack = existing?.Pack;
            kit.SourceFile = existing?.SourceFile;
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

        private List<(string Path, string Label, Slot Slot)> ModSlots(Item root)
        {
            var into = new List<(string Path, string Label, Slot Slot)>();
            WalkMods(root, "", "", into, 0);
            return into;
        }

        private static void WalkMods(Item item, string path, string label, List<(string Path, string Label, Slot Slot)> into, int depth)
        {
            if (depth > 3 || !(item is CompoundItem compound) || compound.Slots == null)
            {
                return;
            }
            for (int i = 0; i < compound.Slots.Length; i++)
            {
                Slot s = compound.Slots[i];
                if (s?.Filters == null || s.Filters.Length == 0 || IsArmorInsert(s.ID))
                {
                    continue;
                }
                string p = path + i;
                string l = (label.Length > 0 ? label + " > " : "") + Pretty(s.ID);
                into.Add((p, l, s));
                if (s.ContainedItem != null)
                {
                    WalkMods(s.ContainedItem, p + "/", l, into, depth + 1);
                }
            }
        }

        private static bool IsArmorInsert(string id)
        {
            string s = (id ?? "").ToLowerInvariant();
            return s.Contains("soft_armor") || s.Contains("plate") || s.StartsWith("helmet_", StringComparison.Ordinal) || s == "collar" || s.StartsWith("groin", StringComparison.Ordinal);
        }

        private static string Pretty(string id)
        {
            string s = (id ?? "slot").Replace("mod_", "").Replace('_', ' ').Trim();
            s = s.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').Trim();
            if (s.Length == 0)
            {
                return "Slot";
            }
            if (s.Equals("nvg", StringComparison.OrdinalIgnoreCase))
            {
                return "NVG / Mount";
            }
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
        }

        private void AttachMod(string path, CatalogEntry entry)
        {
            InventoryEquipment eq = Equipment;
            Item root = eq?.GetSlot(_slot)?.ContainedItem;
            if (root == null)
            {
                _message = "Put an item in this slot first.";
                return;
            }
            (string Path, string Label, Slot Slot) target = ModSlots(root).FirstOrDefault(m => m.Path == path);
            if (target.Slot == null)
            {
                _message = "That attachment slot is gone; pick it again.";
                _modPath = null;
                return;
            }
            Item mod = null;
            if (entry != null)
            {
                mod = ItemCatalog.CreateItem(entry);
                if (mod == null)
                {
                    _message = "That item couldn't be created.";
                    return;
                }
            }
            if (target.Slot.ContainedItem != null)
            {
                target.Slot.RemoveItemWithoutRestrictions();
            }
            if (mod != null)
            {
                var added = target.Slot.AddWithoutRestrictions(mod);
                if (added.Failed)
                {
                    _message = $"{target.Label}: could not fit it ({added.Error}).";
                    _targetEq = null;
                    return;
                }
            }
            _target.ChangeGear(_slot, root);
            _changes++;
            MarkStageDirty();
            _targetEq = null;
            _slotIcons.Clear();
            _message = _target.ReloadError != null
                ? $"{target.Label}: {_target.ReloadError}"
                : $"{target.Label} {(mod == null ? "emptied" : "attached")}.";
        }

        private List<OutfitOption> OutfitOptions(EFT.EBodyModelPart part)
        {
            EFT.EPlayerSide side = _target.Descriptor?.Info != null ? _target.Descriptor.Info.Side : EFT.EPlayerSide.Usec;
            string key = $"{part}|{side}|{_outfitScope}";
            if (!_outfitCache.TryGetValue(key, out List<OutfitOption> list))
            {
                var scope = (OutfitScope)_outfitScope;
                list = part == EFT.EBodyModelPart.Head ? OutfitCatalog.Heads(side, scope)
                    : part == EFT.EBodyModelPart.Body ? OutfitCatalog.Tops(side, scope)
                    : OutfitCatalog.Lowers(side, scope);
                _outfitCache[key] = list;
            }
            return list;
        }

        private string OutfitName(EFT.EBodyModelPart part)
        {
            Dictionary<EFT.EBodyModelPart, MongoID> customization = _target.Descriptor?.Customization;
            if (customization == null || !customization.TryGetValue(part, out MongoID current))
            {
                return "(none)";
            }
            string id = current.ToString();
            OutfitOption hit = OutfitOptions(part).FirstOrDefault(o => o.MainId == id);
            return hit != null ? hit.Name : OutfitCatalog.Current(part, current).Name;
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
