using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace EdoStrategy.Map
{
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(Tilemap))]
    public class HexTilemapView : MonoBehaviour
    {
        [SerializeField] private int width = 16;
        [SerializeField] private int height = 10;
        [SerializeField] private Tilemap tilemap;

        public HexGrid Grid { get; private set; }

        private readonly Dictionary<string, TerrainDefinition> runtimeTerrains =
            new Dictionary<string, TerrainDefinition>();

        private void Awake()
        {
            if (tilemap == null)
            {
                tilemap = GetComponent<Tilemap>();
            }

            CreateRuntimeTerrainAssets();
            Grid = new HexGrid(width, height, runtimeTerrains.Values);
            GenerateSampleMap();
            Grid.CellChanged += OnCellChanged;
            RefreshAllTiles();
        }

        private void OnDestroy()
        {
            if (Grid != null)
            {
                Grid.CellChanged -= OnCellChanged;
            }
        }

        private void CreateRuntimeTerrainAssets()
        {
            AddRuntimeTerrain("plain", "Plain", "#A8E6CF", 1, 1000, 1000, 1, 0);
            AddRuntimeTerrain("forest", "Forest", "#2D6A4F", 2, 1200, 1000, 2, 0);
            AddRuntimeTerrain("mountain", "Mountain", "#8D6E63", 3, 1400, 1000, 3, 1);
            AddRuntimeTerrain("river", "River", "#4FC3F7", 3, 800, 1000, 1, 1);
        }

        private void AddRuntimeTerrain(
            string id,
            string displayName,
            string hexColor,
            int moveCost,
            int defenseMultiplier,
            int attackMultiplier,
            int visionCost,
            int delayPenalty)
        {
            TerrainDefinition definition =
                ScriptableObject.CreateInstance<TerrainDefinition>();
            definition.terrainId = id;
            definition.displayName = displayName;
            definition.baseMoveCost = moveCost;
            definition.defenseMultiplier = defenseMultiplier;
            definition.attackMultiplier = attackMultiplier;
            definition.visionCost = visionCost;
            definition.commandDelayPenalty = delayPenalty;

            var tile = new Tile
            {
                name = displayName + " Tile",
                color = ParseColor(hexColor)
            };
            definition.tileAsset = tile;
            runtimeTerrains[id] = definition;
        }

        private void GenerateSampleMap()
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    string terrainId = "plain";
                    if ((x + y * 2) % 11 == 0)
                    {
                        terrainId = "forest";
                    }
                    else if ((x * 3 + y) % 17 == 0)
                    {
                        terrainId = "mountain";
                    }
                    else if (x == width / 2)
                    {
                        terrainId = "river";
                    }

                    Grid.SetCellTerrain(x, y, terrainId);
                }
            }
        }

        private void RefreshAllTiles()
        {
            tilemap.ClearAllTiles();
            foreach (HexCell cell in Grid.Cells)
            {
                PaintCell(cell);
            }
        }

        private void OnCellChanged(HexGrid grid, HexCell cell)
        {
            PaintCell(cell);
        }

        private void PaintCell(HexCell cell)
        {
            TerrainDefinition terrain = Grid.GetTerrainDefinition(cell.terrainId);
            if (terrain == null)
            {
                return;
            }

            tilemap.SetTile(new Vector3Int(cell.x, cell.y, 0), terrain.tileAsset);
        }

        private static Color ParseColor(string hexColor)
        {
            return ColorUtility.TryParseHtmlString(hexColor, out Color color)
                ? color
                : Color.white;
        }
    }
}
