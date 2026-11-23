using System.Collections;
using System.Collections.Generic;
using EdoStrategy.Command;
using EdoStrategy.Map;
using EdoStrategy.Units;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Tilemaps;

namespace EdoStrategy.Core
{
    [DefaultExecutionOrder(-50)]
    public class TurnManager : MonoBehaviour
    {
        [SerializeField] private HexTilemapView mapView;
        [SerializeField] private UnitBoardView unitBoardView;
        [SerializeField] private GameManager gameManager;
        [SerializeField] private Camera battleCamera;
        [SerializeField] private bool spawnTestUnitsOnStart = true;
        [SerializeField] private string loadMapName = "";

        public event System.Action OnCommandStateChanged;

        private UnitManager unitManager;
        private UnitData selectedUnit;
        private int turnNumber = 1;

        public BattleState State { get; private set; }
        public int TurnNumber
        {
            get { return turnNumber; }
        }
        public UnitManager UnitManager
        {
            get { return unitManager; }
        }
        public UnitData SelectedUnit
        {
            get { return selectedUnit; }
        }

        private void Awake()
        {
            ResolveReferences();
            State = BattleState.DeploymentPhase;
        }

        private void Start()
        {
            StartCoroutine(InitializeBattleAfterMapView());
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
        }

        private void Update()
        {
            if (State != BattleState.PlayerCommandPhase)
            {
                return;
            }

            if (EventSystem.current != null &&
                EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                HandleCellClick(Input.mousePosition);
            }
        }

        public void StartBattle()
        {
            State = BattleState.PlayerCommandPhase;
            OnCommandStateChanged?.Invoke();
        }

        public void EndPlayerCommandPhase()
        {
            if (State != BattleState.PlayerCommandPhase)
            {
                return;
            }

            State = BattleState.ExecutionPhase;
            OnCommandStateChanged?.Invoke();
            StartCoroutine(RunExecutionSequence());
        }

        public void SelectUnit(UnitData unit)
        {
            if (unit == null || unit.faction != Faction.Player)
            {
                selectedUnit = null;
                OnCommandStateChanged?.Invoke();
                return;
            }
            selectedUnit = unit;
            OnCommandStateChanged?.Invoke();
        }

        public bool TryMoveSelectedUnit(int targetX, int targetY)
        {
            if (State != BattleState.PlayerCommandPhase ||
                selectedUnit == null)
            {
                return false;
            }

            UnitDefinition definition =
                gameManager != null
                    ? gameManager.GetUnitDefinition(GetUnitTypeId(selectedUnit))
                    : null;
            return TryMoveUnit(selectedUnit, targetX, targetY, definition);
        }

        private IEnumerator InitializeBattleAfterMapView()
        {
            yield return null;

            ResolveReferences();
            if (mapView == null || mapView.Grid == null)
            {
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(loadMapName))
            {
                try
                {
                    MapSaveData saveData =
                        MapSerializer.Load(loadMapName);
                    mapView.LoadMap(saveData);
                }
                catch (System.Exception exception)
                {
                    Debug.LogWarning(
                        "指定されたマップの読込に失敗しました: " +
                        loadMapName + " / " + exception.Message);
                }
            }

            unitManager = new UnitManager(mapView.Grid);
            if (unitBoardView != null)
            {
                unitBoardView.Initialize(unitManager);
            }

            if (spawnTestUnitsOnStart)
            {
                SpawnTestUnits();
            }
        }

        private IEnumerator RunExecutionSequence()
        {
            yield return null;

            ExecutePlannedMoves();
            yield return null;

            State = BattleState.ReconAndReportPhase;
            yield return null;

            State = BattleState.CheckVictoryPhase;
            yield return null;

            CheckVictory();
            if (State == BattleState.BattleEnded)
            {
                yield break;
            }
            turnNumber++;
            ResetRemainingMovePoints();
            State = BattleState.PlayerCommandPhase;
        }

        private void ExecutePlannedMoves()
        {
            if (unitManager == null)
            {
                return;
            }

            var planningUnits = new List<UnitData>(unitManager.Units.Count);
            foreach (UnitData unit in unitManager.Units)
            {
                if (unit != null &&
                    unit.IsAlive &&
                    HasMoveOrder(unit, out Order moveOrder))
                {
                    planningUnits.Add(unit);
                }
            }

            foreach (UnitData unit in planningUnits)
            {
                if (!HasMoveOrder(unit, out Order moveOrder))
                {
                    continue;
                }
                UnitDefinition definition =
                    gameManager != null
                        ? gameManager.GetUnitDefinition(GetUnitTypeId(unit))
                        : null;

                HexGrid grid = mapView.Grid;
                List<Vector2Int> path =
                    moveOrder.path != null
                        ? moveOrder.path
                        : new List<Vector2Int>();
                if (path.Count < 2)
                {
                    Debug.LogWarning(
                        "予定移動失敗: 保存された経路がありません");
                    unit.pendingOrders.Remove(moveOrder);
                    OnCommandStateChanged?.Invoke();
                    continue;
                }

                int consumedMoveCost = 0;
                int fromX = unit.x;
                int fromY = unit.y;

                for (int i = 1; i < path.Count; i++)
                {
                    Vector2Int step = path[i];
                    UnitData occupant = unitManager.GetUnitAt(step.x, step.y);
                    if (occupant != null)
                    {
                        if (occupant.faction == unit.faction)
                        {
                            HexCell friendlyPassCell = grid.GetCell(step.x, step.y);
                            TerrainDefinition friendlyPassTerrain =
                                grid.GetTerrainDefinition(friendlyPassCell.terrainId);
                            int friendlyPassCost = definition != null
                                ? definition.GetMoveCost(friendlyPassTerrain)
                                : grid.GetMoveCost(step.x, step.y);
                            if (friendlyPassCost < 0 ||
                                consumedMoveCost + friendlyPassCost >
                                    unit.remainingMovePoints)
                            {
                                break;
                            }
                            consumedMoveCost += friendlyPassCost;
                            continue;
                        }
                        Debug.LogWarning(
                            string.Format(
                                "予定移動中断: マスが占有されています ({0},{1})",
                                step.x,
                                step.y));
                        break;
                    }

                    HexCell cell = grid.GetCell(step.x, step.y);
                    TerrainDefinition terrain =
                        grid.GetTerrainDefinition(cell.terrainId);
                    int moveCost = definition != null
                        ? definition.GetMoveCost(terrain)
                        : grid.GetMoveCost(step.x, step.y);
                    if (moveCost < 0)
                    {
                        Debug.LogWarning(
                            string.Format(
                                "予定移動中断: 進入不可の地形 ({0},{1})",
                                step.x,
                                step.y));
                        break;
                    }
                    if (consumedMoveCost + moveCost > unit.remainingMovePoints)
                    {
                        Debug.LogWarning(
                            string.Format(
                                "予定移動中断: 移動コスト超過 必要={0}, 残り={1}",
                                consumedMoveCost + moveCost,
                                unit.remainingMovePoints));
                        break;
                    }

                    if (!unitManager.MoveUnit(unit, step.x, step.y))
                    {
                        break;
                    }
                    consumedMoveCost += moveCost;
                }

                unit.remainingMovePoints -= consumedMoveCost;
                unit.pendingOrders.Remove(moveOrder);
                OnCommandStateChanged?.Invoke();
                Debug.Log(
                    string.Format(
                        "予定移動実行: ({0},{1}) -> ({2},{3}), 消費コスト={4}, 残り移動力={5}",
                        fromX,
                        fromY,
                        unit.x,
                        unit.y,
                        consumedMoveCost,
                        unit.remainingMovePoints));
            }
        }

        private void ResetRemainingMovePoints()
        {
            if (unitManager == null)
            {
                return;
            }

            foreach (UnitData unit in unitManager.Units)
            {
                if (!unit.IsAlive)
                {
                    continue;
                }

                UnitDefinition definition =
                    gameManager != null
                        ? gameManager.GetUnitDefinition(GetUnitTypeId(unit))
                        : null;
                unit.remainingMovePoints =
                    definition != null ? definition.movePoints : 0;
                ClearMoveOrders(unit);
            }
        }

        private void CheckVictory()
        {
            bool enemyHeadquartersAlive = false;
            bool playerHeadquartersAlive = false;
            bool enemyAlive = false;
            bool playerAlive = false;

            foreach (UnitData unit in unitManager.Units)
            {
                if (!unit.IsAlive)
                {
                    continue;
                }

                UnitDefinition definition =
                    gameManager != null
                        ? gameManager.GetUnitDefinition(GetUnitTypeId(unit))
                        : null;
                bool isHeadquarters =
                    definition != null && definition.commandLevel >= 2;

                if (unit.faction == Faction.Player)
                {
                    playerAlive = true;
                    if (isHeadquarters)
                    {
                        playerHeadquartersAlive = true;
                    }
                }
                else if (unit.faction == Faction.Enemy)
                {
                    enemyAlive = true;
                    if (isHeadquarters)
                    {
                        enemyHeadquartersAlive = true;
                    }
                }
            }

            if (!enemyAlive || !enemyHeadquartersAlive)
            {
                Debug.Log("Player victory.");
                State = BattleState.BattleEnded;
                OnCommandStateChanged?.Invoke();
            }
            else if (!playerAlive || !playerHeadquartersAlive)
            {
                Debug.Log("Player defeat.");
                State = BattleState.BattleEnded;
                OnCommandStateChanged?.Invoke();
            }
        }

        private void SpawnTestUnits()
        {
            if (unitManager == null)
            {
                return;
            }

            SpawnTestUnit(
                Faction.Player,
                UnitTypeIds.Headquarters,
                "Player Headquarters");
            SpawnTestUnit(
                Faction.Player,
                UnitTypeIds.Infantry,
                "Player Infantry");
            SpawnTestUnit(
                Faction.Player,
                UnitTypeIds.Cavalry,
                "Player Cavalry");

            SpawnTestUnit(
                Faction.Enemy,
                UnitTypeIds.Headquarters,
                "Enemy Headquarters");
            SpawnTestUnit(
                Faction.Enemy,
                UnitTypeIds.Infantry,
                "Enemy Infantry");
            SpawnTestUnit(
                Faction.Enemy,
                UnitTypeIds.Cavalry,
                "Enemy Cavalry");
        }

        private void SpawnTestUnit(
            Faction faction,
            string unitTypeId,
            string unitName)
        {
            Vector2Int position = FindSpawnPosition(faction);
            if (position.x < 0)
            {
                return;
            }

            UnitDefinition definition =
                gameManager != null
                    ? gameManager.GetUnitDefinition(unitTypeId)
                    : null;
            int maxManpower = definition != null
                ? definition.maxManpower
                : 3000;

            var unit = new UnitData
            {
                unitId = System.Guid.NewGuid().ToString(),
                unitName = unitName,
                faction = faction,
                x = position.x,
                y = position.y,
                organization = 1000,
                maxOrganization = 1000,
                supplyStatus = 1000,
                remainingMovePoints =
                    definition != null ? definition.movePoints : 0
            };
            unit.battalions.Add(new BattalionData
            {
                unitTypeId = unitTypeId,
                maxManpower = maxManpower,
                currentManpower = maxManpower,
                equipmentRatio = 1000
            });

            unitManager.AddUnit(unit);
        }

        private Vector2Int FindSpawnPosition(Faction faction)
        {
            HexGrid grid = mapView.Grid;
            int zoneFaction = faction == Faction.Player ? 1 : 2;

            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    HexCell cell = grid.GetCell(x, y);
                    if (cell.deployZoneFaction == zoneFaction &&
                        unitManager.GetUnitAt(x, y) == null)
                    {
                        return new Vector2Int(x, y);
                    }
                }
            }

            int leftLimit = Mathf.Min(3, grid.Width);
            int rightStart = Mathf.Max(0, grid.Width - 3);
            int startX = faction == Faction.Player ? 0 : rightStart;
            int endX = faction == Faction.Player
                ? leftLimit
                : grid.Width;

            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = startX; x < endX; x++)
                {
                    if (unitManager.GetUnitAt(x, y) == null)
                    {
                        return new Vector2Int(x, y);
                    }
                }
            }

            return new Vector2Int(-1, -1);
        }

        private bool TryMoveUnit(
            UnitData unit,
            int targetX,
            int targetY,
            UnitDefinition definition)
        {
            HexGrid grid = mapView.Grid;

            if (unit.x == targetX && unit.y == targetY)
            {
                ClearMoveOrders(unit);
                OnCommandStateChanged?.Invoke();
                Debug.Log(
                    string.Format(
                        "移動予定キャンセル: ({0},{1})",
                        unit.x,
                        unit.y));
                return true;
            }

            if (!grid.IsInBounds(targetX, targetY))
            {
                Debug.LogWarning(
                    string.Format(
                        "移動失敗: 範囲外 ({0},{1}) -> ({2},{3}), 残り移動力={4}",
                        unit.x,
                        unit.y,
                        targetX,
                        targetY,
                        unit.remainingMovePoints));
                return false;
            }

            UnitData occupant = unitManager.GetUnitAt(targetX, targetY);
            if (occupant != null)
            {
                Debug.LogWarning(
                    string.Format(
                        "移動失敗: 他のユニットが占有中 ({0},{1}) -> ({2},{3}), 残り移動力={4}",
                        unit.x,
                        unit.y,
                        targetX,
                        targetY,
                        unit.remainingMovePoints));
                return false;
            }

            List<Vector2Int> path =
                grid.FindPath(
                    unit.x,
                    unit.y,
                    targetX,
                    targetY,
                    unit.faction,
                    definition,
                    unitManager);
            if (path == null || path.Count < 2)
            {
                Debug.LogWarning(
                    string.Format(
                        "移動失敗: 経路が見つからない ({0},{1}) -> ({2},{3}), 残り移動力={4}",
                        unit.x,
                        unit.y,
                        targetX,
                        targetY,
                        unit.remainingMovePoints));
                return false;
            }

            int totalMoveCost = 0;
            for (int i = 1; i < path.Count; i++)
            {
                Vector2Int step = path[i];

                HexCell cell = grid.GetCell(step.x, step.y);
                TerrainDefinition terrain =
                    grid.GetTerrainDefinition(cell.terrainId);
                int cost = definition != null
                    ? definition.GetMoveCost(terrain)
                    : grid.GetMoveCost(step.x, step.y);
                if (cost < 0)
                {
                    Debug.LogWarning(
                        string.Format(
                            "移動失敗: 進入不可の地形 ({0},{1}) -> ({2},{3}), 経路マス=({4},{5}), コスト={6}, 残り移動力={7}",
                            unit.x,
                            unit.y,
                            targetX,
                            targetY,
                            step.x,
                            step.y,
                            cost,
                            unit.remainingMovePoints));
                    return false;
                }
                totalMoveCost += cost;
            }

            int movePoints = definition != null ? definition.movePoints : 0;
            if (movePoints <= 0 || totalMoveCost > movePoints)
            {
                Debug.LogWarning(
                    string.Format(
                        "移動予約失敗: 移動コスト超過 ({0},{1}) -> ({2},{3}), 必要コスト={4}, 移動力={5}",
                        unit.x,
                        unit.y,
                        targetX,
                        targetY,
                        totalMoveCost,
                        movePoints));
                return false;
            }

            foreach (UnitData otherUnit in unitManager.Units)
            {
                if (otherUnit == unit ||
                    otherUnit.faction != unit.faction)
                {
                    continue;
                }
                if (HasMoveOrder(otherUnit, out Order otherMove) &&
                    otherMove.targetX == targetX &&
                    otherMove.targetY == targetY)
                {
                    Debug.LogWarning(
                        "移動予約失敗: 他の自軍ユニットが既に移動予定先にしています (" +
                        targetX + "," + targetY + ")");
                    return false;
                }
            }

            ClearMoveOrders(unit);
            var moveOrder = new Order
            {
                orderType = OrderType.Move,
                targetX = targetX,
                targetY = targetY,
                remainingDelayTurns = 0
            };
            moveOrder.path.AddRange(path);
            unit.pendingOrders.Add(moveOrder);
            OnCommandStateChanged?.Invoke();
            Debug.Log(
                string.Format(
                    "移動予約: ({0},{1}) -> ({2},{3}), 必要コスト={4}",
                    unit.x,
                    unit.y,
                    targetX,
                    targetY,
                    totalMoveCost));
            return true;
        }

        private void HandleCellClick(Vector2 screenPosition)
        {
            if (mapView == null || battleCamera == null)
            {
                return;
            }

            Tilemap tilemap = mapView.GetComponent<Tilemap>();
            if (tilemap == null)
            {
                return;
            }

            Vector3 worldPosition =
                battleCamera.ScreenToWorldPoint(
                    new Vector3(screenPosition.x, screenPosition.y, battleCamera.nearClipPlane));
            Vector3Int cellPosition = tilemap.WorldToCell(worldPosition);

            foreach (UnitData plannedUnit in unitManager.Units)
            {
                if (plannedUnit != null &&
                    plannedUnit.faction == Faction.Player &&
                    HasMoveOrder(plannedUnit, out Order plannedMove) &&
                    plannedMove.targetX == cellPosition.x &&
                    plannedMove.targetY == cellPosition.y)
                {
                    SelectUnit(plannedUnit);
                    return;
                }
            }

            UnitData clickedUnit =
                unitManager.GetUnitAt(cellPosition.x, cellPosition.y);
            if (clickedUnit != null)
            {
                if (clickedUnit.faction == Faction.Player)
                {
                    SelectUnit(clickedUnit);
                }
                return;
            }

            TryMoveSelectedUnit(cellPosition.x, cellPosition.y);
        }

        public bool HasMoveOrder(UnitData unit, out Order moveOrder)
        {
            moveOrder = null;
            if (unit == null || unit.pendingOrders == null)
            {
                return false;
            }

            for (int i = 0; i < unit.pendingOrders.Count; i++)
            {
                Order order = unit.pendingOrders[i];
                if (order != null && order.orderType == OrderType.Move)
                {
                    moveOrder = order;
                    return true;
                }
            }
            return false;
        }

        public void ClearMoveOrders(UnitData unit)
        {
            if (unit == null || unit.pendingOrders == null)
            {
                return;
            }

            for (int i = unit.pendingOrders.Count - 1; i >= 0; i--)
            {
                Order order = unit.pendingOrders[i];
                if (order != null && order.orderType == OrderType.Move)
                {
                    unit.pendingOrders.RemoveAt(i);
                }
            }
        }

        private void ResolveReferences()
        {
            if (mapView == null)
            {
                mapView = FindObjectOfType<HexTilemapView>();
            }
            if (unitBoardView == null)
            {
                unitBoardView = FindObjectOfType<UnitBoardView>();
            }
            if (gameManager == null)
            {
                gameManager = FindObjectOfType<GameManager>();
            }
            if (battleCamera == null)
            {
                battleCamera = Camera.main;
            }
        }

        private static string GetUnitTypeId(UnitData unit)
        {
            if (unit == null || unit.battalions == null ||
                unit.battalions.Count == 0 || unit.battalions[0] == null)
            {
                return null;
            }
            return unit.battalions[0].unitTypeId;
        }
    }
}
