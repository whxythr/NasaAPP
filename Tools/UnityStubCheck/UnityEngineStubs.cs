// Minimal compile-time stand-in for the parts of the UnityEngine API used by SpaceSim.Runtime.
// Purpose: type-check the Unity-layer C# outside the Editor (names, types, usings, overloads of OUR code).
// Signatures follow the real Unity API; there are no implementations. This does NOT replace compiling in Unity.
#pragma warning disable CS0067, CS0649, CS0414, CS1591
using System;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; }
        public HideFlags hideFlags { get; set; }
        public int GetInstanceID() => 0;
        public static void Destroy(Object obj) { }
        public static void DestroyImmediate(Object obj) { }
        public static T Instantiate<T>(T original, Transform parent) where T : Object => original;
        public static implicit operator bool(Object o) => o != null;
    }

    public class Component : Object
    {
        public Transform transform => null;
        public GameObject gameObject => null;
        public T GetComponent<T>() => default;
        public T[] GetComponentsInChildren<T>() => new T[0];
    }

    public class Behaviour : Component { public bool enabled { get; set; } }
    public class MonoBehaviour : Behaviour { }
    public class ScriptableObject : Object { public static T CreateInstance<T>() where T : ScriptableObject => null; }

    public sealed class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { }
        public Transform transform => null;
        public string tag { get; set; }
        public T AddComponent<T>() where T : Component => null;
        public T GetComponent<T>() => default;
        public T[] GetComponentsInChildren<T>() => new T[0];
        public void SetActive(bool value) { }
        public static GameObject CreatePrimitive(PrimitiveType type) => null;
        public static GameObject Find(string name) => null;
    }

    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Quaternion rotation { get; set; }
        public Vector3 localPosition { get; set; }
        public Vector3 localScale { get; set; }
        public Transform parent { get; set; }
        public void SetParent(Transform p, bool worldPositionStays) { }
        public void SetPositionAndRotation(Vector3 p, Quaternion r) { }
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => default;
        public static Vector2 operator +(Vector2 a, Vector2 b) => a;
        public static Vector2 operator -(Vector2 a, Vector2 b) => a;
        public static Vector2 operator *(Vector2 a, float d) => a;
        public static bool operator ==(Vector2 a, Vector2 b) => true;
        public static bool operator !=(Vector2 a, Vector2 b) => false;
        public static implicit operator Vector2(Vector3 v) => default;
        public static implicit operator Vector3(Vector2 v) => default;
        public override bool Equals(object o) => false;
        public override int GetHashCode() => 0;
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => default;
        public static Vector3 one => default;
        public static Vector3 up => default;
        public static Vector3 right => default;
        public static Vector3 forward => default;
        public float magnitude => 0f;
        public float sqrMagnitude => 0f;
        public Vector3 normalized => this;
        public static Vector3 Cross(Vector3 a, Vector3 b) => a;
        public static float Dot(Vector3 a, Vector3 b) => 0f;
        public static Vector3 ProjectOnPlane(Vector3 v, Vector3 n) => v;
        public static Vector3 operator +(Vector3 a, Vector3 b) => a;
        public static Vector3 operator -(Vector3 a, Vector3 b) => a;
        public static Vector3 operator -(Vector3 a) => a;
        public static Vector3 operator *(Vector3 a, float d) => a;
        public static Vector3 operator *(float d, Vector3 a) => a;
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static implicit operator Vector4(Vector3 v) => default;
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity => default;
        public static Quaternion LookRotation(Vector3 forward) => default;
        public static Quaternion LookRotation(Vector3 forward, Vector3 upwards) => default;
        public static Quaternion Euler(Vector3 euler) => default;
        public static Quaternion operator *(Quaternion a, Quaternion b) => a;
        public static Vector3 operator *(Quaternion q, Vector3 v) => v;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color black => default;
        public static Color white => default;
        public Color linear => this;
        public static Color operator +(Color x, Color y) => x;
        public static Color operator *(Color x, float d) => x;
        public static implicit operator Vector4(Color c) => default;
    }

    public struct Rect
    {
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public float x { get; set; }
        public float y { get; set; }
        public float width { get; set; }
        public float height { get; set; }
        public float yMax => y + height;
    }

    public struct Bounds { public Bounds(Vector3 center, Vector3 size) { } }

    public struct Matrix4x4 { public static Matrix4x4 TRS(Vector3 p, Quaternion q, Vector3 s) => default; }

    public static class Mathf
    {
        public const float PI = 3.14159265f;
        public static float Clamp(float v, float a, float b) => v;
        public static int Clamp(int v, int a, int b) => v;
        public static float Clamp01(float v) => v;
        public static float Max(float a, float b) => a;
        public static int Max(int a, int b) => a;
        public static float Min(float a, float b) => a;
        public static float Abs(float a) => a;
        public static float Exp(float a) => a;
        public static float Lerp(float a, float b, float t) => a;
        public static float LerpAngle(float a, float b, float t) => a;
        public static float Cos(float a) => a;
        public static float Sin(float a) => a;
        public static float Pow(float a, float b) => a;
        public static float Log(float a) => a;
    }

    public sealed class Mesh : Object
    {
        public Vector3[] vertices { get; set; }
        public Vector3[] normals { get; set; }
        public Color[] colors { get; set; }
        public Vector2[] uv { get; set; }
        public Vector2[] uv2 { get; set; }
        public int[] triangles { get; set; }
        public int vertexCount => 0;
        public Bounds bounds { get; set; }
        public Rendering.IndexFormat indexFormat { get; set; }
        public void Clear() { }
        public void MarkDynamic() { }
        public void RecalculateBounds() { }
    }

    public class Renderer : Component
    {
        public bool enabled { get; set; }
        public Material sharedMaterial { get; set; }
        public Rendering.ShadowCastingMode shadowCastingMode { get; set; }
        public bool receiveShadows { get; set; }
    }

    public sealed class MeshRenderer : Renderer { }
    public sealed class MeshFilter : Component { public Mesh sharedMesh { get; set; } }
    public class Collider : Component { }

    public class Material : Object
    {
        public Material(Shader s) { }
        public void SetVector(int id, Vector4 v) { }
        public void SetVector(string n, Vector4 v) { }
        public void SetFloat(int id, float v) { }
        public void SetFloat(string n, float v) { }
        public void SetColor(string n, Color c) { }
        public bool HasProperty(string n) => false;
    }

    public sealed class Shader : Object
    {
        public static Shader Find(string n) => null;
        public static int PropertyToID(string n) => 0;
        public static void SetGlobalVector(int id, Vector4 v) { }
        public static void SetGlobalFloat(int id, float v) { }
    }

    public enum LightType { Spot, Directional, Point }
    public enum LightShadows { None, Hard, Soft }

    public sealed class Light : Behaviour
    {
        public LightType type { get; set; }
        public LightShadows shadows { get; set; }
        public float shadowStrength { get; set; }
        public float shadowBias { get; set; }
        public float shadowNormalBias { get; set; }
        public float intensity { get; set; }
        public Color color { get; set; }
    }

    public sealed class Material2 { }

    public static class RenderSettings
    {
        public static Light sun { get; set; }
        public static Material skybox { get; set; }
        public static Rendering.AmbientMode ambientMode { get; set; }
        public static Color ambientLight { get; set; }
    }

    public enum CameraClearFlags { Skybox = 1, SolidColor = 2, Depth = 3, Nothing = 4 }
    [Flags] public enum DepthTextureMode { None = 0, Depth = 1, DepthNormals = 2, MotionVectors = 4 }

    public sealed class Camera : Behaviour
    {
        public static Camera main => null;
        public CameraClearFlags clearFlags { get; set; }
        public Color backgroundColor { get; set; }
        public bool allowHDR { get; set; }
        public DepthTextureMode depthTextureMode { get; set; }
        public float nearClipPlane { get; set; }
        public float farClipPlane { get; set; }
    }

    public static class Debug
    {
        public static void Log(object m) { }
        public static void Log(object m, Object ctx) { }
        public static void LogWarning(object m) { }
        public static void LogWarning(object m, Object ctx) { }
        public static void LogError(object m) { }
        public static void LogError(object m, Object ctx) { }
    }

    public static class Time
    {
        public static float unscaledDeltaTime => 0f;
        public static float unscaledTime => 0f;
        public static float realtimeSinceStartup => 0f;
        public static int frameCount => 0;
    }

    public enum RuntimePlatform { WindowsPlayer, WebGLPlayer }

    public static class Application
    {
        public static bool isPlaying => false;
        public static RuntimePlatform platform => RuntimePlatform.WindowsPlayer;
        public static string persistentDataPath => "";
    }

    public static class JsonUtility
    {
        public static string ToJson(object o) => "";
        public static string ToJson(object o, bool pretty) => "";
        public static T FromJson<T>(string json) => default;
    }

    public static class Screen
    {
        public static int width => 0;
        public static int height => 0;
    }

    public enum TextureFormat { RGBA32 = 4 }
    [Flags] public enum HideFlags { None = 0, HideAndDontSave = 61 }
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum PrimitiveType { Sphere, Capsule, Cylinder, Cube, Plane, Quad }

    public class Texture : Object { }

    public sealed class Texture2D : Texture
    {
        public Texture2D(int w, int h, TextureFormat f, bool mip) { }
        public void SetPixel(int x, int y, Color c) { }
        public void Apply() { }
    }

    public sealed class Font : Object { public static Font CreateDynamicFontFromOSFont(string[] names, int size) => null; }

    public sealed class RectOffset { public RectOffset(int l, int r, int t, int b) { } }

    public sealed class GUIStyleState
    {
        public Texture2D background { get; set; }
        public Color textColor { get; set; }
    }

    public sealed class GUIStyle
    {
        public GUIStyle() { }
        public GUIStyle(GUIStyle other) { }
        public RectOffset padding { get; set; }
        public TextAnchor alignment { get; set; }
        public GUIStyleState normal { get; set; }
        public int fontSize { get; set; }
        public FontStyle fontStyle { get; set; }
        public Font font { get; set; }
        public bool wordWrap { get; set; }
        public bool richText { get; set; }
    }

    public sealed class GUISkin
    {
        public GUIStyle box { get; set; }
        public GUIStyle label { get; set; }
        public GUIStyle button { get; set; }
        public GUIStyle textField { get; set; }
    }

    public sealed class GUIContent { public static GUIContent none => null; }
    public sealed class GUILayoutOption { }

    public static class GUI
    {
        public delegate void WindowFunction(int id);
        public static Matrix4x4 matrix { get; set; }
        public static GUISkin skin => null;
        public static Color color { get; set; }
        public static void Label(Rect r, string text, GUIStyle s) { }
        public static void Box(Rect r, GUIContent c, GUIStyle s) { }
        public static void DrawTexture(Rect r, Texture t) { }
        public static void DragWindow() { }
    }

    public static class GUILayout
    {
        public static void BeginArea(Rect r) { }
        public static void EndArea() { }
        public static void BeginVertical(GUIStyle s, params GUILayoutOption[] o) { }
        public static void EndVertical() { }
        public static void BeginHorizontal(params GUILayoutOption[] o) { }
        public static void EndHorizontal() { }
        public static void Label(string t, GUIStyle s, params GUILayoutOption[] o) { }
        public static void Space(float px) { }
        public static void FlexibleSpace() { }
        public static bool Button(string t, GUIStyle s, params GUILayoutOption[] o) => false;
        public static bool Toggle(bool v, string t, params GUILayoutOption[] o) => v;
        public static bool Toggle(bool v, string t, GUIStyle s, params GUILayoutOption[] o) => v;
        public static string TextField(string t, GUIStyle s, params GUILayoutOption[] o) => t;
        public static Rect Window(int id, Rect r, GUI.WindowFunction f, string title, params GUILayoutOption[] o) => r;
        public static GUILayoutOption Width(float w) => null;
        public static Vector2 BeginScrollView(Vector2 p, params GUILayoutOption[] o) => p;
        public static void EndScrollView() { }
    }

    public static class GUIUtility { public static int keyboardControl { get; set; } }

    public enum KeyCode { None, M, I, U, O, R, H, P, C, F, G, Comma, Period, Tab, Escape, Equals, Minus, F1 }

    public static class Input
    {
        public static bool GetKeyDown(KeyCode k) => false;
        public static bool GetKey(KeyCode k) => false;
        public static bool GetMouseButton(int b) => false;
        public static Vector3 mousePosition => default;
        public static Vector2 mouseScrollDelta => default;
    }

    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string t) { } }
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)] public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string h) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class RangeAttribute : Attribute { public RangeAttribute(float a, float b) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class MinAttribute : Attribute { public MinAttribute(float m) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int o) { } }
    [AttributeUsage(AttributeTargets.Method)] public sealed class ContextMenu : Attribute { public ContextMenu(string n) { } }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CreateAssetMenuAttribute : Attribute
    {
        public string menuName { get; set; }
        public string fileName { get; set; }
    }
}

namespace UnityEngine.Rendering
{
    public enum ShadowCastingMode { Off, On, TwoSided, ShadowsOnly }
    public enum IndexFormat { UInt16, UInt32 }
    public enum AmbientMode { Skybox, Trilight, Flat = 3, Custom = 4 }
    public class RenderPipelineAsset : ScriptableObject { }
    public static class GraphicsSettings { public static RenderPipelineAsset currentRenderPipeline => null; }
}

namespace UnityEngine.TestTools
{
    [AttributeUsage(AttributeTargets.Method)] public sealed class UnityTestAttribute : Attribute { }
}
