using System;
using SpaceSim.Mathematics;

namespace SpaceSim.Procedural
{
    /// <summary>Address of a quadtree chunk on one of the six cube faces.</summary>
    public readonly struct ChunkId : IEquatable<ChunkId>
    {
        public ChunkId(int face, int level, int x, int y)
        {
            Face = face;
            Level = level;
            X = x;
            Y = y;
        }

        public int Face { get; }
        public int Level { get; }
        public int X { get; }
        public int Y { get; }

        /// <summary>Child 0..3 (bit 0 → +x, bit 1 → +y) at the next level.</summary>
        public ChunkId Child(int index) => new ChunkId(Face, Level + 1, X * 2 + (index & 1), Y * 2 + (index >> 1));

        public static ChunkId Root(int face) => new ChunkId(face, 0, 0, 0);

        public bool Equals(ChunkId o) => Face == o.Face && Level == o.Level && X == o.X && Y == o.Y;
        public override bool Equals(object obj) => obj is ChunkId o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(Face, Level, X, Y);
        public override string ToString() => "F" + Face + "/L" + Level + "/" + X + "," + Y;
    }

    /// <summary>
    /// Cube-sphere parameterisation. Each face is a [−1,1]² grid on a cube; points are projected to the sphere
    /// with the "spherified cube" mapping, which spreads vertices far more evenly than normalising (no pole
    /// pinching as on a UV sphere). Grid coordinates are dyadic rationals, so vertices on shared face edges are
    /// bit-identical from both faces → no seams.
    /// </summary>
    public static class CubeSphere
    {
        // Face normal N and tangents A, B with A × B = N (right-handed), so triangles (i,j),(i+1,j),(i,j+1)
        // are counter-clockwise seen from outside.
        static readonly Vector3d[] N =
        {
            new Vector3d(1, 0, 0), new Vector3d(-1, 0, 0), new Vector3d(0, 1, 0),
            new Vector3d(0, -1, 0), new Vector3d(0, 0, 1), new Vector3d(0, 0, -1)
        };
        static readonly Vector3d[] A =
        {
            new Vector3d(0, 1, 0), new Vector3d(0, -1, 0), new Vector3d(0, 0, 1),
            new Vector3d(0, 0, -1), new Vector3d(1, 0, 0), new Vector3d(-1, 0, 0)
        };
        static readonly Vector3d[] B =
        {
            new Vector3d(0, 0, 1), new Vector3d(0, 0, 1), new Vector3d(1, 0, 0),
            new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 1, 0)
        };

        public const int FaceCount = 6;

        public static Vector3d CubePoint(int face, double u, double v) => N[face] + A[face] * u + B[face] * v;

        /// <summary>Maps a point on the cube surface to the unit sphere (uses only sqrt → deterministic).</summary>
        public static Vector3d Spherify(Vector3d c)
        {
            double x2 = c.x * c.x, y2 = c.y * c.y, z2 = c.z * c.z;
            var s = new Vector3d(
                c.x * Math.Sqrt(Math.Max(0.0, 1.0 - y2 * 0.5 - z2 * 0.5 + y2 * z2 / 3.0)),
                c.y * Math.Sqrt(Math.Max(0.0, 1.0 - z2 * 0.5 - x2 * 0.5 + z2 * x2 / 3.0)),
                c.z * Math.Sqrt(Math.Max(0.0, 1.0 - x2 * 0.5 - y2 * 0.5 + x2 * y2 / 3.0)));
            return s.Normalized;
        }

        public static Vector3d Direction(int face, double u, double v) => Spherify(CubePoint(face, u, v));

        /// <summary>Face coordinates for a point (s,t) ∈ [0,1]² inside a chunk.</summary>
        public static void ChunkToFace(ChunkId id, double s, double t, out double u, out double v)
        {
            double scale = 2.0 / (1L << id.Level);
            u = -1.0 + (id.X + s) * scale;
            v = -1.0 + (id.Y + t) * scale;
        }

        public static Vector3d ChunkDirection(ChunkId id, double s, double t)
        {
            ChunkToFace(id, s, t, out double u, out double v);
            return Direction(id.Face, u, v);
        }

        /// <summary>Approximate ground length of a chunk edge at a given level [m].</summary>
        public static double ApproximateEdgeLength(int level, double radius) => 0.5 * Math.PI * radius / (1L << level);
    }
}
