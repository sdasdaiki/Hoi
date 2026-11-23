using System.Collections.Generic;
using EdoStrategy.Map;
using EdoStrategy.Units;
using UnityEngine;

namespace EdoStrategy.Core
{
    [DefaultExecutionOrder(-200)]
    public class GameManager : MonoBehaviour
    {
        [SerializeField] private List<TerrainDefinition> terrainDefinitions =
            new List<TerrainDefinition>();
        [SerializeField] private List<UnitDefinition> unitDefinitions =
            new List<UnitDefinition>();

        private readonly Dictionary<string, TerrainDefinition> terrains =
            new Dictionary<string, TerrainDefinition>();
        private readonly Dictionary<string, UnitDefinition> units =
            new Dictionary<string, UnitDefinition>();

        private void Awake()
        {
            RebuildRegistries();
        }

        private void OnValidate()
        {
            RebuildRegistries();
        }

        public void RegisterTerrain(TerrainDefinition terrain)
        {
            if (terrain == null || string.IsNullOrEmpty(terrain.terrainId))
            {
                return;
            }

            terrains[terrain.terrainId] = terrain;
            if (!terrainDefinitions.Contains(terrain))
            {
                terrainDefinitions.Add(terrain);
            }
        }

        public void RegisterUnitDefinition(UnitDefinition unit)
        {
            if (unit == null || string.IsNullOrEmpty(unit.unitTypeId))
            {
                return;
            }

            units[unit.unitTypeId] = unit;
            if (!unitDefinitions.Contains(unit))
            {
                unitDefinitions.Add(unit);
            }
        }

        public TerrainDefinition GetTerrain(string terrainId)
        {
            TerrainDefinition terrain;
            if (!string.IsNullOrEmpty(terrainId) &&
                terrains.TryGetValue(terrainId, out terrain))
            {
                return terrain;
            }
            return null;
        }

        public UnitDefinition GetUnitDefinition(string unitTypeId)
        {
            UnitDefinition unit;
            if (!string.IsNullOrEmpty(unitTypeId) &&
                units.TryGetValue(unitTypeId, out unit))
            {
                return unit;
            }
            return null;
        }

        private void RebuildRegistries()
        {
            terrains.Clear();
            units.Clear();

            if (terrainDefinitions != null)
            {
                foreach (TerrainDefinition terrain in terrainDefinitions)
                {
                    RegisterTerrain(terrain);
                }
            }
            if (unitDefinitions != null)
            {
                foreach (UnitDefinition unit in unitDefinitions)
                {
                    RegisterUnitDefinition(unit);
                }
            }

            TerrainDefinition[] loadedTerrains =
                Resources.FindObjectsOfTypeAll<TerrainDefinition>();
            foreach (TerrainDefinition terrain in loadedTerrains)
            {
                RegisterTerrain(terrain);
            }

            UnitDefinition[] loadedUnits =
                Resources.FindObjectsOfTypeAll<UnitDefinition>();
            foreach (UnitDefinition unit in loadedUnits)
            {
                RegisterUnitDefinition(unit);
            }
        }
    }
}
