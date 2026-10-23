using System;
using SpaceSim.Mathematics;
using UnityEngine;
#if SPACESIM_URP
using UnityEngine.Rendering.Universal;
#endif

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Orbit camera around the flight body (or the planet). Its position is computed in double precision in the
    /// simulation frame and drives the floating origin. Only the camera moves — it never influences physics.
    ///
    /// Frames: LocalHorizon keeps the local vertical "up" (natural near the ground); Inertial keeps orientation
    /// fixed relative to the stars (useful in orbit). Near/far planes adapt to altitude to keep depth precision.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class SpaceCameraController : MonoBehaviour
    {
        public enum ReferenceFrame { LocalHorizon, Inertial }
        public enum FocusTarget { FlightBody, Planet }

        [Header("Control")]
        [Tooltip("Degrees per pixel of mouse movement (hold right mouse button).")]
        [SerializeField] float mouseSensitivity = 0.15f;
        [Tooltip("Rotation smoothing rate [1/s]. Higher = snappier.")]
        [SerializeField] float rotationSmoothing = 14f;
        [Tooltip("Zoom smoothing rate [1/s].")]
        [SerializeField] float zoomSmoothing = 8f;
        [Tooltip("Logarithmic zoom per scroll notch.")]
        [SerializeField] float zoomPerNotch = 0.18f;

        [Header("Limits")]
        [SerializeField] double minDistance = 4.0;
        [SerializeField] double maxDistance = 1.5e8;
        [Tooltip("Minimum camera height above the physical surface [m].")]
        [SerializeField] double minGroundClearance = 1.6;

        [SerializeField] ReferenceFrame frame = ReferenceFrame.LocalHorizon;
        [SerializeField] FocusTarget focus = FocusTarget.FlightBody;

        Camera cam;
        PlanetRuntime planet;
        FlightBody body;
        FloatingOrigin origin;
        SimulationController simulation;

        float yaw = 200f, pitch = 8f, targetYaw = 200f, targetPitch = 8f;
        double distance = 28.0, targetDistance = 28.0;

        public Camera Camera => cam;
        public ReferenceFrame Frame => frame;
        public FocusTarget Focus => focus;
        public Vector3d SimulationPosition { get; private set; }
        public double Distance => distance;

        public void Initialize(Camera camera, PlanetRuntime planetRuntime, FlightBody flightBody, FloatingOrigin floatingOrigin, SimulationController sim)
        {
            cam = camera;
            planet = planetRuntime;
            body = flightBody;
            origin = floatingOrigin;
            simulation = sim;

            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.allowHDR = true;
            cam.depthTextureMode |= DepthTextureMode.Depth; // Built-in pipeline: atmosphere needs scene depth
#if SPACESIM_URP
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.requiresDepthTexture = true;
                data.renderShadows = true;
            }
#endif
        }

        public void ToggleFrame() => frame = frame == ReferenceFrame.LocalHorizon ? ReferenceFrame.Inertial : ReferenceFrame.LocalHorizon;

        public void ToggleFocus()
        {
            focus = focus == FocusTarget.FlightBody ? FocusTarget.Planet : FocusTarget.FlightBody;
            if (focus == FocusTarget.Planet)
            {
                frame = ReferenceFrame.Inertial;
                targetDistance = Math.Max(targetDistance, planet.Model.Radius * 3.0);
            }
            else targetDistance = Math.Min(targetDistance, 200.0);
        }

        void LateUpdate()
        {
            if (planet == null || planet.Model == null || body == null) return;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            HandleInput();

            float kr = 1f - Mathf.Exp(-rotationSmoothing * dt);
            float kz = 1f - Mathf.Exp(-zoomSmoothing * dt);
            yaw = Mathf.LerpAngle(yaw, targetYaw, kr);
            pitch = Mathf.Lerp(pitch, targetPitch, kr);
            distance = Math.Exp(MathD.Lerp(Math.Log(distance), Math.Log(targetDistance), kz));

            var model = planet.Model;
            double renderTime = simulation.RenderTime;
            Vector3d target = focus == FocusTarget.Planet ? model.Position : body.RenderPosition(simulation.InterpolationAlpha, renderTime);

            BuildBasis(target, model, out Vector3d up, out Vector3d north, out Vector3d east);
            double yawR = yaw * MathD.DegToRad, pitchR = pitch * MathD.DegToRad;
            Vector3d horizontal = north * Math.Cos(yawR) + east * Math.Sin(yawR);
            Vector3d offset = (-horizontal * Math.Cos(pitchR) + up * Math.Sin(pitchR)) * distance;
            Vector3d camPos = target + offset;

            // Keep the camera above the ground (purely visual constraint).
            double alt = model.AltitudeAboveSurface(camPos, renderTime);
            if (alt < minGroundClearance)
            {
                Vector3d radial = (camPos - model.Position).Normalized;
                camPos += radial * (minGroundClearance - alt);
                alt = minGroundClearance;
            }

            SimulationPosition = camPos;
            origin.SetViewer(camPos);

            Vector3 forward = CoordinateSystem.ToUnityVector((target - camPos).Normalized);
            Vector3 upHint = CoordinateSystem.ToUnityVector(up);
            transform.SetPositionAndRotation(origin.ToUnityPosition(camPos), Quaternion.LookRotation(forward, upHint));
            UpdateClipPlanes(camPos, alt, (target - camPos).Magnitude);
        }

        void HandleInput()
        {
            if (FlightInput.IsTyping) return;
            if (FlightInput.RightMouseHeld || FlightInput.MiddleMouseHeld)
            {
                Vector2 d = FlightInput.MouseDelta;
                targetYaw += d.x * mouseSensitivity;
                targetPitch = Mathf.Clamp(targetPitch - d.y * mouseSensitivity, -89f, 89f);
            }
            float notches = FlightInput.ScrollNotches;
            if (FlightInput.GetKey(InputKey.Equals)) notches += 2f * Time.unscaledDeltaTime * 10f;
            if (FlightInput.GetKey(InputKey.Minus)) notches -= 2f * Time.unscaledDeltaTime * 10f;
            if (Mathf.Abs(notches) > 0f)
            {
                double min = focus == FocusTarget.Planet ? planet.Model.Radius * 1.05 : minDistance;
                targetDistance = MathD.Clamp(targetDistance * Math.Exp(-notches * zoomPerNotch), min, maxDistance);
            }
            if (FlightInput.GetKeyDown(InputKey.C)) ToggleFrame();
            if (FlightInput.GetKeyDown(InputKey.F)) ToggleFocus();
        }

        void BuildBasis(Vector3d target, SpaceSim.Planets.PlanetModel model, out Vector3d up, out Vector3d north, out Vector3d east)
        {
            Vector3d axis = model.Rotation.SpinAxis;
            if (frame == ReferenceFrame.LocalHorizon && focus == FocusTarget.FlightBody)
            {
                up = (target - model.Position).Normalized;
                Vector3d e = Vector3d.Cross(axis, up);
                if (e.SqrMagnitude < 1e-12) e = Vector3d.Cross(Vector3d.UnitX, up);
                east = e.Normalized;
                north = Vector3d.Cross(up, east);
            }
            else
            {
                // Inertial: fixed relative to the stars; "up" = planet's north pole direction.
                up = axis;
                east = Vector3d.Cross(Vector3d.UnitZ, axis).SqrMagnitude > 1e-12 ? Vector3d.Cross(Vector3d.UnitZ, axis).Normalized : Vector3d.UnitX;
                east = Vector3d.Cross(up, Vector3d.Cross(east, up)).Normalized;
                north = Vector3d.Cross(up, east);
            }
        }

        void UpdateClipPlanes(Vector3d camPos, double altitude, double focusDistance)
        {
            var model = planet.Model;
            double r = (camPos - model.Position).Magnitude;
            double far = r + model.Radius + planet.VisualShellHeight + 1000.0;
            double near = MathD.Clamp(altitude * 0.002, 0.05, 2000.0);
            near = Math.Min(near, Math.Max(0.05, focusDistance * 0.25));
            cam.nearClipPlane = (float)near;
            cam.farClipPlane = (float)Math.Max(far, near * 10.0);
        }
    }
}
