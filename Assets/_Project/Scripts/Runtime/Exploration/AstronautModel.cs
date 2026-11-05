using System;
using System.Collections.Generic;
using SpaceSim.Exploration;
using SpaceSim.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// The astronaut's suited body for the EVA: procedural meshes (no imported assets), lit and shadow-casting, animated
    /// from the <see cref="SurfaceWalker"/> state — stride phase and cadence from <see cref="Locomotion.StepFrequency"/>,
    /// the lean tan θ = a/g while speeding up or braking, a flight phase while running, tucked legs in a jump and a knee
    /// bend that soaks up the landing. Purely visual: it never feeds back into the physics.
    ///
    /// Local frame of <see cref="Root"/>: origin at the feet on the ground, +Y up, +Z the way the astronaut faces. In first
    /// person the helmet is drawn into the shadow map only (the camera sits inside it), so the shadow on the ground keeps
    /// the head while the view stays clear; the chest, arms and legs are visible when looking down.
    /// </summary>
    public sealed class AstronautModel
    {
        // Proportions [m]. Rest: sole → ankle 0.0875, shin 0.42, thigh 0.44 → hips 0.9475; eye 0.66 above the hips.
        const float Thigh = 0.44f, Shin = 0.42f, AnkleHeight = 0.0875f;
        const float HipWidth = 0.11f, ShoulderWidth = 0.27f;
        const float TorsoBase = 0.12f;
        static readonly Vector3 EyeInTorso = new Vector3(0f, 0.54f, 0.24f);

        readonly GameObject root;
        readonly Transform hips, torso;
        readonly Transform[] hip = new Transform[2], knee = new Transform[2], ankle = new Transform[2];
        readonly Transform[] shoulder = new Transform[2], elbow = new Transform[2];
        readonly List<Renderer> headRenderers = new List<Renderer>();
        readonly List<Renderer> bodyRenderers = new List<Renderer>();
        readonly List<Material> materials = new List<Material>();

        // Smoothed animation state.
        float swing, runBlend, airBlend, lean, sideLean, dirForward = 1f, dirRight;
        float crouch, crouchSpeed;
        int touchdowns = -1;
        bool firstPerson = true;

        public Transform Root => root.transform;
        /// <summary>The eye in the root frame (follows the hips, the lean and the bob).</summary>
        public Vector3 EyeLocal { get; private set; } = new Vector3(0f, Thigh + Shin + AnkleHeight + TorsoBase + EyeInTorso.y, EyeInTorso.z);
        /// <summary>Forward lean of the torso [degrees] (tan θ = a/g plus the gait's own lean).</summary>
        public float LeanDegrees => lean;

        public AstronautModel(Transform parent, Shader lit)
        {
            root = new GameObject("Astronaut");
            root.transform.SetParent(parent, false);

            Material suit = Mat(lit, new Color(0.86f, 0.86f, 0.83f), 0.18f);
            Material joint = Mat(lit, new Color(0.50f, 0.52f, 0.55f), 0.35f);
            Material dark = Mat(lit, new Color(0.17f, 0.17f, 0.18f), 0.25f);
            Material glove = Mat(lit, new Color(0.36f, 0.37f, 0.40f), 0.3f);
            Material sole = Mat(lit, new Color(0.07f, 0.07f, 0.07f), 0.1f);
            Material stripe = Mat(lit, new Color(0.78f, 0.16f, 0.10f), 0.25f);
            Material panel = Mat(lit, new Color(0.20f, 0.26f, 0.36f), 0.45f);
            Material lamp = Mat(lit, new Color(0.25f, 0.85f, 0.35f), 0.6f);
            Material visor = Mat(lit, new Color(0.80f, 0.58f, 0.20f), 0.92f, 0.85f);

            hips = Pivot("Hips", root.transform, new Vector3(0f, Thigh + Shin + AnkleHeight, 0f));
            Part(PrimitiveType.Sphere, "Pelvis", hips, suit, new Vector3(0f, 0.03f, 0f), new Vector3(0.36f, 0.24f, 0.27f));
            Part(PrimitiveType.Cylinder, "WaistRing", hips, joint, new Vector3(0f, TorsoBase, 0f), new Vector3(0.40f, 0.025f, 0.31f));

            torso = Pivot("Torso", hips, new Vector3(0f, TorsoBase, 0f));
            Part(PrimitiveType.Capsule, "Chest", torso, suit, new Vector3(0f, 0.24f, 0f), new Vector3(0.44f, 0.24f, 0.30f));
            Part(PrimitiveType.Cube, "ChestPlate", torso, suit, new Vector3(0f, 0.31f, 0.13f), new Vector3(0.36f, 0.20f, 0.05f));
            Part(PrimitiveType.Cube, "ControlModule", torso, panel, new Vector3(0f, 0.17f, 0.185f), new Vector3(0.20f, 0.08f, 0.07f));
            Part(PrimitiveType.Cylinder, "Lamp", torso, lamp, new Vector3(-0.05f, 0.212f, 0.2f), new Vector3(0.016f, 0.004f, 0.016f));
            Part(PrimitiveType.Cylinder, "Lamp2", torso, stripe, new Vector3(0.05f, 0.212f, 0.2f), new Vector3(0.016f, 0.004f, 0.016f));
            Part(PrimitiveType.Cube, "Dial", torso, joint, new Vector3(0f, 0.212f, 0.19f), new Vector3(0.05f, 0.006f, 0.03f));
            Part(PrimitiveType.Cube, "Backpack", torso, suit, new Vector3(0f, 0.30f, -0.25f), new Vector3(0.44f, 0.58f, 0.22f));
            Part(PrimitiveType.Cube, "BackpackTop", torso, joint, new Vector3(0f, 0.61f, -0.25f), new Vector3(0.40f, 0.05f, 0.20f));

            // Head: drawn only into the shadow map in first person (the eye is inside the helmet).
            headRenderers.Add(Part(PrimitiveType.Cylinder, "NeckRing", torso, joint, new Vector3(0f, 0.50f, 0.06f), new Vector3(0.30f, 0.02f, 0.28f)));
            headRenderers.Add(Part(PrimitiveType.Sphere, "Helmet", torso, suit, new Vector3(0f, 0.60f, 0.11f), new Vector3(0.34f, 0.34f, 0.34f)));
            headRenderers.Add(Part(PrimitiveType.Sphere, "Visor", torso, visor, new Vector3(0f, 0.59f, 0.175f), new Vector3(0.29f, 0.25f, 0.22f)));
            foreach (Renderer r in headRenderers) bodyRenderers.Remove(r);

            for (int side = 0; side < 2; side++)
            {
                float s = side == 0 ? -1f : 1f; // 0 = left (−X), 1 = right (+X)
                string n = side == 0 ? "L" : "R";

                shoulder[side] = Pivot("Shoulder" + n, torso, new Vector3(s * ShoulderWidth, 0.42f, 0f));
                Part(PrimitiveType.Sphere, "ShoulderJoint" + n, shoulder[side], joint, Vector3.zero, Vector3.one * 0.15f);
                Part(PrimitiveType.Capsule, "UpperArm" + n, shoulder[side], suit, new Vector3(0f, -0.14f, 0f), new Vector3(0.13f, 0.15f, 0.13f));
                if (side == 0) Part(PrimitiveType.Cylinder, "Stripe" + n, shoulder[side], stripe, new Vector3(0f, -0.09f, 0f), new Vector3(0.135f, 0.022f, 0.135f));
                elbow[side] = Pivot("Elbow" + n, shoulder[side], new Vector3(0f, -0.29f, 0f));
                Part(PrimitiveType.Sphere, "ElbowJoint" + n, elbow[side], joint, Vector3.zero, Vector3.one * 0.12f);
                Part(PrimitiveType.Capsule, "Forearm" + n, elbow[side], suit, new Vector3(0f, -0.13f, 0f), new Vector3(0.12f, 0.13f, 0.12f));
                Part(PrimitiveType.Cylinder, "Cuff" + n, elbow[side], joint, new Vector3(0f, -0.255f, 0f), new Vector3(0.115f, 0.018f, 0.115f));
                Part(PrimitiveType.Sphere, "Glove" + n, elbow[side], glove, new Vector3(0f, -0.32f, 0.01f), new Vector3(0.10f, 0.14f, 0.085f));
                Part(PrimitiveType.Capsule, "Thumb" + n, elbow[side], glove, new Vector3(-s * 0.035f, -0.30f, 0.045f), new Vector3(0.035f, 0.035f, 0.035f));

                hip[side] = Pivot("Hip" + n, hips, new Vector3(s * HipWidth, 0f, 0f));
                Part(PrimitiveType.Capsule, "Thigh" + n, hip[side], suit, new Vector3(0f, -0.21f, 0f), new Vector3(0.17f, 0.23f, 0.17f));
                knee[side] = Pivot("Knee" + n, hip[side], new Vector3(0f, -Thigh, 0f));
                Part(PrimitiveType.Sphere, "KneeJoint" + n, knee[side], joint, Vector3.zero, Vector3.one * 0.15f);
                Part(PrimitiveType.Capsule, "Shin" + n, knee[side], suit, new Vector3(0f, -0.20f, 0f), new Vector3(0.155f, 0.22f, 0.155f));
                ankle[side] = Pivot("Ankle" + n, knee[side], new Vector3(0f, -Shin, 0f));
                Part(PrimitiveType.Cube, "Boot" + n, ankle[side], dark, new Vector3(0f, -0.035f, 0.05f), new Vector3(0.15f, 0.09f, 0.29f));
                Part(PrimitiveType.Cube, "Sole" + n, ankle[side], sole, new Vector3(0f, -0.075f, 0.05f), new Vector3(0.155f, 0.025f, 0.30f));
            }
            SetFirstPerson(true);
        }

        /// <summary>First person: the helmet only casts its shadow. Third person: everything is drawn.</summary>
        public void SetFirstPerson(bool value)
        {
            firstPerson = value;
            foreach (Renderer r in headRenderers) r.shadowCastingMode = value ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }

        public bool FirstPerson => firstPerson;
        public IReadOnlyList<Renderer> HeadRenderers => headRenderers;
        public IReadOnlyList<Renderer> BodyRenderers => bodyRenderers;

        public void Destroy()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            foreach (Material m in materials)
                if (m != null) UnityEngine.Object.Destroy(m);
            materials.Clear();
        }

        /// <summary>
        /// Poses the body for this frame. <paramref name="forward"/>/<paramref name="right"/> are the body's horizontal axes
        /// in the planet-fixed frame (the way the camera looks).
        /// </summary>
        public void Animate(SurfaceWalker w, Vector3d forward, Vector3d right, float dt)
        {
            dt = Mathf.Max(dt, 1e-4f);
            double g = w.Gravity;
            Vector3d up = w.Up;
            Vector3d horizontal = Vector3d.ProjectOnPlane(w.Velocity, up);
            float v = (float)horizontal.Magnitude;
            float walk = (float)w.WalkSpeed, sprint = Mathf.Max((float)w.SprintSpeed, walk + 0.01f), transition = (float)Locomotion.TransitionSpeed(g);

            bool running = w.Gait == Gait.Running;
            float targetSwing = w.Gait == Gait.Standing || w.Gait == Gait.Airborne ? 0f
                : running ? Mathf.Lerp(0.55f, 1f, Mathf.Clamp01((v - transition) / Mathf.Max(0.01f, sprint - transition)))
                : 0.5f * Mathf.Clamp01(v / Mathf.Max(0.05f, walk));
            swing = Smooth(swing, targetSwing, 8f, dt);
            runBlend = Smooth(runBlend, running ? 1f : 0f, 5f, dt);
            airBlend = Smooth(airBlend, w.OnGround ? 0f : 1f, 10f, dt);
            if (v > 0.05f)
            {
                dirForward = Smooth(dirForward, (float)(Vector3d.Dot(horizontal, forward) / v), 10f, dt);
                dirRight = Smooth(dirRight, (float)(Vector3d.Dot(horizontal, right) / v), 10f, dt);
            }

            // Lean: tan θ = a/g into an acceleration (forward when speeding up, back when braking), plus the gait's own lean.
            float aForward = (float)Vector3d.Dot(w.Acceleration, forward), aRight = (float)Vector3d.Dot(w.Acceleration, right);
            float targetLean = (float)(Locomotion.LeanAngle(aForward, g) * MathD.RadToDeg) + Mathf.Lerp(2f, 9f, runBlend) * swing;
            lean = Smooth(lean, Mathf.Clamp(targetLean, -20f, 32f), 6f, dt);
            sideLean = Smooth(sideLean, Mathf.Clamp(-(float)(Locomotion.LeanAngle(aRight, g) * MathD.RadToDeg) * 0.5f, -12f, 12f), 6f, dt);

            // Landing: the knees take the vertical speed and spring back (damped, ω = 11 rad/s).
            if (touchdowns < 0) touchdowns = w.Touchdowns;
            if (w.Touchdowns != touchdowns)
            {
                touchdowns = w.Touchdowns;
                crouchSpeed += Mathf.Min(1.5f, (float)w.LastTouchdownSpeed * 0.35f);
            }
            const float omega = 11f;
            crouchSpeed += (-omega * omega * crouch - 2f * 0.85f * omega * crouchSpeed) * dt;
            crouch = Mathf.Clamp(crouch + crouchSpeed * dt, 0f, 0.22f);
            float bend = Mathf.Acos(Mathf.Clamp01(1f - crouch / (Thigh + Shin))) * Mathf.Rad2Deg; // hips drop by `crouch`

            // Legs: hips swing (a little more forward than back), knees fold in the swing phase, feet stay level.
            // Phase 0: left leg mid-swing, right leg mid-stance; phase ¼: legs split (double support / flight).
            float phase = (float)w.StridePhase;
            float hipAmp = Mathf.Lerp(40f, 38f, runBlend) * swing;
            float hipCentre = Mathf.Lerp(3f, 8f, runBlend) * Mathf.Clamp01(swing * 2f);
            float kneeAmp = Mathf.Lerp(110f, 100f, runBlend) * swing;
            float stanceFlex = Mathf.Lerp(5f, 28f, runBlend) * Mathf.Clamp01(swing * 2f);
            float lowest = 0f;
            for (int side = 0; side < 2; side++)
            {
                float theta = 2f * Mathf.PI * (phase + side * 0.5f);
                float aSwing = (hipCentre + hipAmp * Mathf.Sin(theta)) * (1f - airBlend);
                float aFixed = 22f * airBlend + bend; // tucked in the air, folded in a landing — always forward
                float fwdA = aSwing * dirForward + aFixed, sideA = aSwing * dirRight * 0.6f;
                float k = Mathf.Lerp(stanceFlex + kneeAmp * Mathf.Max(0f, Mathf.Cos(theta)), 40f, airBlend) + 2f * bend;
                hip[side].localRotation = Quaternion.Euler(-fwdA, 0f, sideA);
                knee[side].localRotation = Quaternion.Euler(k, 0f, 0f);
                ankle[side].localRotation = Quaternion.Euler(Mathf.Clamp(fwdA - k, -45f, 45f) * (1f - 0.5f * airBlend), 0f, 0f);
                float reach = (Thigh * Mathf.Cos(fwdA * Mathf.Deg2Rad) + Shin * Mathf.Cos((k - fwdA) * Mathf.Deg2Rad)) * Mathf.Cos(sideA * Mathf.Deg2Rad) + AnkleHeight;
                lowest = Mathf.Max(lowest, reach);
            }

            // Walking: the hips ride on the longer (stance) leg — that alone gives the pendulum bob, highest at mid-stance.
            // Running: lowest at mid-stance (knee compression), highest in the flight between steps — long and floaty on a
            // small world, where the step's ballistic hop h = g·t²/8 lasts longer.
            const float rest = Thigh + Shin + AnkleHeight;
            float flight = Mathf.Min(0.12f, (float)Locomotion.StrideFlightHeight(v, g)) * 0.5f + 0.015f;
            float hop = swing * flight * (1f - Mathf.Cos(4f * Mathf.PI * phase));
            float groundHips = Mathf.Lerp(lowest, rest - 0.045f * Mathf.Clamp01(swing * 2f) + hop - crouch, runBlend);
            float hipsY = Mathf.Lerp(groundHips, rest - 0.05f, airBlend);
            hips.localPosition = new Vector3(0f, hipsY, 0f);
            torso.localRotation = Quaternion.Euler(lean, 0f, sideLean);

            // Arms swing against the legs; elbows fold more in a run; raised a little for balance in the air.
            float armAmp = Mathf.Lerp(28f, 50f, runBlend) * swing;
            float elbowBend = Mathf.Lerp(12f, 80f, runBlend * Mathf.Clamp01(swing * 1.5f));
            for (int side = 0; side < 2; side++)
            {
                float s = side == 0 ? -1f : 1f;
                float theta = 2f * Mathf.PI * (phase + side * 0.5f);
                float b = -armAmp * Mathf.Sin(theta) * dirForward;
                float abduct = 7f + 25f * airBlend + 6f * runBlend * swing;
                shoulder[side].localRotation = Quaternion.Euler(-b - 10f * airBlend, 0f, s * abduct);
                elbow[side].localRotation = Quaternion.Euler(-(elbowBend + 20f * airBlend), 0f, 0f);
            }

            Quaternion torsoRot = Quaternion.Euler(lean, 0f, sideLean);
            EyeLocal = new Vector3(0f, hipsY + TorsoBase, 0f) + torsoRot * EyeInTorso;
        }

        static float Smooth(float current, float target, float rate, float dt) => Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));

        Material Mat(Shader lit, Color c, float smoothness, float metallic = 0f)
        {
            Material m = ProceduralMeshes.LitMaterial(lit, c, smoothness, metallic);
            materials.Add(m);
            return m;
        }

        static Transform Pivot(string name, Transform parent, Vector3 localPosition)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            return t;
        }

        Renderer Part(PrimitiveType type, string name, Transform parent, Material material, Vector3 localPosition, Vector3 scale)
        {
            GameObject go = ProceduralMeshes.Primitive(type, name, parent, material);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
            bodyRenderers.Add(r);
            return r;
        }
    }
}
