using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceSim.Editor
{
    /// <summary>
    /// Play in the Editor starts like the built game: from the main menu, even when the game scene is open. Only the game
    /// scene is redirected (other scenes and the Test Runner play as they are). SpaceSim ▸ Play Starts From Main Menu
    /// turns it off; <see cref="SkipOnce"/> lets tools (the bridge) enter the game scene directly.
    /// </summary>
    [InitializeOnLoad]
    public static class MainMenuPlayStart
    {
        const string MenuPath = "SpaceSim/Play Starts From Main Menu";
        const string PrefKey = "SpaceSim.PlayStartsFromMainMenu";
        const string GameScenePath = "Assets/_Project/Scenes/Phase1_Planet.unity";

        /// <summary>The next Play enters the open scene directly (reset after use).</summary>
        public static bool SkipOnce { get; set; }

        static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        static MainMenuPlayStart() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode) return;
            bool skip = SkipOnce;
            SkipOnce = false;
            EditorSceneManager.playModeStartScene = null;
            if (skip || !Enabled) return;
            if (EditorSceneManager.GetActiveScene().path != GameScenePath) return;
            var menu = AssetDatabase.LoadAssetAtPath<SceneAsset>(SpaceSimProjectSetup.MainMenuScenePath);
            if (menu != null) EditorSceneManager.playModeStartScene = menu;
        }

        [MenuItem(MenuPath, priority = 43)]
        static void Toggle() => Enabled = !Enabled;

        [MenuItem(MenuPath, true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }
    }
}
