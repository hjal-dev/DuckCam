using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using EFT;
using DuckCam.Actors;
using DuckCam.Camera;
using DuckCam.Culling;
using DuckCam.Lighting;
using DuckCam.Pause;
using DuckCam.Rendering;
using DuckCam.UI;
using UnityEngine;

namespace DuckCam.Core
{
    internal sealed class DuckCamController : MonoBehaviour
    {
        private readonly RestoreStack _session = new RestoreStack();
        private readonly RestoreStack _freezeStack = new RestoreStack();
        private readonly RaidFreezeController _freeze = new RaidFreezeController();
        private readonly TimeDilation _dilation = new TimeDilation();
        private readonly PhotoCullingAnchor _culling = new PhotoCullingAnchor();
        private readonly ScreenshotController _screenshots = new ScreenshotController();
        private readonly PathVisuals _pathVisuals = new PathVisuals();
        private DuckCamUI _ui;
        private Coroutine _dofRoutine;

        private Coroutine _enterRoutine;
        private PlayerStateSnapshot _snapshot;
        private Player _player;
        private float _enteredAt;
        private float _sessionBaseTimeScale = 1f;
        private bool _everLive;
        private bool _gameSpeedOn;

        public static DuckCamController Instance { get; private set; }

        public DuckCamState State { get; private set; } = DuckCamState.Inactive;
        public bool Frozen { get; private set; }
        public bool StartedLive { get; private set; }
        public bool Live => State == DuckCamState.Active && !Frozen;
        public bool CameraHasMouse { get; set; } = true;

        public PhotoCameraController Camera { get; } = new PhotoCameraController();
        public CameraBookmarks Bookmarks { get; } = new CameraBookmarks();
        public CameraMemoryList Memory { get; } = new CameraMemoryList();
        public PathSet Paths { get; } = new PathSet();
        public CameraPath Path => Paths.Active;
        public CameraRecording Recording { get; } = new CameraRecording();
        public PathVisuals PathVisuals => _pathVisuals;
        public LightingStudio Lights { get; } = new LightingStudio();
        public PhotoEffects Effects { get; } = new PhotoEffects();
        public DepthOfFieldController Dof { get; } = new DepthOfFieldController();
        public DofRenderer DofRender { get; } = new DofRenderer();
        public VideoRecorder Video { get; } = new VideoRecorder();
        public OverlayController Overlays { get; } = new OverlayController();
        public PhotoDecor Decor { get; } = new PhotoDecor();
        public DuckCam.Environment.TimeOfDayController TimeOfDay { get; } = new DuckCam.Environment.TimeOfDayController();
        public DuckCam.Environment.WeatherOverride Weather { get; } = new DuckCam.Environment.WeatherOverride();
        public ActorManager Actors { get; } = new ActorManager();
        public WeaponHider Weapons { get; } = new WeaponHider();
        public BotFreezer BotFreeze { get; } = new BotFreezer();
        public DuckCam.Spawning.BotSpawnService BotSpawns { get; } = new DuckCam.Spawning.BotSpawnService();
        public PhotoCullingAnchor Culling => _culling;
        public ScreenshotController Screenshots => _screenshots;
        public RaidFreezeController Freeze => _freeze;
        public TimeDilation Dilation => _dilation;
        public RestoreStack Restores => _session;
        public RestoreStack FreezeRestores => _freezeStack;
        public Player Player => _player;
        public bool UiVisible { get; set; } = true;
        public string LastMessage { get; set; } = "";
        public List<string> LastExitFailures { get; private set; } = new List<string>();
        public List<string> LastSnapshotDiff { get; private set; } = new List<string>();
        public float ActiveSeconds => State == DuckCamState.Active ? Time.unscaledTime - _enteredAt : 0f;
        public bool BsgCullingDisabled => FreezeFlags.BsgCullingDisabled;
        public bool Playback => Path.Playing || Recording.Playing || (_ui != null && _ui.Timeline.Timeline.Playing);

        private void Awake()
        {
            Instance = this;
            _ui = new DuckCamUI(this);
        }

        private bool _guidePending;

        private void Update()
        {
            if (State == DuckCamState.Active && _ui.ModalOpen)
            {
                _freeze.TickTimer();
                _ui.TickWindows();
                return;
            }
            HandleSessionKeys();
            if (State != DuckCamState.Active)
            {
                return;
            }

            if (!SessionValid(out string reason))
            {
                ForceExit(reason);
                return;
            }

            _freeze.TickTimer();
            Weapons.PmcCopyWeapon = () => Actors.Pmc?.InHandsWeapon;
            Weapons.Tick(_player, _session);

            if (DofRender.Rendering)
            {
                if (DuckCamConfig.UiExit.Value.IsDown() || DuckCamConfig.ToggleKey.Value.IsDown())
                {
                    DofRender.Cancel();
                    LastMessage = "Cancelling DoF render...";
                }
                return;
            }

            if (!UiVisible)
            {
                InputContext.LeaveField();
            }
            bool typing = InputContext.Typing;
            bool leftFieldWithEsc = false;
            if (typing && (DuckCamConfig.UiExit.Value.IsDown() || DuckCamConfig.UiHide.Value.IsDown()))
            {
                leftFieldWithEsc = DuckCamConfig.UiExit.Value.IsDown();
                InputContext.LeaveField();
                typing = false;
            }
            InputContext.Update(true, UiVisible);
            if (!typing && !leftFieldWithEsc)
            {
                if (HandleHotkeys())
                {
                    return;
                }
                if (UiVisible && _ui.PoseEditor.Open)
                {
                    _ui.PoseEditor.HandleKeys();
                }
                else if (UiVisible && !_ui.Spawner.Open && !_ui.Addons.Open && !_ui.Gallery.Open)
                {
                    _ui.HandleKeys();
                }
            }
            UpdateInputOwnership();
            InputContext.Update(true, UiVisible);

            _dilation.Tick();
            TimeOfDay.Tick(Frozen);
            float pathDt = DuckCamConfig.PathUsesRealTime.Value ? PhotoTime.Unscaled : PhotoTime.Scaled;

            if (_ui.Timeline.Tick(out Vector3 tp, out Quaternion tr, out float tf))
            {
                Camera.DriveExternally(tp, tr, tf);
            }
            else if (Recording.Tick(pathDt, out Vector3 rp, out Quaternion rr, out float rf))
            {
                Camera.DriveExternally(rp, rr, rf);
            }
            else if (Path.Tick(pathDt))
            {
                Path.Evaluate(Path.EasedProgress, out Vector3 pp, out Quaternion pr, out float pf);
                Camera.DriveExternally(pp, pr, pf);
            }
            else if (Camera.ExternallyDriven)
            {
                Camera.EndExternalDrive();
            }

            bool look = UiVisible ? Input.GetMouseButton(1) : CameraHasMouse;
            bool overUi = UiVisible && _ui.PointerOverUi;
            if (overUi)
            {
                _ui.Scroll(Input.mouseScrollDelta.y);
            }
            if (PoseMode)
            {
                bool fly = look && _ui.PoseEditor.AllowsFly;
                Camera.Tick(PhotoTime.Unscaled, fly, fly && !typing, fly && !overUi);
            }
            else
            {
                Camera.Tick(PhotoTime.Unscaled, look, !typing, !overUi);
            }
            if (Path.FollowKey >= 0 && Path.FollowKey < Path.Keys.Count && !Playback)
            {
                PathKeyframe followed = Path.Keys[Path.FollowKey];
                followed.Position = Camera.Position;
                followed.Rotation = Camera.Rotation;
                followed.Fov = Camera.Fov;
                Path.MarkDirty();
            }
            Recording.TickRecord(PhotoTime.Unscaled, Camera.Position, Camera.Rotation, Camera.EffectiveFov);
            Actors.Tick();
            if (Camera.Camera != null)
            {
                Dof.Tick(Camera.Camera, Camera.OrbitTarget(), Camera.Attachment.Target, Time.unscaledDeltaTime);
            }
        }

        private void LateUpdate()
        {
            if (State != DuckCamState.Active)
            {
                return;
            }

            Camera.Apply();
            if (Camera.Camera != null)
            {
                Lights.UpdateFlash(_session, Camera.Camera.transform, _screenshots.Capturing || DofRender.Rendering);
            }

            Actors.LateTick(Camera.Camera);
            _ui.Spawner.LateTick();
            _culling.Tick(Camera.Position);
            _pathVisuals.Update(Path, !_screenshots.Capturing && !Playback && !DofRender.Rendering);

            if (Live && !CameraHasMouse && !UiVisible)
            {
                if (_cursorShown == true)
                {
                    GameCursor(false);
                }
                _cursorShown = null;
                return;
            }
            bool showCursor = UiVisible && !Input.GetMouseButton(1);
            if (_cursorShown != showCursor)
            {
                _cursorShown = showCursor;
                GameCursor(showCursor);
            }
        }

        private bool? _cursorShown;

        private static void GameCursor(bool show)
        {
            try
            {
                EFT.GlobalEvents.GlobalEventsController.Instance.CreateCommonEvent<EFT.GlobalEvents.ToggleShowInGameCursorEvent>().Invoke(show);
            }
            catch (Exception ex)
            {
                PhotoLog.WarnOnce("gamecursor", "Game cursor switch failed, using Unity directly: " + ex.Message);
                Cursor.visible = show;
                Cursor.lockState = show ? CursorLockMode.None : CursorLockMode.Locked;
            }
        }

        private void OnGUI()
        {
            if (State != DuckCamState.Active)
            {
                return;
            }
            if (Video.Recording)
            {
                if (Event.current.type == EventType.Repaint)
                {
                    Rect frame = CompositionOverlay.FrameScreenRect();
                    Overlays.DrawOnScreen(frame);
                    Decor.Draw(frame);
                }
                return;
            }
            if (_screenshots.Capturing || DofRender.HideOverlay)
            {
                if (_screenshots.DecorOnScreen)
                {
                    Decor.Draw(CompositionOverlay.FrameScreenRect());
                }
                return;
            }
            if (DofRender.Rendering)
            {
                if (Event.current.type == EventType.Repaint)
                {
                    HudTheme.Ensure();
                    float w = Screen.width * 0.3f;
                    var bar = new Rect((Screen.width - w) * 0.5f, Screen.height - 40f, w, 6f);
                    HudTheme.Fill(bar, HudTheme.P.RowFill);
                    HudTheme.Fill(new Rect(bar.x, bar.y, w * DofRender.Progress, bar.height), HudTheme.P.Highlight);
                    GUI.Label(new Rect(bar.x, bar.y - 22f, w, 20f), $"DOF RENDER {DofRender.Progress * 100f:0}%   ({DuckCamConfig.UiExit.Value} to cancel)", HudTheme.HudSmall);
                }
                return;
            }
            if (Event.current.type == EventType.Repaint)
            {
                CompositionOverlay.DrawLetterbox();
                Overlays.DrawOnScreen(CompositionOverlay.FrameScreenRect());
                Decor.Draw(CompositionOverlay.FrameScreenRect());
                if (UiVisible || DuckCamConfig.GuidesWhenUiHidden.Value)
                {
                    CompositionOverlay.DrawGuides();
                }
                if (UiVisible && Dof.FocusPoint.HasValue && Camera.Camera != null)
                {
                    DrawFocusMarker(Dof.FocusPoint.Value);
                }
            }
            bool modal = _ui.ModalOpen;
            bool enabledBefore = GUI.enabled;
            GUI.enabled = !modal;
            if (UiVisible || modal)
            {
                _ui.DrawPathEditor();
                _ui.PoseEditor.DrawGizmos();
                _ui.Draw();
            }
            GUI.enabled = enabledBefore;
            _ui.DrawWindows();
            DrawExitConfirm();
        }

        private void DrawFocusMarker(Vector3 world)
        {
            Vector3 s = ScreenSpace.WorldToScreen(Camera.Camera, world);
            if (s.z <= 0f)
            {
                return;
            }
            HudTheme.Ensure();
            var c = new Vector2(s.x, Screen.height - s.y);
            Color col = HudTheme.P.Highlight;
            const float r = 14f;
            const float l = 6f;
            HudTheme.Fill(new Rect(c.x - r, c.y - r, l, 1f), col);
            HudTheme.Fill(new Rect(c.x - r, c.y - r, 1f, l), col);
            HudTheme.Fill(new Rect(c.x + r - l, c.y - r, l, 1f), col);
            HudTheme.Fill(new Rect(c.x + r, c.y - r, 1f, l), col);
            HudTheme.Fill(new Rect(c.x - r, c.y + r, l, 1f), col);
            HudTheme.Fill(new Rect(c.x - r, c.y + r - l, 1f, l), col);
            HudTheme.Fill(new Rect(c.x + r - l, c.y + r, l, 1f), col);
            HudTheme.Fill(new Rect(c.x + r, c.y + r - l, 1f, l + 1f), col);
            HudTheme.Fill(new Rect(c.x - 1f, c.y - 1f, 3f, 3f), col);
        }

        private void OnDestroy()
        {
            StopVideo(raidEnding: true);
            ForceExit("controller destroyed (raid end / scene unload)");
            BotFreeze.Release("raid end");
            _pathVisuals.Destroy();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void HandleSessionKeys()
        {
            if (DuckCamConfig.BotFreezeKey.Value.IsDown() && !InputContext.Typing)
            {
                ToggleBotFreeze();
            }
            BotFreeze.Tick();
            BotSpawns.Tick();

            if (TickExitConfirm())
            {
                return;
            }

            if (DuckCamConfig.ToggleKey.Value.IsDown())
            {
                switch (State)
                {
                    case DuckCamState.Inactive:
                        TryStart(frozen: true);
                        break;
                    case DuckCamState.Active when !Frozen:
                        SetFrozen(true);
                        break;
                    case DuckCamState.Active when StartedLive:
                        SetFrozen(false);
                        break;
                    case DuckCamState.Active:
                        RequestExit("user");
                        break;
                    default:
                        PhotoLog.Verbose("Toggle ignored while " + State);
                        break;
                }
            }

            if (DuckCamConfig.UnsnapKey.Value.IsDown())
            {
                if (State == DuckCamState.Inactive)
                {
                    TryStart(frozen: false);
                }
                else if (State == DuckCamState.Active)
                {
                    RequestExit("camera snapped back");
                }
            }
        }

        public void ToggleBotFreeze()
        {
            BotFreeze.Toggle();
            string msg = BotFreeze.Active ? $"Bots frozen ({BotFreeze.Count}). Press {DuckCamConfig.BotFreezeKey.Value} to release." : "Bots released.";
            LastMessage = msg;
            if (State != DuckCamState.Active)
            {
                try
                {
                    EFT.Communications.NotificationManager.DisplayMessageNotification("DuckCam: " + msg);
                }
                catch (Exception ex)
                {
                    PhotoLog.Verbose("Notification failed: " + ex.Message);
                }
            }
        }

        private bool SessionValid(out string reason)
        {
            reason = null;
            GameWorld world = GameContext.World;
            if (world == null || _player == null || _player.HealthController == null || !_player.HealthController.IsAlive)
            {
                reason = "player or world no longer available";
                return false;
            }
            if (world is HideoutGameWorld)
            {
                if (!DuckCamConfig.AllowInHideout.Value)
                {
                    reason = "Hideout sessions disabled";
                    return false;
                }
                return true;
            }
            if (GameContext.Game == null || GameContext.Game.Status != GameStatus.Started)
            {
                reason = "raid no longer running";
                return false;
            }
            return true;
        }

        private void TryStart(bool frozen)
        {
            if (!DuckCamConfig.GuideDone.Value)
            {
                frozen = true;
                _guidePending = true;
            }
            _player = GameContext.MainPlayer;
            if (!SessionValid(out string reason))
            {
                LastMessage = "DuckCam unavailable: " + reason + ".";
                PhotoLog.Info(LastMessage);
                _player = null;
                return;
            }
            _enterRoutine = StartCoroutine(EnterRoutine(frozen));
        }

        private IEnumerator EnterRoutine(bool frozen)
        {
            bool completed = false;
            State = DuckCamState.Entering;
            StartedLive = !frozen;
            _everLive = !frozen;
            Frozen = false;
            _gameSpeedOn = false;
            _sessionBaseTimeScale = Time.timeScale;
            _freeze.Reset();
            LastExitFailures = new List<string>();
            LastSnapshotDiff = new List<string>();

            try
            {
                State = DuckCamState.Snapshotting;
                if (!Step("player snapshot", () => _snapshot = PlayerStateSnapshot.Capture(_player))) yield break;
                if (!Step("cursor", RegisterCursorRestore)) yield break;
                if (!Step("input ownership", RegisterInputOwnershipRestore)) yield break;

                State = DuckCamState.Freezing;
                if (frozen)
                {
                    if (!Step("freeze (gameplay layers)", () => FreezeGameplayLayers(_freezeStack))) yield break;
                }
                else if (DuckCamConfig.ProtectPlayerWhileUnsnapped.Value)
                {
                    if (!Step("live damage protection", () => _freeze.EnableDamageBlock(_session, "live"))) yield break;
                }

                if (DuckCamConfig.SwitchToThirdPerson.Value)
                {
                    if (!Step("third-person view", SwitchToThirdPerson)) yield break;
                    if (frozen)
                    {
                        int frames = DuckCamConfig.SettleFrames.Value;
                        for (int i = 0; i < frames; i++)
                        {
                            yield return null;
                            if (!SessionValid(out string why))
                            {
                                PhotoLog.Warn("Session became invalid while entering: " + why);
                                yield break;
                            }
                        }
                    }
                }

                if (frozen)
                {
                    if (!Step("freeze (world)", () => { _freeze.FreezeWorld(_freezeStack); TimeOfDay.Hold(_freezeStack); HoldAnimators(_freezeStack); })) yield break;
                    Frozen = true;
                }

                string cameraError = null;
                if (!Step("camera ownership", () =>
                    {
                        if (!Camera.Acquire(_player, _session, out cameraError))
                        {
                            throw new InvalidOperationException(cameraError);
                        }
                    }))
                {
                    yield break;
                }

                TimeOfDay.ResetSession();

                _lodHandle = null;

                Actors.ResetSession();
                Weather.ResetSession();
                Animation.GearRackPoseLibrary.Refresh();
                Animation.BodyClipLibrary.Refresh(Player);
                Step("effects discovery", () =>
                {
                    Effects.Probe(Camera.Camera, _session);
                    Dof.Probe(Camera.Camera, _session);
                    OverlayLibrary.Refresh();
                });

                if (DuckCamConfig.CameraAnchoredVolumes.Value)
                {
                    _culling.RestorePoint = () => _everLive && _player != null ? _player.Transform.position + Vector3.up : (Vector3?)null;
                    Step("camera-anchored culling", () => _culling.Activate(_session));
                }
                if (frozen || DuckCamConfig.HideHudWhenUnsnapped.Value)
                {
                    Step("hide HUD", () => HudController.Hide(_session));
                }

                CameraHasMouse = frozen || DuckCamConfig.CameraMouseOnUnsnap.Value;
                _enteredAt = Time.unscaledTime;
                State = DuckCamState.Active;
                completed = true;
                if (_guidePending)
                {
                    _guidePending = false;
                    UiVisible = true;
                    _ui.OpenGuide();
                }
                LastMessage = frozen ? "DuckCam active (frozen)." : "Camera unsnapped (live).";
                PhotoLog.Info((frozen ? "DuckCam entered (frozen)" : "Camera unsnapped (live)") + ". Layers: " + string.Join("; ", Describe(_freeze.LayerStatus)));
            }
            finally
            {
                _enterRoutine = null;
                if (!completed)
                {
                    PhotoLog.Warn("Enter failed, restoring.");
                    RestoreAll("enter aborted");
                }
            }
        }

        private void FreezeGameplayLayers(RestoreStack stack)
        {
            _freeze.EnableDamageBlock(stack, "freeze");
            _freeze.EnableInputBlock(stack);
            _freeze.FreezeBots(stack);
            _freeze.PauseRaidTimer(stack);
            if (DuckCamConfig.RelaxEmptyHands.Value && _player != null)
            {
                _relaxedArms.Enable(_player);
                stack.Push("relaxed arms", _relaxedArms.Disable);
            }
        }

        private readonly RelaxedArms _relaxedArms = new RelaxedArms();

        public void SetFrozen(bool frozen)
        {
            if (State != DuckCamState.Active || frozen == Frozen)
            {
                return;
            }

            if (frozen)
            {
                if (Step("freeze", () =>
                    {
                        FreezeGameplayLayers(_freezeStack);
                        _freeze.FreezeWorld(_freezeStack);
                        HoldAnimators(_freezeStack);
                        TimeOfDay.Hold(_freezeStack);
                    }))
                {
                    Frozen = true;
                    CameraHasMouse = true;
                    LastMessage = "Scene frozen.";
                }
                else
                {
                    UnwindFreeze();
                }
                return;
            }

            UnwindFreeze();
            _everLive = true;
            Actors.RemoveAll();
            LastMessage = "Scene live.";
        }

        private void UnwindFreeze()
        {
            List<string> failures = _freezeStack.UnwindAll();
            LastExitFailures.AddRange(failures);
            Frozen = false;
            _freeze.MarkUnfrozen();
            _dilation.Reapply();
        }

        private bool Step(string name, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex)
            {
                LastMessage = $"DuckCam step failed ({name}): {ex.Message}";
                PhotoLog.Error($"Step '{name}' failed: {ex}");
                return false;
            }
        }

        private void RegisterCursorRestore()
        {
            _cursorShown = null;
            _session.Push("cursor", () =>
            {
                GameCursor(false);
                _cursorShown = null;
            });
        }

        private void RegisterInputOwnershipRestore()
        {
            _session.Push("live input ownership", () =>
            {
                FreezeFlags.PlayerLookBlocked = false;
                FreezeFlags.PlayerCommandsBlocked = false;
                FreezeFlags.PlayerMoveBlocked = false;
                InputContext.Reset();
            });
        }

        private void UpdateInputOwnership()
        {
            bool live = Live;
            FreezeFlags.PlayerLookBlocked = live && (CameraHasMouse || UiVisible);
            FreezeFlags.PlayerCommandsBlocked = live && ((CameraHasMouse && DuckCamConfig.BlockPlayerCommandsWithMouse.Value) || UiVisible);
            FreezeFlags.PlayerMoveBlocked = live && (UiVisible || CameraHasMouse);
        }

        private void SwitchToThirdPerson()
        {
            Player player = _player;
            EPointOfView previous = player.PointOfView;
            if (previous == EPointOfView.ThirdPerson)
            {
                return;
            }
            _session.Push("point of view", () =>
            {
                if (player != null && player.HealthController != null && player.HealthController.IsAlive)
                {
                    player.PointOfView = previous;
                }
            });
            player.PointOfView = EPointOfView.ThirdPerson;
        }

        public void ResetToDefaults(bool clearScene)
        {
            if (State != DuckCamState.Active)
            {
                return;
            }
            var failed = new List<string>();
            void Run(string name, Action action)
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    failed.Add(name);
                    PhotoLog.Warn($"Reset of {name} failed: {ex.Message}");
                }
            }

            Run("paths", () => { Paths.StopAll(); Recording.StopPlayback(); });
            Run("camera", Camera.ResetLens);
            Run("effects", Effects.ResetAll);
            Run("depth of field", () => { Dof.ResetSettings(); DofRender.ResetSettings(); });
            Run("overlays", () => { Overlays.Clear(); Overlays.FrameOpacity = 1f; });
            Run("decorations", Decor.Reset);
            Run("frame", () =>
            {
                ResetEntry(DuckCamConfig.Guides);
                ResetEntry(DuckCamConfig.GuideOpacity);
                ResetEntry(DuckCamConfig.HorizonLine);
                ResetEntry(DuckCamConfig.FrameAspectRatio);
                ResetEntry(DuckCamConfig.LetterboxOpacity);
                ResetEntry(DuckCamConfig.CropToFrame);
            });
            Run("flash", Lights.ResetFlash);
            Run("time of day", TimeOfDay.Revert);
            Run("weather", Weather.Revert);
            Run("game speed", () => SetGameSpeedEnabled(false));
            Run("LOD boost", () => SetLodBoost(false));
            if (clearScene)
            {
                Run("characters", () => { Actors.RemoveAll(); Actors.SetSurroundingHidden(false, _player, _session); });
                Run("lights", Lights.RemoveAll);
                Run("spawned bots", BotSpawns.DespawnSessionBots);
            }

            LastMessage = failed.Count == 0
                ? (clearScene ? "Everything reset to default and the scene cleared." : "Everything reset to default.")
                : "Reset to default, except: " + string.Join(", ", failed) + " (see log).";
            PhotoLog.Info(LastMessage);
        }

        private static void ResetEntry<T>(ConfigEntry<T> entry)
        {
            entry.Value = (T)entry.DefaultValue;
        }

        private bool _uiBeforeVideo;

        public void ToggleVideo()
        {
            if (State != DuckCamState.Active)
            {
                return;
            }
            if (Video.Recording)
            {
                Video.Stop();
                PhotoTime.FixedStep = 0f;
                UiVisible = _uiBeforeVideo;
                LastMessage = "Recording stopped; finishing the video...";
                return;
            }
            if (!Video.Start(this, out string error))
            {
                LastMessage = "Can't record: " + error;
                return;
            }
            PhotoTime.FixedStep = Video.FrameStep;
            _uiBeforeVideo = UiVisible;
            UiVisible = false;
            LastMessage = $"Recording. Press {DuckCamConfig.VideoRecordKey.Value} to stop.";
        }

        private void StopVideo(bool raidEnding)
        {
            if (!Video.Recording && !Video.Finishing)
            {
                return;
            }
            if (raidEnding)
            {
                Video.Abort();
            }
            else if (Video.Recording)
            {
                Video.Stop();
            }
            PhotoTime.FixedStep = 0f;
        }

        private const float ExitConfirmSeconds = 5f;
        private float _exitConfirmUntil = -1f;
        private string _exitConfirmReason;

        public bool ExitConfirmOpen => _exitConfirmUntil > 0f && Time.unscaledTime < _exitConfirmUntil;

        public void RequestExit(string reason)
        {
            if (State != DuckCamState.Active)
            {
                return;
            }
            if (!DuckCamConfig.ConfirmExit.Value || ExitConfirmOpen)
            {
                CancelExitConfirm();
                Exit(reason);
                return;
            }
            _exitConfirmReason = reason;
            _exitConfirmUntil = Time.unscaledTime + ExitConfirmSeconds;
        }

        public void CancelExitConfirm()
        {
            _exitConfirmUntil = -1f;
            _exitConfirmReason = null;
        }

        private bool TickExitConfirm()
        {
            if (_exitConfirmUntil <= 0f)
            {
                return false;
            }
            if (Time.unscaledTime >= _exitConfirmUntil || State != DuckCamState.Active)
            {
                CancelExitConfirm();
                return false;
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                string reason = _exitConfirmReason ?? "user";
                CancelExitConfirm();
                Exit(reason);
                return true;
            }
            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                CancelExitConfirm();
                return true;
            }
            return false;
        }

        private void DrawExitConfirm()
        {
            if (!ExitConfirmOpen)
            {
                return;
            }
            HudTheme.Ensure();
            float w = 460f;
            float h = 170f;
            var box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            if (Event.current.type == EventType.Repaint)
            {
                HudTheme.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.35f));
                HudTheme.Fill(box, HudTheme.P.TabFill);
                HudTheme.Border(box, HudTheme.P.Highlight, 2f);
            }
            GUI.Label(new Rect(box.x + 20f, box.y + 14f, w - 40f, 34f), Locale.T("Leave DuckCam?"), HudTheme.Title);
            string keys = $"{DuckCamConfig.UiExit.Value} / {DuckCamConfig.ToggleKey.Value} / Enter";
            GUI.Label(new Rect(box.x + 20f, box.y + 52f, w - 40f, 44f),
                Locale.T("The scene unfreezes and every DuckCam change is undone. Press the key again or Enter to leave, Backspace to stay.") + "\n" + keys,
                HudTheme.Note);
            float bw = (w - 60f) * 0.5f;
            if (UI.SpawnerWindow.Button(new Rect(box.x + 20f, box.yMax - 50f, bw, 34f), "LEAVE", true))
            {
                string reason = _exitConfirmReason ?? "user";
                CancelExitConfirm();
                Exit(reason);
            }
            else if (UI.SpawnerWindow.Button(new Rect(box.x + 40f + bw, box.yMax - 50f, bw, 34f), "STAY", false))
            {
                CancelExitConfirm();
            }
        }

        public void Exit(string reason)
        {
            if (State != DuckCamState.Active)
            {
                return;
            }
            RestoreAll(reason);
        }

        public void ForceExit(string reason)
        {
            if (State == DuckCamState.Inactive && _session.PendingCount == 0 && _freezeStack.PendingCount == 0)
            {
                return;
            }
            if (_enterRoutine != null)
            {
                StopCoroutine(_enterRoutine);
                _enterRoutine = null;
            }
            RestoreAll(reason);
        }

        private void RestoreAll(string reason)
        {
            if (State == DuckCamState.Restoring)
            {
                return;
            }

            State = DuckCamState.Exiting;
            PhotoLog.Info("Leaving DuckCam: " + reason);
            if (_dofRoutine != null)
            {
                StopCoroutine(_dofRoutine);
                _dofRoutine = null;
            }
            StopVideo(raidEnding: false);
            DofRender.Abort(Camera, _screenshots);
            Paths.StopAll();
            Recording.StopPlayback();
            Recording.End();

            try
            {
                _ui.Spawner.Close();
                _ui.Addons.Close();
                _ui.Gallery.Close();
                _ui.PoseEditor.Close();
                _ui.Timeline.Close();
                BotSpawns.DespawnSessionBots();
            }
            catch (Exception ex)
            {
                PhotoLog.Warn("Bot spawner cleanup failed: " + ex.Message);
            }

            State = DuckCamState.Restoring;
            LastExitFailures = _freezeStack.UnwindAll();
            LastExitFailures.AddRange(_session.UnwindAll());
            Actors.ResetSession();
            Weapons.ResetSession();
            Frozen = false;
            _gameSpeedOn = false;

            FreezeFlags.ClearAll();
            if (Time.timeScale != _sessionBaseTimeScale)
            {
                PhotoLog.Warn($"timeScale {Time.timeScale} after restore; forcing {_sessionBaseTimeScale}.");
                Time.timeScale = _sessionBaseTimeScale > 0f ? _sessionBaseTimeScale : 1f;
            }

            try
            {
                if (_snapshot != null && !_everLive)
                {
                    LastSnapshotDiff = _snapshot.Diff(_player);
                    if (LastSnapshotDiff.Count > 0)
                    {
                        PhotoLog.Warn("State check found differences: " + string.Join("; ", LastSnapshotDiff));
                    }
                }
            }
            catch (Exception ex)
            {
                PhotoLog.Warn("State check failed: " + ex.Message);
            }

            _snapshot = null;
            _pathVisuals.Destroy();
            LastMessage = LastExitFailures.Count == 0 ? "DuckCam exited cleanly." : $"DuckCam exited with {LastExitFailures.Count} restore failure(s); see log.";
            State = DuckCamState.Inactive;
        }

        private bool HandleHotkeys()
        {
            bool menu = InputContext.MenuActive;
            bool gameBlocked = InputContext.GameKeysBlocked;
            bool aliases = DuckCamConfig.WasdAlias.Value && FreezeFlags.InputBlocked && !menu;

            if (DuckCamConfig.VideoRecordKey.Value.IsDown())
            {
                ToggleVideo();
                return false;
            }

            if (DuckCamConfig.UiExit.Value.IsDown() && _ui.Spawner.Open)
            {
                _ui.Spawner.Back();
                return false;
            }
            if (DuckCamConfig.UiExit.Value.IsDown() && _ui.Addons.Open)
            {
                _ui.Addons.Back();
                return false;
            }
            if (DuckCamConfig.UiExit.Value.IsDown() && _ui.Gallery.Open)
            {
                _ui.Gallery.Back();
                return false;
            }
            if (DuckCamConfig.UiExit.Value.IsDown() && _ui.PoseEditor.Open)
            {
                _ui.PoseEditor.Back();
                return false;
            }
            if (DuckCamConfig.TimelineKey.Value.IsDown())
            {
                _ui.Timeline.Toggle();
                UiVisible = true;
                return false;
            }
            if (DuckCamConfig.TimelinePlayKey.Value.IsDown() && _ui.Timeline.Open)
            {
                _ui.Timeline.PlayPause();
                return false;
            }
            if (DuckCamConfig.PoseEditorKey.Value.IsDown())
            {
                if (_ui.PoseEditor.Open)
                {
                    _ui.PoseEditor.Close();
                }
                else
                {
                    _ui.PoseEditor.Show(null);
                    UiVisible = true;
                }
                return false;
            }
            if (DuckCamConfig.PoseModeKey.Value.IsDown())
            {
                if (_ui.PoseEditor.Open && UiVisible)
                {
                    _ui.PoseEditor.Close();
                    LastMessage = "Left Pose Mode.";
                }
                else
                {
                    UiVisible = true;
                    _ui.PoseEditor.Show(null);
                }
                return false;
            }
            if (PoseMode)
            {
                if (DuckCamConfig.UiToggleKey.Value.IsDown())
                {
                    UiVisible = false;
                    InputContext.LeaveField();
                    PhotoLog.Info("UI hidden in Pose Mode (" + DuckCamConfig.UiToggleKey.Value + ").");
                }
                return false;
            }
            if (DuckCamConfig.UiExit.Value.IsDown() && (menu || FreezeFlags.InputBlocked))
            {
                RequestExit("Esc");
                return true;
            }
            if (DuckCamConfig.UiToggleKey.Value.IsDown()
                || (DuckCamConfig.UiHide.Value.IsDown() && (menu || gameBlocked)))
            {
                UiVisible = !UiVisible;
                InputContext.LeaveField();
                PhotoLog.Info($"UI {(UiVisible ? "shown" : "hidden")} ({(DuckCamConfig.UiToggleKey.Value.IsDown() ? DuckCamConfig.UiToggleKey.Value.ToString() : DuckCamConfig.UiHide.Value.ToString())}).");
            }
            else if (Input.GetKeyDown(DuckCamConfig.UiToggleKey.Value.MainKey))
            {
                PhotoLog.Info($"{DuckCamConfig.UiToggleKey.Value} ignored, another key was held.");
            }
            if (DuckCamConfig.ScreenshotKey.Value.IsDown() || (menu && DuckCamConfig.UiPhoto.Value.IsDown()))
            {
                TakeScreenshot();
            }
            if (DuckCamConfig.GalleryKey.Value.IsDown() && !PoseMode && !_ui.PoseEditor.Open && !_ui.Spawner.Open && !_ui.Addons.Open)
            {
                if (_ui.Gallery.Open)
                {
                    _ui.Gallery.Close();
                }
                else
                {
                    UiVisible = true;
                    _ui.Gallery.Show();
                }
            }
            if (aliases && Input.GetKeyDown(KeyCode.F)) Camera.ToggleOrbit();
            if (aliases && Input.GetKeyDown(KeyCode.T)) Camera.ToggleTripod();

            if (DuckCamConfig.CameraMouseKey.Value.IsDown())
            {
                if (Frozen)
                {
                    LastMessage = "The mouse always flies the camera while frozen.";
                }
                else
                {
                    CameraHasMouse = !CameraHasMouse;
                    LastMessage = CameraHasMouse ? "Mouse controls the camera." : "Mouse controls your PMC.";
                }
            }
            if (DuckCamConfig.ChangeGameSpeedKey.Value.IsDown())
            {
                ToggleGameSpeed();
            }

            if (DuckCamConfig.AttachKey.Value.IsDown())
            {
                Camera.Attach(out string message);
                LastMessage = message;
            }
            if (DuckCamConfig.DetachKey.Value.IsDown())
            {
                Camera.DetachKeepingPose();
                LastMessage = "Detached.";
            }

            if (DuckCamConfig.MemoryAdd.Value.IsDown())
            {
                Memory.Add(Camera.CurrentPose);
                LastMessage = $"Memory position {Memory.Count} stored.";
            }
            if (DuckCamConfig.MemoryAdvance.Value.IsDown() && Memory.Advance(out CameraPose memoryPose))
            {
                Camera.Mode = CameraMode.Free;
                Camera.SetPose(memoryPose);
                LastMessage = $"Memory position {Memory.Index + 1}/{Memory.Count}.";
            }
            if (DuckCamConfig.MemoryClear.Value.IsDown())
            {
                Memory.Clear();
                LastMessage = "Camera memory cleared.";
            }

            if (DuckCamConfig.CreateKeyframe.Value.IsDown()) AddKeyframe();
            if (DuckCamConfig.PathDeleteNode.Value.IsDown()) DeleteKeyframe();
            if (DuckCamConfig.BeginRecording.Value.IsDown())
            {
                if (Recording.Recording) EndRecording();
                else BeginRecording(false);
            }
            if (DuckCamConfig.ContinueRecording.Value.IsDown()) BeginRecording(true);
            if (DuckCamConfig.EndRecording.Value.IsDown()) EndRecording();
            if (DuckCamConfig.PlayRecording.Value.IsDown())
            {
                if (Recording.Playing)
                {
                    Recording.StopPlayback();
                    LastMessage = "Recording stopped.";
                }
                else
                {
                    PlayRecording();
                }
            }

            if (DuckCamConfig.PathPrevNode.Value.IsDown()) SelectKeyframe(Path.SelectedNode - 1);
            if (DuckCamConfig.PathNextNode.Value.IsDown()) SelectKeyframe(Path.SelectedNode + 1);
            if (DuckCamConfig.PathFirstNode.Value.IsDown()) SelectKeyframe(0);
            if (DuckCamConfig.PathLastNode.Value.IsDown()) SelectKeyframe(Path.Keys.Count - 1);
            if (DuckCamConfig.PathReplaceNode.Value.IsDown()) ReplaceKeyframe();
            if (DuckCamConfig.PathInsertBefore.Value.IsDown()) InsertKeyframe(before: true);
            if (DuckCamConfig.PathInsertAfter.Value.IsDown()) InsertKeyframe(before: false);
            if (DuckCamConfig.FrameStepKey.Value.IsDown()) StepFrames(FrameStepCount);

            int digit = AltDigitDown();
            if (digit >= 0)
            {
                if (Path.Playing && DuckCamConfig.PathCutKeys.Value)
                {
                    if (Paths.CutTo(digit))
                    {
                        LastMessage = "Cut to " + Path.Name;
                    }
                }
                else
                {
                    HandleBookmark(digit);
                }
            }
            return false;
        }

        public bool PoseMode => State == DuckCamState.Active && UiVisible && _ui != null && _ui.PoseEditor.Open;

        public bool PointerOverUi => UiVisible && _ui != null && _ui.PointerOverUi;

        private static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        private static bool Ctrl => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        private static int AltDigitDown()
        {
            if (!(Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) || Input.GetKey(KeyCode.AltGr)))
            {
                return -1;
            }
            for (int i = 0; i < 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    return i;
                }
            }
            return -1;
        }

        public void DeleteKeyframe()
        {
            int selected = Path.SelectedNode;
            if (selected < 0 || selected >= Path.Keys.Count)
            {
                LastMessage = "Select a keyframe first.";
                return;
            }
            Path.RemoveAt(selected);
            LastMessage = $"Keyframe {selected} deleted.";
        }

        public void InsertKeyframe(bool before)
        {
            var key = new PathKeyframe { Position = Camera.Position, Rotation = Camera.Rotation, Fov = Camera.Fov };
            int selected = Path.SelectedNode;
            if (selected < 0 || selected >= Path.Keys.Count)
            {
                Path.Add(key);
                Path.SelectedNode = Path.Keys.Count - 1;
            }
            else
            {
                int index = before ? selected : selected + 1;
                Path.InsertAt(index, key);
                Path.SelectedNode = index;
            }
            LastMessage = $"Keyframe {Path.SelectedNode} inserted.";
        }

        public void ReplaceKeyframe()
        {
            int selected = Path.SelectedNode;
            if (selected < 0 || selected >= Path.Keys.Count)
            {
                LastMessage = "Select a keyframe first.";
                return;
            }
            Path.Replace(selected, new PathKeyframe { Position = Camera.Position, Rotation = Camera.Rotation, Fov = Camera.Fov });
            LastMessage = $"Keyframe {selected} replaced.";
        }

        public int FrameStepCount { get; set; } = 1;
        public bool Stepping { get; private set; }

        public void StepFrames(int frames)
        {
            if (State != DuckCamState.Active || !Frozen || Stepping || DofRender.Rendering)
            {
                return;
            }
            StartCoroutine(StepRoutine(Mathf.Clamp(frames, 1, 600)));
        }

        private readonly Dictionary<Animator, float> _heldAnimators = new Dictionary<Animator, float>();

        private void HoldAnimators(RestoreStack stack)
        {
            _heldAnimators.Clear();
            GameWorld world = GameContext.World;
            if (world?.AllAlivePlayersList == null)
            {
                return;
            }
            foreach (Player p in world.AllAlivePlayersList)
            {
                if (p == null)
                {
                    continue;
                }
                foreach (Animator a in p.GetComponentsInChildren<Animator>(true))
                {
                    if (a != null && !_heldAnimators.ContainsKey(a))
                    {
                        _heldAnimators[a] = a.speed;
                        a.speed = 0f;
                    }
                }
            }
            stack.Push("character animators", () => SetHeldAnimators(release: true));

            HoldWorldPoses();
            stack.Push("character poses", _worldPoses.Clear);
        }

        private readonly PoseHold _worldPoses = new PoseHold(pinRoot: false);

        private void HoldWorldPoses()
        {
            _worldPoses.Clear();
            GameWorld world = GameContext.World;
            if (world?.AllAlivePlayersList == null)
            {
                return;
            }
            foreach (Player p in world.AllAlivePlayersList)
            {
                _worldPoses.Add(p);
            }
        }

        private void SetHeldAnimators(bool release)
        {
            foreach (KeyValuePair<Animator, float> kv in _heldAnimators)
            {
                if (kv.Key != null)
                {
                    kv.Key.speed = release ? kv.Value : 0f;
                }
            }
            if (release)
            {
                _heldAnimators.Clear();
            }
        }

        private IEnumerator StepRoutine(int frames)
        {
            Stepping = true;
            bool posesHeld = _worldPoses.Count > 0;
            _worldPoses.Clear();
            var held = new Dictionary<Animator, float>(_heldAnimators);
            foreach (KeyValuePair<Animator, float> kv in held)
            {
                if (kv.Key != null)
                {
                    kv.Key.speed = kv.Value;
                }
            }
            float scale = _dilation.Active ? _dilation.Scale : (_sessionBaseTimeScale > 0f ? _sessionBaseTimeScale : 1f);
            FreezeFlags.WorldTickFrozen = false;
            Time.timeScale = scale;
            PhysicsExtensions.Simulation.UpdateEnabled = true;
            try
            {
                for (int i = 0; i < frames; i++)
                {
                    yield return null;
                }
            }
            finally
            {
                if (State == DuckCamState.Active && Frozen)
                {
                    FreezeFlags.WorldTickFrozen = true;
                    Time.timeScale = 0f;
                    PhysicsExtensions.Simulation.UpdateEnabled = false;
                    if (_heldAnimators.Count > 0)
                    {
                        SetHeldAnimators(release: false);
                    }
                    if (posesHeld)
                    {
                        HoldWorldPoses();
                    }
                }
                Stepping = false;
                LastMessage = $"Stepped {frames} frame(s).";
            }
        }

        private RestoreHandle _lodHandle;
        private float _baseLodBias = 1f;
        private float _lodBoostFactor = 2f;

        public bool LodBoost => _lodHandle != null && !_lodHandle.Done;

        public float LodBoostFactor
        {
            get => _lodBoostFactor;
            set
            {
                _lodBoostFactor = Mathf.Clamp(value, 1f, 8f);
                if (LodBoost)
                {
                    QualitySettings.lodBias = _baseLodBias * _lodBoostFactor;
                }
            }
        }

        public void SetLodBoost(bool on)
        {
            if (on == LodBoost || State != DuckCamState.Active)
            {
                return;
            }
            if (!on)
            {
                _lodHandle.Run();
                _lodHandle = null;
                return;
            }
            _baseLodBias = QualitySettings.lodBias;
            int maxLod = QualitySettings.maximumLODLevel;
            float baseBias = _baseLodBias;
            _lodHandle = _session.Push("LOD boost", () =>
            {
                QualitySettings.lodBias = baseBias;
                QualitySettings.maximumLODLevel = maxLod;
            });
            QualitySettings.lodBias = _baseLodBias * _lodBoostFactor;
            QualitySettings.maximumLODLevel = 0;
        }

        private void HandleBookmark(int i)
        {
            if (!DuckCamConfig.BookmarkKeys.Value || i >= CameraBookmarks.Count)
            {
                return;
            }
            if (Shift || Ctrl || Input.GetKey(KeyCode.AltGr))
            {
                Bookmarks.Store(i, Camera.CurrentPose);
                LastMessage = $"Bookmark {i + 1} stored.";
            }
            else if (Bookmarks.TryGet(i, out CameraPose pose))
            {
                Camera.Mode = CameraMode.Free;
                Camera.SetPose(pose);
                LastMessage = $"Bookmark {i + 1} recalled.";
            }
            else
            {
                LastMessage = $"Bookmark {i + 1} is empty (Alt+Shift+{i + 1} stores it).";
            }
        }

        public void ToggleGameSpeed()
        {
            SetGameSpeedEnabled(!_gameSpeedOn);
        }

        public bool GameSpeedOn => _gameSpeedOn;

        public void SetGameSpeedEnabled(bool on)
        {
            if (State != DuckCamState.Active)
            {
                return;
            }
            _gameSpeedOn = on;
            if (on)
            {
                _dilation.Set(DuckCamConfig.GameSpeedPercent.Value / 100f, _sessionBaseTimeScale, _session);
                LastMessage = $"Game speed {DuckCamConfig.GameSpeedPercent.Value}%" + (Frozen ? " (applies when unfrozen)." : ".");
            }
            else
            {
                _dilation.Clear();
                LastMessage = "Game speed 100%.";
            }
        }

        public void AddKeyframe()
        {
            Path.Add(new PathKeyframe { Position = Camera.Position, Rotation = Camera.Rotation, Fov = Camera.Fov });
            Path.SelectedNode = Path.Keys.Count - 1;
            LastMessage = $"Keyframe {Path.Keys.Count - 1} added.";
        }

        public void BeginRecording(bool continueTake)
        {
            Path.Stop();
            if (continueTake)
            {
                Recording.Continue(Camera.Position, Camera.Rotation, Camera.EffectiveFov);
            }
            else
            {
                Recording.Begin(Camera.Position, Camera.Rotation, Camera.EffectiveFov);
            }
            LastMessage = continueTake ? "Recording (continued)..." : "Recording camera...";
        }

        public void EndRecording()
        {
            if (Recording.Recording)
            {
                Recording.End();
                LastMessage = $"Recording ended ({Recording.Duration:F1}s, {Recording.Samples.Count} samples).";
            }
        }

        public void PlayRecording()
        {
            Path.Stop();
            Recording.Play();
            LastMessage = Recording.Playing ? "Playing recording." : "Nothing recorded yet.";
        }

        public void PlayPath()
        {
            Recording.StopPlayback();
            Path.Play();
            LastMessage = Path.Playing ? "Playing path." : "A path needs at least 2 keyframes.";
        }

        public void SelectKeyframe(int index)
        {
            if (index < 0 || index >= Path.Keys.Count)
            {
                return;
            }
            PathKeyframe k = Path.Keys[index];
            Vector3 e = k.Rotation.eulerAngles;
            Path.Stop();
            Path.SelectedNode = index;
            Camera.Mode = CameraMode.Free;
            Camera.SetPose(new CameraPose { Position = k.Position, Yaw = e.y, Pitch = PhotoCameraController.NormalizePitch(e.x), Roll = PhotoCameraController.NormalizePitch(e.z), Fov = k.Fov });
        }

        public void SetBsgCulling(bool disabled)
        {
            if (disabled == FreezeFlags.BsgCullingDisabled || State != DuckCamState.Active)
            {
                return;
            }
            if (disabled)
            {
                CullingManager manager = CullingManager.Instance;
                if (manager == null)
                {
                    LastMessage = "BSG CullingManager not present on this map.";
                    return;
                }
                FreezeFlags.BsgCullingDisabled = true;
                manager.ForceEnable(true);
                _session.Push("BSG culling off", () => FreezeFlags.BsgCullingDisabled = false);
                LastMessage = "BSG object culling off (all culled objects visible; costs performance).";
            }
            else
            {
                FreezeFlags.BsgCullingDisabled = false;
                LastMessage = "BSG object culling back on.";
            }
        }

        public void TakeScreenshot()
        {
            if (State == DuckCamState.Active && !_screenshots.Capturing)
            {
                StartCoroutine(ScreenshotRoutine());
            }
        }

        private IEnumerator ScreenshotRoutine()
        {
            string before = _screenshots.LastPath;
            yield return _screenshots.Capture(Camera, Actors, Overlays, Decor);
            if (_screenshots.LastError != null)
            {
                LastMessage = "Photo failed: " + _screenshots.LastError;
            }
            else if (_screenshots.LastPath != before)
            {
                LastMessage = "Photo saved: " + System.IO.Path.GetFileName(_screenshots.LastPath);
                _ui.Flash();
            }
        }

        public void StartDofRender()
        {
            if (State != DuckCamState.Active || DofRender.Rendering || _screenshots.Capturing)
            {
                return;
            }
            if (!Frozen)
            {
                LastMessage = "The DoF render needs a frozen scene (everything must hold still).";
                return;
            }
            Path.Stop();
            Recording.StopPlayback();
            _dofRoutine = StartCoroutine(RunDofRender());
        }

        private IEnumerator RunDofRender()
        {
            yield return DofRender.Render(Camera, Dof, _screenshots, () => Overlays, () => Actors);
            _dofRoutine = null;
            LastMessage = "DoF render: " + DofRender.Status;
        }

        private bool CanStageActors()
        {
            if (State != DuckCamState.Active)
            {
                return false;
            }
            if (!Frozen)
            {
                LastMessage = "Characters need a frozen scene (" + DuckCamConfig.ToggleKey.Value + ").";
                return false;
            }
            return true;
        }

        public void AddPmcActor()
        {
            if (CanStageActors())
            {
                Actors.AddPmc(_player, _session);
            }
        }

        public void AddActorCopy(Player source, bool atCamera, bool hideOriginal)
        {
            if (!CanStageActors())
            {
                return;
            }
            if (source == _player)
            {
                Actors.AddPmc(_player, _session);
                return;
            }
            if (Actors.AddCopy(source, atCamera, Camera.Camera.transform, hideOriginal, _session) == null)
            {
                LastMessage = $"Actor limit ({Actors.Limit}) reached.";
            }
        }

        public void AddSavedActor(string name)
        {
            if (!CanStageActors())
            {
                return;
            }
            SavedActor saved = SavedActors.Load(name, out string error);
            if (saved == null || Actors.AddSaved(saved, Camera.Camera.transform, ActorManager.LayerOf(_player), _session, out error) == null)
            {
                LastMessage = $"Could not add '{name}': {error}";
            }
        }

        public void SimulateFailure()
        {
            try
            {
                throw new InvalidOperationException("simulated DuckCam failure (Diagnostics button)");
            }
            catch (Exception ex)
            {
                PhotoLog.Warn(ex.Message);
                ForceExit("simulated failure");
            }
        }

        private static IEnumerable<string> Describe(Dictionary<string, string> status)
        {
            foreach (KeyValuePair<string, string> kv in status)
            {
                yield return kv.Key + "=" + kv.Value;
            }
        }
    }
}
