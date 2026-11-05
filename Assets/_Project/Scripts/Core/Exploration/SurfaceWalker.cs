using System;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;

namespace SpaceSim.Exploration
{
    /// <summary>
    /// An astronaut on foot, simulated in the planet-fixed frame (SI). Gravity is the planet's own μ/r²; walking speed and
    /// grip scale with gravity (Froude-number walking: v = √(Fr·g·L), traction μ·g), a jump is a fixed push-off speed, so
    /// the same legs give a short hop on a heavy world and a long flight on a small moon. The sprint and the effort it
    /// costs come from <see cref="Locomotion"/>: a fast, tiring run on a heavy world, a slow but endless lope on a small one,
    /// none at all where the legs cannot lift the suited body. Rotation (Coriolis/centrifugal) is neglected at walking
    /// speeds. A safety tether keeps the astronaut within <see cref="TetherLength"/> of the lander.
    /// </summary>
    public sealed class SurfaceWalker
    {
        /// <summary>Leg length L [m] in the Froude number Fr = v²/(g·L).</summary>
        public const double LegLength = Locomotion.LegLength;
        public const double WalkFroude = Locomotion.WalkFroude;
        /// <summary>Push-off speed of a jump in a suit [m/s].</summary>
        public const double JumpSpeed = 1.8;
        /// <summary>Boot-ground friction coefficient: horizontal acceleration ≤ μ·g.</summary>
        public const double Friction = Locomotion.Friction;
        public const double EyeHeight = 1.6;
        public const double TetherLength = 300.0;

        readonly ISurfaceHeightProvider surface;
        readonly Func<Vector3d, bool> isWater;

        public SurfaceWalker(double mu, double radius, ISurfaceHeightProvider surfaceProvider, Vector3d anchorFixedDirection, Func<Vector3d, bool> water = null)
        {
            Mu = mu;
            Radius = radius;
            surface = surfaceProvider ?? ReferenceSphereSurface.Instance;
            isWater = water;
            Anchor = PointOnGround(anchorFixedDirection.Normalized);
        }

        public double Mu { get; }
        public double Radius { get; }
        /// <summary>The lander (tether anchor) in planet-fixed coordinates [m].</summary>
        public Vector3d Anchor { get; }
        /// <summary>Feet position, planet-fixed [m].</summary>
        public Vector3d Position { get; private set; }
        /// <summary>Velocity, planet-fixed [m/s].</summary>
        public Vector3d Velocity { get; private set; }
        public bool OnGround { get; private set; }
        public double DistanceWalked { get; private set; }
        /// <summary>Highest point above the ground in the current jump [m].</summary>
        public double JumpApex { get; private set; }
        public string Blocked { get; private set; } = "";

        /// <summary>The anaerobic reserve W′ that a sprint spends.</summary>
        public WorkCapacity Stamina { get; } = new WorkCapacity();
        /// <summary>Sprint requested and allowed during the last step.</summary>
        public bool Sprinting { get; private set; }
        /// <summary>Why the requested sprint was refused ("" if it was not).</summary>
        public string SprintBlocked { get; private set; } = "";
        public Gait Gait { get; private set; } = Gait.Standing;
        /// <summary>Metabolic power during the last step [W].</summary>
        public double MetabolicPower { get; private set; } = Locomotion.RestPower;
        /// <summary>Horizontal acceleration during the last step, planet-fixed [m/s²] (≤ μ·g).</summary>
        public Vector3d Acceleration { get; private set; }
        /// <summary>Gait cycle (one left + one right step) in turns, for animation.</summary>
        public double StridePhase { get; private set; }
        /// <summary>Vertical speed at the last touchdown after a jump [m/s] and how many touchdowns there were.</summary>
        public double LastTouchdownSpeed { get; private set; }
        public int Touchdowns { get; private set; }

        public Vector3d Up => Position.Normalized;
        public double Gravity => PlanetaryGravity.AccelerationMagnitude(Mu, Position.Magnitude);
        public double WalkSpeed => Locomotion.WalkSpeed(Gravity);
        /// <summary>Top running speed here (= walking speed where running is impossible).</summary>
        public double SprintSpeed => Locomotion.SprintSpeed(Gravity);
        public bool CanSprint => Locomotion.CanRun(Gravity);
        /// <summary>Ideal jump height v²/(2g) for <see cref="JumpSpeed"/>.</summary>
        public double JumpHeight => JumpSpeed * JumpSpeed / (2.0 * Gravity);
        public Vector3d EyePosition => Position + Up * EyeHeight;
        public double DistanceToAnchor => (Position - Anchor).Magnitude;
        public double HorizontalSpeed => Vector3d.ProjectOnPlane(Velocity, Up).Magnitude;

        public Vector3d PointOnGround(Vector3d unitDirection) => unitDirection * (Radius + surface.SurfaceHeight(unitDirection));

        /// <summary>Places the astronaut on the ground at a planet-fixed point (e.g. beside the lander).</summary>
        public void PlaceAt(Vector3d fixedPoint)
        {
            Position = PointOnGround(fixedPoint.Normalized);
            Velocity = Vector3d.Zero;
            Acceleration = Vector3d.Zero;
            OnGround = true;
            Gait = Gait.Standing;
        }

        /// <summary>
        /// Advances by <paramref name="dt"/>. <paramref name="forward"/>/<paramref name="right"/> ∈ [−1, 1] relative to
        /// <paramref name="headingDegrees"/> (0 = north, 90 = east); <paramref name="sprint"/> asks for the top running speed.
        /// </summary>
        public void Step(double dt, double forward, double right, double headingDegrees, bool sprint, bool jump)
        {
            if (dt <= 0.0) return;
            Blocked = "";
            Vector3d up = Up;
            PlanetRotation.LocalBasis(up, out Vector3d east, out Vector3d north);
            double h = headingDegrees * MathD.DegToRad;
            Vector3d fwd = north * Math.Cos(h) + east * Math.Sin(h);
            Vector3d rgt = east * Math.Cos(h) - north * Math.Sin(h); // heading + 90°
            double g = Gravity;

            if (OnGround)
            {
                Vector3d wish = fwd * MathD.Clamp(forward, -1.0, 1.0) + rgt * MathD.Clamp(right, -1.0, 1.0);
                if (wish.SqrMagnitude > 1.0) wish = wish.Normalized;
                bool wantsSprint = sprint && wish.SqrMagnitude > 0.01;
                SprintBlocked = !wantsSprint ? ""
                    : !Locomotion.CanRun(g) ? "бег невозможен: ноги не поднимут " + Math.Round(Locomotion.EffectiveLoad(g)) + " кг (вес с костюмом) в полёт между шагами"
                    : Stamina.Exhausted ? "нет сил — отдышитесь (запас ≥ " + Math.Round(Locomotion.RecoveredFraction * 100.0) + " %)"
                    : "";
                Sprinting = wantsSprint && SprintBlocked.Length == 0;
                Vector3d desired = wish * (Sprinting ? SprintSpeed : WalkSpeed);
                Vector3d horizontal = Vector3d.ProjectOnPlane(Velocity, up);
                Vector3d change = desired - horizontal;
                double maxChange = Locomotion.MaxAcceleration(g) * dt;
                if (change.Magnitude > maxChange) change = change.Normalized * maxChange;
                Acceleration = change / dt;
                Velocity = horizontal + change;
                if (jump)
                {
                    Velocity += up * JumpSpeed;
                    OnGround = false;
                    JumpApex = 0.0;
                }
            }
            else
            {
                Acceleration = Vector3d.Zero;
                Sprinting = false;
            }
            if (!OnGround) Velocity += up * (-g * dt);

            Vector3d next = Position + Velocity * dt;
            Vector3d dir = next.Normalized;
            if (isWater != null && isWater(dir))
            {
                // Deep violet water: no walking on it — stop at the shore.
                Velocity = up * Vector3d.Dot(Velocity, up);
                next = Position + Velocity * dt;
                dir = next.Normalized;
                Blocked = "впереди вода";
            }
            Vector3d fromAnchor = next - Anchor;
            if (fromAnchor.Magnitude > TetherLength)
            {
                next = Anchor + fromAnchor.Normalized * TetherLength;
                Vector3d along = fromAnchor.Normalized;
                Velocity -= along * Math.Max(0.0, Vector3d.Dot(Velocity, along));
                dir = next.Normalized;
                Blocked = "страховочный трос натянут (" + TetherLength + " м)";
            }
            double ground = Radius + surface.SurfaceHeight(dir);
            double height = next.Magnitude - ground;
            if (OnGround || height <= 0.0)
            {
                if (!OnGround)
                {
                    // Touchdown: the legs absorb the vertical speed.
                    LastTouchdownSpeed = Math.Max(0.0, -Vector3d.Dot(Velocity, dir));
                    Touchdowns++;
                    Velocity = Vector3d.ProjectOnPlane(Velocity, dir);
                }
                next = dir * ground;
                OnGround = true;
            }
            else JumpApex = Math.Max(JumpApex, height);
            DistanceWalked += Vector3d.ProjectOnPlane(next - Position, up).Magnitude;
            Position = next;

            // Effort and gait. In a ballistic jump the muscles rest; on the ground the cost is per metre of weight carried.
            double v = HorizontalSpeed;
            MetabolicPower = OnGround ? Locomotion.MetabolicPower(v, g) : Locomotion.RestPower;
            Stamina.Step(MetabolicPower, dt);
            Gait = !OnGround ? Gait.Airborne
                : v < 0.05 ? Gait.Standing
                : Locomotion.FroudeNumber(v, g) >= Locomotion.TransitionFroude ? Gait.Running
                : Gait.Walking;
            if (OnGround && v >= 0.05)
            {
                StridePhase += 0.5 * Locomotion.StepFrequency(v, g) * dt;
                StridePhase -= Math.Floor(StridePhase);
            }
        }
    }
}
