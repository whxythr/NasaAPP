using System.Collections.Generic;
using SpaceSim.Physics;
using SpaceSim.Planets;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// A star with its planets and moons. Each body is a <see cref="PlanetDefinition"/> with an orbit around the star
    /// or around another body (parentBodyId). Create via Assets → Create → SpaceSim → Star System, or
    /// SpaceSim ▸ Create Home System Assets.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceSim/Star System", fileName = "StarSystem")]
    public sealed class StarSystemDefinition : ScriptableObject
    {
        public string systemName = "Домашняя система";
        public StarProperties star = new StarProperties();
        public List<PlanetDefinition> bodies = new List<PlanetDefinition>();
        [Tooltip("planetId of the body where the probe starts.")]
        public string startBodyId = HomeSystemCatalog.HomeWorldId;

        /// <summary>The designer's home system (Teonan, Quoris, Hermeous Mora + moon), built in memory.</summary>
        public static StarSystemDefinition CreateHomeSystem()
        {
            var s = CreateInstance<StarSystemDefinition>();
            s.name = "Home system (runtime default)";
            s.star = HomeSystemCatalog.Star();
            foreach (var preset in HomeSystemCatalog.Bodies()) s.bodies.Add(PlanetDefinition.FromPreset(preset));
            return s;
        }
    }
}
