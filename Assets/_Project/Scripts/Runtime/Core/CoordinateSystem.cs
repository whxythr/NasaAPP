using SpaceSim.Mathematics;
using SpaceSim.Procedural;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// The only place where physics coordinates become Unity coordinates.
    ///
    /// Physics: right-handed, double precision, SI metres, Z = ecliptic north.
    /// Unity:   left-handed, float, Y up. Mapping: unity = (x, z, y) — swapping two axes is a reflection,
    /// which converts handedness correctly (east stays to the right of north when looking down).
    /// Positions must additionally be made origin-relative through <see cref="FloatingOrigin"/> first.
    /// </summary>
    public static class CoordinateSystem
    {
        public static Vector3 ToUnityVector(Vector3d v) => new Vector3((float)v.x, (float)v.z, (float)v.y);

        public static Vector3d FromUnityVector(Vector3 v) => new Vector3d(v.x, v.z, v.y);

        /// <summary>Rotation R in physics axes → S·R·S in Unity axes, i.e. quaternion (−x, −z, −y, w).</summary>
        public static Quaternion ToUnityRotation(QuaternionD q) => new Quaternion((float)-q.x, (float)-q.z, (float)-q.y, (float)q.w);

        public static QuaternionD FromUnityRotation(Quaternion q) => new QuaternionD(-q.x, -q.z, -q.y, q.w);

        /// <summary>Core colours are sRGB-encoded; Unity vertex colours are used as-is by our shaders (they linearise).</summary>
        public static Color ToColor(ColorRGB c, float a = 1f) => new Color(c.r, c.g, c.b, a);

        /// <summary>
        /// Copies engine-independent chunk geometry into a Unity mesh: swaps axes and reverses triangle winding
        /// (the reflection flips orientation; Unity front faces are clockwise).
        /// </summary>
        public static void FillMesh(ChunkMeshData data, Mesh mesh)
        {
            int n = data.VertexCount;
            var vertices = new Vector3[n];
            var normals = new Vector3[n];
            var colors = new Color[n];
            for (int i = 0; i < n; i++)
            {
                vertices[i] = new Vector3(data.Positions[i * 3], data.Positions[i * 3 + 2], data.Positions[i * 3 + 1]);
                normals[i] = new Vector3(data.Normals[i * 3], data.Normals[i * 3 + 2], data.Normals[i * 3 + 1]);
                colors[i] = new Color(data.Colors[i * 4], data.Colors[i * 4 + 1], data.Colors[i * 4 + 2], data.Colors[i * 4 + 3]);
            }
            var tris = new int[data.Triangles.Length];
            for (int t = 0; t < tris.Length; t += 3)
            {
                tris[t] = data.Triangles[t];
                tris[t + 1] = data.Triangles[t + 2];
                tris[t + 2] = data.Triangles[t + 1];
            }
            mesh.Clear();
            mesh.indexFormat = n > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.colors = colors;
            mesh.triangles = tris;
            float b = (float)data.BoundingRadius;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(2f * b, 2f * b, 2f * b));
        }
    }
}
