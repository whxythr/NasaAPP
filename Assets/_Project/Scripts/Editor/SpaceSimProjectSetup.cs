using System.IO;
using SpaceSim.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
#if SPACESIM_URP
using UnityEngine.Rendering.Universal;
#endif

namespace SpaceSim.Editor
{
    /// <summary>One-click project setup: linear colour space, URP asset with depth texture, Earth asset, Phase 1 scene.</summary>
    public static class SpaceSimProjectSetup
    {
        const string Root = "Assets/_Project";
        const string EarthPath = Root + "/ScriptableObjects/Planets/Earth.asset";
        const string ScenePath = Root + "/Scenes/Phase1_Planet.unity";
        const string UrpPath = Root + "/Settings/SpaceSim_URP.asset";
        const string UrpRendererPath = Root + "/Settings/SpaceSim_URP_Renderer.asset";

        [MenuItem("SpaceSim/Setup Project (URP + Phase 1 scene)", priority = 0)]
        public static void SetupAll()
        {
            ConfigurePlayer();
            ConfigureUrp();
            var earth = EnsureEarthDefinition();
            CreatePhase1Scene(earth);
        }

        [MenuItem("SpaceSim/Configure Player (linear colour space)", priority = 20)]
        public static void ConfigurePlayer()
        {
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                PlayerSettings.colorSpace = ColorSpace.Linear;
                Debug.Log("[SpaceSim] Colour space set to Linear.");
            }
        }

        [MenuItem("SpaceSim/Configure URP (depth texture, shadows)", priority = 21)]
        public static void ConfigureUrp()
        {
#if SPACESIM_URP
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpPath);
            if (asset == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(UrpPath));
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, UrpRendererPath);
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, UrpPath);
            }
            asset.supportsCameraDepthTexture = true; // the atmosphere pass needs scene depth
            asset.supportsHDR = true;
            asset.shadowDistance = 400f;
            asset.shadowCascadeCount = 4;
            EditorUtility.SetDirty(asset);
            GraphicsSettings.defaultRenderPipeline = asset;
            QualitySettings.renderPipeline = asset;
            AssetDatabase.SaveAssets();
            Debug.Log("[SpaceSim] URP asset assigned: " + UrpPath);
#else
            Debug.LogWarning("[SpaceSim] URP package not installed — the Built-in pipeline fallback shaders will be used (no real-time terrain shadows).");
#endif
        }

        [MenuItem("SpaceSim/Create Earth Definition", priority = 40)]
        public static PlanetDefinition EnsureEarthDefinition()
        {
            var def = AssetDatabase.LoadAssetAtPath<PlanetDefinition>(EarthPath);
            if (def != null) return def;
            Directory.CreateDirectory(Path.GetDirectoryName(EarthPath));
            def = ScriptableObject.CreateInstance<PlanetDefinition>();
            AssetDatabase.CreateAsset(def, EarthPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[SpaceSim] Created " + EarthPath);
            return def;
        }

        [MenuItem("SpaceSim/Create Phase 1 Scene", priority = 41)]
        public static void CreatePhase1SceneMenu() => CreatePhase1Scene(EnsureEarthDefinition());

        static void CreatePhase1Scene(PlanetDefinition planet)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("SpaceSim");
            var boot = go.AddComponent<SpaceSimBootstrap>();
            var so = new SerializedObject(boot);
            so.FindProperty("planetDefinition").objectReferenceValue = planet;
            so.FindProperty("planetSurfaceShader").objectReferenceValue = Shader.Find("SpaceSim/PlanetSurface");
            so.FindProperty("atmosphereShader").objectReferenceValue = Shader.Find("SpaceSim/Atmosphere");
            so.FindProperty("starfieldShader").objectReferenceValue = Shader.Find("SpaceSim/Starfield");
            so.FindProperty("sunDiskShader").objectReferenceValue = Shader.Find("SpaceSim/SunDisk");
            bool urp = GraphicsSettings.currentRenderPipeline != null;
            so.FindProperty("litShader").objectReferenceValue = Shader.Find(urp ? "Universal Render Pipeline/Lit" : "Standard");
            so.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[SpaceSim] Scene created: " + ScenePath + " — press Play.");
        }

        [MenuItem("SpaceSim/Create Random Planet Definition", priority = 60)]
        public static void CreateRandomPlanet()
        {
            // Randomness only at creation time; the seeds are then stored in the asset and never change.
            int visualSeed = System.Environment.TickCount & 0x7FFFFFFF;
            int physicalSeed = (visualSeed * 1103515245 + 12345) & 0x7FFFFFFF;
            var def = ScriptableObject.CreateInstance<PlanetDefinition>();
            def.planetId = "planet-" + visualSeed.ToString("x8");
            def.displayName = "Планета " + visualSeed.ToString("x4").ToUpperInvariant();
            def.generation.seed = visualSeed;
            def.GeneratePhysicalProperties(physicalSeed);
            if (!def.physical.atmosphere.hasAtmosphere)
            {
                def.generation.hasOcean = false;
                def.generation.craterCount = 400;
                def.generation.continentAmplitude = 2500;
                def.generation.oceanDepthAmplitude = 2500;
                def.generation.grassland = def.generation.rock;
                def.generation.forest = def.generation.rock;
                def.generation.desert = new SpaceSim.Mathematics.ColorRGB(0.45f, 0.42f, 0.40f);
            }
            string dir = Root + "/ScriptableObjects/Planets";
            Directory.CreateDirectory(dir);
            string path = AssetDatabase.GenerateUniqueAssetPath(dir + "/" + def.planetId + ".asset");
            AssetDatabase.CreateAsset(def, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = def;
            Debug.Log("[SpaceSim] Created " + path + " (visual seed " + visualSeed + ", physical seed " + physicalSeed +
                      "). Assign it to the SpaceSimBootstrap to fly there.");
        }

        [MenuItem("SpaceSim/Reveal Planet Save Folder", priority = 80)]
        public static void RevealSaves()
        {
            Directory.CreateDirectory(PlanetSaveService.Directory);
            EditorUtility.RevealInFinder(PlanetSaveService.Directory);
        }
    }
}
