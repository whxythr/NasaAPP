using System;

namespace SpaceSim.Mathematics
{
    /// <summary>
    /// Double-precision unit quaternion for rotations in the right-handed physics frame.
    /// Convention: q = (x, y, z, w) = (n·sin(θ/2), cos(θ/2)) rotates counter-clockwise by θ about n.
    /// </summary>
    [Serializable]
    public struct QuaternionD
    {
        public double x;
        public double y;
        public double z;
        public double w;

        public QuaternionD(double x, double y, double z, double w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public static QuaternionD Identity => new QuaternionD(0.0, 0.0, 0.0, 1.0);

        public static QuaternionD AngleAxis(double angleRadians, Vector3d axis)
        {
            Vector3d n = axis.Normalized;
            double half = 0.5 * angleRadians;
            double s = Math.Sin(half);
            return new QuaternionD(n.x * s, n.y * s, n.z * s, Math.Cos(half));
        }

        public QuaternionD Conjugate => new QuaternionD(-x, -y, -z, w);

        public QuaternionD Normalized
        {
            get
            {
                double m = Math.Sqrt(x * x + y * y + z * z + w * w);
                return m > 0.0 ? new QuaternionD(x / m, y / m, z / m, w / m) : Identity;
            }
        }

        /// <summary>Hamilton product: applying (a * b) to a vector rotates by b first, then by a.</summary>
        public static QuaternionD operator *(QuaternionD a, QuaternionD b) => new QuaternionD(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);

        /// <summary>Rotates a vector by this (unit) quaternion.</summary>
        public Vector3d Rotate(Vector3d v)
        {
            // v' = v + 2w(q×v) + 2 q×(q×v)
            var q = new Vector3d(x, y, z);
            Vector3d t = 2.0 * Vector3d.Cross(q, v);
            return v + w * t + Vector3d.Cross(q, t);
        }

        public static Vector3d operator *(QuaternionD q, Vector3d v) => q.Rotate(v);
    }
}
