using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using EFT.CameraControl;
using DuckCam.Actors;
using DuckCam.Animation;
using DuckCam.Camera;
using DuckCam.Core;
using DuckCam.Culling;
using DuckCam.Pause;
using DuckCam.Rendering;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed partial class DuckCamUI
    {
        private void DrawDebug()
        {
            int b = _kit.Buttons("Scan Visibility", "Rig Probe");
            if (b == 0)
            {
                VisibilityDiagnostics.Scan(_c.Camera.Camera, 150f);
            }
            else if (b == 1)
            {
                RigProbe.Run(_c.Player, _c.Actors.SelectedActor?.Animator);
            }
            int b2 = _kit.Buttons("Copy Report", "Simulate Failure");
            if (b2 == 0)
            {
                GUIUtility.systemCopyBuffer = BuildReport();
                _c.LastMessage = "Diagnostics report copied to clipboard.";
            }
            else if (b2 == 1)
            {
                _c.SimulateFailure();
                return;
            }
            _kit.Note(BuildReport(), mono: true);
        }

        private string BuildReport()
        {
            var sb = new StringBuilder();
            PhotoCameraController cam = _c.Camera;
            sb.AppendLine($"{DuckCamPlugin.Name} {DuckCamPlugin.Version} | state {_c.State} | {(_c.Frozen ? "frozen" : "live")} | started {(_c.StartedLive ? "live" : "frozen")} | active {_c.ActiveSeconds:F0}s | timeScale {Time.timeScale} | fixedDelta {Time.fixedDeltaTime:F4}");
            sb.AppendLine($"Map {GameContext.MapDisplayName} ({GameContext.LocationId}) | world {GameContext.World?.GetType().Name} | UI font {HudTheme.FontSource}");
            sb.AppendLine("- Freeze layers -");
            foreach (KeyValuePair<string, string> kv in _c.Freeze.LayerStatus)
            {
                sb.AppendLine($"  {kv.Key}: {kv.Value}");
            }
            sb.AppendLine($"  Raid clock: {_c.Freeze.TimerStatus}; slow-motion compensation {_c.Dilation.ClockCompensation.TotalSeconds:F1}s");
            sb.AppendLine($"  Flags: dmg={FreezeFlags.DamageBlocks} input={FreezeFlags.InputBlocks} bots={FreezeFlags.BotsFrozen} timer={FreezeFlags.RaidTimerFrozen} world={FreezeFlags.WorldTickFrozen} cam={FreezeFlags.CameraOwned} look={FreezeFlags.PlayerLookBlocked} cmds={FreezeFlags.PlayerCommandsBlocked} bsgCullingOff={FreezeFlags.BsgCullingDisabled}");
            sb.AppendLine($"  HUD: {HudController.Status}");
            sb.AppendLine("- Camera / culling -");
            UnityEngine.Camera c = cam.Camera;
            Vector3 pmc = _c.Player != null ? _c.Player.Transform.position : Vector3.zero;
            sb.AppendLine($"  Photo camera {cam.Position} mode {cam.Mode} {(cam.ExternallyDriven ? "(playback)" : "")} attach {(cam.Attachment.Attached ? cam.Attachment.Type + " " + cam.Attachment.TargetName : "none")}");
            sb.AppendLine($"  PMC {pmc}, distance {Vector3.Distance(cam.Position, pmc):F1} m; anchor distance {cam.DistanceFromAnchor:F1} m");
            sb.AppendLine($"  Driven camera is CameraManager.Camera: {cam.IsDrivenCameraCurrent} ({(CameraManager.Instance.Camera != null ? CameraManager.Instance.Camera.name : "null")})");
            if (c != null)
            {
                sb.AppendLine($"  occlusion {c.useOcclusionCulling}, fov {c.fieldOfView:F1}, near {c.nearClipPlane:F3}, far {c.farClipPlane:F0}, lodBias {QualitySettings.lodBias:F2}");
            }
            sb.AppendLine($"  Camera drift events {cam.DriftCount} (last {cam.LastDrift:F3} m)");
            sb.AppendLine($"  Volume culling anchor: {_c.Culling.Status}; camera inside {_c.Culling.CameraInsideCount}; forced updates {_c.Culling.ForcedUpdates}");
            sb.AppendLine("- Effects / DoF / Overlays -");
            sb.AppendLine($"  Effects: {_c.Effects.Discovery}; {_c.Effects.Params.Count} sliders, filter {(_c.Effects.FilterAvailable ? _c.Effects.Filter.ToString() : "n/a")}");
            sb.AppendLine($"  DoF: {_c.Dof.Discovery}; live {_c.Dof.Enabled} mode {_c.Dof.Mode} focus {_c.Dof.FocusDistance:0.00} m; lens render {_c.DofRender.Status}");
            sb.AppendLine($"  Overlays: {OverlayLibrary.Assets.Count} files, frame {_c.Overlays.Frame?.Name ?? "none"}, stickers {_c.Overlays.Stickers.Count}");
            sb.AppendLine($"  Environment: clock {(_c.TimeOfDay.Available ? _c.TimeOfDay.Now.ToString("HH:mm:ss") : "n/a")} ({_c.TimeOfDay.Status}); weather {(DuckCam.Environment.WeatherOverride.Available ? (_c.Weather.Enabled ? "override" : "raid") : "n/a")}{(DuckCam.Environment.WeatherOverride.OtherWeatherModLoaded ? ", TimeWeatherChanger loaded" : "")}");
            sb.AppendLine("- Cinematic -");
            sb.AppendLine($"  Paths {_c.Paths.Paths.Count}, active '{_c.Path.Name}' {_c.Path.Keys.Count} keys, {_c.Path.Duration:F1}s, ease {(_c.Path.EaseIn ? "in" : "-")}/{(_c.Path.EaseOut ? "out" : "-")}, playing {_c.Path.Playing}; shake manual {_c.Camera.Shake.Manual.Enabled} path {_c.Camera.Shake.Path.Enabled}; LOD boost {(_c.LodBoost ? "x" + _c.LodBoostFactor.ToString("0.00", CultureInfo.InvariantCulture) : "off")}; photo {HiResRenderer.ResolutionNames[(int)DuckCamConfig.PhotoResolution.Value]} ss {DuckCamConfig.PhotoSupersample.Value}x transparent {DuckCamConfig.TransparentBackground.Value}");
            sb.AppendLine($"  Recording {_c.Recording.Samples.Count} samples, {_c.Recording.Duration:F1}s; lights {_c.Lights.Lights.Count}; memory {_c.Memory.Count}");
            sb.AppendLine("- Gear Rack -");
            sb.AppendLine($"  Controller: {(GearRackPoseLibrary.Controller != null ? GearRackPoseLibrary.Controller.name : "not loaded")}, clips {GearRackPoseLibrary.ControllerClipNames.Length}, poses {GearRackPoseLibrary.Poses.Count} ({GearRackPoseLibrary.ModPoseCount} from mods){(GearRackPoseLibrary.Error != null ? ", error: " + GearRackPoseLibrary.Error : "")}");
            sb.AppendLine($"  Pose library: {BodyClipLibrary.Status}; source {BodyClipLibrary.Source}");
            sb.AppendLine($"  Actors: {_c.Actors.Actors.Count}/{_c.Actors.Limit}" + string.Concat(_c.Actors.Actors.Select(a => $"; {a.Name} [{a.Status}] pose {a.CurrentPose?.PoseName ?? "none"}{(a.Error != null ? " (" + a.Error + ")" : "")}")));
            foreach (PhotoActor a in _c.Actors.Actors.Where(a => a.Rig?.Skeleton != null))
            {
                sb.AppendLine($"  Skeleton {a.Name}: {a.Rig.Skeleton.Map.Count} bones, profile {a.Rig.Skeleton.Profile}. {a.Rig.Skeleton.AxisReport}");
            }
            sb.AppendLine("- Rig probe -");
            sb.AppendLine(RigProbe.LastReport.TrimEnd());
            sb.AppendLine("- Visibility scan -");
            sb.AppendLine(VisibilityDiagnostics.LastReport.TrimEnd());
            sb.AppendLine("- Patches -");
            foreach (PatchBinding b in PatchRegistry.Bindings)
            {
                sb.AppendLine($"  {(b.Bound ? "OK  " : "FAIL")} {b.Id} [{b.Feature}]{(b.Bound ? "" : " " + b.Error)}");
            }
            sb.AppendLine("- Pending restores (unwind order) -");
            sb.AppendLine("  freeze: " + string.Join(" > ", _c.FreezeRestores.PendingNames));
            sb.AppendLine("  session: " + string.Join(" > ", _c.Restores.PendingNames));
            return sb.ToString();
        }
    }
}
