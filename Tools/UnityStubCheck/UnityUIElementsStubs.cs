// Compile-time stand-in for the UI Toolkit (UnityEngine.UIElements), TextCore, SceneManagement and PlayerPrefs APIs used by
// SpaceSim.Runtime (main menu, in-game Esc menu, settings). Signatures follow Unity 6; no implementations.
// This does NOT replace compiling in Unity — it only type-checks our code outside the Editor.
#pragma warning disable CS0067, CS0649, CS0414, CS1591
using System;

namespace UnityEngine
{
    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color(Color32 c) => default;
        public static implicit operator Color32(Color c) => default;
    }

    public struct Vector2Int
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
    }

    public enum TextureWrapMode { Repeat, Clamp, Mirror, MirrorOnce }
    public enum FilterMode { Point, Bilinear, Trilinear }

    public static class PlayerPrefs
    {
        public static bool HasKey(string key) => false;
        public static string GetString(string key, string defaultValue) => defaultValue;
        public static void SetString(string key, string value) { }
        public static void Save() { }
    }

    public static class Resources
    {
        public static T Load<T>(string path) where T : Object => null;
        public static T GetBuiltinResource<T>(string path) where T : Object => null;
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RequireComponent : Attribute { public RequireComponent(Type t) { } }

    public sealed class GUISettings
    {
        public Color cursorColor { get; set; }
        public Color selectionColor { get; set; }
    }
}

namespace UnityEngine.SceneManagement
{
    public static class SceneManager
    {
        public static void LoadScene(string sceneName) { }
    }
}

namespace UnityEngine.TextCore.Text
{
    public class FontAsset : ScriptableObject
    {
        public static FontAsset CreateFontAsset(string familyName, string styleName) => null;
    }
}

namespace UnityEngine.UIElements
{
    using UnityEngine.TextCore.Text;

    public delegate void EventCallback<in TEventType>(TEventType evt);
    public enum TrickleDown { NoTrickleDown, TrickleDown }
    public enum PickingMode { Position, Ignore }
    public enum Position { Relative, Absolute }
    public enum DisplayStyle { Flex, None }
    public enum Align { Auto, FlexStart, Center, FlexEnd, Stretch }
    public enum PanelScaleMode { ConstantPixelSize, ConstantPhysicalSize, ScaleWithScreenSize }
    public enum PanelScreenMatchMode { MatchWidthOrHeight, ShrinkToFit, ExpandToFill }

    public interface IEventHandler { }

    public abstract class EventBase
    {
        public IEventHandler target { get; set; }
        public void StopPropagation() { }
        public void StopImmediatePropagation() { }
    }

    public abstract class EventBase<T> : EventBase where T : EventBase<T>, new() { }

    public sealed class NavigationCancelEvent : EventBase<NavigationCancelEvent> { }
    public sealed class NavigationSubmitEvent : EventBase<NavigationSubmitEvent> { }
    public sealed class NavigationMoveEvent : EventBase<NavigationMoveEvent>
    {
        public enum Direction { None, Left, Up, Right, Down, Next, Previous }
        public Direction direction { get; set; }
    }
    public sealed class FocusInEvent : EventBase<FocusInEvent> { }
    public sealed class FocusOutEvent : EventBase<FocusOutEvent> { }
    public sealed class ClickEvent : EventBase<ClickEvent> { }
    public sealed class KeyDownEvent : EventBase<KeyDownEvent> { public KeyCode keyCode { get; set; } }
    public sealed class AttachToPanelEvent : EventBase<AttachToPanelEvent> { }
    public sealed class GeometryChangedEvent : EventBase<GeometryChangedEvent> { }
    public sealed class WheelEvent : EventBase<WheelEvent> { public Vector3 delta { get; set; } }
    public sealed class PointerEnterEvent : EventBase<PointerEnterEvent> { }
    public sealed class PointerCaptureOutEvent : EventBase<PointerCaptureOutEvent> { }
    public sealed class PointerDownEvent : EventBase<PointerDownEvent>
    {
        public int pointerId { get; set; }
        public int button { get; set; }
        public Vector3 position { get; set; }
    }
    public sealed class PointerMoveEvent : EventBase<PointerMoveEvent>
    {
        public int pointerId { get; set; }
        public Vector3 position { get; set; }
    }
    public sealed class PointerUpEvent : EventBase<PointerUpEvent>
    {
        public int pointerId { get; set; }
        public Vector3 position { get; set; }
    }

    public abstract class CallbackEventHandler : IEventHandler
    {
        public void RegisterCallback<TEventType>(EventCallback<TEventType> callback, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown)
            where TEventType : EventBase<TEventType>, new() { }
    }

    public class FocusController
    {
        public Focusable focusedElement => null;
        public void IgnoreEvent(EventBase evt) { }
    }

    public abstract class Focusable : CallbackEventHandler
    {
        public bool focusable { get; set; }
        public FocusController focusController => null;
        public void Focus() { }
        public void Blur() { }
    }

    public static class PointerCaptureHelper
    {
        public static void CapturePointer(this IEventHandler handler, int pointerId) { }
        public static void ReleasePointer(this IEventHandler handler, int pointerId) { }
        public static bool HasPointerCapture(this IEventHandler handler, int pointerId) => false;
    }

    public struct Length
    {
        public static Length Percent(float value) => default;
        public static implicit operator Length(float value) => default;
    }

    public struct Translate { public Translate(Length x, Length y) { } }

    public struct StyleLength
    {
        public static implicit operator StyleLength(float v) => default;
        public static implicit operator StyleLength(Length v) => default;
    }
    public struct StyleFloat { public static implicit operator StyleFloat(float v) => default; }
    public struct StyleColor { public static implicit operator StyleColor(Color v) => default; }
    public struct StyleTranslate { public static implicit operator StyleTranslate(Translate v) => default; }
    public struct StyleEnum<T> where T : struct, IConvertible { public static implicit operator StyleEnum<T>(T v) => default; }
    public struct StyleBackground { public StyleBackground(Texture2D t) { } }
    public struct StyleFontDefinition { public static implicit operator StyleFontDefinition(FontDefinition v) => default; }

    public struct FontDefinition
    {
        public FontAsset fontAsset => null;
        public static FontDefinition FromFont(Font f) => default;
        public static FontDefinition FromSDFFont(FontAsset f) => default;
    }

    public interface IStyle
    {
        StyleEnum<Position> position { get; set; }
        StyleLength left { get; set; }
        StyleLength top { get; set; }
        StyleLength right { get; set; }
        StyleLength bottom { get; set; }
        StyleLength width { get; set; }
        StyleLength height { get; set; }
        StyleLength marginTop { get; set; }
        StyleLength marginLeft { get; set; }
        StyleLength marginRight { get; set; }
        StyleLength marginBottom { get; set; }
        StyleLength fontSize { get; set; }
        StyleColor color { get; set; }
        StyleColor backgroundColor { get; set; }
        StyleColor borderTopColor { get; set; }
        StyleColor borderBottomColor { get; set; }
        StyleColor borderLeftColor { get; set; }
        StyleColor borderRightColor { get; set; }
        StyleBackground backgroundImage { get; set; }
        StyleFloat opacity { get; set; }
        StyleTranslate translate { get; set; }
        StyleEnum<DisplayStyle> display { get; set; }
        StyleEnum<Align> alignItems { get; set; }
        StyleFontDefinition unityFontDefinition { get; set; }
        StyleEnum<FontStyle> unityFontStyleAndWeight { get; set; }
    }

    public class StyleSheet : ScriptableObject { }
    public sealed class VisualElementStyleSheetSet { public void Add(StyleSheet s) { } }

    public struct TimerState { }
    public interface IVisualElementScheduledItem
    {
        IVisualElementScheduledItem StartingIn(long delayMs);
        IVisualElementScheduledItem Every(long intervalMs);
    }
    public interface IVisualElementScheduler
    {
        IVisualElementScheduledItem Execute(Action timerUpdateEvent);
        IVisualElementScheduledItem Execute(Action<TimerState> timerUpdateEvent);
    }

    public interface IPanel { VisualElement visualTree { get; } }

    public struct Vertex
    {
        public static float nearZ => 0f;
        public Vector3 position;
        public Color32 tint;
    }
    public sealed class MeshWriteData
    {
        public void SetNextVertex(Vertex v) { }
        public void SetNextIndex(ushort i) { }
    }
    public sealed class MeshGenerationContext { public MeshWriteData Allocate(int vertexCount, int indexCount) => null; }

    public struct UQueryBuilder<T> where T : VisualElement { public void ForEach(Action<T> action) { } }

    public class VisualElement : Focusable
    {
        public string name { get; set; }
        public string tooltip { get; set; }
        public VisualElement parent => null;
        public IPanel panel => null;
        public Rect layout => default;
        public Rect contentRect => default;
        public PickingMode pickingMode { get; set; }
        public IStyle style => null;
        public VisualElementStyleSheetSet styleSheets => null;
        public IVisualElementScheduler schedule => null;
        public Action<MeshGenerationContext> generateVisualContent { get; set; }
        public void Add(VisualElement child) { }
        public void Clear() { }
        public void AddToClassList(string c) { }
        public void RemoveFromClassList(string c) { }
        public void EnableInClassList(string c, bool enable) { }
        public void ToggleInClassList(string c) { }
        public bool ClassListContains(string c) => false;
        public void MarkDirtyRepaint() { }
        public Vector2 WorldToLocal(Vector2 p) => p;
    }

    public static class UQueryExtensions
    {
        public static T Q<T>(this VisualElement e, string name = null, string className = null) where T : VisualElement => null;
        public static UQueryBuilder<VisualElement> Query(this VisualElement e, string name = null, string className = null) => default;
    }

    public class TextElement : VisualElement { public string text { get; set; } }
    public class Label : TextElement
    {
        public Label() { }
        public Label(string text) { }
    }
    public class Button : TextElement
    {
        public Button() { }
        public Button(Action clickEvent) { }
        public event Action clicked;
    }

    public class PanelSettings : ScriptableObject
    {
        public PanelScaleMode scaleMode { get; set; }
        public Vector2Int referenceResolution { get; set; }
        public PanelScreenMatchMode screenMatchMode { get; set; }
        public float match { get; set; }
        public bool clearColor { get; set; }
        public Color colorClearValue { get; set; }
        public float sortingOrder { get; set; }
    }

    public sealed class UIDocument : MonoBehaviour
    {
        public PanelSettings panelSettings { get; set; }
        public VisualElement rootVisualElement => null;
    }
}
