using BepInEx.Configuration;
using UnityEngine;

namespace DuckCam.Core
{
    public enum CameraRadiusProfile
    {
        Radius100m,
        Radius250m,
        Radius500m,
        Radius750m,
        Radius1000m,
        UnlimitedExperimental
    }

    public enum CameraCollisionMode
    {
        Off,
        Soft,
        Strict
    }

    public enum CameraAttachType
    {
        LookAt,
        Parented
    }

    public enum AnchorPoint
    {
        Chest,
        Head,
        Feet
    }

    public enum AnchorTurn
    {
        Heading,
        Full,
        None
    }

    public enum UiSide
    {
        Right,
        Left
    }

    public enum WheelFunction
    {
        FovZoom,
        MoveSpeed
    }

    internal static class DuckCamConfig
    {
        public static ConfigEntry<KeyboardShortcut> ToggleKey;
        public static ConfigEntry<KeyboardShortcut> UiToggleKey;
        public static ConfigEntry<KeyboardShortcut> ScreenshotKey;
        public static ConfigEntry<KeyboardShortcut> GalleryKey;
        public static ConfigEntry<KeyboardShortcut> BotFreezeKey;
        public static ConfigEntry<bool> ProtectFrozenBots;
        public static ConfigEntry<bool> BookmarkKeys;
        public static ConfigEntry<bool> WasdAlias;
        public static ConfigEntry<PhotoLogLevel> LogLevel;

        public static ConfigEntry<bool> SwitchToThirdPerson;
        public static ConfigEntry<bool> RelaxEmptyHands;
        public static ConfigEntry<int> SettleFrames;
        public static ConfigEntry<bool> PauseAudio;
        public static ConfigEntry<bool> AllowInHideout;

        public static ConfigEntry<float> CameraFov;
        public static ConfigEntry<float> MoveSpeed;
        public static ConfigEntry<float> FastMultiplier;
        public static ConfigEntry<float> SlowMultiplier;
        public static ConfigEntry<float> LookSensitivity;
        public static ConfigEntry<float> RollSpeed;
        public static ConfigEntry<float> MovementSmoothing;
        public static ConfigEntry<float> MouseSmoothing;
        public static ConfigEntry<WheelFunction> Wheel;
        public static ConfigEntry<float> FovZoomSpeed;
        public static ConfigEntry<float> WheelSpeedStep;
        public static ConfigEntry<float> ZoomTime;
        public static ConfigEntry<float> ZoomAmount;
        public static ConfigEntry<CameraRadiusProfile> RadiusProfile;
        public static ConfigEntry<CameraCollisionMode> CollisionMode;
        public static ConfigEntry<float> CollisionRadius;

        public static ConfigEntry<KeyboardShortcut> MoveForward;
        public static ConfigEntry<KeyboardShortcut> MoveBack;
        public static ConfigEntry<KeyboardShortcut> MoveLeft;
        public static ConfigEntry<KeyboardShortcut> MoveRight;
        public static ConfigEntry<KeyboardShortcut> MoveUp;
        public static ConfigEntry<KeyboardShortcut> MoveDown;
        public static ConfigEntry<KeyboardShortcut> MoveFast;
        public static ConfigEntry<KeyboardShortcut> SpeedKey;
        public static ConfigEntry<KeyboardShortcut> ZoomKey;
        public static ConfigEntry<KeyboardShortcut> RollLeft;
        public static ConfigEntry<KeyboardShortcut> RollRight;
        public static ConfigEntry<KeyboardShortcut> ResetRotation;

        public static ConfigEntry<KeyboardShortcut> UnsnapKey;
        public static ConfigEntry<KeyboardShortcut> CameraMouseKey;
        public static ConfigEntry<bool> CameraMouseOnUnsnap;
        public static ConfigEntry<bool> BlockPlayerCommandsWithMouse;
        public static ConfigEntry<bool> ProtectPlayerWhileUnsnapped;
        public static ConfigEntry<bool> HideHudWhenUnsnapped;
        public static ConfigEntry<KeyboardShortcut> ChangeGameSpeedKey;
        public static ConfigEntry<int> GameSpeedPercent;
        public static ConfigEntry<bool> RaidClockFollowsGameSpeed;

        public static ConfigEntry<KeyboardShortcut> AttachKey;
        public static ConfigEntry<KeyboardShortcut> DetachKey;
        public static ConfigEntry<CameraAttachType> AttachType;
        public static ConfigEntry<AnchorPoint> AttachPoint;
        public static ConfigEntry<AnchorTurn> AttachTurn;
        public static ConfigEntry<float> AttachSmoothing;
        public static ConfigEntry<float> AttachRaycastDistance;
        public static ConfigEntry<float> LookAtSmoothing;
        public static ConfigEntry<float> LookAtEscapeThreshold;
        public static ConfigEntry<KeyboardShortcut> MemoryAdd;
        public static ConfigEntry<KeyboardShortcut> MemoryAdvance;
        public static ConfigEntry<KeyboardShortcut> MemoryClear;

        public static ConfigEntry<KeyboardShortcut> CreateKeyframe;
        public static ConfigEntry<KeyboardShortcut> PathDeleteNode;
        public static ConfigEntry<KeyboardShortcut> BeginRecording;
        public static ConfigEntry<KeyboardShortcut> ContinueRecording;
        public static ConfigEntry<KeyboardShortcut> EndRecording;
        public static ConfigEntry<KeyboardShortcut> PlayRecording;
        public static ConfigEntry<bool> PathUsesRealTime;
        public static ConfigEntry<bool> PathConstantSpeed;
        public static ConfigEntry<KeyboardShortcut> PathPrevNode;
        public static ConfigEntry<KeyboardShortcut> PathNextNode;
        public static ConfigEntry<KeyboardShortcut> PathFirstNode;
        public static ConfigEntry<KeyboardShortcut> PathLastNode;
        public static ConfigEntry<KeyboardShortcut> PathReplaceNode;
        public static ConfigEntry<KeyboardShortcut> PathInsertBefore;
        public static ConfigEntry<KeyboardShortcut> PathInsertAfter;
        public static ConfigEntry<bool> PathCutKeys;
        public static ConfigEntry<KeyboardShortcut> FrameStepKey;
        public static ConfigEntry<Rendering.CaptureResolution> PhotoResolution;
        public static ConfigEntry<int> PhotoSupersample;
        public static ConfigEntry<bool> TransparentBackground;
        public static ConfigEntry<Rendering.CutoutSubjects> CutoutSubjects;
        public static ConfigEntry<bool> CutoutKeepScene;

        public static ConfigEntry<bool> CameraAnchoredVolumes;

        public static ConfigEntry<bool> OnlyUnlockedPoses;
        public static ConfigEntry<bool> CopyKeepsStance;
        public static ConfigEntry<int> ActorLimit;
        public static ConfigEntry<string> FavoritePoses;

        public static ConfigEntry<bool> WriteSidecar;
        public static ConfigEntry<KeyboardShortcut> VideoRecordKey;
        public static ConfigEntry<KeyboardShortcut> PoseEditorKey;
        public static ConfigEntry<KeyboardShortcut> PoseModeKey;
        public static ConfigEntry<KeyboardShortcut> TimelineKey;
        public static ConfigEntry<KeyboardShortcut> TimelinePlayKey;
        public static ConfigEntry<string> FfmpegPath;
        public static ConfigEntry<bool> CropToFrame;

        public static ConfigEntry<UiSide> PanelSide;
        public static ConfigEntry<UI.UiPalette> Palette;
        public static ConfigEntry<string> Language;
        public static ConfigEntry<bool> ConfirmExit;
        public static ConfigEntry<string> HiddenTabs;
        public static ConfigEntry<bool> GuideDone;
        public static ConfigFile File { get; private set; }
        public static ConfigEntry<bool> HudDecorations;
        public static ConfigEntry<bool> GuidesWhenUiHidden;
        public static ConfigEntry<KeyboardShortcut> MenuUp;
        public static ConfigEntry<KeyboardShortcut> MenuDown;
        public static ConfigEntry<KeyboardShortcut> MenuLeft;
        public static ConfigEntry<KeyboardShortcut> MenuRight;
        public static ConfigEntry<KeyboardShortcut> MenuSelect;
        public static ConfigEntry<KeyboardShortcut> PrevCategory;
        public static ConfigEntry<KeyboardShortcut> NextCategory;
        public static ConfigEntry<bool> EdgeGlow;

        public static ConfigEntry<KeyboardShortcut> UiCategoryPrev;
        public static ConfigEntry<KeyboardShortcut> UiCategoryNext;
        public static ConfigEntry<KeyboardShortcut> UiOptionUp;
        public static ConfigEntry<KeyboardShortcut> UiOptionDown;
        public static ConfigEntry<KeyboardShortcut> UiValueLeft;
        public static ConfigEntry<KeyboardShortcut> UiValueRight;
        public static ConfigEntry<KeyboardShortcut> UiActivate;
        public static ConfigEntry<KeyboardShortcut> UiPhoto;
        public static ConfigEntry<KeyboardShortcut> UiHide;
        public static ConfigEntry<KeyboardShortcut> UiExit;
        public static ConfigEntry<KeyboardShortcut> UiCamForward;
        public static ConfigEntry<KeyboardShortcut> UiCamBack;
        public static ConfigEntry<KeyboardShortcut> UiCamLeft;
        public static ConfigEntry<KeyboardShortcut> UiCamRight;
        public static ConfigEntry<KeyboardShortcut> UiCamUp;
        public static ConfigEntry<KeyboardShortcut> UiCamDown;
        public static ConfigEntry<float> UiScale;
        public static ConfigEntry<int> PanelRows;
        public static ConfigEntry<bool> ShowKeyHints;
        public static ConfigEntry<Rendering.CompositionGuide> Guides;
        public static ConfigEntry<float> GuideOpacity;
        public static ConfigEntry<bool> HorizonLine;
        public static ConfigEntry<Rendering.FrameAspect> FrameAspectRatio;
        public static ConfigEntry<float> LetterboxOpacity;

        public static void Bind(ConfigFile config)
        {
            File = config;
            const string keys = "01. Keybinds";
            ToggleKey = config.Bind(keys, "Toggle DuckCam (freeze)", new KeyboardShortcut(KeyCode.F6), "Enter/exit frozen DuckCam. During a live (unsnapped) session it freezes/unfreezes the scene instead.");
            UiToggleKey = config.Bind(keys, "Toggle DuckCam UI", new KeyboardShortcut(KeyCode.F11), "Show/hide the workspace (KappaCam used F10 for its menu; set F10 here if you prefer).");
            ScreenshotKey = config.Bind(keys, "Take screenshot", new KeyboardShortcut(KeyCode.P), "Capture a PNG without the DuckCam UI.");
            GalleryKey = config.Bind(keys, "Open gallery", new KeyboardShortcut(KeyCode.G, KeyCode.LeftControl), "Open or close the photo and video gallery.");
            BotFreezeKey = config.Bind(keys, "Freeze bots only", new KeyboardShortcut(KeyCode.F9), "Freeze every bot in place (pose, AI, senses) while you and the rest of the raid keep going. Works with or without a DuckCam session. Press again to release them.");
            ProtectFrozenBots = config.Bind(keys, "Frozen bots are protected", true, "While \"Freeze bots only\" is on, bots take no damage. Off: you can shoot them, but they only react once released if they see you.");
            BookmarkKeys = config.Bind(keys, "Camera bookmarks (Alt+1-9)", true, "Alt+1-9 (top number row) recalls a camera bookmark, Alt+Shift+1-9 or Ctrl+Alt+1-9 stores one. While a path plays, Alt+1-9 cut between paths instead.");
            WasdAlias = config.Bind(keys, "WASD/R/F/T aliases when game input is blocked", true, "While the scene is frozen (game input locked) WASD also moves the camera, R resets roll, F orbits, T toggles tripod.");
            LogLevel = config.Bind(keys, "Log level", PhotoLogLevel.Normal, "Verbose/Debug add detail to BepInEx/LogOutput.log.");

            const string general = "02. General";
            SwitchToThirdPerson = config.Bind(general, "Show own PMC (third person)", true, "Switch the PMC to third-person while a session is active so the full body renders. Restored on exit.");
            RelaxEmptyHands = config.Bind(general, "Relaxed empty hands when frozen", true, "With nothing in your hands, EFT shows your PMC in third person with the arms up as if holding a gun. While frozen, lower the arms beside the body with open hands instead.");
            SettleFrames = config.Bind(general, "Third-person settle frames", 3, new ConfigDescription("Frames the game keeps ticking after the third-person switch before a freeze (input blocked, bots frozen, damage blocked).", new AcceptableValueRange<int>(0, 30)));
            PauseAudio = config.Bind(general, "Pause audio when frozen", false, "Pause all game audio (AudioListener.pause) while the scene is frozen.");
            AllowInHideout = config.Bind(general, "Allow in Hideout (experimental)", true, "Allow sessions in the Hideout (KappaCam 2.4.0 feature). There is no raid clock or AI there.");

            const string cam = "03. Camera";
            CameraFov = config.Bind(cam, "Camera FOV", 75f, new ConfigDescription("FOV the photo camera starts with (0 = keep the game's current FOV).", new AcceptableValueRange<float>(0f, 120f)));
            MoveSpeed = config.Bind(cam, "Camera move speed (m/s)", 10f, new ConfigDescription("Base speed; Speed key + wheel scales it during a session.", new AcceptableValueRange<float>(0.1f, 100f)));
            FastMultiplier = config.Bind(cam, "Fast multiplier", 5f, new ConfigDescription("", new AcceptableValueRange<float>(1f, 50f)));
            SlowMultiplier = config.Bind(cam, "Precision multiplier (Alt)", 0.15f, new ConfigDescription("", new AcceptableValueRange<float>(0.01f, 1f)));
            LookSensitivity = config.Bind(cam, "Camera sensitivity", 2f, new ConfigDescription("", new AcceptableValueRange<float>(0.05f, 20f)));
            RollSpeed = config.Bind(cam, "Roll speed (deg/s)", 45f, new ConfigDescription("", new AcceptableValueRange<float>(1f, 360f)));
            MovementSmoothing = config.Bind(cam, "Movement smoothing", 0.9f, new ConfigDescription("0 = instant. Higher = the camera glides (velocity is eased). KappaCam default feel ~ 0.99.", new AcceptableValueRange<float>(0f, 0.999f)));
            MouseSmoothing = config.Bind(cam, "Mouse smoothing", 0f, new ConfigDescription("0 = raw mouse. Higher = rotation is eased.", new AcceptableValueRange<float>(0f, 0.99f)));
            Wheel = config.Bind(cam, "Mouse wheel", WheelFunction.FovZoom, "What the wheel does in free mode. Holding the Speed key always makes the wheel change move speed. In orbit mode the wheel changes distance.");
            FovZoomSpeed = config.Bind(cam, "Camera FOV zoom speed", 50f, new ConfigDescription("Degrees of FOV per wheel unit.", new AcceptableValueRange<float>(1f, 200f)));
            WheelSpeedStep = config.Bind(cam, "Movement speed mouse wheel step", 1.25f, new ConfigDescription("Speed multiplier per wheel notch.", new AcceptableValueRange<float>(1.01f, 3f)));
            ZoomTime = config.Bind(cam, "Zoom time (s)", 0.3f, new ConfigDescription("Smooth zoom time while the Zoom key is held/released.", new AcceptableValueRange<float>(0.01f, 3f)));
            ZoomAmount = config.Bind(cam, "Zoom amount", 0.8f, new ConfigDescription("Fraction of the FOV removed while the Zoom key is held (0.8: 75° -> 15°).", new AcceptableValueRange<float>(0.05f, 0.95f)));
            RadiusProfile = config.Bind(cam, "Movement radius", CameraRadiusProfile.Radius250m, "Sphere around the PMC the camera may move in. Unlimited is experimental: world streaming and culling may not cover far areas.");
            CollisionMode = config.Bind(cam, "Collision", CameraCollisionMode.Off, "Off = noclip, Soft = slide along surfaces, Strict = stop at surfaces. The camera never pushes objects or characters.");
            CollisionRadius = config.Bind(cam, "Collision radius (m)", 0.2f, new ConfigDescription("", new AcceptableValueRange<float>(0.02f, 1f)));

            const string ck = "04. Camera control keys";
            MoveForward = config.Bind(ck, "Move forward", new KeyboardShortcut(KeyCode.UpArrow));
            MoveBack = config.Bind(ck, "Move back", new KeyboardShortcut(KeyCode.DownArrow));
            MoveLeft = config.Bind(ck, "Move left", new KeyboardShortcut(KeyCode.LeftArrow));
            MoveRight = config.Bind(ck, "Move right", new KeyboardShortcut(KeyCode.RightArrow));
            MoveUp = config.Bind(ck, "Move up", new KeyboardShortcut(KeyCode.Space));
            MoveDown = config.Bind(ck, "Move down", new KeyboardShortcut(KeyCode.LeftControl));
            MoveFast = config.Bind(ck, "Move fast", new KeyboardShortcut(KeyCode.LeftShift));
            SpeedKey = config.Bind(ck, "Speed key", new KeyboardShortcut(KeyCode.Mouse5), "Hold: mouse wheel changes move speed.");
            ZoomKey = config.Bind(ck, "Zoom key", new KeyboardShortcut(KeyCode.Mouse4), "Hold for a smooth zoom.");
            RollLeft = config.Bind(ck, "Rotate left (roll)", new KeyboardShortcut(KeyCode.Q));
            RollRight = config.Bind(ck, "Rotate right (roll)", new KeyboardShortcut(KeyCode.E));
            ResetRotation = config.Bind(ck, "Reset rotation", new KeyboardShortcut(KeyCode.Backspace));

            const string live = "05. Live camera (unsnap)";
            UnsnapKey = config.Bind(live, "Toggle camera snap", new KeyboardShortcut(KeyCode.F5), "Open DuckCam live: the menu opens and the camera unsnaps from the PMC while the raid keeps running. Press again to snap back.");
            CameraMouseKey = config.Bind(live, "Switch camera control to mouse", new KeyboardShortcut(KeyCode.F1), "Toggle whether the mouse flies the camera or turns the PMC while unsnapped.");
            CameraMouseOnUnsnap = config.Bind(live, "Mouse controls camera when unsnapping", true, "Live sessions start with the mouse and WASD on the camera (on), or on your PMC so you keep playing while it films (off, the KappaCam default). The camera/PMC mouse key switches at any time.");
            BlockPlayerCommandsWithMouse = config.Bind(live, "Block player keys while camera has the mouse", true, "Prevents camera keys (Space, Ctrl, Q, E...) from also making the PMC jump/crouch/lean while you fly.");
            ProtectPlayerWhileUnsnapped = config.Bind(live, "Protect player while unsnapped", false, "Block damage to everyone while a live session is active.");
            HideHudWhenUnsnapped = config.Bind(live, "Hide HUD while unsnapped", true, "");
            ChangeGameSpeedKey = config.Bind(live, "Change gamespeed", new KeyboardShortcut(KeyCode.F4), "Toggle between normal speed and the gamespeed below (live sessions).");
            GameSpeedPercent = config.Bind(live, "Set gamespeed (%)", 16, new ConfigDescription("Slow-motion speed.", new AcceptableValueRange<int>(1, 100)));
            RaidClockFollowsGameSpeed = config.Bind(live, "Raid clock follows gamespeed", true, "Slow motion does not consume extra raid time (the clock is compensated every frame).");

            const string attach = "06. Attach and memory";
            AttachKey = config.Bind(attach, "Attach to object", new KeyboardShortcut(KeyCode.F7), "Attach to whatever the camera looks at (KappaCam used F6, which is the DuckCam toggle here).");
            DetachKey = config.Bind(attach, "Detach from object", new KeyboardShortcut(KeyCode.F8));
            AttachType = config.Bind(attach, "Attach type", CameraAttachType.LookAt, "Look At: you fly freely while the camera keeps aiming at the target. Parented: the camera rides along with the target at its current offset.");
            AttachPoint = config.Bind(attach, "Anchor point on characters", AnchorPoint.Chest, "Where the camera anchors on a player or bot: chest, head or feet.");
            AttachTurn = config.Bind(attach, "Anchored camera turning", AnchorTurn.Heading, "Parented only. Heading: turns with the body's facing direction only (steady, no bob or tilt). Full: copies every turn and tilt of the anchor bone. None: only follows the position.");
            AttachSmoothing = config.Bind(attach, "Anchored camera smoothing", 0.6f, new ConfigDescription("Parented only. How much the follow is smoothed (0 = rigid, higher = softer, steadier).", new AcceptableValueRange<float>(0f, 0.95f)));
            AttachRaycastDistance = config.Bind(attach, "Attachment raycast distance", 100f, new ConfigDescription("", new AcceptableValueRange<float>(1f, 1000f)));
            LookAtSmoothing = config.Bind(attach, "LookAt smoothing", 0.2f, new ConfigDescription("Seconds to settle on the target.", new AcceptableValueRange<float>(0f, 2f)));
            LookAtEscapeThreshold = config.Bind(attach, "LookAt escape threshold", 0.10f, new ConfigDescription("Mouse movement (fraction of a full-speed flick) that breaks a Look At lock.", new AcceptableValueRange<float>(0.01f, 1f)));
            MemoryAdd = config.Bind(attach, "Add position to camera memory list", new KeyboardShortcut(KeyCode.Home));
            MemoryAdvance = config.Bind(attach, "Advance memory list position", new KeyboardShortcut(KeyCode.End));
            MemoryClear = config.Bind(attach, "Clear camera memory list", new KeyboardShortcut(KeyCode.Home, KeyCode.LeftShift));

            const string path = "07. Pathing and recording";
            CreateKeyframe = config.Bind(path, "Create keyframe", new KeyboardShortcut(KeyCode.Insert));
            PathDeleteNode = config.Bind(path, "Delete selected keyframe", new KeyboardShortcut(KeyCode.Delete));
            BeginRecording = config.Bind(path, "Record camera (start/stop)", new KeyboardShortcut(KeyCode.F2), "Starts a new camera recording, or ends the one in progress.");
            ContinueRecording = config.Bind(path, "Continue camera recording", new KeyboardShortcut(KeyCode.F2, KeyCode.LeftShift), "Appends to the current recording.");
            EndRecording = config.Bind(path, "End path recording", new KeyboardShortcut(KeyCode.None), "Optional separate stop key (the record key already stops).");
            PlayRecording = config.Bind(path, "Play camera recording (start/stop)", new KeyboardShortcut(KeyCode.F3), "Plays the recording, or stops playback.");
            PathUsesRealTime = config.Bind(path, "Paths play in real time", true, "On: paths/recordings play at wall-clock speed even in slow motion. Off: they follow the gamespeed.");
            PathConstantSpeed = config.Bind(path, "Bézier path constant speed", true, "On: even speed along the curve. Off: equal time between keyframes.");
            PathPrevNode = config.Bind(path, "Previous keyframe", new KeyboardShortcut(KeyCode.PageUp, KeyCode.LeftControl), "Select the previous keyframe and move the camera there.");
            PathNextNode = config.Bind(path, "Next keyframe", new KeyboardShortcut(KeyCode.PageDown, KeyCode.LeftControl));
            PathFirstNode = config.Bind(path, "First keyframe", new KeyboardShortcut(KeyCode.Home, KeyCode.LeftControl));
            PathLastNode = config.Bind(path, "Last keyframe", new KeyboardShortcut(KeyCode.End, KeyCode.LeftControl));
            PathReplaceNode = config.Bind(path, "Replace selected keyframe", new KeyboardShortcut(KeyCode.Insert, KeyCode.LeftControl), "Overwrite the selected keyframe with the current camera.");
            PathInsertBefore = config.Bind(path, "Insert keyframe before selected", new KeyboardShortcut(KeyCode.Insert, KeyCode.LeftAlt));
            PathInsertAfter = config.Bind(path, "Insert keyframe after selected", new KeyboardShortcut(KeyCode.Insert, KeyCode.LeftShift));
            PathCutKeys = config.Bind(path, "Alt+1-9 cut between paths", true, "While a path plays, Alt+1-9 (top number row) cut to that path at the same progress (multi-angle).");
            FrameStepKey = config.Bind(path, "Frame step", new KeyboardShortcut(KeyCode.None), "While frozen: let the world run for the frame-step count, then freeze again (bots stay frozen). Unbound by default.");

            const string culling = "08. Culling";
            CameraAnchoredVolumes = config.Bind(culling, "Camera-anchored volume culling", true, "Evaluate the game's player-anchored interior/occlusion volumes at the camera position while a session is active (experimental). Restored on exit.");

            const string gearRack = "09. Gear Rack";
            OnlyUnlockedPoses = config.Bind(gearRack, "Only unlocked poses", false, "Only list the Gear Rack poses this profile has unlocked.");
            CopyKeepsStance = config.Bind(gearRack, "Copies keep the raid stance", true, "Your PMC (and copies of players) start in the exact pose they have in the raid, holding the item in their hands, instead of the mannequin's standing pose.");
            ActorLimit = config.Bind(gearRack, "Photo actor limit", 8, new ConfigDescription("Maximum photo actors (characters) at once. Each one is a full character model; raise carefully.", new AcceptableValueRange<int>(1, 16)));
            FavoritePoses = config.Bind(gearRack, "Favorite poses", "", "Comma-separated pose state names (managed from the Gear Rack tab).");

            const string shots = "10. Screenshots";
            WriteSidecar = config.Bind(shots, "Write JSON sidecar", true, "Write camera/map/pose metadata next to each screenshot.");
            VideoRecordKey = config.Bind(shots, "Start or stop video recording", new KeyboardShortcut(KeyCode.F12, KeyCode.LeftControl), "Records the DuckCam view (and game audio) to an MP4 through FFmpeg.");
            PoseEditorKey = config.Bind(shots, "Open or close the pose editor", new KeyboardShortcut(KeyCode.P, KeyCode.LeftControl), "Bone-by-bone pose editor for the selected character (frozen scene).");
            PoseModeKey = config.Bind(shots, "Enter or leave Pose Mode", new KeyboardShortcut(KeyCode.Tab, KeyCode.LeftControl), "Blender Ctrl+Tab: opens the pose editor and gives the keyboard to Blender-style pose shortcuts (G, R, A, H...). Pressed again it leaves Pose Mode.");
            TimelineKey = config.Bind(shots, "Open or close the animation timeline", new KeyboardShortcut(KeyCode.T, KeyCode.LeftControl), "SFM-style timeline: keyframe the camera and character poses, play, scrub and record them.");
            TimelinePlayKey = config.Bind(shots, "Play or pause the animation timeline", new KeyboardShortcut(KeyCode.Space, KeyCode.LeftControl));
            FfmpegPath = config.Bind(shots, "FFmpeg path", "", "Full path to ffmpeg.exe. Empty: ffmpeg.exe in the DuckCam plugin folder, else the one on PATH.");
            PhotoResolution = config.Bind(shots, "Photo resolution", Rendering.CaptureResolution.Screen, "Screen = what you see. 1080p to 16K (8640p) re-render the view at that height (width follows the screen aspect). Large sizes render in tiles.");
            PhotoSupersample = config.Bind(shots, "Photo anti-alias supersampling", 1, new ConfigDescription("Renders photos at 2x or 4x the chosen size and scales them down for smoother edges. Transparent photos always use at least 2x. Limited so the render stays within 16384 pixels a side.", new AcceptableValueList<int>(1, 2, 4)));
            TransparentBackground = config.Bind(shots, "Transparent background", false, "Photos keep only the characters, on a transparent background (PNG with alpha).");
            CutoutSubjects = config.Bind(shots, "Transparent: characters", Rendering.CutoutSubjects.AllCharacters, "AllCharacters = photo characters, your PMC and bots. PhotoCharacters = only DuckCam photo characters.");
            CutoutKeepScene = config.Bind(shots, "Transparent: keep scene lighting", false, "Off: everything except the characters is hidden while the photo renders (clean cutout, no shadows from the scene). On: the characters keep the scene's shadows and reflections, but anything in front of them shows through the cutout.");
            CropToFrame = config.Bind(shots, "Crop screenshot to frame", true, "When a frame aspect ratio is selected, the saved PNG is cropped to exactly the area inside the letterbox preview.");

            const string ui = "11. Interface";
            PanelSide = config.Bind(ui, "Panel side", UiSide.Right, "Which screen edge the DuckCam panel sits on.");
            Palette = config.Bind(ui, "Color scheme", UI.UiPalette.NeonRed, "NeonRed (red + cyan highlight), Acid (yellow + blue), Ice (cyan + red), Tarkov (khaki).");
            Language = config.Bind(ui, "Language", Locale.BuiltIn, "Menu language. English is built in; other languages are JSON files in BepInEx/plugins/DuckCam/Locales or <SPT>/DuckCam/Locales, named by code (de.json -> de).");
            ConfirmExit = config.Bind(ui, "Ask before leaving DuckCam", true, "Leaving DuckCam (Esc, the toggle key or SESSION > End) first asks for confirmation: press the key again or Enter to leave, Backspace to stay.");
            HiddenTabs = config.Bind(ui, "Hidden tabs", "", "Tabs hidden from the tab bar (comma-separated tab ids). Change them in FRAME > Interface > Tabs.");
            GuideDone = config.Bind(ui, "Beginner guide done", false, "Off: the next DuckCam session starts frozen with the Beginner Guide. Run it again from FRAME > Interface.");
            HudDecorations = config.Bind(ui, "HUD decorations", true, "Camera coordinates, mode line, edge raid clock and frame counter around the screen (UI only, never captured).");
            GuidesWhenUiHidden = config.Bind(ui, "Guides visible when UI hidden", true, "Keep composition guides on screen after hiding the UI (never captured).");
            MenuUp = config.Bind(ui, "Menu: previous option", new KeyboardShortcut(KeyCode.Keypad8));
            MenuDown = config.Bind(ui, "Menu: next option", new KeyboardShortcut(KeyCode.Keypad2));
            MenuLeft = config.Bind(ui, "Menu: decrease value", new KeyboardShortcut(KeyCode.Keypad4));
            MenuRight = config.Bind(ui, "Menu: increase value", new KeyboardShortcut(KeyCode.Keypad6));
            MenuSelect = config.Bind(ui, "Menu: activate", new KeyboardShortcut(KeyCode.Keypad5));
            PrevCategory = config.Bind(ui, "Menu: previous category", new KeyboardShortcut(KeyCode.PageUp));
            NextCategory = config.Bind(ui, "Menu: next category", new KeyboardShortcut(KeyCode.PageDown));
            EdgeGlow = config.Bind(ui, "Edge glow", false, "Colored glow around the screen edge while the UI is visible (never captured).");

            const string uiKeys = "12. Keys while UI is open";
            const string uiKeysNote = "Only active while the DuckCam UI is visible and no text field is focused. With the UI hidden, the camera keys above apply.";
            UiCategoryPrev = config.Bind(uiKeys, "Previous category", new KeyboardShortcut(KeyCode.Q), uiKeysNote);
            UiCategoryNext = config.Bind(uiKeys, "Next category", new KeyboardShortcut(KeyCode.E), uiKeysNote);
            UiOptionUp = config.Bind(uiKeys, "Select option up", new KeyboardShortcut(KeyCode.UpArrow), uiKeysNote);
            UiOptionDown = config.Bind(uiKeys, "Select option down", new KeyboardShortcut(KeyCode.DownArrow), uiKeysNote);
            UiValueLeft = config.Bind(uiKeys, "Decrease value", new KeyboardShortcut(KeyCode.LeftArrow), uiKeysNote);
            UiValueRight = config.Bind(uiKeys, "Increase value", new KeyboardShortcut(KeyCode.RightArrow), uiKeysNote);
            UiActivate = config.Bind(uiKeys, "Activate option", new KeyboardShortcut(KeyCode.Return), uiKeysNote);
            UiPhoto = config.Bind(uiKeys, "Take screenshot", new KeyboardShortcut(KeyCode.Space), uiKeysNote);
            UiHide = config.Bind(uiKeys, "Show/hide UI", new KeyboardShortcut(KeyCode.Tab), "Hides the UI. With the UI hidden it shows it again, but only while game input is blocked (frozen, or the camera has the mouse), so it never opens your inventory.");
            UiExit = config.Bind(uiKeys, "Exit DuckCam", new KeyboardShortcut(KeyCode.Escape), "Ends the session. With the UI hidden it only works while game input is blocked.");
            UiCamForward = config.Bind(uiKeys, "Camera forward", new KeyboardShortcut(KeyCode.W), uiKeysNote);
            UiCamBack = config.Bind(uiKeys, "Camera back", new KeyboardShortcut(KeyCode.S), uiKeysNote);
            UiCamLeft = config.Bind(uiKeys, "Camera left", new KeyboardShortcut(KeyCode.A), uiKeysNote);
            UiCamRight = config.Bind(uiKeys, "Camera right", new KeyboardShortcut(KeyCode.D), uiKeysNote);
            UiCamUp = config.Bind(uiKeys, "Camera up", new KeyboardShortcut(KeyCode.R), uiKeysNote);
            UiCamDown = config.Bind(uiKeys, "Camera down", new KeyboardShortcut(KeyCode.F), uiKeysNote);
            UiScale = config.Bind(ui, "UI scale", 1f, new ConfigDescription("Multiplier on top of automatic resolution scaling (1080p reference).", new AcceptableValueRange<float>(0.6f, 1.8f)));
            PanelRows = config.Bind(ui, "Panel height (rows)", 12, new ConfigDescription("Fixed height of the option panel, in rows. Every category uses the same size; longer ones scroll (mouse wheel over the panel, or drag the scrollbar).", new AcceptableValueRange<int>(6, 24)));
            ShowKeyHints = config.Bind(ui, "Show key hint bar", true, "Bottom bar with the main keys, like console photo modes.");
            Guides = config.Bind(ui, "Composition guide", Rendering.CompositionGuide.RuleOfThirds, "Overlay guide (never captured).");
            GuideOpacity = config.Bind(ui, "Guide opacity", 0.35f, new ConfigDescription("", new AcceptableValueRange<float>(0.05f, 1f)));
            HorizonLine = config.Bind(ui, "Horizon line", false, "Center horizontal line to level the shot (never captured).");
            FrameAspectRatio = config.Bind(ui, "Frame aspect ratio", Rendering.FrameAspect.Native, "Letterbox preview of the framed area.");
            LetterboxOpacity = config.Bind(ui, "Letterbox opacity", 0.85f, new ConfigDescription("", new AcceptableValueRange<float>(0.2f, 1f)));

            MigrateKeyLayout(config);
        }

        private static void MigrateKeyLayout(ConfigFile config)
        {
            ConfigEntry<int> layout = config.Bind("99. Internal", "Key layout version", 1, "Used to update old defaults once. Don't change.");
            if (layout.Value < 2)
            {
                MigrateV2();
                layout.Value = 2;
            }
            if (layout.Value < 3)
            {
                if (!CameraMouseOnUnsnap.Value)
                {
                    CameraMouseOnUnsnap.Value = true;
                    PhotoLog.Info("Config migrated: live sessions start with mouse and WASD on the camera.");
                }
                layout.Value = 3;
            }
            if (layout.Value < 4)
            {
                if (GalleryKey.Value.MainKey == KeyCode.None)
                {
                    GalleryKey.Value = (KeyboardShortcut)GalleryKey.DefaultValue;
                }
                layout.Value = 4;
            }
        }

        private static void MigrateV2()
        {
            int moved = 0;
            void Move(ConfigEntry<KeyboardShortcut> entry, KeyboardShortcut oldDefault)
            {
                if (entry.Value.ToString() == oldDefault.ToString())
                {
                    entry.Value = (KeyboardShortcut)entry.DefaultValue;
                    moved++;
                }
            }
            Move(UnsnapKey, new KeyboardShortcut(KeyCode.Keypad7));
            Move(CameraMouseKey, new KeyboardShortcut(KeyCode.Equals, KeyCode.Backspace));
            Move(ChangeGameSpeedKey, new KeyboardShortcut(KeyCode.BackQuote));
            Move(ResetRotation, new KeyboardShortcut(KeyCode.Minus));
            Move(MemoryAdd, new KeyboardShortcut(KeyCode.Plus));
            Move(MemoryAdvance, new KeyboardShortcut(KeyCode.Greater));
            Move(MemoryClear, new KeyboardShortcut(KeyCode.Less));
            Move(CreateKeyframe, new KeyboardShortcut(KeyCode.KeypadPlus));
            Move(EndRecording, new KeyboardShortcut(KeyCode.RightBracket));
            Move(PathReplaceNode, new KeyboardShortcut(KeyCode.KeypadPlus, KeyCode.LeftControl));
            Move(PathInsertBefore, new KeyboardShortcut(KeyCode.KeypadPlus, KeyCode.LeftAlt));
            Move(PathInsertAfter, new KeyboardShortcut(KeyCode.KeypadPlus, KeyCode.LeftShift));

            PhotoLog.Info($"Config migrated: TKL key layout, {moved} key(s) moved.");
        }

        public static float RadiusMeters(CameraRadiusProfile profile)
        {
            switch (profile)
            {
                case CameraRadiusProfile.Radius100m: return 100f;
                case CameraRadiusProfile.Radius250m: return 250f;
                case CameraRadiusProfile.Radius500m: return 500f;
                case CameraRadiusProfile.Radius750m: return 750f;
                case CameraRadiusProfile.Radius1000m: return 1000f;
                default: return float.PositiveInfinity;
            }
        }

        public static bool Held(ConfigEntry<KeyboardShortcut> entry)
        {
            KeyboardShortcut s = entry.Value;
            if (s.MainKey == KeyCode.None || !Input.GetKey(s.MainKey))
            {
                return false;
            }
            foreach (KeyCode m in s.Modifiers)
            {
                if (!Input.GetKey(m))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
