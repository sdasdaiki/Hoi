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
            if (GetUnitAt(unit.x, unit.y) != null)
            {
                Debug.LogWarning(
                    "ユニット追加拒否: 同じ座標が占有されています (" +
                    unit.x + "," + unit.y + ")");
                return;
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

        public bool TryMoveUnit(UnitData unit, int targetX, int targetY)
        {
            if (unit == null)
            {
                return false;
            }

            if (grid != null && !grid.IsInBounds(targetX, targetY))
            {
                return false;
            }

            UnitData occupant = GetUnitAt(targetX, targetY);
            if (occupant != null)
            {
                return false;
            }

            unit.x = targetX;
            unit.y = targetY;
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
