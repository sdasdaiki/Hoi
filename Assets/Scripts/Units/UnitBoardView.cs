using System.Collections;
using System.Collections.Generic;
using EdoStrategy.Combat;
using EdoStrategy.Core;
using EdoStrategy.Map;
using EdoStrategy.Command;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace EdoStrategy.Units
{
    public class UnitBoardView : MonoBehaviour
    {
        [SerializeField] private Tilemap tilemap;
        [SerializeField] private HexTilemapView mapView;
        [SerializeField] private GameManager gameManager;
        [SerializeField] private Transform unitContainer;

        private UnitManager unitManager;
        private readonly Dictionary<UnitData, UnitView> views =
            new Dictionary<UnitData, UnitView>();
        private readonly List<HexStackCounterView> counters =
            new List<HexStackCounterView>();
        private readonly HashSet<UnitData> animatingUnits =
            new HashSet<UnitData>();
        private TurnManager turnManager;
        private readonly List<SpriteRenderer> reachableHighlights =
            new List<SpriteRenderer>();
        private readonly List<GameObject> orderVisuals =
            new List<GameObject>();
        private UnitData lastHighlightedUnit;
        private string reachableOccupancyKey;
        private Material orderArrowMaterial;
        private Sprite orderArrowHeadSprite;
        private bool hasPendingAssaultMoveTarget;
        private Vector2Int pendingAssaultMoveTarget;
        private TurnManager.CommandInputMode lastInputMode;
        private bool hasObservedInputMode;

        public bool ShowAllPlannedArrows { get; private set; }

        public UnitManager UnitManager
        {
            get { return unitManager; }
        }
        public bool IsAnimatingUnits
        {
            get { return animatingUnits.Count > 0; }
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            ClearOrderVisuals();
            if (orderArrowMaterial != null)
            {
                Destroy(orderArrowMaterial);
            }
            if (orderArrowHeadSprite != null)
            {
                if (orderArrowHeadSprite.texture != null)
                {
                    Destroy(orderArrowHeadSprite.texture);
                }
                Destroy(orderArrowHeadSprite);
            }
        }

        public void Initialize(UnitManager manager)
        {
            ResolveReferences();
            Unsubscribe();
            unitManager = manager;

            if (unitManager == null)
            {
                return;
            }

            unitManager.UnitAdded += OnUnitAdded;
            unitManager.UnitMoved += OnUnitMoved;
            unitManager.UnitMovedAlongPath += OnUnitMovedAlongPath;
            unitManager.UnitRemoved += OnUnitRemoved;

            if (turnManager == null)
            {
                turnManager = FindObjectOfType<TurnManager>();
            }
            if (turnManager != null)
            {
                turnManager.OnCommandStateChanged += OnCommandStateChanged;
            }
            foreach (UnitData unit in unitManager.Units)
            {
                OnUnitAdded(unit);
            }
            OnCommandStateChanged();
        }

        public UnitView GetView(UnitData unit)
        {
            UnitView view;
            return unit != null && views.TryGetValue(unit, out view) ? view : null;
        }

        public HexStackCounterView GetCounterAtWorldPosition(Vector2 worldPos)
        {
            Physics2D.SyncTransforms();
            Collider2D[] colliders = Physics2D.OverlapPointAll(worldPos);
            var checkedCounters = new HashSet<HexStackCounterView>();
            HexStackCounterView frontmostCounter = null;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D hit = colliders[i];
                HexStackCounterView counter = hit != null
                    ? hit.GetComponentInParent<HexStackCounterView>()
                    : null;
                if (counter == null ||
                    !counter.isActiveAndEnabled ||
                    counter.Units == null ||
                    counter.Units.Count == 0 ||
                    !counters.Contains(counter) ||
                    !checkedCounters.Add(counter))
                {
                    continue;
                }

                if (frontmostCounter == null ||
                    IsCounterInFrontOf(counter, frontmostCounter))
                {
                    frontmostCounter = counter;
                }
            }

            return frontmostCounter;
        }

        private static bool IsCounterInFrontOf(
            HexStackCounterView candidate,
            HexStackCounterView current)
        {
            Renderer candidateRenderer = GetTopRenderer(candidate);
            Renderer currentRenderer = GetTopRenderer(current);
            if (candidateRenderer != null && currentRenderer != null)
            {
                int candidateLayer =
                    SortingLayer.GetLayerValueFromID(
                        candidateRenderer.sortingLayerID);
                int currentLayer =
                    SortingLayer.GetLayerValueFromID(
                        currentRenderer.sortingLayerID);
                if (candidateLayer != currentLayer)
                {
                    return candidateLayer > currentLayer;
                }
                if (candidateRenderer.sortingOrder !=
                    currentRenderer.sortingOrder)
                {
                    return candidateRenderer.sortingOrder >
                           currentRenderer.sortingOrder;
                }

                Camera camera = Camera.main;
                if (camera != null)
                {
                    float candidateDepth = camera.WorldToScreenPoint(
                        candidateRenderer.bounds.center).z;
                    float currentDepth = camera.WorldToScreenPoint(
                        currentRenderer.bounds.center).z;
                    if (!Mathf.Approximately(candidateDepth, currentDepth))
                    {
                        return candidateDepth < currentDepth;
                    }
                }
            }
            else if (candidateRenderer != null || currentRenderer != null)
            {
                return candidateRenderer != null;
            }

            return candidate.transform.GetSiblingIndex() >
                   current.transform.GetSiblingIndex();
        }

        private static Renderer GetTopRenderer(
            HexStackCounterView counter)
        {
            Renderer[] renderers =
                counter.GetComponentsInChildren<Renderer>();
            Renderer topRenderer = null;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (topRenderer == null ||
                    SortingLayer.GetLayerValueFromID(
                        renderer.sortingLayerID) >
                    SortingLayer.GetLayerValueFromID(
                        topRenderer.sortingLayerID) ||
                    renderer.sortingLayerID == topRenderer.sortingLayerID &&
                    renderer.sortingOrder > topRenderer.sortingOrder)
                {
                    topRenderer = renderer;
                }
            }

            return topRenderer;
        }

        public void RefreshAllViews()
        {
            RefreshCounters();
            RefreshPlannedArrows();
            RefreshSelectionAndReachable();
        }

        public void ToggleAllPlannedArrows()
        {
            ShowAllPlannedArrows = !ShowAllPlannedArrows;
            RefreshPlannedArrows();
        }

        private void Update()
        {
            if (turnManager == null)
            {
                turnManager = FindObjectOfType<TurnManager>();
            }
            if (turnManager == null)
            {
                return;
            }

            TurnManager.CommandInputMode inputMode =
                turnManager.InputMode;
            bool modeChanged = hasObservedInputMode &&
                inputMode != lastInputMode;
            if (inputMode ==
                    TurnManager.CommandInputMode.SelectAssaultMoveTarget &&
                lastInputMode !=
                    TurnManager.CommandInputMode.SelectAssaultMoveTarget)
            {
                hasPendingAssaultMoveTarget = false;
            }

            bool isAssaultMoveClick =
                inputMode ==
                    TurnManager.CommandInputMode.SelectAssaultMoveTarget ||
                inputMode ==
                    TurnManager.CommandInputMode.SelectAssaultAttackTarget &&
                lastInputMode ==
                    TurnManager.CommandInputMode.SelectAssaultMoveTarget;
            if (isAssaultMoveClick &&
                Input.GetMouseButtonDown(1) &&
                TryGetCursorCell(out Vector2Int clickedCell))
            {
                pendingAssaultMoveTarget = clickedCell;
                hasPendingAssaultMoveTarget = true;
            }

            if (inputMode == TurnManager.CommandInputMode.Normal ||
                inputMode ==
                    TurnManager.CommandInputMode.SelectAmbushTarget)
            {
                hasPendingAssaultMoveTarget = false;
            }

            if (modeChanged)
            {
                RefreshAllViews();
            }
            lastInputMode = inputMode;
            hasObservedInputMode = true;
        }

        private void OnCommandStateChanged()
        {
            reachableOccupancyKey = null;
            RefreshAllViews();
        }

        private bool TryGetCursorCell(out Vector2Int cellPosition)
        {
            cellPosition = Vector2Int.zero;
            if (tilemap == null)
            {
                return false;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                camera = FindObjectOfType<Camera>();
            }
            if (camera == null)
            {
                return false;
            }

            Vector3 worldPosition = camera.ScreenToWorldPoint(
                new Vector3(
                    Input.mousePosition.x,
                    Input.mousePosition.y,
                    camera.nearClipPlane));
            HexStackCounterView counter = GetCounterAtWorldPosition(
                new Vector2(worldPosition.x, worldPosition.y));
            if (counter != null)
            {
                cellPosition = new Vector2Int(counter.X, counter.Y);
            }
            else
            {
                Vector3Int tileCell = tilemap.WorldToCell(worldPosition);
                cellPosition = new Vector2Int(tileCell.x, tileCell.y);
            }

            return mapView != null &&
                mapView.Grid != null &&
                mapView.Grid.IsInBounds(
                    cellPosition.x,
                    cellPosition.y);
        }

        private void RefreshCounters()
        {
            for (int i = 0; i < counters.Count; i++)
            {
                if (counters[i] != null)
                {
                    Destroy(counters[i].gameObject);
                }
            }
            counters.Clear();

            if (unitManager == null)
            {
                return;
            }

            var groups = new List<CounterGroup>();
            foreach (UnitData unit in unitManager.Units)
            {
                if (unit == null || !unit.IsAlive)
                {
                    continue;
                }
                if (animatingUnits.Contains(unit))
                {
                    continue;
                }

                string unitTypeId = GetUnitTypeId(unit);
                CounterGroup group = null;
                for (int i = 0; i < groups.Count; i++)
                {
                    CounterGroup candidate = groups[i];
                    if (candidate.X == unit.x &&
                        candidate.Y == unit.y &&
                        candidate.Faction == unit.faction &&
                        candidate.UnitTypeId == unitTypeId)
                    {
                        group = candidate;
                        break;
                    }
                }

                if (group == null)
                {
                    group = new CounterGroup(
                        unit.x,
                        unit.y,
                        unit.faction,
                        unitTypeId);
                    groups.Add(group);
                }
                group.Units.Add(unit);
            }

            groups.Sort((left, right) =>
            {
                int xOrder = left.X.CompareTo(right.X);
                if (xOrder != 0)
                {
                    return xOrder;
                }
                int yOrder = left.Y.CompareTo(right.Y);
                if (yOrder != 0)
                {
                    return yOrder;
                }

                int factionOrder = left.Faction.CompareTo(right.Faction);
                return factionOrder != 0
                    ? factionOrder
                    : string.Compare(
                        left.UnitTypeId,
                        right.UnitTypeId,
                        System.StringComparison.Ordinal);
            });

            float cellWidth = tilemap != null
                ? Mathf.Abs(tilemap.cellSize.x)
                : 1f;
            float cellHeight = tilemap != null
                ? Mathf.Abs(tilemap.cellSize.y)
                : 1f;
            int currentIndex = 0;
            while (currentIndex < groups.Count)
            {
                int firstIndex = currentIndex;
                int x = groups[firstIndex].X;
                int y = groups[firstIndex].Y;
                while (currentIndex < groups.Count &&
                       groups[currentIndex].X == x &&
                       groups[currentIndex].Y == y)
                {
                    currentIndex++;
                }

                int groupCount = currentIndex - firstIndex;
                float counterHeight = Mathf.Min(
                    cellHeight * 0.25f,
                    cellHeight * 0.84f / groupCount);
                float verticalSpacing = counterHeight * 1.08f;
                Vector3 center = tilemap != null
                    ? tilemap.GetCellCenterWorld(new Vector3Int(x, y, 0))
                    : new Vector3(x, y, 0f);

                for (int i = firstIndex; i < currentIndex; i++)
                {
                    CounterGroup group = groups[i];
                    bool selected = false;
                    if (turnManager != null)
                    {
                        for (int j = 0; j < group.Units.Count; j++)
                        {
                            if (turnManager.IsUnitSelected(group.Units[j]))
                            {
                                selected = true;
                                break;
                            }
                        }
                    }

                    UnitDefinition definition = gameManager != null
                        ? gameManager.GetUnitDefinition(group.UnitTypeId)
                        : null;
                    GameObject counterObject =
                        new GameObject("Hex Stack Counter");
                    counterObject.transform.SetParent(unitContainer, false);
                    counterObject.transform.position = center + new Vector3(
                        0f,
                        ((groupCount - 1) * 0.5f -
                         (i - firstIndex)) * verticalSpacing,
                        -0.2f);

                    HexStackCounterView counter =
                        counterObject.AddComponent<HexStackCounterView>();
                    counter.Initialize(
                        group.Units,
                        definition,
                        group.UnitTypeId,
                        counterObject.transform.position,
                        new Vector2(
                            cellWidth * 0.76f,
                            counterHeight),
                        selected);
                    counters.Add(counter);
                }
            }
        }

        private void RefreshSelectionAndReachable()
        {
            UnitData selectedUnit =
                turnManager != null ? turnManager.SelectedUnit : null;

            foreach (KeyValuePair<UnitData, UnitView> entry in views)
            {
                if (entry.Value != null)
                {
                    entry.Value.SetSelected(entry.Key == selectedUnit);
                }
            }

            if (turnManager == null ||
                turnManager.State != BattleState.PlayerCommandPhase ||
                selectedUnit == null ||
                turnManager.SelectedUnits.Count == 0 ||
                unitManager == null ||
                mapView == null ||
                mapView.Grid == null)
            {
                ClearReachableHighlights();
                lastHighlightedUnit = null;
                reachableOccupancyKey = null;
                return;
            }

            var occupancyKey = new System.Text.StringBuilder();
            occupancyKey.Append(selectedUnit.unitId)
                .Append(':')
                .Append(selectedUnit.x)
                .Append(',')
                .Append(selectedUnit.y)
                .Append("|mode:")
                .Append((int)turnManager.InputMode)
                .Append("|assaultTarget:")
                .Append(hasPendingAssaultMoveTarget
                    ? pendingAssaultMoveTarget.x
                    : -1)
                .Append(',')
                .Append(hasPendingAssaultMoveTarget
                    ? pendingAssaultMoveTarget.y
                    : -1);
            foreach (UnitData selected in turnManager.SelectedUnits)
            {
                if (selected != null)
                {
                    occupancyKey.Append("|selected:")
                        .Append(selected.unitId)
                        .Append(':')
                        .Append(selected.remainingMovePoints);
                }
            }
            foreach (UnitData unit in unitManager.Units)
            {
                occupancyKey.Append('|')
                    .Append(unit.unitId)
                    .Append(':')
                    .Append(unit.x)
                    .Append(',')
                    .Append(unit.y);
            }
            string newOccupancyKey = occupancyKey.ToString();

            if (lastHighlightedUnit == selectedUnit &&
                reachableOccupancyKey == newOccupancyKey)
            {
                return;
            }
            lastHighlightedUnit = selectedUnit;
            reachableOccupancyKey = newOccupancyKey;

            ClearReachableHighlights();

            IReadOnlyList<UnitData> selectedUnits = turnManager.SelectedUnits;
            var reachableCells = new HashSet<Vector2Int>();
            if (selectedUnits.Count == 1)
            {
                UnitData selected = selectedUnits[0];
                if (selected != null && selected.IsAlive)
                {
                    UnitDefinition definition =
                        gameManager != null
                            ? gameManager.GetUnitDefinition(
                                GetUnitTypeId(selected))
                            : null;
                    List<Vector2Int> unitReachable =
                        mapView.Grid.GetReachableCells(
                            selected.x,
                            selected.y,
                            selected.remainingMovePoints,
                            selected.faction,
                            definition,
                            unitManager);
                    reachableCells.UnionWith(unitReachable);
                }
            }
            else
            {
                bool firstUnit = true;
                UnitData firstSelected = selectedUnits[0];
                bool allUnitsShareCell =
                    firstSelected != null && firstSelected.IsAlive;
                for (int i = 0; i < selectedUnits.Count; i++)
                {
                    UnitData selected = selectedUnits[i];
                    if (selected == null || !selected.IsAlive)
                    {
                        reachableCells.Clear();
                        allUnitsShareCell = false;
                        break;
                    }

                    if (allUnitsShareCell &&
                        (selected.x != firstSelected.x ||
                         selected.y != firstSelected.y))
                    {
                        allUnitsShareCell = false;
                    }

                    UnitDefinition definition =
                        gameManager != null
                            ? gameManager.GetUnitDefinition(
                                GetUnitTypeId(selected))
                            : null;
                    List<Vector2Int> unitReachable =
                        mapView.Grid.GetReachableCells(
                            selected.x,
                            selected.y,
                            selected.remainingMovePoints,
                            selected.faction,
                            definition,
                            unitManager);
                    var unitReachableCells =
                        new HashSet<Vector2Int>(unitReachable)
                        {
                            new Vector2Int(selected.x, selected.y)
                        };

                    if (firstUnit)
                    {
                        reachableCells.UnionWith(unitReachableCells);
                        firstUnit = false;
                    }
                    else
                    {
                        reachableCells.IntersectWith(unitReachableCells);
                    }

                    if (reachableCells.Count == 0)
                    {
                        break;
                    }
                }

                if (allUnitsShareCell)
                {
                    reachableCells.Remove(
                        new Vector2Int(firstSelected.x, firstSelected.y));
                }
            }

            Sprite highlightSprite =
                LoadBorderedHexSprite();
            TurnManager.CommandInputMode inputMode =
                turnManager.InputMode;
            bool showMoveRange =
                inputMode == TurnManager.CommandInputMode.Normal ||
                inputMode ==
                    TurnManager.CommandInputMode.SelectAssaultMoveTarget;
            if (inputMode ==
                TurnManager.CommandInputMode.SelectAssaultMoveTarget)
            {
                foreach (UnitData selected in selectedUnits)
                {
                    if (selected != null && selected.IsAlive)
                    {
                        reachableCells.Add(
                            new Vector2Int(selected.x, selected.y));
                    }
                }
            }

            if (showMoveRange)
            {
                foreach (Vector2Int cell in reachableCells)
                {
                    AddCellHighlight(
                        cell,
                        new Color(0.2f, 0.7f, 1f, 0.35f),
                        "ReachableHighlight",
                        6,
                        highlightSprite);
                }
            }

            if (inputMode == TurnManager.CommandInputMode.Normal)
            {
                HashSet<Vector2Int> attackableCells =
                    GetAttackableEnemyCells(selectedUnits);
                foreach (Vector2Int cell in attackableCells)
                {
                    AddCellHighlight(
                        cell,
                        new Color(1f, 0.25f, 0.25f, 0.45f),
                        "AttackableHighlight",
                        7,
                        highlightSprite);
                }
            }
            else if (inputMode ==
                TurnManager.CommandInputMode.SelectAmbushTarget)
            {
                HashSet<Vector2Int> ambushCells =
                    GetAmbushTargetCells(selectedUnits);
                foreach (Vector2Int cell in ambushCells)
                {
                    AddCellHighlight(
                        cell,
                        new Color(1f, 0.25f, 0.25f, 0.45f),
                        "AmbushTargetHighlight",
                        7,
                        highlightSprite);
                }
            }
            else if (inputMode ==
                TurnManager.CommandInputMode.SelectAssaultAttackTarget &&
                hasPendingAssaultMoveTarget)
            {
                HashSet<Vector2Int> assaultCells =
                    GetAssaultTargetCells(pendingAssaultMoveTarget);
                foreach (Vector2Int cell in assaultCells)
                {
                    AddCellHighlight(
                        cell,
                        new Color(1f, 0.25f, 0.25f, 0.45f),
                        "AssaultTargetHighlight",
                        7,
                        highlightSprite);
                }
                AddCellHighlight(
                    pendingAssaultMoveTarget,
                    new Color(1f, 0.85f, 0.2f, 0.5f),
                    "AssaultMoveTargetHighlight",
                    8,
                    highlightSprite);
            }
        }

        private void AddCellHighlight(
            Vector2Int cell,
            Color color,
            string name,
            int sortingOrder,
            Sprite highlightSprite)
        {
            GameObject highlightObject = new GameObject(
                name + "_" + cell.x + "_" + cell.y);
            highlightObject.transform.SetParent(
                tilemap != null ? tilemap.transform : unitContainer,
                false);
            highlightObject.transform.position =
                tilemap != null
                    ? tilemap.GetCellCenterWorld(
                        new Vector3Int(cell.x, cell.y, 0))
                    : new Vector3(cell.x, cell.y, 0f);

            SpriteRenderer renderer =
                highlightObject.AddComponent<SpriteRenderer>();
            renderer.sprite = highlightSprite != null
                ? highlightSprite
                : CreateHighlightCircleSprite();
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            highlightObject.transform.localScale = Vector3.one;
            reachableHighlights.Add(renderer);
        }

        private HashSet<Vector2Int> GetAttackableEnemyCells(
            IReadOnlyList<UnitData> attackers)
        {
            var cells = new HashSet<Vector2Int>();
            if (unitManager == null || attackers == null)
            {
                return cells;
            }

            foreach (UnitData enemy in unitManager.Units)
            {
                if (enemy == null ||
                    !enemy.IsAlive ||
                    enemy.faction != Faction.Enemy)
                {
                    continue;
                }

                for (int i = 0; i < attackers.Count; i++)
                {
                    UnitData attacker = attackers[i];
                    if (attacker == null || !attacker.IsAlive)
                    {
                        continue;
                    }

                    UnitDefinition definition = gameManager != null
                        ? gameManager.GetUnitDefinition(
                            GetUnitTypeId(attacker))
                        : null;
                    if (CombatResolver.CanAttack(
                            attacker,
                            enemy,
                            definition))
                    {
                        cells.Add(new Vector2Int(enemy.x, enemy.y));
                        break;
                    }
                }
            }
            return cells;
        }

        private HashSet<Vector2Int> GetAmbushTargetCells(
            IReadOnlyList<UnitData> units)
        {
            var cells = new HashSet<Vector2Int>();
            if (units == null || mapView == null || mapView.Grid == null)
            {
                return cells;
            }

            for (int i = 0; i < units.Count; i++)
            {
                UnitData unit = units[i];
                if (unit == null || !unit.IsAlive)
                {
                    continue;
                }

                cells.UnionWith(GetAdjacentCells(
                    new Vector2Int(unit.x, unit.y)));
            }
            return cells;
        }

        private HashSet<Vector2Int> GetAssaultTargetCells(
            Vector2Int moveTarget)
        {
            return GetAdjacentCells(moveTarget);
        }

        private HashSet<Vector2Int> GetAdjacentCells(Vector2Int origin)
        {
            var cells = new HashSet<Vector2Int>();
            if (mapView == null || mapView.Grid == null)
            {
                return cells;
            }

            for (int direction = 0; direction < 6; direction++)
            {
                Vector2Int neighbor = HexUtility.GetNeighborOffset(
                    origin.x,
                    origin.y,
                    direction);
                if (mapView.Grid.IsInBounds(neighbor.x, neighbor.y))
                {
                    cells.Add(neighbor);
                }
            }
            return cells;
        }

        private Sprite LoadBorderedHexSprite()
        {
            Sprite sprite =
                Resources.Load<Sprite>("HexagonBordered");
            if (sprite == null)
            {
                sprite = AssetDatabaseLoadSprite(
                    "Assets/Tiles/HexagonBordered.png");
            }
            return sprite;
        }

        private static Sprite AssetDatabaseLoadSprite(string path)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
#else
            return null;
#endif
        }

        private static Sprite CreateHighlightCircleSprite()
        {
            const int size = 32;
            var texture =
                new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            Vector2 center =
                new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance =
                        Vector2.Distance(new Vector2(x, y), center);
                    pixels[y * size + x] =
                        distance <= (size - 2) * 0.5f
                            ? new Color32(255, 255, 255, 255)
                            : new Color32(255, 255, 255, 0);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            texture.hideFlags = HideFlags.DontSave;
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                size);
        }

        private void ClearReachableHighlights()
        {
            foreach (SpriteRenderer highlight in reachableHighlights)
            {
                if (highlight != null)
                {
                    Destroy(highlight.gameObject);
                }
            }
            reachableHighlights.Clear();
        }

        private void RefreshPlannedArrows()
        {
            ClearOrderVisuals();
            foreach (KeyValuePair<UnitData, UnitView> entry in views)
            {
                UnitData unit = entry.Key;
                UnitView view = entry.Value;
                if (unit == null || view == null)
                {
                    continue;
                }

                bool visible =
                    unit.faction == Faction.Player &&
                    HasMoveOrder(unit) &&
                    (ShowAllPlannedArrows ||
                     (turnManager != null &&
                      turnManager.IsUnitSelected(unit)));
                view.SetArrowVisible(visible);
            }

            if (unitManager == null)
            {
                return;
            }

            foreach (UnitData unit in unitManager.Units)
            {
                if (unit == null ||
                    !unit.IsAlive ||
                    unit.faction != Faction.Player ||
                    unit.pendingOrders == null ||
                    (!ShowAllPlannedArrows &&
                     (turnManager == null ||
                      !turnManager.IsUnitSelected(unit))))
                {
                    continue;
                }

                for (int i = 0; i < unit.pendingOrders.Count; i++)
                {
                    Order order = unit.pendingOrders[i];
                    if (order == null)
                    {
                        continue;
                    }

                    switch (order.orderType)
                    {
                        case OrderType.Attack:
                            DrawAttackOrder(unit, order);
                            break;
                        case OrderType.Assault:
                            DrawAssaultOrder(unit, order);
                            break;
                        case OrderType.Ambush:
                            DrawAmbushOrder(unit, order);
                            break;
                        case OrderType.Defend:
                            DrawOrderBadge(unit, "[防]", new Color(
                                0.35f, 0.85f, 1f, 0.95f));
                            break;
                        case OrderType.Entrench:
                            DrawOrderBadge(unit, "[壕]", new Color(
                                0.85f, 0.72f, 0.4f, 0.95f));
                            break;
                    }
                }
            }
        }

        private void DrawAttackOrder(UnitData attacker, Order order)
        {
            UnitData target = unitManager.GetUnitById(order.targetUnitId);
            if (target == null ||
                !target.IsAlive ||
                target.faction != Faction.Enemy)
            {
                return;
            }

            DrawOrderArrow(
                new List<Vector2Int>
                {
                    new Vector2Int(attacker.x, attacker.y),
                    new Vector2Int(target.x, target.y)
                },
                new Color(1f, 0.2f, 0.18f, 0.95f),
                "AttackOrderArrow",
                14);
        }

        private void DrawAssaultOrder(UnitData unit, Order order)
        {
            if (order.path != null && order.path.Count > 1)
            {
                DrawOrderArrow(
                    order.path,
                    new Color(1f, 0.85f, 0.2f, 0.95f),
                    "AssaultMoveArrow",
                    13);
            }

            if (order.secondaryTargetX < 0 ||
                order.secondaryTargetY < 0)
            {
                return;
            }

            Vector2Int destination = order.path != null &&
                order.path.Count > 0
                    ? order.path[order.path.Count - 1]
                    : new Vector2Int(unit.x, unit.y);
            DrawOrderArrow(
                new List<Vector2Int>
                {
                    destination,
                    new Vector2Int(
                        order.secondaryTargetX,
                        order.secondaryTargetY)
                },
                new Color(1f, 0.2f, 0.18f, 0.95f),
                "AssaultAttackArrow",
                15);
            DrawOrderCellMarker(
                new Vector2Int(
                    order.secondaryTargetX,
                    order.secondaryTargetY),
                new Color(1f, 0.3f, 0.12f, 0.55f),
                "AssaultTargetMarker",
                10);
        }

        private void DrawAmbushOrder(UnitData unit, Order order)
        {
            if (order.secondaryTargetX < 0 ||
                order.secondaryTargetY < 0)
            {
                return;
            }

            Vector2Int target = new Vector2Int(
                order.secondaryTargetX,
                order.secondaryTargetY);
            DrawOrderArrow(
                new List<Vector2Int>
                {
                    new Vector2Int(unit.x, unit.y),
                    target
                },
                new Color(1f, 0.2f, 0.18f, 0.95f),
                "AmbushWatchArrow",
                14);
            DrawOrderCellMarker(
                target,
                new Color(1f, 0.25f, 0.25f, 0.45f),
                "AmbushWatchMarker",
                10);
        }

        private void DrawOrderBadge(
            UnitData unit,
            string text,
            Color color)
        {
            GameObject badgeObject = new GameObject("OrderBadge_" + text);
            badgeObject.transform.SetParent(
                unitContainer != null ? unitContainer : transform,
                false);
            badgeObject.transform.position =
                GetCellWorldPosition(unit.x, unit.y) +
                new Vector3(0f, 0.34f, -0.1f);
            TextMesh badge = badgeObject.AddComponent<TextMesh>();
            badge.text = text;
            badge.anchor = TextAnchor.MiddleCenter;
            badge.alignment = TextAlignment.Center;
            badge.fontSize = 36;
            badge.characterSize = 0.075f;
            badge.color = color;
            Renderer badgeRenderer = badge.GetComponent<Renderer>();
            if (badgeRenderer != null)
            {
                badgeRenderer.sortingOrder = 20;
            }
            orderVisuals.Add(badgeObject);
        }

        private void DrawOrderArrow(
            IReadOnlyList<Vector2Int> path,
            Color color,
            string name,
            int sortingOrder)
        {
            if (path == null || path.Count < 2)
            {
                return;
            }

            EnsureOrderArrowAssets();
            if (orderArrowMaterial == null)
            {
                return;
            }

            GameObject lineObject = new GameObject(name);
            lineObject.transform.SetParent(
                tilemap != null ? tilemap.transform : unitContainer,
                false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = path.Count;
            line.startWidth = 0.055f;
            line.endWidth = 0.055f;
            line.numCapVertices = 2;
            line.material = orderArrowMaterial;
            line.startColor = color;
            line.endColor = color;
            line.sortingOrder = sortingOrder;
            var points = new Vector3[path.Count];
            for (int i = 0; i < path.Count; i++)
            {
                points[i] = GetCellWorldPosition(path[i].x, path[i].y);
            }
            line.SetPositions(points);
            orderVisuals.Add(lineObject);

            if (orderArrowHeadSprite == null)
            {
                return;
            }

            GameObject headObject = new GameObject(name + "_Head");
            headObject.transform.SetParent(
                tilemap != null ? tilemap.transform : unitContainer,
                false);
            SpriteRenderer head = headObject.AddComponent<SpriteRenderer>();
            head.sprite = orderArrowHeadSprite;
            head.color = color;
            head.sortingOrder = sortingOrder + 1;
            Vector3 direction =
                points[points.Length - 1] - points[points.Length - 2];
            headObject.transform.position = points[points.Length - 1];
            if (direction.sqrMagnitude > 0.0001f)
            {
                float angle = Mathf.Atan2(direction.y, direction.x) *
                    Mathf.Rad2Deg - 90f;
                headObject.transform.rotation =
                    Quaternion.Euler(0f, 0f, angle);
            }
            headObject.transform.localScale =
                new Vector3(0.2f, 0.2f, 1f);
            orderVisuals.Add(headObject);
        }

        private void DrawOrderCellMarker(
            Vector2Int cell,
            Color color,
            string name,
            int sortingOrder)
        {
            GameObject markerObject = new GameObject(
                name + "_" + cell.x + "_" + cell.y);
            markerObject.transform.SetParent(
                tilemap != null ? tilemap.transform : unitContainer,
                false);
            markerObject.transform.position =
                GetCellWorldPosition(cell.x, cell.y);
            SpriteRenderer marker =
                markerObject.AddComponent<SpriteRenderer>();
            marker.sprite = LoadBorderedHexSprite();
            if (marker.sprite == null)
            {
                marker.sprite = CreateHighlightCircleSprite();
            }
            marker.color = color;
            marker.sortingOrder = sortingOrder;
            orderVisuals.Add(markerObject);
        }

        private void EnsureOrderArrowAssets()
        {
            if (orderArrowMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    orderArrowMaterial = new Material(shader);
                }
            }
            if (orderArrowHeadSprite == null)
            {
                orderArrowHeadSprite = CreateOrderArrowHeadSprite();
            }
        }

        private static Sprite CreateOrderArrowHeadSprite()
        {
            const int size = 24;
            var texture =
                new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float halfWidth =
                    (size - y - 1) / (float)(size - 1) * size * 0.5f;
                float center = (size - 1) * 0.5f;
                for (int x = 0; x < size; x++)
                {
                    bool inside = y < size - 2 &&
                        Mathf.Abs(x - center) <= halfWidth;
                    pixels[y * size + x] = inside
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            texture.hideFlags = HideFlags.DontSave;
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                size);
        }

        private void ClearOrderVisuals()
        {
            for (int i = 0; i < orderVisuals.Count; i++)
            {
                if (orderVisuals[i] != null)
                {
                    Destroy(orderVisuals[i]);
                }
            }
            orderVisuals.Clear();
        }

        private static bool HasMoveOrder(UnitData unit)
        {
            if (unit == null || unit.pendingOrders == null)
            {
                return false;
            }

            foreach (Order order in unit.pendingOrders)
            {
                if (order != null && order.orderType == OrderType.Move)
                {
                    return true;
                }
            }
            return false;
        }

        private void ResolveReferences()
        {
            if (tilemap == null)
            {
                tilemap = mapView != null
                    ? mapView.GetComponent<Tilemap>()
                    : FindObjectOfType<Tilemap>();
            }
            if (mapView == null)
            {
                mapView = FindObjectOfType<HexTilemapView>();
            }
            if (gameManager == null)
            {
                gameManager = FindObjectOfType<GameManager>();
            }
            if (unitContainer == null)
            {
                unitContainer = transform;
            }
        }

        private void Unsubscribe()
        {
            if (turnManager != null)
            {
                turnManager.OnCommandStateChanged -= OnCommandStateChanged;
            }
            if (unitManager == null)
            {
                return;
            }

            unitManager.UnitAdded -= OnUnitAdded;
            unitManager.UnitMoved -= OnUnitMoved;
            unitManager.UnitMovedAlongPath -= OnUnitMovedAlongPath;
            unitManager.UnitRemoved -= OnUnitRemoved;
        }

        private void OnUnitAdded(UnitData unit)
        {
            if (unit == null || views.ContainsKey(unit))
            {
                return;
            }

            ResolveReferences();
            var viewObject = new GameObject(
                string.IsNullOrEmpty(unit.unitName) ? "UnitView" : unit.unitName);
            viewObject.transform.SetParent(unitContainer, false);

            UnitView view = viewObject.AddComponent<UnitView>();
            UnitDefinition definition = gameManager != null
                ? gameManager.GetUnitDefinition(GetUnitTypeId(unit))
                : null;
            view.Initialize(unit, definition, tilemap);
            view.SetCounterManaged(true);
            views.Add(unit, view);
            RefreshAllViews();
        }

        private void OnUnitMovedAlongPath(
            UnitData unit,
            Vector2Int from,
            Vector2Int to,
            IReadOnlyList<Vector2Int> path)
        {
            if (unit == null || !animatingUnits.Add(unit))
            {
                return;
            }

            UnitView view;
            if (unit != null && views.TryGetValue(unit, out view))
            {
                view.Refresh();
            }

            RefreshCounters();
            var worldPath = new List<Vector3>();
            if (path != null)
            {
                for (int i = 0; i < path.Count; i++)
                {
                    worldPath.Add(GetCellWorldPosition(path[i].x, path[i].y));
                }
            }
            if (worldPath.Count == 0 ||
                path == null ||
                path.Count == 0 ||
                path[0] != from)
            {
                worldPath.Insert(0, GetCellWorldPosition(from.x, from.y));
            }
            if (path == null ||
                path.Count == 0 ||
                path[path.Count - 1] != to)
            {
                worldPath.Add(GetCellWorldPosition(to.x, to.y));
            }

            UnitDefinition definition = gameManager != null
                ? gameManager.GetUnitDefinition(GetUnitTypeId(unit))
                : null;
            Vector3 startPosition =
                GetCellWorldPosition(from.x, from.y);
            GameObject movingCounterObject =
                new GameObject("Moving Hex Stack Counter");
            movingCounterObject.transform.SetParent(unitContainer, false);
            movingCounterObject.transform.position = startPosition;

            HexStackCounterView movingCounter =
                movingCounterObject.AddComponent<HexStackCounterView>();
            float cellWidth = tilemap != null
                ? Mathf.Abs(tilemap.cellSize.x)
                : 1f;
            float cellHeight = tilemap != null
                ? Mathf.Abs(tilemap.cellSize.y)
                : 1f;
            movingCounter.Initialize(
                new[] { unit },
                definition,
                GetUnitTypeId(unit),
                startPosition,
                new Vector2(cellWidth * 0.76f, cellHeight * 0.25f),
                turnManager != null && turnManager.IsUnitSelected(unit));
            StartCoroutine(
                AnimateUnitCounter(unit, movingCounter, worldPath));
        }

        private void OnUnitMoved(UnitData unit)
        {
            if (unit == null || animatingUnits.Contains(unit))
            {
                return;
            }

            UnitView view;
            if (views.TryGetValue(unit, out view) && view != null)
            {
                view.Refresh();
            }
            RefreshAllViews();
        }

        private IEnumerator AnimateUnitCounter(
            UnitData unit,
            HexStackCounterView movingCounter,
            List<Vector3> worldPath)
        {
            const float segmentDuration = 0.3f;
            for (int i = 1; i < worldPath.Count; i++)
            {
                Vector3 start = movingCounter.transform.position;
                Vector3 end = worldPath[i];
                end.z = start.z;
                float elapsed = 0f;
                while (elapsed < segmentDuration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float progress = Mathf.Clamp01(
                        elapsed / segmentDuration);
                    movingCounter.transform.position =
                        Vector3.Lerp(start, end, progress);
                    yield return null;
                }
                movingCounter.transform.position = end;
            }

            if (movingCounter != null)
            {
                Destroy(movingCounter.gameObject);
            }
            animatingUnits.Remove(unit);
            RefreshAllViews();
        }

        private Vector3 GetCellWorldPosition(int x, int y)
        {
            Vector3 position = tilemap != null
                ? tilemap.GetCellCenterWorld(new Vector3Int(x, y, 0))
                : new Vector3(x, y, 0f);
            position.z -= 0.2f;
            return position;
        }

        private void OnUnitRemoved(UnitData unit)
        {
            UnitView view;
            if (unit == null)
            {
                return;
            }

            if (views.TryGetValue(unit, out view))
            {
                views.Remove(unit);
                if (view != null)
                {
                    Destroy(view.gameObject);
                }
            }
            RefreshAllViews();
        }

        private string GetUnitTypeId(UnitData unit)
        {
            if (unit == null || unit.battalions == null ||
                unit.battalions.Count == 0 || unit.battalions[0] == null)
            {
                return null;
            }
            return unit.battalions[0].unitTypeId;
        }

        private sealed class CounterGroup
        {
            public readonly int X;
            public readonly int Y;
            public readonly Faction Faction;
            public readonly string UnitTypeId;
            public readonly List<UnitData> Units = new List<UnitData>();

            public CounterGroup(
                int x,
                int y,
                Faction faction,
                string unitTypeId)
            {
                X = x;
                Y = y;
                Faction = faction;
                UnitTypeId = unitTypeId;
            }
        }
    }
}
