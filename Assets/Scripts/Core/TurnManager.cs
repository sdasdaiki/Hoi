using System.Collections;
using System.Collections.Generic;
using EdoStrategy.Command;
using EdoStrategy.Map;
using EdoStrategy.UI;
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

        private BattleCameraController battleCameraController;
        public event System.Action OnCommandStateChanged;
        public event System.Action<int, int, List<UnitData>>
            OnCellUnitsSelected;

        private UnitManager unitManager;
        private UnitData selectedUnit;
        private readonly List<UnitData> selectedUnits =
            new List<UnitData>();
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
        public IReadOnlyList<UnitData> SelectedUnits
        {
            get { return selectedUnits; }
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

            if (Input.GetMouseButtonDown(0) &&
                !Input.GetMouseButton(2) &&
                (battleCameraController == null ||
                 !battleCameraController.IsPanning))
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
            if (unit == null)
            {
                SelectUnits(null);
                return;
            }

            SelectUnits(new[] { unit });
        }

        public void SelectUnits(IEnumerable<UnitData> units)
        {
            selectedUnits.Clear();
            if (units != null)
            {
                foreach (UnitData unit in units)
                {
                    if (unit != null &&
                        unit.faction == Faction.Player &&
                        unit.IsAlive &&
                        !selectedUnits.Contains(unit))
                    {
                        selectedUnits.Add(unit);
                    }
                }
            }

            selectedUnit = selectedUnits.Count > 0
                ? selectedUnits[0]
                : null;
            OnCommandStateChanged?.Invoke();
        }

        public void ToggleUnitSelection(UnitData unit)
        {
            if (unit == null ||
                unit.faction != Faction.Player ||
                !unit.IsAlive)
            {
                return;
            }

            if (selectedUnits.Contains(unit))
            {
                selectedUnits.Remove(unit);
            }
            else
            {
                selectedUnits.Add(unit);
            }

            selectedUnit = selectedUnits.Count > 0
                ? selectedUnits[0]
                : null;
            OnCommandStateChanged?.Invoke();
        }

        public bool IsUnitSelected(UnitData unit)
        {
            return unit != null && selectedUnits.Contains(unit);
        }

        public bool TryMoveSelectedUnit(int targetX, int targetY)
        {
            if (State != BattleState.PlayerCommandPhase ||
                selectedUnits.Count == 0)
            {
                return false;
            }

            bool anyIssued = false;
            foreach (UnitData unit in new List<UnitData>(selectedUnits))
            {
                if (IssueMoveOrder(unit, targetX, targetY))
                {
                    anyIssued = true;
                }
            }
            return anyIssued;
        }

        public bool IssueMoveOrder(UnitData unit, int targetX, int targetY)
        {
            if (unit == null ||
                unit.faction != Faction.Player ||
                State != BattleState.PlayerCommandPhase)
            {
                return false;
            }

            UnitDefinition definition =
                gameManager != null
                    ? gameManager.GetUnitDefinition(GetUnitTypeId(unit))
                    : null;
            return TryMoveUnit(unit, targetX, targetY, definition);
        }

        public void CancelOrder(UnitData unit, OrderType orderType)
        {
            if (unit == null || unit.pendingOrders == null)
            {
                return;
            }

            bool removed = false;
            for (int i = unit.pendingOrders.Count - 1; i >= 0; i--)
            {
                Order order = unit.pendingOrders[i];
                if (order != null && order.orderType == orderType)
                {
                    unit.pendingOrders.RemoveAt(i);
                    removed = true;
                }
            }
            if (removed)
            {
                OnCommandStateChanged?.Invoke();
            }
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

            bool movedUnits = ExecutePlannedMoves();
            if (movedUnits)
            {
                if (unitBoardView != null &&
                    unitBoardView.IsAnimatingUnits)
                {
                    while (unitBoardView.IsAnimatingUnits)
                    {
                        yield return null;
                    }
                }
                else
                {
                    yield return new WaitForSeconds(0.35f);
                }
            }
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

        private bool ExecutePlannedMoves()
        {
            if (unitManager == null)
            {
                return false;
            }

            bool movedUnits = false;
            for (int pass = 0; pass <= unitManager.Units.Count; pass++)
            {
                bool anyMoved = false;

                foreach (UnitData unit in new List<UnitData>(unitManager.Units))
                {
                    if (!unit.IsAlive ||
                        !HasMoveOrder(unit, out Order moveOrder))
                    {
                        continue;
                    }
                    if (moveOrder.remainingDelayTurns != 0)
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
                        unit.pendingOrders.Remove(moveOrder);
                        OnCommandStateChanged?.Invoke();
                        continue;
                    }

                    int consumedMoveCost = 0;
                    int fromX = unit.x;
                    int fromY = unit.y;
                    var traversedPath = new List<Vector2Int>
                    {
                        new Vector2Int(fromX, fromY)
                    };

                    for (int i = 1; i < path.Count; i++)
                    {
                        Vector2Int step = path[i];
                        List<UnitData> occupants =
                            unitManager.GetUnitsAt(step.x, step.y);
                        bool blockedByEnemy = false;
                        for (int j = 0; j < occupants.Count; j++)
                        {
                            if (occupants[j].faction != unit.faction)
                            {
                                blockedByEnemy = true;
                                break;
                            }
                        }
                        if (blockedByEnemy)
                        {
                            break;
                        }

                        HexCell cell = grid.GetCell(step.x, step.y);
                        TerrainDefinition terrain =
                            grid.GetTerrainDefinition(cell.terrainId);
                        int moveCost = definition != null
                            ? definition.GetMoveCost(terrain)
                            : grid.GetMoveCost(step.x, step.y);
                        if (moveCost < 0 ||
                            consumedMoveCost + moveCost >
                                unit.remainingMovePoints)
                        {
                            break;
                        }

                        consumedMoveCost += moveCost;
                        traversedPath.Add(step);
                    }

                    bool movedThisPass =
                        traversedPath.Count > 1 &&
                        unitManager.MoveUnitAlongPath(
                            unit,
                            traversedPath[traversedPath.Count - 1].x,
                            traversedPath[traversedPath.Count - 1].y,
                            traversedPath);
                    if (!movedThisPass)
                    {
                        continue;
                    }

                    unit.remainingMovePoints -= consumedMoveCost;
                    anyMoved = true;
                    movedUnits = true;
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

                if (!anyMoved)
                {
                    break;
                }
            }
            return movedUnits;
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
                        !HasEnemyAt(x, y, faction))
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
                    if (!HasEnemyAt(x, y, faction))
                    {
                        return new Vector2Int(x, y);
                    }
                }
            }

            return new Vector2Int(-1, -1);
        }

        private bool HasEnemyAt(int x, int y, Faction faction)
        {
            if (unitManager == null)
            {
                return false;
            }

            List<UnitData> occupants = unitManager.GetUnitsAt(x, y);
            for (int i = 0; i < occupants.Count; i++)
            {
                if (occupants[i].faction != faction)
                {
                    return true;
                }
            }
            return false;
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

            if (HasEnemyAt(targetX, targetY, unit.faction))
            {
                Debug.LogWarning(
                    string.Format(
                        "移動失敗: 敵部隊が占有中 ({0},{1}) -> ({2},{3}), 残り移動力={4}",
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

            List<UnitData> clickedUnits =
                unitManager.GetUnitsAt(cellPosition.x, cellPosition.y);
            OnCellUnitsSelected?.Invoke(
                cellPosition.x,
                cellPosition.y,
                clickedUnits);
            if (clickedUnits.Count > 0)
            {
                UnitData onlyPlayerUnit = null;
                int playerUnitCount = 0;
                for (int i = 0; i < clickedUnits.Count; i++)
                {
                    if (clickedUnits[i].faction == Faction.Player)
                    {
                        onlyPlayerUnit = clickedUnits[i];
                        playerUnitCount++;
                    }
                }
                if (playerUnitCount == 1)
                {
                    SelectUnit(onlyPlayerUnit);
                }
                return;
            }

            if (selectedUnits.Count > 0 &&
                !TryMoveSelectedUnit(
                    cellPosition.x,
                    cellPosition.y))
            {
                SelectUnit(null);
            }
        }

        public bool HasMoveOrder(UnitData unit, out Order moveOrder)
        {
            moveOrder = null;
            if (unit == null || unit.pendingOrders == null)
            {
                return false;
            }

            if (unit.pendingOrders.Count == 0)
            {
                return false;
            }

            Order order = unit.pendingOrders[0];
            if (order != null && order.orderType == OrderType.Move)
            {
                moveOrder = order;
                return true;
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
            if (battleCamera != null)
            {
                battleCameraController =
                    battleCamera.GetComponent<BattleCameraController>();
                if (battleCameraController == null)
                {
                    battleCameraController =
                        battleCamera.gameObject
                            .AddComponent<BattleCameraController>();
                }
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
