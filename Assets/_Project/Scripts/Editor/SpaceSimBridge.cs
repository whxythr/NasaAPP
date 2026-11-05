using System;
using System.Globalization;
using System.IO;
using System.Text;
using SpaceSim.Mathematics;
using SpaceSim.Runtime;
using SpaceSim.Units;
using UnityEditor;
using UnityEngine;

namespace SpaceSim.Editor
{
    /// <summary>
    /// Local development bridge: lets a coding assistant on this machine drive the open Editor through plain files,
    /// without extra tools. Polls  Temp/SpaceSimBridge/command.txt  (one command per line), executes it, and appends
    /// results to  Temp/SpaceSimBridge/result.txt ; screenshots go to  Temp/SpaceSimBridge/*.png .
    /// Editor-only, project-local, no network. Remove this file to disable it.
    ///
    /// Commands: ping · status · refresh · setup · play · stop · screenshot NAME · launch · cut · stage ·
    ///           pitch DEG · heading DEG · throttle 0..1 · reset · view YAW PITCH DISTANCE · focus · frame ·
    ///           warp+ · warp- · pause · initial LAT LON ALT VE VN VU · jump BODY_ID · unlock · hangar · shop ·
    ///           academy [topic ID | calc ID | hint] · hints · calc ID [values…] ·
    ///           sandbox on|off (Play mode on a copy of the player's profile) · profile
    /// </summary>
    [InitializeOnLoad]
    static class SpaceSimBridge
    {
        static readonly string Dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/SpaceSimBridge"));
        static readonly string CommandPath = Path.Combine(Dir, "command.txt");
        static readonly string ResultPath = Path.Combine(Dir, "result.txt");
        static readonly string SandboxDir = Path.Combine(Dir, "sandbox_profile");
        const string SandboxKey = "SpaceSimBridge.SandboxProfile";
        static double nextPoll;

        static SpaceSimBridge()
        {
            Directory.CreateDirectory(Dir);
            PlayerSettings.runInBackground = true; // keep Play mode running while the Editor window is not focused
            EditorApplication.update += Poll;
            // Entering Play mode reloads the domain and clears statics: re-apply the sandbox before the scene starts.
            string sandbox = SessionState.GetString(SandboxKey, "");
            if (sandbox.Length > 0) PlayerSession.SaveDirectoryOverride = sandbox;
        }

        /// <summary>
        /// sandbox on — the next Play sessions load and save a COPY of the player's profile (Temp/SpaceSimBridge/sandbox_profile),
        /// so test flights, test tools and sample cargo never touch the real save. sandbox off — back to the real profile.
        /// </summary>
        static string Sandbox(bool on)
        {
            if (EditorApplication.isPlaying) return "ERROR stop Play mode first";
            if (!on)
            {
                SessionState.EraseString(SandboxKey);
                PlayerSession.SaveDirectoryOverride = null;
                return "sandbox off — real profile " + PlayerSession.SavePath;
            }
            string real = Path.Combine(Application.persistentDataPath, "Player", "profile.json");
            Directory.CreateDirectory(SandboxDir);
            string copy = Path.Combine(SandboxDir, "profile.json");
            if (File.Exists(real)) File.Copy(real, copy, true);
            SessionState.SetString(SandboxKey, SandboxDir);
            PlayerSession.SaveDirectoryOverride = SandboxDir;
            return "sandbox on — profile copy " + copy + (File.Exists(real) ? "" : " (no real profile yet: a new one)");
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 0.25;
            if (!File.Exists(CommandPath)) return;
            string text;
            try
            {
                text = File.ReadAllText(CommandPath);
                File.Delete(CommandPath);
            }
            catch (IOException)
            {
                return; // still being written
            }
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                string result;
                try
                {
                    result = Execute(line);
                }
                catch (Exception e)
                {
                    result = "ERROR " + e.GetType().Name + ": " + e.Message;
                }
                File.AppendAllText(ResultPath, DateTime.Now.ToString("HH:mm:ss") + " > " + line + "\n" + result + "\n", Encoding.UTF8);
            }
        }

        static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

        static string BuildScenes()
        {
            var sb = new StringBuilder();
            foreach (var s in EditorBuildSettings.scenes) sb.Append(s.path).Append(s.enabled ? "" : " (off)").Append("; ");
            return sb.ToString();
        }

        /// <summary>
        /// settings [sandbox | brightness PCT | sens X | invert 0|1 | bind CMD KEY | reset] — the player's settings.
        /// "sandbox" (Play mode) keeps every later change in memory only, so tests never touch the real PlayerPrefs.
        /// </summary>
        static string Settings(string[] a)
        {
            string sub = a.Length > 1 ? a[1].ToLowerInvariant() : "";
            switch (sub)
            {
                case "sandbox":
                    if (!EditorApplication.isPlaying) return "ERROR sandbox only in Play mode (the store resets on domain reload)";
                    GameSettings.Store = new MemorySettingsStore();
                    break;
                case "brightness": GameSettings.Brightness = (float)(D(a[2]) / 100.0); break;
                case "sens": GameSettings.MouseSensitivity = (float)D(a[2]); break;
                case "invert": GameSettings.InvertMouseY = a[2] == "1"; break;
                case "bind":
                {
                    var key = (InputKey)Enum.Parse(typeof(InputKey), a[2], true);
                    var code = (KeyCode)Enum.Parse(typeof(KeyCode), a[3], true);
                    InputKey? swapped = GameSettings.SetBinding(key, code);
                    if (swapped.HasValue) return "bound " + key + "=" + code + " (swapped with " + swapped.Value + ") · " + SettingsSummary();
                    break;
                }
                case "reset":
                    GameSettings.ResetDisplay();
                    GameSettings.ResetMouse();
                    GameSettings.ResetBindings();
                    break;
                case "forget":
                    // Removes the settings from the real PlayerPrefs entirely (as if never set).
                    foreach (string k in new[] { GameSettings.BrightnessKey, GameSettings.MouseSensitivityKey, GameSettings.InvertMouseYKey, GameSettings.BindingsKey })
                        PlayerPrefs.DeleteKey(k);
                    PlayerPrefs.Save();
                    GameSettings.Reload();
                    break;
            }
            return SettingsSummary();
        }

        static string SettingsSummary()
        {
            var sb = new StringBuilder();
            sb.Append("store=").Append(GameSettings.Store.GetType().Name)
              .Append(" brightness=").Append((GameSettings.Brightness * 100f).ToString("0", CultureInfo.InvariantCulture)).Append('%')
              .Append(" sens=").Append(GameSettings.MouseSensitivity.ToString("0.00", CultureInfo.InvariantCulture))
              .Append(" invertY=").Append(GameSettings.InvertMouseY).Append(" custom keys:");
            foreach (KeyAction k in KeyBindings.Actions)
                if (!GameSettings.IsDefaultBinding(k.Key)) sb.Append(' ').Append(k.Key).Append('=').Append(GameSettings.GetBinding(k.Key));
            return sb.ToString();
        }

        static string Execute(string line)
        {
            string[] a = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string cmd = a[0].ToLowerInvariant();
            switch (cmd)
            {
                case "ping": return "pong (playing=" + EditorApplication.isPlaying + ", compiling=" + EditorApplication.isCompiling + ")";
                case "sandbox": return Sandbox(a.Length > 1 && a[1] == "on");
                case "profile":
                {
                    var bootP = EditorApplication.isPlaying ? UnityEngine.Object.FindFirstObjectByType<SpaceSimBootstrap>() : null;
                    return "savePath=" + PlayerSession.SavePath + " sandbox=" + (SessionState.GetString(SandboxKey, "").Length > 0) +
                           (bootP != null && bootP.Session != null && bootP.Session.Profile != null
                               ? " credits=" + bootP.Session.Profile.credits + " samples=" + bootP.Session.Profile.samples.Count + " readOnly=" + bootP.Session.ReadOnly
                               : "");
                }
                case "refresh":
                    AssetDatabase.Refresh();
                    return "refreshed";
                case "setup":
                    if (EditorApplication.isPlaying) return "ERROR stop Play mode first";
                    SpaceSimProjectSetup.SetupAll();
                    return "setup done; active scene " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
                case "test":
                    if (EditorApplication.isPlaying) return "ERROR stop Play mode first";
                    return SpaceSimBridgeTests.Run(a.Length > 1 ? a[1].ToLowerInvariant() : "editmode");
                case "errors":
                    return CompileState();
                case "scene":
                {
                    if (EditorApplication.isPlaying) return "ERROR stop Play mode first";
                    string path = a.Length > 1 ? a[1] : "Assets/_Project/Scenes/Phase1_Planet.unity";
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
                    return "opened " + path;
                }
                case "playgame":
                    MainMenuPlayStart.SkipOnce = true; // straight into the open scene, no main menu
                    EditorApplication.isPlaying = true;
                    return "entering play mode (no main menu)";
                case "play":
                    EditorApplication.isPlaying = true;
                    return "entering play mode";
                case "stop":
                    EditorApplication.isPlaying = false;
                    return "leaving play mode";
                case "screenshot":
                {
                    string name = a.Length > 1 ? a[1] : "shot";
                    string path = Path.Combine(Dir, name + ".png");
                    if (File.Exists(path)) File.Delete(path);
                    ScreenCapture.CaptureScreenshot(path);
                    return "capturing " + path;
                }
            }

            switch (cmd)
            {
                case "menuscene":
                {
                    if (EditorApplication.isPlaying) return "ERROR stop Play mode first";
                    var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                    if (active.isDirty) return "ERROR the open scene has unsaved changes: " + active.path;
                    SpaceSimProjectSetup.BuildMainMenuScene();
                    return "main menu scene created; build scenes: " + BuildScenes();
                }
                case "buildscenes": return BuildScenes();
                case "uipanel":
                    SpaceSimProjectSetup.EnsureUiPanelAsset();
                    return "ui panel template: " + (Resources.Load<UnityEngine.UIElements.PanelSettings>("UI/HorizonPanel") != null ? "ok" : "MISSING");
                case "settings": return Settings(a);
                case "fonts": return "HUD fonts: " + HudStyle.FontInfo;
                case "menu":
                {
                    if (!EditorApplication.isPlaying) return "ERROR not in Play mode";
                    var mm = UnityEngine.Object.FindFirstObjectByType<SpaceSim.Runtime.Menu.MainMenuController>();
                    if (mm == null) return "ERROR no MainMenuController in the scene";
                    if (a.Length > 2 && a[1] == "show") mm.Show(a[2]);
                    else if (a.Length > 1 && a[1] == "back") mm.Back();
                    else if (a.Length > 2 && a[1] == "page") mm.Settings.ShowPage((SpaceSim.Runtime.Menu.SettingsView.Page)int.Parse(a[2]));
                    else if (a.Length > 2 && a[1] == "capture") mm.Settings.BeginCapture((InputKey)Enum.Parse(typeof(InputKey), a[2], true));
                    else if (a.Length > 1 && a[1] == "continue") mm.BeginMission();
                    return "screen=" + mm.CurrentScreen + " ready=" + mm.IsReady +
                           (mm.Settings != null ? " page=" + mm.Settings.CurrentPage + " capturing=" + mm.Settings.IsCapturing : "");
                }
            }

            if (!EditorApplication.isPlaying) return "ERROR not in Play mode";
            var boot = UnityEngine.Object.FindFirstObjectByType<SpaceSimBootstrap>();
            if (boot == null) return "ERROR no SpaceSimBootstrap in the scene";
            var r = boot.Body.Rocket;
            switch (cmd)
            {
                case "status": return Status(boot);
                case "launch": return boot.Body.Launch() ? "ignition" : "cannot ignite";
                case "cut":
                    if (r != null) r.EngineOn = false;
                    return "engine off";
                case "stage": return boot.Body.SeparateStage() ? "stage separated" : "nothing to separate";
                case "pitch":
                    r.PitchDegrees = D(a[1]);
                    return "pitch " + r.PitchDegrees;
                case "heading":
                    r.HeadingDegrees = D(a[1]);
                    return "heading " + r.HeadingDegrees;
                case "throttle":
                    r.Throttle = D(a[1]);
                    return "throttle " + r.Throttle;
                case "reset":
                    boot.Body.ResetToLaunchSite();
                    return "new rocket on the pad";
                case "view":
                    boot.CameraController.SetView((float)D(a[1]), (float)D(a[2]), D(a[3]));
                    return "view set";
                case "focus":
                    boot.CameraController.ToggleFocus();
                    return "focus " + boot.CameraController.Focus;
                case "frame":
                    boot.CameraController.ToggleFrame();
                    return "frame " + boot.CameraController.Frame;
                case "warp+":
                    boot.Simulation.IncreaseWarp();
                    return "warp ×" + boot.Simulation.TimeWarp;
                case "warp-":
                    boot.Simulation.DecreaseWarp();
                    return "warp ×" + boot.Simulation.TimeWarp;
                case "pause":
                    boot.Simulation.TogglePause();
                    return "paused " + boot.Simulation.Paused;
                case "initial":
                    boot.Body.ApplyInitialConditions(D(a[1]), D(a[2]), D(a[3]), D(a[4]), D(a[5]), D(a[6]), true);
                    return "initial conditions applied";
                case "jump":
                {
                    int i = boot.System.IndexOf(a[1]);
                    if (i < 0) return "ERROR unknown body " + a[1];
                    boot.SwitchToBody(i);
                    return "active " + boot.System.ActiveBody.Name;
                }
                case "unlock":
                    boot.System.UnlockSpace();
                    return "space unlocked";
                case "hangar":
                {
                    var hangar = UnityEngine.Object.FindFirstObjectByType<HangarUI>();
                    if (hangar == null) return "ERROR no HangarUI";
                    hangar.Toggle();
                    return "hangar open=" + hangar.IsOpen;
                }
                case "shop":
                {
                    var shop = UnityEngine.Object.FindFirstObjectByType<ShopUI>();
                    if (shop == null) return "ERROR no ShopUI";
                    shop.Toggle();
                    return "shop open=" + shop.IsOpen + " credits=" + boot.Session.Profile.credits + " readOnly=" + boot.Session.ReadOnly;
                }
                case "academy":
                {
                    var ui = UnityEngine.Object.FindFirstObjectByType<AcademyUI>();
                    if (ui == null || boot.Academy == null) return "ERROR no Academy";
                    if (a.Length > 2 && a[1] == "topic") ui.OpenTopic(a[2]);
                    else if (a.Length > 2 && a[1] == "calc") ui.OpenCalculator(a[2]);
                    else if (a.Length > 1 && a[1] == "hint" && boot.Academy.Advisor.Latest != null) ui.OpenHint(boot.Academy.Advisor.Latest);
                    else ui.Toggle();
                    return "academy open=" + ui.IsOpen + " topics=" + boot.Academy.Topics.Count + " calculators=" + boot.Academy.Calculators.Count +
                           " hints=" + boot.Academy.Advisor.History.Count + " paused=" + boot.Simulation.Paused +
                           " read=" + boot.Session.Profile.academy.readTopics.Count + " solved=" + boot.Session.Profile.academy.solvedExercises.Count;
                }
                case "hints":
                {
                    var sb = new StringBuilder();
                    foreach (var h in boot.Academy.Advisor.History)
                        sb.AppendLine("t=" + UnitFormatter.Number(h.RaisedAt, 1) + " " + h.Id + " [" + h.Severity + "] active=" + boot.Academy.Advisor.IsActive(h.Id) + " — " + h.Message);
                    return sb.Length > 0 ? sb.ToString().TrimEnd() : "no hints";
                }
                case "calc":
                {
                    // calc ID [v1 v2 …] — runs an in-game Academy calculator (defaults for missing values).
                    var calc = a.Length > 1 ? boot.Academy.FindCalculator(a[1]) : null;
                    if (calc == null) return "ERROR unknown calculator";
                    double[] values = calc.Defaults();
                    for (int i = 0; i + 2 < a.Length && i < values.Length; i++) values[i] = D(a[i + 2]);
                    var res = calc.Compute(values);
                    if (!res.Ok) return "ERROR " + res.Error;
                    var sb = new StringBuilder();
                    foreach (var o in res.Outputs) sb.AppendLine(o.Symbol + " = " + o.DisplayText);
                    foreach (var w in res.Warnings) sb.AppendLine("warn: " + w);
                    return sb.ToString().TrimEnd();
                }
                case "nav":
                {
                    var ui = UnityEngine.Object.FindFirstObjectByType<NavigationUI>();
                    if (ui == null) return "ERROR no NavigationUI";
                    if (a.Length > 1 && a[1] == "plan") ui.Open(NavigationUI.Page.Plan);
                    else if (a.Length > 1 && a[1] == "map") ui.Open(NavigationUI.Page.Map);
                    else if (a.Length > 1 && a[1] == "planets") ui.Open(NavigationUI.Page.Planets);
                    else ui.Toggle();
                    return "nav open=" + ui.IsOpen;
                }
                case "orbit":
                {
                    // orbit ALT_M — test tool: circular orbit (inertial, eastward) at the pad's latitude/longitude.
                    var site = boot.Planet.Definition.launchSite;
                    double alt = a.Length > 1 ? D(a[1]) : 300000.0;
                    double vc = System.Math.Sqrt(boot.Planet.Model.GravitationalParameter / (boot.Planet.Model.Radius + alt));
                    boot.Body.ApplyInitialConditions(site.latitudeDegrees, site.longitudeDegrees, alt, vc, 0.0, 0.0, false);
                    return "circular orbit alt=" + alt + " v=" + UnitFormatter.Number(vc, 1);
                }
                case "target":
                {
                    int i = boot.System.IndexOf(a[1]);
                    if (i < 0) return "ERROR unknown body";
                    boot.Navigation.SetTarget(i);
                    return NavSummary(boot);
                }
                case "plan": return NavSummary(boot);
                case "suggest":
                    boot.Navigation.SuggestAsync();
                    return "suggest: " + boot.Navigation.Status;
                case "validate":
                {
                    var res = boot.Navigation.Validate();
                    if (res == null) return "ERROR no navigation";
                    var sb = new StringBuilder(res.Verdict);
                    foreach (var issue in res.Issues) sb.Append("\n- [" + issue.Code + "] " + issue.Reason + " → " + issue.Fix);
                    if (res.Prediction != null && res.Prediction.Encounter)
                        sb.Append("\nencounter in " + UnitFormatter.Number((res.Prediction.EncounterTime - boot.Navigation.Now) / 86400.0, 2) + " d, v_inf=" +
                                  UnitFormatter.Number(res.Prediction.ArrivalVInfinity, 0) + " periapsis_alt=" +
                                  UnitFormatter.Number((res.Prediction.ArrivalPeriapsis - boot.System.Navigation[res.Prediction.Target].Radius) / 1000.0, 0) + "km capture=" +
                                  UnitFormatter.Number(res.ActualCaptureDeltaV, 0));
                    return sb.ToString();
                }
                case "node":
                {
                    // node +DT PROGRADE [NORMAL RADIAL] — time relative to now
                    var n = new SpaceSim.Navigation.ManeuverNode { Time = boot.Navigation.Now + D(a[1]), Prograde = D(a[2]), Normal = a.Length > 3 ? D(a[3]) : 0, Radial = a.Length > 4 ? D(a[4]) : 0 };
                    boot.Navigation.SetNode(n);
                    return "node set; " + NavSummary(boot);
                }
                case "clearnode":
                    boot.Navigation.ClearNode();
                    return "node cleared";
                case "burnnode":
                {
                    // Test tool: applies the planned node impulsively NOW (no engine) — marks the flight as a test.
                    var node = boot.Navigation.Plan.Departure;
                    if (node == null) return "ERROR no node";
                    var c = boot.Body.Simulation.CoastStateNow();
                    boot.Body.ApplyTestImpulse(node.DeltaVector(c.Position, c.Velocity));
                    return "impulse " + UnitFormatter.Number(node.Magnitude, 1) + " m/s applied at t=" + UnitFormatter.Number(boot.Navigation.Now, 1);
                }
                case "warpset":
                {
                    double want = a.Length > 1 ? D(a[1]) : 1.0;
                    boot.Simulation.ResetWarp();
                    for (int k = 0; k < 20 && boot.Simulation.TimeWarp < want; k++) boot.Simulation.IncreaseWarp();
                    return "warp ×" + boot.Simulation.TimeWarp;
                }
                case "eva":
                {
                    var ex = boot.Explorer;
                    string reply = ex.Interact();
                    return "eva=" + ex.Eva + " " + reply;
                }
                case "walk":
                {
                    // walk FORWARD RIGHT SECONDS [run] — scripted keys for the astronaut
                    boot.Explorer.Drive(D(a[1]), D(a[2]), (float)D(a[3]), a.Length > 4);
                    return "walking";
                }
                case "face":
                    boot.Explorer.Face((float)D(a[1]));
                    return "yaw " + boot.Explorer.Yaw;
                case "hop":
                    boot.Explorer.Jump();
                    return "jump";
                case "look":
                    // look PITCH — head pitch in degrees (+ up, − down)
                    boot.Explorer.Look((float)D(a[1]));
                    return "pitch " + boot.Explorer.LookPitch;
                case "tp":
                    boot.Explorer.ToggleThirdPerson();
                    return "thirdPerson=" + boot.Explorer.ThirdPerson;
                case "shadowdiag":
                {
                    // Real-time shadow state: pipeline, sun, camera and the shadow casters in the scene.
                    var sb = new StringBuilder();
#if SPACESIM_URP
                    var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
                    sb.AppendLine(rp == null ? "pipeline=none" : "pipeline=" + rp.name + " mainShadows=" + rp.supportsMainLightShadows + " res=" + rp.mainLightShadowmapResolution +
                                                                 " dist=" + rp.shadowDistance + " cascades=" + rp.shadowCascadeCount + " soft=" + rp.supportsSoftShadows +
                                                                 " depthBias=" + rp.shadowDepthBias + " normalBias=" + rp.shadowNormalBias);
                    var camData = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(boot.CameraController.Camera);
                    sb.AppendLine("camera renderShadows=" + (camData != null && camData.renderShadows) + " near=" + boot.CameraController.Camera.nearClipPlane + " far=" + boot.CameraController.Camera.farClipPlane);
#endif
                    Light sun = RenderSettings.sun;
                    sb.AppendLine(sun == null ? "sun=none" : "sun enabled=" + sun.enabled + " shadows=" + sun.shadows + " intensity=" + sun.intensity + " strength=" + sun.shadowStrength +
                                                             " elevation=" + UnitFormatter.Number(90.0 - Vector3.Angle(-sun.transform.forward, boot.CameraController.transform.up), 1) + "°");
                    int casters = 0, terrain = 0;
                    float largest = 0f;
                    string largestName = "";
                    foreach (Renderer rend in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    {
                        if (!rend.enabled || !rend.gameObject.activeInHierarchy || rend.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off) continue;
                        casters++;
                        if (rend.sharedMaterial != null && rend.sharedMaterial.shader != null && rend.sharedMaterial.shader.name == "SpaceSim/PlanetSurface") terrain++;
                        float size = rend.bounds.size.magnitude;
                        if (size > largest)
                        {
                            largest = size;
                            largestName = rend.name;
                        }
                    }
                    sb.Append("casters=" + casters + " terrainCasters=" + terrain + " largest=" + largestName + " (" + UnitFormatter.Number(largest, 0) + " m)");
                    return sb.ToString();
                }
                case "clip":
                    // clip NEAR FAR — fixed camera planes for diagnostics; "clip 0 0" — automatic again
                    boot.CameraController.DebugClipOverride = new Vector2((float)D(a[1]), (float)D(a[2]));
                    return "clip override " + boot.CameraController.DebugClipOverride;
                case "terrainshadows":
                {
                    // terrainshadows on|off — whether terrain chunks cast real-time shadows (diagnostics).
                    bool on = a.Length > 1 && a[1] == "on";
                    int n = 0;
                    foreach (Renderer rend in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    {
                        if (rend.sharedMaterial == null || rend.sharedMaterial.shader == null || rend.sharedMaterial.shader.name != "SpaceSim/PlanetSurface") continue;
                        rend.shadowCastingMode = on ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                        n++;
                    }
                    return "terrain casters " + (on ? "on" : "off") + ": " + n;
                }
                case "shadowtest":
                {
                    // A floating 2 m cube and a white 8 m floor beside the astronaut (URP/Lit): who casts, who receives.
                    var model = boot.Explorer.Model;
                    if (model == null) return "ERROR go outside first (eva)";
                    var old = GameObject.Find("ShadowTest");
                    if (old != null) UnityEngine.Object.Destroy(old);
                    if (a.Length > 1 && a[1] == "off") return "removed";
                    var rootGo = new GameObject("ShadowTest");
                    Transform feet = model.Root;
                    rootGo.transform.SetParent(feet, false); // the body is re-placed every frame (floating origin)
                    Shader lit = Shader.Find("Universal Render Pipeline/Lit");
                    var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    floor.transform.SetParent(rootGo.transform, false);
                    floor.transform.localPosition = new Vector3(-4f, 0.03f, 5f);
                    floor.transform.localScale = Vector3.one * 0.6f;
                    floor.GetComponent<Renderer>().sharedMaterial = ProceduralMeshes.LitMaterial(lit, new Color(0.9f, 0.9f, 0.9f));
                    // Cubes exactly on the sun ray through the floor's centre and through a bare-ground point: their shadows
                    // must land there.
                    Vector3 toSun = RenderSettings.sun != null ? -RenderSettings.sun.transform.forward : feet.up;
                    Vector3 groundPoint = feet.TransformPoint(new Vector3(4f, 0f, 5f));
                    var onFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    onFloor.transform.SetParent(rootGo.transform, false);
                    onFloor.transform.position = floor.transform.position + toSun * 3f;
                    onFloor.transform.localScale = Vector3.one;
                    onFloor.GetComponent<Renderer>().sharedMaterial = ProceduralMeshes.LitMaterial(lit, new Color(0.8f, 0.2f, 0.2f));
                    var onGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    onGround.transform.SetParent(rootGo.transform, false);
                    onGround.transform.position = groundPoint + toSun * 3f;
                    onGround.transform.localScale = Vector3.one;
                    onGround.GetComponent<Renderer>().sharedMaterial = ProceduralMeshes.LitMaterial(lit, new Color(0.2f, 0.3f, 0.8f));
                    return "shadow test placed: white floor 4 m left / 5 m ahead with a red cube 3 m up its sun ray; blue cube 3 m up the sun ray of bare ground 4 m right / 5 m ahead";
                }
                case "stride":
                {
                    // Sprint and body state of the astronaut.
                    var w = boot.Explorer.Walker;
                    if (w == null) return "eva=False";
                    var m = boot.Explorer.Model;
                    return "g=" + UnitFormatter.Number(w.Gravity, 3) + " v=" + UnitFormatter.Number(w.HorizontalSpeed, 3) + " gait=" + w.Gait +
                           " sprinting=" + w.Sprinting + " canSprint=" + w.CanSprint + " walk=" + UnitFormatter.Number(w.WalkSpeed, 3) +
                           " sprint=" + UnitFormatter.Number(w.SprintSpeed, 3) + " limit=" + SpaceSim.Exploration.Locomotion.SprintLimitedBy(w.Gravity) +
                           " P=" + UnitFormatter.Number(w.MetabolicPower, 0) + "W reserve=" + UnitFormatter.Number(w.Stamina.Fraction * 100.0, 1) + "% exhausted=" + w.Stamina.Exhausted +
                           " endurance=" + UnitFormatter.Number(SpaceSim.Exploration.Locomotion.SprintEndurance(w.Gravity), 1) + "s blocked=" + w.SprintBlocked +
                           (m != null ? " lean=" + UnitFormatter.Number(m.LeanDegrees, 1) + " eyeY=" + UnitFormatter.Number(m.EyeLocal.y, 3) + " fp=" + m.FirstPerson +
                                        " fov=" + UnitFormatter.Number(boot.CameraController.Camera.fieldOfView, 1) : " model=null");
                }
                case "sites":
                {
                    var ex = boot.Explorer;
                    var sb = new StringBuilder();
                    var w = ex.Walker;
                    sb.AppendLine("eva=" + ex.Eva + (w != null ? " g=" + UnitFormatter.Number(w.Gravity, 3) + " walk=" + UnitFormatter.Number(w.WalkSpeed, 2) +
                                  " jumpH=" + UnitFormatter.Number(w.JumpHeight, 2) + " apex=" + UnitFormatter.Number(w.JumpApex, 2) + " walked=" + UnitFormatter.Number(w.DistanceWalked, 1) +
                                  " toRocket=" + UnitFormatter.Number(w.DistanceToAnchor, 1) + " onGround=" + w.OnGround + " blocked=" + w.Blocked : "") +
                                  " cargo=" + UnitFormatter.Number(boot.Session.Profile.CargoMass(), 2) + "kg capacity=" + UnitFormatter.Number(boot.Body.Capabilities.SampleCapacity, 0));
                    if (ex.Sites != null)
                    {
                        Vector3d from = w != null ? w.Position : boot.Body.Simulation.LandedFixedPosition;
                        var list = new System.Collections.Generic.List<SpaceSim.Exploration.SampleSite>(ex.Sites);
                        list.Sort((x, y) => (Ground(boot, x) - from).Magnitude.CompareTo((Ground(boot, y) - from).Magnitude));
                        for (int k = 0; k < System.Math.Min(6, list.Count); k++)
                        {
                            var s = list[k];
                            Vector3d d = Ground(boot, s) - from;
                            SpaceSim.Physics.PlanetRotation.LocalBasis(from.Normalized, out Vector3d e, out Vector3d nn);
                            double bearing = System.Math.Atan2(Vector3d.Dot(d, e), Vector3d.Dot(d, nn)) * 57.2958;
                            sb.AppendLine(s.Id + " " + s.Name + " [" + s.Category + "/" + s.Rarity + "] " + UnitFormatter.Number(s.Mass, 2) + "kg dist~" +
                                          UnitFormatter.Number(d.Magnitude, 1) + "m bearing=" + UnitFormatter.Number((bearing + 360) % 360, 0) +
                                          " taken=" + boot.Session.Profile.collectedSites.Contains(s.Id));
                        }
                    }
                    return sb.ToString().TrimEnd();
                }
                case "landing":
                {
                    var l = boot.Body.LastLanding;
                    return l.HasValue ? l.Value.Result + ": " + l.Value.Reason + " destroyed=" + boot.Body.Destroyed + " integrity=" + UnitFormatter.Number(boot.Body.Condition.Integrity, 2) : "no touchdown yet";
                }
                case "terrain":
                {
                    // Diagnostics: terrain on|off (chunk renderers of the active body).
                    bool on = a.Length < 2 || a[1] != "off";
                    var gen = boot.PlanetVisual.Procedural;
                    int n = 0;
                    if (gen != null)
                        foreach (var mr in gen.GetComponentsInChildren<MeshRenderer>(true))
                        {
                            mr.enabled = on;
                            n++;
                        }
                    return "terrain renderers " + (on ? "on" : "off") + ": " + n;
                }
                case "terrainh":
                {
                    // Diagnostics: heights of the DISPLAYED terrain mesh within 30 m of the pad, relative to the physical ground there.
                    var site = UnityEngine.Object.FindFirstObjectByType<LaunchSite>();
                    var gen = boot.PlanetVisual.Procedural;
                    Vector3 p = site.transform.position, up = site.transform.up;
                    float lo = float.MaxValue, hi = float.MinValue;
                    int count = 0, chunks = 0;
                    var levels = new StringBuilder();
                    foreach (var mr in gen.GetComponentsInChildren<MeshRenderer>(false))
                    {
                        if (!mr.enabled || !mr.gameObject.activeInHierarchy) continue;
                        var mf = mr.GetComponent<MeshFilter>();
                        if (mf == null || mf.sharedMesh == null) continue;
                        var verts = mf.sharedMesh.vertices;
                        bool any = false;
                        foreach (var v in verts)
                        {
                            Vector3 w = mr.transform.TransformPoint(v) - p;
                            float hgt = Vector3.Dot(w, up);
                            if ((w - up * hgt).magnitude > 30f || hgt < -20f) continue; // skip skirts far below
                            lo = Mathf.Min(lo, hgt);
                            hi = Mathf.Max(hi, hgt);
                            count++;
                            any = true;
                        }
                        if (any)
                        {
                            chunks++;
                            levels.Append(" " + mr.transform.lossyScale.x.ToString("0.###") + "/" + verts.Length);
                        }
                    }
                    return "terrain vertices within 30 m of pad: " + count + " in " + chunks + " chunks; height vs physical ground: min " + lo + " m, max " + hi + " m; chunk scale/verts:" + levels;
                }
                case "padlift":
                {
                    // Diagnostics: padlift METRES — moves the pad slab/apron/scorch up (visual only).
                    var site = UnityEngine.Object.FindFirstObjectByType<LaunchSite>();
                    float k = (float)D(a[1]);
                    foreach (Transform c in site.transform)
                        if (c.name == "Pad" || c.name == "Apron" || c.name == "Scorch") c.localPosition += new Vector3(0f, k, 0f);
                    return "pad lifted by " + k;
                }
                case "padinfo":
                {
                    // Diagnostics: where the pad's parts are and whether they render.
                    var site = UnityEngine.Object.FindFirstObjectByType<LaunchSite>();
                    if (site == null) return "ERROR no LaunchSite";
                    var sb = new StringBuilder("site pos=" + site.transform.position + " up=" + site.transform.up + " lossyScale=" + site.transform.lossyScale +
                                               " camera=" + boot.CameraController.transform.position);
                    foreach (Transform c in site.transform)
                    {
                        var mr = c.GetComponent<MeshRenderer>();
                        if (c.name != "Pad" && c.name != "Apron" && c.name != "Scorch") continue;
                        sb.Append("\n" + c.name + " localPos=" + c.localPosition + " scale=" + c.localScale + " active=" + c.gameObject.activeInHierarchy +
                                  (mr != null ? " enabled=" + mr.enabled + " visible=" + mr.isVisible + " bounds=" + mr.bounds.center + "/" + mr.bounds.size +
                                                " shader=" + (mr.sharedMaterial != null ? mr.sharedMaterial.shader.name : "null") + " queue=" + (mr.sharedMaterial != null ? mr.sharedMaterial.renderQueue : -1) : " no renderer"));
                    }
                    var dir = boot.Planet.LaunchSiteDirection();
                    sb.Append("\nsurface height at pad=" + UnitFormatter.Number(boot.Planet.Model.Surface.SurfaceHeight(dir), 3) + " m");
                    return sb.ToString();
                }
                case "newrocket":
                    boot.NewRocketAtHome();
                    return "new rocket; body=" + boot.System.ActiveBody.Id;
                case "gamemenu":
                {
                    // gamemenu [open | close | esc | page 0..2 | capture CMD | cancel | modal]
                    var gm = boot.EscMenu;
                    if (gm == null) return "ERROR no Esc menu";
                    string sub = a.Length > 1 ? a[1].ToLowerInvariant() : "";
                    if (sub == "open") gm.Open();
                    else if (sub == "close") gm.Close();
                    else if (sub == "esc") gm.HandleEscape();
                    else if (sub == "page" && a.Length > 2) gm.Settings.ShowPage((SpaceSim.Runtime.Menu.SettingsView.Page)int.Parse(a[2]));
                    else if (sub == "capture" && a.Length > 2) gm.Settings.BeginCapture((InputKey)Enum.Parse(typeof(InputKey), a[2], true));
                    else if (sub == "cancel") gm.Settings.CancelCapture();
                    else if (sub == "tomenu") { gm.ReturnToMainMenu(); return "loading " + SpaceSim.Runtime.Menu.GameMenu.MainMenuScene; }
                    return "open=" + gm.IsOpen + " modal=" + gm.ModalOpen + " paused=" + boot.Simulation.Paused + " pauseMenuFlag=" + FlightInput.PauseMenuOpen +
                           " page=" + gm.Settings.CurrentPage + " capturing=" + gm.Settings.IsCapturing + " windows=" + boot.Hud.AnyWindowOpen +
                           " brightnessEV=" + boot.Brightness.AppliedExposure.ToString("0.###", CultureInfo.InvariantCulture) +
                           " pp=" + boot.Brightness.PostProcessingActive + " status=" + gm.Settings.Status;
                }
                case "manual":
                    boot.Hud.ToggleManual();
                    return "manual toggled; windows=" + boot.Hud.AnyWindowOpen;
                case "apply-test":
                {
                    // Default design + navigation computer + an extra 10 t tank on stage 1, through the real hangar path.
                    var bp = SpaceSim.Vehicles.RocketBlueprint.CreateDefault();
                    bp.payloadModules.Add(SpaceSim.Vehicles.ModuleCatalog.NavComputer);
                    bp.stages[0].tanks.Add(new SpaceSim.Vehicles.TankLoad { moduleId = SpaceSim.Vehicles.ModuleCatalog.TankK10, propellantLoaded = 10000 });
                    bool ok = boot.Body.ApplyBlueprint(bp);
                    return "applied=" + ok + " mass=" + UnitFormatter.Number(boot.Body.Simulation.CurrentMass, 0) +
                           " orbitPrediction=" + boot.Body.Capabilities.Has(SpaceSim.Vehicles.ModuleCapability.OrbitPrediction) +
                           " dv=" + UnitFormatter.Number(boot.Body.Rocket.DeltaVRemaining(), 0) + " issues=" + boot.Body.Compiled.Issues.Count;
                }
                default: return "ERROR unknown command";
            }
        }

        /// <summary>Whether the last script compilation succeeded (Editor-side view).</summary>
        static string CompileState() =>
            "compiling=" + EditorApplication.isCompiling + " scriptCompilationFailed=" + EditorUtility.scriptCompilationFailed;

        static Vector3d Ground(SpaceSimBootstrap boot, SpaceSim.Exploration.SampleSite s) =>
            s.FixedDirection * (boot.Planet.Model.Radius + boot.Planet.Model.Surface.SurfaceHeight(s.FixedDirection));

        static string NavSummary(SpaceSimBootstrap boot)
        {
            var nav = boot.Navigation;
            var sys = nav.System;
            var sb = new StringBuilder("origin=" + sys.NameOf(nav.Origin) + " target=" + (nav.Plan.Target >= 0 ? sys.NameOf(nav.Plan.Target) : "-") + " assisted=" + nav.Assisted);
            var q = nav.Requirements;
            if (q != null && q.Ok)
                sb.Append("\nlevelA: transfer=" + UnitFormatter.Number(q.TransferTime / 86400.0, 1) + "d theta=" + UnitFormatter.Number(q.RequiredPhase * 57.29578, 1) +
                          " phase_now=" + UnitFormatter.Number(q.CurrentPhase * 57.29578, 1) + " window_in=" + UnitFormatter.Number(q.TimeToWindow / 86400.0, 2) + "d dv_dep=" +
                          UnitFormatter.Number(q.DepartureDeltaV, 0) + " dv_cap=" + UnitFormatter.Number(q.CaptureDeltaV, 0) + " total=" + UnitFormatter.Number(q.TotalDeltaV, 0) +
                          " vehicle_dv=" + UnitFormatter.Number(nav.Vehicle().DeltaV, 0));
            else if (q != null) sb.Append("\nlevelA: " + q.Error);
            var n = nav.Plan.Departure;
            if (n != null) sb.Append("\nnode: in " + UnitFormatter.Number(n.Time - nav.Now, 0) + "s dv=" + UnitFormatter.Number(n.Magnitude, 1) + " (p " + UnitFormatter.Number(n.Prograde, 1) + ", n " + UnitFormatter.Number(n.Normal, 1) + ")");
            var g = nav.Guidance;
            if (g.Active) sb.Append("\nguidance:ignite_in=" + UnitFormatter.Number(g.TimeToIgnition, 0) + "s remaining=" + UnitFormatter.Number(g.Remaining, 1) + " pitch=" + UnitFormatter.Number(g.Pitch, 1) + " heading=" + UnitFormatter.Number(g.Heading, 1) + " burn=" + UnitFormatter.Number(g.BurnTime, 0) + "s");
            var p = nav.Prediction;
            if (p != null) sb.Append("\nprediction:patches=" + p.Patches.Count + " encounter=" + p.Encounter + (p.Encounter ? " in " + UnitFormatter.Number((p.EncounterTime - nav.Now) / 86400.0, 2) + "d" : " closest=" + UnitFormatter.Number(p.ClosestDistance / 1e9, 3) + " Gm") + " impact=" + p.Impact);
            sb.Append("\nstatus: " + nav.Status);
            return sb.ToString();
        }

        static string Status(SpaceSimBootstrap boot)
        {
            var t = boot.FlightState.Telemetry;
            var r = boot.Body.Rocket;
            var sb = new StringBuilder();
            sb.AppendLine("body=" + boot.System.ActiveBody.Id + " phase=" + boot.FlightState.Phase + " t=" + UnitFormatter.Number(t.Time, 1) + "s warp×" + boot.Simulation.TimeWarp);
            sb.AppendLine("alt_surface=" + UnitFormatter.Number(t.AltitudeAboveSurface, 1) + "m alt_R=" + UnitFormatter.Number(t.AltitudeAboveReference, 1) +
                          "m v_inertial=" + UnitFormatter.Number(t.InertialSpeed, 1) + " v_surface=" + UnitFormatter.Number(t.SurfaceSpeed, 1) +
                          " v_r=" + UnitFormatter.Number(t.RadialVelocity, 1) + " eps=" + UnitFormatter.Number(t.SpecificEnergy, 0));
            sb.AppendLine("rho=" + UnitFormatter.Scientific(t.Density) + " q=" + UnitFormatter.Number(t.DynamicPressure, 0) + "Pa thrust_a=" +
                          UnitFormatter.Number(t.ThrustAcceleration, 2) + " periapsis_alt=" + UnitFormatter.Number(t.Orbit.PeriapsisRadius - boot.Planet.Model.Radius, 0));
            if (r != null)
                sb.AppendLine("rocket stage=" + r.StageIndex + "/" + r.StageCount + " engine=" + r.IsBurning + " fuel=" + UnitFormatter.Number(r.CurrentPropellant, 0) +
                              " mass=" + UnitFormatter.Number(r.Mass, 0) + " pitch=" + UnitFormatter.Number(r.PitchDegrees, 1) + " heading=" + UnitFormatter.Number(r.HeadingDegrees, 1) +
                              " dv=" + UnitFormatter.Number(r.DeltaVRemaining(), 0));
            var sim = boot.Body.Simulation;
            if (sim.Navigation != null)
                sb.AppendLine("frame=" + sim.Navigation.NameOf(sim.FrameBody) + " primary=" + sim.Navigation.NameOf(sim.Primary) + " on_rails=" + boot.Simulation.OnRails +
                              " rails_blocked=" + boot.Simulation.RailsBlocked + " effective_warp=" + UnitFormatter.Number(boot.Simulation.EffectiveWarp, 0) +
                              " rails_reason=" + boot.Body.RailsBlockedReason() + " dist_frame=" + UnitFormatter.Number(sim.State.Position.Magnitude / 1000.0, 0) + "km");
            sb.Append("space_unlocked=" + boot.System.SpaceUnlocked + " chunks=" + boot.PlanetVisual.Procedural?.DisplayedChunkCount +
                      " deepest_lod=" + boot.PlanetVisual.Procedural?.DeepestDisplayedLevel + " fps=" + UnitFormatter.Number(1.0 / Math.Max(1e-4, Time.unscaledDeltaTime), 0));
            return sb.ToString();
        }
    }
}
