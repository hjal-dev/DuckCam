using EFT;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Camera
{
    internal sealed class CameraAttachment
    {
        private Vector3 _aimLocal;
        private Player _player;
        private bool _hasFrame;

        public Transform Target { get; private set; }
        public string TargetName { get; private set; }
        public CameraAttachType Type { get; private set; }
        public bool Attached => Target != null;

        public Vector3 FramePosition { get; private set; }
        public Quaternion FrameRotation { get; private set; } = Quaternion.identity;

        public Vector3 AimPoint => Target == null ? Vector3.zero : (_player != null ? AnchorPosition() : Target.TransformPoint(_aimLocal));

        public bool TryAttach(UnityEngine.Camera camera, CameraAttachType type, float distance, out string message)
        {
            Transform t = camera.transform;
            if (!Physics.Raycast(t.position, t.forward, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                message = $"Nothing to attach to within {distance:F0} m.";
                return false;
            }

            Player player = GameContext.World != null ? GameContext.World.GetPlayerByCollider(hit.collider) : null;
            if (player != null)
            {
                _player = player;
                Target = player.Transform.Original;
                TargetName = player.Profile?.Nickname ?? player.name;
                _aimLocal = Vector3.zero;
            }
            else
            {
                _player = null;
                Target = hit.rigidbody != null ? hit.rigidbody.transform : hit.collider.transform;
                TargetName = Target.name;
                _aimLocal = Target.InverseTransformPoint(hit.point);
            }

            Type = type;
            _hasFrame = false;
            UpdateFrame(0f);
            message = $"Attached ({type}) to {TargetName}" + (_player != null ? $" ({DuckCamConfig.AttachPoint.Value.ToString().ToLowerInvariant()})." : ".");
            return true;
        }

        public void Detach()
        {
            Target = null;
            TargetName = null;
            _player = null;
            _hasFrame = false;
        }

        public void UpdateFrame(float dt)
        {
            if (Target == null)
            {
                return;
            }
            Vector3 position = AnchorPosition();
            Quaternion rotation = AnchorRotation();
            float smoothing = DuckCamConfig.AttachSmoothing.Value;
            if (!_hasFrame || dt <= 0f || smoothing <= 0.001f)
            {
                FramePosition = position;
                FrameRotation = rotation;
                _hasFrame = true;
                return;
            }
            float k = 1f - Mathf.Pow(smoothing, dt * 60f);
            FramePosition = Vector3.Lerp(FramePosition, position, k);
            FrameRotation = Quaternion.Slerp(FrameRotation, rotation, k);
        }

        private Vector3 AnchorPosition()
        {
            if (_player == null)
            {
                return Target.position;
            }
            PlayerBones bones = _player.PlayerBones;
            switch (DuckCamConfig.AttachPoint.Value)
            {
                case AnchorPoint.Head when bones != null && bones.Head != null:
                    return bones.Head.Original.position;
                case AnchorPoint.Feet:
                    return _player.Transform.position;
                default:
                    return bones != null && bones.Ribcage != null ? bones.Ribcage.Original.position : _player.Transform.position + Vector3.up * 1.3f;
            }
        }

        private Quaternion AnchorRotation()
        {
            switch (DuckCamConfig.AttachTurn.Value)
            {
                case AnchorTurn.None:
                    return Quaternion.identity;
                case AnchorTurn.Full:
                    if (_player != null)
                    {
                        PlayerBones bones = _player.PlayerBones;
                        if (DuckCamConfig.AttachPoint.Value == AnchorPoint.Head && bones?.Head != null)
                        {
                            return bones.Head.Original.rotation;
                        }
                        if (DuckCamConfig.AttachPoint.Value == AnchorPoint.Chest && bones?.Ribcage != null)
                        {
                            return bones.Ribcage.Original.rotation;
                        }
                    }
                    return Target.rotation;
                default:
                    Vector3 forward = Vector3.ProjectOnPlane(Target.rotation * Vector3.forward, Vector3.up);
                    return forward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;
            }
        }
    }
}
