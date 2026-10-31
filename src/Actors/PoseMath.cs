using UnityEngine;

namespace DuckCam.Actors
{
    internal static class PoseMath
    {
        public static Quaternion Mirror(Quaternion q) => new Quaternion(q.x, -q.y, -q.z, q.w);

        public static Vector3 Mirror(Vector3 v) => new Vector3(-v.x, v.y, v.z);

        public static Quaternion Normalized(Quaternion q)
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return m < 1e-8f ? Quaternion.identity : new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
        }

        public static bool Finite(Quaternion q) => Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w);

        public static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);

        private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        public static Quaternion FromEulerXyz(Vector3 degrees)
        {
            return Quaternion.AngleAxis(degrees.z, Vector3.forward) * Quaternion.AngleAxis(degrees.y, Vector3.up) * Quaternion.AngleAxis(degrees.x, Vector3.right);
        }

        public static Vector3 ToEulerXyz(Quaternion q)
        {
            Matrix4x4 m = Matrix4x4.Rotate(Normalized(q));
            float sy = Mathf.Clamp(-m.m20, -1f, 1f);
            float y = Mathf.Asin(sy);
            float x;
            float z;
            if (Mathf.Abs(sy) < 0.99999f)
            {
                x = Mathf.Atan2(m.m21, m.m22);
                z = Mathf.Atan2(m.m10, m.m00);
            }
            else
            {
                x = Mathf.Atan2(-m.m12, m.m11);
                z = 0f;
            }
            return new Vector3(x, y, z) * Mathf.Rad2Deg;
        }

        public static Vector3 NearestEuler(Vector3 e, Vector3 near)
        {
            var a = new Vector3(Unwrap(e.x, near.x), Unwrap(e.y, near.y), Unwrap(e.z, near.z));
            var b = new Vector3(Unwrap(e.x + 180f, near.x), Unwrap(180f - e.y, near.y), Unwrap(e.z + 180f, near.z));
            return (b - near).sqrMagnitude < (a - near).sqrMagnitude - 1e-3f ? b : a;
        }

        private static float Unwrap(float a, float near) => near + Mathf.DeltaAngle(near, a);

        public static float TwistAngle(Quaternion q, Vector3 axis)
        {
            Vector3 r = new Vector3(q.x, q.y, q.z);
            Vector3 p = Vector3.Project(r, axis);
            var twist = new Quaternion(p.x, p.y, p.z, q.w);
            if (twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w < 1e-10f)
            {
                return 0f;
            }
            twist = Normalized(twist);
            float angle = 2f * Mathf.Atan2(new Vector3(twist.x, twist.y, twist.z).magnitude, twist.w) * Mathf.Rad2Deg;
            if (Vector3.Dot(new Vector3(twist.x, twist.y, twist.z), axis) < 0f)
            {
                angle = -angle;
            }
            return Mathf.DeltaAngle(0f, angle);
        }

        public static bool ClosestOnLine(Ray ray, Vector3 origin, Vector3 dir, out Vector3 point)
        {
            point = origin;
            Vector3 w0 = origin - ray.origin;
            float a = Vector3.Dot(dir, dir);
            float b = Vector3.Dot(dir, ray.direction);
            float c = Vector3.Dot(ray.direction, ray.direction);
            float d = Vector3.Dot(dir, w0);
            float e = Vector3.Dot(ray.direction, w0);
            float denom = a * c - b * b;
            if (Mathf.Abs(denom) < 1e-6f)
            {
                return false;
            }
            float s = (b * e - c * d) / denom;
            point = origin + dir * s;
            return true;
        }

        public static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len = ab.sqrMagnitude;
            float t = len < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
