using System;
using SpaceSim.Mathematics;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Floating origin. Unity floats have ~7 significant digits: at 6.4·10⁶ m from the origin the spacing
    /// between representable positions is ~0.5 m, which would make everything near the camera jitter.
    /// Therefore all simulation positions stay in double precision, and Unity's (0,0,0) is placed at
    /// <see cref="Origin"/>, a double-precision point kept near the camera. When the viewer drifts more than
    /// <see cref="recenterDistance"/> away, the origin jumps to the viewer.
    ///
    /// All SpaceSim objects recompute their Unity transforms from double positions every frame, so a recenter
    /// needs no transform shifting; <see cref="OriginShifted"/> is raised for any other Unity-side systems
    /// (particles, trails) that keep world-space state.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class FloatingOrigin : MonoBehaviour
    {
        [Tooltip("Distance [m] from the Unity origin at which the origin is moved to the viewer.")]
        [SerializeField] double recenterDistance = 2000.0;

        /// <summary>Simulation-frame position that maps to Unity (0,0,0) [m].</summary>
        public Vector3d Origin { get; private set; }

        /// <summary>Simulation-frame position of the active camera [m], set by the camera each frame.</summary>
        public Vector3d ViewerPosition { get; private set; }

        /// <summary>Raised after a recenter with the Unity-space shift that was applied (old − new).</summary>
        public event Action<Vector3> OriginShifted;

        public int RecenterCount { get; private set; }

        public double RecenterDistance
        {
            get => recenterDistance;
            set => recenterDistance = Math.Max(1.0, value);
        }

        public Vector3 ToUnityPosition(Vector3d simulationPosition) => CoordinateSystem.ToUnityVector(simulationPosition - Origin);

        public Vector3d ToSimulationPosition(Vector3 unityPosition) => Origin + CoordinateSystem.FromUnityVector(unityPosition);

        /// <summary>Called by the camera before anything is positioned for rendering this frame.</summary>
        public void SetViewer(Vector3d viewerSimulationPosition)
        {
            ViewerPosition = viewerSimulationPosition;
            if ((viewerSimulationPosition - Origin).Magnitude > recenterDistance) Recenter(viewerSimulationPosition);
        }

        public void Recenter(Vector3d newOrigin)
        {
            Vector3 shift = CoordinateSystem.ToUnityVector(Origin - newOrigin);
            Origin = newOrigin;
            RecenterCount++;
            OriginShifted?.Invoke(shift);
        }
    }
}
