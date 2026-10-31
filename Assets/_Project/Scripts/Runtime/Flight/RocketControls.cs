using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Manual rocket controls. Everything the rocket does comes from these inputs and physics — there is no
    /// guidance or autopilot. Rates are in real time (not scaled by time warp).
    ///   W / S — pitch down towards the horizon / back up (0° = vertical, 90° = horizontal)
    ///   A / D — heading left / right (90° = east)
    ///   Left Shift / Left Ctrl — throttle up / down;  Z — full throttle;  X — engine cut-off
    ///   Enter — ignite (same as the ЗАПУСК button);  Space — separate the current stage
    /// </summary>
    [DefaultExecutionOrder(-120)]
    public sealed class RocketControls : MonoBehaviour
    {
        [SerializeField] float pitchRate = 12f;     // deg/s
        [SerializeField] float headingRate = 20f;   // deg/s
        [SerializeField] float throttleRate = 0.5f; // per second

        FlightBody body;
        FlightStateController state;

        public void Initialize(FlightBody flightBody, FlightStateController flightState)
        {
            body = flightBody;
            state = flightState;
        }

        void Update()
        {
            var r = body != null ? body.Rocket : null;
            if (r == null || FlightInput.IsTyping) return;
            float dt = Time.unscaledDeltaTime;

            if (FlightInput.GetKey(InputKey.W)) r.PitchDegrees += pitchRate * dt;
            if (FlightInput.GetKey(InputKey.S)) r.PitchDegrees -= pitchRate * dt;
            if (FlightInput.GetKey(InputKey.A)) r.HeadingDegrees -= headingRate * dt;
            if (FlightInput.GetKey(InputKey.D)) r.HeadingDegrees += headingRate * dt;
            if (FlightInput.GetKey(InputKey.LeftShift)) r.Throttle += throttleRate * dt;
            if (FlightInput.GetKey(InputKey.LeftCtrl)) r.Throttle -= throttleRate * dt;
            if (FlightInput.GetKeyDown(InputKey.Z)) r.Throttle = 1.0;
            if (FlightInput.GetKeyDown(InputKey.X))
            {
                r.EngineOn = false;
                state.Notify("Двигатель выключен.");
            }
            if (FlightInput.GetKeyDown(InputKey.Return)) Ignite();
            if (FlightInput.GetKeyDown(InputKey.Space)) Separate();
        }

        public void Ignite()
        {
            if (body.Launch()) state.Notify("Зажигание: " + body.Rocket.CurrentStage.name + ", тяга " + (body.Rocket.Throttle * 100.0).ToString("0") + " %.");
            else state.Notify("Нечем запускаться: ступень пуста или отделены все ступени.");
        }

        public void Separate()
        {
            string name = body.Rocket.HasStages ? body.Rocket.CurrentStage.name : null;
            if (body.SeparateStage()) state.Notify("Отделена " + name + "." + (body.Rocket.HasStages && !body.Rocket.EngineOn ? " Включите следующую ступень (Enter)." : ""));
        }
    }
}
