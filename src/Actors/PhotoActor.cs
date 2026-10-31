using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using DuckCam.Animation;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Actors
{
    internal enum ActorStatus
    {
        None,
        Loading,
        Ready,
        Failed
    }

    internal sealed class PhotoActor
    {
        private const float LoadTimeoutSeconds = 30f;

        private PlayerModelLoader _loader;
        private CancellationTokenSource _cts;
        private RestoreHandle _handle;
        private float _loadStarted;
        private readonly List<(Renderer renderer, bool forcedOff)> _hiddenSource = new List<(Renderer, bool)>();
        private GameObject _weaponObject;
        private bool _stanceWeapon;
        private Dictionary<string, float[]> _stanceFull;
        private readonly List<Behaviour> _stanceAnimators = new List<Behaviour>();
        private readonly List<(Transform t, Vector3 p, Quaternion r)> _weaponRest = new List<(Transform, Vector3, Quaternion)>();
        private Transform _head;
        private bool _visible = true;

        public string Name { get; set; }
        public string Info { get; set; }
        public bool IsPmc { get; set; }
        public string SourceProfileId { get; set; }
        public PlayerVisualRepresentationDescriptor Descriptor { get; private set; }

        public Dictionary<EBodyModelPart, MongoID> OriginalCustomization { get; private set; }

        public bool Reloading { get; private set; }
        public string ReloadError { get; private set; }

        private AnimatorOverrideController _override;
        private PlayerModelLoader _pendingLoader;

        public ActorStatus Status { get; private set; }
        public string Error { get; private set; }
        public Animator Animator { get; private set; }
        public GearRackPose CurrentPose { get; private set; }
        public float NormalizedTime { get; private set; }
        public bool Animate { get; private set; }
        public bool WeaponVisible { get; private set; }
        public bool LookAtCamera
        {
            get => Pose.Look == LookMode.Camera;
            set => Pose.Look = value ? LookMode.Camera : (Pose.Look == LookMode.Camera ? LookMode.Off : Pose.Look);
        }

        public string PendingPose { get; set; }
        public float PendingPoseTime { get; set; }
        public bool PendingWeapon { get; set; }

        public StanceSnapshot Stance { get; set; }

        public bool InStance => _stanceFull != null && PoseEditor.State.FullPose == _stanceFull;

        public Vector3 SpawnPosition { get; set; }
        public Quaternion SpawnRotation { get; set; } = Quaternion.identity;
        public float Yaw { get; set; }
        public float OffsetRight { get; set; }
        public float OffsetForward { get; set; }
        public float OffsetUp { get; set; }

        public Transform Root => _loader?.ModelPlayerPoser != null ? _loader.ModelPlayerPoser.transform : null;
        public Transform Head => Rig?.Head ?? _head;
        public int Layer { get; private set; }

        public bool Visible
        {
            get => _visible;
            set
            {
                if (_visible == value)
                {
                    return;
                }
                _visible = value;
                ApplyVisibility();
            }
        }

        public void Begin(PlayerVisualRepresentation visual, int layer, Player hideSource, RestoreStack stack)
        {
            if (Status == ActorStatus.Loading || Status == ActorStatus.Ready)
            {
                return;
            }
            if (!GearRackPoseLibrary.EnsureLoaded())
            {
                Fail("Gear Rack data unavailable: " + GearRackPoseLibrary.Error);
                return;
            }

            try
            {
                Descriptor = new PlayerVisualRepresentationDescriptor(visual);
                if (Descriptor.Customization != null)
                {
                    OriginalCustomization = new Dictionary<EBodyModelPart, MongoID>(Descriptor.Customization);
                }
                if (Descriptor.Equipment != null)
                {
                    OriginalEquipment = new EFT.InventoryEquipmentDescriptor(Descriptor.Equipment.ToEquipment());
                }
            }
            catch (Exception ex)
            {
                PhotoLog.Debug("Descriptor snapshot failed (actor cannot be saved): " + ex.Message);
            }

            Layer = layer;
            Status = ActorStatus.Loading;
            Error = null;
            CurrentPose = null;
            _loadStarted = Time.unscaledTime;
            _cts = new CancellationTokenSource();
            _handle = stack.Push("photo actor " + Name, Teardown);
            _ = LoadAsync(visual, hideSource, _cts.Token);
        }

        public void Remove()
        {
            _handle?.Run();
            _handle = null;
        }

        public void Tick()
        {
            if (Reloading && Time.unscaledTime - _loadStarted > LoadTimeoutSeconds)
            {
                _cts?.Cancel();
                _pendingLoader?.Clear();
                _pendingLoader = null;
                Reloading = false;
                ReloadError = $"Outfit load timed out after {LoadTimeoutSeconds:F0}s.";
            }
            if (Status == ActorStatus.Loading && Time.unscaledTime - _loadStarted > LoadTimeoutSeconds)
            {
                _cts?.Cancel();
                Fail($"Model load timed out after {LoadTimeoutSeconds:F0}s (a loader step may wait on scaled time while frozen).");
                Remove();
            }
        }

        private async Task LoadAsync(PlayerVisualRepresentation visual, Player hideSource, CancellationToken ct)
        {
            var loader = new PlayerModelLoader();
            _loader = loader;
            try
            {
                await loader.Load(visual, null, 0f, ThirdPersonCustomizationFilter.Default, null, ct, SpawnPosition, null, Layer, false, null, false);
                if (ct.IsCancellationRequested || _loader != loader)
                {
                    loader.Clear();
                    return;
                }
                if (loader.ModelPlayerPoser == null)
                {
                    throw new InvalidOperationException("PlayerModelLoader produced no MenuPlayerPoser");
                }
                Setup(loader, hideSource);
                Status = ActorStatus.Ready;
                PhotoLog.Verbose($"Photo actor '{Name}' ready.");
            }
            catch (Exception ex)
            {
                Fail(ex.GetType().Name + ": " + ex.Message);
                PhotoLog.Error($"Photo actor '{Name}' load failed: {ex}");
                Remove();
            }
        }

        private void Setup(PlayerModelLoader loader, Player hideSource)
        {
            MenuPlayerPoser poser = loader.ModelPlayerPoser;
            poser.enabled = false;
            _weaponObject = loader._weaponPrefab != null ? loader._weaponPrefab.gameObject : null;
            CaptureWeaponRest();
            CaptureFingers();
            SetWeaponVisible(false);

            Animator = poser.PlayerAnimatorController != null ? poser.PlayerAnimatorController : poser.GetComponent<Animator>();
            if (Animator == null)
            {
                throw new InvalidOperationException("actor has no Animator");
            }
            Animator.runtimeAnimatorController = GearRackPoseLibrary.Controller;
            _probeBones = null;
            _probeDefault = null;
            Animator.applyRootMotion = false;
            Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Animator.updateMode = AnimatorUpdateMode.Normal;
            Animator.enabled = true;
            GearRackPoseLibrary.Validate(Animator);

            _head = poser.PBones != null && poser.PBones.Head != null ? poser.PBones.Head.Original : null;
            Rig = new ActorRig(poser, Root);
            Rig.OnBase = OnRigBase;
            _gripValid = false;
            _following = false;
            ApplyTransform();
            ApplyFirstPose();
            CaptureStanding();
            ApplyVisibility();
            SyncTogglableGear();
            if (Stance != null && _stanceFull == null)
            {
                ApplyStance();
            }
            else if (_stanceWeapon)
            {
                _stanceAnimators.Clear();
                PlaceStanceWeapon(true);
            }


            if (hideSource != null)
            {
                var seen = new HashSet<Renderer>();
                foreach (Renderer r in hideSource.GetComponentsInChildren<Renderer>(true))
                {
                    if (seen.Add(r))
                    {
                        _hiddenSource.Add((r, r.forceRenderingOff));
                        r.forceRenderingOff = true;
                    }
                }
                GameObject held = null;
                try
                {
                    held = hideSource.HandsController?.ControllerGameObject;
                }
                catch (Exception ex)
                {
                    PhotoLog.Debug($"Photo actor '{Name}': source hands not readable: {ex.Message}");
                }
                if (held != null)
                {
                    int extra = 0;
                    foreach (Renderer r in held.GetComponentsInChildren<Renderer>(true))
                    {
                        if (seen.Add(r))
                        {
                            _hiddenSource.Add((r, r.forceRenderingOff));
                            r.forceRenderingOff = true;
                            extra++;
                        }
                    }
                }
            }
        }

        public GameObject InHandsWeapon => _weaponObject;

        private static readonly System.Reflection.FieldInfo ItemInHandsField = HarmonyLib.AccessTools.Field(typeof(PlayerBody), "_itemInHands");

        public EquipmentSlot? HeldSlot { get; private set; }

        public bool HasSidearm => SlotItem(EquipmentSlot.Holster) != null;

        public bool HasKnife => SlotItem(EquipmentSlot.Scabbard) != null;

        private Item SlotItem(EquipmentSlot slot)
        {
            try
            {
                return _loader?.PlayerBody?.Equipment?.GetSlot(slot)?.ContainedItem;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private Item DefaultHandItem()
        {
            foreach (EquipmentSlot s in new[] { EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster, EquipmentSlot.Scabbard })
            {
                Item item = SlotItem(s);
                if (item != null)
                {
                    return item;
                }
            }
            return null;
        }

        private GameObject _offhand;
        private int _offhandLateral;

        public bool DualWield { get; private set; }

        public bool CanDualWield => HeldSlot == EquipmentSlot.Holster && HasSidearm;

        public void SetDualWield(bool on)
        {
            ReleaseOffhand();
            DualWield = on;
            _gripValid = false;
            if (!on || !CanDualWield)
            {
                return;
            }
            Item item = SlotItem(EquipmentSlot.Holster);
            Transform chest = _loader?.ModelPlayerPoser?.PBones?.Ribcage?.Original;
            if (item == null || chest == null)
            {
                return;
            }
            try
            {
                WeaponPrefab prefab = Comfort.Common.Singleton<EFT.ObjectsFactory>.Instance.CreateItem(item, true).GetComponent<WeaponPrefab>();
                if (prefab == null)
                {
                    return;
                }
                GameObject go = prefab.gameObject;
                go.transform.SetParent(chest, false);
                go.SetActive(true);
                prefab.Init(null, true);
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.layer = Layer;
                }
                foreach (Animator an in go.GetComponentsInChildren<Animator>(true))
                {
                    an.enabled = false;
                }
                _offhand = go;
                _offhandLateral = ThinnestAxis(go.transform);
            }
            catch (Exception ex)
            {
                PhotoLog.Warn($"Photo actor '{Name}': second pistol failed: {ex.Message}");
                ReleaseOffhand();
            }
        }

        private void ReleaseOffhand()
        {
            if (_offhand == null)
            {
                return;
            }
            try
            {
                foreach (Animator an in _offhand.GetComponentsInChildren<Animator>(true))
                {
                    an.enabled = true;
                }
                EFT.AssetsManager.AssetPoolObject.ReturnToPool(_offhand);
            }
            catch (Exception ex)
            {
                PhotoLog.Warn($"Photo actor '{Name}': second pistol release failed: {ex.Message}");
            }
            _offhand = null;
        }

        private static int ThinnestAxis(Transform root)
        {
            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null)
                {
                    continue;
                }
                Bounds b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = root.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
            }
            Vector3 size = max - min;
            if (float.IsInfinity(size.x))
            {
                return 0;
            }
            return size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
        }

        private Quaternion? HandFrame(string side)
        {
            Transform palm = Rig?.Skeleton?.Find("Base Human" + side + "Palm")?.Bone;
            Transform middle = Rig?.Skeleton?.Find("Base Human" + side + "Digit31")?.Bone;
            Transform index = Rig?.Skeleton?.Find("Base Human" + side + "Digit21")?.Bone;
            Transform pinky = Rig?.Skeleton?.Find("Base Human" + side + "Digit51")?.Bone;
            if (palm == null || middle == null || index == null || pinky == null)
            {
                return null;
            }
            Vector3 f = middle.position - palm.position;
            Vector3 s = side == "R" ? pinky.position - index.position : index.position - pinky.position;
            Vector3 u = Vector3.Cross(f, s);
            if (f.sqrMagnitude < 1e-8f || u.sqrMagnitude < 1e-8f)
            {
                return null;
            }
            return Quaternion.LookRotation(f.normalized, u.normalized);
        }

        private void PlaceOffhand()
        {
            Transform rightPalm = GripPalm;
            Transform leftPalm = Rig?.Skeleton?.Find("Base HumanLPalm")?.Bone;
            Quaternion? fr = HandFrame("R");
            Quaternion? fl = HandFrame("L");
            if (rightPalm == null || leftPalm == null || fr == null || fl == null)
            {
                return;
            }
            Transform main = _weaponObject.transform;
            Quaternion inv = Quaternion.Inverse(fr.Value);
            Quaternion rel = inv * main.rotation;
            Vector3 relPos = inv * (main.position - rightPalm.position);
            Quaternion mirrored = new Quaternion(rel.x, -rel.y, -rel.z, rel.w);
            Quaternion flip = _offhandLateral == 1 ? Quaternion.Euler(0f, 0f, 180f) : _offhandLateral == 2 ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            Vector3 pos = leftPalm.position + fl.Value * new Vector3(-relPos.x, relPos.y, relPos.z);
            _offhand.transform.SetPositionAndRotation(pos, fl.Value * mirrored * flip);
        }

        public bool SetHeldSlot(EquipmentSlot? slot)
        {
            if (Status != ActorStatus.Ready || _loader?.ModelPlayerPoser == null)
            {
                return false;
            }
            Item item = slot.HasValue ? SlotItem(slot.Value) : DefaultHandItem();
            if (item == null)
            {
                return false;
            }
            Diz.Binding.BindableState<Item> inHands = _loader.PlayerBody != null ? ItemInHandsField?.GetValue(_loader.PlayerBody) as Diz.Binding.BindableState<Item> : null;
            Item current = inHands?.Value;
            if (current != null && current.Id == item.Id && _weaponObject != null)
            {
                HeldSlot = slot;
                return true;
            }
            ReleaseOffhand();
            try
            {
                ReleaseWeapon();
                if (_loader._weaponPrefab != null)
                {
                    EFT.AssetsManager.AssetPoolObject.ReturnToPool(_loader._weaponPrefab.gameObject);
                    _loader._weaponPrefab = null;
                }
                _loader.CreateWeapon(0f, false, item, Layer);
                _weaponObject = _loader._weaponPrefab != null ? _loader._weaponPrefab.gameObject : null;
                CaptureWeaponRest();
                if (inHands != null)
                {
                    inHands.Value = item;
                }
                HeldSlot = slot;
                _gripValid = false;
                SetWeaponVisible(true);
                if (DualWield)
                {
                    SetDualWield(true);
                }
                return _weaponObject != null;
            }
            catch (Exception ex)
            {
                PhotoLog.Warn($"Photo actor '{Name}': weapon swap failed: {ex.Message}");
                return false;
            }
        }

        private static readonly System.Reflection.FieldInfo NvgMountItem = HarmonyLib.AccessTools.Field(typeof(EFT.Visual.NightVisionMount), "_mount");
        private static readonly System.Reflection.FieldInfo HelmetItem = HarmonyLib.AccessTools.Field(typeof(EFT.Visual.HelmetDress), "_helmet");

        private void SyncTogglableGear()
        {
            Transform root = Root;
            if (root == null)
            {
                return;
            }
            try
            {
                foreach (EFT.Visual.NightVisionMount m in root.GetComponentsInChildren<EFT.Visual.NightVisionMount>(true))
                {
                    if (NvgMountItem.GetValue(m) == null)
                    {
                        continue;
                    }
                    m.SetNightVision(null, true);
                    m.UpdateState(true);
                }
                foreach (EFT.Visual.HelmetDress h in root.GetComponentsInChildren<EFT.Visual.HelmetDress>(true))
                {
                    if (HelmetItem.GetValue(h) == null)
                    {
                        continue;
                    }
                    h.SetFaceShield(null, true);
                    h.UpdateState(true);
                }
            }
            catch (Exception ex)
            {
                PhotoLog.WarnOnce("togglable-sync", $"Photo actor '{Name}': NVG/face shield sync failed: {ex.Message}");
            }
        }

        private readonly Dictionary<Transform, Quaternion> _standingLocal = new Dictionary<Transform, Quaternion>();

        public IReadOnlyDictionary<Transform, Quaternion> StandingLocal => _standingLocal.Count > 0 ? _standingLocal : null;

        public Vector3? StandingHips { get; private set; }

        private void CaptureStanding()
        {
            _standingLocal.Clear();
            StandingHips = null;
            if (Rig?.Skeleton == null)
            {
                return;
            }
            foreach (SkelBone b in Rig.Skeleton.Map)
            {
                if (b.Bone != null)
                {
                    _standingLocal[b.Bone] = b.Bone.localRotation;
                }
            }
            if (Rig.Skeleton.Hips != null)
            {
                StandingHips = Rig.Skeleton.Hips.Bone.localPosition;
            }
        }

        private void ApplyFirstPose()
        {
            foreach (GearRackPose p in GearRackPoseLibrary.Poses)
            {
                if (p.PoseName == "standing" && p.StateExists != false)
                {
                    ApplyPose(p, 0f, out _);
                    return;
                }
            }
            CaptureHeadBase();
        }

        public void ChangeCustomization(Dictionary<EBodyModelPart, MongoID> customization)
        {
            if (Animator == null || Descriptor == null || _loader == null)
            {
                ReloadError = "character is not ready";
                return;
            }
            Descriptor.Customization = new Dictionary<EBodyModelPart, MongoID>(customization);
            PlayerVisualRepresentation visual;
            try
            {
                visual = new PlayerVisualRepresentation(Descriptor);
            }
            catch (Exception ex)
            {
                ReloadError = ex.Message;
                return;
            }
            StartReload(visual);
        }

        public EFT.InventoryEquipmentDescriptor OriginalEquipment { get; private set; }

        public EFT.InventoryLogic.InventoryEquipment CurrentEquipment()
        {
            try
            {
                return Descriptor?.Equipment?.ToEquipment();
            }
            catch (Exception ex)
            {
                PhotoLog.Debug($"Photo actor '{Name}': equipment unavailable: {ex.Message}");
                return null;
            }
        }

        public void ChangeGear(EFT.InventoryLogic.EquipmentSlot slot, EFT.InventoryLogic.Item donor)
        {
            if (Animator == null || Descriptor == null || _loader == null)
            {
                ReloadError = "character is not ready";
                return;
            }
            try
            {
                EFT.InventoryLogic.InventoryEquipment equipment = Descriptor.Equipment != null
                    ? Descriptor.Equipment.ToEquipment()
                    : null;
                EFT.InventoryLogic.Slot target = equipment?.GetSlot(slot);
                if (target == null)
                {
                    ReloadError = "this character has no " + slot + " slot";
                    return;
                }
                if (target.ContainedItem != null)
                {
                    target.RemoveItemWithoutRestrictions();
                }
                if (donor != null)
                {
                    var added = target.AddWithoutRestrictions(donor.CloneVisibleItem());
                    if (added.Failed)
                    {
                        ReloadError = "could not fit the item: " + added.Error;
                        return;
                    }
                }
                Descriptor.Equipment = new EFT.InventoryEquipmentDescriptor(equipment);
                StartReload(new PlayerVisualRepresentation(Descriptor));
            }
            catch (Exception ex)
            {
                ReloadError = ex.Message;
                PhotoLog.Warn($"Photo actor '{Name}': gear change failed: {ex}");
            }
        }

        public void ResetGear()
        {
            if (OriginalEquipment == null || Descriptor == null)
            {
                return;
            }
            Descriptor.Equipment = OriginalEquipment;
            try
            {
                StartReload(new PlayerVisualRepresentation(Descriptor));
            }
            catch (Exception ex)
            {
                ReloadError = ex.Message;
            }
        }

        public void ReloadVisual(PlayerVisualRepresentation visual)
        {
            if (Status != ActorStatus.Ready || visual == null)
            {
                return;
            }
            try
            {
                Descriptor = new PlayerVisualRepresentationDescriptor(visual);
                StartReload(visual);
            }
            catch (Exception ex)
            {
                ReloadError = ex.Message;
                PhotoLog.Warn($"Photo actor '{Name}': appearance reload failed: {ex.Message}");
            }
        }

        private void StartReload(PlayerVisualRepresentation visual)
        {
            _cts?.Cancel();
            _pendingLoader?.Clear();
            _pendingLoader = null;
            _cts = new CancellationTokenSource();
            Reloading = true;
            ReloadError = null;
            _loadStarted = Time.unscaledTime;
            _ = ReloadAsync(visual, _cts.Token);
        }

        private async Task ReloadAsync(PlayerVisualRepresentation visual, CancellationToken ct)
        {
            var loader = new PlayerModelLoader();
            _pendingLoader = loader;
            try
            {
                await loader.Load(visual, null, 0f, ThirdPersonCustomizationFilter.Default, null, ct, SpawnPosition, null, Layer, false, null, false);
                if (ct.IsCancellationRequested || _pendingLoader != loader || _loader == null)
                {
                    loader.Clear();
                    return;
                }
                if (loader.ModelPlayerPoser == null)
                {
                    throw new InvalidOperationException("PlayerModelLoader produced no MenuPlayerPoser");
                }

                GearRackPose pose = CurrentPose;
                float time = NormalizedTime;
                bool weapon = WeaponVisible;
                bool animate = Animate;

                PlayerModelLoader old = _loader;
                _loader = loader;
                _pendingLoader = null;
                bool hadStanceWeapon = _stanceWeapon;
                string oldWeapon = _weaponObject != null ? _weaponObject.name : null;
                float[] oldGrip = GripData;
                ReleaseOffhand();
                ReleaseWeapon();
                _stanceWeapon = hadStanceWeapon;
                old.Clear();
                if (_override != null)
                {
                    UnityEngine.Object.Destroy(_override);
                    _override = null;
                }

                Setup(loader, null);
                if (pose != null)
                {
                    ApplyPose(pose, time, out _);
                }
                SetWeaponVisible(weapon);
                SetAnimate(animate);
                ApplyVisibility();
                if (HeldSlot.HasValue)
                {
                    EquipmentSlot keep = HeldSlot.Value;
                    SetHeldSlot(keep);
                    SetWeaponVisible(weapon);
                }
                if (oldGrip != null && _weaponObject != null && SameWeapon(_weaponObject.name, oldWeapon))
                {
                    _gripPos = new Vector3(oldGrip[0], oldGrip[1], oldGrip[2]);
                    _gripRot = new Quaternion(oldGrip[3], oldGrip[4], oldGrip[5], oldGrip[6]);
                    _gripValid = true;
                }
                PhotoLog.Info($"Photo actor '{Name}' outfit changed.");
            }
            catch (Exception ex)
            {
                if (_pendingLoader == loader)
                {
                    _pendingLoader = null;
                }
                loader.Clear();
                if (!ct.IsCancellationRequested)
                {
                    ReloadError = ex.GetType().Name + ": " + ex.Message;
                    PhotoLog.Error($"Photo actor '{Name}' outfit change failed: {ex}");
                }
            }
            finally
            {
                if (!ct.IsCancellationRequested)
                {
                    Reloading = false;
                }
            }
        }

        private const float ProbeAngleDegrees = 4f;
        private Transform[] _probeBones;
        private Quaternion[] _probeDefault;
        private AnimationClip _emptyClip;

        public BodyCoverage ProbeClip(AnimationClip clip)
        {
            if (clip == null || Animator == null || Root == null)
            {
                return BodyCoverage.None;
            }
            try
            {
                if (_probeBones == null)
                {
                    _probeBones = Root.GetComponentsInChildren<Transform>(true)
                        .Where(t => t.name.StartsWith("Base Human", StringComparison.Ordinal))
                        .ToArray();
                    if (_emptyClip == null)
                    {
                        _emptyClip = new AnimationClip { name = "DuckCam_Empty" };
                    }
                    PlayOverride(_emptyClip, 0f);
                    _probeDefault = _probeBones.Select(b => b.localRotation).ToArray();
                }
                if (_probeBones.Length == 0)
                {
                    return BodyCoverage.None;
                }

                PlayOverride(_emptyClip, 0f);
                PlayOverride(clip, 0.5f);
                bool legs = false;
                bool torso = false;
                bool arms = false;
                for (int i = 0; i < _probeBones.Length; i++)
                {
                    if (Quaternion.Angle(_probeBones[i].localRotation, _probeDefault[i]) < ProbeAngleDegrees)
                    {
                        continue;
                    }
                    string n = _probeBones[i].name;
                    if (n.Contains("Thigh") || n.Contains("Calf") || n.Contains("Foot") || n.Contains("Toe"))
                    {
                        legs = true;
                    }
                    else if (n.Contains("arm") || n.Contains("Arm") || n.Contains("Hand") || n.Contains("Palm") || n.Contains("Digit") || n.Contains("Finger") || n.Contains("Clavicle"))
                    {
                        arms = true;
                    }
                    else
                    {
                        torso = true;
                    }
                }
                if (legs && (torso || arms))
                {
                    return BodyCoverage.FullBody;
                }
                if (torso || arms)
                {
                    return BodyCoverage.UpperBody;
                }
                return legs ? BodyCoverage.LowerBody : BodyCoverage.None;
            }
            catch (Exception ex)
            {
                PhotoLog.Debug($"Pose library: clip '{clip.name}' test failed: {ex.Message}");
                return BodyCoverage.None;
            }
        }

        private void PlayOverride(AnimationClip clip, float normalizedTime)
        {
            if (_override == null)
            {
                _override = new AnimatorOverrideController(GearRackPoseLibrary.Controller) { name = "DuckCam_PoseOverride" };
            }
            _override[GearRackPoseLibrary.OverrideClipName] = clip;
            if (Animator.runtimeAnimatorController != _override)
            {
                Animator.runtimeAnimatorController = _override;
            }
            ApplyFingers();
            Animator.Play(Animator.StringToHash(GearRackPoseLibrary.OverrideClipName), 0, normalizedTime);
            Animator.Update(0f);
        }

        public void RestoreCurrentPose()
        {
            if (CurrentPose != null)
            {
                ApplyPose(CurrentPose, NormalizedTime, out _);
            }
            else
            {
                ApplyFirstPose();
            }
        }

        private void ApplyVisibility()
        {
            if (Root == null)
            {
                return;
            }
            foreach (Renderer r in Root.GetComponentsInChildren<Renderer>(true))
            {
                r.forceRenderingOff = !_visible;
            }
            foreach (EquipmentSlot slot in _hiddenSlots)
            {
                foreach (Renderer r in SlotRenderers(slot))
                {
                    r.forceRenderingOff = true;
                }
            }
        }

        private readonly HashSet<EquipmentSlot> _hiddenSlots = new HashSet<EquipmentSlot>();

        public bool IsSlotHidden(EquipmentSlot slot) => _hiddenSlots.Contains(slot);
        public IEnumerable<EquipmentSlot> HiddenSlots => _hiddenSlots;

        public void SetSlotHidden(EquipmentSlot slot, bool hidden)
        {
            if (hidden ? _hiddenSlots.Add(slot) : _hiddenSlots.Remove(slot))
            {
                ApplyVisibility();
            }
        }

        public bool SlotHasModel(EquipmentSlot slot) => SlotRenderers(slot).Any();

        private IEnumerable<Renderer> SlotRenderers(EquipmentSlot slot)
        {
            PlayerBody body = _loader?.PlayerBody;
            if (body == null || !body.SlotViews.TryGetByKey(slot, out PlayerBody.SlotView view) || view == null)
            {
                yield break;
            }
            if (view.Renderers != null)
            {
                foreach (Renderer r in view.Renderers)
                {
                    if (r != null)
                    {
                        yield return r;
                    }
                }
            }
            if (view.Model != null)
            {
                foreach (Renderer r in view.Model.GetComponentsInChildren<Renderer>(true))
                {
                    yield return r;
                }
            }
            if (view.Dresses != null)
            {
                foreach (EFT.Visual.Dress dress in view.Dresses)
                {
                    if (dress?.Renderers == null)
                    {
                        continue;
                    }
                    foreach (Renderer r in dress.Renderers)
                    {
                        if (r != null)
                        {
                            yield return r;
                        }
                    }
                }
            }
        }

        public void ApplyTransform()
        {
            Transform root = Root;
            if (root == null)
            {
                return;
            }
            Quaternion facing = SpawnRotation * Quaternion.Euler(0f, Yaw, 0f);
            Vector3 offset = SpawnRotation * new Vector3(OffsetRight, 0f, OffsetForward) + Vector3.up * OffsetUp;
            root.SetPositionAndRotation(SpawnPosition + offset, facing);
        }

        public bool ApplyPose(GearRackPose pose, float normalizedTime, out string error)
        {
            error = null;
            if (Status != ActorStatus.Ready && Animator == null)
            {
                error = "actor not ready";
                return false;
            }
            int hash = Animator.StringToHash(pose.PoseName);
            try
            {
                if (Animator.HasState(0, hash))
                {
                    if (Animator.runtimeAnimatorController != GearRackPoseLibrary.Controller)
                    {
                        Animator.runtimeAnimatorController = GearRackPoseLibrary.Controller;
                    }
                }
                else if (pose.ModClip != null)
                {
                    if (_override == null)
                    {
                        _override = new AnimatorOverrideController(GearRackPoseLibrary.Controller) { name = "DuckCam_PoseOverride" };
                    }
                    _override[GearRackPoseLibrary.OverrideClipName] = pose.ModClip;
                    Animator.runtimeAnimatorController = _override;
                    hash = Animator.StringToHash(GearRackPoseLibrary.OverrideClipName);
                }
                else
                {
                    pose.StateExists = false;
                    error = $"state '{pose.PoseName}' is not in the mannequin controller and no mod supplies its animation";
                    return false;
                }

                NormalizedTime = Mathf.Clamp01(normalizedTime);
                ApplyFingers();
                Animator.Play(hash, 0, NormalizedTime);
                Animator.Update(0f);
                CurrentPose = pose;
                _gripValid = false;
                CaptureHeadBase();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public void SetNormalizedTime(float t)
        {
            if (CurrentPose != null)
            {
                ApplyPose(CurrentPose, t, out _);
            }
        }

        public void SetAnimate(bool animate)
        {
            Animate = animate;
            _gripValid = false;
            if (Animator != null)
            {
                Animator.updateMode = animate ? AnimatorUpdateMode.UnscaledTime : AnimatorUpdateMode.Normal;
            }
            if (!animate)
            {
                CaptureHeadBase();
            }
        }

        public void SetWeaponVisible(bool visible)
        {
            WeaponVisible = visible;
            _gripValid = false;
            if (_weaponObject != null)
            {
                _weaponObject.SetActive(visible);
            }
            ApplyFingers();
            if (CurrentPose != null && Animator != null)
            {
                ApplyPose(CurrentPose, NormalizedTime, out _);
            }
        }

        private readonly List<(Transform bone, Quaternion rest, Quaternion grip)> _fingers = new List<(Transform, Quaternion, Quaternion)>();

        private void CaptureFingers()
        {
            _fingers.Clear();
            Transform root = Root;
            if (root == null)
            {
                return;
            }
            try
            {
                var rest = new Dictionary<Transform, Quaternion>();
                foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Transform[] bones = smr.bones;
                    Matrix4x4[] bind = smr.sharedMesh != null ? smr.sharedMesh.bindposes : null;
                    if (bones == null || bind == null || bind.Length != bones.Length)
                    {
                        continue;
                    }
                    var index = new Dictionary<Transform, int>();
                    for (int i = 0; i < bones.Length; i++)
                    {
                        if (bones[i] != null && !index.ContainsKey(bones[i]))
                        {
                            index[bones[i]] = i;
                        }
                    }
                    for (int i = 0; i < bones.Length; i++)
                    {
                        Transform b = bones[i];
                        if (b == null || rest.ContainsKey(b) || !b.name.Contains("Digit") || b.parent == null
                            || !index.TryGetValue(b.parent, out int parent))
                        {
                            continue;
                        }
                        Matrix4x4 local = bind[parent] * bind[i].inverse;
                        rest[b] = local.rotation;
                    }
                }
                foreach (KeyValuePair<Transform, Quaternion> kv in rest)
                {
                    _fingers.Add((kv.Key, kv.Value, kv.Key.localRotation));
                }
            }
            catch (Exception ex)
            {
                _fingers.Clear();
                PhotoLog.Debug($"Photo actor '{Name}': finger rest pose unavailable: {ex.Message}");
            }
        }

        private void ApplyFingers()
        {
            foreach ((Transform bone, Quaternion rest, Quaternion grip) f in _fingers)
            {
                if (f.bone != null)
                {
                    f.bone.localRotation = WeaponVisible ? f.grip : f.rest;
                }
            }
        }

        public RigSettings Pose { get; } = new RigSettings();

        public PoseEdit PoseEditor { get; } = new PoseEdit();

        public IReadOnlyList<(Transform bone, Quaternion rest, Quaternion grip)> Fingers => _fingers;

        public ActorRig Rig { get; private set; }

        private void CaptureHeadBase()
        {
            Rig?.CaptureBase();
        }

        public void LateTick(UnityEngine.Camera camera)
        {
            if (Status != ActorStatus.Ready || Rig == null)
            {
                return;
            }
            Rig.Apply(Pose, PoseEditor, camera);
            bool follow = _gripValid && !Animate && HasHeldWeapon;
            if (follow)
            {
                FollowHand();
            }
            else
            {
                if (_following && !_stanceWeapon && _weaponObject != null)
                {
                    _weaponObject.transform.localPosition = Vector3.zero;
                    _weaponObject.transform.localRotation = Quaternion.identity;
                }
                if (_stanceWeapon && WeaponVisible)
                {
                    PlaceStanceWeapon(false);
                }
            }
            if (_offhand != null)
            {
                bool showOffhand = follow && HeldSlot == EquipmentSlot.Holster;
                if (_offhand.activeSelf != showOffhand)
                {
                    _offhand.SetActive(showOffhand);
                }
                if (showOffhand)
                {
                    PlaceOffhand();
                }
            }
            _following = follow;
        }

        private bool _gripValid;
        private bool _following;
        private Vector3 _gripPos;
        private Quaternion _gripRot = Quaternion.identity;

        public bool HasHeldWeapon
        {
            get
            {
                if (!WeaponVisible || _weaponObject == null || !_weaponObject.activeInHierarchy)
                {
                    return false;
                }
                foreach ((Renderer r, bool off) w in _weaponRenderers)
                {
                    if (w.r != null && w.r.enabled && !w.r.forceRenderingOff)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public Transform HeldWeapon => HasHeldWeapon ? _weaponObject.transform : null;

        private Transform GripPalm => Rig?.Skeleton?.Find("Base HumanRPalm")?.Bone;

        public void ResetGrip() => _gripValid = false;

        public bool PendingLoad { get; set; }

        public string PendingPoseState { get; set; }

        public float[] PendingGrip { get; set; }

        public float[] GripData => _gripValid
            ? new[] { _gripPos.x, _gripPos.y, _gripPos.z, _gripRot.x, _gripRot.y, _gripRot.z, _gripRot.w }
            : null;

        public void RestorePoseState(string json, float[] grip)
        {
            PoseState state;
            try
            {
                state = PoseEdit.Deserialize(json);
            }
            catch (Exception ex)
            {
                PhotoLog.Warn($"Photo actor '{Name}': saved pose could not be read: {ex.Message}");
                return;
            }
            ClearStance();
            PoseEditor.Restore(state);
            PoseEditor.SetBase(state.FullPose, state.FullHips);
            _savedFull = state.FullPose;
            SavedPoseName = state.FullPose != null ? Name : null;
            if (grip != null && grip.Length == 7 && grip.All(v => !float.IsNaN(v) && !float.IsInfinity(v)))
            {
                _gripPos = new Vector3(grip[0], grip[1], grip[2]);
                _gripRot = PoseMath.Normalized(new Quaternion(grip[3], grip[4], grip[5], grip[6]));
                _gripValid = true;
            }
        }

        private bool _leftOnWeapon;

        private void OnRigBase()
        {
            if (_stanceWeapon && !Animate && HasHeldWeapon)
            {
                PlaceStanceWeapon(false);
                SnapHandsToWeapon();
            }
            else if (!Animate && HasHeldWeapon && PoseEditor.State.FullPose != null && !PoseEditor.State.RestBase)
            {
                HoldWeaponInPalm();
            }
            CaptureGrip();
        }

        private void HoldWeaponInPalm()
        {
            Transform palm = GripPalm;
            Transform marker = Marker("weapon_R_IK_marker");
            if (palm == null || marker == null)
            {
                return;
            }
            Transform w = _weaponObject.transform;
            if (!_gripValid)
            {
                Vector3 relPos = w.InverseTransformPoint(marker.position);
                Quaternion relRot = Quaternion.Inverse(w.rotation) * marker.rotation;
                _gripRot = Quaternion.Inverse(relRot);
                _gripPos = -(_gripRot * relPos);
                _gripValid = true;
                Quaternion g0 = GripWorld(palm);
                w.SetPositionAndRotation(palm.TransformPoint(_gripPos), g0);
                Transform left = Marker("weapon_L_IK_marker");
                Transform[] leftChain = Rig.Chain(PoseLimb.LeftHand);
                _leftOnWeapon = !DualWield && left != null && leftChain != null && Vector3.Distance(leftChain[2].position, left.position) < 0.25f;
            }
            Quaternion g = GripWorld(palm);
            w.SetPositionAndRotation(palm.TransformPoint(_gripPos), g);
            if (_leftOnWeapon)
            {
                SnapHand(HandMarkers[0]);
            }
        }

        private static readonly (string marker, string bend, PoseLimb limb)[] HandMarkers =
        {
            ("weapon_L_IK_marker", "Bend_Goal_Left", PoseLimb.LeftHand),
            ("weapon_R_IK_marker", "Bend_Goal_Right", PoseLimb.RightHand)
        };

        private GameObject _markerWeapon;
        private readonly Dictionary<string, Transform> _markerCache = new Dictionary<string, Transform>();

        private Transform Marker(string name)
        {
            if (_markerWeapon != _weaponObject)
            {
                _markerWeapon = _weaponObject;
                _markerCache.Clear();
                foreach ((string m, string b, PoseLimb l) in HandMarkers)
                {
                    _markerCache[m] = FindChild(_weaponObject.transform, m);
                    _markerCache[b] = FindChild(_weaponObject.transform, b);
                }
                PhotoLog.Debug($"Photo actor '{Name}': weapon hand markers L={_markerCache["weapon_L_IK_marker"] != null} R={_markerCache["weapon_R_IK_marker"] != null}.");
            }
            return _markerCache.TryGetValue(name, out Transform t) ? t : null;
        }

        private void SnapHandsToWeapon()
        {
            foreach ((string marker, string bend, PoseLimb limb) hand in HandMarkers)
            {
                SnapHand(hand, true);
            }
        }

        private void SnapHand((string marker, string bend, PoseLimb limb) hand, bool useBendGoal = false)
        {
            Transform marker = Marker(hand.marker);
            Transform[] chain = Rig.Chain(hand.limb);
            if (marker == null || chain == null)
            {
                return;
            }
            Vector3 a = chain[0].position;
            Vector3 b = chain[1].position;
            Vector3 bend = Vector3.Cross(b - a, chain[2].position - b);
            Transform goal = useBendGoal ? Marker(hand.bend) : null;
            if (goal != null)
            {
                Vector3 toGoal = Vector3.Cross(goal.position - a, marker.position - goal.position);
                if (toGoal.sqrMagnitude > 1e-6f)
                {
                    bend = toGoal;
                }
            }
            if (bend.sqrMagnitude < 1e-6f)
            {
                return;
            }
            RootMotion.FinalIK.IKSolverTrigonometric.Solve(chain[0], chain[1], chain[2], marker.position, bend.normalized, 1f);
            chain[2].rotation = marker.rotation;
        }

        private static Transform FindChild(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t;
                }
            }
            return null;
        }

        private void CaptureGrip()
        {
            if (_gripValid || Animate || !HasHeldWeapon)
            {
                return;
            }
            Transform palm = GripPalm;
            if (palm == null)
            {
                return;
            }
            if (_stanceWeapon)
            {
                PlaceStanceWeapon(false);
            }
            else
            {
                _weaponObject.transform.localPosition = Vector3.zero;
                _weaponObject.transform.localRotation = Quaternion.identity;
            }
            Transform w = _weaponObject.transform;
            _gripPos = palm.InverseTransformPoint(w.position);
            _gripRot = Quaternion.Inverse(palm.rotation) * w.rotation;
            _gripValid = true;
        }

        private Quaternion GripWorld(Transform palm) => palm.rotation * _gripRot;

        private void FollowHand()
        {
            Transform palm = GripPalm;
            if (palm == null)
            {
                return;
            }
            PoseState s = PoseEditor.State;
            Vector3 up = s.WeaponPos != null && s.WeaponPos.Length == 3 ? new Vector3(s.WeaponPos[0], s.WeaponPos[1], s.WeaponPos[2]) : Vector3.zero;
            Quaternion ur = s.WeaponRot != null && s.WeaponRot.Length == 4 ? PoseMath.Normalized(new Quaternion(s.WeaponRot[0], s.WeaponRot[1], s.WeaponRot[2], s.WeaponRot[3])) : Quaternion.identity;
            Quaternion g = GripWorld(palm);
            _weaponObject.transform.SetPositionAndRotation(palm.TransformPoint(_gripPos) + g * up, g * ur);
        }

        public bool WeaponGizmoFrame(out Quaternion gripWorld)
        {
            gripWorld = Quaternion.identity;
            Transform palm = GripPalm;
            if (!_following || palm == null)
            {
                return false;
            }
            gripWorld = GripWorld(palm);
            return true;
        }

        private void ApplyStance()
        {
            StanceSnapshot s = Stance;
            if (s == null || Rig == null || Root == null)
            {
                return;
            }
            var full = new Dictionary<string, float[]>();
            foreach (SkelBone b in Rig.Skeleton.Map)
            {
                if (s.Bones.TryGetValue(b.Name, out Quaternion q))
                {
                    full[b.Name] = new[] { q.x, q.y, q.z, q.w };
                }
            }
            float[] hipsPosition = null;
            PosePart hips = Rig.Skeleton.Hips;
            if (s.HasHips && hips != null && hips.Bone.parent != null)
            {
                Transform parent = hips.Bone.parent;
                Quaternion local = Quaternion.Inverse(parent.rotation) * (Root.rotation * s.HipsRootRotation);
                Vector3 position = parent.InverseTransformPoint(Root.TransformPoint(s.HipsRootPosition));
                full[hips.Key] = new[] { local.x, local.y, local.z, local.w };
                hipsPosition = new[] { position.x, position.y, position.z };
            }
            if (full.Count == 0)
            {
                PhotoLog.Warn($"Photo actor '{Name}': stance has no matching bones, using standing pose.");
                return;
            }
            PoseEditor.State.FullPose = full;
            PoseEditor.State.FullHips = hipsPosition;
            _stanceFull = full;
            PoseEditor.SetBase(full, hipsPosition);
            _gripValid = false;
            if (s.HasWeapon && _weaponObject != null && SameWeapon(_weaponObject.name, s.WeaponPrefabName))
            {
                SetWeaponVisible(true);
                PlaceStanceWeapon(true);
            }
            else if (s.HasWeapon)
            {
                PhotoLog.Debug($"Photo actor '{Name}': held {s.WeaponName} isn't the model's weapon ({(_weaponObject != null ? _weaponObject.name : "none")}); no weapon in hands.");
            }
        }

        private static bool SameWeapon(string a, string b)
        {
            string Clean(string n) => (n ?? "").Replace("(Clone)", "").Trim();
            return Clean(a).Length > 0 && string.Equals(Clean(a), Clean(b), StringComparison.OrdinalIgnoreCase);
        }

        private void PlaceStanceWeapon(bool parts)
        {
            Transform chest = _loader?.ModelPlayerPoser?.PBones?.Ribcage?.Original;
            if (Stance == null || _weaponObject == null || chest == null)
            {
                return;
            }
            if (parts)
            {
                foreach (Animator a in _weaponObject.GetComponentsInChildren<Animator>(true))
                {
                    if (a.enabled)
                    {
                        a.enabled = false;
                        _stanceAnimators.Add(a);
                    }
                }
                Stance.PlaceWeapon(_weaponObject.transform, chest, true);
            }
            else
            {
                Stance.PlaceWeapon(_weaponObject.transform, chest, false);
            }
            _stanceWeapon = true;
        }

        private Dictionary<string, float[]> _savedFull;

        public string SavedPoseName { get; private set; }

        public bool InSavedPose => _savedFull != null && PoseEditor.State.FullPose == _savedFull;

        public bool ApplySavedPose(string name, PoseFile file, out string error)
        {
            error = null;
            if (Status != ActorStatus.Ready || Rig == null)
            {
                error = "the character isn't ready yet.";
                return false;
            }
            if (file?.Bones == null || file.Bones.Count == 0)
            {
                error = "that pose is empty.";
                return false;
            }
            Dictionary<string, float[]> bones = file.Bones
                .Where(kv => kv.Value != null && kv.Value.Length == 4 && kv.Value.All(v => !float.IsNaN(v) && !float.IsInfinity(v)))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            if (!bones.Keys.Any(k => Rig.Skeleton.Find(k) != null || Rig.Skeleton.ByKey.ContainsKey(k)))
            {
                error = "that pose has no bones this character has.";
                return false;
            }
            ClearStance();
            PoseEditor.Checkpoint();
            PoseState s = PoseEditor.State;
            s.FullPose = bones;
            s.FullHips = file.Hips != null && file.Hips.Length == 3 && file.Hips.All(v => !float.IsNaN(v) && !float.IsInfinity(v) && Mathf.Abs(v) < 100f) ? file.Hips : null;
            s.Rot.Clear();
            s.Loc.Clear();
            s.Pins.Clear();
            s.RestBase = false;
            _savedFull = bones;
            SavedPoseName = name;
            PoseEditor.SetBase(bones, s.FullHips);
            _gripValid = false;
            return true;
        }

        public void ClearSavedPose()
        {
            if (_savedFull != null && PoseEditor.BaseFull == _savedFull)
            {
                PoseEditor.SetBase(null, null);
            }
            if (InSavedPose)
            {
                PoseEditor.Checkpoint();
                PoseEditor.State.FullPose = null;
                PoseEditor.State.FullHips = null;
            }
            _savedFull = null;
        }

        public void RestoreStance()
        {
            if (Stance == null || Status != ActorStatus.Ready)
            {
                return;
            }
            PoseEditor.Checkpoint();
            _savedFull = null;
            _stanceFull = null;
            PoseEditor.State.FullPose = null;
            PoseEditor.State.FullHips = null;
            ApplyFirstPose();
            ApplyStance();
        }

        public void ClearStance()
        {
            if (_stanceFull != null && PoseEditor.BaseFull == _stanceFull)
            {
                PoseEditor.SetBase(null, null);
            }
            if (_stanceFull != null && PoseEditor.State.FullPose == _stanceFull)
            {
                PoseEditor.Checkpoint();
                PoseEditor.State.FullPose = null;
                PoseEditor.State.FullHips = null;
            }
            _stanceFull = Stance != null ? new Dictionary<string, float[]>() : null;
            if (_stanceWeapon)
            {
                _stanceWeapon = false;
                foreach (Behaviour a in _stanceAnimators)
                {
                    if (a != null)
                    {
                        a.enabled = true;
                    }
                }
                _stanceAnimators.Clear();
                if (_weaponObject != null)
                {
                    _weaponObject.transform.localPosition = Vector3.zero;
                    _weaponObject.transform.localRotation = Quaternion.identity;
                }
            }
        }

        private readonly List<(Renderer r, bool off)> _weaponRenderers = new List<(Renderer, bool)>();

        private void CaptureWeaponRest()
        {
            _weaponRest.Clear();
            _weaponRenderers.Clear();
            if (_weaponObject == null)
            {
                return;
            }
            foreach (Transform t in _weaponObject.GetComponentsInChildren<Transform>(true))
            {
                _weaponRest.Add((t, t.localPosition, t.localRotation));
            }
            foreach (Renderer r in _weaponObject.GetComponentsInChildren<Renderer>(true))
            {
                _weaponRenderers.Add((r, r.forceRenderingOff));
            }
        }

        private void ReleaseWeapon()
        {
            try
            {
                foreach (Behaviour a in _stanceAnimators)
                {
                    if (a != null)
                    {
                        a.enabled = true;
                    }
                }
                _stanceAnimators.Clear();
                _stanceWeapon = false;
                foreach ((Transform t, Vector3 p, Quaternion r) w in _weaponRest)
                {
                    if (w.t != null)
                    {
                        w.t.localPosition = w.p;
                        w.t.localRotation = w.r;
                    }
                }
                foreach ((Renderer r, bool off) w in _weaponRenderers)
                {
                    if (w.r != null)
                    {
                        w.r.forceRenderingOff = w.off;
                    }
                }
            }
            catch (Exception ex)
            {
                PhotoLog.Warn($"Photo actor '{Name}': weapon reset before release failed: {ex.Message}");
            }
            _weaponRest.Clear();
            _weaponRenderers.Clear();
        }

        private void Teardown()
        {
            _cts?.Cancel();
            foreach ((Renderer renderer, bool forcedOff) in _hiddenSource)
            {
                if (renderer != null)
                {
                    renderer.forceRenderingOff = forcedOff;
                }
            }
            _hiddenSource.Clear();


            _pendingLoader?.Clear();
            _pendingLoader = null;
            Reloading = false;
            if (_override != null)
            {
                UnityEngine.Object.Destroy(_override);
                _override = null;
            }

            ReleaseOffhand();
            ReleaseWeapon();
            PlayerModelLoader loader = _loader;
            _loader = null;
            Animator = null;
            CurrentPose = null;
            _weaponObject = null;
            _head = null;
            Rig = null;
            loader?.Clear();
            if (Status != ActorStatus.Failed)
            {
                Status = ActorStatus.None;
            }
        }

        private void Fail(string error)
        {
            Status = ActorStatus.Failed;
            Error = error;
        }
    }
}
