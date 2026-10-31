using System;
using System.Globalization;
using System.IO;
using System.Text;
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
    ///           warp+ · warp- · pause · initial LAT LON ALT VE VN VU · jump BODY_ID · unlock
    /// </summary>
    [InitializeOnLoad]
    static class SpaceSimBridge
    {
        static readonly string Dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/SpaceSimBridge"));
        static readonly string CommandPath = Path.Combine(Dir, "command.txt");
        static readonly string ResultPath = Path.Combine(Dir, "result.txt");
        static double nextPoll;

        static SpaceSimBridge()
        {
            Directory.CreateDirectory(Dir);
            PlayerSettings.runInBackground = true; // keep Play mode running while the Editor window is not focused
            EditorApplication.update += Poll;
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

        static string Execute(string line)
        {
            string[] a = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string cmd = a[0].ToLowerInvariant();
            switch (cmd)
            {
                case "ping": return "pong (playing=" + EditorApplication.isPlaying + ", compiling=" + EditorApplication.isCompiling + ")";
                case "refresh":
                    AssetDatabase.Refresh();
                    return "refreshed";
                case "setup":
                    if (EditorApplication.isPlaying) return "ERROR stop Play mode first";
                    SpaceSimProjectSetup.SetupAll();
                    return "setup done; active scene " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
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
                default: return "ERROR unknown command";
            }
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
            sb.Append("space_unlocked=" + boot.System.SpaceUnlocked + " chunks=" + boot.PlanetVisual.Procedural?.DisplayedChunkCount +
                      " deepest_lod=" + boot.PlanetVisual.Procedural?.DeepestDisplayedLevel + " fps=" + UnitFormatter.Number(1.0 / Math.Max(1e-4, Time.unscaledDeltaTime), 0));
            return sb.ToString();
        }
    }
}
