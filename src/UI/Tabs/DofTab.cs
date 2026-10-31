using System.Globalization;
using DuckCam.Rendering;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed partial class DuckCamUI
    {
        private void DrawDof()
        {
            DepthOfFieldController dof = _c.Dof;
            if (!dof.Available)
            {
                _kit.Value("Depth of Field", "Unavailable");
                _kit.Note(dof.Discovery);
            }
            else
            {
                bool on = _kit.Toggle("Depth of Field", dof.Enabled);
                if (on != dof.Enabled)
                {
                    dof.SetEnabled(on);
                }
                dof.Mode = _kit.Enum("Focus Mode", dof.Mode, new[] { "Manual", "Screen Center", "Your PMC", "Attached Target" });
                float distance = _kit.LogSlider("Focus Distance", dof.FocusDistance, 0.2f, 500f, "0.00", " m");
                if (Mathf.Abs(distance - dof.FocusDistance) > 0.0001f)
                {
                    dof.FocusDistance = distance;
                    dof.Mode = FocusMode.Manual;
                }
                dof.Aperture = _kit.Slider("Aperture", dof.Aperture, 0f, 60f, 0.5f, "0.0");
                dof.FocalSize = _kit.Slider("Focus Range", dof.FocalSize, 0f, 2f, 0.01f, "0.00");
                dof.MaxBlur = _kit.Slider("Max Blur", dof.MaxBlur, 0.1f, 10f, 0.1f, "0.0");
                dof.NearBlur = _kit.Toggle("Foreground Blur", dof.NearBlur);
                dof.Visualize = _kit.Toggle("Show Focus Plane", dof.Visualize);
            }

            _kit.Section("Lens render · multi-frame bokeh (frozen scene)");
            DofRenderer r = _c.DofRender;
            r.Rings = _kit.IntSlider("Rings", r.Rings, 1, 8);
            r.InnerPoints = _kit.IntSlider("Points In Inner Ring", r.InnerPoints, 3, 10);
            r.ApertureRadius = _kit.Slider("Lens Radius", r.ApertureRadius * 1000f, 1f, 200f, 1f, "0", " mm") / 1000f;
            r.SettleFrames = _kit.IntSlider("Settle Frames", r.SettleFrames, 1, 10);
            r.HighlightBoost = _kit.Slider("Highlight Boost", r.HighlightBoost, 0f, 4f, 0.1f, "0.0");
            _kit.Value("Samples", r.SampleCount.ToString(CultureInfo.InvariantCulture));
            if (_kit.Action("Render DoF Photo", r.Rendering ? "Rendering" : "Render"))
            {
                _c.StartDofRender();
            }
            _kit.Value("Last Render", r.Status);
            _kit.Note("The camera steps across a virtual lens and averages every frame, so out-of-focus areas get true bokeh. Focus uses the live DoF focus distance (or the screen center). Needs a frozen scene. If you see ghosting, raise Settle Frames or turn off DLSS/FSR/TAA in the graphics settings.");
        }
    }
}
