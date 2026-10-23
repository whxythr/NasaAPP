using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>Small procedural meshes used by the renderer (no imported assets required).</summary>
    public static class ProceduralMeshes
    {
        /// <summary>Unit-radius UV sphere with outward normals (Unity winding).</summary>
        public static Mesh UvSphere(int longitudeSegments, int latitudeSegments, string name = "UvSphere")
        {
            int cols = longitudeSegments + 1, rows = latitudeSegments + 1;
            var vertices = new Vector3[cols * rows];
            var normals = new Vector3[cols * rows];
            var uvs = new Vector2[cols * rows];
            for (int y = 0; y < rows; y++)
            {
                float v = (float)y / latitudeSegments;
                float lat = Mathf.PI * (v - 0.5f);
                for (int x = 0; x < cols; x++)
                {
                    float u = (float)x / longitudeSegments;
                    float lon = 2f * Mathf.PI * u;
                    var p = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                    int i = y * cols + x;
                    vertices[i] = p;
                    normals[i] = p;
                    uvs[i] = new Vector2(u, v);
                }
            }
            var tris = new int[longitudeSegments * latitudeSegments * 6];
            int t = 0;
            for (int y = 0; y < latitudeSegments; y++)
            {
                for (int x = 0; x < longitudeSegments; x++)
                {
                    int a = y * cols + x, b = a + 1, c = a + cols, d = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }
            }
            var mesh = new Mesh { name = name, vertices = vertices, normals = normals, uv = uvs, triangles = tris };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Removes a primitive's collider: SpaceSim does not use Unity physics for flight.</summary>
        public static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(parent, false);
            if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }
    }
}
