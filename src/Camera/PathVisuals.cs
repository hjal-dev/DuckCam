using System.Collections.Generic;
using UnityEngine;

namespace DuckCam.Camera
{
    internal sealed class PathVisuals
    {
        private GameObject _root;
        private LineRenderer _curve;
        private readonly List<GameObject> _markers = new List<GameObject>();
        private Material _material;
        private int _builtVersion = -1;
        private CameraPath _builtPath;

        public bool Enabled { get; set; } = true;

        public void Update(CameraPath path, bool visible)
        {
            int version = path.Version;
            bool show = Enabled && visible && path.Keys.Count > 0;
            if (!show)
            {
                if (_root != null)
                {
                    _root.SetActive(false);
                }
                return;
            }

            EnsureRoot();
            _root.SetActive(true);
            if (version == _builtVersion && ReferenceEquals(path, _builtPath))
            {
                return;
            }
            _builtVersion = version;
            _builtPath = path;

            List<Vector3> points = path.Keys.Count >= 2 ? path.Polyline() : new List<Vector3>();
            _curve.positionCount = points.Count;
            _curve.SetPositions(points.ToArray());

            foreach (GameObject m in _markers)
            {
                Object.Destroy(m);
            }
            _markers.Clear();
            for (int i = 0; i < path.Keys.Count; i++)
            {
                PathKeyframe key = path.Keys[i];
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(sphere.GetComponent<Collider>());
                sphere.name = "PathKey_" + i;
                sphere.transform.SetParent(_root.transform, false);
                sphere.transform.position = key.Position;
                sphere.transform.localScale = Vector3.one * 0.25f;
                Renderer r = sphere.GetComponent<Renderer>();
                r.sharedMaterial = _material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _markers.Add(sphere);

                var tick = new GameObject("PathDir_" + i).AddComponent<LineRenderer>();
                tick.transform.SetParent(_root.transform, false);
                Configure(tick, new Color(0.2f, 0.35f, 1f), 0.04f);
                tick.positionCount = 2;
                tick.SetPosition(0, key.Position);
                tick.SetPosition(1, key.Position + key.Rotation * Vector3.forward * 0.6f);
                _markers.Add(tick.gameObject);
            }
        }

        public void Destroy()
        {
            if (_root != null)
            {
                Object.Destroy(_root);
            }
            if (_material != null)
            {
                Object.Destroy(_material);
            }
            _root = null;
            _material = null;
            _markers.Clear();
            _builtVersion = -1;
        }

        private void EnsureRoot()
        {
            if (_root != null)
            {
                return;
            }
            _root = new GameObject("DuckCam_PathVisuals");
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("UI/Default");
            _material = new Material(shader);
            _curve = _root.AddComponent<LineRenderer>();
            Configure(_curve, new Color(0.1f, 1f, 0.1f), 0.06f);
        }

        private void Configure(LineRenderer line, Color color, float width)
        {
            line.sharedMaterial = _material;
            line.startColor = color;
            line.endColor = color;
            line.startWidth = width;
            line.endWidth = width;
            line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
        }
    }
}
