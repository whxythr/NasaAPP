using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && SPACESIM_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Logical game commands, named after their default key. The physical key that triggers each one is a player setting
    /// (<see cref="GameSettings.GetBinding"/>); Escape is reserved for closing windows and the pause menu.
    /// </summary>
    public enum InputKey
    {
        M, I, U, O, R, H, P, C, F, G, J, N, Comma, Period, Tab, Escape, Equals, Minus, F1,
        W, A, S, D, Z, X, Space, LeftShift, LeftCtrl, Return, B, K, L, E, V
    }

    /// <summary>
    /// Thin input facade. Uses the Input System package when it is installed and enabled
    /// (Project Settings → Player → Active Input Handling), otherwise the legacy Input Manager,
    /// so the prototype runs with either setting. Commands go through the player's key bindings and mouse settings.
    /// </summary>
    public static class FlightInput
    {
        static int lastFrame = -1;
        static Vector2 lastMouse;
        static Vector2 mouseDelta;

        /// <summary>Set while a modal window (e.g. the hangar) owns the input: camera and flight hotkeys pause.</summary>
        public static bool ModalWindowOpen { get; set; }

        /// <summary>Set while the Esc menu (settings) is open: the HUD hides and every flight hotkey pauses.</summary>
        public static bool PauseMenuOpen { get; set; }

        /// <summary>True while an IMGUI text field has keyboard focus or a modal window is open (hotkeys are suppressed).</summary>
        public static bool IsTyping => GUIUtility.keyboardControl != 0 || ModalWindowOpen || PauseMenuOpen;

        /// <summary>The command's key went down this frame (through the player's bindings).</summary>
        public static bool GetKeyDown(InputKey key) => KeyDown(GameSettings.GetBinding(key));

        /// <summary>The command's key is held (through the player's bindings).</summary>
        public static bool GetKey(InputKey key) => KeyHeld(GameSettings.GetBinding(key));

        /// <summary>Esc went down this frame (never rebound).</summary>
        public static bool EscapePressed => KeyDown(KeyCode.Escape);

        /// <summary>Name of the key bound to a command, for hints and buttons ("B", "Пробел").</summary>
        public static string KeyLabel(InputKey key) => KeyBindings.DisplayName(GameSettings.GetBinding(key));

        /// <summary>A physical key went down this frame.</summary>
        public static bool KeyDown(KeyCode code)
        {
            if (code == KeyCode.None) return false;
#if ENABLE_INPUT_SYSTEM && SPACESIM_INPUT_SYSTEM
            var kb = Keyboard.current;
            Key k = ToKey(code);
            return kb != null && k != Key.None && kb[k].wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(code);
#else
            return false;
#endif
        }

        /// <summary>A physical key is held.</summary>
        public static bool KeyHeld(KeyCode code)
        {
            if (code == KeyCode.None) return false;
#if ENABLE_INPUT_SYSTEM && SPACESIM_INPUT_SYSTEM
            var kb = Keyboard.current;
            Key k = ToKey(code);
            return kb != null && k != Key.None && kb[k].isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(code);
#else
            return false;
#endif
        }

        /// <summary>The first assignable key pressed this frame (used by "press a key" in Settings).</summary>
        public static bool TryGetPressedKey(out KeyCode code)
        {
            foreach (KeyCode c in KeyBindings.Assignable)
            {
                if (!KeyDown(c)) continue;
                code = c;
                return true;
            }
            code = KeyCode.None;
            return false;
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

        /// <summary>Mouse movement for looking around: <see cref="MouseDelta"/> × sensitivity, Y inverted if the player chose so.</summary>
        public static Vector2 LookDelta => ApplyLookSettings(MouseDelta, GameSettings.MouseSensitivity, GameSettings.InvertMouseY);

        public static Vector2 ApplyLookSettings(Vector2 delta, float sensitivity, bool invertY) =>
            new Vector2(delta.x * sensitivity, (invertY ? -delta.y : delta.y) * sensitivity);

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
        static readonly Dictionary<KeyCode, Key> keyMap = new Dictionary<KeyCode, Key>();

        /// <summary>Legacy <see cref="KeyCode"/> → Input System <see cref="Key"/> (same names except the cases below).</summary>
        static Key ToKey(KeyCode code)
        {
            if (keyMap.TryGetValue(code, out Key cached)) return cached;
            Key k;
            // By name, not by offset: in Key the digits run 1…9, 0.
            if (code >= KeyCode.Alpha0 && code <= KeyCode.Alpha9)
                k = Enum.TryParse("Digit" + (int)(code - KeyCode.Alpha0), out Key digit) ? digit : Key.None;
            else if (code >= KeyCode.Keypad0 && code <= KeyCode.Keypad9)
                k = Enum.TryParse("Numpad" + (int)(code - KeyCode.Keypad0), out Key pad) ? pad : Key.None;
            else
            {
                switch (code)
                {
                    case KeyCode.Return: k = Key.Enter; break;
                    case KeyCode.LeftControl: k = Key.LeftCtrl; break;
                    case KeyCode.RightControl: k = Key.RightCtrl; break;
                    case KeyCode.BackQuote: k = Key.Backquote; break;
                    case KeyCode.KeypadPlus: k = Key.NumpadPlus; break;
                    case KeyCode.KeypadMinus: k = Key.NumpadMinus; break;
                    case KeyCode.KeypadMultiply: k = Key.NumpadMultiply; break;
                    case KeyCode.KeypadDivide: k = Key.NumpadDivide; break;
                    case KeyCode.KeypadEnter: k = Key.NumpadEnter; break;
                    case KeyCode.KeypadPeriod: k = Key.NumpadPeriod; break;
                    default:
                        if (!Enum.TryParse(code.ToString(), out k)) k = Key.None;
                        break;
                }
            }
            keyMap[code] = k;
            return k;
        }
#endif
    }
}
