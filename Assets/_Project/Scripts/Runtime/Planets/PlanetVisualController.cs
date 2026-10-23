using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Chooses and maintains the planet's visual representation. Whatever is shown here, the physics comes from
    /// <see cref="PlanetRuntime.Model"/>: an imported model is scaled to the PHYSICAL radius, never the other way
    /// round. Additional layers (clouds, rings, moons) are meant to be added as sibling components.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class PlanetVisualController : MonoBehaviour
    {
        PlanetRuntime planet;
        FloatingOrigin origin;
        SimulationController simulation;
        Material surfaceMaterial;

        ProceduralPlanetGenerator procedural;
        GameObject importedInstance;
        GameObject referenceSphere;
        PlanetSurfaceSource activeSource = (PlanetSurfaceSource)(-1);

        public ProceduralPlanetGenerator Procedural => procedural;
        public Material SurfaceMaterial => surfaceMaterial;

        public void Initialize(PlanetRuntime planetRuntime, FloatingOrigin floatingOrigin, SimulationController sim, Material material)
        {
            planet = planetRuntime;
            origin = floatingOrigin;
            simulation = sim;
            surfaceMaterial = material;
            planet.SurfaceChanged += _ => Rebuild();
            Rebuild();
        }

        void Rebuild()
        {
            PlanetDefinition def = planet.Definition;
            PlanetSurfaceSource source = def.surfaceSource;
            if (source == PlanetSurfaceSource.Procedural && planet.Sampler == null) source = PlanetSurfaceSource.ReferenceSphere;
            if (source == PlanetSurfaceSource.ImportedModel && def.importedModel.modelPrefab == null)
            {
                Debug.LogWarning("[SpaceSim] Imported model selected but no prefab assigned; showing the reference sphere.", this);
                source = PlanetSurfaceSource.ReferenceSphere;
            }

            if (source != activeSource)
            {
                if (procedural != null) Destroy(procedural.gameObject);
                if (importedInstance != null) Destroy(importedInstance);
                if (referenceSphere != null) Destroy(referenceSphere);
                procedural = null;
                importedInstance = null;
                referenceSphere = null;
                activeSource = source;

                switch (source)
                {
                    case PlanetSurfaceSource.Procedural:
                        var go = new GameObject("ProceduralSurface");
                        go.transform.SetParent(transform, false);
                        procedural = go.AddComponent<ProceduralPlanetGenerator>();
                        procedural.Initialize(planet, origin, simulation, surfaceMaterial);
                        break;
                    case PlanetSurfaceSource.ImportedModel:
                        importedInstance = Instantiate(def.importedModel.modelPrefab, transform);
                        importedInstance.name = "ImportedModel";
                        foreach (var c in importedInstance.GetComponentsInChildren<Collider>()) Destroy(c);
                        break;
                    default:
                        referenceSphere = new GameObject("ReferenceSphere");
                        referenceSphere.transform.SetParent(transform, false);
                        var sphereMesh = ProceduralMeshes.UvSphere(128, 64, "ReferenceSphere");
                        // The surface shader reads albedo from vertex colours (alpha = water mask): plain grey land.
                        var colors = new Color[sphereMesh.vertexCount];
                        for (int i = 0; i < colors.Length; i++) colors[i] = new Color(0.45f, 0.43f, 0.40f, 0f);
                        sphereMesh.colors = colors;
                        referenceSphere.AddComponent<MeshFilter>().sharedMesh = sphereMesh;
                        referenceSphere.AddComponent<MeshRenderer>().sharedMaterial = surfaceMaterial;
                        break;
                }
            }
        }

        void LateUpdate()
        {
            if (planet == null || planet.Model == null) return;
            double t = simulation.RenderTime;
            Vector3 center = planet.UnityCenter(origin);
            Quaternion rotation = planet.UnityRotation(t);
            float radius = (float)planet.Model.Radius;

            if (importedInstance != null)
            {
                var s = planet.Definition.importedModel;
                float scale = radius / Mathf.Max(1e-6f, s.modelRadius);
                importedInstance.transform.SetPositionAndRotation(center, rotation * Quaternion.Euler(s.rotationOffsetEuler));
                importedInstance.transform.localScale = Vector3.one * scale;
            }
            if (referenceSphere != null)
            {
                referenceSphere.transform.SetPositionAndRotation(center, rotation);
                referenceSphere.transform.localScale = Vector3.one * radius;
            }
        }
    }
}
