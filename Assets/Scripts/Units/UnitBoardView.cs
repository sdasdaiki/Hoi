using System.Collections.Generic;
using EdoStrategy.Core;
using EdoStrategy.Map;
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
            RefreshPlannedArrows();
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
                    unit.hasPlannedMove &&
                    (ShowAllPlannedArrows ||
                     unit == selectedUnit);
                view.SetArrowVisible(visible);
            }
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
