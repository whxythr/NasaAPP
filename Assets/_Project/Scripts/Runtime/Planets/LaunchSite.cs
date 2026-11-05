using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Test launch pad built from primitives on the flattened terrain at the launch site. Purely visual —
    /// the physical ground is the planet surface function (the pad zone is flattened inside it).
    /// </summary>
    public sealed class LaunchSite : MonoBehaviour
    {
        PlanetRuntime planet;
        PlanetFixedObject anchor;
        Material concrete, steel, marker, paint, soot;
        /// <summary>The pad deck is this far above the physical ground: a raised slab, never coplanar with the terrain mesh.</summary>
        public const float DeckHeight = 1.0f;

        public void Initialize(PlanetRuntime planetRuntime, FloatingOrigin origin, SimulationController sim, Shader litShader)
        {
            planet = planetRuntime;
            anchor = gameObject.AddComponent<PlanetFixedObject>();
            anchor.Initialize(planetRuntime, origin, sim);

            concrete = ProceduralMeshes.LitMaterial(litShader, new Color(0.62f, 0.61f, 0.58f), 0.15f);
            steel = ProceduralMeshes.LitMaterial(litShader, new Color(0.30f, 0.31f, 0.33f), 0.35f);
            marker = ProceduralMeshes.LitMaterial(litShader, new Color(0.9f, 0.85f, 0.2f), 0.2f);
            paint = ProceduralMeshes.LitMaterial(litShader, new Color(0.78f, 0.24f, 0.10f), 0.3f);
            soot = ProceduralMeshes.LitMaterial(litShader, new Color(0.10f, 0.10f, 0.10f), 0.05f);

            BuildGeometry(planet.Definition.launchSite.padRadius);
            Place();
            planet.ModelChanged += OnModelChanged;
        }

        void OnModelChanged(SpaceSim.Planets.PlanetModel model) => Place();

        void OnDestroy()
        {
            if (planet != null) planet.ModelChanged -= OnModelChanged;
            if (concrete != null) Destroy(concrete);
            if (steel != null) Destroy(steel);
            if (marker != null) Destroy(marker);
            if (paint != null) Destroy(paint);
            if (soot != null) Destroy(soot);
        }

        void Place()
        {
            var site = planet.Definition.launchSite;
            anchor.PlaceOnSurface(site.latitudeDegrees, site.longitudeDegrees, 0.0);
        }

        void BuildGeometry(float padRadius)
        {
            // A 3 m slab (Unity's cylinder is 2 units tall → scale.y 1.5) whose deck is DeckHeight above the physical ground: the
            // terrain mesh approximates the ground with chords (coarser further away), so the deck must clearly stand above it.
            var pad = ProceduralMeshes.Primitive(PrimitiveType.Cylinder, "Pad", transform, concrete);
            pad.transform.localScale = new Vector3(padRadius * 2f, 1.5f, padRadius * 2f);
            pad.transform.localPosition = new Vector3(0f, DeckHeight - 1.5f, 0f);
            // A sloped apron around the slab so it reads as a raised platform, not a floating disc.
            var apron = ProceduralMeshes.MeshObject(ProceduralMeshes.Frustum(48, padRadius + 6f, padRadius, false, false, "Apron"), "Apron", transform, concrete);
            apron.transform.localScale = new Vector3(1f, DeckHeight + 1.5f, 1f);
            apron.transform.localPosition = new Vector3(0f, -1.5f, 0f);
            // Scorched ring around the flame deflector.
            var scorch = ProceduralMeshes.Primitive(PrimitiveType.Cylinder, "Scorch", transform, soot);
            scorch.transform.localScale = new Vector3(14f, 0.01f, 14f);
            scorch.transform.localPosition = new Vector3(0f, DeckHeight + 0.01f, 0f);

            // Service tower 14 m north-east of the centre: grey frame, red-orange arm.
            var tower = ProceduralMeshes.Primitive(PrimitiveType.Cube, "ServiceTower", transform, steel);
            tower.transform.localScale = new Vector3(4f, 45f, 4f);
            tower.transform.localPosition = new Vector3(14f, 22.5f + DeckHeight, 14f);
            for (int k = 1; k < 9; k++)
            {
                var floor = ProceduralMeshes.Primitive(PrimitiveType.Cube, "TowerFloor" + k, transform, paint);
                floor.transform.localScale = new Vector3(4.6f, 0.35f, 4.6f);
                floor.transform.localPosition = new Vector3(14f, k * 5f + DeckHeight, 14f);
            }
            var arm = ProceduralMeshes.Primitive(PrimitiveType.Cube, "Arm", transform, paint);
            arm.transform.localScale = new Vector3(9f, 0.8f, 0.8f);
            arm.transform.localPosition = new Vector3(9.5f, 30f + DeckHeight, 14f);

            // North marker (helps orientation: local +Z = north).
            var north = ProceduralMeshes.Primitive(PrimitiveType.Cube, "NorthMarker", transform, marker);
            north.transform.localScale = new Vector3(1.5f, 0.2f, 8f);
            north.transform.localPosition = new Vector3(0f, DeckHeight + 0.1f, padRadius - 6f);

            // Light masts give scale against the horizon.
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                var mast = ProceduralMeshes.Primitive(PrimitiveType.Cylinder, "Mast" + i, transform, steel);
                mast.transform.localScale = new Vector3(0.6f, 15f, 0.6f);
                mast.transform.localPosition = new Vector3(Mathf.Cos(a) * (padRadius + 25f), 15f, Mathf.Sin(a) * (padRadius + 25f));
            }
        }

        static Material MakeMaterial(Shader shader, Color color)
        {
            var m = new Material(shader);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.2f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.2f);
            return m;
        }
    }
}
