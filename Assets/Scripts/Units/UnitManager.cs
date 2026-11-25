using System;
using System.Collections.Generic;
using EdoStrategy.Map;
using UnityEngine;

namespace EdoStrategy.Units
{
    public class UnitManager
    {
        private readonly List<UnitData> units = new List<UnitData>();
        private readonly HexGrid grid;

        public event Action<UnitData> UnitAdded;
        public event Action<UnitData> UnitMoved;
        public event Action<
            UnitData,
            Vector2Int,
            Vector2Int,
            IReadOnlyList<Vector2Int>> UnitMovedAlongPath;
        public event Action<UnitData> UnitRemoved;

        public IReadOnlyList<UnitData> Units
        {
            get { return units; }
        }

        public UnitManager()
        {
            grid = null;
        }

        public UnitManager(HexGrid grid)
        {
            this.grid = grid;
        }

        public void AddUnit(UnitData unit)
        {
            if (unit == null)
            {
                return;
            }
            if (units.Contains(unit))
            {
                Debug.LogWarning(
                    "ユニット追加拒否: 同じユニット参照が既に登録されています。");
                return;
            }
            if (!string.IsNullOrEmpty(unit.unitId) &&
                GetUnitById(unit.unitId) != null)
            {
                Debug.LogWarning(
                    "ユニット追加拒否: 重複したunitId=" + unit.unitId);
                return;
            }
            List<UnitData> occupants = GetUnitsAt(unit.x, unit.y);
            for (int i = 0; i < occupants.Count; i++)
            {
                if (occupants[i].faction != unit.faction)
                {
                    Debug.LogWarning(
                        "ユニット追加拒否: 敵部隊が同じ座標を占有しています (" +
                        unit.x + "," + unit.y + ")");
                    return;
                }
            }

            units.Add(unit);
            UnitAdded?.Invoke(unit);
        }

        public bool RemoveUnit(UnitData unit)
        {
            if (unit == null)
            {
                return false;
            }

            if (!units.Remove(unit))
            {
                return false;
            }

            UnitRemoved?.Invoke(unit);
            return true;
        }

        public bool RemoveUnit(string unitId)
        {
            if (string.IsNullOrEmpty(unitId))
            {
                return false;
            }

            UnitData unit = GetUnitById(unitId);
            return RemoveUnit(unit);
        }

        public UnitData GetUnitById(string unitId)
        {
            if (string.IsNullOrEmpty(unitId))
            {
                return null;
            }

            for (int i = 0; i < units.Count; i++)
            {
                if (units[i] != null && units[i].unitId == unitId)
                {
                    return units[i];
                }
            }
            return null;
        }

        public UnitData GetUnitAt(int x, int y)
        {
            for (int i = 0; i < units.Count; i++)
            {
                UnitData unit = units[i];
                if (unit != null && unit.x == x && unit.y == y)
                {
                    return unit;
                }
            }
            return null;
        }

        public List<UnitData> GetUnitsAt(int x, int y)
        {
            var unitsAtCell = new List<UnitData>();
            for (int i = 0; i < units.Count; i++)
            {
                UnitData unit = units[i];
                if (unit != null && unit.x == x && unit.y == y)
                {
                    unitsAtCell.Add(unit);
                }
            }
            return unitsAtCell;
        }

        public bool TryMoveUnit(UnitData unit, int targetX, int targetY)
        {
            Vector2Int from = unit == null
                ? Vector2Int.zero
                : new Vector2Int(unit.x, unit.y);
            return MoveUnitAlongPath(
                unit,
                targetX,
                targetY,
                new List<Vector2Int>
                {
                    from,
                    new Vector2Int(targetX, targetY)
                });
        }

        public bool MoveUnitAlongPath(
            UnitData unit,
            int targetX,
            int targetY,
            IReadOnlyList<Vector2Int> path)
        {
            if (unit == null ||
                grid != null && !grid.IsInBounds(targetX, targetY))
            {
                return false;
            }

            List<UnitData> occupants = GetUnitsAt(targetX, targetY);
            for (int i = 0; i < occupants.Count; i++)
            {
                if (occupants[i] != unit &&
                    occupants[i].faction != unit.faction)
                {
                    return false;
                }
            }

            Vector2Int from = new Vector2Int(unit.x, unit.y);
            Vector2Int to = new Vector2Int(targetX, targetY);
            var movementPath = path != null
                ? new List<Vector2Int>(path)
                : new List<Vector2Int>();
            if (movementPath.Count == 0 || movementPath[0] != from)
            {
                movementPath.Insert(0, from);
            }
            if (movementPath[movementPath.Count - 1] != to)
            {
                movementPath.Add(to);
            }

            unit.x = targetX;
            unit.y = targetY;
            UnitMovedAlongPath?.Invoke(unit, from, to, movementPath);
            UnitMoved?.Invoke(unit);
            return true;
        }

        public bool MoveUnit(UnitData unit, int targetX, int targetY)
        {
            return TryMoveUnit(unit, targetX, targetY);
        }

        public bool TryMoveUnit(
            UnitData unit,
            int targetX,
            int targetY,
            UnitDefinition definition)
        {
            if (unit == null)
            {
                return false;
            }

            if (grid == null)
            {
                return TryMoveUnit(unit, targetX, targetY);
            }

            if (!grid.IsInBounds(targetX, targetY))
            {
                return false;
            }

            HexCell targetCell = grid.GetCell(targetX, targetY);
            TerrainDefinition terrain =
                targetCell == null ? null : grid.GetTerrainDefinition(targetCell.terrainId);
            int moveCost = definition == null
                ? grid.GetMoveCost(targetX, targetY)
                : definition.GetMoveCost(terrain);

            if (moveCost < 0)
            {
                return false;
            }

            int distance = HexUtility.DistanceOffset(unit.x, unit.y, targetX, targetY);
            int movePoints = definition == null ? 0 : definition.movePoints;
            if (movePoints > 0 && distance > movePoints)
            {
                return false;
            }

            return TryMoveUnit(unit, targetX, targetY);
        }

        public bool MoveUnit(
            UnitData unit,
            int targetX,
            int targetY,
            UnitDefinition definition)
        {
            return TryMoveUnit(unit, targetX, targetY, definition);
        }

        public void NotifyUnitMoved(UnitData unit)
        {
            if (unit != null)
            {
                UnitMoved?.Invoke(unit);
            }
        }
    }
}
