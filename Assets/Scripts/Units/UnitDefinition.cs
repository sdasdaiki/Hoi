using System;
using System.Collections.Generic;
using EdoStrategy.Map;
using UnityEngine;

namespace EdoStrategy.Units
{
    [Serializable]
    public struct TerrainMoveCostOverride
    {
        public string terrainId;
        public int moveCost;
    }

    [CreateAssetMenu(fileName = "NewUnit", menuName = "EdoStrategy/UnitDefinition")]
    public class UnitDefinition : ScriptableObject
    {
        public string unitTypeId;
        public string displayName;
        public int commandLevel;
        public GameObject unitPrefab;

        public int maxManpower = 3000;
        public int baseAttack = 300;
        public int baseDefense = 300;
        public int breakthrough = 250;
        public int movePoints = 3;
        public int attackRange = 1;
        public int recon = 1;
        public int visionRange = 2;

        public List<TerrainMoveCostOverride> terrainCostOverrides =
            new List<TerrainMoveCostOverride>();

        public int GetMoveCost(TerrainDefinition terrain)
        {
            if (terrain == null)
            {
                return -1;
            }

            if (terrainCostOverrides != null)
            {
                for (int i = 0; i < terrainCostOverrides.Count; i++)
                {
                    TerrainMoveCostOverride overrideCost = terrainCostOverrides[i];
                    if (overrideCost.terrainId == terrain.terrainId)
                    {
                        return overrideCost.moveCost;
                    }
                }
            }

            return terrain.baseMoveCost;
        }
    }
}
