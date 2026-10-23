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
        Material concrete, steel, marker;

        public void Initialize(PlanetRuntime planetRuntime, FloatingOrigin origin, SimulationController sim, Shader litShader)
        {
            planet = planetRuntime;
            anchor = gameObject.AddComponent<PlanetFixedObject>();
            anchor.Initialize(planetRuntime, origin, sim);

            concrete = MakeMaterial(litShader, new Color(0.55f, 0.55f, 0.53f));
            steel = MakeMaterial(litShader, new Color(0.62f, 0.22f, 0.12f));
            marker = MakeMaterial(litShader, new Color(0.9f, 0.85f, 0.2f));

            BuildGeometry(planet.Definition.launchSite.padRadius);
            Place();
            planet.ModelChanged += _ => Place();
        }

        void OnDestroy()
        {
            if (concrete != null) Destroy(concrete);
            if (steel != null) Destroy(steel);
            if (marker != null) Destroy(marker);
        }

        void Place()
        {
            var site = planet.Definition.launchSite;
            anchor.PlaceOnSurface(site.latitudeDegrees, site.longitudeDegrees, 0.0);
        }

        void BuildGeometry(float padRadius)
        {
            var pad = ProceduralMeshes.Primitive(PrimitiveType.Cylinder, "Pad", transform, concrete);
            // Unity's cylinder is 2 units tall: scale.y 0.3 → 0.6 m slab whose top is flush with the physical ground
            // (y = 0), where the probe rests at contactRadius above it.
            pad.transform.localScale = new Vector3(padRadius * 2f, 0.3f, padRadius * 2f);
            pad.transform.localPosition = new Vector3(0f, -0.3f, 0f);

            // Service tower 30 m north-east of the centre.
            var tower = ProceduralMeshes.Primitive(PrimitiveType.Cube, "ServiceTower", transform, steel);
            tower.transform.localScale = new Vector3(4f, 45f, 4f);
            tower.transform.localPosition = new Vector3(14f, 22.5f, 14f);
            var arm = ProceduralMeshes.Primitive(PrimitiveType.Cube, "Arm", transform, steel);
            arm.transform.localScale = new Vector3(9f, 0.8f, 0.8f);
            arm.transform.localPosition = new Vector3(9.5f, 30f, 14f);

            // North marker (helps orientation: local +Z = north).
            var north = ProceduralMeshes.Primitive(PrimitiveType.Cube, "NorthMarker", transform, marker);
            north.transform.localScale = new Vector3(1.5f, 0.2f, 8f);
            north.transform.localPosition = new Vector3(0f, 0.7f, padRadius - 6f);

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
