using UnityEngine;
using UnityEngine.Tilemaps;

namespace EdoStrategy.Map
{
    [CreateAssetMenu(fileName = "NewTerrain", menuName = "EdoStrategy/TerrainDefinition")]
    public class TerrainDefinition : ScriptableObject
    {
        public string terrainId;
        public string displayName;
        public TileBase tileAsset;
        public int baseMoveCost = 1;
        public int defenseMultiplier = 1000;
        public int attackMultiplier = 1000;
        public int visionCost = 1;
        public int commandDelayPenalty = 0;
    }
}
