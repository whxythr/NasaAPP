using NUnit.Framework;
using SpaceSim.Mathematics;
using SpaceSim.Persistence;
using SpaceSim.Procedural;
using SpaceSim.Runtime;
using UnityEngine;

namespace SpaceSim.Tests
{
    /// <summary>Tests of the Unity layer: coordinate conversion, mesh winding, floating origin, JSON saves.</summary>
    public class UnityIntegrationTests
    {
        [Test]
        public void RotationConversion_IsConsistentWithVectorConversion()
        {
            var rng = new DeterministicRandom(5);
            for (int i = 0; i < 100; i++)
            {
                var q = QuaternionD.AngleAxis(rng.Range(-3.0, 3.0), rng.UnitVector());
                var v = rng.UnitVector() * rng.Range(1.0, 100.0);
                Vector3 expected = CoordinateSystem.ToUnityVector(q.Rotate(v));
                Vector3 actual = CoordinateSystem.ToUnityRotation(q) * CoordinateSystem.ToUnityVector(v);
                Assert.Less((expected - actual).magnitude, 1e-3f * (float)v.Magnitude);
            }
        }

        [Test]
        public void AxisMapping_KeepsEastRightOfNorth()
        {
            // At lat 0, lon 0: east = +Y, north = +Z, up = +X (physics). In Unity: right × up = forward.
            Vector3 east = CoordinateSystem.ToUnityVector(Vector3d.UnitY);
            Vector3 north = CoordinateSystem.ToUnityVector(Vector3d.UnitZ);
            Vector3 up = CoordinateSystem.ToUnityVector(Vector3d.UnitX);
            Assert.Less((Vector3.Cross(east, up) - north).magnitude, 1e-6f);
        }

        [Test]
        public void ConvertedChunkMesh_IsFrontFacingFromOutside()
        {
            var sampler = PlanetSurfaceSampler.Create(new PlanetGenerationSettings { seed = 3 }, 6371000.0);
            ChunkMeshData data = ChunkMeshBuilder.Build(sampler, new ChunkId(2, 4, 5, 9), 16);
            var mesh = new Mesh();
            CoordinateSystem.FillMesh(data, mesh);
            Vector3[] v = mesh.vertices;
            int[] t = mesh.triangles;
            Vector3 centerDir = CoordinateSystem.ToUnityVector(data.Center.Normalized);
            int grid = 16 * 16 * 2, outward = 0;
            for (int i = 0; i < grid; i++)
            {
                Vector3 a = v[t[i * 3]], b = v[t[i * 3 + 1]], c = v[t[i * 3 + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), centerDir) > 0f) outward++; // Unity: clockwise front face
            }
            Object.DestroyImmediate(mesh);
            Assert.Greater(outward, grid * 0.98f);
        }

        [Test]
        public void FloatingOrigin_RecentersAndRoundTrips()
        {
            var go = new GameObject("origin");
            var origin = go.AddComponent<FloatingOrigin>();
            origin.RecenterDistance = 1000.0;
            var far = new Vector3d(6.4e6, 1.0e5, -3.0e4);
            origin.SetViewer(far);
            Assert.AreEqual(1, origin.RecenterCount);
            Vector3 u = origin.ToUnityPosition(far + new Vector3d(1.25, -0.5, 0.75));
            Assert.Less(u.magnitude, 2f, "positions near the viewer are small in Unity space");
            Assert.AreEqual(1.25f, u.x, 1e-6f);
            Vector3d back = origin.ToSimulationPosition(u);
            Assert.Less((back - (far + new Vector3d(1.25, -0.5, 0.75))).Magnitude, 1e-6);
            origin.SetViewer(far + new Vector3d(10, 0, 0));
            Assert.AreEqual(1, origin.RecenterCount, "no recenter inside the threshold");
            Object.DestroyImmediate(go);
        }

        [Test]
        public void PlanetSave_RoundTripsThroughJson()
        {
            var def = PlanetDefinition.CreateEarthPrototype();
            def.generation.seed = 98765;
            PlanetSaveData data = PlanetSaveValidator.Capture(def.planetId, def.displayName, def.BuildGenerationSettings(),
                def.physical, def.optics, 0, 0, "test");
            string json = PlanetSaveService.ToJson(data);
            PlanetSaveData restored = PlanetSaveService.FromJson(json);
            Assert.AreEqual(data.surfaceFingerprint, restored.surfaceFingerprint);
            Assert.AreEqual(98765, restored.generation.seed);
            Assert.AreEqual(def.physical.mass, restored.physical.mass);
            Assert.AreEqual(def.launchSite.latitudeDegrees, restored.generation.flattenZones[0].latitudeDegrees);
            Assert.IsTrue(PlanetSaveValidator.Validate(restored).IsValid);
            Object.DestroyImmediate(def);
        }

        [Test]
        public void LaunchSite_IsPartOfDeterministicSurface()
        {
            var def = PlanetDefinition.CreateEarthPrototype();
            var zones = def.BuildGenerationSettings().flattenZones;
            Assert.AreEqual(def.launchSite.latitudeDegrees, zones[0].latitudeDegrees);
            Assert.AreEqual(def.launchSite.flattenRadius, zones[0].radius);
            Object.DestroyImmediate(def);
        }
    }
}
