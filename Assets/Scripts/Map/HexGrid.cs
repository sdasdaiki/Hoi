using System;
using System.Collections.Generic;
using UnityEngine;

namespace EdoStrategy.Map
{
    public class HexGrid
    {
        private readonly Dictionary<string, TerrainDefinition> terrainDefinitions =
            new Dictionary<string, TerrainDefinition>();

        public int Width { get; private set; }
        public int Height { get; private set; }
        public HexCell[] Cells { get; private set; }

        public event Action<HexGrid, HexCell> CellChanged;

        public HexGrid(int width, int height, IEnumerable<TerrainDefinition> definitions)
        {
            Width = width;
            Height = height;
            Cells = new HexCell[width * height];

            if (definitions != null)
            {
                foreach (TerrainDefinition definition in definitions)
                {
                    if (definition != null && !string.IsNullOrEmpty(definition.terrainId))
                    {
                        terrainDefinitions[definition.terrainId] = definition;
                    }
                }
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Cells[y * width + x] = new HexCell(x, y);
                }
            }
        }

        public bool IsInBounds(int x, int y)
        {
            return x >= 0 && y >= 0 && x < Width && y < Height;
        }

        public HexCell GetCell(int x, int y)
        {
            return IsInBounds(x, y) ? Cells[y * Width + x] : null;
        }

        public void SetCellTerrain(int x, int y, string terrainId)
        {
            HexCell cell = GetCell(x, y);
            if (cell == null || string.IsNullOrEmpty(terrainId))
            {
                return;
            }

            if (cell.terrainId == terrainId)
            {
                return;
            }

            cell.terrainId = terrainId;
            CellChanged?.Invoke(this, cell);
        }

        public TerrainDefinition GetTerrainDefinition(string terrainId)
        {
            return terrainDefinitions.TryGetValue(terrainId, out TerrainDefinition definition)
                ? definition
                : null;
        }

        public int GetMoveCost(int x, int y)
        {
            HexCell cell = GetCell(x, y);
            if (cell == null)
            {
                return -1;
            }

            TerrainDefinition terrain = GetTerrainDefinition(cell.terrainId);
            return terrain == null ? -1 : terrain.baseMoveCost;
        }

        public List<Vector2Int> GetNeighbors(int x, int y)
        {
            var neighbors = new List<Vector2Int>(6);
            for (int direction = 0; direction < 6; direction++)
            {
                Vector2Int neighbor = HexUtility.GetNeighborOffset(x, y, direction);
                if (IsInBounds(neighbor.x, neighbor.y))
                {
                    neighbors.Add(neighbor);
                }
            }
            return neighbors;
        }

        public List<Vector2Int> FindPath(int startX, int startY, int goalX, int goalY)
        {
            HexCell start = GetCell(startX, startY);
            HexCell goal = GetCell(goalX, goalY);
            if (start == null || goal == null)
            {
                return new List<Vector2Int>();
            }

            int total = Width * Height;
            var frontier = new List<PathNode>();
            var bestCost = new int[total];
            var cameFrom = new int[total];
            for (int i = 0; i < total; i++)
            {
                bestCost[i] = int.MaxValue;
                cameFrom[i] = -1;
            }

            int startIndex = startY * Width + startX;
            int goalIndex = goalY * Width + goalX;
            bestCost[startIndex] = 0;
            frontier.Add(new PathNode(startIndex, 0));

            while (frontier.Count > 0)
            {
                int bestPosition = 0;
                for (int i = 1; i < frontier.Count; i++)
                {
                    if (frontier[i].Priority < frontier[bestPosition].Priority)
                    {
                        bestPosition = i;
                    }
                }

                PathNode current = frontier[bestPosition];
                frontier.RemoveAt(bestPosition);
                if (current.Index == goalIndex)
                {
                    break;
                }

                int currentX = current.Index % Width;
                int currentY = current.Index / Width;
                foreach (Vector2Int neighbor in GetNeighbors(currentX, currentY))
                {
                    int moveCost = GetMoveCost(neighbor.x, neighbor.y);
                    if (moveCost < 0)
                    {
                        continue;
                    }

                    int neighborIndex = neighbor.y * Width + neighbor.x;
                    int newCost = bestCost[current.Index] + moveCost;
                    if (newCost >= bestCost[neighborIndex])
                    {
                        continue;
                    }

                    bestCost[neighborIndex] = newCost;
                    cameFrom[neighborIndex] = current.Index;
                    int distance = HexUtility.DistanceOffset(neighbor.x, neighbor.y, goalX, goalY);
                    frontier.Add(new PathNode(neighborIndex, newCost + distance));
                }
            }

            if (cameFrom[goalIndex] == -1 && startIndex != goalIndex)
            {
                return new List<Vector2Int>();
            }

            var reversePath = new List<Vector2Int>();
            int pathIndex = goalIndex;
            while (pathIndex != -1)
            {
                reversePath.Add(new Vector2Int(pathIndex % Width, pathIndex / Width));
                if (pathIndex == startIndex)
                {
                    break;
                }
                pathIndex = cameFrom[pathIndex];
            }

            reversePath.Reverse();
            return reversePath;
        }

        private readonly struct PathNode
        {
            public readonly int Index;
            public readonly int Priority;

            public PathNode(int index, int priority)
            {
                Index = index;
                Priority = priority;
            }
        }
    }
}
