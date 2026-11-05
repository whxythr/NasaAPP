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

        /// <summary>
        /// Truncated cone along +Y from y = 0 (radius <paramref name="bottom"/>) to y = 1 (radius <paramref name="top"/>), unit height
        /// (scale it). uv.x goes around, uv.y along the axis (0 at the bottom). Optional caps.
        /// </summary>
        public static Mesh Frustum(int segments, float bottom, float top, bool capBottom, bool capTop, string name = "Frustum")
        {
            var v = new System.Collections.Generic.List<Vector3>();
            var n = new System.Collections.Generic.List<Vector3>();
            var uv = new System.Collections.Generic.List<Vector2>();
            var t = new System.Collections.Generic.List<int>();
            float slope = bottom - top; // outward normal tilt for unit height
            for (int i = 0; i <= segments; i++)
            {
                float a = 2f * Mathf.PI * i / segments;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var normal = new Vector3(dir.x, slope, dir.z).normalized;
                v.Add(dir * bottom);
                v.Add(dir * top + Vector3.up);
                n.Add(normal);
                n.Add(normal);
                uv.Add(new Vector2((float)i / segments, 0f));
                uv.Add(new Vector2((float)i / segments, 1f));
            }
            for (int i = 0; i < segments; i++)
            {
                int a = 2 * i, b = a + 1, c = a + 2, d = a + 3;
                t.Add(a); t.Add(b); t.Add(c);
                t.Add(c); t.Add(b); t.Add(d);
            }
            void Cap(float y, float radius, bool up)
            {
                if (radius <= 0f) return;
                int centre = v.Count;
                v.Add(new Vector3(0f, y, 0f));
                n.Add(up ? Vector3.up : new Vector3(0f, -1f, 0f));
                uv.Add(new Vector2(0.5f, y));
                for (int i = 0; i <= segments; i++)
                {
                    float a = 2f * Mathf.PI * i / segments;
                    v.Add(new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius));
                    n.Add(up ? Vector3.up : new Vector3(0f, -1f, 0f));
                    uv.Add(new Vector2((float)i / segments, y));
                }
                for (int i = 0; i < segments; i++)
                {
                    int p = centre + 1 + i, q = p + 1;
                    if (up) { t.Add(centre); t.Add(q); t.Add(p); }
                    else { t.Add(centre); t.Add(p); t.Add(q); }
                }
            }
            if (capBottom) Cap(0f, bottom, false);
            if (capTop) Cap(1f, top, true);
            var mesh = new Mesh { name = name };
            mesh.vertices = v.ToArray();
            mesh.normals = n.ToArray();
            mesh.uv = uv.ToArray();
            mesh.triangles = t.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A mesh object (no collider) under <paramref name="parent"/>.</summary>
        public static GameObject MeshObject(Mesh mesh, string name, Transform parent, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>
        /// Opaque lit material for hardware (URP/Lit or Standard): matte by default and WITHOUT environment reflections — the
        /// default skybox reflection made painted metal look blue; real lighting comes from the star and the sky ambient.
        /// </summary>
        public static Material LitMaterial(Shader shader, Color color, float smoothness = 0.25f, float metallic = 0f)
        {
            var m = new Material(shader);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f);
            if (m.HasProperty("_GlossyReflections")) m.SetFloat("_GlossyReflections", 0f);
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            m.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
            return m;
        }

        /// <summary>Material of a SpaceSim effect shader looked up by name (null if the shader is missing).</summary>
        public static Material EffectMaterial(string shaderName)
        {
            Shader s = Shader.Find(shaderName);
            return s != null ? new Material(s) : null;
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
