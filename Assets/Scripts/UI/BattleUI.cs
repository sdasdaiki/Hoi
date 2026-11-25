using System.Collections.Generic;
using EdoStrategy.Command;
using EdoStrategy.Core;
using EdoStrategy.Map;
using EdoStrategy.Units;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EdoStrategy.UI
{
    public class BattleUI : MonoBehaviour
    {
        private static Sprite uiRectangleSprite;

        [SerializeField] private TurnManager turnManager;
        [SerializeField] private UnitBoardView unitBoardView;
        [SerializeField] private TMP_FontAsset japaneseFont;

        private GameObject statusPanel;
        private TextMeshProUGUI statusLabel;
        private Button startBattleButton;
        private Button endTurnButton;
        private Button plannedArrowToggleButton;
        private TextMeshProUGUI plannedArrowToggleLabel;
        private GameObject unitListPanel;
        private RectTransform unitListContent;
        private TurnManager subscribedTurnManager;
        private readonly List<UnitData> displayedUnits =
            new List<UnitData>();
        private readonly List<UnitListRow> unitListRows =
            new List<UnitListRow>();

        private void Awake()
        {
#if UNITY_EDITOR
            EnsureJapaneseFont();
#endif
            if (turnManager == null)
            {
                turnManager = FindObjectOfType<TurnManager>();
            }
            if (unitBoardView == null)
            {
                unitBoardView = FindObjectOfType<UnitBoardView>();
            }
            ApplyJapaneseFontToTextComponents();
        }

        private void Start()
        {
#if UNITY_EDITOR
            EnsureJapaneseFont();
#endif
            if (turnManager == null)
            {
                turnManager = FindObjectOfType<TurnManager>();
            }

            BindTurnManager();
            EnsureEventSystem();
            CreateBattleUi();
            ApplyJapaneseFontToTextComponents();
            RefreshUi();
        }

        private void Update()
        {
            if (turnManager == null)
            {
                turnManager = FindObjectOfType<TurnManager>();
            }
            if (Input.GetKeyDown(KeyCode.Q) &&
                !IsInputFieldFocused() &&
                statusPanel != null)
            {
                statusPanel.SetActive(!statusPanel.activeSelf);
            }
            if (turnManager == null)
            {
                return;
            }
            if (unitBoardView == null)
            {
                unitBoardView = FindObjectOfType<UnitBoardView>();
            }

            BindTurnManager();
            ApplyJapaneseFontToTextComponents();
            RefreshUi();
        }

        private void OnDestroy()
        {
            if (subscribedTurnManager != null)
            {
                subscribedTurnManager.OnCellUnitsSelected -=
                    ShowUnitsAtCell;
            }
        }

        private void RefreshUi()
        {
            if (statusLabel == null || turnManager == null)
            {
                return;
            }

            UnitData selectedUnit = turnManager.SelectedUnit;
            string selectedName = selectedUnit == null
                ? "None"
                : (string.IsNullOrEmpty(selectedUnit.unitName)
                    ? selectedUnit.unitId
                    : selectedUnit.unitName);
            string unitTypeName = "None";
            int currentManpower = 0;
            int maxManpower = 0;
            int organization = 0;
            string terrainName = "None";
            int defenseMultiplier = 0;

            if (selectedUnit != null)
            {
                UnitDefinition unitDefinition =
                    FindObjectOfType<GameManager>() != null
                        ? FindObjectOfType<GameManager>()
                            .GetUnitDefinition(
                                GetSelectedUnitTypeId(selectedUnit))
                        : null;
                if (unitDefinition != null)
                {
                    unitTypeName =
                        string.IsNullOrEmpty(unitDefinition.displayName)
                            ? unitDefinition.unitTypeId
                            : unitDefinition.displayName;
                    maxManpower =
                        unitDefinition.maxManpower;
                }
                currentManpower = selectedUnit.CurrentTotalManpower;
                organization = selectedUnit.organization;

                HexTilemapView mapView =
                    FindObjectOfType<HexTilemapView>();
                if (mapView != null &&
                    mapView.Grid != null)
                {
                    HexCell cell =
                        mapView.Grid.GetCell(
                            selectedUnit.x,
                            selectedUnit.y);
                    if (cell != null)
                    {
                        TerrainDefinition terrain =
                            mapView.Grid.GetTerrainDefinition(
                                cell.terrainId);
                        if (terrain != null)
                        {
                            terrainName =
                                string.IsNullOrEmpty(terrain.displayName)
                                    ? terrain.terrainId
                                    : terrain.displayName;
                            defenseMultiplier =
                                terrain.defenseMultiplier;
                        }
                    }
                }
            }

            statusLabel.text = string.Format(
                "Phase: {0}\nTurn: {1}\nSelected: {2}\nUnit: {3}\nPosition: ({4}, {5})\nManpower: {6} / {7}\nOrganization: {8}\nRemaining Move: {9}\nTerrain: {10}\nDefense: {11}",
                turnManager.State,
                turnManager.TurnNumber,
                selectedName,
                unitTypeName,
                selectedUnit != null ? selectedUnit.x : 0,
                selectedUnit != null ? selectedUnit.y : 0,
                currentManpower,
                maxManpower,
                organization,
                selectedUnit != null ? selectedUnit.remainingMovePoints : 0,
                terrainName,
                defenseMultiplier);

            if (startBattleButton != null)
            {
                startBattleButton.gameObject.SetActive(
                    turnManager.State == BattleState.DeploymentPhase);
            }
            if (endTurnButton != null)
            {
                endTurnButton.gameObject.SetActive(
                    turnManager.State == BattleState.PlayerCommandPhase);
            }
            if (plannedArrowToggleLabel != null)
            {
                bool showAll =
                    unitBoardView != null &&
                    unitBoardView.ShowAllPlannedArrows;
                plannedArrowToggleLabel.text = showAll
                    ? "移動矢印: 全表示"
                    : "移動矢印: 個別";
            }

            if (unitListPanel != null && unitListPanel.activeSelf)
            {
                RefreshUnitListRows();
            }
        }

        private void CreateBattleUi()
        {
            Canvas canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObject = new GameObject(
                    "BattleUI Canvas",
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(GraphicRaycaster));
                canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                CanvasScaler scaler =
                    canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode =
                    CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
            }

            TMP_FontAsset font = LoadMeiryoFont();

            RectTransform statusPanelRect = CreateUiRect(
                "Battle Status Panel",
                canvas.transform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(16f, -16f),
                new Vector2(480f, 350f));
            statusPanel = statusPanelRect.gameObject;
            Image statusBackground =
                statusPanel.AddComponent<Image>();
            statusBackground.sprite = GetUiRectangleSprite();
            statusBackground.color = new Color(0f, 0f, 0f, 0.65f);

            RectTransform statusTextRect = CreateUiRect(
                "Battle Status Text",
                statusPanelRect,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
            statusTextRect.offsetMin = new Vector2(14f, 12f);
            statusTextRect.offsetMax = new Vector2(-14f, -12f);
            statusLabel =
                statusTextRect.gameObject.AddComponent<TextMeshProUGUI>();
            statusLabel.font = font;
            statusLabel.fontSize = 22f;
            statusLabel.color = Color.white;
            statusLabel.alignment = TextAlignmentOptions.TopLeft;
            statusLabel.enableWordWrapping = true;
            statusLabel.enableAutoSizing = true;
            statusLabel.fontSizeMin = 17f;
            statusLabel.fontSizeMax = 22f;
            statusLabel.text = "Phase";
            statusLabel.raycastTarget = false;

            RectTransform buttonRect = CreateUiRect(
                "Battle Buttons",
                canvas.transform,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-16f, 16f),
                new Vector2(220f, 110f));

            startBattleButton = CreateButton(
                "Start Battle",
                "Start Battle",
                buttonRect,
                font,
                0f,
                OnStartBattle);
            endTurnButton = CreateButton(
                "End Turn",
                "Turn End",
                buttonRect,
                font,
                -55f,
                OnEndTurn);
            plannedArrowToggleButton = CreateButton(
                "Planned Arrow Toggle",
                "移動矢印: 個別",
                buttonRect,
                font,
                -110f,
                OnTogglePlannedArrows);
            plannedArrowToggleLabel =
                plannedArrowToggleButton
                    .GetComponentInChildren<TextMeshProUGUI>();
            CreateUnitListPanel(canvas.transform, font);
            ApplyJapaneseFontToTextComponents();
        }

        private void CreateUnitListPanel(
            Transform parent,
            TMP_FontAsset font)
        {
            RectTransform panelRect = CreateUiRect(
                "Cell Unit List",
                parent,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(16f, -70f),
                new Vector2(430f, 500f));
            panelRect.pivot = new Vector2(0f, 0.5f);
            unitListPanel = panelRect.gameObject;
            Image panelImage = unitListPanel.AddComponent<Image>();
            panelImage.sprite = GetUiRectangleSprite();
            panelImage.color = new Color(0.06f, 0.07f, 0.09f, 0.94f);

            RectTransform titleRect = CreateUiRect(
                "Cell Unit List Title",
                panelRect,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(10f, -8f),
                new Vector2(-20f, 36f));
            titleRect.pivot = new Vector2(0f, 1f);
            TextMeshProUGUI title =
                titleRect.gameObject.AddComponent<TextMeshProUGUI>();
            title.font = font;
            title.fontSize = 23f;
            title.color = Color.white;
            title.alignment = TextAlignmentOptions.MidlineLeft;
            title.text = "選択マス部隊一覧";
            title.raycastTarget = false;

            RectTransform allButtonRect = CreateUiRect(
                "Select All Cell Units",
                panelRect,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(10f, -48f),
                new Vector2(190f, 40f));
            allButtonRect.pivot = new Vector2(0f, 1f);
            var allButton = allButtonRect.gameObject.AddComponent<Button>();
            Image allButtonImage =
                allButtonRect.gameObject.AddComponent<Image>();
            allButtonImage.sprite = GetUiRectangleSprite();
            allButtonImage.color = new Color(0.14f, 0.25f, 0.4f, 1f);
            allButton.targetGraphic = allButtonImage;
            allButton.onClick.AddListener(OnSelectAllCellUnits);

            RectTransform allButtonLabelRect = CreateUiRect(
                "Label",
                allButtonRect,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
            allButtonLabelRect.offsetMin = Vector2.zero;
            allButtonLabelRect.offsetMax = Vector2.zero;
            TextMeshProUGUI allButtonLabel =
                allButtonLabelRect.gameObject
                    .AddComponent<TextMeshProUGUI>();
            allButtonLabel.font = font;
            allButtonLabel.fontSize = 18f;
            allButtonLabel.color = Color.white;
            allButtonLabel.alignment = TextAlignmentOptions.Center;
            allButtonLabel.text = "同マス全選択";
            allButtonLabel.raycastTarget = false;

            RectTransform viewportRect = CreateUiRect(
                "Cell Unit List Viewport",
                panelRect,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(8f, -96f),
                new Vector2(-16f, 394f));
            viewportRect.pivot = new Vector2(0f, 1f);
            Image viewportImage =
                viewportRect.gameObject.AddComponent<Image>();
            viewportImage.sprite = GetUiRectangleSprite();
            viewportImage.color = new Color(0f, 0f, 0f, 0.12f);
            Mask viewportMask = viewportRect.gameObject.AddComponent<Mask>();
            viewportMask.showMaskGraphic = false;

            GameObject contentObject = new GameObject(
                "Cell Unit List Content",
                typeof(RectTransform),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            contentObject.transform.SetParent(viewportRect, false);
            unitListContent = contentObject.GetComponent<RectTransform>();
            unitListContent.anchorMin = new Vector2(0f, 1f);
            unitListContent.anchorMax = new Vector2(1f, 1f);
            unitListContent.pivot = new Vector2(0.5f, 1f);
            unitListContent.anchoredPosition = Vector2.zero;
            unitListContent.sizeDelta = new Vector2(0f, 0f);

            VerticalLayoutGroup layout =
                contentObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter =
                contentObject.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = viewportRect.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = unitListContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            unitListPanel.SetActive(false);
        }

        private void ShowUnitsAtCell(
            int x,
            int y,
            List<UnitData> units)
        {
            if (unitListPanel == null)
            {
                return;
            }

            displayedUnits.Clear();
            if (units != null)
            {
                displayedUnits.AddRange(units);
            }

            unitListPanel.SetActive(displayedUnits.Count > 0);
            if (displayedUnits.Count == 0)
            {
                ClearUnitListRows();
                return;
            }

            TextMeshProUGUI title =
                unitListPanel.GetComponentInChildren<TextMeshProUGUI>();
            if (title != null)
            {
                title.text = string.Format(
                    "選択マス部隊一覧 ({0}, {1})",
                    x,
                    y);
            }
            RebuildUnitListRows();
        }

        private void RebuildUnitListRows()
        {
            ClearUnitListRows();
            if (unitListContent == null)
            {
                return;
            }

            TMP_FontAsset font = LoadMeiryoFont();
            for (int i = 0; i < displayedUnits.Count; i++)
            {
                UnitData unit = displayedUnits[i];
                if (unit == null)
                {
                    continue;
                }

                RectTransform rowRect = CreateUiRect(
                    "Unit List Row",
                    unitListContent,
                    Vector2.zero,
                    Vector2.one,
                    Vector2.zero,
                    new Vector2(0f, 72f));
                LayoutElement layoutElement =
                    rowRect.gameObject.AddComponent<LayoutElement>();
                layoutElement.preferredHeight = 72f;
                layoutElement.minHeight = 72f;

                Image rowImage = rowRect.gameObject.AddComponent<Image>();
                rowImage.sprite = GetUiRectangleSprite();
                rowImage.color = new Color(0.16f, 0.17f, 0.2f, 1f);
                Button rowButton = rowRect.gameObject.AddComponent<Button>();
                rowButton.targetGraphic = rowImage;
                UnitData clickedUnit = unit;
                rowButton.onClick.AddListener(
                    () => OnSelectCellUnit(clickedUnit));

                CreateUiGauge(
                    rowRect,
                    "Organization Track",
                    10f,
                    new Color(0.04f, 0.04f, 0.04f, 1f));
                Image organizationFill = CreateUiGauge(
                    rowRect,
                    "Organization Fill",
                    10f,
                    new Color(0.18f, 0.86f, 0.28f, 1f));
                organizationFill.type = Image.Type.Filled;
                organizationFill.fillMethod = Image.FillMethod.Vertical;
                organizationFill.fillOrigin = (int)Image.OriginVertical.Bottom;
                organizationFill.fillAmount = 1f;
                organizationFill.transform.SetAsLastSibling();

                CreateUiGauge(
                    rowRect,
                    "Manpower Track",
                    26f,
                    new Color(0.04f, 0.04f, 0.04f, 1f));
                Image manpowerFill = CreateUiGauge(
                    rowRect,
                    "Manpower Fill",
                    26f,
                    new Color(0.96f, 0.48f, 0.12f, 1f));
                manpowerFill.type = Image.Type.Filled;
                manpowerFill.fillMethod = Image.FillMethod.Vertical;
                manpowerFill.fillOrigin = (int)Image.OriginVertical.Bottom;
                manpowerFill.fillAmount = 1f;
                manpowerFill.transform.SetAsLastSibling();

                RectTransform labelRect = CreateUiRect(
                    "Unit Details",
                    rowRect,
                    new Vector2(0f, 0f),
                    new Vector2(1f, 1f),
                    new Vector2(48f, 0f),
                    new Vector2(-54f, -6f));
                TextMeshProUGUI details =
                    labelRect.gameObject.AddComponent<TextMeshProUGUI>();
                details.font = font;
                details.fontSize = 15f;
                details.color = Color.white;
                details.alignment = TextAlignmentOptions.MidlineLeft;
                details.enableWordWrapping = false;
                details.raycastTarget = false;

                unitListRows.Add(new UnitListRow(
                    clickedUnit,
                    rowImage,
                    details,
                    organizationFill,
                    manpowerFill));
            }
            RefreshUnitListRows();
        }

        private Image CreateUiGauge(
            RectTransform parent,
            string name,
            float x,
            Color color)
        {
            RectTransform rect = CreateUiRect(
                name,
                parent,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(x, 0f),
                new Vector2(10f, 1f));
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(10f, -20f);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = GetUiRectangleSprite();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private void RefreshUnitListRows()
        {
            GameManager gameManager = FindObjectOfType<GameManager>();
            for (int i = 0; i < unitListRows.Count; i++)
            {
                UnitListRow row = unitListRows[i];
                UnitData unit = row.Unit;
                if (unit == null)
                {
                    continue;
                }

                UnitDefinition definition = gameManager != null
                    ? gameManager.GetUnitDefinition(
                        GetSelectedUnitTypeId(unit))
                    : null;
                string typeName = definition != null &&
                                  !string.IsNullOrEmpty(definition.displayName)
                    ? definition.displayName
                    : GetSelectedUnitTypeId(unit) ?? "不明";
                string unitName = string.IsNullOrEmpty(unit.unitName)
                    ? unit.unitId
                    : unit.unitName;
                int currentManpower = unit.CurrentTotalManpower;
                int maxManpower = GetUnitMaxManpower(unit);
                int organizationPercent = unit.maxOrganization > 0
                    ? Mathf.RoundToInt(
                        Mathf.Clamp01(
                            unit.organization / (float)unit.maxOrganization) *
                        100f)
                    : 0;
                string orderStatus = GetOrderStatus(unit);

                row.Details.text = string.Format(
                    "{0} / {1}\n兵員 {2}/{3}  組織 {4}%  {5}",
                    typeName,
                    unitName,
                    currentManpower,
                    maxManpower,
                    organizationPercent,
                    orderStatus);
                row.OrganizationFill.fillAmount =
                    unit.maxOrganization > 0
                        ? Mathf.Clamp01(
                            unit.organization / (float)unit.maxOrganization)
                        : 0f;
                row.ManpowerFill.fillAmount = maxManpower > 0
                    ? Mathf.Clamp01(currentManpower / (float)maxManpower)
                    : 0f;
                row.Background.color =
                    turnManager != null &&
                    turnManager.IsUnitSelected(unit)
                        ? new Color(0.35f, 0.34f, 0.12f, 1f)
                        : new Color(0.16f, 0.17f, 0.2f, 1f);
            }
        }

        private void ClearUnitListRows()
        {
            unitListRows.Clear();
            if (unitListContent == null)
            {
                return;
            }

            for (int i = unitListContent.childCount - 1; i >= 0; i--)
            {
                Destroy(unitListContent.GetChild(i).gameObject);
            }
        }

        private void OnSelectCellUnit(UnitData unit)
        {
            if (turnManager == null)
            {
                return;
            }

            bool shiftHeld =
                Input.GetKey(KeyCode.LeftShift) ||
                Input.GetKey(KeyCode.RightShift);
            if (shiftHeld)
            {
                turnManager.ToggleUnitSelection(unit);
            }
            else
            {
                turnManager.SelectUnit(unit);
            }
        }

        private void OnSelectAllCellUnits()
        {
            if (turnManager == null)
            {
                return;
            }

            var playerUnits = new List<UnitData>();
            for (int i = 0; i < displayedUnits.Count; i++)
            {
                if (displayedUnits[i] != null &&
                    displayedUnits[i].faction == Faction.Player)
                {
                    playerUnits.Add(displayedUnits[i]);
                }
            }
            turnManager.SelectUnits(playerUnits);
        }

        private void BindTurnManager()
        {
            if (subscribedTurnManager == turnManager)
            {
                return;
            }
            if (subscribedTurnManager != null)
            {
                subscribedTurnManager.OnCellUnitsSelected -=
                    ShowUnitsAtCell;
            }
            subscribedTurnManager = turnManager;
            if (subscribedTurnManager != null)
            {
                subscribedTurnManager.OnCellUnitsSelected +=
                    ShowUnitsAtCell;
            }
        }

        private static string GetOrderStatus(UnitData unit)
        {
            if (unit.pendingOrders == null ||
                unit.pendingOrders.Count == 0 ||
                unit.pendingOrders[0] == null)
            {
                return "命令なし";
            }

            Order order = unit.pendingOrders[0];
            if (order.orderType == OrderType.Move)
            {
                return string.Format(
                    "移動待機 ({0},{1})",
                    order.targetX,
                    order.targetY);
            }
            return string.Format(
                "攻撃待機 ({0},{1})",
                order.targetX,
                order.targetY);
        }

        private static int GetUnitMaxManpower(UnitData unit)
        {
            int total = 0;
            if (unit.battalions != null)
            {
                for (int i = 0; i < unit.battalions.Count; i++)
                {
                    if (unit.battalions[i] != null)
                    {
                        total += Mathf.Max(
                            0,
                            unit.battalions[i].maxManpower);
                    }
                }
            }
            return total;
        }

        private sealed class UnitListRow
        {
            public readonly UnitData Unit;
            public readonly Image Background;
            public readonly TextMeshProUGUI Details;
            public readonly Image OrganizationFill;
            public readonly Image ManpowerFill;

            public UnitListRow(
                UnitData unit,
                Image background,
                TextMeshProUGUI details,
                Image organizationFill,
                Image manpowerFill)
            {
                Unit = unit;
                Background = background;
                Details = details;
                OrganizationFill = organizationFill;
                ManpowerFill = manpowerFill;
            }
        }

        private TMP_FontAsset LoadMeiryoFont()
        {
            TMP_FontAsset font =
                Resources.Load<TMP_FontAsset>("Fonts/MEIRYO SDF");
            if (font == null)
            {
                font = Resources.Load<TMP_FontAsset>("MEIRYO SDF");
            }
            if (font == null)
            {
                TMP_FontAsset[] loadedFonts =
                    Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                foreach (TMP_FontAsset loadedFont in loadedFonts)
                {
                    if (loadedFont != null &&
                        loadedFont.name.IndexOf(
                            "MEIRYO",
                            System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        font = loadedFont;
                        break;
                    }
                }
            }
            if (font == null)
            {
                font = TMP_Settings.defaultFontAsset;
            }
            return font;
        }

        private static RectTransform CreateUiRect(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            GameObject objectInstance = new GameObject(
                name,
                typeof(RectTransform));
            objectInstance.transform.SetParent(parent, false);

            RectTransform rect =
                objectInstance.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = anchorMin;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return rect;
        }

        private static Button CreateButton(
            string name,
            string label,
            RectTransform parent,
            TMP_FontAsset font,
            float yPosition,
            UnityEngine.Events.UnityAction onClick)
        {
            RectTransform rect = CreateUiRect(
                name,
                parent,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-110f, yPosition),
                new Vector2(220f, 50f));
            rect.pivot = new Vector2(0.5f, 0.5f);

            var button = rect.gameObject.AddComponent<Button>();
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = GetUiRectangleSprite();
            image.color = new Color(0.12f, 0.12f, 0.12f, 0.9f);
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            RectTransform labelRect = CreateUiRect(
                "Label",
                rect,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            TextMeshProUGUI text =
                labelRect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = label;
            text.fontSize = 24f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            return button;
        }

        private void OnStartBattle()
        {
            if (turnManager != null)
            {
                turnManager.StartBattle();
            }
        }

        private void OnEndTurn()
        {
            if (turnManager != null)
            {
                turnManager.EndPlayerCommandPhase();
            }
        }

        private void OnTogglePlannedArrows()
        {
            if (unitBoardView == null)
            {
                unitBoardView = FindObjectOfType<UnitBoardView>();
            }
            if (unitBoardView != null)
            {
                unitBoardView.ToggleAllPlannedArrows();
            }
            RefreshUi();
        }

        private static void EnsureEventSystem()
        {
            if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                new GameObject(
                    "EventSystem",
                    typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.EventSystems.StandaloneInputModule));
            }
        }

        private static bool IsInputFieldFocused()
        {
            if (EventSystem.current == null ||
                EventSystem.current.currentSelectedGameObject == null)
            {
                return false;
            }

            GameObject selected =
                EventSystem.current.currentSelectedGameObject;
            return selected.GetComponentInParent<TMP_InputField>() != null ||
                   selected.GetComponentInParent<InputField>() != null;
        }

        private static Sprite GetUiRectangleSprite()
        {
            if (uiRectangleSprite != null)
            {
                return uiRectangleSprite;
            }

            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            texture.hideFlags = HideFlags.DontSave;
            uiRectangleSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f);
            uiRectangleSprite.hideFlags = HideFlags.DontSave;
            return uiRectangleSprite;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            EnsureJapaneseFont();
        }

        private void EnsureJapaneseFont()
        {
            if (japaneseFont != null)
            {
                return;
            }

            string[] guids =
                UnityEditor.AssetDatabase.FindAssets(
                    "t:TMP_FontAsset",
                    new[] { "Assets/Fonts" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path =
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
                TMP_FontAsset font =
                    UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                        path);
                if (font == null)
                {
                    continue;
                }

                if (font.name.IndexOf(
                        "MEIRYO",
                        System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    guids.Length == 1)
                {
                    japaneseFont = font;
                    UnityEditor.EditorUtility.SetDirty(this);
                    return;
                }
            }
        }
#endif

        private void ApplyJapaneseFontToTextComponents()
        {
            if (japaneseFont == null)
            {
                japaneseFont = LoadMeiryoFont();
            }
            if (japaneseFont == null)
            {
                return;
            }

            TextMeshProUGUI[] textComponents =
                GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < textComponents.Length; i++)
            {
                if (textComponents[i] != null)
                {
                    textComponents[i].font = japaneseFont;
                }
            }
        }

        private static string GetSelectedUnitTypeId(UnitData unit)
        {
            if (unit == null ||
                unit.battalions == null ||
                unit.battalions.Count == 0 ||
                unit.battalions[0] == null)
            {
                return null;
            }
            return unit.battalions[0].unitTypeId;
        }
    }
}
