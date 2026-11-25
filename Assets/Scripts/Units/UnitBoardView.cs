using System.Collections;
using System.Collections.Generic;
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
        private UnitData lastHighlightedUnit;
        private string reachableOccupancyKey;

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
        }

        private void OnCommandStateChanged()
        {
            RefreshAllViews();
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
                .Append(selectedUnit.y);
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

            UnitDefinition definition =
                gameManager != null
                    ? gameManager.GetUnitDefinition(
                        GetUnitTypeId(selectedUnit))
                    : null;
            int movePoints =
                definition != null ? definition.movePoints : 0;
            if (movePoints <= 0)
            {
                return;
            }

            List<Vector2Int> reachable =
                mapView.Grid.GetReachableCells(
                    selectedUnit.x,
                    selectedUnit.y,
                    movePoints,
                    Faction.Player,
                    definition,
                    unitManager);

            Sprite highlightSprite =
                LoadBorderedHexSprite();
            foreach (Vector2Int cell in reachable)
            {
                GameObject highlightObject =
                    new GameObject(
                        "ReachableHighlight_" + cell.x + "_" + cell.y);
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
                renderer.sprite =
                    highlightSprite != null
                        ? highlightSprite
                        : CreateHighlightCircleSprite();
                renderer.color =
                    new Color(0.2f, 0.7f, 1f, 0.35f);
                renderer.sortingOrder = 6;
                highlightObject.transform.localScale =
                    Vector3.one;
                reachableHighlights.Add(renderer);
            }
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
