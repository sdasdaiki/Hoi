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
        [SerializeField] private List<TerrainDefinition> terrainDefinitions =
            new List<TerrainDefinition>();

        public HexGrid Grid { get; private set; }

        private void Awake()
        {
            if (tilemap == null)
            {
                tilemap = GetComponent<Tilemap>();
            }
        }

        private void Start()
        {
            var definitions = new List<TerrainDefinition>();
            foreach (TerrainDefinition definition in terrainDefinitions)
            {
                if (definition != null)
                {
                    definitions.Add(definition);
                }
            }

            if (!ContainsTerrain(definitions, "plain"))
            {
                AddPlainFallback(definitions);
            }

            Grid = new HexGrid(width, height, definitions);
            FillWithPlain();
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

        private static bool ContainsTerrain(
            IEnumerable<TerrainDefinition> definitions,
            string terrainId)
        {
            foreach (TerrainDefinition definition in definitions)
            {
                if (definition != null && definition.terrainId == terrainId)
                {
                    return true;
                }
            }
            return false;
        }

        private static void AddPlainFallback(ICollection<TerrainDefinition> definitions)
        {
            TerrainDefinition definition =
                ScriptableObject.CreateInstance<TerrainDefinition>();
            definition.terrainId = "plain";
            definition.displayName = "Plain";
            definition.baseMoveCost = 1;
            definition.defenseMultiplier = 1000;
            definition.attackMultiplier = 1000;
            definition.visionCost = 1;
            definition.commandDelayPenalty = 0;

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = "Plain Tile";
            tile.color = new Color(168f / 255f, 230f / 255f, 207f / 255f, 1f);
            definition.tileAsset = tile;
            definitions.Add(definition);
        }

        private void FillWithPlain()
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Grid.SetCellTerrain(x, y, "plain");
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

        public void RefreshAllTilesPublic()
        {
            RefreshAllTiles();
        }

        public void LoadMap(MapSaveData saveData)
        {
            if (saveData == null)
            {
                return;
            }

            Grid.ApplyMapSaveData(saveData);
            RefreshAllTiles();
        }

        public MapSaveData CreateSaveData(string mapName)
        {
            var saveData = new MapSaveData
            {
                mapName = mapName,
                width = Grid.Width,
                height = Grid.Height,
                cells = new HexCell[Grid.Cells.Length]
            };

            for (int i = 0; i < Grid.Cells.Length; i++)
            {
                HexCell source = Grid.Cells[i];
                saveData.cells[i] = new HexCell(source.x, source.y)
                {
                    terrainId = source.terrainId,
                    deployZoneFaction = source.deployZoneFaction,
                    infrastructureLevel = source.infrastructureLevel,
                    currentSupply = source.currentSupply
                };
            }
            return saveData;
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
    }
}
