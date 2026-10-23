using System;
using System.Threading.Tasks;
using NUnit.Framework;
using SpaceSim.Mathematics;
using SpaceSim.Planets;
using SpaceSim.Procedural;

namespace SpaceSim.Tests
{
    public class ProceduralGenerationTests
    {
        const double R = TestPlanets.EarthRadius;

        static PlanetSurfaceSampler Sampler(int seed, Action<PlanetGenerationSettings> tweak = null)
        {
            var s = new PlanetGenerationSettings { seed = seed };
            tweak?.Invoke(s);
            return PlanetSurfaceSampler.Create(s, R);
        }

        static Vector3d[] TestDirections(int count, ulong seed)
        {
            var rng = new DeterministicRandom(seed);
            var dirs = new Vector3d[count];
            for (int i = 0; i < count; i++) dirs[i] = rng.UnitVector();
            return dirs;
        }

        [Test]
        public void SplitMix64_MatchesReferenceSequence()
        {
            // Reference output of SplitMix64 with state 0 (Vigna). Guards against accidental algorithm changes.
            var rng = new DeterministicRandom(0);
            Assert.AreEqual(0xE220A8397B1DCDAFUL, rng.NextULong());
            Assert.AreEqual(0x6E789E6AA1B965F4UL, rng.NextULong());
        }

        [Test]
        public void SameSeed_ReproducesSameSurface()
        {
            Assert.AreEqual(PlanetFingerprint.Compute(Sampler(42)), PlanetFingerprint.Compute(Sampler(42)));
            var a = Sampler(42);
            var b = Sampler(42);
            foreach (var d in TestDirections(500, 1))
            {
                Assert.AreEqual(a.TerrainHeight(d), b.TerrainHeight(d));
                Assert.AreEqual(a.Sample(d).Albedo.g, b.Sample(d).Albedo.g);
            }
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentSurfaces()
        {
            Assert.AreNotEqual(PlanetFingerprint.Compute(Sampler(1)), PlanetFingerprint.Compute(Sampler(2)));
            var a = Sampler(1);
            var b = Sampler(2);
            int differing = 0;
            foreach (var d in TestDirections(200, 3))
                if (Math.Abs(a.TerrainHeight(d) - b.TerrainHeight(d)) > 1.0) differing++;
            Assert.Greater(differing, 190);
        }

        [Test]
        public void ChangingAParameter_ChangesFingerprint()
        {
            Assert.AreNotEqual(PlanetFingerprint.Compute(Sampler(5)),
                PlanetFingerprint.Compute(Sampler(5, s => s.mountainAmplitude *= 1.5)));
        }

        [Test]
        public void Sampling_IsIndependentOfOrderAndThreads()
        {
            var sampler = Sampler(77);
            var dirs = TestDirections(2000, 9);
            var sequential = new double[dirs.Length];
            for (int i = dirs.Length - 1; i >= 0; i--) sequential[i] = sampler.SurfaceHeight(dirs[i]);
            var parallel = new double[dirs.Length];
            Parallel.For(0, dirs.Length, i => parallel[i] = sampler.SurfaceHeight(dirs[i]));
            CollectionAssert.AreEqual(sequential, parallel);
        }

        [Test]
        public void SeedNeverChangesPhysics()
        {
            var physics = PlanetPhysicalProperties.CreateEarth();
            var a = new PlanetModel("a", physics, Sampler(1), Vector3d.Zero);
            var b = new PlanetModel("b", physics, Sampler(999), Vector3d.Zero);
            Assert.AreEqual(a.GravitationalParameter, b.GravitationalParameter);
            Assert.AreEqual(a.Radius, b.Radius);
        }

        [Test]
        public void UnsupportedAlgorithmVersion_IsRefused()
        {
            var s = new PlanetGenerationSettings { algorithmVersion = 99 };
            Assert.Throws<NotSupportedException>(() => PlanetSurfaceSampler.Create(s, R));
        }

        [Test]
        public void Heights_StayWithinDeclaredBounds()
        {
            var sampler = Sampler(123);
            foreach (var d in TestDirections(5000, 11))
            {
                double h = sampler.SurfaceHeight(d);
                Assert.That(h, Is.InRange(sampler.MinSurfaceHeight, sampler.MaxSurfaceHeight));
                Assert.IsFalse(double.IsNaN(h));
            }
        }

        [Test]
        public void FlattenZone_IsLevel()
        {
            var sampler = Sampler(8, s => s.flattenZones = new[]
            {
                new FlattenZone { latitudeDegrees = 45.0, longitudeDegrees = 63.0, radius = 500.0, minHeightAboveSeaLevel = 20.0 }
            });
            Vector3d c = Physics.PlanetRotation.DirectionFromLatLon(45.0 * MathD.DegToRad, 63.0 * MathD.DegToRad);
            Physics.PlanetRotation.LocalBasis(c, out Vector3d east, out Vector3d north);
            double h0 = sampler.SurfaceHeight(c);
            Assert.GreaterOrEqual(h0, 20.0);
            foreach (double off in new[] { 100.0, 250.0, 400.0 })
            {
                Assert.AreEqual(h0, sampler.SurfaceHeight((c + east * (off / R)).Normalized), 1e-6);
                Assert.AreEqual(h0, sampler.SurfaceHeight((c - north * (off / R)).Normalized), 1e-6);
            }
        }

        [Test]
        public void CubeFaces_ShareIdenticalEdgeVertices()
        {
            // Face 0 (+X) edge u = +1 coincides with face 2 (+Y) edge v = +1 (points (1, 1, z)).
            for (int k = 0; k <= 64; k++)
            {
                double t = -1.0 + 2.0 * k / 64.0;
                Vector3d a = CubeSphere.Direction(0, 1.0, t);
                Vector3d b = CubeSphere.Direction(2, t, 1.0);
                Assert.AreEqual(a, b, "bit-identical shared edge");
            }
        }

        [Test]
        public void NeighbourChunks_HaveMatchingBorderNormals()
        {
            var sampler = Sampler(31);
            const int n = 16;
            // Level-1 chunk on face 0 at the +u edge and its neighbour on face 2 at the +v edge.
            var a = ChunkMeshBuilder.Build(sampler, new ChunkId(0, 1, 1, 0), n);
            var b = ChunkMeshBuilder.Build(sampler, new ChunkId(2, 1, 0, 1), n);
            int side = n + 1;
            int matched = 0;
            for (int j = 0; j <= n; j++)
            {
                int ia = j * side + n; // face 0, i = n (u = +1)
                Vector3d pa = a.Center + new Vector3d(a.Positions[ia * 3], a.Positions[ia * 3 + 1], a.Positions[ia * 3 + 2]);
                for (int i = 0; i <= n; i++)
                {
                    int ib = n * side + i; // face 2, j = n (v = +1)
                    Vector3d pb = b.Center + new Vector3d(b.Positions[ib * 3], b.Positions[ib * 3 + 1], b.Positions[ib * 3 + 2]);
                    if ((pa - pb).Magnitude < 5.0)
                    {
                        matched++;
                        Assert.AreEqual(a.Normals[ia * 3], b.Normals[ib * 3], 1e-6);
                        Assert.AreEqual(a.Normals[ia * 3 + 1], b.Normals[ib * 3 + 1], 1e-6);
                        Assert.AreEqual(a.Normals[ia * 3 + 2], b.Normals[ib * 3 + 2], 1e-6);
                    }
                }
            }
            // Both chunks span z ∈ [−1, 0] along the shared cube edge, so every edge vertex has a twin.
            Assert.AreEqual(n + 1, matched, "all shared edge vertices coincide");
        }

        [Test]
        public void ChunkMesh_IsWellFormedAndFacesOutward()
        {
            var sampler = Sampler(4);
            var mesh = ChunkMeshBuilder.Build(sampler, new ChunkId(4, 3, 2, 5), 32);
            Assert.AreEqual(33 * 33 + 4 * 32, mesh.VertexCount);
            foreach (float f in mesh.Positions) Assert.IsFalse(float.IsNaN(f));
            foreach (int idx in mesh.Triangles) Assert.That(idx, Is.InRange(0, mesh.VertexCount - 1));

            int outward = 0;
            int gridTris = 32 * 32 * 2;
            for (int t = 0; t < gridTris; t++)
            {
                Vector3d p0 = V(mesh, mesh.Triangles[t * 3]), p1 = V(mesh, mesh.Triangles[t * 3 + 1]), p2 = V(mesh, mesh.Triangles[t * 3 + 2]);
                Vector3d nrm = Vector3d.Cross(p1 - p0, p2 - p0);
                if (Vector3d.Dot(nrm, (mesh.Center + p0).Normalized) > 0.0) outward++;
            }
            Assert.Greater(outward, gridTris * 0.98, "counter-clockwise seen from outside (physics frame)");
        }

        [Test]
        public void ChunkGeneration_IsFastEnoughForStreaming()
        {
            var sampler = Sampler(10);
            ChunkMeshBuilder.Build(sampler, new ChunkId(0, 10, 500, 500), 32); // warm-up (JIT)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            const int count = 8;
            for (int i = 0; i < count; i++) ChunkMeshBuilder.Build(sampler, new ChunkId(i % 6, 12, 1000 + i, 2000), 32);
            double msPerChunk = sw.Elapsed.TotalMilliseconds / count;
            TestContext.WriteLine("Chunk 33×33 + skirts: " + msPerChunk.ToString("0.0") + " ms (single thread)");
            Assert.Less(msPerChunk, 250.0, "worker threads must be able to keep up with camera motion");
        }

        static Vector3d V(ChunkMeshData m, int i) => new Vector3d(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
    }
}
