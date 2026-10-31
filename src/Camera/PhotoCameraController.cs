using EFT;
using EFT.CameraControl;
using DuckCam.Actors;
using DuckCam.Core;
using DuckCam.Pause;
using UnityEngine;

namespace DuckCam.Camera
{
    internal enum CameraMode
    {
        Free,
        Orbit,
        Tripod,
        FirstPerson
    }

    internal sealed class PhotoCameraController
    {
        public const float MinFov = 5f;
        public const float MaxFov = 120f;
        private const float FullFlick = 10f;

        private UnityEngine.Camera _camera;
        private Transform _transform;
        private Player _player;

        private Vector3 _position;
        private float _yaw;
        private float _pitch;
        private float _roll;
        private float _fov;
        private float _near;
        private float _far;

        private Vector3 _velocity;
        private Vector2 _smoothedMouse;
        private float _zoomBlend;
        private float _zoomVelocity;

        private Vector3 _appliedPosition;
        private Quaternion _appliedRotation;
        private float _appliedFov;
        private bool _hasApplied;

        private Vector3 _anchor;
        private float _speedScale = 1f;
        private float _orbitDistance = 3f;
        private float _startFov = 60f;
        private float _startNear = 0.05f;
        private float _startFar = 1000f;

        public CameraMode Mode { get; set; } = CameraMode.Free;
        public bool HorizonLock { get; set; }
        public bool Owned { get; private set; }
        public UnityEngine.Camera Camera => _camera;
        public CameraAttachment Attachment { get; } = new CameraAttachment();
        public Vector3 Position => _hasApplied ? _appliedPosition : _position;
        public Quaternion Rotation => _hasApplied ? _appliedRotation : Quaternion.Euler(_pitch, _yaw, _roll);
        public Vector3 Anchor => _anchor;
        public float SpeedScale => _speedScale;
        public float OrbitDistance => _orbitDistance;
        public float Roll
        {
            get => _roll;
            set => _roll = HorizonLock ? 0f : Mathf.Clamp(value, -180f, 180f);
        }
        public float EffectiveFov => Mathf.Lerp(_fov, Mathf.Max(MinFov, _fov * (1f - DuckCamConfig.ZoomAmount.Value)), _zoomBlend);
        public int DriftCount { get; private set; }
        public float LastDrift { get; private set; }
        public bool ExternallyDriven { get; private set; }

        private bool Parented => Attachment.Attached && Attachment.Type == CameraAttachType.Parented;

        public float Fov
        {
            get => _fov;
            set => _fov = Mathf.Clamp(value, MinFov, MaxFov);
        }

        public float NearClip
        {
            get => _near;
            set
            {
                _near = Mathf.Clamp(value, 0.01f, 10f);
                _far = Mathf.Max(_far, _near + 1f);
            }
        }

        public float FarClip
        {
            get => _far;
            set => _far = Mathf.Clamp(value, _near + 1f, 20000f);
        }

        public float Radius => DuckCamConfig.RadiusMeters(DuckCamConfig.RadiusProfile.Value);

        public float DistanceFromAnchor => Vector3.Distance(Position, _anchor);

        public bool Acquire(Player player, RestoreStack stack, out string error)
        {
            error = null;
            _camera = CameraManager.Instance.Camera;
            if (_camera == null)
            {
                error = "CameraManager.Instance.Camera is null";
                return false;
            }

            _player = player;
            _transform = _camera.transform;

            Vector3 originalPosition = _transform.position;
            Quaternion originalRotation = _transform.rotation;
            float originalFov = _camera.fieldOfView;
            float originalNear = _camera.nearClipPlane;
            float originalFar = _camera.farClipPlane;
            UnityEngine.Camera ownedCamera = _camera;

            PlayerCameraController playerCameraController = player.GetComponent<PlayerCameraController>();
            bool pccWasEnabled = playerCameraController != null && playerCameraController.enabled;

            stack.Push("camera ownership", () =>
            {
                Owned = false;
                StopSpectating();
                ExternallyDriven = false;
                Attachment.Detach();
                LensOverride = null;
                if (_lensOverridden && ownedCamera != null)
                {
                    ownedCamera.usePhysicalProperties = false;
                    ownedCamera.lensShift = Vector2.zero;
                    _lensOverridden = false;
                }
                ProjectionOverride = null;
                if (_projectionOverridden && ownedCamera != null)
                {
                    ownedCamera.ResetProjectionMatrix();
                    _projectionOverridden = false;
                }
                FreezeFlags.CameraOwned = false;
                Application.onBeforeRender -= OnBeforeRender;
                if (ownedCamera != null)
                {
                    ownedCamera.transform.SetPositionAndRotation(originalPosition, originalRotation);
                    ownedCamera.fieldOfView = originalFov;
                    ownedCamera.nearClipPlane = originalNear;
                    ownedCamera.farClipPlane = originalFar;
                }
                if (playerCameraController != null)
                {
                    playerCameraController.enabled = pccWasEnabled;
                }
            });

            if (playerCameraController != null)
            {
                playerCameraController.enabled = false;
            }
            else
            {
                PhotoLog.WarnOnce("no-pcc", "PlayerCameraController not found, camera snap blocked by P12 only.");
            }

            FreezeFlags.CameraOwned = true;
            Owned = true;

            _position = originalPosition;
            Vector3 euler = originalRotation.eulerAngles;
            _pitch = NormalizePitch(euler.x);
            _yaw = euler.y;
            _roll = 0f;
            float startFov = DuckCamConfig.CameraFov.Value > 0f ? DuckCamConfig.CameraFov.Value : originalFov;
            _fov = Mathf.Clamp(startFov, MinFov, MaxFov);
            _near = Mathf.Clamp(originalNear, 0.01f, 10f);
            _far = Mathf.Max(originalFar, _near + 1f);
            _startFov = _fov;
            _startNear = _near;
            _startFar = _far;
            _anchor = player.Transform.position + Vector3.up * 1.5f;
            _velocity = Vector3.zero;
            _smoothedMouse = Vector2.zero;
            _zoomBlend = 0f;
            _speedScale = 1f;
            _hasApplied = false;
            DriftCount = 0;
            LastDrift = 0f;
            Mode = CameraMode.Free;

            Application.onBeforeRender += OnBeforeRender;
            return true;
        }

        public CameraPose CurrentPose
        {
            get
            {
                Quaternion r = Rotation;
                Vector3 e = r.eulerAngles;
                return new CameraPose { Position = Position, Yaw = e.y, Pitch = NormalizePitch(e.x), Roll = NormalizePitch(e.z), Fov = _fov };
            }
        }

        public void ResetLens()
        {
            HorizonLock = false;
            _roll = 0f;
            _fov = _startFov;
            _near = _startNear;
            _far = _startFar;
            _zoomBlend = 0f;
            _speedScale = 1f;
            foreach (ShakeSettings s in new[] { Shake.Manual, Shake.Path })
            {
                var d = new ShakeSettings();
                s.Enabled = false;
                s.MoveFrequency = d.MoveFrequency;
                s.MoveStrength = d.MoveStrength;
                s.RotationFrequency = d.RotationFrequency;
                s.RotationStrength = d.RotationStrength;
            }
        }

        public void SetPose(CameraPose pose)
        {
            DetachKeepingPose();
            _position = ClampToRadius(pose.Position);
            _yaw = pose.Yaw;
            _pitch = Mathf.Clamp(pose.Pitch, -89f, 89f);
            _roll = HorizonLock ? 0f : pose.Roll;
            Fov = pose.Fov;
            _velocity = Vector3.zero;
            _hasApplied = false;
        }

        public void DriveExternally(Vector3 position, Quaternion rotation, float fov)
        {
            DetachKeepingPose();
            ExternallyDriven = true;
            _position = position;
            Vector3 e = rotation.eulerAngles;
            _yaw = e.y;
            _pitch = NormalizePitch(e.x);
            _roll = NormalizePitch(e.z);
            Fov = fov;
            _velocity = Vector3.zero;
            _appliedPosition = position;
            _appliedRotation = rotation;
            _appliedFov = Mathf.Clamp(fov, MinFov, MaxFov);
            _hasApplied = true;
        }

        public void EndExternalDrive()
        {
            ExternallyDriven = false;
        }

        public void ResetRoll() => _roll = 0f;

        public bool CanNavigate => Owned && !ExternallyDriven && !Parented && ActorSubject == null && !SubjectAlive;

        private bool PrepareNavigate()
        {
            if (!CanNavigate)
            {
                return false;
            }
            if (Attachment.Attached)
            {
                DetachKeepingPose();
            }
            if (Mode != CameraMode.Free)
            {
                Mode = CameraMode.Free;
            }
            _velocity = Vector3.zero;
            _hasApplied = false;
            return true;
        }

        public void OrbitAround(Vector3 pivot, float yawDegrees, float pitchDegrees)
        {
            if (!PrepareNavigate())
            {
                return;
            }
            float pitch = Mathf.Clamp(_pitch + pitchDegrees, -89f, 89f);
            pitchDegrees = pitch - _pitch;
            Quaternion before = Quaternion.Euler(_pitch, _yaw, 0f);
            Quaternion after = Quaternion.Euler(pitch, _yaw + yawDegrees, 0f);
            Vector3 offset = _position - pivot;
            _position = ClampToRadius(pivot + after * (Quaternion.Inverse(before) * offset));
            _yaw += yawDegrees;
            _pitch = pitch;
        }

        public void PanView(Vector2 pixels, float distance)
        {
            if (!PrepareNavigate() || _camera == null)
            {
                return;
            }
            float perPixel = 2f * Mathf.Max(0.05f, distance) * Mathf.Tan(EffectiveFov * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);
            Quaternion look = Quaternion.Euler(_pitch, _yaw, _roll);
            _position = ClampToRadius(_position - (look * Vector3.right) * pixels.x * perPixel - (look * Vector3.up) * pixels.y * perPixel);
        }

        public void DollyToward(Vector3 pivot, float amount)
        {
            if (!PrepareNavigate())
            {
                return;
            }
            Quaternion look = Quaternion.Euler(_pitch, _yaw, _roll);
            Vector3 forward = look * Vector3.forward;
            float distance = Mathf.Max(0.05f, Vector3.Dot(pivot - _position, forward));
            float step = distance * Mathf.Clamp(amount, -0.9f, 0.9f);
            if (distance - step < 0.08f)
            {
                step = distance - 0.08f;
            }
            _position = ClampToRadius(_position + forward * step);
        }

        public void FrameBounds(Bounds bounds)
        {
            if (!PrepareNavigate())
            {
                return;
            }
            Quaternion look = Quaternion.Euler(_pitch, _yaw, _roll);
            float radius = Mathf.Max(0.1f, bounds.extents.magnitude);
            float distance = radius / Mathf.Sin(Mathf.Max(5f, EffectiveFov) * 0.5f * Mathf.Deg2Rad) * 1.1f;
            _position = ClampToRadius(bounds.center - look * Vector3.forward * distance);
        }

        public void ToggleOrbit()
        {
            DetachKeepingPose();
            if (Mode == CameraMode.Orbit || Mode == CameraMode.FirstPerson)
            {
                StopSpectating();
                Mode = CameraMode.Free;
                return;
            }
            Mode = CameraMode.Orbit;
            _orbitDistance = Mathf.Clamp(Vector3.Distance(_position, OrbitTarget()), 0.5f, 50f);
            Vector3 toCamera = _position - OrbitTarget();
            if (toCamera.sqrMagnitude > 1e-4f)
            {
                Vector3 e = Quaternion.LookRotation(-toCamera.normalized, Vector3.up).eulerAngles;
                _yaw = e.y;
                _pitch = NormalizePitch(e.x);
            }
        }

        public void ToggleTripod()
        {
            Mode = Mode == CameraMode.Tripod ? CameraMode.Free : CameraMode.Tripod;
            _velocity = Vector3.zero;
        }

        public bool Attach(out string message)
        {
            DetachKeepingPose();
            if (!Attachment.TryAttach(_camera, DuckCamConfig.AttachType.Value, DuckCamConfig.AttachRaycastDistance.Value, out message))
            {
                return false;
            }
            Mode = CameraMode.Free;
            if (Attachment.Type == CameraAttachType.Parented)
            {
                Quaternion frame = Attachment.FrameRotation;
                Quaternion local = Quaternion.Inverse(frame) * Rotation;
                _position = Quaternion.Inverse(frame) * (Position - Attachment.FramePosition);
                Vector3 e = local.eulerAngles;
                _yaw = e.y;
                _pitch = NormalizePitch(e.x);
                _roll = NormalizePitch(e.z);
            }
            _velocity = Vector3.zero;
            return true;
        }

        public void DetachKeepingPose()
        {
            if (!Attachment.Attached)
            {
                return;
            }
            Vector3 worldPos = Position;
            Vector3 e = Rotation.eulerAngles;
            Attachment.Detach();
            _position = worldPos;
            _yaw = e.y;
            _pitch = NormalizePitch(e.x);
            _roll = NormalizePitch(e.z);
            _velocity = Vector3.zero;
        }

        public void Tick(float dt, bool lookEnabled, bool keysEnabled, bool wheelEnabled)
        {
            if (!Owned || ExternallyDriven)
            {
                return;
            }

            if (Attachment.Attached && Attachment.Target == null)
            {
                Attachment.Detach();
            }

            bool aliases = DuckCamConfig.WasdAlias.Value && (FreezeFlags.InputBlocked || FreezeFlags.PlayerMoveBlocked);

            Vector2 rawMouse = lookEnabled
                ? new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * DuckCamConfig.LookSensitivity.Value
                : Vector2.zero;
            float mouseT = 1f - Mathf.Pow(DuckCamConfig.MouseSmoothing.Value, dt * 60f);
            _smoothedMouse = Vector2.Lerp(_smoothedMouse, rawMouse, mouseT);

            bool lookingAt = Attachment.Attached && Attachment.Type == CameraAttachType.LookAt;
            if (lookingAt && rawMouse.magnitude / DuckCamConfig.LookSensitivity.Value > DuckCamConfig.LookAtEscapeThreshold.Value * FullFlick)
            {
                DetachKeepingPose();
                lookingAt = false;
                PhotoLog.Verbose("Look At released by mouse movement.");
            }
            if (!lookingAt)
            {
                _yaw += _smoothedMouse.x;
                _pitch = Mathf.Clamp(_pitch - _smoothedMouse.y, -89f, 89f);
            }

            if (wheelEnabled)
            {
                HandleWheel();
            }
            HandleZoom(dt);

            bool menu = UI.InputContext.MenuActive;
            if (keysEnabled)
            {
                if (!HorizonLock && !menu)
                {
                    float rollInput = (DuckCamConfig.Held(DuckCamConfig.RollLeft) ? 1f : 0f) - (DuckCamConfig.Held(DuckCamConfig.RollRight) ? 1f : 0f);
                    _roll += rollInput * DuckCamConfig.RollSpeed.Value * dt;
                }
                if (DuckCamConfig.ResetRotation.Value.IsDown() || (aliases && !menu && Input.GetKeyDown(KeyCode.R)))
                {
                    ResetRoll();
                }
            }
            if (HorizonLock)
            {
                _roll = 0f;
            }

            switch (Mode)
            {
                case CameraMode.Free:
                    MoveFree(dt, keysEnabled, aliases);
                    break;
                case CameraMode.Orbit:
                    MoveOrbit();
                    break;
                case CameraMode.FirstPerson:
                    if (ActorSubject != null)
                    {
                        FollowActorEyes();
                    }
                    else
                    {
                        FollowEyes(dt);
                    }
                    break;
                case CameraMode.Tripod:
                    _velocity = Vector3.zero;
                    break;
            }

            if (lookingAt)
            {
                AimAt(Attachment.AimPoint, dt);
            }
        }

        private void HandleWheel()
        {
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(wheel) < 1e-4f)
            {
                return;
            }
            bool speedHeld = DuckCamConfig.Held(DuckCamConfig.SpeedKey);
            if (Mode == CameraMode.Orbit && !speedHeld)
            {
                _orbitDistance = Mathf.Clamp(_orbitDistance * Mathf.Pow(0.85f, wheel * 10f), 0.5f, 50f);
            }
            else if (speedHeld || DuckCamConfig.Wheel.Value == WheelFunction.MoveSpeed)
            {
                _speedScale = Mathf.Clamp(_speedScale * Mathf.Pow(DuckCamConfig.WheelSpeedStep.Value, wheel * 10f), 0.02f, 50f);
            }
            else
            {
                Fov -= wheel * DuckCamConfig.FovZoomSpeed.Value * 0.2f;
            }
        }

        private void HandleZoom(float dt)
        {
            float target = DuckCamConfig.Held(DuckCamConfig.ZoomKey) ? 1f : 0f;
            _zoomBlend = Mathf.SmoothDamp(_zoomBlend, target, ref _zoomVelocity, DuckCamConfig.ZoomTime.Value, Mathf.Infinity, dt);
        }

        private void MoveFree(float dt, bool keysEnabled, bool aliases)
        {
            Vector3 input = Vector3.zero;
            float vertical = 0f;
            float speed = DuckCamConfig.MoveSpeed.Value * _speedScale;
            if (keysEnabled && UI.InputContext.MenuActive)
            {
                if (DuckCamConfig.Held(DuckCamConfig.UiCamForward)) input.z += 1f;
                if (DuckCamConfig.Held(DuckCamConfig.UiCamBack)) input.z -= 1f;
                if (DuckCamConfig.Held(DuckCamConfig.UiCamRight)) input.x += 1f;
                if (DuckCamConfig.Held(DuckCamConfig.UiCamLeft)) input.x -= 1f;
                if (DuckCamConfig.Held(DuckCamConfig.UiCamUp)) vertical += 1f;
                if (DuckCamConfig.Held(DuckCamConfig.UiCamDown)) vertical -= 1f;
            }
            else if (keysEnabled)
            {
                if (DuckCamConfig.Held(DuckCamConfig.MoveForward) || (aliases && Input.GetKey(KeyCode.W))) input.z += 1f;
                if (DuckCamConfig.Held(DuckCamConfig.MoveBack) || (aliases && Input.GetKey(KeyCode.S))) input.z -= 1f;
                if (DuckCamConfig.Held(DuckCamConfig.MoveRight) || (aliases && Input.GetKey(KeyCode.D))) input.x += 1f;
                if (DuckCamConfig.Held(DuckCamConfig.MoveLeft) || (aliases && Input.GetKey(KeyCode.A))) input.x -= 1f;
                if (DuckCamConfig.Held(DuckCamConfig.MoveUp)) vertical += 1f;
                if (DuckCamConfig.Held(DuckCamConfig.MoveDown)) vertical -= 1f;
            }
            if (keysEnabled)
            {

                if (DuckCamConfig.Held(DuckCamConfig.MoveFast))
                {
                    speed *= DuckCamConfig.FastMultiplier.Value;
                }
                if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
                {
                    speed *= DuckCamConfig.SlowMultiplier.Value;
                }
            }

            Quaternion look = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 targetVelocity = (look * Vector3.ClampMagnitude(input, 1f) + Vector3.up * vertical) * speed;
            float t = 1f - Mathf.Pow(DuckCamConfig.MovementSmoothing.Value, dt * 60f);
            _velocity = Vector3.Lerp(_velocity, targetVelocity, t);
            if (_velocity.sqrMagnitude < 1e-8f)
            {
                _velocity = Vector3.zero;
                return;
            }

            if (Parented)
            {
                _position += _velocity * dt;
                return;
            }

            Vector3 target = ClampToRadius(_position + _velocity * dt);
            Vector3 resolved = CameraCollision.Resolve(_position, target, DuckCamConfig.CollisionMode.Value, DuckCamConfig.CollisionRadius.Value);
            if ((resolved - target).sqrMagnitude > 1e-6f)
            {
                _velocity = (resolved - _position) / Mathf.Max(dt, 1e-5f);
            }
            _position = resolved;
        }

        private void MoveOrbit()
        {
            Vector3 target = OrbitTarget();
            Quaternion look = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 desired = ClampToRadius(target - look * Vector3.forward * _orbitDistance);
            _position = CameraCollision.Resolve(target, desired, DuckCamConfig.CollisionMode.Value, DuckCamConfig.CollisionRadius.Value);
        }

        private void AimAt(Vector3 point, float dt)
        {
            Vector3 dir = point - _position;
            if (dir.sqrMagnitude < 1e-4f)
            {
                return;
            }
            Vector3 e = Quaternion.LookRotation(dir.normalized, Vector3.up).eulerAngles;
            float smoothing = DuckCamConfig.LookAtSmoothing.Value;
            float t = smoothing <= 0f ? 1f : 1f - Mathf.Exp(-dt / smoothing);
            _yaw = Mathf.LerpAngle(_yaw, e.y, t);
            _pitch = Mathf.Clamp(Mathf.LerpAngle(_pitch, NormalizePitch(e.x), t), -89f, 89f);
        }

        public Vector3 OrbitTarget()
        {
            Player p = SubjectAlive ? Subject : _player;
            if (p != null && p.PlayerBones != null && p.PlayerBones.Ribcage != null)
            {
                return p.PlayerBones.Ribcage.Original.position;
            }
            return p != null ? p.Transform.position + Vector3.up * 1.3f : _anchor;
        }

        public Player Subject { get; private set; }

        private bool SubjectAlive => Subject != null && Subject.HealthController != null && Subject.HealthController.IsAlive;
        private readonly System.Collections.Generic.List<Renderer> _hiddenForEyes = new System.Collections.Generic.List<Renderer>();

        public void Spectate(Player player, bool firstPerson)
        {
            StopSpectating();
            if (player == null)
            {
                return;
            }
            DetachKeepingPose();
            Subject = player;
            if (firstPerson)
            {
                Mode = CameraMode.FirstPerson;
                HideHead(player);
                FollowEyes(0f);
            }
            else
            {
                Mode = CameraMode.Free;
                _position = OrbitTarget() - player.Transform.rotation * Vector3.forward * 2.5f + Vector3.up * 0.3f;
                ToggleOrbit();
            }
        }

        public void StopSpectating()
        {
            foreach (Renderer r in _hiddenForEyes)
            {
                if (r != null)
                {
                    r.forceRenderingOff = false;
                }
            }
            _hiddenForEyes.Clear();
            if ((Subject != null || ActorSubject != null) && (Mode == CameraMode.FirstPerson || Mode == CameraMode.Orbit))
            {
                Mode = CameraMode.Free;
            }
            Subject = null;
            ActorSubject = null;
        }

        public PhotoActor ActorSubject { get; private set; }

        public void ViewThroughActor(PhotoActor actor)
        {
            StopSpectating();
            if (actor == null || actor.Status != ActorStatus.Ready || actor.Root == null)
            {
                return;
            }
            DetachKeepingPose();
            ActorSubject = actor;
            Mode = CameraMode.FirstPerson;
            Transform head = actor.Head;
            if (head != null)
            {
                foreach (Renderer r in actor.Root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r != null && !r.forceRenderingOff && (r.bounds.center - head.position).sqrMagnitude < 0.3f * 0.3f)
                    {
                        r.forceRenderingOff = true;
                        _hiddenForEyes.Add(r);
                    }
                }
            }
            Vector3 e = Quaternion.LookRotation(Vector3.ProjectOnPlane(actor.Root.forward, Vector3.up).normalized + Vector3.up * 1e-4f, Vector3.up).eulerAngles;
            _yaw = e.y;
            _pitch = 0f;
            _roll = 0f;
            FollowActorEyes();
        }

        private void FollowActorEyes()
        {
            PhotoActor a = ActorSubject;
            if (a == null || a.Status != ActorStatus.Ready || a.Root == null)
            {
                StopSpectating();
                return;
            }
            Transform head = a.Head;
            Vector3 up = a.Root.up;
            Vector3 forward = Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.forward;
            Vector3 eyes = head != null ? head.position + up * 0.07f : a.Root.position + up * 1.6f;
            _position = eyes + forward * 0.09f;
            _velocity = Vector3.zero;
        }

        private void HideHead(Player player)
        {
            Transform head = player.PlayerBones?.Head?.Original;
            if (head == null)
            {
                return;
            }
            foreach (Renderer r in player.GetComponentsInChildren<Renderer>(true))
            {
                if (r != null && !r.forceRenderingOff && (r.bounds.center - head.position).sqrMagnitude < 0.35f * 0.35f)
                {
                    r.forceRenderingOff = true;
                    _hiddenForEyes.Add(r);
                }
            }
        }

        private void FollowEyes(float dt)
        {
            if (!SubjectAlive)
            {
                StopSpectating();
                return;
            }
            Transform head = Subject.PlayerBones?.Head?.Original;
            Vector3 look = Subject.LookDirection;
            if (look.sqrMagnitude < 1e-4f)
            {
                look = Subject.Transform.rotation * Vector3.forward;
            }
            look.Normalize();
            Vector3 eyes = head != null ? head.position + Vector3.up * 0.06f : Subject.Transform.position + Vector3.up * 1.6f;
            _position = eyes + look * 0.1f;
            _velocity = Vector3.zero;
            Vector3 e = Quaternion.LookRotation(look, Vector3.up).eulerAngles;
            float t = dt <= 0f ? 1f : 1f - Mathf.Exp(-dt / 0.05f);
            _yaw = Mathf.LerpAngle(_yaw, e.y, t);
            _pitch = Mathf.Clamp(Mathf.LerpAngle(_pitch, NormalizePitch(e.x), t), -89f, 89f);
            _roll = 0f;
        }

        private Vector3 ClampToRadius(Vector3 p)
        {
            float radius = Radius;
            if (float.IsInfinity(radius))
            {
                return p;
            }
            Vector3 offset = p - _anchor;
            return offset.sqrMagnitude > radius * radius ? _anchor + offset.normalized * radius : p;
        }

        public void Apply()
        {
            if (!Owned || _camera == null)
            {
                return;
            }

            if (!ExternallyDriven)
            {
                Quaternion rotation = Quaternion.Euler(_pitch, _yaw, _roll);
                Vector3 position = _position;
                if (Parented)
                {
                    Attachment.UpdateFrame(PhotoTime.Unscaled);
                    position = Attachment.FramePosition + Attachment.FrameRotation * _position;
                    rotation = Attachment.FrameRotation * rotation;
                }
                _appliedPosition = position;
                _appliedRotation = rotation;
                _appliedFov = EffectiveFov;
                _hasApplied = true;
            }

            WriteCamera();
        }

        private bool _projectionOverridden;

        public Matrix4x4? ProjectionOverride { get; set; }

        public struct Lens
        {
            public Vector2 Sensor;
            public float Focal;
            public Vector2 Shift;
        }

        public Lens? LensOverride { get; set; }

        private bool _lensOverridden;

        public bool LensActive => LensOverride.HasValue;

        public CameraShake Shake { get; } = new CameraShake();

        private Vector3 _writtenPosition;

        private void WriteCamera()
        {
            Vector3 position = _appliedPosition;
            Quaternion rotation = _appliedRotation;
            if (!ProjectionOverride.HasValue && !LensOverride.HasValue)
            {
                Shake.Evaluate(ExternallyDriven, out Vector3 offset, out Quaternion shakeRotation);
                position += rotation * offset;
                rotation *= shakeRotation;
            }
            _writtenPosition = position;
            _transform.SetPositionAndRotation(position, rotation);
            if (LensOverride.HasValue)
            {
                Lens lens = LensOverride.Value;
                _camera.usePhysicalProperties = true;
                _camera.gateFit = UnityEngine.Camera.GateFitMode.None;
                _camera.sensorSize = lens.Sensor;
                _camera.focalLength = lens.Focal;
                _camera.lensShift = lens.Shift;
                _lensOverridden = true;
            }
            else
            {
                if (_lensOverridden)
                {
                    _camera.usePhysicalProperties = false;
                    _camera.lensShift = Vector2.zero;
                    _camera.ResetProjectionMatrix();
                    _lensOverridden = false;
                }
                _camera.fieldOfView = _appliedFov;
            }
            _camera.nearClipPlane = _near;
            _camera.farClipPlane = _far;
            if (ProjectionOverride.HasValue)
            {
                _camera.projectionMatrix = ProjectionOverride.Value;
                _projectionOverridden = true;
            }
            else if (_projectionOverridden)
            {
                _camera.ResetProjectionMatrix();
                _projectionOverridden = false;
            }
        }

        private void OnBeforeRender()
        {
            if (!Owned || _camera == null || !_hasApplied)
            {
                return;
            }

            float drift = Vector3.Distance(_transform.position, _writtenPosition);
            if (drift > 0.001f || (!LensOverride.HasValue && Mathf.Abs(_camera.fieldOfView - _appliedFov) > 0.01f))
            {
                DriftCount++;
                LastDrift = drift;
                if (DriftCount == 1)
                {
                    PhotoLog.Warn($"Camera changed after LateUpdate (moved {drift:F3} m, fov {_camera.fieldOfView:F1}), re-applied before render.");
                }
            }
            WriteCamera();
        }

        public bool IsDrivenCameraCurrent => _camera != null && CameraManager.Instance.Camera == _camera;

        public static float NormalizePitch(float x) => x > 180f ? x - 360f : x;
    }
}
