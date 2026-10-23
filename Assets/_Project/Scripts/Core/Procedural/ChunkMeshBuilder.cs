using System;
using SpaceSim.Mathematics;
using SpaceSim.Physics;

namespace SpaceSim.Procedural
{
    /// <summary>
    /// Engine-independent geometry of one surface chunk. Positions are relative to <see cref="Center"/>
    /// (planet-fixed, metres) so they stay small enough for float precision. Physics (right-handed) axes;
    /// triangles are counter-clockwise seen from outside. The Unity layer converts axes and winding.
    /// </summary>
    public sealed class ChunkMeshData
    {
        public ChunkId Id;
        public Vector3d Center;
        public int VertexCount;
        public float[] Positions;  // xyz
        public float[] Normals;    // xyz
        public float[] Colors;     // rgba, a = 1 for water
        public int[] Triangles;
        public double BoundingRadius;
        public double MinSurfaceHeight;
        public double MaxSurfaceHeight;

        public long ApproximateBytes =>
            (Positions.Length + Normals.Length + Colors.Length) * 4L + Triangles.Length * 4L + 128L;
    }

    /// <summary>Builds chunk geometry from the deterministic surface sampler. Thread-safe (no shared state).</summary>
    public static class ChunkMeshBuilder
    {
        /// <param name="resolution">Quads per chunk edge (power of two keeps edge vertices exact).</param>
        /// <param name="skirtFraction">Skirt depth as a fraction of the chunk edge length (hides LOD cracks).</param>
        public static ChunkMeshData Build(PlanetSurfaceSampler sampler, ChunkId id, int resolution, double skirtFraction = 0.015)
        {
            if (sampler == null) throw new ArgumentNullException(nameof(sampler));
            int n = Math.Max(2, resolution);
            int side = n + 1;
            int gridCount = side * side;
            int borderCount = 4 * n;
            double R = sampler.PlanetRadius;
            double edge = CubeSphere.ApproximateEdgeLength(id.Level, R);
            double skirtDepth = Math.Max(edge * skirtFraction, 30.0);

            Vector3d center = CubeSphere.ChunkDirection(id, 0.5, 0.5) * R;

            var dirs = new Vector3d[gridCount];
            var pos = new Vector3d[gridCount];
            var data = new ChunkMeshData
            {
                Id = id,
                Center = center,
                VertexCount = gridCount + borderCount,
                Positions = new float[(gridCount + borderCount) * 3],
                Normals = new float[(gridCount + borderCount) * 3],
                Colors = new float[(gridCount + borderCount) * 4],
                MinSurfaceHeight = double.PositiveInfinity,
                MaxSurfaceHeight = double.NegativeInfinity
            };

            for (int j = 0; j < side; j++)
            {
                for (int i = 0; i < side; i++)
                {
                    int k = j * side + i;
                    Vector3d dir = CubeSphere.ChunkDirection(id, (double)i / n, (double)j / n);
                    SurfaceSample smp = sampler.Sample(dir);
                    dirs[k] = dir;
                    pos[k] = dir * (R + smp.SurfaceHeight);
                    data.MinSurfaceHeight = Math.Min(data.MinSurfaceHeight, smp.SurfaceHeight);
                    data.MaxSurfaceHeight = Math.Max(data.MaxSurfaceHeight, smp.SurfaceHeight);
                    data.Colors[k * 4 + 0] = smp.Albedo.r;
                    data.Colors[k * 4 + 1] = smp.Albedo.g;
                    data.Colors[k * 4 + 2] = smp.Albedo.b;
                    data.Colors[k * 4 + 3] = smp.IsWater ? 1f : 0f;
                }
            }

            // Normals: interior from grid neighbours; border from a face-independent tangent-frame finite
            // difference, so vertices shared by neighbouring chunks/faces get identical normals.
            double eps = edge / n / R;
            for (int j = 0; j < side; j++)
            {
                for (int i = 0; i < side; i++)
                {
                    int k = j * side + i;
                    Vector3d nrm;
                    if (i > 0 && i < n && j > 0 && j < n)
                    {
                        Vector3d du = pos[k + 1] - pos[k - 1];
                        Vector3d dv = pos[k + side] - pos[k - side];
                        nrm = Vector3d.Cross(du, dv).Normalized;
                    }
                    else
                    {
                        nrm = TangentFrameNormal(sampler, dirs[k], eps, R);
                    }
                    WriteVec(data.Normals, k, nrm);
                    WriteVec(data.Positions, k, pos[k] - center);
                }
            }

            // Skirts: a ring of vertices pushed below each border vertex.
            int[] border = BorderLoop(n);
            for (int b = 0; b < borderCount; b++)
            {
                int src = border[b];
                int dst = gridCount + b;
                WriteVec(data.Positions, dst, pos[src] - dirs[src] * skirtDepth - center);
                Array.Copy(data.Normals, src * 3, data.Normals, dst * 3, 3);
                Array.Copy(data.Colors, src * 4, data.Colors, dst * 4, 4);
            }

            var tris = new int[n * n * 6 + borderCount * 12];
            int t = 0;
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    int v00 = j * side + i, v10 = v00 + 1, v01 = v00 + side, v11 = v01 + 1;
                    tris[t++] = v00; tris[t++] = v10; tris[t++] = v01;
                    tris[t++] = v10; tris[t++] = v11; tris[t++] = v01;
                }
            }
            for (int b = 0; b < borderCount; b++)
            {
                int a0 = border[b], a1 = border[(b + 1) % borderCount];
                int s0 = gridCount + b, s1 = gridCount + (b + 1) % borderCount;
                // Double-sided wall: both windings.
                tris[t++] = a0; tris[t++] = s0; tris[t++] = a1;
                tris[t++] = a1; tris[t++] = s0; tris[t++] = s1;
                tris[t++] = a0; tris[t++] = a1; tris[t++] = s0;
                tris[t++] = a1; tris[t++] = s1; tris[t++] = s0;
            }
            data.Triangles = tris;

            double maxR2 = 0.0;
            for (int v = 0; v < data.VertexCount; v++)
            {
                double x = data.Positions[v * 3], y = data.Positions[v * 3 + 1], z = data.Positions[v * 3 + 2];
                maxR2 = Math.Max(maxR2, x * x + y * y + z * z);
            }
            data.BoundingRadius = Math.Sqrt(maxR2);
            return data;
        }

        static Vector3d TangentFrameNormal(PlanetSurfaceSampler sampler, Vector3d dir, double eps, double R)
        {
            PlanetRotation.LocalBasis(dir, out Vector3d east, out Vector3d north);
            double hE1 = sampler.SurfaceHeight((dir + east * eps).Normalized);
            double hE0 = sampler.SurfaceHeight((dir - east * eps).Normalized);
            double hN1 = sampler.SurfaceHeight((dir + north * eps).Normalized);
            double hN0 = sampler.SurfaceHeight((dir - north * eps).Normalized);
            double ds = 2.0 * eps * R;
            double dhde = (hE1 - hE0) / ds;
            double dhdn = (hN1 - hN0) / ds;
            return (dir - east * dhde - north * dhdn).Normalized;
        }

        /// <summary>Grid indices around the border, in order (counter-clockwise in grid space).</summary>
        static int[] BorderLoop(int n)
        {
            int side = n + 1;
            var loop = new int[4 * n];
            int k = 0;
            for (int i = 0; i < n; i++) loop[k++] = i;                         // bottom: (i, 0)
            for (int j = 0; j < n; j++) loop[k++] = j * side + n;              // right:  (n, j)
            for (int i = n; i > 0; i--) loop[k++] = n * side + i;              // top:    (i, n)
            for (int j = n; j > 0; j--) loop[k++] = j * side;                  // left:   (0, j)
            return loop;
        }

        static void WriteVec(float[] arr, int index, Vector3d v)
        {
            arr[index * 3] = (float)v.x;
            arr[index * 3 + 1] = (float)v.y;
            arr[index * 3 + 2] = (float)v.z;
        }
    }
}
