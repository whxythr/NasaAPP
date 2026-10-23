using UnityEngine;
#if ENABLE_INPUT_SYSTEM && SPACESIM_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SpaceSim.Runtime
{
    public enum InputKey
    {
        M, I, U, O, R, H, P, C, F, G, Comma, Period, Tab, Escape, Equals, Minus, F1
    }

    /// <summary>
    /// Thin input facade. Uses the Input System package when it is installed and enabled
    /// (Project Settings → Player → Active Input Handling), otherwise the legacy Input Manager,
    /// so the prototype runs with either setting.
    /// </summary>
    public static class FlightInput
    {
        static int lastFrame = -1;
        static Vector2 lastMouse;
        static Vector2 mouseDelta;

        /// <summary>True while an IMGUI text field has keyboard focus (hotkeys are suppressed).</summary>
        public static bool IsTyping => GUIUtility.keyboardControl != 0;

        public static bool GetKeyDown(InputKey key)
        {
#if ENABLE_INPUT_SYSTEM && SPACESIM_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb[ToKey(key)].wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(ToKeyCode(key));
#else
            return false;
#endif
        }

        public static bool GetKey(InputKey key)
        {
#if ENABLE_INPUT_SYSTEM && SPACESIM_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb[ToKey(key)].isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(ToKeyCode(key));
#else
            return false;
#endif
        }

        public static bool RightMouseHeld
        {
            get
            {
#if ENABLE_INPUT_SYSTEM && SPACESIM_INPUT_SYSTEM
                return Mouse.current != null && Mouse.current.rightButton.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
                return Input.GetMouseButton(1);
#else
                return false;
#endif
            }
        }

        public static bool MiddleMouseHeld
        {
            get
            {
#if ENABLE_INPUT_SYSTEM && SPACESIM_INPUT_SYSTEM
                return Mouse.current != null && Mouse.current.middleButton.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
                return Input.GetMouseButton(2);
#else
                return false;
#endif
            }
        }

        /// <summary>Mouse movement this frame in pixels (same scale for both input backends).</summary>
        public static Vector2 MouseDelta
        {
            get
            {
                if (lastFrame == Time.frameCount) return mouseDelta;
                lastFrame = Time.frameCount;
#if ENABLE_INPUT_SYSTEM && SPACESIM_INPUT_SYSTEM
                mouseDelta = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
#elif ENABLE_LEGACY_INPUT_MANAGER
                Vector2 p = Input.mousePosition;
                mouseDelta = lastMouse == Vector2.zero ? Vector2.zero : p - lastMouse;
                lastMouse = p;
#else
                mouseDelta = Vector2.zero;
#endif
                return mouseDelta;
            }
        }

        /// <summary>Scroll in wheel notches (+ = away from the user).</summary>
        public static float ScrollNotches
        {
            get
            {
#if ENABLE_INPUT_SYSTEM && SPACESIM_INPUT_SYSTEM
                if (Mouse.current == null) return 0f;
                float y = Mouse.current.scroll.ReadValue().y;
                // Depending on platform/version the Input System reports 120 per notch or ±1 per notch.
                if (Mathf.Abs(y) >= 10f) y /= 120f;
                return Mathf.Clamp(y, -10f, 10f);
#elif ENABLE_LEGACY_INPUT_MANAGER
                return Input.mouseScrollDelta.y;
#else
                return 0f;
#endif
            }
        }

#if ENABLE_INPUT_SYSTEM && SPACESIM_INPUT_SYSTEM
        static Key ToKey(InputKey k)
        {
            switch (k)
            {
                case InputKey.M: return Key.M;
                case InputKey.I: return Key.I;
                case InputKey.U: return Key.U;
                case InputKey.O: return Key.O;
                case InputKey.R: return Key.R;
                case InputKey.H: return Key.H;
                case InputKey.P: return Key.P;
                case InputKey.C: return Key.C;
                case InputKey.F: return Key.F;
                case InputKey.G: return Key.G;
                case InputKey.Comma: return Key.Comma;
                case InputKey.Period: return Key.Period;
                case InputKey.Tab: return Key.Tab;
                case InputKey.Escape: return Key.Escape;
                case InputKey.Equals: return Key.Equals;
                case InputKey.Minus: return Key.Minus;
                case InputKey.F1: return Key.F1;
                default: return Key.None;
            }
        }
#endif

        static KeyCode ToKeyCode(InputKey k)
        {
            switch (k)
            {
                case InputKey.M: return KeyCode.M;
                case InputKey.I: return KeyCode.I;
                case InputKey.U: return KeyCode.U;
                case InputKey.O: return KeyCode.O;
                case InputKey.R: return KeyCode.R;
                case InputKey.H: return KeyCode.H;
                case InputKey.P: return KeyCode.P;
                case InputKey.C: return KeyCode.C;
                case InputKey.F: return KeyCode.F;
                case InputKey.G: return KeyCode.G;
                case InputKey.Comma: return KeyCode.Comma;
                case InputKey.Period: return KeyCode.Period;
                case InputKey.Tab: return KeyCode.Tab;
                case InputKey.Escape: return KeyCode.Escape;
                case InputKey.Equals: return KeyCode.Equals;
                case InputKey.Minus: return KeyCode.Minus;
                case InputKey.F1: return KeyCode.F1;
                default: return KeyCode.None;
            }
        }
    }
}
