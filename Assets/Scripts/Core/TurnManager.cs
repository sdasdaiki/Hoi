using System.Collections;
using System.Collections.Generic;
using EdoStrategy.Combat;
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
        public enum CommandInputMode
        {
            Normal,
            SelectAmbushTarget,
            SelectAssaultMoveTarget,
            SelectAssaultAttackTarget
        }

        private sealed class PendingAssaultMove
        {
            public UnitData unit;
            public List<Vector2Int> path;
        }

        private sealed class PlannedMove
        {
            public UnitData unit;
            public Order order;
            public UnitDefinition definition;
            public List<Vector2Int> path;
            public List<Vector2Int> traversedPath;
            public int nextPathIndex = 1;
            public int consumedMoveCost;
            public int movePoints;
            public int startX;
            public int startY;
            public bool stopped;
            public bool moved;
        }

        private sealed class MoveProposal
        {
            public PlannedMove move;
            public Vector2Int destination;
            public int stepCost;
            public int cumulativeCost;
            public int remainingCost;
            public int nearestHeadquartersDistance;
        }

        private sealed class AttackEntry
        {
            public UnitData attacker;
            public Order order;
            public UnitDefinition attackerDefinition;
            public int attackRange;
            public int sequence;
        }

        private sealed class ResolvedAttack
        {
            public UnitData attacker;
            public int attackerX;
            public int attackerY;
            public Faction defenderFaction;
            public int targetX;
            public int targetY;
        }

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
        private readonly List<PendingAssaultMove> pendingAssaultMoves =
            new List<PendingAssaultMove>();
        private readonly List<string> combatLog = new List<string>();
        private int turnNumber = 1;

        public event System.Action<bool> OnBattleEnded;
        public event System.Action<string> OnCombatLogUpdated;
        public BattleState State { get; private set; }
        public CommandInputMode InputMode { get; private set; }
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
        public IReadOnlyList<string> CombatLog
        {
            get { return combatLog; }
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

            if (Input.GetKeyDown(KeyCode.H) &&
                !BattleUI.IsInputFieldFocused())
            {
                ClearAllOrdersForSelectedUnits();
                CancelCommandInputMode();
                return;
            }

            if (EventSystem.current != null &&
                EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            if (battleCameraController != null &&
                battleCameraController.IsPanning)
            {
                return;
            }

            if (Input.GetMouseButtonDown(1) &&
                InputMode != CommandInputMode.Normal)
            {
                HandleCommandInputModeClick(Input.mousePosition);
                return;
            }

            if (Input.GetMouseButtonDown(0) &&
                !Input.GetMouseButton(2))
            {
                HandleCellSelectionClick(Input.mousePosition);
            }
            else if (Input.GetMouseButtonDown(1))
            {
                HandleCellOrderClick(Input.mousePosition);
            }
        }

        public void StartBattle()
        {
            ResetCommandInputMode();
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
            ResetCommandInputMode();
            selectedUnit = null;
            selectedUnits.Clear();
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

            NotifySelectionChanged();
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

            NotifySelectionChanged();
        }

        public void ToggleCellUnitSelection(IEnumerable<UnitData> unitsAtCell)
        {
            var playerUnits = new List<UnitData>();
            if (unitsAtCell != null)
            {
                foreach (UnitData unit in unitsAtCell)
                {
                    if (unit != null &&
                        unit.faction == Faction.Player &&
                        unit.IsAlive &&
                        !playerUnits.Contains(unit))
                    {
                        playerUnits.Add(unit);
                    }
                }
            }
            if (playerUnits.Count == 0)
            {
                return;
            }

            bool allSelected = true;
            for (int i = 0; i < playerUnits.Count; i++)
            {
                if (!selectedUnits.Contains(playerUnits[i]))
                {
                    allSelected = false;
                    break;
                }
            }

            for (int i = 0; i < playerUnits.Count; i++)
            {
                UnitData unit = playerUnits[i];
                if (allSelected)
                {
                    selectedUnits.Remove(unit);
                }
                else if (!selectedUnits.Contains(unit))
                {
                    selectedUnits.Add(unit);
                }
            }
            NotifySelectionChanged();
        }

        public void ClearAllOrdersForSelectedUnits()
        {
            if (selectedUnits.Count == 0)
            {
                return;
            }

            for (int i = 0; i < selectedUnits.Count; i++)
            {
                UnitData unit = selectedUnits[i];
                if (unit != null)
                {
                    if (unit.pendingOrders != null)
                    {
                        unit.pendingOrders.Clear();
                    }
                    unit.isAmbushed = false;
                }
            }
            OnCommandStateChanged?.Invoke();
        }

        private void NotifySelectionChanged()
        {
            ResetCommandInputMode();
            selectedUnit = selectedUnits.Count > 0
                ? selectedUnits[0]
                : null;
            OnCommandStateChanged?.Invoke();
        }

        public void BeginAmbushOrderMode()
        {
            if (State != BattleState.PlayerCommandPhase ||
                selectedUnits.Count == 0)
            {
                return;
            }

            pendingAssaultMoves.Clear();
            InputMode = CommandInputMode.SelectAmbushTarget;
            OnCommandStateChanged?.Invoke();
        }

        public void BeginAssaultOrderMode()
        {
            if (State != BattleState.PlayerCommandPhase ||
                selectedUnits.Count == 0)
            {
                return;
            }

            pendingAssaultMoves.Clear();
            InputMode = CommandInputMode.SelectAssaultMoveTarget;
            OnCommandStateChanged?.Invoke();
        }

        public void CancelCommandInputMode()
        {
            ResetCommandInputMode();
            OnCommandStateChanged?.Invoke();
        }

        public bool IssueDefendOrderToSelected()
        {
            ResetCommandInputMode();
            return TryDefendSelectedUnits();
        }

        public bool IssueEntrenchOrderToSelected()
        {
            ResetCommandInputMode();
            return TryEntrenchSelectedUnits();
        }

        private void ResetCommandInputMode()
        {
            InputMode = CommandInputMode.Normal;
            pendingAssaultMoves.Clear();
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
                !unit.IsAlive ||
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

        public bool IssueDefendOrder(UnitData unit)
        {
            return IssueStationaryOrder(unit, OrderType.Defend, -1, -1);
        }

        public bool IssueEntrenchOrder(UnitData unit)
        {
            return IssueStationaryOrder(unit, OrderType.Entrench, -1, -1);
        }

        public bool IssueAmbushOrder(
            UnitData unit,
            int watchedX,
            int watchedY)
        {
            HexGrid grid = mapView != null ? mapView.Grid : null;
            if (grid == null ||
                !grid.IsInBounds(watchedX, watchedY) ||
                unit == null)
            {
                return false;
            }

            if (!CombatResolver.IsAdjacent(
                    unit.x,
                    unit.y,
                    watchedX,
                    watchedY))
            {
                return false;
            }

            return IssueStationaryOrder(
                unit,
                OrderType.Ambush,
                watchedX,
                watchedY);
        }

        public bool IssueAssaultOrder(
            UnitData unit,
            int moveTargetX,
            int moveTargetY,
            int assaultTargetX,
            int assaultTargetY)
        {
            if (unit == null ||
                State != BattleState.PlayerCommandPhase ||
                !unit.IsAlive ||
                unit.faction != Faction.Player ||
                !HasLivingEnemyAt(
                    assaultTargetX,
                    assaultTargetY,
                    unit.faction))
            {
                return false;
            }

            if (!CombatResolver.IsAdjacent(
                    moveTargetX,
                    moveTargetY,
                    assaultTargetX,
                    assaultTargetY))
            {
                return false;
            }

            UnitDefinition definition = GetUnitDefinition(unit);
            if (definition == null)
            {
                return false;
            }

            bool isAlreadyAtTarget =
                unit.x == moveTargetX && unit.y == moveTargetY;
            if (isAlreadyAtTarget)
            {
                ClearActionOrders(unit);
                if (unit.pendingOrders == null)
                {
                    unit.pendingOrders = new List<Order>();
                }
                var stationaryAssault = new Order
                {
                    orderType = OrderType.Assault,
                    targetX = moveTargetX,
                    targetY = moveTargetY,
                    secondaryTargetX = assaultTargetX,
                    secondaryTargetY = assaultTargetY,
                    remainingDelayTurns = 0
                };
                stationaryAssault.path.Add(
                    new Vector2Int(unit.x, unit.y));
                stationaryAssault.path.Add(
                    new Vector2Int(unit.x, unit.y));
                unit.pendingOrders.Add(stationaryAssault);
                OnCommandStateChanged?.Invoke();
                return true;
            }

            if (!TryMoveUnit(unit, moveTargetX, moveTargetY, definition))
            {
                return false;
            }

            Order order;
            if (!HasMoveOrder(unit, out order))
            {
                return false;
            }

            order.orderType = OrderType.Assault;
            order.secondaryTargetX = assaultTargetX;
            order.secondaryTargetY = assaultTargetY;
            OnCommandStateChanged?.Invoke();
            return true;
        }

        public bool TryDefendSelectedUnits()
        {
            return IssueStationaryOrderForSelection(OrderType.Defend, -1, -1);
        }

        public bool TryEntrenchSelectedUnits()
        {
            return IssueStationaryOrderForSelection(OrderType.Entrench, -1, -1);
        }

        public bool TryAmbushSelectedUnits(int watchedX, int watchedY)
        {
            bool issued = false;
            foreach (UnitData unit in new List<UnitData>(selectedUnits))
            {
                if (IssueAmbushOrder(unit, watchedX, watchedY))
                {
                    issued = true;
                }
            }
            return issued;
        }

        public bool TryAssaultSelectedUnits(
            int moveTargetX,
            int moveTargetY,
            int assaultTargetX,
            int assaultTargetY)
        {
            bool issued = false;
            foreach (UnitData unit in new List<UnitData>(selectedUnits))
            {
                if (IssueAssaultOrder(
                        unit,
                        moveTargetX,
                        moveTargetY,
                        assaultTargetX,
                        assaultTargetY))
                {
                    issued = true;
                }
            }
            return issued;
        }

        private bool IssueStationaryOrder(
            UnitData unit,
            OrderType orderType,
            int secondaryTargetX,
            int secondaryTargetY)
        {
            if (unit == null ||
                State != BattleState.PlayerCommandPhase ||
                !unit.IsAlive ||
                unit.faction != Faction.Player)
            {
                return false;
            }

            ClearActionOrders(unit);
            if (unit.pendingOrders == null)
            {
                unit.pendingOrders = new List<Order>();
            }
            unit.isAmbushed = orderType == OrderType.Ambush;
            unit.pendingOrders.Add(new Order
            {
                orderType = orderType,
                targetX = unit.x,
                targetY = unit.y,
                secondaryTargetX = secondaryTargetX,
                secondaryTargetY = secondaryTargetY,
                remainingDelayTurns = 0
            });
            OnCommandStateChanged?.Invoke();
            return true;
        }

        private bool IssueStationaryOrderForSelection(
            OrderType orderType,
            int secondaryTargetX,
            int secondaryTargetY)
        {
            bool issued = false;
            foreach (UnitData unit in new List<UnitData>(selectedUnits))
            {
                if (IssueStationaryOrder(
                        unit,
                        orderType,
                        secondaryTargetX,
                        secondaryTargetY))
                {
                    issued = true;
                }
            }
            return issued;
        }

        public bool TryAttackSelectedUnits(UnitData target)
        {
            if (State != BattleState.PlayerCommandPhase ||
                selectedUnits.Count == 0 ||
                target == null ||
                !target.IsAlive ||
                target.faction != Faction.Enemy ||
                string.IsNullOrEmpty(target.unitId))
            {
                return false;
            }

            bool issuedAny = false;
            for (int i = 0; i < selectedUnits.Count; i++)
            {
                UnitData attacker = selectedUnits[i];
                UnitDefinition definition =
                    gameManager != null
                        ? gameManager.GetUnitDefinition(GetUnitTypeId(attacker))
                        : null;
                if (!CombatResolver.CanAttack(attacker, target, definition))
                {
                    continue;
                }

                if (attacker.pendingOrders == null)
                {
                    attacker.pendingOrders = new List<Order>();
                }
                attacker.pendingOrders.Clear();
                attacker.isAmbushed = false;
                attacker.pendingOrders.Add(new Order
                {
                    orderType = OrderType.Attack,
                    targetUnitId = target.unitId,
                    targetX = target.x,
                    targetY = target.y,
                    remainingDelayTurns = 0
                });
                issuedAny = true;
            }

            if (issuedAny)
            {
                OnCommandStateChanged?.Invoke();
            }
            return issuedAny;
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
                    if (orderType == OrderType.Ambush)
                    {
                        unit.isAmbushed = false;
                    }
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

            AdvancePendingOrderDelays();
            List<AttackEntry> readyAttacks = GetReadyAttackEntries();
            var engagedUnits = new HashSet<UnitData>();
            var pinnedUnits = new HashSet<UnitData>();
            var retreatOrigins = new Dictionary<UnitData, Vector2Int>();

            bool movedUnits = ResolveFirstCombatPhase(
                readyAttacks,
                engagedUnits,
                pinnedUnits,
                retreatOrigins);
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

            bool movementOccurred = ExecuteMovementSteps(
                pinnedUnits,
                engagedUnits,
                retreatOrigins);
            if (movementOccurred)
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

            bool assaultMovedUnits = ResolveAssaultAttacks(
                engagedUnits,
                retreatOrigins);
            if (assaultMovedUnits)
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

            CompleteEntrenchmentsAndOrders(engagedUnits);
            RecoverNonCombatUnits(engagedUnits);

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
            OnCommandStateChanged?.Invoke();
        }

        private void AdvancePendingOrderDelays()
        {
            if (unitManager == null)
            {
                return;
            }

            foreach (UnitData unit in unitManager.Units)
            {
                if (unit == null ||
                    !unit.IsAlive ||
                    unit.pendingOrders == null ||
                    unit.pendingOrders.Count == 0)
                {
                    continue;
                }

                Order order = unit.pendingOrders[0];
                if (order == null)
                {
                    continue;
                }

                if (order.remainingDelayTurns > 0)
                {
                    order.remainingDelayTurns--;
                }
                if (order.orderType == OrderType.Ambush &&
                    order.remainingDelayTurns <= 0)
                {
                    unit.isAmbushed = true;
                }
            }
        }

        private bool ResolveFirstCombatPhase(
            List<AttackEntry> attacks,
            HashSet<UnitData> engagedUnits,
            HashSet<UnitData> pinnedUnits,
            Dictionary<UnitData, Vector2Int> retreatOrigins)
        {
            if (unitManager == null)
            {
                return false;
            }

            var resolvedAttacks = new List<ResolvedAttack>();
            System.Func<string, UnitDefinition> definitionLookup =
                gameManager != null
                    ? new System.Func<string, UnitDefinition>(
                        gameManager.GetUnitDefinition)
                    : null;

            for (int i = 0; i < attacks.Count; i++)
            {
                AttackEntry attack = attacks[i];
                if (attack.attacker == null ||
                    !attack.attacker.IsAlive ||
                    !IsCurrentOrder(attack.attacker, attack.order))
                {
                    continue;
                }

                UnitData target =
                    unitManager.GetUnitById(attack.order.targetUnitId);
                int targetX = target != null
                    ? target.x
                    : attack.order.targetX;
                int targetY = target != null
                    ? target.y
                    : attack.order.targetY;
                List<UnitData> defenders = GetAttackDefenders(
                    targetX,
                    targetY,
                    attack.attacker.faction);
                if (defenders.Count == 0)
                {
                    AddCombatLog(
                        GetUnitLabel(attack.attacker) +
                        " の攻撃命令は、対象マスに敵がいないため不発となりました。");
                    RemoveOrder(attack.attacker, attack.order);
                    continue;
                }

                bool hasStationaryDefender = false;
                for (int j = 0; j < defenders.Count; j++)
                {
                    if (IsStationaryDefenseUnit(defenders[j]))
                    {
                        hasStationaryDefender = true;
                        break;
                    }
                }

                if (hasStationaryDefender)
                {
                    defenders.RemoveAll(defender =>
                        !IsStationaryDefenseUnit(defender));
                }
                else
                {
                    for (int j = 0; j < defenders.Count; j++)
                    {
                        pinnedUnits.Add(defenders[j]);
                    }
                }

                UnitData defender = SelectCombatDefender(defenders);
                if (defender == null ||
                    !CombatResolver.CanAttack(
                        attack.attacker,
                        defender,
                        attack.attackerDefinition))
                {
                    AddCombatLog(
                        GetUnitLabel(attack.attacker) +
                        " の攻撃命令は、対象が射程外のため実行されませんでした。");
                    RemoveOrder(attack.attacker, attack.order);
                    continue;
                }

                int combatX = defender.x;
                int combatY = defender.y;
                int attackerX = attack.attacker.x;
                int attackerY = attack.attacker.y;
                Faction defenderFaction = defender.faction;
                UnitDefinition defenderDefinition =
                    GetUnitDefinition(defender);
                bool defenderCurrentlyAttacking =
                    HasReadyAttackOrder(defender, attacks);
                UnitModifier defenderModifier =
                    GetDefenderOrderModifier(defender);
                CombatResult result = CombatResolver.ResolveCombat(
                    attack.attacker,
                    defender,
                    attack.attackerDefinition,
                    defenderDefinition,
                    mapView != null ? mapView.Grid : null,
                    unitManager,
                    definitionLookup,
                    defenderCurrentlyAttacking,
                    null,
                    defenderModifier,
                    1000,
                    true,
                    false);

                engagedUnits.Add(attack.attacker);
                engagedUnits.Add(defender);
                if (defender.isAmbushed)
                {
                    defender.isAmbushed = false;
                }
                resolvedAttacks.Add(new ResolvedAttack
                {
                    attacker = attack.attacker,
                    attackerX = attackerX,
                    attackerY = attackerY,
                    defenderFaction = defenderFaction,
                    targetX = combatX,
                    targetY = combatY
                });
                LogCombatResult(attack.attacker, defender, result);
                RemoveOrder(attack.attacker, attack.order);
            }

            bool movedUnits = ResolveDestroyedAndRoutedUnits(retreatOrigins);
            movedUnits |= AdvanceMeleeAttackers(resolvedAttacks);
            OnCommandStateChanged?.Invoke();
            return movedUnits;
        }

        private List<UnitData> GetAttackDefenders(
            int x,
            int y,
            Faction attackerFaction)
        {
            var defenders = new List<UnitData>();
            if (unitManager == null)
            {
                return defenders;
            }

            List<UnitData> occupants = unitManager.GetUnitsAt(x, y);
            for (int i = 0; i < occupants.Count; i++)
            {
                UnitData unit = occupants[i];
                if (unit != null &&
                    unit.IsAlive &&
                    unit.faction != attackerFaction)
                {
                    defenders.Add(unit);
                }
            }
            return defenders;
        }

        private static bool IsStationaryDefenseUnit(UnitData unit)
        {
            if (unit == null ||
                unit.pendingOrders == null ||
                unit.pendingOrders.Count == 0 ||
                unit.pendingOrders[0] == null)
            {
                return true;
            }

            OrderType orderType = unit.pendingOrders[0].orderType;
            return orderType == OrderType.Defend ||
                orderType == OrderType.Entrench ||
                orderType == OrderType.Ambush;
        }

        private UnitData SelectCombatDefender(List<UnitData> defenders)
        {
            UnitData selected = null;
            int highestOrganization = int.MinValue;
            int highestDefense = int.MinValue;
            for (int i = 0; i < defenders.Count; i++)
            {
                UnitData candidate = defenders[i];
                if (candidate == null || !candidate.IsAlive)
                {
                    continue;
                }

                UnitDefinition definition = GetUnitDefinition(candidate);
                int defense = definition != null
                    ? definition.baseDefense
                    : 0;
                if (selected == null ||
                    candidate.organization > highestOrganization ||
                    candidate.organization == highestOrganization &&
                    defense > highestDefense)
                {
                    selected = candidate;
                    highestOrganization = candidate.organization;
                    highestDefense = defense;
                }
            }
            return selected;
        }

        private UnitModifier GetDefenderOrderModifier(UnitData unit)
        {
            if (unit == null ||
                unit.pendingOrders == null ||
                unit.pendingOrders.Count == 0 ||
                unit.pendingOrders[0] == null)
            {
                return null;
            }

            switch (unit.pendingOrders[0].orderType)
            {
                case OrderType.Entrench:
                    return CombatResolver.EvaluateEntrenchModifier();
                case OrderType.Ambush:
                    return CombatResolver.EvaluateAmbushDefenderModifier();
                default:
                    return null;
            }
        }

        private bool ExecuteMovementSteps(
            HashSet<UnitData> pinnedUnits,
            HashSet<UnitData> engagedUnits,
            Dictionary<UnitData, Vector2Int> retreatOrigins)
        {
            if (unitManager == null)
            {
                return false;
            }

            var plannedMoves = new List<PlannedMove>();
            foreach (UnitData unit in new List<UnitData>(unitManager.Units))
            {
                Order order;
                if (unit == null ||
                    !unit.IsAlive ||
                    !HasReadyMovementOrder(unit, out order))
                {
                    continue;
                }

                if (pinnedUnits.Contains(unit))
                {
                    AddCombatLog(
                        GetUnitLabel(unit) +
                        " は攻撃対象として釘付けとなり、移動を見送りました。");
                    RemoveOrder(unit, order);
                    continue;
                }

                List<Vector2Int> path = order.path;
                if (path == null ||
                    path.Count < 2 ||
                    path[0].x != unit.x ||
                    path[0].y != unit.y)
                {
                    RemoveOrder(unit, order);
                    continue;
                }

                UnitDefinition definition = GetUnitDefinition(unit);
                plannedMoves.Add(new PlannedMove
                {
                    unit = unit,
                    order = order,
                    definition = definition,
                    path = new List<Vector2Int>(path),
                    traversedPath = new List<Vector2Int>
                    {
                        new Vector2Int(unit.x, unit.y)
                    },
                    movePoints = definition != null
                        ? definition.movePoints
                        : 0,
                    startX = unit.x,
                    startY = unit.y
                });
            }

            HexGrid grid = mapView != null ? mapView.Grid : null;
            bool movedUnits = false;
            if (grid != null)
            {
                while (true)
                {
                    var proposals = new List<MoveProposal>();
                    for (int i = 0; i < plannedMoves.Count; i++)
                    {
                        PlannedMove move = plannedMoves[i];
                        if (move.stopped ||
                            !move.unit.IsAlive ||
                            move.nextPathIndex >= move.path.Count)
                        {
                            move.stopped = true;
                            continue;
                        }

                        Vector2Int next = move.path[move.nextPathIndex];
                        if (!grid.IsInBounds(next.x, next.y))
                        {
                            move.stopped = true;
                            continue;
                        }

                        HexCell cell = grid.GetCell(next.x, next.y);
                        TerrainDefinition terrain = cell == null
                            ? null
                            : grid.GetTerrainDefinition(cell.terrainId);
                        int stepCost =
                            next.x == move.unit.x &&
                            next.y == move.unit.y
                                ? 0
                                : move.definition != null
                                    ? move.definition.GetMoveCost(terrain)
                                    : grid.GetMoveCost(next.x, next.y);
                        if (stepCost < 0 ||
                            move.consumedMoveCost + stepCost >
                                move.unit.remainingMovePoints)
                        {
                            move.stopped = true;
                            continue;
                        }

                        proposals.Add(new MoveProposal
                        {
                            move = move,
                            destination = next,
                            stepCost = stepCost,
                            cumulativeCost =
                                move.consumedMoveCost + stepCost,
                            remainingCost = CalculateRemainingMoveCost(
                                move,
                                move.nextPathIndex),
                            nearestHeadquartersDistance =
                                GetNearestHeadquartersDistance(
                                    move.unit.x,
                                    move.unit.y,
                                    move.unit.faction)
                        });
                    }

                    if (proposals.Count == 0)
                    {
                        break;
                    }

                    StopMovesBlockedByStationaryEnemies(proposals);
                    bool ambushTriggered = ResolveAmbushes(
                        proposals,
                        engagedUnits,
                        retreatOrigins);
                    var accepted = new HashSet<MoveProposal>();
                    var conflicted = new HashSet<MoveProposal>();

                    var sameCellGroups =
                        new Dictionary<Vector2Int, List<MoveProposal>>();
                    for (int i = 0; i < proposals.Count; i++)
                    {
                        MoveProposal proposal = proposals[i];
                        if (proposal.move.stopped ||
                            !proposal.move.unit.IsAlive)
                        {
                            continue;
                        }

                        List<MoveProposal> group;
                        if (!sameCellGroups.TryGetValue(
                                proposal.destination,
                                out group))
                        {
                            group = new List<MoveProposal>();
                            sameCellGroups.Add(proposal.destination, group);
                        }
                        group.Add(proposal);
                    }

                    foreach (KeyValuePair<Vector2Int, List<MoveProposal>> entry
                        in sameCellGroups)
                    {
                        if (!HasDifferentFactions(entry.Value))
                        {
                            continue;
                        }

                        MoveProposal priorityWinner =
                            SelectMoveWinnerByPriority(entry.Value);
                        Faction winningFaction = ResolveBookingEncounter(
                            entry.Value,
                            priorityWinner.move.unit.faction,
                            engagedUnits,
                            retreatOrigins);
                        for (int i = 0; i < entry.Value.Count; i++)
                        {
                            MoveProposal participant = entry.Value[i];
                            conflicted.Add(participant);
                            if (participant.move.unit.faction ==
                                    winningFaction &&
                                participant.move.unit.IsAlive &&
                                participant.move.unit.organization > 0)
                            {
                                accepted.Add(participant);
                            }
                            else
                            {
                                participant.move.stopped = true;
                            }
                        }
                    }

                    for (int i = 0; i < proposals.Count; i++)
                    {
                        MoveProposal first = proposals[i];
                        if (conflicted.Contains(first) ||
                            first.move.stopped ||
                            !first.move.unit.IsAlive)
                        {
                            continue;
                        }

                        for (int j = i + 1; j < proposals.Count; j++)
                        {
                            MoveProposal second = proposals[j];
                            if (conflicted.Contains(second) ||
                                second.move.stopped ||
                                !second.move.unit.IsAlive ||
                                first.move.unit.faction ==
                                    second.move.unit.faction)
                            {
                                continue;
                            }

                            bool headOnSwap =
                                first.destination.x == second.move.unit.x &&
                                first.destination.y == second.move.unit.y &&
                                second.destination.x == first.move.unit.x &&
                                second.destination.y == first.move.unit.y;
                            if (!headOnSwap)
                            {
                                continue;
                            }

                            var conflict = new List<MoveProposal>
                            {
                                first,
                                second
                            };
                            MoveProposal priorityWinner =
                                SelectMoveWinnerByPriority(conflict);
                            Faction winningFaction = ResolveBookingEncounter(
                                conflict,
                                priorityWinner.move.unit.faction,
                                engagedUnits,
                                retreatOrigins);
                            conflicted.Add(first);
                            conflicted.Add(second);
                            MoveProposal winner =
                                first.move.unit.faction == winningFaction
                                    ? first
                                    : second;
                            MoveProposal loser =
                                winner == first ? second : first;
                            loser.move.stopped = true;
                            EvacuateHeadOnLoser(
                                loser,
                                winner.destination,
                                retreatOrigins);
                            if (winner.move.unit.IsAlive &&
                                winner.move.unit.organization > 0)
                            {
                                accepted.Add(winner);
                            }
                            break;
                        }
                    }

                    var proposalsByCell =
                        new Dictionary<Vector2Int, List<MoveProposal>>();
                    for (int i = 0; i < proposals.Count; i++)
                    {
                        MoveProposal proposal = proposals[i];
                        if (conflicted.Contains(proposal) ||
                            proposal.move.stopped ||
                            !proposal.move.unit.IsAlive)
                        {
                            continue;
                        }

                        List<UnitData> occupants = unitManager.GetUnitsAt(
                            proposal.destination.x,
                            proposal.destination.y);
                        bool blockedByEnemy = false;
                        for (int j = 0; j < occupants.Count; j++)
                        {
                            UnitData occupant = occupants[j];
                            if (occupant != null &&
                                occupant.IsAlive &&
                                occupant.faction !=
                                    proposal.move.unit.faction)
                            {
                                blockedByEnemy = true;
                                break;
                            }
                        }
                        if (blockedByEnemy)
                        {
                            proposal.move.stopped = true;
                            continue;
                        }

                        List<MoveProposal> cellProposals;
                        if (!proposalsByCell.TryGetValue(
                                proposal.destination,
                                out cellProposals))
                        {
                            cellProposals = new List<MoveProposal>();
                            proposalsByCell.Add(
                                proposal.destination,
                                cellProposals);
                        }
                        cellProposals.Add(proposal);
                    }

                    foreach (KeyValuePair<Vector2Int, List<MoveProposal>> entry
                        in proposalsByCell)
                    {
                        bool mixedFactions = HasDifferentFactions(
                            entry.Value);
                        if (!mixedFactions)
                        {
                            for (int i = 0; i < entry.Value.Count; i++)
                            {
                                accepted.Add(entry.Value[i]);
                            }
                            continue;
                        }

                        MoveProposal priorityWinner =
                            SelectMoveWinnerByPriority(entry.Value);
                        Faction winningFaction = ResolveBookingEncounter(
                            entry.Value,
                            priorityWinner.move.unit.faction,
                            engagedUnits,
                            retreatOrigins);
                        for (int i = 0; i < entry.Value.Count; i++)
                        {
                            MoveProposal proposal = entry.Value[i];
                            if (proposal.move.unit.faction == winningFaction &&
                                proposal.move.unit.IsAlive &&
                                proposal.move.unit.organization > 0)
                            {
                                accepted.Add(proposal);
                            }
                            else
                            {
                                proposal.move.stopped = true;
                            }
                        }
                    }

                    bool movedThisStep = false;
                    foreach (MoveProposal proposal in accepted)
                    {
                        PlannedMove move = proposal.move;
                        if (!move.unit.IsAlive || move.stopped)
                        {
                            continue;
                        }

                        Vector2Int previous = new Vector2Int(
                            move.unit.x,
                            move.unit.y);
                        move.unit.x = proposal.destination.x;
                        move.unit.y = proposal.destination.y;
                        retreatOrigins[move.unit] = previous;
                        move.consumedMoveCost += proposal.stepCost;
                        move.nextPathIndex++;
                        move.traversedPath.Add(proposal.destination);
                        move.moved = true;
                        movedThisStep = true;
                        movedUnits = true;
                    }

                    bool routed = ResolveDestroyedAndRoutedUnits(
                        retreatOrigins);
                    movedUnits |= routed;
                    if (!movedThisStep)
                    {
                        break;
                    }

                    if (ambushTriggered)
                    {
                        OnCommandStateChanged?.Invoke();
                    }
                }
            }

            for (int i = 0; i < plannedMoves.Count; i++)
            {
                PlannedMove move = plannedMoves[i];
                if (move.moved && IsRegisteredUnit(move.unit))
                {
                    int targetX = move.unit.x;
                    int targetY = move.unit.y;
                    move.unit.x = move.startX;
                    move.unit.y = move.startY;
                    if (!unitManager.MoveUnitAlongPath(
                            move.unit,
                            targetX,
                            targetY,
                            move.traversedPath))
                    {
                        move.unit.x = targetX;
                        move.unit.y = targetY;
                        unitManager.NotifyUnitMoved(move.unit);
                    }
                    move.unit.remainingMovePoints = Mathf.Max(
                        0,
                        move.unit.remainingMovePoints -
                            move.consumedMoveCost);
                }

                if (move.unit.pendingOrders != null &&
                    move.order != null &&
                    move.order.orderType == OrderType.Move)
                {
                    RemoveOrder(move.unit, move.order);
                }
            }

            if (plannedMoves.Count > 0)
            {
                OnCommandStateChanged?.Invoke();
            }
            return movedUnits;
        }

        private void StopMovesBlockedByStationaryEnemies(
            List<MoveProposal> proposals)
        {
            for (int i = 0; i < proposals.Count; i++)
            {
                MoveProposal proposal = proposals[i];
                List<UnitData> occupants = unitManager.GetUnitsAt(
                    proposal.destination.x,
                    proposal.destination.y);
                bool blocked = false;
                for (int j = 0; j < occupants.Count; j++)
                {
                    UnitData occupant = occupants[j];
                    if (occupant == null ||
                        !occupant.IsAlive ||
                        occupant.faction == proposal.move.unit.faction)
                    {
                        continue;
                    }

                    bool swapping = false;
                    for (int k = 0; k < proposals.Count; k++)
                    {
                        MoveProposal opposingMove = proposals[k];
                        if (opposingMove.move.unit == occupant &&
                            opposingMove.destination.x == proposal.move.unit.x &&
                            opposingMove.destination.y == proposal.move.unit.y)
                        {
                            swapping = true;
                            break;
                        }
                    }
                    if (!swapping)
                    {
                        blocked = true;
                        break;
                    }
                }

                if (blocked)
                {
                    proposal.move.stopped = true;
                }
            }
        }

        private bool ResolveAmbushes(
            List<MoveProposal> proposals,
            HashSet<UnitData> engagedUnits,
            Dictionary<UnitData, Vector2Int> retreatOrigins)
        {
            bool triggered = false;
            System.Func<string, UnitDefinition> definitionLookup =
                gameManager != null
                    ? new System.Func<string, UnitDefinition>(
                        gameManager.GetUnitDefinition)
                    : null;
            for (int i = 0; i < proposals.Count; i++)
            {
                MoveProposal proposal = proposals[i];
                if (proposal.move.stopped ||
                    !proposal.move.unit.IsAlive)
                {
                    continue;
                }

                foreach (UnitData watcher in unitManager.Units)
                {
                    if (watcher == null ||
                        !watcher.IsAlive ||
                        !watcher.isAmbushed ||
                        watcher.faction == proposal.move.unit.faction ||
                        watcher.pendingOrders == null ||
                        watcher.pendingOrders.Count == 0)
                    {
                        continue;
                    }

                    Order ambushOrder = watcher.pendingOrders[0];
                    if (ambushOrder == null ||
                        ambushOrder.orderType != OrderType.Ambush ||
                        ambushOrder.remainingDelayTurns > 0 ||
                        ambushOrder.secondaryTargetX !=
                            proposal.destination.x ||
                        ambushOrder.secondaryTargetY !=
                            proposal.destination.y)
                    {
                        continue;
                    }

                    if (!CombatResolver.IsAdjacent(
                            watcher.x,
                            watcher.y,
                            proposal.destination.x,
                            proposal.destination.y))
                    {
                        continue;
                    }

                    UnitDefinition ambusherDefinition =
                        GetUnitDefinition(watcher);
                    UnitDefinition targetDefinition =
                        GetUnitDefinition(proposal.move.unit);
                    if (ambusherDefinition == null ||
                        targetDefinition == null)
                    {
                        Debug.LogWarning(
                            "伏兵戦闘を解決できません。部隊兵科の定義が見つかりません。");
                        proposal.move.stopped = true;
                        triggered = true;
                        break;
                    }

                    CombatResult result = CombatResolver.ResolveCombat(
                        watcher,
                        proposal.move.unit,
                        ambusherDefinition,
                        targetDefinition,
                        mapView != null ? mapView.Grid : null,
                        unitManager,
                        definitionLookup,
                        false,
                        CombatResolver.EvaluateAmbushAttackerModifier(),
                        CombatResolver.EvaluateAmbushDefenderModifier(),
                        1000,
                        false,
                        true);
                    engagedUnits.Add(watcher);
                    engagedUnits.Add(proposal.move.unit);
                    watcher.isAmbushed = false;
                    RemoveOrder(watcher, ambushOrder);
                    proposal.move.stopped = true;
                    retreatOrigins[proposal.move.unit] = new Vector2Int(
                        proposal.move.unit.x,
                        proposal.move.unit.y);
                    LogCombatResult(
                        watcher,
                        proposal.move.unit,
                        result);
                    AddCombatLog(
                        GetUnitLabel(proposal.move.unit) +
                        " は伏兵に遭遇し、移動を停止しました。");
                    triggered = true;
                    break;
                }
            }
            return triggered;
        }

        private Faction ResolveBookingEncounter(
            List<MoveProposal> participants,
            Faction priorityFaction,
            HashSet<UnitData> engagedUnits,
            Dictionary<UnitData, Vector2Int> retreatOrigins)
        {
            MoveProposal first = SelectMoveWinnerByPriority(participants);
            MoveProposal second = null;
            for (int i = 0; i < participants.Count; i++)
            {
                if (participants[i].move.unit.faction !=
                    first.move.unit.faction &&
                    (second == null ||
                     IsHigherMovePriority(participants[i], second)))
                {
                    second = participants[i];
                }
            }

            if (second == null)
            {
                return first.move.unit.faction;
            }

            MoveProposal attackerProposal =
                first.move.unit.faction == priorityFaction ? first : second;
            MoveProposal defenderProposal =
                attackerProposal == first ? second : first;
            UnitData attacker = attackerProposal.move.unit;
            UnitData defender = defenderProposal.move.unit;
            UnitDefinition attackerDefinition = GetUnitDefinition(attacker);
            UnitDefinition defenderDefinition = GetUnitDefinition(defender);
            if (attackerDefinition == null || defenderDefinition == null)
            {
                Debug.LogWarning(
                    "遭遇戦を解決できません。部隊兵科の定義が見つかりません。");
                return priorityFaction;
            }

            CombatResult result = CombatResolver.ResolveCombat(
                attacker,
                defender,
                attackerDefinition,
                defenderDefinition,
                mapView != null ? mapView.Grid : null,
                unitManager,
                gameManager != null
                    ? new System.Func<string, UnitDefinition>(
                        gameManager.GetUnitDefinition)
                    : null,
                false,
                null,
                null,
                500,
                true,
                true);
            engagedUnits.Add(attacker);
            engagedUnits.Add(defender);
            retreatOrigins[attacker] = new Vector2Int(attacker.x, attacker.y);
            retreatOrigins[defender] = new Vector2Int(defender.x, defender.y);
            LogCombatResult(attacker, defender, result);
            if (!result.counterAttackOccurred &&
                defender.IsAlive &&
                attacker.IsAlive)
            {
                CombatResult returnResult = CombatResolver.ResolveCombat(
                    defender,
                    attacker,
                    defenderDefinition,
                    attackerDefinition,
                    mapView != null ? mapView.Grid : null,
                    unitManager,
                    gameManager != null
                        ? new System.Func<string, UnitDefinition>(
                            gameManager.GetUnitDefinition)
                        : null,
                    false,
                    null,
                    null,
                    500,
                    false,
                    true);
                LogCombatResult(defender, attacker, returnResult);
            }

            int firstFactionManpower = 0;
            int secondFactionManpower = 0;
            for (int i = 0; i < participants.Count; i++)
            {
                UnitData unit = participants[i].move.unit;
                if (unit == null || !unit.IsAlive)
                {
                    continue;
                }

                if (unit.faction == first.move.unit.faction)
                {
                    firstFactionManpower += unit.CurrentTotalManpower;
                }
                else
                {
                    secondFactionManpower += unit.CurrentTotalManpower;
                }
            }

            if (firstFactionManpower == secondFactionManpower)
            {
                AddCombatLog("遭遇戦は兵力同数のため、優先順位側が進入します。");
                return priorityFaction;
            }
            return firstFactionManpower > secondFactionManpower
                ? first.move.unit.faction
                : second.move.unit.faction;
        }

        private void EvacuateHeadOnLoser(
            MoveProposal loser,
            Vector2Int winnerDestination,
            Dictionary<UnitData, Vector2Int> retreatOrigins)
        {
            UnitData unit = loser.move.unit;
            Vector2Int source = new Vector2Int(unit.x, unit.y);
            if (TryFindSafeAdjacentCell(
                    unit,
                    source,
                    winnerDestination,
                    out Vector2Int safeCell))
            {
                unit.x = safeCell.x;
                unit.y = safeCell.y;
                retreatOrigins[unit] = safeCell;
                loser.move.traversedPath.Add(safeCell);
                loser.move.moved = true;
            }
            else
            {
                unitManager.RemoveUnit(unit);
                AddCombatLog(
                    GetUnitLabel(unit) +
                    " は正面衝突で退路を失い壊滅しました。");
            }
        }

        private bool TryFindSafeAdjacentCell(
            UnitData unit,
            Vector2Int firstCenter,
            Vector2Int secondCenter,
            out Vector2Int destination)
        {
            destination = Vector2Int.zero;
            HexGrid grid = mapView != null ? mapView.Grid : null;
            if (grid == null || unit == null)
            {
                return false;
            }

            var candidates = new List<Vector2Int>();
            for (int centerIndex = 0; centerIndex < 2; centerIndex++)
            {
                Vector2Int center = centerIndex == 0
                    ? firstCenter
                    : secondCenter;
                for (int direction = 0; direction < 6; direction++)
                {
                    Vector2Int candidate =
                        HexUtility.GetNeighborOffset(
                            center.x,
                            center.y,
                            direction);
                    if (grid.IsInBounds(candidate.x, candidate.y) &&
                        candidate != secondCenter &&
                        !candidates.Contains(candidate))
                    {
                        candidates.Add(candidate);
                    }
                }
            }

            var safeCandidates = new List<Vector2Int>();
            for (int i = 0; i < candidates.Count; i++)
            {
                Vector2Int candidate = candidates[i];
                if (HasLivingEnemyAt(
                        candidate.x,
                        candidate.y,
                        unit.faction) ||
                    grid.GetMoveCost(candidate.x, candidate.y) < 0)
                {
                    continue;
                }

                safeCandidates.Add(candidate);
            }
            if (safeCandidates.Count > 0)
            {
                destination = safeCandidates[
                    UnityEngine.Random.Range(0, safeCandidates.Count)];
                return true;
            }
            return false;
        }

        private int CalculateRemainingMoveCost(
            PlannedMove move,
            int nextPathIndex)
        {
            int remainingCost = 0;
            HexGrid grid = mapView != null ? mapView.Grid : null;
            for (int i = nextPathIndex + 1; i < move.path.Count; i++)
            {
                Vector2Int cellPosition = move.path[i];
                HexCell cell = grid != null
                    ? grid.GetCell(cellPosition.x, cellPosition.y)
                    : null;
                TerrainDefinition terrain = cell == null || grid == null
                    ? null
                    : grid.GetTerrainDefinition(cell.terrainId);
                int cost = move.definition != null
                    ? move.definition.GetMoveCost(terrain)
                    : grid != null
                        ? grid.GetMoveCost(cellPosition.x, cellPosition.y)
                        : -1;
                if (cost < 0)
                {
                    return int.MaxValue;
                }
                remainingCost += cost;
            }
            return remainingCost;
        }

        private MoveProposal SelectMoveWinnerByPriority(
            List<MoveProposal> proposals)
        {
            MoveProposal winner = proposals[0];
            for (int i = 1; i < proposals.Count; i++)
            {
                if (IsHigherMovePriority(proposals[i], winner))
                {
                    winner = proposals[i];
                }
                else if (!IsHigherMovePriority(winner, proposals[i]) &&
                         UnityEngine.Random.Range(0, 2) == 0)
                {
                    winner = proposals[i];
                }
            }
            return winner;
        }

        private static bool IsHigherMovePriority(
            MoveProposal left,
            MoveProposal right)
        {
            if (left.remainingCost != right.remainingCost)
            {
                return left.remainingCost < right.remainingCost;
            }
            if (left.move.movePoints != right.move.movePoints)
            {
                return left.move.movePoints > right.move.movePoints;
            }
            return left.nearestHeadquartersDistance <
                right.nearestHeadquartersDistance;
        }

        private int GetNearestHeadquartersDistance(
            int x,
            int y,
            Faction faction)
        {
            int nearest = int.MaxValue;
            if (unitManager == null)
            {
                return nearest;
            }

            foreach (UnitData unit in unitManager.Units)
            {
                if (unit == null || !unit.IsAlive || unit.faction != faction)
                {
                    continue;
                }

                UnitDefinition definition = GetUnitDefinition(unit);
                if (definition == null || definition.commandLevel < 1)
                {
                    continue;
                }

                nearest = Mathf.Min(
                    nearest,
                    HexUtility.DistanceOffset(x, y, unit.x, unit.y));
            }
            return nearest;
        }

        private bool HasReadyMovementOrder(UnitData unit, out Order order)
        {
            order = null;
            if (unit == null ||
                unit.pendingOrders == null ||
                unit.pendingOrders.Count == 0)
            {
                return false;
            }

            Order current = unit.pendingOrders[0];
            if (current == null ||
                (current.orderType != OrderType.Move &&
                 current.orderType != OrderType.Assault) ||
                current.remainingDelayTurns > 0)
            {
                return false;
            }

            order = current;
            return true;
        }

        private bool ResolveAssaultAttacks(
            HashSet<UnitData> engagedUnits,
            Dictionary<UnitData, Vector2Int> retreatOrigins)
        {
            if (unitManager == null)
            {
                return false;
            }

            var attackers = new List<UnitData>(unitManager.Units);
            System.Func<string, UnitDefinition> definitionLookup =
                gameManager != null
                    ? new System.Func<string, UnitDefinition>(
                        gameManager.GetUnitDefinition)
                    : null;

            for (int i = 0; i < attackers.Count; i++)
            {
                UnitData attacker = attackers[i];
                if (attacker == null ||
                    !attacker.IsAlive ||
                    attacker.pendingOrders == null ||
                    attacker.pendingOrders.Count == 0)
                {
                    continue;
                }

                Order order = attacker.pendingOrders[0];
                if (order == null ||
                    order.orderType != OrderType.Assault ||
                    order.remainingDelayTurns > 0)
                {
                    continue;
                }

                List<UnitData> defenders = GetAttackDefenders(
                    order.secondaryTargetX,
                    order.secondaryTargetY,
                    attacker.faction);
                UnitData defender = SelectCombatDefender(defenders);
                UnitDefinition attackerDefinition =
                    GetUnitDefinition(attacker);
                if (defender == null ||
                    attackerDefinition == null ||
                    !CombatResolver.IsAdjacent(
                        attacker.x,
                        attacker.y,
                        defender.x,
                        defender.y))
                {
                    AddCombatLog(
                        GetUnitLabel(attacker) +
                        " の急襲は対象不在または隣接マス以外のため不発となりました。");
                    RemoveOrder(attacker, order);
                    continue;
                }

                UnitModifier defenderModifier =
                    CombineDefenderOrderModifiers(
                        CombatResolver.EvaluateAssaultDefenderModifier(),
                        GetDefenderOrderModifier(defender));
                CombatResult result = CombatResolver.ResolveCombat(
                    attacker,
                    defender,
                    attackerDefinition,
                    GetUnitDefinition(defender),
                    mapView != null ? mapView.Grid : null,
                    unitManager,
                    definitionLookup,
                    false,
                    CombatResolver.EvaluateAssaultAttackerModifier(),
                    defenderModifier,
                    1000,
                    true,
                    false);

                engagedUnits.Add(attacker);
                engagedUnits.Add(defender);
                if (defender.isAmbushed)
                {
                    defender.isAmbushed = false;
                }
                LogCombatResult(attacker, defender, result);
                RemoveOrder(attacker, order);
            }

            bool movedUnits = ResolveDestroyedAndRoutedUnits(retreatOrigins);
            OnCommandStateChanged?.Invoke();
            return movedUnits;
        }

        private static UnitModifier CombineDefenderOrderModifiers(
            UnitModifier first,
            UnitModifier second)
        {
            if (first == null)
            {
                return second;
            }
            if (second == null)
            {
                return first;
            }

            return new UnitModifier
            {
                sourceId = first.sourceId + "+" + second.sourceId,
                displayName = first.displayName + " + " + second.displayName,
                attackMultiplier = (int)((long)first.attackMultiplier *
                    second.attackMultiplier / 1000),
                defenseMultiplier = (int)((long)first.defenseMultiplier *
                    second.defenseMultiplier / 1000),
                orgDamageMultiplier = (int)((long)first.orgDamageMultiplier *
                    second.orgDamageMultiplier / 1000)
            };
        }

        private void CompleteEntrenchmentsAndOrders(
            HashSet<UnitData> engagedUnits)
        {
            if (unitManager == null)
            {
                return;
            }

            HexGrid grid = mapView != null ? mapView.Grid : null;
            foreach (UnitData unit in unitManager.Units)
            {
                if (unit == null)
                {
                    continue;
                }

                if (unit.IsAlive &&
                    !engagedUnits.Contains(unit) &&
                    unit.pendingOrders != null &&
                    unit.pendingOrders.Count > 0 &&
                    unit.pendingOrders[0] != null &&
                    unit.pendingOrders[0].orderType == OrderType.Entrench &&
                    grid != null)
                {
                    HexCell cell = grid.GetCell(unit.x, unit.y);
                    if (cell != null)
                    {
                        cell.fortificationLevel = Mathf.Min(
                            5,
                            cell.fortificationLevel + 1);
                        AddCombatLog(
                            GetUnitLabel(unit) +
                            " は塹壕を構築しました（陣地レベル=" +
                            cell.fortificationLevel +
                            "）。");
                    }
                }

                unit.isAmbushed = false;
                if (unit.pendingOrders != null &&
                    unit.pendingOrders.Count > 0 &&
                    unit.pendingOrders[0] != null &&
                    (unit.pendingOrders[0].orderType == OrderType.Defend ||
                     unit.pendingOrders[0].orderType == OrderType.Entrench ||
                     unit.pendingOrders[0].orderType == OrderType.Ambush))
                {
                    unit.pendingOrders.RemoveAt(0);
                }
            }
            OnCommandStateChanged?.Invoke();
        }

        private bool ExecutePlannedMoves(List<AttackEntry> readyAttacks)
        {
            if (unitManager == null)
            {
                return false;
            }

            HashSet<UnitData> pinnedUnits =
                GetUnitsPinnedByReadyAttacks(readyAttacks);
            var plannedMoves = new List<PlannedMove>();
            foreach (UnitData unit in new List<UnitData>(unitManager.Units))
            {
                if (unit == null ||
                    !unit.IsAlive ||
                    !HasMoveOrder(unit, out Order moveOrder) ||
                    moveOrder.remainingDelayTurns > 0)
                {
                    continue;
                }

                if (pinnedUnits.Contains(unit))
                {
                    AddCombatLog(unit.unitName + " は攻撃対象として釘付けとなり、移動を見送りました。");
                    continue;
                }

                List<Vector2Int> path = moveOrder.path;
                if (path == null ||
                    path.Count < 2 ||
                    path[0].x != unit.x ||
                    path[0].y != unit.y)
                {
                    RemoveOrder(unit, moveOrder);
                    continue;
                }

                UnitDefinition definition = GetUnitDefinition(unit);
                plannedMoves.Add(new PlannedMove
                {
                    unit = unit,
                    order = moveOrder,
                    definition = definition,
                    path = new List<Vector2Int>(path),
                    traversedPath = new List<Vector2Int>
                    {
                        new Vector2Int(unit.x, unit.y)
                    },
                    movePoints = definition != null ? definition.movePoints : 0,
                    startX = unit.x,
                    startY = unit.y
                });
            }

            bool movedUnits = false;
            HexGrid grid = mapView != null ? mapView.Grid : null;
            while (grid != null)
            {
                var proposalsByCell =
                    new Dictionary<Vector2Int, List<MoveProposal>>();

                for (int i = 0; i < plannedMoves.Count; i++)
                {
                    PlannedMove move = plannedMoves[i];
                    if (move.stopped)
                    {
                        continue;
                    }

                    if (move.nextPathIndex >= move.path.Count)
                    {
                        move.stopped = true;
                        continue;
                    }

                    Vector2Int next = move.path[move.nextPathIndex];
                    if (!grid.IsInBounds(next.x, next.y) ||
                        HasLivingEnemyAt(next.x, next.y, move.unit.faction))
                    {
                        move.stopped = true;
                        continue;
                    }

                    HexCell cell = grid.GetCell(next.x, next.y);
                    TerrainDefinition terrain = cell == null
                        ? null
                        : grid.GetTerrainDefinition(cell.terrainId);
                    int stepCost = move.definition != null
                        ? move.definition.GetMoveCost(terrain)
                        : grid.GetMoveCost(next.x, next.y);
                    if (stepCost < 0 ||
                        move.consumedMoveCost + stepCost >
                            move.unit.remainingMovePoints)
                    {
                        move.stopped = true;
                        continue;
                    }

                    var proposal = new MoveProposal
                    {
                        move = move,
                        destination = next,
                        stepCost = stepCost,
                        cumulativeCost = move.consumedMoveCost + stepCost
                    };
                    List<MoveProposal> proposals;
                    if (!proposalsByCell.TryGetValue(next, out proposals))
                    {
                        proposals = new List<MoveProposal>();
                        proposalsByCell.Add(next, proposals);
                    }
                    proposals.Add(proposal);
                }

                var winningProposals = new List<MoveProposal>();
                foreach (KeyValuePair<Vector2Int, List<MoveProposal>> entry
                    in proposalsByCell)
                {
                    if (!HasDifferentFactions(entry.Value))
                    {
                        winningProposals.AddRange(entry.Value);
                        continue;
                    }

                    MoveProposal winner =
                        SelectMoveWinnerByFaction(entry.Value);
                    Faction winningFaction = winner.move.unit.faction;
                    for (int i = 0; i < entry.Value.Count; i++)
                    {
                        MoveProposal proposal = entry.Value[i];
                        if (proposal.move.unit.faction == winningFaction)
                        {
                            winningProposals.Add(proposal);
                            continue;
                        }

                        proposal.move.stopped = true;
                        AddCombatLog(
                            proposal.move.unit.unitName +
                            " は同時移動の競合に敗れ、直前のマスで停止しました。");
                    }
                }

                if (winningProposals.Count == 0)
                {
                    break;
                }

                for (int i = 0; i < winningProposals.Count; i++)
                {
                    MoveProposal proposal = winningProposals[i];
                    PlannedMove move = proposal.move;
                    move.unit.x = proposal.destination.x;
                    move.unit.y = proposal.destination.y;
                    move.consumedMoveCost += proposal.stepCost;
                    move.nextPathIndex++;
                    move.traversedPath.Add(proposal.destination);
                    move.moved = true;
                    movedUnits = true;
                }
            }

            for (int i = 0; i < plannedMoves.Count; i++)
            {
                PlannedMove move = plannedMoves[i];
                if (move.moved)
                {
                    int targetX = move.unit.x;
                    int targetY = move.unit.y;
                    move.unit.x = move.startX;
                    move.unit.y = move.startY;
                    if (!unitManager.MoveUnitAlongPath(
                            move.unit,
                            targetX,
                            targetY,
                            move.traversedPath))
                    {
                        move.unit.x = targetX;
                        move.unit.y = targetY;
                        unitManager.NotifyUnitMoved(move.unit);
                    }

                    move.unit.remainingMovePoints = Mathf.Max(
                        0,
                        move.unit.remainingMovePoints - move.consumedMoveCost);
                    AddCombatLog(
                        move.unit.unitName +
                        " は (" + targetX + "," + targetY + ") まで移動しました。");
                }

                RemoveOrder(move.unit, move.order);
            }

            if (plannedMoves.Count > 0)
            {
                OnCommandStateChanged?.Invoke();
            }
            return movedUnits;
        }

        private static bool HasDifferentFactions(
            List<MoveProposal> proposals)
        {
            if (proposals.Count < 2)
            {
                return false;
            }

            Faction firstFaction = proposals[0].move.unit.faction;
            for (int i = 1; i < proposals.Count; i++)
            {
                if (proposals[i].move.unit.faction != firstFaction)
                {
                    return true;
                }
            }
            return false;
        }

        private MoveProposal SelectMoveWinnerByFaction(
            List<MoveProposal> proposals)
        {
            var factionWinners = new List<MoveProposal>();
            for (int i = 0; i < proposals.Count; i++)
            {
                Faction faction = proposals[i].move.unit.faction;
                bool alreadyAdded = false;
                for (int j = 0; j < factionWinners.Count; j++)
                {
                    if (factionWinners[j].move.unit.faction == faction)
                    {
                        alreadyAdded = true;
                        break;
                    }
                }
                if (alreadyAdded)
                {
                    continue;
                }

                var sameFactionProposals = new List<MoveProposal>();
                for (int j = 0; j < proposals.Count; j++)
                {
                    if (proposals[j].move.unit.faction == faction)
                    {
                        sameFactionProposals.Add(proposals[j]);
                    }
                }
                factionWinners.Add(SelectMoveWinner(sameFactionProposals));
            }

            return SelectMoveWinner(factionWinners);
        }

        private HashSet<UnitData> GetUnitsPinnedByReadyAttacks(
            List<AttackEntry> attacks)
        {
            var pinnedUnits = new HashSet<UnitData>();
            if (unitManager == null)
            {
                return pinnedUnits;
            }

            for (int i = 0; i < attacks.Count; i++)
            {
                AttackEntry attack = attacks[i];
                UnitData target =
                    unitManager.GetUnitById(attack.order.targetUnitId);
                if (target != null &&
                    target.IsAlive &&
                    target.faction != attack.attacker.faction)
                {
                    pinnedUnits.Add(target);
                }
            }

            return pinnedUnits;
        }

        private List<AttackEntry> GetReadyAttackEntries()
        {
            var attacks = new List<AttackEntry>();
            if (unitManager == null)
            {
                return attacks;
            }

            int sequence = 0;
            foreach (UnitData unit in unitManager.Units)
            {
                if (unit == null ||
                    !unit.IsAlive ||
                    unit.pendingOrders == null ||
                    unit.pendingOrders.Count == 0)
                {
                    continue;
                }

                Order order = unit.pendingOrders[0];
                if (order == null ||
                    order.orderType != OrderType.Attack ||
                    order.remainingDelayTurns > 0)
                {
                    continue;
                }

                UnitDefinition definition = GetUnitDefinition(unit);
                attacks.Add(new AttackEntry
                {
                    attacker = unit,
                    order = order,
                    attackerDefinition = definition,
                    attackRange = definition != null
                        ? definition.attackRange
                        : 0,
                    sequence = sequence++
                });
            }

            attacks.Sort((left, right) =>
            {
                int rangeOrder = right.attackRange.CompareTo(left.attackRange);
                return rangeOrder != 0
                    ? rangeOrder
                    : left.sequence.CompareTo(right.sequence);
            });
            return attacks;
        }

        private bool ResolveCombatAndAftermath(
            HashSet<UnitData> engagedUnits,
            List<AttackEntry> attacks)
        {
            if (unitManager == null)
            {
                return false;
            }

            var resolvedAttacks = new List<ResolvedAttack>();
            System.Func<string, UnitDefinition> definitionLookup =
                gameManager != null
                    ? new System.Func<string, UnitDefinition>(
                        gameManager.GetUnitDefinition)
                    : null;

            for (int i = 0; i < attacks.Count; i++)
            {
                AttackEntry attack = attacks[i];
                if (!attack.attacker.IsAlive ||
                    !IsCurrentOrder(attack.attacker, attack.order))
                {
                    continue;
                }

                UnitData defender =
                    unitManager.GetUnitById(attack.order.targetUnitId);
                if (!CombatResolver.CanAttack(
                        attack.attacker,
                        defender,
                        attack.attackerDefinition))
                {
                    AddCombatLog(
                        GetUnitLabel(attack.attacker) +
                        " の攻撃命令は、対象が不在または射程外のため実行されませんでした。");
                    RemoveOrder(attack.attacker, attack.order);
                    continue;
                }

                int targetX = defender.x;
                int targetY = defender.y;
                Faction defenderFaction = defender.faction;
                UnitDefinition defenderDefinition =
                    GetUnitDefinition(defender);
                bool defenderCurrentlyAttacking =
                    HasReadyAttackOrder(defender, attacks);

                CombatResult result = CombatResolver.ResolveCombat(
                    attack.attacker,
                    defender,
                    attack.attackerDefinition,
                    defenderDefinition,
                    mapView != null ? mapView.Grid : null,
                    unitManager,
                    definitionLookup,
                    defenderCurrentlyAttacking);

                engagedUnits.Add(attack.attacker);
                engagedUnits.Add(defender);
                resolvedAttacks.Add(new ResolvedAttack
                {
                    attacker = attack.attacker,
                    defenderFaction = defenderFaction,
                    targetX = targetX,
                    targetY = targetY
                });
                LogCombatResult(attack.attacker, defender, result);
                RemoveOrder(attack.attacker, attack.order);
            }

            bool movedUnits = ResolveDestroyedAndRoutedUnits();
            movedUnits |= AdvanceMeleeAttackers(resolvedAttacks);
            OnCommandStateChanged?.Invoke();
            return movedUnits;
        }

        private void LogCombatResult(
            UnitData attacker,
            UnitData defender,
            CombatResult result)
        {
            var modifierNames = new List<string>();
            if (result.appliedModifiers != null)
            {
                for (int i = 0; i < result.appliedModifiers.Count; i++)
                {
                    UnitModifier modifier = result.appliedModifiers[i];
                    if (modifier != null)
                    {
                        modifierNames.Add(
                            string.IsNullOrEmpty(modifier.displayName)
                                ? modifier.sourceId
                                : modifier.displayName);
                    }
                }
            }

            string modifierSummary = modifierNames.Count > 0
                ? " 補正=" + string.Join("、", modifierNames)
                : string.Empty;
            AddCombatLog(
                GetUnitLabel(attacker) +
                " が " +
                GetUnitLabel(defender) +
                " を攻撃。兵員損害=" +
                result.defenderManpowerDamage +
                "、組織率損害=" +
                result.defenderOrganizationDamage +
                modifierSummary);

            if (result.counterAttackOccurred)
            {
                AddCombatLog(
                    GetUnitLabel(defender) +
                    " が反撃。攻撃側の兵員損害=" +
                    result.attackerManpowerDamage +
                    "、組織率損害=" +
                    result.attackerOrganizationDamage +
                    "。");
            }
        }

        private bool ResolveDestroyedAndRoutedUnits(
            Dictionary<UnitData, Vector2Int> retreatOrigins = null)
        {
            bool movedUnits = false;
            List<UnitData> units = new List<UnitData>(unitManager.Units);

            for (int i = 0; i < units.Count; i++)
            {
                UnitData unit = units[i];
                if (unit == null || unit.CurrentTotalManpower > 0)
                {
                    continue;
                }

                unitManager.RemoveUnit(unit);
                AddCombatLog(GetUnitLabel(unit) + " は兵員を失い壊滅しました。");
            }

            for (int i = 0; i < units.Count; i++)
            {
                UnitData unit = units[i];
                if (unit == null ||
                    unit.CurrentTotalManpower <= 0 ||
                    unit.organization > 0 ||
                    !IsRegisteredUnit(unit))
                {
                    continue;
                }

                Vector2Int retreatCell;
                bool hasRetreatCell = false;
                Vector2Int previousCell;
                if (retreatOrigins != null &&
                    retreatOrigins.TryGetValue(unit, out previousCell))
                {
                    if (TryFindSafeRoutDestination(
                            unit,
                            previousCell,
                            out retreatCell))
                    {
                        hasRetreatCell = true;
                    }
                }
                else
                {
                    hasRetreatCell =
                        TryFindRoutDestination(unit, out retreatCell);
                }

                if (hasRetreatCell)
                {
                    int fromX = unit.x;
                    int fromY = unit.y;
                    unit.organization = 250;
                    bool retreated = unitManager.MoveUnitAlongPath(
                        unit,
                        retreatCell.x,
                        retreatCell.y,
                        new List<Vector2Int>
                        {
                            new Vector2Int(fromX, fromY),
                            retreatCell
                        });
                    if (retreated)
                    {
                        if (unit.pendingOrders != null)
                        {
                            unit.pendingOrders.Clear();
                        }
                        movedUnits = true;
                        AddCombatLog(
                            GetUnitLabel(unit) +
                            " は敗走し、(" +
                            retreatCell.x +
                            "," +
                            retreatCell.y +
                            ") へ退避しました。");
                        continue;
                    }
                }

                unitManager.RemoveUnit(unit);
                AddCombatLog(
                    GetUnitLabel(unit) +
                    " は退路がなく壊滅しました。");
            }

            return movedUnits;
        }

        private bool TryFindSafeRoutDestination(
            UnitData unit,
            Vector2Int preferredCell,
            out Vector2Int destination)
        {
            destination = Vector2Int.zero;
            HexGrid grid = mapView != null ? mapView.Grid : null;
            if (unit == null || grid == null)
            {
                return false;
            }

            if (IsSafeRoutCell(unit, preferredCell, grid))
            {
                destination = preferredCell;
                return true;
            }

            return TryFindSafeAdjacentCell(
                unit,
                preferredCell,
                new Vector2Int(unit.x, unit.y),
                out destination);
        }

        private bool IsSafeRoutCell(
            UnitData unit,
            Vector2Int cellPosition,
            HexGrid grid)
        {
            if (!grid.IsInBounds(cellPosition.x, cellPosition.y) ||
                HasLivingEnemyAt(
                    cellPosition.x,
                    cellPosition.y,
                    unit.faction))
            {
                return false;
            }

            return grid.GetMoveCost(cellPosition.x, cellPosition.y) >= 0;
        }

        private bool TryFindRoutDestination(
            UnitData unit,
            out Vector2Int destination)
        {
            destination = Vector2Int.zero;
            HexGrid grid = mapView != null ? mapView.Grid : null;
            if (unit == null || grid == null || unitManager == null)
            {
                return false;
            }

            UnitDefinition definition = GetUnitDefinition(unit);
            int bestEnemyDistance = int.MinValue;
            bool foundDestination = false;
            for (int direction = 0; direction < 6; direction++)
            {
                Vector2Int neighbor =
                    HexUtility.GetNeighborOffset(unit.x, unit.y, direction);
                if (!grid.IsInBounds(neighbor.x, neighbor.y) ||
                    HasLivingEnemyAt(neighbor.x, neighbor.y, unit.faction))
                {
                    continue;
                }

                HexCell cell = grid.GetCell(neighbor.x, neighbor.y);
                TerrainDefinition terrain = cell == null
                    ? null
                    : grid.GetTerrainDefinition(cell.terrainId);
                int moveCost = definition != null
                    ? definition.GetMoveCost(terrain)
                    : grid.GetMoveCost(neighbor.x, neighbor.y);
                if (moveCost < 0)
                {
                    continue;
                }

                int enemyDistance = GetNearestEnemyDistance(
                    neighbor.x,
                    neighbor.y,
                    unit.faction);
                if (!foundDestination || enemyDistance > bestEnemyDistance)
                {
                    destination = neighbor;
                    bestEnemyDistance = enemyDistance;
                    foundDestination = true;
                }
            }

            return foundDestination;
        }

        private int GetNearestEnemyDistance(int x, int y, Faction faction)
        {
            int nearestDistance = int.MaxValue;
            bool foundEnemy = false;
            foreach (UnitData enemy in unitManager.Units)
            {
                if (enemy == null || !enemy.IsAlive || enemy.faction == faction)
                {
                    continue;
                }

                int distance = HexUtility.DistanceOffset(
                    x,
                    y,
                    enemy.x,
                    enemy.y);
                nearestDistance = Mathf.Min(nearestDistance, distance);
                foundEnemy = true;
            }

            return foundEnemy ? nearestDistance : 0;
        }

        private bool AdvanceMeleeAttackers(List<ResolvedAttack> resolvedAttacks)
        {
            bool movedUnits = false;
            HexGrid grid = mapView != null ? mapView.Grid : null;
            if (grid == null)
            {
                return false;
            }

            for (int i = 0; i < resolvedAttacks.Count; i++)
            {
                ResolvedAttack attack = resolvedAttacks[i];
                UnitData attacker = attack.attacker;
                if (attacker == null ||
                    !attacker.IsAlive ||
                    attacker.x != attack.attackerX ||
                    attacker.y != attack.attackerY)
                {
                    continue;
                }

                UnitDefinition definition = GetUnitDefinition(attacker);
                if (definition == null || definition.attackRange != 1)
                {
                    continue;
                }

                if (HasLivingFactionUnitAt(
                        attack.targetX,
                        attack.targetY,
                        attack.defenderFaction) ||
                    HasLivingEnemyAt(
                        attack.targetX,
                        attack.targetY,
                        attacker.faction))
                {
                    continue;
                }

                HexCell targetCell = grid.GetCell(
                    attack.targetX,
                    attack.targetY);
                TerrainDefinition terrain = targetCell == null
                    ? null
                    : grid.GetTerrainDefinition(targetCell.terrainId);
                if (definition.GetMoveCost(terrain) < 0)
                {
                    continue;
                }

                if (attacker.x == attack.targetX &&
                    attacker.y == attack.targetY)
                {
                    continue;
                }

                int fromX = attacker.x;
                int fromY = attacker.y;
                if (unitManager.MoveUnitAlongPath(
                        attacker,
                        attack.targetX,
                        attack.targetY,
                        new List<Vector2Int>
                        {
                            new Vector2Int(fromX, fromY),
                            new Vector2Int(attack.targetX, attack.targetY)
                        }))
                {
                    movedUnits = true;
                    AddCombatLog(
                        GetUnitLabel(attacker) +
                        " が敵陣の空いたマスを占領しました。");
                }
            }

            return movedUnits;
        }

        private void RecoverNonCombatUnits(HashSet<UnitData> engagedUnits)
        {
            if (unitManager == null)
            {
                return;
            }

            bool recoveredAny = false;
            foreach (UnitData unit in unitManager.Units)
            {
                if (unit == null ||
                    !unit.IsAlive ||
                    engagedUnits.Contains(unit))
                {
                    continue;
                }

                int previousOrganization = unit.organization;
                unit.organization = Mathf.Min(
                    unit.maxOrganization,
                    unit.organization + 150);
                if (unit.organization > previousOrganization)
                {
                    recoveredAny = true;
                    AddCombatLog(
                        GetUnitLabel(unit) +
                        " は組織率を回復しました（+" +
                        (unit.organization - previousOrganization) +
                        "）。");
                }
            }

            if (recoveredAny)
            {
                OnCommandStateChanged?.Invoke();
            }
        }

        private MoveProposal SelectMoveWinner(List<MoveProposal> proposals)
        {
            int lowestCost = int.MaxValue;
            int highestMovePoints = int.MinValue;
            var tiedProposals = new List<MoveProposal>();

            for (int i = 0; i < proposals.Count; i++)
            {
                MoveProposal proposal = proposals[i];
                if (proposal.cumulativeCost < lowestCost ||
                    (proposal.cumulativeCost == lowestCost &&
                     proposal.move.movePoints > highestMovePoints))
                {
                    lowestCost = proposal.cumulativeCost;
                    highestMovePoints = proposal.move.movePoints;
                    tiedProposals.Clear();
                    tiedProposals.Add(proposal);
                }
                else if (proposal.cumulativeCost == lowestCost &&
                    proposal.move.movePoints == highestMovePoints)
                {
                    tiedProposals.Add(proposal);
                }
            }

            return tiedProposals[
                UnityEngine.Random.Range(0, tiedProposals.Count)];
        }

        private bool HasReadyAttackOrder(
            UnitData unit,
            List<AttackEntry> attacks)
        {
            if (unit == null)
            {
                return false;
            }

            for (int i = 0; i < attacks.Count; i++)
            {
                AttackEntry attack = attacks[i];
                if (attack.attacker == unit &&
                    CombatResolver.CanAttack(
                        unit,
                        unitManager.GetUnitById(attack.order.targetUnitId),
                        attack.attackerDefinition))
                {
                    return true;
                }
            }
            return false;
        }

        private bool HasLivingEnemyAt(int x, int y, Faction faction)
        {
            if (unitManager == null)
            {
                return false;
            }

            List<UnitData> occupants = unitManager.GetUnitsAt(x, y);
            for (int i = 0; i < occupants.Count; i++)
            {
                UnitData occupant = occupants[i];
                if (occupant != null &&
                    occupant.IsAlive &&
                    occupant.faction != faction)
                {
                    return true;
                }
            }
            return false;
        }

        private bool HasLivingFactionUnitAt(int x, int y, Faction faction)
        {
            List<UnitData> occupants = unitManager.GetUnitsAt(x, y);
            for (int i = 0; i < occupants.Count; i++)
            {
                UnitData occupant = occupants[i];
                if (occupant != null &&
                    occupant.IsAlive &&
                    occupant.faction == faction)
                {
                    return true;
                }
            }
            return false;
        }

        private UnitDefinition GetUnitDefinition(UnitData unit)
        {
            return gameManager != null
                ? gameManager.GetUnitDefinition(GetUnitTypeId(unit))
                : null;
        }

        private bool IsRegisteredUnit(UnitData target)
        {
            foreach (UnitData unit in unitManager.Units)
            {
                if (unit == target)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsCurrentOrder(UnitData unit, Order order)
        {
            return unit != null &&
                unit.pendingOrders != null &&
                unit.pendingOrders.Count > 0 &&
                object.ReferenceEquals(unit.pendingOrders[0], order);
        }

        private static void RemoveOrder(UnitData unit, Order order)
        {
            if (unit == null || unit.pendingOrders == null || order == null)
            {
                return;
            }

            unit.pendingOrders.Remove(order);
        }

        private static string GetUnitLabel(UnitData unit)
        {
            if (unit == null)
            {
                return "不明な部隊";
            }

            return string.IsNullOrEmpty(unit.unitName)
                ? unit.unitId
                : unit.unitName;
        }

        private void AddCombatLog(string message)
        {
            string entry = "第" + turnNumber + "ターン: " + message;
            combatLog.Add(entry);
            OnCombatLogUpdated?.Invoke(entry);
        }

        private void ResetRemainingMovePoints()
        {
            if (unitManager == null)
            {
                return;
            }

            foreach (UnitData unit in unitManager.Units)
            {
                if (unit == null || !unit.IsAlive)
                {
                    continue;
                }

                UnitDefinition definition = GetUnitDefinition(unit);
                unit.remainingMovePoints =
                    definition != null ? definition.movePoints : 0;
            }
        }

        private void CheckVictory()
        {
            if (unitManager == null || State == BattleState.BattleEnded)
            {
                return;
            }

            bool enemyHeadquartersAlive = false;
            bool playerHeadquartersAlive = false;
            bool enemyAlive = false;
            bool playerAlive = false;

            foreach (UnitData unit in unitManager.Units)
            {
                if (unit == null || !unit.IsAlive)
                {
                    continue;
                }

                UnitDefinition definition = GetUnitDefinition(unit);
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
                AddCombatLog("プレイヤー軍の勝利です。");
                OnBattleEnded?.Invoke(true);
            }
            else if (!playerAlive || !playerHeadquartersAlive)
            {
                Debug.Log("Player defeat.");
                State = BattleState.BattleEnded;
                OnCommandStateChanged?.Invoke();
                AddCombatLog("プレイヤー軍の敗北です。");
                OnBattleEnded?.Invoke(false);
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
                if (occupants[i] != null &&
                    occupants[i].IsAlive &&
                    occupants[i].faction != faction)
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

            if (HasLivingEnemyAt(targetX, targetY, unit.faction))
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
            if (movePoints <= 0 ||
                totalMoveCost > movePoints ||
                totalMoveCost > unit.remainingMovePoints)
            {
                Debug.Log(
                    string.Format(
                        "移動予約不可: {0}（必要コスト={1}, 残り移動力={2}）",
                        unit.unitName,
                        totalMoveCost,
                        unit.remainingMovePoints));
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

            if (unit.pendingOrders == null)
            {
                unit.pendingOrders = new List<Order>();
            }
            ClearActionOrders(unit);
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

        private void ClearActionOrders(UnitData unit)
        {
            if (unit == null || unit.pendingOrders == null)
            {
                return;
            }

            for (int i = unit.pendingOrders.Count - 1; i >= 0; i--)
            {
                Order order = unit.pendingOrders[i];
                if (order != null)
                {
                    if (order.orderType == OrderType.Ambush)
                    {
                        unit.isAmbushed = false;
                    }
                    unit.pendingOrders.RemoveAt(i);
                }
            }
        }

        private void HandleCommandInputModeClick(Vector2 screenPosition)
        {
            if (InputMode == CommandInputMode.Normal)
            {
                return;
            }

            Vector2Int clickedCell;
            if (!TryGetClickedCell(screenPosition, out clickedCell))
            {
                return;
            }

            if (InputMode == CommandInputMode.SelectAmbushTarget)
            {
                bool issuedAny = false;
                foreach (UnitData unit in new List<UnitData>(selectedUnits))
                {
                    if (IssueAmbushOrder(
                            unit,
                            clickedCell.x,
                            clickedCell.y))
                    {
                        issuedAny = true;
                    }
                }

                if (issuedAny)
                {
                    ResetCommandInputMode();
                    OnCommandStateChanged?.Invoke();
                }
                return;
            }

            if (InputMode == CommandInputMode.SelectAssaultMoveTarget)
            {
                var plannedMoves = new List<PendingAssaultMove>();
                foreach (UnitData unit in new List<UnitData>(selectedUnits))
                {
                    List<Vector2Int> path;
                    if (TryBuildMovementPath(
                            unit,
                            clickedCell.x,
                            clickedCell.y,
                            out path))
                    {
                        plannedMoves.Add(new PendingAssaultMove
                        {
                            unit = unit,
                            path = path
                        });
                    }
                }

                if (plannedMoves.Count > 0)
                {
                    pendingAssaultMoves.Clear();
                    pendingAssaultMoves.AddRange(plannedMoves);
                    InputMode = CommandInputMode.SelectAssaultAttackTarget;
                    OnCommandStateChanged?.Invoke();
                }
                return;
            }

            if (InputMode == CommandInputMode.SelectAssaultAttackTarget)
            {
                if (!HasLivingEnemyAt(
                        clickedCell.x,
                        clickedCell.y,
                        Faction.Player))
                {
                    return;
                }

                bool issuedAny = false;
                foreach (PendingAssaultMove pendingMove
                    in new List<PendingAssaultMove>(pendingAssaultMoves))
                {
                    if (pendingMove == null ||
                        pendingMove.unit == null ||
                        !pendingMove.unit.IsAlive ||
                        !selectedUnits.Contains(pendingMove.unit) ||
                        pendingMove.path == null ||
                        pendingMove.path.Count == 0)
                    {
                        continue;
                    }

                    Vector2Int destination =
                        pendingMove.path[pendingMove.path.Count - 1];
                    if (!CombatResolver.IsAdjacent(
                            destination.x,
                            destination.y,
                            clickedCell.x,
                            clickedCell.y))
                    {
                        continue;
                    }

                    if (!IssueAssaultOrder(
                            pendingMove.unit,
                            destination.x,
                            destination.y,
                            clickedCell.x,
                            clickedCell.y))
                    {
                        continue;
                    }

                    Order assaultOrder;
                    if (HasMoveOrder(pendingMove.unit, out assaultOrder))
                    {
                        assaultOrder.path = new List<Vector2Int>(
                            pendingMove.path);
                    }
                    issuedAny = true;
                }

                if (issuedAny)
                {
                    ResetCommandInputMode();
                    OnCommandStateChanged?.Invoke();
                }
            }
        }

        private bool TryGetClickedCell(
            Vector2 screenPosition,
            out Vector2Int cellPosition)
        {
            cellPosition = Vector2Int.zero;
            if (mapView == null || battleCamera == null)
            {
                return false;
            }

            Vector3 worldPosition = battleCamera.ScreenToWorldPoint(
                new Vector3(
                    screenPosition.x,
                    screenPosition.y,
                    battleCamera.nearClipPlane));
            HexStackCounterView counter = unitBoardView != null
                ? unitBoardView.GetCounterAtWorldPosition(
                    new Vector2(worldPosition.x, worldPosition.y))
                : null;
            if (counter != null)
            {
                cellPosition = new Vector2Int(counter.X, counter.Y);
                return true;
            }

            Tilemap tilemap = mapView.GetComponent<Tilemap>();
            if (tilemap == null)
            {
                return false;
            }

            Vector3Int tileCell = tilemap.WorldToCell(worldPosition);
            cellPosition = new Vector2Int(tileCell.x, tileCell.y);
            return mapView.Grid != null &&
                mapView.Grid.IsInBounds(cellPosition.x, cellPosition.y);
        }

        private bool TryBuildMovementPath(
            UnitData unit,
            int targetX,
            int targetY,
            out List<Vector2Int> path)
        {
            path = null;
            HexGrid grid = mapView != null ? mapView.Grid : null;
            UnitDefinition definition =
                unit != null ? GetUnitDefinition(unit) : null;
            if (unit == null ||
                !unit.IsAlive ||
                grid == null ||
                definition == null ||
                !grid.IsInBounds(targetX, targetY) ||
                HasLivingEnemyAt(targetX, targetY, unit.faction))
            {
                return false;
            }

            if (unit.x == targetX && unit.y == targetY)
            {
                path = new List<Vector2Int>
                {
                    new Vector2Int(unit.x, unit.y),
                    new Vector2Int(unit.x, unit.y)
                };
                return true;
            }

            path = grid.FindPath(
                unit.x,
                unit.y,
                targetX,
                targetY,
                unit.faction,
                definition,
                unitManager);
            if (path == null || path.Count < 2)
            {
                path = null;
                return false;
            }

            int totalMoveCost = 0;
            for (int i = 1; i < path.Count; i++)
            {
                HexCell cell = grid.GetCell(path[i].x, path[i].y);
                TerrainDefinition terrain = cell == null
                    ? null
                    : grid.GetTerrainDefinition(cell.terrainId);
                int moveCost = definition.GetMoveCost(terrain);
                if (moveCost < 0)
                {
                    path = null;
                    return false;
                }
                totalMoveCost += moveCost;
            }

            if (definition.movePoints <= 0 ||
                totalMoveCost > definition.movePoints ||
                totalMoveCost > unit.remainingMovePoints)
            {
                path = null;
                return false;
            }
            return true;
        }

        private void HandleCellSelectionClick(Vector2 screenPosition)
        {
            if (mapView == null ||
                battleCamera == null ||
                unitManager == null)
            {
                return;
            }

            Vector3 worldPosition =
                battleCamera.ScreenToWorldPoint(
                    new Vector3(screenPosition.x, screenPosition.y, battleCamera.nearClipPlane));
            bool isShiftHeld =
                Input.GetKey(KeyCode.LeftShift) ||
                Input.GetKey(KeyCode.RightShift);
            HexStackCounterView counter =
                unitBoardView != null
                    ? unitBoardView.GetCounterAtWorldPosition(
                        new Vector2(worldPosition.x, worldPosition.y))
                    : null;
            if (counter != null)
            {
                int counterX = counter.X;
                int counterY = counter.Y;
                List<UnitData> cellUnits =
                    unitManager.GetUnitsAt(counterX, counterY);
                if (counter.Faction == Faction.Player)
                {
                    var counterUnits = new List<UnitData>(counter.Units);
                    if (isShiftHeld)
                    {
                        ToggleCellUnitSelection(counterUnits);
                    }
                    else
                    {
                        SelectUnits(counterUnits);
                    }
                }

                OnCellUnitsSelected?.Invoke(
                    counterX,
                    counterY,
                    cellUnits);
                return;
            }

            Tilemap tilemap = mapView.GetComponent<Tilemap>();
            if (tilemap == null)
            {
                return;
            }

            Vector3Int cellPosition = tilemap.WorldToCell(worldPosition);

            List<UnitData> clickedUnits =
                unitManager.GetUnitsAt(cellPosition.x, cellPosition.y);
            var playerUnits = new List<UnitData>();
            for (int i = 0; i < clickedUnits.Count; i++)
            {
                if (clickedUnits[i] != null &&
                    clickedUnits[i].faction == Faction.Player &&
                    clickedUnits[i].IsAlive)
                {
                    playerUnits.Add(clickedUnits[i]);
                }
            }

            if (isShiftHeld && playerUnits.Count == 0)
            {
                return;
            }

            if (playerUnits.Count > 0)
            {
                OnCellUnitsSelected?.Invoke(
                    cellPosition.x,
                    cellPosition.y,
                    clickedUnits);
                if (isShiftHeld)
                {
                    ToggleCellUnitSelection(playerUnits);
                    return;
                }
                if (playerUnits.Count == 1)
                {
                    SelectUnit(playerUnits[0]);
                }
                return;
            }

            if (!isShiftHeld)
            {
                SelectUnits(null);
                OnCellUnitsSelected?.Invoke(
                    cellPosition.x,
                    cellPosition.y,
                    new List<UnitData>());
            }
        }

        private void HandleCellOrderClick(Vector2 screenPosition)
        {
            if (selectedUnits.Count == 0 ||
                mapView == null ||
                battleCamera == null ||
                unitManager == null)
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
            UnitData targetEnemy = null;
            for (int i = 0; i < clickedUnits.Count; i++)
            {
                UnitData unit = clickedUnits[i];
                if (unit != null &&
                    unit.IsAlive &&
                    unit.faction == Faction.Enemy)
                {
                    targetEnemy = unit;
                    break;
                }
            }

            if (targetEnemy != null)
            {
                TryAttackSelectedUnits(targetEnemy);
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

            if (unit.pendingOrders.Count == 0)
            {
                return false;
            }

            Order order = unit.pendingOrders[0];
            if (order != null &&
                (order.orderType == OrderType.Move ||
                 order.orderType == OrderType.Assault))
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
                if (order != null &&
                    (order.orderType == OrderType.Move ||
                     order.orderType == OrderType.Assault))
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
