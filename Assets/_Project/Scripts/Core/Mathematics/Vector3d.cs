using System;
using System.Globalization;

namespace SpaceSim.Mathematics
{
    /// <summary>
    /// Double-precision 3D vector. All physical quantities (positions in metres, velocities in m/s,
    /// accelerations in m/s²) are stored in this type. The physics frame is right-handed;
    /// conversion to Unity's left-handed float space happens only in the Runtime layer.
    /// </summary>
    [Serializable]
    public struct Vector3d : IEquatable<Vector3d>
    {
        public double x;
        public double y;
        public double z;

        public Vector3d(double x, double y, double z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static Vector3d Zero => new Vector3d(0.0, 0.0, 0.0);
        public static Vector3d UnitX => new Vector3d(1.0, 0.0, 0.0);
        public static Vector3d UnitY => new Vector3d(0.0, 1.0, 0.0);
        public static Vector3d UnitZ => new Vector3d(0.0, 0.0, 1.0);

        public double SqrMagnitude => x * x + y * y + z * z;
        public double Magnitude => Math.Sqrt(x * x + y * y + z * z);

        public Vector3d Normalized
        {
            get
            {
                double m = Magnitude;
                return m > 0.0 ? new Vector3d(x / m, y / m, z / m) : Zero;
            }
        }

        public bool IsFinite => !(double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z) ||
                                  double.IsInfinity(x) || double.IsInfinity(y) || double.IsInfinity(z));

        public static double Dot(Vector3d a, Vector3d b) => a.x * b.x + a.y * b.y + a.z * b.z;

        /// <summary>Right-handed cross product.</summary>
        public static Vector3d Cross(Vector3d a, Vector3d b) =>
            new Vector3d(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);

        public static double Distance(Vector3d a, Vector3d b) => (a - b).Magnitude;

        public static Vector3d Lerp(Vector3d a, Vector3d b, double t) =>
            new Vector3d(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);

        /// <summary>Component of <paramref name="v"/> perpendicular to the unit vector <paramref name="unitNormal"/>.</summary>
        public static Vector3d ProjectOnPlane(Vector3d v, Vector3d unitNormal) => v - unitNormal * Dot(v, unitNormal);

        /// <summary>Angle between two vectors in radians, numerically stable for small angles.</summary>
        public static double Angle(Vector3d a, Vector3d b) =>
            Math.Atan2(Cross(a, b).Magnitude, Dot(a, b));

        public static Vector3d operator +(Vector3d a, Vector3d b) => new Vector3d(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3d operator -(Vector3d a, Vector3d b) => new Vector3d(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3d operator -(Vector3d a) => new Vector3d(-a.x, -a.y, -a.z);
        public static Vector3d operator *(Vector3d a, double s) => new Vector3d(a.x * s, a.y * s, a.z * s);
        public static Vector3d operator *(double s, Vector3d a) => new Vector3d(a.x * s, a.y * s, a.z * s);
        public static Vector3d operator /(Vector3d a, double s) => new Vector3d(a.x / s, a.y / s, a.z / s);

        public static bool operator ==(Vector3d a, Vector3d b) => a.x == b.x && a.y == b.y && a.z == b.z;
        public static bool operator !=(Vector3d a, Vector3d b) => !(a == b);

        public bool Equals(Vector3d other) => this == other;
        public override bool Equals(object obj) => obj is Vector3d other && this == other;
        public override int GetHashCode() => HashCode.Combine(x, y, z);

        public override string ToString() => ToString("G6");

        public string ToString(string format) =>
            string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2})",
                x.ToString(format, CultureInfo.InvariantCulture),
                y.ToString(format, CultureInfo.InvariantCulture),
                z.ToString(format, CultureInfo.InvariantCulture));
    }
}
