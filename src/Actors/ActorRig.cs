using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using RootMotion.FinalIK;
using UnityEngine;

namespace DuckCam.Actors
{
    internal enum LookMode
    {
        Off,
        Camera,
        Character,
        Point
    }

    internal sealed class RigSettings
    {
        public LookMode Look = LookMode.Off;
        public PhotoActor LookActor;
        public Vector3? LookPoint;
        public float LookClamp = 70f;
        public float HeadTurn;
        public float HeadNod;
        public float HeadTilt;

        public bool BodyAim;
        public float BodyAimWeight = 0.5f;
    }

    internal sealed class ActorRig
    {
        private const float BodyAimClamp = 60f;

        private readonly Transform _root;
        private readonly Transform _head;
        private readonly Transform[] _spine;
        private readonly Transform[] _leftArm;
        private readonly Transform[] _rightArm;
        private readonly Transform[] _leftLeg;
        private readonly Transform[] _rightLeg;

        private readonly List<Transform> _tracked = new List<Transform>();
        private readonly Dictionary<Transform, Quaternion> _baseLocal = new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<Transform, Quaternion> _written = new Dictionary<Transform, Quaternion>();
        private Vector3 _baseHipsPos;
        private Vector3? _writtenHipsPos;
        private Vector3 _headForwardLocal = Vector3.forward;
        private Vector3 _chestForwardLocal = Vector3.forward;
        private bool _baseValid;

        public bool HasHead => _head != null;
        public bool HasSpine => _spine.Length > 0;
        public bool HasArms => _leftArm != null || _rightArm != null;
        public bool HasLegs => _leftLeg != null || _rightLeg != null;
        public Transform Head => _head;

        public Vector3 FaceDirection => _head != null ? _head.rotation * _headForwardLocal : (_root != null ? _root.forward : Vector3.forward);
        public Transform Root => _root;
        public PoseSkeleton Skeleton { get; }

        public DuckCam.Animation.AnimatedPose Override { get; set; }

        public ActorRig(MenuPlayerPoser poser, Transform root)
        {
            _root = root;
            PlayerBones b = poser.PBones;
            _head = b?.Head?.Original;

            var spine = new List<Transform>();
            if (b?.Spine1 != null)
            {
                spine.Add(b.Spine1);
            }
            Transform spine3 = b?.Spine3?.Original;
            if (spine3 != null)
            {
                Transform t = spine3.parent;
                var middle = new List<Transform>();
                while (t != null && t != b.Spine1 && t != root)
                {
                    middle.Add(t);
                    t = t.parent;
                }
                if (t == b.Spine1)
                {
                    middle.Reverse();
                    spine.AddRange(middle);
                }
                spine.Add(spine3);
            }
            _spine = spine.Distinct().ToArray();

            Transform pelvis = b?.Pelvis?.Original;
            Transform known = pelvis ?? b?.Head?.Original ?? b?.Spine3?.Original ?? b?.Spine1 ?? b?.LeftPalm ?? b?.RightPalm;
            for (Transform t = known; pelvis == null && t != null; t = t.parent)
            {
                if (t.name == "Base HumanPelvis")
                {
                    pelvis = t;
                }
            }
            Skeleton = new PoseSkeleton(root, pelvis != null && pelvis.parent != null ? pelvis.parent : pelvis);
            SkelBone liveHead = Skeleton.Find("Base HumanHead");
            if (liveHead != null)
            {
                _head = liveHead.Bone;
            }
            Transform[] liveSpine = new[] { "Base HumanSpine1", "Base HumanSpine2", "Base HumanSpine3" }
                .Select(n => Skeleton.Find(n)?.Bone)
                .Where(t => t != null)
                .ToArray();
            if (liveSpine.Length >= 2)
            {
                _spine = liveSpine;
            }

            _leftArm = ArmChain(b?.LeftPalm) ?? Chain(b?.LeftShoulder?.Original, b?.LeftPalm) ?? PartChain("LUpperarm", "LForearm1", "LPalm");
            _rightArm = ArmChain(b?.RightPalm) ?? Chain(b?.RightShoulder?.Original, b?.RightPalm) ?? PartChain("RUpperarm", "RForearm1", "RPalm");
            _leftLeg = LegChain(b?.LeftThigh1?.Original) ?? PartChain("LThigh1", "LCalf", "LFoot");
            _rightLeg = LegChain(b?.RightThigh1?.Original) ?? PartChain("RThigh1", "RCalf", "RFoot");
            var tracked = new HashSet<Transform>(Skeleton.Parts.Select(p => p.Bone));
            tracked.UnionWith(Skeleton.Map.Select(b => b.Bone));
            if (_head != null)
            {
                tracked.Add(_head);
            }
            tracked.UnionWith(_spine);
            foreach (Transform[] chain in new[] { _leftArm, _rightArm, _leftLeg, _rightLeg })
            {
                if (chain != null)
                {
                    tracked.UnionWith(chain);
                }
            }
            _tracked.AddRange(tracked.Where(t => t != null));
        }

        private Transform[] PartChain(string upper, string middle, string end)
        {
            PosePart a = Part(upper);
            PosePart m = Part(middle);
            PosePart e = Part(end);
            return a != null && m != null && e != null && m.Bone.IsChildOf(a.Bone) && e.Bone.IsChildOf(m.Bone)
                ? new[] { a.Bone, m.Bone, e.Bone }
                : null;
        }

        private static Transform[] Chain(Transform upper, Transform end)
        {
            if (upper == null || end == null)
            {
                return null;
            }
            Transform t = end;
            Transform childOfUpper = null;
            while (t != null && t != upper)
            {
                childOfUpper = t;
                t = t.parent;
            }
            return t == upper && childOfUpper != null && childOfUpper != end ? new[] { upper, childOfUpper, end } : null;
        }

        private static Transform[] ArmChain(Transform palm)
        {
            if (palm == null)
            {
                return null;
            }
            Transform forearm = null;
            for (Transform t = palm.parent; t != null; t = t.parent)
            {
                if (t.name.IndexOf("Forearm", StringComparison.Ordinal) >= 0)
                {
                    forearm = t;
                }
                else if (forearm != null)
                {
                    return t.name.IndexOf("Upperarm", StringComparison.OrdinalIgnoreCase) >= 0 ? new[] { t, forearm, palm } : null;
                }
            }
            return null;
        }

        private static Transform[] LegChain(Transform thigh)
        {
            if (thigh == null)
            {
                return null;
            }
            Transform calf = thigh.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.IndexOf("Calf", StringComparison.Ordinal) >= 0);
            Transform foot = calf != null ? calf.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.IndexOf("Foot", StringComparison.Ordinal) >= 0) : null;
            return calf != null && foot != null ? new[] { thigh, calf, foot } : null;
        }

        public Transform[] Chain(PoseLimb limb)
        {
            switch (limb)
            {
                case PoseLimb.LeftHand: return _leftArm;
                case PoseLimb.RightHand: return _rightArm;
                case PoseLimb.LeftFoot: return _leftLeg;
                default: return _rightLeg;
            }
        }

        public void CaptureBase()
        {
            _written.Clear();
            _writtenHipsPos = null;
            foreach (Transform t in _tracked)
            {
                if (t != null)
                {
                    _baseLocal[t] = t.localRotation;
                }
            }
            if (Skeleton.Hips != null)
            {
                _baseHipsPos = Skeleton.Hips.Bone.localPosition;
            }
            if (_root != null && _head != null)
            {
                _headForwardLocal = RestForward(_head);
            }
            if (_root != null && _spine.Length > 0)
            {
                _chestForwardLocal = RestForward(_spine[_spine.Length - 1]);
            }
            _baseValid = true;
        }

        private void ResolveBase()
        {
            foreach (Transform t in _tracked)
            {
                if (t == null)
                {
                    continue;
                }
                if (_written.TryGetValue(t, out Quaternion w) && t.localRotation == w && _baseLocal.TryGetValue(t, out Quaternion b))
                {
                    t.localRotation = b;
                }
                else
                {
                    _baseLocal[t] = t.localRotation;
                }
            }
            Transform hips = Skeleton.Hips?.Bone;
            if (hips != null)
            {
                if (_writtenHipsPos.HasValue && (hips.localPosition - _writtenHipsPos.Value).sqrMagnitude < 1e-10f)
                {
                    hips.localPosition = _baseHipsPos;
                }
                else
                {
                    _baseHipsPos = hips.localPosition;
                }
            }
        }

        private void RecordWritten()
        {
            foreach (Transform t in _tracked)
            {
                if (t != null)
                {
                    _written[t] = t.localRotation;
                }
            }
            if (Skeleton.Hips != null)
            {
                _writtenHipsPos = Skeleton.Hips.Bone.localPosition;
            }
        }

        public Quaternion BaseUnder(PosePart p, PoseState s) => BaseUnder(p.Key, p.Bone, s);

        public Quaternion BaseUnder(SkelBone b, PoseState s) => BaseUnder(b.Name, b.Bone, s);

        private Quaternion BaseUnder(string name, Transform bone, PoseState s)
        {
            if (s != null && s.RestBase)
            {
                SkelBone sb = Skeleton.Find(name);
                if (sb != null)
                {
                    return sb.BindLocal;
                }
            }
            if (s?.FullPose != null && s.FullPose.TryGetValue(name, out float[] q) && q.Length == 4)
            {
                return new Quaternion(q[0], q[1], q[2], q[3]);
            }
            return _baseLocal.TryGetValue(bone, out Quaternion b) ? b : bone.localRotation;
        }

        public void Apply(RigSettings s, PoseEdit edit, UnityEngine.Camera camera)
        {
            if (!_baseValid || _root == null)
            {
                return;
            }
            ResolveBase();
            if (Override != null)
            {
                foreach (SkelBone b in Skeleton.Map)
                {
                    if (Override.Bones.TryGetValue(b.Name, out Quaternion q))
                    {
                        b.Bone.localRotation = q;
                    }
                }
                if (Override.Hips.HasValue && Skeleton.Hips != null)
                {
                    Skeleton.Hips.Bone.localPosition = Override.Hips.Value;
                }
                RecordWritten();
                return;
            }
            bool posing = edit != null && edit.Enabled;
            if (posing)
            {
                ApplyEdits(edit.State);
            }
            else
            {
                OnBase?.Invoke();
            }

            Vector3? target = LookTarget(s, camera);
            if (s.BodyAim && target.HasValue && _spine.Length > 0)
            {
                AimSpine(target.Value, s.BodyAimWeight);
            }
            if (_head != null)
            {
                if (target.HasValue)
                {
                    AimHead(target.Value, s.LookClamp);
                }
                if (Mathf.Abs(s.HeadTurn) > 0.01f || Mathf.Abs(s.HeadNod) > 0.01f || Mathf.Abs(s.HeadTilt) > 0.01f)
                {
                    Vector3 face = _head.rotation * _headForwardLocal;
                    _head.rotation = Quaternion.AngleAxis(s.HeadTurn, _root.up)
                                     * Quaternion.AngleAxis(s.HeadNod, Vector3.Cross(_root.up, face).normalized)
                                     * Quaternion.AngleAxis(s.HeadTilt, face)
                                     * _head.rotation;
                }
            }

            if (posing)
            {
                SolvePins(edit.State);
            }
            RecordWritten();
        }

        public Action OnBase { get; set; }

        public bool SpreadWristTwist { get; set; } = true;

        private void ApplyEdits(PoseState s)
        {
            Transform hips = Skeleton.Hips?.Bone;
            if (s.RestBase)
            {
                foreach (SkelBone b in Skeleton.Map)
                {
                    b.Bone.localRotation = b.BindLocal;
                    if (b.LocationUnlocked)
                    {
                        b.Bone.localPosition = b.BindLocalPosition;
                    }
                }
            }
            else if (s.FullPose != null)
            {
                foreach (SkelBone b in Skeleton.Map)
                {
                    if (s.FullPose.TryGetValue(b.Name, out float[] q) && q.Length == 4)
                    {
                        Quaternion r = new Quaternion(q[0], q[1], q[2], q[3]);
                        if (PoseMath.Finite(r))
                        {
                            b.Bone.localRotation = PoseMath.Normalized(r);
                        }
                    }
                }
                if (hips != null && s.FullHips != null && s.FullHips.Length == 3 && PoseMath.Finite(V(s.FullHips)))
                {
                    hips.localPosition = V(s.FullHips);
                }
            }
            OnBase?.Invoke();
            foreach (SkelBone b in Skeleton.Map)
            {
                if (s.Rot.TryGetValue(b.Name, out float[] q) && q.Length == 4)
                {
                    b.Bone.localRotation = b.Bone.localRotation * PoseMath.Normalized(new Quaternion(q[0], q[1], q[2], q[3]));
                }
                if (b.LocationUnlocked && s.Loc.TryGetValue(b.Name, out float[] l) && l.Length == 3)
                {
                    b.Bone.localPosition += V(l);
                }
            }
            if (SpreadWristTwist)
            {
                SpreadTwist(s, "L");
                SpreadTwist(s, "R");
            }
        }

        public static readonly (string bone, float share)[] TwistShares = { ("Forearm1", 0.183f), ("Forearm2", 0.2f), ("Forearm3", 0.274f) };

        private void SpreadTwist(PoseState s, string side)
        {
            SkelBone palm = Skeleton.Find("Base Human" + side + "Palm");
            SkelBone forearm = Skeleton.Find("Base Human" + side + "Forearm1");
            if (palm == null || forearm == null || !s.Rot.TryGetValue(palm.Name, out float[] q) || q.Length != 4)
            {
                return;
            }
            Vector3 axis = palm.Bone.position - forearm.Bone.position;
            if (axis.sqrMagnitude < 1e-8f)
            {
                return;
            }
            axis.Normalize();
            Quaternion offset = PoseMath.Normalized(new Quaternion(q[0], q[1], q[2], q[3]));
            Quaternion world = palm.Bone.rotation;
            Quaternion baseWorld = world * Quaternion.Inverse(offset);
            float twist = PoseMath.TwistAngle(world * Quaternion.Inverse(baseWorld), axis);
            if (Mathf.Abs(twist) < 0.01f)
            {
                return;
            }
            foreach ((string bone, float share) in TwistShares)
            {
                SkelBone t = Skeleton.Find("Base Human" + side + bone);
                if (t != null && palm.IsDescendantOf(t))
                {
                    t.Bone.rotation = Quaternion.AngleAxis(twist * share, axis) * t.Bone.rotation;
                }
            }
            palm.Bone.rotation = world;
        }

        private void SolvePins(PoseState s)
        {
            foreach (PoseLimb limb in (PoseLimb[])Enum.GetValues(typeof(PoseLimb)))
            {
                if (!s.Pins.TryGetValue(limb.ToString(), out PinState pin) || pin.Mode == PinMode.Off)
                {
                    continue;
                }
                Transform[] chain = Chain(limb);
                if (chain == null)
                {
                    continue;
                }
                if (pin.Point == null || pin.Repin)
                {
                    SetPinPoint(limb, pin, chain[2].position - _root.rotation * V(pin.Offset));
                    pin.Repin = false;
                }
                Vector3 goal = PinTarget(limb, pin);
                Vector3 a = chain[0].position;
                Vector3 b = chain[1].position;
                Vector3 c = chain[2].position;
                Vector3 bend = Vector3.Cross(b - a, c - b);
                if (bend.sqrMagnitude < 1e-6f)
                {
                    bend = limb == PoseLimb.LeftFoot || limb == PoseLimb.RightFoot ? _root.right : -_root.right;
                }
                Vector3 axis = goal - a;
                if (Mathf.Abs(pin.Swivel) > 0.01f && axis.sqrMagnitude > 1e-6f)
                {
                    bend = Quaternion.AngleAxis(pin.Swivel, axis.normalized) * bend;
                }
                Quaternion endRotation = chain[2].rotation;
                IKSolverTrigonometric.Solve(chain[0], chain[1], chain[2], goal, bend.normalized, 1f);
                chain[2].rotation = endRotation;
            }
        }

        public Vector3 PinTarget(PoseLimb limb, PinState pin)
        {
            Vector3 p = pin.Point != null && pin.Point.Length == 3 ? V(pin.Point) : Vector3.zero;
            Vector3 world = pin.Mode == PinMode.Body ? Anchor(limb).TransformPoint(p) : p;
            return world + _root.rotation * V(pin.Offset);
        }

        public void SetPinTarget(PoseLimb limb, PinState pin, Vector3 target)
        {
            SetPinPoint(limb, pin, target - _root.rotation * V(pin.Offset));
        }

        private void SetPinPoint(PoseLimb limb, PinState pin, Vector3 world)
        {
            Vector3 p = pin.Mode == PinMode.Body ? Anchor(limb).InverseTransformPoint(world) : world;
            pin.Point = new[] { p.x, p.y, p.z };
        }

        public void SetPinMode(PoseLimb limb, PinState pin, PinMode mode)
        {
            Transform[] chain = Chain(limb);
            if (chain == null)
            {
                return;
            }
            Vector3 current = pin.Mode != PinMode.Off && pin.Point != null ? PinTarget(limb, pin) : chain[2].position;
            pin.Mode = mode;
            if (mode != PinMode.Off)
            {
                SetPinTarget(limb, pin, current);
            }
        }

        private Transform Anchor(PoseLimb limb)
        {
            bool hand = limb == PoseLimb.LeftHand || limb == PoseLimb.RightHand;
            if (hand)
            {
                PosePart chest = Skeleton.Parts.FirstOrDefault(p => p.Key.EndsWith("Ribcage", StringComparison.Ordinal))
                                 ?? Skeleton.Parts.FirstOrDefault(p => p.Key.EndsWith("Spine3", StringComparison.Ordinal));
                if (chest != null)
                {
                    return chest.Bone;
                }
            }
            return Skeleton.Hips?.Bone ?? _root;
        }

        public static Quaternion MirrorOffset(SkelBone from, SkelBone to, Quaternion offset)
        {
            Quaternion delta = from.BindRootRotation * offset * Quaternion.Inverse(from.BindRootRotation);
            return Quaternion.Inverse(to.BindRootRotation) * PoseMath.Mirror(delta) * to.BindRootRotation;
        }

        public void Bake(IEnumerable<Transform> bones, PoseEdit edit)
        {
            foreach (SkelBone b in Skeleton.Map.Where(x => bones.Contains(x.Bone)).OrderBy(x => x.Depth))
            {
                edit.SetRot(b.Name, Quaternion.Inverse(BaseUnder(b, edit.State)) * b.Bone.localRotation);
            }
        }

        public PoseFile Capture(string name)
        {
            var file = new PoseFile { Name = name };
            foreach (SkelBone b in Skeleton.Map)
            {
                Quaternion q = b.Bone.localRotation;
                file.Bones[b.Name] = new[] { q.x, q.y, q.z, q.w };
            }
            if (Skeleton.Hips != null)
            {
                Vector3 h = Skeleton.Hips.Bone.localPosition;
                file.Hips = new[] { h.x, h.y, h.z };
            }
            return file;
        }

        public PoseFile Neutral(bool aPose)
        {
            Dictionary<Transform, Quaternion> saved = Skeleton.Map.ToDictionary(b => b.Bone, b => b.Bone.localRotation);
            try
            {
                foreach (SkelBone b in Skeleton.Map)
                {
                    if (b.Short != "Pelvis" && b.HasBind)
                    {
                        b.Bone.localRotation = b.BindLocal;
                    }
                }
                foreach (bool left in new[] { true, false })
                {
                    string s = left ? "L" : "R";
                    PosePart upper = Part(s + "Upperarm");
                    if (upper != null)
                    {
                        Vector3 out_ = Vector3.ProjectOnPlane(upper.TipPosition - upper.Bone.position, _root.up);
                        if (out_.sqrMagnitude < 1e-6f)
                        {
                            out_ = left ? -_root.right : _root.right;
                        }
                        Vector3 dir = aPose ? (out_.normalized - _root.up).normalized : out_.normalized;
                        foreach (PosePart p in new[] { upper, Part(s + "Forearm1"), Part(s + "Palm") })
                        {
                            Aim(p, dir);
                        }
                    }
                    foreach (PosePart p in new[] { Part(s + "Thigh1"), Part(s + "Calf") })
                    {
                        Aim(p, -_root.up);
                    }
                }
                return Capture("neutral");
            }
            finally
            {
                foreach (KeyValuePair<Transform, Quaternion> kv in saved)
                {
                    kv.Key.localRotation = kv.Value;
                }
            }
        }

        public PosePart Part(string suffix)
        {
            Skeleton.ByKey.TryGetValue("Base Human" + suffix, out PosePart p);
            return p;
        }

        private static void Aim(PosePart p, Vector3 dir)
        {
            if (p == null)
            {
                return;
            }
            Vector3 from = p.TipPosition - p.Bone.position;
            if (from.sqrMagnitude > 1e-8f)
            {
                p.Bone.rotation = Quaternion.FromToRotation(from, dir) * p.Bone.rotation;
            }
        }

        private static Vector3 V(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;

        private Vector3? LookTarget(RigSettings s, UnityEngine.Camera camera)
        {
            switch (s.Look)
            {
                case LookMode.Camera:
                    return camera != null ? camera.transform.position : (Vector3?)null;
                case LookMode.Character:
                    Transform other = s.LookActor?.Rig?.Head ?? s.LookActor?.Root;
                    return other != null ? other.position : (Vector3?)null;
                case LookMode.Point:
                    return s.LookPoint;
                default:
                    return null;
            }
        }

        private Vector3 RestForward(Transform bone)
        {
            Vector3 current = Quaternion.Inverse(bone.rotation) * _root.forward;
            SkelBone b = Skeleton.Map.FirstOrDefault(x => x.Bone == bone);
            if (b == null || !b.HasBind)
            {
                return current;
            }
            Vector3 rest = Quaternion.Inverse(b.BindRootRotation) * Vector3.forward;
            return Vector3.Angle(rest, current) < 100f ? rest.normalized : current;
        }

        private void AimHead(Vector3 target, float clamp)
        {
            Vector3 faceNow = _head.rotation * _headForwardLocal;
            Vector3 eyes = _head.position + _root.up * 0.08f + faceNow * 0.09f;
            Vector3 to = target - eyes;
            if (to.sqrMagnitude < 1e-4f)
            {
                return;
            }
            Vector3 chestFacing = _spine.Length > 0 ? Vector3.ProjectOnPlane(_spine[_spine.Length - 1].rotation * _chestForwardLocal, _root.up) : Vector3.zero;
            Vector3 basis = chestFacing.sqrMagnitude > 1e-4f ? chestFacing.normalized : _root.forward;
            Vector3 clamped = Vector3.RotateTowards(basis, to.normalized, clamp * Mathf.Deg2Rad, 0f);
            _head.rotation = Quaternion.FromToRotation(faceNow, clamped) * _head.rotation;
        }

        private void AimSpine(Vector3 target, float weight)
        {
            Transform chest = _spine[_spine.Length - 1];
            Vector3 to = target - chest.position;
            if (to.sqrMagnitude < 1e-4f)
            {
                return;
            }
            Vector3 clamped = Vector3.RotateTowards(_root.forward, to.normalized, BodyAimClamp * Mathf.Deg2Rad, 0f);
            Vector3 chestNow = chest.rotation * _chestForwardLocal;
            Quaternion delta = Quaternion.FromToRotation(chestNow, clamped);
            Quaternion step = Quaternion.Slerp(Quaternion.identity, delta, Mathf.Clamp01(weight) / _spine.Length);
            foreach (Transform bone in _spine)
            {
                bone.rotation = step * bone.rotation;
            }
        }
    }
}
