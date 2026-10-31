using System;
using System.Collections.Generic;
using EFT.Interactive;
using HarmonyLib;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Culling
{
    internal sealed class PhotoCullingAnchor
    {
        private const float RefreshInterval = 0.2f;
        private const float RefreshDistance = 0.5f;

        private static readonly AccessTools.FieldRef<DisablerCullingObjectBase, List<ColliderReporter>> _entered =
            SafeRef<List<ColliderReporter>>("_enteredColliders");
        private static readonly AccessTools.FieldRef<DisablerCullingObjectBase, List<ColliderReporter>> _enteredInverse =
            SafeRef<List<ColliderReporter>>("_enteredInverseColliders");
        private static readonly AccessTools.FieldRef<DisablerCullingObjectBase, List<ColliderReporter>> _reporters =
            SafeRef<List<ColliderReporter>>("_colliderReporters");
        private static readonly AccessTools.FieldRef<DisablerCullingObjectBase, List<ColliderReporter>> _inverseReporters =
            SafeRef<List<ColliderReporter>>("_inverseColliderReporters");
        private static readonly AccessTools.FieldRef<DisablerCullingObjectBase, bool> _pendingUpdate =
            SafeRef<bool>("_updateComponentsStatus");

        private sealed class Volume
        {
            public DisablerCullingObjectBase Obj;
            public List<ColliderReporter> OriginalEntered;
            public List<ColliderReporter> OriginalEnteredInverse;
            public List<(ColliderReporter reporter, Collider[] colliders)> Reporters;
            public List<(ColliderReporter reporter, Collider[] colliders)> InverseReporters;
            public int PendingFrames;
        }

        private readonly List<Volume> _volumes = new List<Volume>();
        private readonly List<ColliderReporter> _scratch = new List<ColliderReporter>();
        private Vector3 _lastPosition = new Vector3(float.NaN, 0f, 0f);
        private float _nextRefresh;

        public static string UnavailableReason
        {
            get
            {
                if (_entered == null || _enteredInverse == null || _reporters == null || _inverseReporters == null || _pendingUpdate == null)
                {
                    return "DisablerCullingObjectBase fields not found in this client build";
                }
                return null;
            }
        }

        public Func<Vector3?> RestorePoint { get; set; }

        public bool Active { get; private set; }
        public int VolumeCount => _volumes.Count;
        public int CameraInsideCount { get; private set; }
        public int ForcedUpdates { get; private set; }
        public string Status { get; private set; } = "inactive";

        public void Activate(RestoreStack stack)
        {
            string reason = UnavailableReason;
            if (reason != null)
            {
                Status = "UNAVAILABLE: " + reason;
                PhotoLog.Warn("Camera-anchored culling unavailable: " + reason);
                return;
            }

            _volumes.Clear();
            DisablerCullingObjectBase[] found = UnityEngine.Object.FindObjectsOfType<DisablerCullingObjectBase>();
            foreach (DisablerCullingObjectBase obj in found)
            {
                try
                {
                    _volumes.Add(new Volume
                    {
                        Obj = obj,
                        OriginalEntered = new List<ColliderReporter>(_entered(obj) ?? new List<ColliderReporter>()),
                        OriginalEnteredInverse = new List<ColliderReporter>(_enteredInverse(obj) ?? new List<ColliderReporter>()),
                        Reporters = CollectReporters(_reporters(obj)),
                        InverseReporters = CollectReporters(_inverseReporters(obj))
                    });
                }
                catch (Exception ex)
                {
                    PhotoLog.Debug($"skip culling volume {obj?.name}: {ex.Message}");
                }
            }

            stack.Push("camera-anchored culling", Restore);
            Active = true;
            ForcedUpdates = 0;
            _lastPosition = new Vector3(float.NaN, 0f, 0f);
            _nextRefresh = 0f;
            Status = $"ACTIVE ({_volumes.Count} volumes)";
            PhotoLog.Info($"Camera-anchored culling active over {_volumes.Count} volumes.");
        }

        public void Tick(Vector3 cameraPosition)
        {
            if (!Active)
            {
                return;
            }

            float now = Time.unscaledTime;
            bool moved = float.IsNaN(_lastPosition.x) || (cameraPosition - _lastPosition).sqrMagnitude > RefreshDistance * RefreshDistance;
            if (moved || now >= _nextRefresh)
            {
                _lastPosition = cameraPosition;
                _nextRefresh = now + RefreshInterval;
                Evaluate(cameraPosition);
            }

            foreach (Volume v in _volumes)
            {
                if (v.PendingFrames <= 0 || v.Obj == null)
                {
                    continue;
                }
                if (!_pendingUpdate(v.Obj))
                {
                    v.PendingFrames = 0;
                }
                else if (--v.PendingFrames == 0)
                {
                    v.Obj.ManualUpdate(0f);
                    ForcedUpdates++;
                }
            }
        }

        private void Evaluate(Vector3 p)
        {
            int inside = 0;
            foreach (Volume v in _volumes)
            {
                if (v.Obj == null)
                {
                    continue;
                }

                bool changed = Rewrite(_entered(v.Obj), v.Reporters, p);
                changed |= Rewrite(_enteredInverse(v.Obj), v.InverseReporters, p);
                if (v.Obj.HasEntered)
                {
                    inside++;
                }
                if (changed)
                {
                    v.Obj.UpdateComponentsStatusOnUpdate();
                    v.PendingFrames = 3;
                }
            }
            CameraInsideCount = inside;
        }

        private bool Rewrite(List<ColliderReporter> list, List<(ColliderReporter reporter, Collider[] colliders)> reporters, Vector3 p)
        {
            if (list == null)
            {
                return false;
            }

            _scratch.Clear();
            foreach ((ColliderReporter reporter, Collider[] colliders) in reporters)
            {
                if (reporter != null && ContainsPoint(colliders, p))
                {
                    _scratch.Add(reporter);
                }
            }

            if (SameMembers(list, _scratch))
            {
                return false;
            }
            list.Clear();
            list.AddRange(_scratch);
            return true;
        }

        private void Restore()
        {
            if (!Active)
            {
                return;
            }
            Active = false;
            Vector3? playerPoint = RestorePoint?.Invoke();

            foreach (Volume v in _volumes)
            {
                if (v.Obj == null)
                {
                    continue;
                }
                try
                {
                    if (playerPoint.HasValue)
                    {
                        Rewrite(_entered(v.Obj), v.Reporters, playerPoint.Value);
                        Rewrite(_enteredInverse(v.Obj), v.InverseReporters, playerPoint.Value);
                    }
                    else
                    {
                        List<ColliderReporter> entered = _entered(v.Obj);
                        List<ColliderReporter> enteredInverse = _enteredInverse(v.Obj);
                        entered?.Clear();
                        entered?.AddRange(v.OriginalEntered);
                        enteredInverse?.Clear();
                        enteredInverse?.AddRange(v.OriginalEnteredInverse);
                    }
                    v.Obj.UpdateComponentsStatusOnUpdate();
                    v.Obj.ManualUpdate(0f);
                }
                catch (Exception ex)
                {
                    PhotoLog.Warn($"Culling volume {v.Obj.name} restore failed: {ex.Message}");
                }
            }
            _volumes.Clear();
            Status = "restored";
        }

        private static List<(ColliderReporter, Collider[])> CollectReporters(List<ColliderReporter> reporters)
        {
            var result = new List<(ColliderReporter, Collider[])>();
            if (reporters == null)
            {
                return result;
            }
            foreach (ColliderReporter r in reporters)
            {
                if (r != null)
                {
                    result.Add((r, r.GetComponents<Collider>()));
                }
            }
            return result;
        }

        private static bool ContainsPoint(Collider[] colliders, Vector3 p)
        {
            foreach (Collider c in colliders)
            {
                if (c == null || !c.enabled || !c.bounds.Contains(p))
                {
                    continue;
                }
                bool exact = c is BoxCollider || c is SphereCollider || c is CapsuleCollider || (c is MeshCollider mesh && mesh.convex);
                if (!exact || (c.ClosestPoint(p) - p).sqrMagnitude < 1e-6f)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool SameMembers(List<ColliderReporter> a, List<ColliderReporter> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            foreach (ColliderReporter r in b)
            {
                if (!a.Contains(r))
                {
                    return false;
                }
            }
            return true;
        }

        private static AccessTools.FieldRef<DisablerCullingObjectBase, T> SafeRef<T>(string field)
        {
            try
            {
                return AccessTools.FieldRefAccess<DisablerCullingObjectBase, T>(field);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
