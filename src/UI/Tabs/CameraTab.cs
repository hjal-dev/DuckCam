using System.Collections.Generic;
using System.Linq;
using DuckCam.Actors;
using DuckCam.Camera;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed partial class DuckCamUI
    {
        private int _spectateIndex;

        private List<EFT.Player> _spectateList = new List<EFT.Player>();

        private float _spectateRefresh;

        private void DrawSpectate(PhotoCameraController cam)
        {
            _kit.Section("View From A Player");
            if (Time.unscaledTime >= _spectateRefresh || _spectateList.Count == 0)
            {
                _spectateRefresh = Time.unscaledTime + 2f;
                Vector3 from = _c.Player != null ? _c.Player.Transform.position : cam.Position;
                _spectateList = ActorManager.Candidates(from, 40).Where(p => p != null && p != _c.Player).ToList();
            }
            if (_spectateList.Count == 0)
            {
                _kit.Value("Players", "Nobody else alive nearby");
                return;
            }
            _spectateIndex = Mathf.Clamp(_spectateIndex, 0, _spectateList.Count - 1);
            Vector3 me = _c.Player != null ? _c.Player.Transform.position : cam.Position;
            string[] names = _spectateList
                .Select(p => $"{ActorManager.Describe(p)} · {ActorManager.Faction(p)} · {Vector3.Distance(me, p.Transform.position):0} m")
                .ToArray();
            _spectateIndex = _kit.Selector("Player", names, _spectateIndex);
            EFT.Player chosen = _spectateList[_spectateIndex];
            int b = _kit.Buttons("Their Eyes", "Orbit Them", cam.Subject != null ? "Stop" : "-");
            if (b == 0)
            {
                cam.Spectate(chosen, firstPerson: true);
                _c.LastMessage = "Watching through " + ActorManager.Describe(chosen) + "'s eyes.";
            }
            else if (b == 1)
            {
                cam.Spectate(chosen, firstPerson: false);
                _c.LastMessage = "Orbiting " + ActorManager.Describe(chosen) + " (mouse to circle, wheel for distance).";
            }
            else if (b == 2 && cam.Subject != null)
            {
                cam.StopSpectating();
                _c.LastMessage = "Back to the free camera.";
            }
            if (cam.Subject != null)
            {
                _kit.Value("Watching", ActorManager.Describe(cam.Subject) + (cam.Mode == CameraMode.FirstPerson ? " (eyes)" : " (orbit)"));
            }
        }

        private void DrawCamera()
        {
            PhotoCameraController cam = _c.Camera;
            string orbitName = cam.Subject != null ? "Orbit " + ActorManager.Describe(cam.Subject) : "Orbit PMC";
            string eyesName = cam.ActorSubject != null ? "Eyes of " + cam.ActorSubject.Name : cam.Subject != null ? "Eyes of " + ActorManager.Describe(cam.Subject) : "First Person (pick below)";
            CameraMode mode = _kit.Enum("Camera Type", cam.Mode, new[] { "Free", orbitName, "Tripod", eyesName });
            if (mode != cam.Mode)
            {
                if (mode == CameraMode.FirstPerson)
                {
                    if (cam.ActorSubject != null)
                    {
                        cam.ViewThroughActor(cam.ActorSubject);
                    }
                    else                     if (cam.Subject != null)
                    {
                        cam.Spectate(cam.Subject, firstPerson: true);
                    }
                    else
                    {
                        _c.LastMessage = "Pick a player under View From A Player first.";
                    }
                }
                else
                {
                    if (mode == CameraMode.Orbit || cam.Mode == CameraMode.Orbit || cam.Mode == CameraMode.FirstPerson)
                    {
                        cam.ToggleOrbit();
                    }
                    cam.Mode = mode;
                }
            }
            DrawSpectate(cam);
            cam.Fov = _kit.Slider("Field of View", cam.Fov, PhotoCameraController.MinFov, PhotoCameraController.MaxFov, 1f, "0");
            cam.Roll = _kit.Slider("Rotate", Mathf.DeltaAngle(0f, cam.Roll), -180f, 180f, 1f, "0");
            cam.HorizonLock = _kit.Toggle("Horizon Lock", cam.HorizonLock);
            DuckCamConfig.RadiusProfile.Value = _kit.Enum("Movement Radius", DuckCamConfig.RadiusProfile.Value, new[] { "100 m", "250 m", "500 m", "750 m", "1000 m", "Unlimited" });
            _kit.Value("Distance", float.IsInfinity(cam.Radius) ? $"{cam.DistanceFromAnchor:0} m" : $"{cam.DistanceFromAnchor:0} / {cam.Radius:0} m");
            DuckCamConfig.CollisionMode.Value = _kit.Enum("Collision", DuckCamConfig.CollisionMode.Value, new[] { "Off", "Soft", "Strict" });
            cam.NearClip = _kit.Slider("Near Clip", cam.NearClip, 0.01f, 2f, 0.01f, "0.00", " m");
            cam.FarClip = _kit.Slider("Far Clip", cam.FarClip, 100f, 20000f, 100f, "0", " m");
            bool lod = _kit.Toggle("LOD Boost", _c.LodBoost);
            if (lod != _c.LodBoost)
            {
                _c.SetLodBoost(lod);
            }
            if (_c.LodBoost)
            {
                _c.LodBoostFactor = _kit.Slider("  LOD Distance Factor", _c.LodBoostFactor, 1f, 8f, 0.25f, "0.00", "x");
            }
            bool bsgOff = _kit.Toggle("BSG Culling Off", _c.BsgCullingDisabled);
            if (bsgOff != _c.BsgCullingDisabled)
            {
                _c.SetBsgCulling(bsgOff);
            }
            _kit.Value("Interior Culling", _c.Culling.Active ? "Follows camera" : _c.Culling.Status);

            _kit.Section("Bookmarks · Alt+1-9 · click empty to store, shift+click to overwrite");
            for (int row = 0; row < 3; row++)
            {
                string[] labels = Enumerable.Range(row * 3, 3)
                    .Select(i => $"Alt+{i + 1}" + (_c.Bookmarks.Has(i) ? " *" : ""))
                    .ToArray();
                int clicked = _kit.Buttons(labels);
                if (clicked >= 0)
                {
                    int i = row * 3 + clicked;
                    if (Event.current.shift || !_c.Bookmarks.Has(i))
                    {
                        _c.Bookmarks.Store(i, cam.CurrentPose);
                        _c.LastMessage = $"Bookmark {i + 1} stored.";
                    }
                    else if (_c.Bookmarks.TryGet(i, out CameraPose pose))
                    {
                        cam.Mode = CameraMode.Free;
                        cam.SetPose(pose);
                    }
                }
            }
        }
    }
}
