using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>Where <see cref="GameSettings"/> keeps its values (PlayerPrefs in the game, memory in tests).</summary>
    public interface ISettingsStore
    {
        bool TryGet(string key, out string value);
        void Set(string key, string value);
        void Save();
    }

    /// <summary>Player preferences, shared by every scene (main menu and flight).</summary>
    public sealed class PlayerPrefsSettingsStore : ISettingsStore
    {
        public bool TryGet(string key, out string value)
        {
            value = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key, "") : null;
            return value != null;
        }

        public void Set(string key, string value) => PlayerPrefs.SetString(key, value);
        public void Save() => PlayerPrefs.Save();
    }

    /// <summary>In-memory store for tests: nothing touches the player's real preferences.</summary>
    public sealed class MemorySettingsStore : ISettingsStore
    {
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
        public int Saves { get; private set; }
        public bool TryGet(string key, out string value) => Values.TryGetValue(key, out value);
        public void Set(string key, string value) => Values[key] = value;
        public void Save() => Saves++;
    }

    /// <summary>
    /// Player settings that apply everywhere: screen brightness, mouse look (sensitivity, inverted Y) and key bindings.
    /// Changes take effect at once (<see cref="Changed"/>) and are saved immediately. Purely presentation/input — nothing
    /// here touches the physics.
    /// </summary>
    public static class GameSettings
    {
        public const float MinBrightness = 0.5f, MaxBrightness = 2f, DefaultBrightness = 1f;
        public const float MinMouseSensitivity = 0.2f, MaxMouseSensitivity = 3f, DefaultMouseSensitivity = 1f;

        public const string BrightnessKey = "SpaceSim.Settings.Brightness";
        public const string MouseSensitivityKey = "SpaceSim.Settings.MouseSensitivity";
        public const string InvertMouseYKey = "SpaceSim.Settings.InvertMouseY";
        public const string BindingsKey = "SpaceSim.Settings.KeyBindings";

        static ISettingsStore store;
        static bool loaded;
        static float brightness = DefaultBrightness, mouseSensitivity = DefaultMouseSensitivity;
        static bool invertMouseY;
        static readonly Dictionary<InputKey, KeyCode> bindings = new Dictionary<InputKey, KeyCode>();

        /// <summary>Raised after any setting changed (brightness, mouse, bindings, resets).</summary>
        public static event Action Changed;

        /// <summary>Backing store. Assigning one (tests) reloads every value from it.</summary>
        public static ISettingsStore Store
        {
            get => store ?? (store = new PlayerPrefsSettingsStore());
            set
            {
                store = value;
                loaded = false;
            }
        }

        /// <summary>Screen brightness as a linear factor (1 = unchanged, 2 = +1 EV, 0.5 = −1 EV).</summary>
        public static float Brightness
        {
            get
            {
                EnsureLoaded();
                return brightness;
            }
            set
            {
                EnsureLoaded();
                float v = Mathf.Clamp(value, MinBrightness, MaxBrightness);
                if (Mathf.Abs(v - brightness) < 1e-4f) return;
                brightness = v;
                Store.Set(BrightnessKey, Format(v));
                Commit();
            }
        }

        /// <summary>Brightness as a photographic exposure offset in EV (stops): log2(<see cref="Brightness"/>).</summary>
        public static float ExposureEv => (float)Math.Log(Brightness, 2.0);

        /// <summary>Multiplier on mouse look in flight (right/middle button) and on foot.</summary>
        public static float MouseSensitivity
        {
            get
            {
                EnsureLoaded();
                return mouseSensitivity;
            }
            set
            {
                EnsureLoaded();
                float v = Mathf.Clamp(value, MinMouseSensitivity, MaxMouseSensitivity);
                if (Mathf.Abs(v - mouseSensitivity) < 1e-4f) return;
                mouseSensitivity = v;
                Store.Set(MouseSensitivityKey, Format(v));
                Commit();
            }
        }

        public static bool InvertMouseY
        {
            get
            {
                EnsureLoaded();
                return invertMouseY;
            }
            set
            {
                EnsureLoaded();
                if (value == invertMouseY) return;
                invertMouseY = value;
                Store.Set(InvertMouseYKey, value ? "1" : "0");
                Commit();
            }
        }

        /// <summary>The physical key that currently triggers a command.</summary>
        public static KeyCode GetBinding(InputKey key)
        {
            EnsureLoaded();
            return bindings.TryGetValue(key, out KeyCode code) ? code : KeyBindings.Default(key);
        }

        public static bool IsDefaultBinding(InputKey key) => GetBinding(key) == KeyBindings.Default(key);

        /// <summary>
        /// Assigns <paramref name="code"/> to <paramref name="key"/>. If another command already used that key the two swap
        /// keys, so no key ever does two things; that command is returned (null when nothing was swapped or nothing changed).
        /// Esc and keys outside <see cref="KeyBindings.Assignable"/> are refused (<see cref="ArgumentException"/>).
        /// </summary>
        public static InputKey? SetBinding(InputKey key, KeyCode code)
        {
            if (!KeyBindings.IsRebindable(key)) throw new ArgumentException("Command " + key + " cannot be rebound.", nameof(key));
            if (!KeyBindings.IsAssignable(code)) throw new ArgumentException("Key " + code + " cannot be assigned.", nameof(code));
            EnsureLoaded();
            KeyCode previous = GetBinding(key);
            if (previous == code) return null;

            InputKey? swapped = null;
            foreach (KeyAction other in KeyBindings.Actions)
            {
                if (other.Key == key || GetBinding(other.Key) != code) continue;
                bindings[other.Key] = previous;
                swapped = other.Key;
            }
            bindings[key] = code;
            SaveBindings();
            Commit();
            return swapped;
        }

        /// <summary>Command that currently uses <paramref name="code"/> (null if none).</summary>
        public static InputKey? CommandOn(KeyCode code)
        {
            foreach (KeyAction a in KeyBindings.Actions)
                if (GetBinding(a.Key) == code) return a.Key;
            return null;
        }

        public static void ResetBindings()
        {
            EnsureLoaded();
            bindings.Clear();
            SaveBindings();
            Commit();
        }

        public static void ResetDisplay()
        {
            EnsureLoaded();
            brightness = DefaultBrightness;
            Store.Set(BrightnessKey, Format(brightness));
            Commit();
        }

        public static void ResetMouse()
        {
            EnsureLoaded();
            mouseSensitivity = DefaultMouseSensitivity;
            invertMouseY = false;
            Store.Set(MouseSensitivityKey, Format(mouseSensitivity));
            Store.Set(InvertMouseYKey, "0");
            Commit();
        }

        /// <summary>Reads everything from <see cref="Store"/> again (unknown or broken values fall back to defaults).</summary>
        public static void Reload()
        {
            loaded = false;
            EnsureLoaded();
            Changed?.Invoke();
        }

        static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            ISettingsStore s = Store;
            brightness = Mathf.Clamp(ReadFloat(s, BrightnessKey, DefaultBrightness), MinBrightness, MaxBrightness);
            mouseSensitivity = Mathf.Clamp(ReadFloat(s, MouseSensitivityKey, DefaultMouseSensitivity), MinMouseSensitivity, MaxMouseSensitivity);
            invertMouseY = s.TryGet(InvertMouseYKey, out string inv) && inv == "1";
            bindings.Clear();
            if (s.TryGet(BindingsKey, out string text)) ParseBindings(text, bindings);
        }

        /// <summary>"W=UpArrow;Space=J": only non-default keys; malformed, duplicate or forbidden entries are ignored.</summary>
        public static string FormatBindings(IDictionary<InputKey, KeyCode> map)
        {
            var sb = new StringBuilder();
            foreach (KeyAction a in KeyBindings.Actions)
            {
                if (!map.TryGetValue(a.Key, out KeyCode code) || code == KeyBindings.Default(a.Key)) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(a.Key).Append('=').Append(code);
            }
            return sb.ToString();
        }

        public static void ParseBindings(string text, IDictionary<InputKey, KeyCode> into)
        {
            if (string.IsNullOrEmpty(text)) return;
            var used = new HashSet<KeyCode>();
            var parsed = new Dictionary<InputKey, KeyCode>();
            foreach (string pair in text.Split(';'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                if (!Enum.TryParse(pair.Substring(0, eq).Trim(), out InputKey key) || !KeyBindings.IsRebindable(key)) continue;
                if (!Enum.TryParse(pair.Substring(eq + 1).Trim(), out KeyCode code) || !KeyBindings.IsAssignable(code)) continue;
                parsed[key] = code;
            }
            // A key may serve only one command: drop any entry that would collide with another command's effective key.
            foreach (KeyAction a in KeyBindings.Actions)
            {
                KeyCode effective = parsed.TryGetValue(a.Key, out KeyCode c) ? c : KeyBindings.Default(a.Key);
                if (!used.Add(effective))
                {
                    into.Clear();
                    return; // inconsistent file: fall back to the defaults rather than a half-broken layout
                }
            }
            foreach (KeyValuePair<InputKey, KeyCode> p in parsed) into[p.Key] = p.Value;
        }

        static void SaveBindings() => Store.Set(BindingsKey, FormatBindings(bindings));

        static void Commit()
        {
            Store.Save();
            Changed?.Invoke();
        }

        static float ReadFloat(ISettingsStore s, string key, float fallback) =>
            s.TryGet(key, out string text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) &&
            !float.IsNaN(v) && !float.IsInfinity(v)
                ? v
                : fallback;

        static string Format(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
