using System;
using System.Collections.Generic;
using NUnit.Framework;
using SpaceSim.Runtime;
using UnityEngine;

namespace SpaceSim.Tests
{
    /// <summary>
    /// Player settings (brightness, mouse, key bindings): limits, persistence, conflict-free rebinding and robustness
    /// against a damaged save. Uses an in-memory store — the player's real PlayerPrefs are never touched.
    /// </summary>
    public class GameSettingsTests
    {
        MemorySettingsStore store;

        [SetUp]
        public void SetUp()
        {
            store = new MemorySettingsStore();
            GameSettings.Store = store;
        }

        [TearDown]
        public void TearDown() => GameSettings.Store = null; // back to PlayerPrefs, reloaded lazily

        [Test]
        public void Defaults_AreNeutral()
        {
            Assert.AreEqual(1f, GameSettings.Brightness);
            Assert.AreEqual(0f, GameSettings.ExposureEv, 1e-6f);
            Assert.AreEqual(1f, GameSettings.MouseSensitivity);
            Assert.IsFalse(GameSettings.InvertMouseY);
            foreach (KeyAction a in KeyBindings.Actions)
                Assert.AreEqual(KeyBindings.Default(a.Key), GameSettings.GetBinding(a.Key), a.Label);
        }

        [Test]
        public void DefaultLayout_IsUniqueAndAssignable()
        {
            var used = new HashSet<KeyCode>();
            foreach (KeyAction a in KeyBindings.Actions)
            {
                KeyCode k = KeyBindings.Default(a.Key);
                Assert.AreNotEqual(KeyCode.None, k, a.Label);
                Assert.IsTrue(KeyBindings.IsAssignable(k), a.Label + " → " + k);
                Assert.IsTrue(used.Add(k), "key " + k + " used twice");
                Assert.IsFalse(string.IsNullOrEmpty(KeyBindings.DisplayName(k)));
            }
            Assert.IsFalse(KeyBindings.IsRebindable(InputKey.Escape), "Esc is reserved for menus");
            Assert.IsFalse(KeyBindings.IsAssignable(KeyCode.Escape));
            Assert.AreEqual(KeyCode.LeftControl, KeyBindings.Default(InputKey.LeftCtrl));
            Assert.AreEqual("Пробел", KeyBindings.DisplayName(KeyCode.Space));
        }

        [Test]
        public void BrightnessAndMouse_AreClampedSavedAndReloaded()
        {
            int changes = 0;
            Action count = () => changes++;
            GameSettings.Changed += count;
            try
            {
                GameSettings.Brightness = 4f;
                GameSettings.MouseSensitivity = 0.01f;
                GameSettings.InvertMouseY = true;
            }
            finally
            {
                GameSettings.Changed -= count;
            }
            Assert.AreEqual(3, changes);
            Assert.AreEqual(GameSettings.MaxBrightness, GameSettings.Brightness);
            Assert.AreEqual(1f, GameSettings.ExposureEv, 1e-5f, "200 % = +1 EV");
            Assert.AreEqual(GameSettings.MinMouseSensitivity, GameSettings.MouseSensitivity);
            Assert.Greater(store.Saves, 0, "every change is saved at once");

            GameSettings.Store = store; // "restart": everything is read back from the store
            Assert.AreEqual(GameSettings.MaxBrightness, GameSettings.Brightness);
            Assert.AreEqual(GameSettings.MinMouseSensitivity, GameSettings.MouseSensitivity);
            Assert.IsTrue(GameSettings.InvertMouseY);

            GameSettings.ResetDisplay();
            GameSettings.ResetMouse();
            Assert.AreEqual(1f, GameSettings.Brightness);
            Assert.AreEqual(1f, GameSettings.MouseSensitivity);
            Assert.IsFalse(GameSettings.InvertMouseY);
        }

        [Test]
        public void Rebinding_SwapsWithTheCommandThatHadTheKey()
        {
            // N belongs to "body markers"; giving it to the hangar hands the hangar's old key B to the markers.
            InputKey? swapped = GameSettings.SetBinding(InputKey.B, KeyCode.N);
            Assert.AreEqual(InputKey.N, swapped);
            Assert.AreEqual(KeyCode.N, GameSettings.GetBinding(InputKey.B));
            Assert.AreEqual(KeyCode.B, GameSettings.GetBinding(InputKey.N));
            Assert.AreEqual(InputKey.B, GameSettings.CommandOn(KeyCode.N));

            // A free key: no swap.
            Assert.IsNull(GameSettings.SetBinding(InputKey.W, KeyCode.UpArrow));
            Assert.AreEqual(KeyCode.UpArrow, GameSettings.GetBinding(InputKey.W));
            Assert.IsNull(GameSettings.CommandOn(KeyCode.W));
            Assert.IsNull(GameSettings.SetBinding(InputKey.W, KeyCode.UpArrow), "same key again: nothing changes");

            GameSettings.Store = store; // reload
            Assert.AreEqual(KeyCode.N, GameSettings.GetBinding(InputKey.B));
            Assert.AreEqual(KeyCode.B, GameSettings.GetBinding(InputKey.N));
            Assert.AreEqual(KeyCode.UpArrow, GameSettings.GetBinding(InputKey.W));

            GameSettings.ResetBindings();
            Assert.IsTrue(GameSettings.IsDefaultBinding(InputKey.B));
            Assert.IsTrue(GameSettings.IsDefaultBinding(InputKey.W));
            Assert.AreEqual("", store.Values[GameSettings.BindingsKey], "defaults are not written out");
        }

        [Test]
        public void Rebinding_RefusesEscAndUnknownCommands()
        {
            Assert.Throws<ArgumentException>(() => GameSettings.SetBinding(InputKey.W, KeyCode.Escape));
            Assert.Throws<ArgumentException>(() => GameSettings.SetBinding(InputKey.Escape, KeyCode.Q));
            Assert.AreEqual(KeyCode.W, GameSettings.GetBinding(InputKey.W));
        }

        [Test]
        public void DamagedSave_FallsBackSafely()
        {
            store.Values[GameSettings.BrightnessKey] = "not a number";
            store.Values[GameSettings.MouseSensitivityKey] = "NaN";
            store.Values[GameSettings.BindingsKey] = "garbage;W=NotAKey;=;B=;Escape=Q;G=Escape;K=Y";
            GameSettings.Store = store;
            Assert.AreEqual(1f, GameSettings.Brightness);
            Assert.AreEqual(1f, GameSettings.MouseSensitivity);
            Assert.AreEqual(KeyCode.Y, GameSettings.GetBinding(InputKey.K), "the one valid entry is kept");
            Assert.AreEqual(KeyCode.W, GameSettings.GetBinding(InputKey.W));
            Assert.AreEqual(KeyCode.G, GameSettings.GetBinding(InputKey.G));

            // Two commands on one key would be ambiguous: the whole layout falls back to the defaults.
            store.Values[GameSettings.BindingsKey] = "W=Q;S=Q";
            GameSettings.Store = store;
            Assert.AreEqual(KeyCode.W, GameSettings.GetBinding(InputKey.W));
            Assert.AreEqual(KeyCode.S, GameSettings.GetBinding(InputKey.S));
        }

        [Test]
        public void LookSettings_ScaleAndInvertMouse()
        {
            Vector2 d = FlightInput.ApplyLookSettings(new Vector2(2f, 3f), 1.5f, invertY: true);
            Assert.AreEqual(3f, d.x, 1e-6f);
            Assert.AreEqual(-4.5f, d.y, 1e-6f);
            d = FlightInput.ApplyLookSettings(new Vector2(2f, 3f), 1f, invertY: false);
            Assert.AreEqual(2f, d.x, 1e-6f);
            Assert.AreEqual(3f, d.y, 1e-6f);
        }
    }
}
