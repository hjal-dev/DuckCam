using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Actors
{
    internal enum PoseRegion
    {
        Torso,
        Head,
        LeftArm,
        RightArm,
        LeftHand,
        RightHand,
        LeftLeg,
        RightLeg
    }

    internal enum PoseLimb
    {
        LeftHand,
        RightHand,
        LeftFoot,
        RightFoot
    }

    internal enum PinMode
    {
        Off,
        World,
        Body
    }

    internal sealed class PosePart
    {
        public string Key;
        public string Label;
        public PoseRegion Region;
        public PoseRegion? AlsoIn;
        public Transform Bone;
        public Transform Tip;
        public Vector3 TipLocal;
        public PosePart Parent;
        public PosePart Mirror;
        public Vector3 Limit;
        public Quaternion Frame = Quaternion.identity;
        public PoseLimb? Limb;
        public bool IsHips;
        public bool IsHead;
        public int FingerSegment;
        public bool Left;
        public bool Right;
        public string Group;
        public int Depth;
        public Quaternion? BindRootRotation;

        public Vector3 TipPosition => Tip != null ? Tip.position : Bone.TransformPoint(TipLocal);

        public bool InRegion(PoseRegion r) => Region == r || AlsoIn == r;

        public Quaternion Compose(Vector3 angles) => Frame * Quaternion.Euler(angles) * Quaternion.Inverse(Frame);

        public Vector3 Decompose(Quaternion offset)
        {
            Vector3 e = (Quaternion.Inverse(Frame) * offset * Frame).eulerAngles;
            return new Vector3(Mathf.DeltaAngle(0f, e.x), Mathf.DeltaAngle(0f, e.y), Mathf.DeltaAngle(0f, e.z));
        }

        public Vector3 Decompose(Quaternion offset, Vector3 near)
        {
            Vector3 a = Decompose(offset);
            var b = new Vector3(Mathf.DeltaAngle(0f, 180f - a.x), Mathf.DeltaAngle(0f, a.y + 180f), Mathf.DeltaAngle(0f, a.z + 180f));
            return Distance(a, near) <= Distance(b, near) ? a : b;
        }

        private static float Distance(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(Mathf.DeltaAngle(a.x, b.x)) + Mathf.Abs(Mathf.DeltaAngle(a.y, b.y)) + Mathf.Abs(Mathf.DeltaAngle(a.z, b.z));
        }
    }

    internal enum BoneSet
    {
        Torso,
        Head,
        ArmL,
        ArmR,
        FingersL,
        FingersR,
        LegL,
        LegR,
        Twist,
        Gear
    }

    internal sealed class SkelBone
    {
        public string Name;
        public string Short;
        public string Path;
        public Transform Bone;
        public SkelBone Parent;
        public readonly List<SkelBone> Children = new List<SkelBone>();
        public SkelBone TailBone;
        public Vector3 TipLocal;
        public int Depth;
        public BoneSet Set;
        public SkelBone Mirror;
        public PosePart Part;
        public bool HasBind;
        public Vector3 BindRootPosition;
        public Quaternion BindRootRotation = Quaternion.identity;
        public Quaternion BindLocal = Quaternion.identity;
        public Vector3 BindLocalPosition;
        public Quaternion Frame = Quaternion.identity;
        public float Length;
        public bool LocationUnlocked;

        public Vector3 Head => Bone.position;

        public Vector3 Tail => TailBone != null ? TailBone.Bone.position : Bone.TransformPoint(TipLocal);

        public Vector3 Axis(int i)
        {
            Quaternion r = Bone.rotation * Frame;
            return i == 0 ? r * Vector3.right : i == 1 ? r * Vector3.up : r * Vector3.forward;
        }

        public bool IsDescendantOf(SkelBone other)
        {
            for (SkelBone b = Parent; b != null; b = b.Parent)
            {
                if (b == other)
                {
                    return true;
                }
            }
            return false;
        }
    }

    internal sealed class PoseSkeleton
    {
        private const string Prefix = "Base Human";
        private static readonly string[] FingerNames = { "Thumb", "Index", "Middle", "Ring", "Little" };

        public readonly List<PosePart> Parts = new List<PosePart>();
        public readonly Dictionary<string, PosePart> ByKey = new Dictionary<string, PosePart>();
        public PosePart Hips;
        public Transform Root { get; }
        public readonly Dictionary<Transform, Quaternion> BindLocal = new Dictionary<Transform, Quaternion>();
        public Dictionary<string, Transform> Bones { get; }

        public readonly List<SkelBone> Map = new List<SkelBone>();
        public readonly Dictionary<string, SkelBone> MapByName = new Dictionary<string, SkelBone>();
        public readonly List<SkelBone> MapRoots = new List<SkelBone>();
        public string Profile { get; private set; } = "unknown";
        public string AxisReport { get; private set; } = "not checked";
        public bool AxesAsExpected { get; private set; }
        public Transform SkeletonRoot { get; }

        public PoseSkeleton(Transform root, Transform skeletonRoot)
        {
            Root = root;
            SkeletonRoot = skeletonRoot;
            Dictionary<string, Transform> bones = PickBones(root, skeletonRoot, out string report);
            Bones = bones;
            PhotoLog.Info("Pose editor skeleton: " + report);

            Dictionary<Transform, Matrix4x4> bindRoot = ReadBind(root);

            Add(bones, "Pelvis", "Hips", PoseRegion.Torso, null, new Vector3(180f, 180f, 180f)).IsHips = true;
            Add(bones, "Spine1", "Lower Back", PoseRegion.Torso, null, new Vector3(35f, 30f, 30f));
            Add(bones, "Spine2", "Middle Back", PoseRegion.Torso, null, new Vector3(35f, 30f, 30f));
            Add(bones, "Spine3", "Upper Back", PoseRegion.Torso, null, new Vector3(35f, 30f, 30f));
            Add(bones, "Ribcage", "Chest", PoseRegion.Torso, null, new Vector3(25f, 25f, 25f));
            Add(bones, "Neck", "Neck", PoseRegion.Head, null, new Vector3(45f, 40f, 55f));
            PosePart head = Add(bones, "Head", "Head", PoseRegion.Head, null, new Vector3(50f, 40f, 60f));
            if (head != null)
            {
                head.IsHead = true;
            }

            foreach (bool left in new[] { true, false })
            {
                string s = left ? "L" : "R";
                PoseRegion arm = left ? PoseRegion.LeftArm : PoseRegion.RightArm;
                PoseRegion hand = left ? PoseRegion.LeftHand : PoseRegion.RightHand;
                PoseRegion leg = left ? PoseRegion.LeftLeg : PoseRegion.RightLeg;
                Side(Add(bones, s + "Collarbone", "Shoulder", arm, null, new Vector3(25f, 25f, 10f)), left, "Arm");
                Side(Add(bones, s + "Upperarm", "Upper Arm", arm, null, new Vector3(170f, 120f, 90f)), left, "Arm");
                Side(Add(bones, s + "Forearm1", "Forearm", arm, null, new Vector3(150f, 25f, 90f)), left, "Arm");
                PosePart palm = Side(Add(bones, s + "Palm", "Hand", hand, arm, new Vector3(80f, 45f, 60f)), left, "Arm");
                if (palm != null)
                {
                    palm.Limb = left ? PoseLimb.LeftHand : PoseLimb.RightHand;
                }
                for (int f = 1; f <= 5; f++)
                {
                    for (int seg = 1; seg <= 3; seg++)
                    {
                        Vector3 limit = f == 1 ? new Vector3(70f, 50f, 45f) : seg == 1 ? new Vector3(90f, 25f, 15f) : new Vector3(100f, 8f, 8f);
                        PosePart p = Side(Add(bones, $"{s}Digit{f}{seg}", $"{FingerNames[f - 1]} {seg}", hand, null, limit), left, "Arm");
                        if (p != null)
                        {
                            p.FingerSegment = seg;
                        }
                    }
                }
                Side(Add(bones, s + "Thigh1", "Thigh", leg, null, new Vector3(125f, 80f, 45f)), left, "Leg");
                Side(Add(bones, s + "Calf", "Shin", leg, null, new Vector3(150f, 10f, 15f)), left, "Leg");
                PosePart foot = Side(Add(bones, s + "Foot", "Foot", leg, null, new Vector3(50f, 35f, 25f)), left, "Leg");
                if (foot != null)
                {
                    foot.Limb = left ? PoseLimb.LeftFoot : PoseLimb.RightFoot;
                    Transform toe = foot.Bone.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != foot.Bone && t.name.StartsWith(Prefix + s + "Toe", StringComparison.Ordinal));
                    if (toe != null)
                    {
                        Side(AddBone(toe, toe.name, "Toes", leg, null, new Vector3(40f, 10f, 10f)), left, "Leg");
                    }
                }
            }

            Hips = Parts.FirstOrDefault(p => p.IsHips);
            foreach (PosePart p in Parts)
            {
                p.Depth = DepthOf(p.Bone);
                p.Parent = Parts.Where(o => o != p && p.Bone.IsChildOf(o.Bone)).OrderByDescending(o => o.Depth).FirstOrDefault();
                string mirrorKey = p.Left ? p.Key.Replace(Prefix + "L", Prefix + "R") : p.Right ? p.Key.Replace(Prefix + "R", Prefix + "L") : null;
                if (mirrorKey != null)
                {
                    ByKey.TryGetValue(mirrorKey, out p.Mirror);
                }
            }
            Parts.Sort((a, b) => a.Depth.CompareTo(b.Depth));

            foreach (PosePart p in Parts)
            {
                ResolveTip(p);
            }
            foreach (PosePart p in Parts)
            {
                BuildFrame(p, bindRoot);
            }
            BuildBindLocal(bindRoot);
            BuildMap();
        }

        private void BuildMap()
        {
            foreach (KeyValuePair<string, Transform> kv in Bones)
            {
                Transform t = kv.Value;
                if (t == null || (SkeletonRoot != null && !t.IsChildOf(SkeletonRoot)))
                {
                    continue;
                }
                var b = new SkelBone { Name = kv.Key, Short = kv.Key.Substring(Prefix.Length), Bone = t, Depth = DepthOf(t) };
                ByKey.TryGetValue(kv.Key, out b.Part);
                b.Set = SetOf(b.Short);
                b.LocationUnlocked = b.Short == "Pelvis";
                Map.Add(b);
                MapByName[b.Name] = b;
            }
            Map.Sort((a, b) => a.Depth != b.Depth ? a.Depth.CompareTo(b.Depth) : string.CompareOrdinal(a.Name, b.Name));
            foreach (SkelBone b in Map)
            {
                for (Transform t = b.Bone.parent; t != null && t != Root; t = t.parent)
                {
                    if (MapByName.TryGetValue(t.name, out SkelBone parent) && parent.Bone == t)
                    {
                        b.Parent = parent;
                        parent.Children.Add(b);
                        break;
                    }
                }
                if (b.Parent == null)
                {
                    MapRoots.Add(b);
                }
                var names = new List<string>();
                for (Transform t = b.Bone; t != null && t != Root; t = t.parent)
                {
                    names.Add(t.name);
                    if (t == SkeletonRoot)
                    {
                        break;
                    }
                }
                names.Reverse();
                b.Path = string.Join("/", names);
                string mirror = b.Short.Length > 1 && (b.Short[0] == 'L' || b.Short[0] == 'R') && char.IsUpper(b.Short[1])
                    ? Prefix + (b.Short[0] == 'L' ? "R" : "L") + b.Short.Substring(1)
                    : null;
                if (mirror != null)
                {
                    MapByName.TryGetValue(mirror, out b.Mirror);
                }
            }
            Dictionary<Transform, Matrix4x4> bindLocals = ReadBindLocals();
            foreach (SkelBone b in Map)
            {
                ReadBindFor(b, bindLocals);
            }
            ChainBindRoot();
            foreach (SkelBone b in Map)
            {
                b.TailBone = TailOf(b);
                if (b.TailBone == null)
                {
                    b.TipLocal = FallbackTip(b);
                }
                BuildBoneFrame(b);
            }
            DetectProfile();
            CheckAxes();
            PhotoLog.Info($"Skeleton map: {Map.Count} bones under '{(SkeletonRoot != null ? SkeletonRoot.name : "none")}', {Map.Count(b => b.HasBind)} with bind data, profile {Profile}. {AxisReport}");
        }

        private static BoneSet SetOf(string s)
        {
            bool left = s.Length > 1 && s[0] == 'L' && char.IsUpper(s[1]);
            bool right = s.Length > 1 && s[0] == 'R' && char.IsUpper(s[1]);
            string n = left || right ? s.Substring(1) : s;
            if (n.StartsWith("Gear", StringComparison.Ordinal) || n == "Backpack")
            {
                return BoneSet.Gear;
            }
            if (n == "Neck" || n == "Head")
            {
                return BoneSet.Head;
            }
            if (n == "Forearm2" || n == "Forearm3" || n == "Thigh2")
            {
                return BoneSet.Twist;
            }
            if (n.StartsWith("Digit", StringComparison.Ordinal))
            {
                return left ? BoneSet.FingersL : BoneSet.FingersR;
            }
            if (n == "Collarbone" || n == "Upperarm" || n.StartsWith("Forearm", StringComparison.Ordinal) || n == "Palm")
            {
                return left ? BoneSet.ArmL : BoneSet.ArmR;
            }
            if (n.StartsWith("Thigh", StringComparison.Ordinal) || n == "Calf" || n == "Foot" || n.StartsWith("Toe", StringComparison.Ordinal))
            {
                return left ? BoneSet.LegL : BoneSet.LegR;
            }
            return BoneSet.Torso;
        }

        private void ReadBindFor(SkelBone b, Dictionary<Transform, Matrix4x4> bindLocals)
        {
            if (bindLocals.TryGetValue(b.Bone, out Matrix4x4 local))
            {
                b.HasBind = true;
                b.BindLocal = local.rotation;
                b.BindLocalPosition = local.GetColumn(3);
            }
            else
            {
                b.BindLocal = b.Bone.localRotation;
                b.BindLocalPosition = b.Bone.localPosition;
            }
        }

        private Dictionary<Transform, Matrix4x4> ReadBindLocals()
        {
            var result = new Dictionary<Transform, Matrix4x4>();
            try
            {
                foreach (SkinnedMeshRenderer smr in Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Transform[] bones = smr.bones;
                    Matrix4x4[] bind = smr.sharedMesh != null ? smr.sharedMesh.bindposes : null;
                    if (bones == null || bind == null || bind.Length != bones.Length)
                    {
                        continue;
                    }
                    var index = new Dictionary<Transform, int>();
                    for (int i = 0; i < bones.Length; i++)
                    {
                        if (bones[i] != null && !index.ContainsKey(bones[i]))
                        {
                            index[bones[i]] = i;
                        }
                    }
                    for (int i = 0; i < bones.Length; i++)
                    {
                        Transform t = bones[i];
                        if (t == null || result.ContainsKey(t) || t.parent == null || !index.TryGetValue(t.parent, out int p))
                        {
                            continue;
                        }
                        result[t] = bind[p] * bind[i].inverse;
                    }
                }
            }
            catch (Exception ex)
            {
                PhotoLog.WarnOnce("posebindlocal", "Pose editor: bind locals not readable: " + ex.Message);
            }
            return result;
        }

        private void ChainBindRoot()
        {
            var chained = new Dictionary<Transform, Matrix4x4>();
            Matrix4x4 rootInv = Root.worldToLocalMatrix;
            foreach (SkelBone b in Map)
            {
                Transform parent = b.Bone.parent;
                Matrix4x4 parentM = parent != null && chained.TryGetValue(parent, out Matrix4x4 pm)
                    ? pm
                    : parent != null ? rootInv * parent.localToWorldMatrix : Matrix4x4.identity;
                Matrix4x4 m = parentM * Matrix4x4.TRS(b.BindLocalPosition, b.BindLocal, b.Bone.localScale);
                chained[b.Bone] = m;
                b.BindRootPosition = m.GetColumn(3);
                b.BindRootRotation = m.rotation;
            }
        }

        private static readonly Dictionary<string, string[]> TailNames = new Dictionary<string, string[]>
        {
            { "Pelvis", new[] { "Spine1" } },
            { "Spine1", new[] { "Spine2" } },
            { "Spine2", new[] { "Spine3" } },
            { "Spine3", new[] { "Neck" } },
            { "Neck", new[] { "Head" } },
            { "Collarbone", new[] { "Upperarm" } },
            { "Upperarm", new[] { "Forearm1" } },
            { "Forearm1", new[] { "Palm" } },
            { "Forearm2", new[] { "Forearm3", "Palm" } },
            { "Forearm3", new[] { "Palm" } },
            { "Palm", new[] { "Digit31", "Digit21" } },
            { "Thigh1", new[] { "Calf" } },
            { "Thigh2", new[] { "Calf" } },
            { "Calf", new[] { "Foot" } },
            { "Foot", new[] { "Toe" } }
        };

        private SkelBone TailOf(SkelBone b)
        {
            string s = b.Short;
            bool sided = s.Length > 1 && (s[0] == 'L' || s[0] == 'R') && char.IsUpper(s[1]);
            string side = sided ? s.Substring(0, 1) : "";
            string n = sided ? s.Substring(1) : s;
            var candidates = new List<string>();
            if (TailNames.TryGetValue(n, out string[] next))
            {
                candidates.AddRange(next.Select(x => side + x));
            }
            if (n.StartsWith("Digit", StringComparison.Ordinal) && n.Length == 7 && n[6] < '3')
            {
                candidates.Add(side + n.Substring(0, 6) + (char)(n[6] + 1));
            }
            if (n == "Foot")
            {
                SkelBone toe = b.Children.FirstOrDefault(c => c.Short.StartsWith(side + "Toe", StringComparison.Ordinal));
                if (toe != null)
                {
                    return toe;
                }
            }
            foreach (string c in candidates)
            {
                if (MapByName.TryGetValue(Prefix + c, out SkelBone tail) && tail.IsDescendantOf(b))
                {
                    return tail;
                }
            }
            return null;
        }

        private Vector3 FallbackTip(SkelBone b)
        {
            if (b.Short == "Head")
            {
                return b.Bone.InverseTransformVector(Root.up * 0.2f);
            }
            Transform from = b.Parent?.Bone ?? b.Bone.parent;
            Vector3 dir = from != null ? b.Bone.position - from.position : Root.up * 0.1f;
            if (dir.sqrMagnitude < 1e-8f)
            {
                dir = Root.up * 0.1f;
            }
            float length = b.Set == BoneSet.FingersL || b.Set == BoneSet.FingersR
                ? Mathf.Clamp(dir.magnitude * 0.8f, 0.015f, 0.04f)
                : b.Set == BoneSet.Gear ? 0.05f : Mathf.Clamp(dir.magnitude * 0.6f, 0.04f, 0.12f);
            return b.Bone.InverseTransformVector(dir.normalized * length);
        }

        private void BuildBoneFrame(SkelBone b)
        {
            Vector3 headRoot = b.BindRootPosition;
            Vector3 tailRoot = b.TailBone != null
                ? b.TailBone.BindRootPosition
                : headRoot + b.BindRootRotation * b.TipLocal;
            Vector3 along = tailRoot - headRoot;
            if (along.sqrMagnitude < 1e-10f)
            {
                along = Vector3.up;
            }
            b.Length = (b.Tail - b.Head).magnitude;
            Vector3 a = along.normalized;
            Vector3 x = Vector3.zero;
            foreach (Vector3 candidate in new[] { Vector3.right, Vector3.forward, Vector3.up })
            {
                Vector3 c = Vector3.ProjectOnPlane(candidate, a);
                if (c.sqrMagnitude > 0.1f)
                {
                    x = c.normalized;
                    break;
                }
            }
            if (b.Short.Length > 1 && b.Short[0] == 'L' && char.IsUpper(b.Short[1]))
            {
                x = -x;
            }
            Quaternion toLocal = Quaternion.Inverse(b.BindRootRotation);
            Vector3 aL = (toLocal * a).normalized;
            Vector3 xL = (toLocal * x).normalized;
            b.Frame = Quaternion.LookRotation(Vector3.Cross(xL, aL).normalized, aL);
        }

        private void DetectProfile()
        {
            bool body = MapByName.ContainsKey(Prefix + "Pelvis") && MapByName.ContainsKey(Prefix + "LThigh1");
            bool arms = MapByName.ContainsKey(Prefix + "LPalm") || MapByName.ContainsKey(Prefix + "RPalm");
            Profile = body ? "eft_body" : arms ? "eft_fp_arms" : "unknown";
        }

        private void CheckAxes()
        {
            if (!MapByName.TryGetValue(Prefix + "LThigh1", out SkelBone lt) || !MapByName.TryGetValue(Prefix + "RThigh1", out SkelBone rt)
                || !MapByName.TryGetValue(Prefix + "Head", out SkelBone head) || !MapByName.TryGetValue(Prefix + "Pelvis", out SkelBone pelvis))
            {
                AxisReport = "Axis check skipped (thighs, pelvis or head missing).";
                return;
            }
            float side = lt.BindRootPosition.x - rt.BindRootPosition.x;
            float up = head.BindRootPosition.y - pelvis.BindRootPosition.y;
            float forward = 0f;
            if (MapByName.TryGetValue(Prefix + "LToe", out SkelBone toe) && MapByName.TryGetValue(Prefix + "LFoot", out SkelBone foot))
            {
                forward = toe.BindRootPosition.z - foot.BindRootPosition.z;
            }
            AxesAsExpected = side < 0f && up > 0f && forward > 0f;
            AxisReport = string.Format(CultureInfo.InvariantCulture,
                "Axis check: left minus right x={0:0.000} (expect <0), head above pelvis y={1:0.000} (expect >0), toe ahead of foot z={2:0.000} (expect >0): {3}.",
                side, up, forward, AxesAsExpected ? "left=-X, up=+Y, forward=+Z, Unity->Blender (x,y,z)->(-x,-z,y) holds" : "UNEXPECTED, the Blender conversion needs revising");
        }

        public SkelBone Find(string name) => name != null && MapByName.TryGetValue(name, out SkelBone b) ? b : null;

        internal static Dictionary<string, Transform> PickBones(Transform root, Transform skeletonRoot, out string report)
        {
            var skinned = new Dictionary<Transform, int>();
            foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.bones == null)
                {
                    continue;
                }
                foreach (Transform b in smr.bones)
                {
                    if (b != null)
                    {
                        skinned[b] = skinned.TryGetValue(b, out int n) ? n + 1 : 1;
                    }
                }
            }
            var candidates = new Dictionary<string, List<Transform>>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    continue;
                }
                if (!candidates.TryGetValue(t.name, out List<Transform> list))
                {
                    candidates[t.name] = list = new List<Transform>();
                }
                list.Add(t);
            }
            var result = new Dictionary<string, Transform>();
            int duplicated = 0;
            foreach (KeyValuePair<string, List<Transform>> kv in candidates)
            {
                if (kv.Value.Count > 1)
                {
                    duplicated++;
                }
                result[kv.Key] = kv.Value
                    .OrderBy(t => skeletonRoot != null && t.IsChildOf(skeletonRoot) ? 0 : 1)
                    .ThenByDescending(t => skinned.TryGetValue(t, out int n) ? n : 0)
                    .ThenBy(DepthOf)
                    .First();
            }
            report = $"{candidates.Count} bone names, {duplicated} with duplicate copies, body skeleton root '{(skeletonRoot != null ? skeletonRoot.name : "none")}'";
            return result;
        }

        public IEnumerable<PosePart> InRegion(PoseRegion r) => Parts.Where(p => p.InRegion(r)).OrderBy(p => p.Region == r ? 0 : 1).ThenBy(p => p.Depth);

        public PosePart LimbEnd(PoseLimb limb) => Parts.FirstOrDefault(p => p.Limb == limb);

        private PosePart Add(Dictionary<string, Transform> bones, string suffix, string label, PoseRegion region, PoseRegion? alsoIn, Vector3 limit)
        {
            return bones.TryGetValue(Prefix + suffix, out Transform t) ? AddBone(t, t.name, label, region, alsoIn, limit) : null;
        }

        private PosePart AddBone(Transform t, string key, string label, PoseRegion region, PoseRegion? alsoIn, Vector3 limit)
        {
            if (ByKey.ContainsKey(key))
            {
                return ByKey[key];
            }
            var p = new PosePart { Key = key, Label = label, Region = region, AlsoIn = alsoIn, Bone = t, Limit = limit };
            Parts.Add(p);
            ByKey[key] = p;
            return p;
        }

        private static PosePart Side(PosePart p, bool left, string group)
        {
            if (p != null)
            {
                p.Left = left;
                p.Right = !left;
                p.Group = group;
            }
            return p;
        }

        private static int DepthOf(Transform t)
        {
            int d = 0;
            for (; t != null; t = t.parent)
            {
                d++;
            }
            return d;
        }

        private void ResolveTip(PosePart p)
        {
            string n = p.Key.Substring(Prefix.Length);
            string s = p.Left ? "L" : "R";
            string next = null;
            if (p.IsHips) next = "Spine1";
            else if (n == "Spine1") next = "Spine2";
            else if (n == "Spine2") next = "Spine3";
            else if (n == "Spine3") next = "Ribcage";
            else if (n == "Ribcage") next = "Neck";
            else if (n == "Neck") next = "Head";
            else if (n.EndsWith("Collarbone", StringComparison.Ordinal)) next = s + "Upperarm";
            else if (n.EndsWith("Upperarm", StringComparison.Ordinal)) next = s + "Forearm1";
            else if (n.EndsWith("Forearm1", StringComparison.Ordinal)) next = s + "Palm";
            else if (n.EndsWith("Palm", StringComparison.Ordinal)) next = s + "Digit31";
            else if (n.EndsWith("Thigh1", StringComparison.Ordinal)) next = s + "Calf";
            else if (n.EndsWith("Calf", StringComparison.Ordinal)) next = s + "Foot";
            else if (p.FingerSegment > 0 && p.FingerSegment < 3) next = n.Substring(0, n.Length - 1) + (p.FingerSegment + 1);

            if (next != null && ByKey.TryGetValue(Prefix + next, out PosePart tip) && tip.Bone.IsChildOf(p.Bone) && tip.Bone != p.Bone)
            {
                p.Tip = tip.Bone;
                return;
            }
            if (p.Limb == PoseLimb.LeftFoot || p.Limb == PoseLimb.RightFoot)
            {
                PosePart toe = Parts.FirstOrDefault(o => o.Parent == p || (o.Bone.parent == p.Bone && o.Label == "Toes"));
                if (toe != null)
                {
                    p.Tip = toe.Bone;
                    return;
                }
            }
            Transform from = p.Parent?.Bone ?? p.Bone.parent;
            Vector3 dir = from != null ? p.Bone.position - from.position : Root.up * 0.1f;
            float length = p.IsHead ? 0.2f : p.FingerSegment > 0 ? Mathf.Clamp(dir.magnitude * 0.8f, 0.015f, 0.04f) : Mathf.Clamp(dir.magnitude * 0.6f, 0.04f, 0.12f);
            if (p.IsHead)
            {
                dir = Root.up;
            }
            p.TipLocal = p.Bone.InverseTransformVector(dir.normalized * length);
        }

        private void BuildFrame(PosePart p, Dictionary<Transform, Matrix4x4> bindRoot)
        {
            Quaternion boneRot;
            Vector3 along;
            if (bindRoot.TryGetValue(p.Bone, out Matrix4x4 bm) && (p.Tip == null || bindRoot.ContainsKey(p.Tip)))
            {
                boneRot = bm.rotation;
                p.BindRootRotation = boneRot;
                Vector3 bonePos = bm.GetColumn(3);
                Vector3 tipPos = p.Tip != null ? (Vector3)bindRoot[p.Tip].GetColumn(3) : bonePos + boneRot * p.TipLocal;
                along = tipPos - bonePos;
            }
            else
            {
                boneRot = Quaternion.Inverse(Root.rotation) * p.Bone.rotation;
                along = Root.InverseTransformVector(p.TipPosition - p.Bone.position);
            }
            if (along.sqrMagnitude < 1e-8f)
            {
                along = Vector3.up;
            }
            Vector3 a = along.normalized;
            Vector3 b = Vector3.zero;
            foreach (Vector3 candidate in new[] { Vector3.right, Vector3.forward, Vector3.up })
            {
                Vector3 c = Vector3.ProjectOnPlane(candidate, a);
                if (c.sqrMagnitude > 0.1f)
                {
                    b = c.normalized;
                    break;
                }
            }
            Quaternion toLocal = Quaternion.Inverse(boneRot);
            Vector3 aL = (toLocal * a).normalized;
            Vector3 bL = (toLocal * b).normalized;
            Vector3 sL = Vector3.Cross(aL, bL).normalized;
            p.Frame = Quaternion.LookRotation(aL, sL);
        }

        private Dictionary<Transform, Matrix4x4> ReadBind(Transform root)
        {
            var result = new Dictionary<Transform, Matrix4x4>();
            try
            {
                Matrix4x4 rootInv = root.worldToLocalMatrix;
                foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Transform[] bones = smr.bones;
                    Matrix4x4[] bind = smr.sharedMesh != null ? smr.sharedMesh.bindposes : null;
                    if (bones == null || bind == null || bind.Length != bones.Length)
                    {
                        continue;
                    }
                    Matrix4x4 rendererToRoot = rootInv * smr.transform.localToWorldMatrix;
                    for (int i = 0; i < bones.Length; i++)
                    {
                        Transform b = bones[i];
                        if (b != null && !result.ContainsKey(b))
                        {
                            result[b] = rendererToRoot * bind[i].inverse;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PhotoLog.WarnOnce("posebind", "Pose editor: bind pose not readable: " + ex.Message);
            }
            return result;
        }

        private void BuildBindLocal(Dictionary<Transform, Matrix4x4> bindRoot)
        {
            foreach (PosePart p in Parts)
            {
                Transform parent = p.Bone.parent;
                if (parent != null && bindRoot.TryGetValue(p.Bone, out Matrix4x4 child) && bindRoot.TryGetValue(parent, out Matrix4x4 par))
                {
                    BindLocal[p.Bone] = Quaternion.Inverse(par.rotation) * child.rotation;
                }
            }
        }
    }
}
