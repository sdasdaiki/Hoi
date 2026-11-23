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
            unitManager.UnitRemoved += OnUnitRemoved;

            if (turnManager == null)
            {
                turnManager = FindObjectOfType<TurnManager>();
            }
            if (turnManager != null)
            {
                turnManager.OnCommandStateChanged += OnCommandStateChanged;
            }
            OnCommandStateChanged();

            foreach (UnitData unit in unitManager.Units)
            {
                OnUnitAdded(unit);
            }
        }

        public UnitView GetView(UnitData unit)
        {
            UnitView view;
            return unit != null && views.TryGetValue(unit, out view) ? view : null;
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
            RefreshPlannedArrows();
            RefreshSelectionAndReachable();
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
            UnitData selectedUnit =
                turnManager != null ? turnManager.SelectedUnit : null;

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
                     unit == selectedUnit);
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
            views.Add(unit, view);
        }

        private void OnUnitMoved(UnitData unit)
        {
            UnitView view;
            if (unit != null && views.TryGetValue(unit, out view))
            {
                view.Refresh();
            }
            OnCommandStateChanged();
        }

        private void OnUnitRemoved(UnitData unit)
        {
            UnitView view;
            if (unit == null || !views.TryGetValue(unit, out view))
            {
                return;
            }

            views.Remove(unit);
            if (view != null)
            {
                Destroy(view.gameObject);
            }
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
    }
}
