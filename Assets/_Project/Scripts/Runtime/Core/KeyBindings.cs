using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>One rebindable command: the logical key used in code, where it belongs and what it does (shown in Settings).</summary>
    public readonly struct KeyAction
    {
        public readonly InputKey Key;
        public readonly string Group, Label;

        public KeyAction(InputKey key, string group, string label)
        {
            Key = key;
            Group = group;
            Label = label;
        }
    }

    /// <summary>
    /// The catalogue of rebindable commands, their default keys and the physical keys a player may assign.
    /// <see cref="InputKey"/> stays the logical name used in code (it is named after its default key); <see cref="GameSettings"/>
    /// stores which physical <see cref="KeyCode"/> currently triggers it. Esc is reserved for menus and is never rebindable.
    /// </summary>
    public static class KeyBindings
    {
        public const string GroupRocket = "РАКЕТА", GroupCamera = "КАМЕРА И ВРЕМЯ", GroupWindows = "ОКНА",
            GroupInterface = "ИНТЕРФЕЙС", GroupSurface = "НА ПОВЕРХНОСТИ";

        /// <summary>Every rebindable command, in the order the Settings screen lists them.</summary>
        public static readonly KeyAction[] Actions =
        {
            new KeyAction(InputKey.Return, GroupRocket, "Зажигание двигателя"),
            new KeyAction(InputKey.W, GroupRocket, "Тангаж вверх · шаг вперёд"),
            new KeyAction(InputKey.S, GroupRocket, "Тангаж вниз · шаг назад"),
            new KeyAction(InputKey.A, GroupRocket, "Курс влево · шаг влево"),
            new KeyAction(InputKey.D, GroupRocket, "Курс вправо · шаг вправо"),
            new KeyAction(InputKey.LeftShift, GroupRocket, "Тяга больше · спринт"),
            new KeyAction(InputKey.LeftCtrl, GroupRocket, "Тяга меньше"),
            new KeyAction(InputKey.Z, GroupRocket, "Полная тяга"),
            new KeyAction(InputKey.X, GroupRocket, "Отсечка двигателя"),
            new KeyAction(InputKey.Space, GroupRocket, "Отделить ступень · прыжок"),

            new KeyAction(InputKey.C, GroupCamera, "Система отсчёта камеры"),
            new KeyAction(InputKey.F, GroupCamera, "Фокус камеры: ракета / планета"),
            new KeyAction(InputKey.Equals, GroupCamera, "Приблизить"),
            new KeyAction(InputKey.Minus, GroupCamera, "Отдалить"),
            new KeyAction(InputKey.Period, GroupCamera, "Ускорить время"),
            new KeyAction(InputKey.Comma, GroupCamera, "Замедлить время"),
            new KeyAction(InputKey.P, GroupCamera, "Пауза симуляции"),

            new KeyAction(InputKey.B, GroupWindows, "Ангар"),
            new KeyAction(InputKey.K, GroupWindows, "Магазин"),
            new KeyAction(InputKey.G, GroupWindows, "Навигация"),
            new KeyAction(InputKey.L, GroupWindows, "Академия"),
            new KeyAction(InputKey.M, GroupWindows, "Flight Manual"),
            new KeyAction(InputKey.F1, GroupWindows, "Flight Manual (вторая клавиша)"),
            new KeyAction(InputKey.I, GroupWindows, "Начальные условия (тест)"),

            new KeyAction(InputKey.H, GroupInterface, "Скрыть / показать приборы"),
            new KeyAction(InputKey.Tab, GroupInterface, "Карта системы"),
            new KeyAction(InputKey.N, GroupInterface, "Метки тел"),
            new KeyAction(InputKey.J, GroupInterface, "Перелёт к другому телу"),
            new KeyAction(InputKey.O, GroupInterface, "Анализ орбиты"),
            new KeyAction(InputKey.U, GroupInterface, "Единицы измерения"),
            new KeyAction(InputKey.R, GroupInterface, "Новая ракета на площадке"),

            new KeyAction(InputKey.E, GroupSurface, "Выйти · взять образец"),
            new KeyAction(InputKey.V, GroupSurface, "Вид от первого / третьего лица"),
        };

        /// <summary>Physical keys that can be assigned (and that the "press a key" prompt listens to).</summary>
        public static readonly KeyCode[] Assignable = BuildAssignable();

        static KeyCode[] BuildAssignable()
        {
            var list = new List<KeyCode>();
            for (KeyCode k = KeyCode.A; k <= KeyCode.Z; k++) list.Add(k);
            for (KeyCode k = KeyCode.Alpha0; k <= KeyCode.Alpha9; k++) list.Add(k);
            for (KeyCode k = KeyCode.F1; k <= KeyCode.F12; k++) list.Add(k);
            list.AddRange(new[]
            {
                KeyCode.Space, KeyCode.Return, KeyCode.Tab, KeyCode.Backspace,
                KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftAlt, KeyCode.RightAlt,
                KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
                KeyCode.Comma, KeyCode.Period, KeyCode.Minus, KeyCode.Equals, KeyCode.Slash, KeyCode.Backslash,
                KeyCode.Semicolon, KeyCode.Quote, KeyCode.LeftBracket, KeyCode.RightBracket, KeyCode.BackQuote,
                KeyCode.Insert, KeyCode.Delete, KeyCode.Home, KeyCode.End, KeyCode.PageUp, KeyCode.PageDown,
                KeyCode.KeypadPlus, KeyCode.KeypadMinus, KeyCode.KeypadMultiply, KeyCode.KeypadDivide, KeyCode.KeypadEnter, KeyCode.KeypadPeriod,
            });
            for (KeyCode k = KeyCode.Keypad0; k <= KeyCode.Keypad9; k++) list.Add(k);
            return list.ToArray();
        }

        static HashSet<KeyCode> assignableSet;

        public static bool IsAssignable(KeyCode key)
        {
            if (assignableSet == null) assignableSet = new HashSet<KeyCode>(Assignable);
            return assignableSet.Contains(key);
        }

        public static bool IsRebindable(InputKey key)
        {
            foreach (KeyAction a in Actions) if (a.Key == key) return true;
            return false;
        }

        public static string LabelOf(InputKey key)
        {
            foreach (KeyAction a in Actions) if (a.Key == key) return a.Label;
            return key.ToString();
        }

        /// <summary>The factory key of a command (its <see cref="InputKey"/> name).</summary>
        public static KeyCode Default(InputKey key)
        {
            switch (key)
            {
                case InputKey.Comma: return KeyCode.Comma;
                case InputKey.Period: return KeyCode.Period;
                case InputKey.Tab: return KeyCode.Tab;
                case InputKey.Escape: return KeyCode.Escape;
                case InputKey.Equals: return KeyCode.Equals;
                case InputKey.Minus: return KeyCode.Minus;
                case InputKey.F1: return KeyCode.F1;
                case InputKey.Space: return KeyCode.Space;
                case InputKey.LeftShift: return KeyCode.LeftShift;
                case InputKey.LeftCtrl: return KeyCode.LeftControl;
                case InputKey.Return: return KeyCode.Return;
                default:
                    // Single letters: InputKey.W ↔ KeyCode.W.
                    return Enum.TryParse(key.ToString(), out KeyCode code) ? code : KeyCode.None;
            }
        }

        /// <summary>Short key name for key caps and hints ("W", "Shift", "Пробел", "Num 5").</summary>
        public static string DisplayName(KeyCode key)
        {
            if (key >= KeyCode.A && key <= KeyCode.Z) return key.ToString();
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return ((int)(key - KeyCode.Alpha0)).ToString();
            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9) return "Num " + (int)(key - KeyCode.Keypad0);
            switch (key)
            {
                case KeyCode.None: return "—";
                case KeyCode.Space: return "Пробел";
                case KeyCode.Return: return "Enter";
                case KeyCode.KeypadEnter: return "Num Enter";
                case KeyCode.Backspace: return "Backspace";
                case KeyCode.Escape: return "Esc";
                case KeyCode.LeftShift: return "Shift";
                case KeyCode.RightShift: return "Правый Shift";
                case KeyCode.LeftControl: return "Ctrl";
                case KeyCode.RightControl: return "Правый Ctrl";
                case KeyCode.LeftAlt: return "Alt";
                case KeyCode.RightAlt: return "Правый Alt";
                case KeyCode.UpArrow: return "Вверх";
                case KeyCode.DownArrow: return "Вниз";
                case KeyCode.LeftArrow: return "Влево";
                case KeyCode.RightArrow: return "Вправо";
                case KeyCode.Comma: return ",";
                case KeyCode.Period: return ".";
                case KeyCode.Minus: return "-";
                case KeyCode.Equals: return "=";
                case KeyCode.Slash: return "/";
                case KeyCode.Backslash: return "\\";
                case KeyCode.Semicolon: return ";";
                case KeyCode.Quote: return "'";
                case KeyCode.LeftBracket: return "[";
                case KeyCode.RightBracket: return "]";
                case KeyCode.BackQuote: return "`";
                case KeyCode.PageUp: return "PgUp";
                case KeyCode.PageDown: return "PgDn";
                case KeyCode.Insert: return "Ins";
                case KeyCode.Delete: return "Del";
                case KeyCode.KeypadPlus: return "Num +";
                case KeyCode.KeypadMinus: return "Num -";
                case KeyCode.KeypadMultiply: return "Num *";
                case KeyCode.KeypadDivide: return "Num /";
                case KeyCode.KeypadPeriod: return "Num .";
                default: return key.ToString();
            }
        }
    }
}
