using System.Collections;
using NUnit.Framework;
using SpaceSim.Runtime;
using SpaceSim.Runtime.Menu;
using UnityEngine;
using UnityEngine.TestTools;

namespace SpaceSim.Tests
{
    /// <summary>
    /// The in-game Esc menu and the main menu: the menu pauses the flight and gives it back as it was, hides the HUD
    /// (blocks hotkeys), Esc closes windows before it opens the menu, the brightness setting reaches the camera, and the
    /// main menu's Settings screen is the real one. Profile and settings live in temporary stores only.
    /// </summary>
    public class GameMenuPlayModeTests
    {
        GameObject root, menuGo;
        string saveDir;

        [SetUp]
        public void SetUp()
        {
            saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SpaceSimMenuTest_" + System.Guid.NewGuid().ToString("N"));
            PlayerSession.SaveDirectoryOverride = saveDir;
            GameSettings.Store = new MemorySettingsStore();
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.Destroy(root);
            if (menuGo != null) Object.Destroy(menuGo);
            var cam = GameObject.Find("SpaceCamera");
            if (cam != null) Object.Destroy(cam);
            FlightInput.PauseMenuOpen = false;
            FlightInput.ModalWindowOpen = false;
            GameSettings.Store = null;
            PlayerSession.SaveDirectoryOverride = null;
            if (System.IO.Directory.Exists(saveDir)) System.IO.Directory.Delete(saveDir, true);
        }

        [UnityTest]
        public IEnumerator EscMenu_PausesAndRestores_AndAppliesSettings()
        {
            root = new GameObject("SpaceSimTest");
            var boot = root.AddComponent<SpaceSimBootstrap>();
            for (int i = 0; i < 20; i++) yield return null;

            GameMenu menu = boot.EscMenu;
            Assert.IsNotNull(menu, "the bootstrap creates the Esc menu");
            Assert.IsFalse(menu.IsOpen);
            Assert.IsFalse(boot.Hud.AnyWindowOpen);
            Assert.IsFalse(boot.Hud.CloseTopWindow(), "nothing to close: Esc would open the menu");

            // Opening pauses the running simulation and owns the input; closing gives both back.
            Assert.IsFalse(boot.Simulation.Paused);
            menu.Open();
            yield return null;
            Assert.IsTrue(menu.IsOpen);
            Assert.IsTrue(FlightInput.PauseMenuOpen);
            Assert.IsTrue(FlightInput.IsTyping, "flight hotkeys and camera ignore the keyboard");
            Assert.IsTrue(boot.Simulation.Paused);

            // Key prompt: a pending prompt blocks Esc from leaving the menu until it is cancelled.
            menu.Settings.ShowPage(SettingsView.Page.Controls);
            menu.Settings.BeginCapture(InputKey.B);
            Assert.IsTrue(menu.Settings.IsCapturing);
            Assert.IsTrue(menu.Settings.BlocksBack);
            menu.Settings.CancelCapture();
            Assert.IsFalse(menu.Settings.IsCapturing);

            menu.Close();
            yield return null;
            Assert.IsFalse(menu.IsOpen);
            Assert.IsFalse(FlightInput.PauseMenuOpen);
            Assert.IsFalse(boot.Simulation.Paused, "the flight continues");

            // A flight the player paused stays paused after the menu.
            boot.Simulation.SetPaused(true);
            menu.Open();
            menu.Close();
            Assert.IsTrue(boot.Simulation.Paused);
            boot.Simulation.SetPaused(false);

            // Brightness reaches the camera as post-exposure; 100 % keeps post-processing off.
            Assert.IsFalse(boot.Brightness.PostProcessingActive);
            GameSettings.Brightness = 2f;
            Assert.AreEqual(1f, boot.Brightness.AppliedExposure, 1e-4f);
            Assert.IsTrue(boot.Brightness.PostProcessingActive);
            GameSettings.ResetDisplay();
            Assert.AreEqual(0f, boot.Brightness.AppliedExposure);
            Assert.IsFalse(boot.Brightness.PostProcessingActive);
        }

        [UnityTest]
        public IEnumerator MainMenu_BuildsAndOpensRealSettings()
        {
            menuGo = new GameObject("MainMenuTest");
            var menu = menuGo.AddComponent<MainMenuController>();
            yield return null;
            yield return null;
            Assert.AreEqual(MainMenuController.MainScreen, menu.CurrentScreen);
            Assert.IsNotNull(menu.Settings, "the Settings screen uses the shared settings view");

            menu.Show(MainMenuController.SettingsScreen);
            menu.Settings.ShowPage(SettingsView.Page.Mouse);
            Assert.AreEqual(SettingsView.Page.Mouse, menu.Settings.CurrentPage);
            menu.Back();
            Assert.AreEqual(MainMenuController.MainScreen, menu.CurrentScreen);
        }
    }
}
