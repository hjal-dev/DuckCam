using DuckCam.Core;
using UnityEngine;
using DoF = UnityStandardAssets.ImageEffects.DepthOfField;

namespace DuckCam.Rendering
{
    public enum FocusMode
    {
        Manual,
        ScreenCenter,
        Pmc,
        AttachedTarget
    }

    internal sealed class DepthOfFieldController
    {
        private DoF _dof;
        private RestoreStack _stack;
        private bool _snapshotted;
        private float _smoothedDistance = -1f;

        public bool Available => _dof != null;
        public string Discovery { get; private set; } = "not probed";
        public bool Enabled { get; private set; }
        public FocusMode Mode { get; set; } = FocusMode.ScreenCenter;
        public float FocusDistance { get; set; } = 5f;
        public float Aperture { get; set; } = 18f;
        public float FocalSize { get; set; } = 0.08f;
        public float MaxBlur { get; set; } = 2.5f;
        public bool NearBlur { get; set; } = true;
        public bool HighResolution { get; set; } = true;
        public bool Visualize { get; set; }
        public Vector3? FocusPoint { get; private set; }

        public void Probe(UnityEngine.Camera camera, RestoreStack stack)
        {
            _stack = stack;
            _snapshotted = false;
            Enabled = false;
            FocusPoint = null;
            _smoothedDistance = -1f;
            _dof = camera.GetComponent<DoF>();
            Discovery = _dof == null ? "DepthOfField missing on camera" : $"DepthOfField present ({(_dof.enabled ? "enabled" : "disabled")}, blur {_dof.blurType})";
        }

        public void ResetSettings()
        {
            SetEnabled(false);
            Mode = FocusMode.ScreenCenter;
            FocusDistance = 5f;
            Aperture = 18f;
            FocalSize = 0.08f;
            MaxBlur = 2.5f;
            NearBlur = true;
            Visualize = false;
        }

        public void SetEnabled(bool on)
        {
            if (_dof == null || on == Enabled)
            {
                return;
            }
            Snapshot();
            Enabled = on;
            _dof.enabled = on;
            if (!on)
            {
                _dof.visualizeFocus = false;
            }
        }

        public void Tick(UnityEngine.Camera camera, Vector3 pmcPoint, Transform attached, float dt)
        {
            if (_dof == null || !Enabled)
            {
                FocusPoint = null;
                return;
            }

            float distance = FocusDistance;
            Vector3? point = null;
            switch (Mode)
            {
                case FocusMode.ScreenCenter:
                    Transform t = camera.transform;
                    if (Physics.Raycast(t.position, t.forward, out RaycastHit hit, 2000f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        distance = hit.distance;
                        point = hit.point;
                    }
                    break;
                case FocusMode.Pmc:
                    distance = Vector3.Distance(camera.transform.position, pmcPoint);
                    point = pmcPoint;
                    break;
                case FocusMode.AttachedTarget:
                    if (attached != null)
                    {
                        distance = Vector3.Distance(camera.transform.position, attached.position);
                        point = attached.position;
                    }
                    break;
            }

            if (Mode != FocusMode.Manual)
            {
                _smoothedDistance = _smoothedDistance < 0f ? distance : Mathf.Lerp(_smoothedDistance, distance, 1f - Mathf.Exp(-dt * 10f));
                FocusDistance = _smoothedDistance;
            }
            FocusPoint = point;

            _dof.focalTransform = null;
            _dof.focalLength = Mathf.Max(0.05f, FocusDistance);
            _dof.focalSize = Mathf.Clamp(FocalSize, 0f, 2f);
            _dof.aperture = Mathf.Max(0f, Aperture);
            _dof.maxBlurSize = Mathf.Max(0.1f, MaxBlur);
            _dof.nearBlur = NearBlur;
            _dof.highResolution = HighResolution;
            _dof.visualizeFocus = Visualize;
        }

        private void Snapshot()
        {
            if (_snapshotted)
            {
                return;
            }
            _snapshotted = true;
            DoF d = _dof;
            bool enabled = d.enabled;
            float focalLength = d.focalLength;
            float focalSize = d.focalSize;
            float aperture = d.aperture;
            Transform focalTransform = d.focalTransform;
            float maxBlur = d.maxBlurSize;
            bool near = d.nearBlur;
            bool highRes = d.highResolution;
            bool visualize = d.visualizeFocus;
            _stack.Push("depth of field", () =>
            {
                Enabled = false;
                FocusPoint = null;
                if (d == null)
                {
                    return;
                }
                d.focalLength = focalLength;
                d.focalSize = focalSize;
                d.aperture = aperture;
                d.focalTransform = focalTransform;
                d.maxBlurSize = maxBlur;
                d.nearBlur = near;
                d.highResolution = highRes;
                d.visualizeFocus = visualize;
                d.enabled = enabled;
            });
        }
    }
}
