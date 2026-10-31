using System.Globalization;
using DuckCam.Camera;
using DuckCam.Core;

namespace DuckCam.UI
{
    internal sealed partial class DuckCamUI
    {
        private void DrawMotion()
        {
            PhotoCameraController cam = _c.Camera;
            DuckCamConfig.MoveSpeed.Value = _kit.Slider("Move Speed", DuckCamConfig.MoveSpeed.Value, 0.1f, 50f, 0.5f, "0.0", " m/s");
            _kit.Value("Speed Scale", "x" + cam.SpeedScale.ToString("0.00", CultureInfo.InvariantCulture));
            DuckCamConfig.MovementSmoothing.Value = _kit.Slider("Movement Smoothing", DuckCamConfig.MovementSmoothing.Value, 0f, 0.999f, 0.01f, "0.000");
            DuckCamConfig.MouseSmoothing.Value = _kit.Slider("Mouse Smoothing", DuckCamConfig.MouseSmoothing.Value, 0f, 0.99f, 0.01f);
            DuckCamConfig.LookSensitivity.Value = _kit.Slider("Sensitivity", DuckCamConfig.LookSensitivity.Value, 0.05f, 20f, 0.1f);
            DuckCamConfig.Wheel.Value = _kit.Enum("Mouse Wheel", DuckCamConfig.Wheel.Value, new[] { "FOV Zoom", "Move Speed" });
            DuckCamConfig.ZoomAmount.Value = _kit.Slider("Zoom Key Amount", DuckCamConfig.ZoomAmount.Value, 0.05f, 0.95f, 0.05f);
            DuckCamConfig.AttachType.Value = _kit.Enum("Attach Type", DuckCamConfig.AttachType.Value, new[] { "Look At", "Parented" });
            DuckCamConfig.AttachPoint.Value = _kit.Enum("Anchor On Characters", DuckCamConfig.AttachPoint.Value, new[] { "Chest", "Head", "Feet" });
            if (DuckCamConfig.AttachType.Value == CameraAttachType.Parented)
            {
                DuckCamConfig.AttachTurn.Value = _kit.Enum("Turn With Target", DuckCamConfig.AttachTurn.Value, new[] { "Heading Only", "Full (bone)", "Never" });
                DuckCamConfig.AttachSmoothing.Value = _kit.Slider("Follow Smoothing", DuckCamConfig.AttachSmoothing.Value, 0f, 0.95f, 0.05f);
            }
            _kit.Value("Attached To", cam.Attachment.Attached ? cam.Attachment.TargetName : "Nothing");
            int a = _kit.Buttons($"Attach ({KeyName(DuckCamConfig.AttachKey)})", $"Detach ({KeyName(DuckCamConfig.DetachKey)})");
            if (a == 0)
            {
                cam.Attach(out string message);
                _c.LastMessage = message;
            }
            else if (a == 1)
            {
                cam.DetachKeepingPose();
            }
            _kit.Value("Memory List", $"{_c.Memory.Count} positions");
            int m = _kit.Buttons("Add", "Next", "Clear");
            if (m == 0)
            {
                _c.Memory.Add(cam.CurrentPose);
            }
            else if (m == 1 && _c.Memory.Advance(out CameraPose next))
            {
                cam.Mode = CameraMode.Free;
                cam.SetPose(next);
            }
            else if (m == 2)
            {
                _c.Memory.Clear();
            }

            _kit.Section("Handheld shake · free camera");
            DrawShake(cam.Shake.Manual);
        }

        private void DrawShake(ShakeSettings s)
        {
            s.Enabled = _kit.Toggle("Handheld Shake", s.Enabled);
            if (!s.Enabled)
            {
                return;
            }
            s.MoveStrength = _kit.Slider("  Position Strength", s.MoveStrength, 0f, 5f, 0.05f);
            s.MoveFrequency = _kit.Slider("  Position Frequency", s.MoveFrequency, 0.05f, 10f, 0.05f);
            s.RotationStrength = _kit.Slider("  Rotation Strength", s.RotationStrength, 0f, 5f, 0.05f);
            s.RotationFrequency = _kit.Slider("  Rotation Frequency", s.RotationFrequency, 0.05f, 10f, 0.05f);
        }
    }
}
