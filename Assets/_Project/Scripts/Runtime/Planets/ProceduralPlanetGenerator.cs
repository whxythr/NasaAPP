using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpaceSim.Mathematics;
using SpaceSim.Procedural;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Streams the procedural surface as a cube-sphere quadtree. Only chunks near the camera are refined;
    /// geometry is produced on worker threads from the deterministic sampler, uploaded to the GPU at a bounded
    /// rate, and cached (LRU, memory-bounded). The planet is never meshed at full detail.
    ///
    /// A node is replaced by its four children only once all four are ready, so the surface never has holes.
    /// Each chunk's transform is computed in double precision from the planet-fixed chunk centre, so geometry
    /// near the camera has no float jitter (see FloatingOrigin).
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class ProceduralPlanetGenerator : MonoBehaviour
    {
        sealed class Node
        {
            public ChunkId Id;
            public Node[] Children;
            public ChunkMeshData Data;
            public bool Requested;
            public ChunkView View;
            public double Edge;
        }

        sealed class ChunkView
        {
            public GameObject Go;
            public MeshRenderer Renderer;
            public Mesh Mesh;
        }

        PlanetRuntime planet;
        FloatingOrigin origin;
        SimulationController simulation;
        Material material;
        ProceduralLodSettings lod;
        PlanetSurfaceSampler sampler;
        int generation;

        readonly Node[] roots = new Node[CubeSphere.FaceCount];
        readonly Dictionary<ChunkId, Node> nodes = new Dictionary<ChunkId, Node>();
        readonly List<Node> pending = new List<Node>();
        readonly ConcurrentQueue<KeyValuePair<int, ChunkMeshData>> completed = new ConcurrentQueue<KeyValuePair<int, ChunkMeshData>>();
        readonly ConcurrentQueue<string> workerErrors = new ConcurrentQueue<string>();
        readonly Dictionary<ChunkId, LinkedListNode<ChunkMeshData>> cache = new Dictionary<ChunkId, LinkedListNode<ChunkMeshData>>();
        readonly LinkedList<ChunkMeshData> lru = new LinkedList<ChunkMeshData>();
        readonly Stack<ChunkView> viewPool = new Stack<ChunkView>();
        readonly List<Node> displayed = new List<Node>();
        readonly HashSet<Node> displayedSet = new HashSet<Node>();
        readonly List<Node> previouslyDisplayed = new List<Node>();
        long cacheBytes;
        int runningJobs;
        int uploadsThisFrame;
        Vector3d cameraFixed;
        double occluderRadius;

        public int DisplayedChunkCount => displayed.Count;
        public int NodeCount => nodes.Count;
        public int PendingJobs => pending.Count + runningJobs;
        public long CacheBytes => cacheBytes;
        public int DeepestDisplayedLevel { get; private set; }

        public void Initialize(PlanetRuntime planetRuntime, FloatingOrigin floatingOrigin, SimulationController sim, Material surfaceMaterial)
        {
            planet = planetRuntime;
            origin = floatingOrigin;
            simulation = sim;
            material = surfaceMaterial;
            planet.SurfaceChanged += OnSurfaceChanged;
            OnSurfaceChanged(planet.Sampler);
        }

        void OnDestroy()
        {
            if (planet != null) planet.SurfaceChanged -= OnSurfaceChanged;
            ClearAll();
        }

        void OnSurfaceChanged(PlanetSurfaceSampler newSampler)
        {
            ClearAll();
            sampler = newSampler;
            generation++;
            lod = planet.Definition.lod;
            if (sampler == null) return;
            // Roots synchronously: the planet is always visible from the first frame.
            for (int f = 0; f < CubeSphere.FaceCount; f++)
            {
                var id = ChunkId.Root(f);
                roots[f] = CreateNode(id);
                roots[f].Data = ChunkMeshBuilder.Build(sampler, id, lod.chunkResolution, lod.skirtFraction);
                AddToCache(roots[f].Data);
            }
        }

        Node CreateNode(ChunkId id)
        {
            var n = new Node { Id = id, Edge = CubeSphere.ApproximateEdgeLength(id.Level, sampler.PlanetRadius) };
            nodes[id] = n;
            if (cache.TryGetValue(id, out var cached))
            {
                n.Data = cached.Value;
                Touch(cached);
            }
            return n;
        }

        void LateUpdate()
        {
            if (sampler == null || planet == null || planet.Model == null) return;
            lod = planet.Definition.lod;

            while (workerErrors.TryDequeue(out string err)) Debug.LogError("[SpaceSim] Chunk generation failed: " + err, this);
            IntegrateCompletedJobs();

            double renderTime = simulation.RenderTime;
            cameraFixed = planet.Model.ToFixed(origin.ViewerPosition, renderTime);
            occluderRadius = planet.Model.Radius + sampler.MinSurfaceHeight;
            uploadsThisFrame = 0;

            previouslyDisplayed.Clear();
            previouslyDisplayed.AddRange(displayed);
            displayed.Clear();
            displayedSet.Clear();
            DeepestDisplayedLevel = 0;

            for (int f = 0; f < roots.Length; f++)
                if (roots[f] != null) Traverse(roots[f]);

            // Hide chunks that are no longer displayed (views are kept for quick reuse while the node exists).
            for (int i = 0; i < previouslyDisplayed.Count; i++)
            {
                Node n = previouslyDisplayed[i];
                if (!displayedSet.Contains(n) && n.View != null) n.View.Go.SetActive(false);
            }

            Quaternion rotation = planet.UnityRotation(renderTime);
            for (int i = 0; i < displayed.Count; i++)
            {
                Node n = displayed[i];
                bool visible = AboveHorizon(n);
                n.View.Go.SetActive(visible);
                if (!visible) continue;
                Transform t = n.View.Go.transform;
                t.SetPositionAndRotation(origin.ToUnityPosition(planet.Model.ToInertial(n.Data.Center, renderTime)), rotation);
            }

            StartJobs();
            EnforceCacheBudget();
        }

        void Traverse(Node node)
        {
            double distance = DistanceToChunk(node);
            bool isSplit = node.Children != null;
            double threshold = node.Edge * lod.splitDistanceFactor * (isSplit ? 1.15 : 1.0); // hysteresis
            bool wantSplit = node.Id.Level < lod.maxLevel && distance < threshold;

            if (wantSplit)
            {
                if (node.Children == null)
                {
                    node.Children = new Node[4];
                    for (int i = 0; i < 4; i++) node.Children[i] = CreateNode(node.Id.Child(i));
                }
                bool ready = true;
                for (int i = 0; i < 4; i++)
                {
                    Node c = node.Children[i];
                    if (c.Data == null)
                    {
                        Request(c);
                        ready = false;
                    }
                    else if (!EnsureView(c)) ready = false;
                }
                if (ready)
                {
                    for (int i = 0; i < 4; i++) Traverse(node.Children[i]);
                    return;
                }
            }
            else if (node.Children != null)
            {
                Collapse(node);
            }

            if (node.Data != null && EnsureView(node)) Display(node);
            else if (node.Children != null)
            {
                // Parent geometry missing (evicted): keep showing children until it is regenerated.
                Request(node);
                for (int i = 0; i < 4; i++) Traverse(node.Children[i]);
            }
            else Request(node);
        }

        void Display(Node node)
        {
            displayed.Add(node);
            displayedSet.Add(node);
            if (node.Id.Level > DeepestDisplayedLevel) DeepestDisplayedLevel = node.Id.Level;
        }

        double DistanceToChunk(Node node)
        {
            Vector3d center;
            double radius;
            if (node.Data != null)
            {
                center = node.Data.Center;
                radius = node.Data.BoundingRadius;
            }
            else
            {
                center = CubeSphere.ChunkDirection(node.Id, 0.5, 0.5) * sampler.PlanetRadius;
                radius = node.Edge;
            }
            return Math.Max(0.0, (cameraFixed - center).Magnitude - radius);
        }

        /// <summary>Conservative horizon test against the lowest possible surface sphere.</summary>
        bool AboveHorizon(Node node)
        {
            double camR = cameraFixed.Magnitude;
            if (camR <= occluderRadius) return true;
            Vector3d c = node.Data.Center;
            double cR = c.Magnitude;
            double chunkAngle = Math.Min(Math.PI, node.Data.BoundingRadius / Math.Max(1.0, cR));
            double top = cR + node.Data.BoundingRadius;
            double horizonCam = Math.Acos(MathD.Clamp(occluderRadius / camR, -1.0, 1.0));
            double horizonChunk = Math.Acos(MathD.Clamp(occluderRadius / Math.Max(top, occluderRadius), -1.0, 1.0));
            double angle = Vector3d.Angle(cameraFixed, c);
            return angle <= horizonCam + horizonChunk + chunkAngle;
        }

        void Collapse(Node node)
        {
            if (node.Children == null) return;
            for (int i = 0; i < 4; i++)
            {
                Node c = node.Children[i];
                Collapse(c);
                ReleaseView(c);
                nodes.Remove(c.Id);
                pending.Remove(c);
            }
            node.Children = null;
        }

        bool EnsureView(Node node)
        {
            if (node.View != null) return true;
            if (uploadsThisFrame >= lod.maxMeshUploadsPerFrame) return false;
            uploadsThisFrame++;
            ChunkView v = viewPool.Count > 0 ? viewPool.Pop() : CreateView();
            CoordinateSystem.FillMesh(node.Data, v.Mesh);
            v.Go.name = "Chunk " + node.Id;
            v.Go.SetActive(false);
            node.View = v;
            return true;
        }

        ChunkView CreateView()
        {
            var go = new GameObject("Chunk");
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
            var mesh = new Mesh { name = "PlanetChunk" };
            mesh.MarkDynamic();
            mf.sharedMesh = mesh;
            return new ChunkView { Go = go, Renderer = mr, Mesh = mesh };
        }

        void ReleaseView(Node node)
        {
            if (node.View == null) return;
            node.View.Go.SetActive(false);
            viewPool.Push(node.View);
            node.View = null;
        }

        // ---- Jobs & cache --------------------------------------------------------------------

        void Request(Node node)
        {
            if (node.Data != null || node.Requested) return;
            node.Requested = true;
            pending.Add(node);
        }

        void StartJobs()
        {
            if (pending.Count == 0) return;
            // Coarse and near chunks first.
            pending.Sort((a, b) =>
            {
                int byLevel = a.Id.Level.CompareTo(b.Id.Level);
                return byLevel != 0 ? byLevel : DistanceToChunk(a).CompareTo(DistanceToChunk(b));
            });

            int gen = generation;
            PlanetSurfaceSampler s = sampler;
            int resolution = lod.chunkResolution;
            double skirt = lod.skirtFraction;
            int synchronousBudget = 2;

            while (pending.Count > 0)
            {
                Node n = pending[0];
                ChunkId id = n.Id;
                if (lod.useWorkerThreads && Application.platform != RuntimePlatform.WebGLPlayer)
                {
                    if (runningJobs >= lod.maxConcurrentJobs) break;
                    pending.RemoveAt(0);
                    Interlocked.Increment(ref runningJobs);
                    Task.Run(() =>
                    {
                        try
                        {
                            completed.Enqueue(new KeyValuePair<int, ChunkMeshData>(gen, ChunkMeshBuilder.Build(s, id, resolution, skirt)));
                        }
                        catch (Exception e)
                        {
                            workerErrors.Enqueue(id + ": " + e.Message);
                        }
                        finally
                        {
                            Interlocked.Decrement(ref runningJobs);
                        }
                    });
                }
                else
                {
                    // Synchronous fallback: a small number of chunks per frame.
                    if (synchronousBudget-- <= 0) break;
                    pending.RemoveAt(0);
                    completed.Enqueue(new KeyValuePair<int, ChunkMeshData>(gen, ChunkMeshBuilder.Build(s, id, resolution, skirt)));
                }
            }
        }

        void IntegrateCompletedJobs()
        {
            while (completed.TryDequeue(out var item))
            {
                if (item.Key != generation) continue; // produced for an older surface: discard
                ChunkMeshData data = item.Value;
                AddToCache(data);
                if (nodes.TryGetValue(data.Id, out Node n) && n.Data == null)
                {
                    n.Data = data;
                    n.Requested = false;
                }
            }
        }

        void AddToCache(ChunkMeshData data)
        {
            if (cache.TryGetValue(data.Id, out var existing))
            {
                Touch(existing);
                return;
            }
            cache[data.Id] = lru.AddFirst(data);
            cacheBytes += data.ApproximateBytes;
        }

        void Touch(LinkedListNode<ChunkMeshData> entry)
        {
            lru.Remove(entry);
            lru.AddFirst(entry);
        }

        void EnforceCacheBudget()
        {
            long budget = (long)lod.cacheBudgetMegabytes * 1024L * 1024L;
            var entry = lru.Last;
            while (cacheBytes > budget && entry != null)
            {
                var prev = entry.Previous;
                ChunkMeshData d = entry.Value;
                bool inUse = nodes.TryGetValue(d.Id, out Node n) && n.Data == d;
                if (!inUse && d.Id.Level > 0)
                {
                    lru.Remove(entry);
                    cache.Remove(d.Id);
                    cacheBytes -= d.ApproximateBytes;
                }
                entry = prev;
            }
        }

        void ClearAll()
        {
            foreach (var n in nodes.Values)
            {
                if (n.View != null)
                {
                    Destroy(n.View.Mesh);
                    Destroy(n.View.Go);
                    n.View = null;
                }
            }
            while (viewPool.Count > 0)
            {
                var v = viewPool.Pop();
                Destroy(v.Mesh);
                Destroy(v.Go);
            }
            nodes.Clear();
            pending.Clear();
            displayed.Clear();
            displayedSet.Clear();
            cache.Clear();
            lru.Clear();
            cacheBytes = 0;
            for (int i = 0; i < roots.Length; i++) roots[i] = null;
        }
    }
}
