using EdoStrategy.Core;
using EdoStrategy.Units;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EdoStrategy.UI
{
    public class BattleUI : MonoBehaviour
    {
        [SerializeField] private TurnManager turnManager;
        [SerializeField] private UnitBoardView unitBoardView;

        private TextMeshProUGUI statusLabel;
        private Button startBattleButton;
        private Button endTurnButton;
        private Button plannedArrowToggleButton;
        private TextMeshProUGUI plannedArrowToggleLabel;

        private void Awake()
        {
            if (turnManager == null)
            {
                turnManager = FindObjectOfType<TurnManager>();
            }
            if (unitBoardView == null)
            {
                unitBoardView = FindObjectOfType<UnitBoardView>();
            }
        }

        private void Start()
        {
            if (turnManager == null)
            {
                turnManager = FindObjectOfType<TurnManager>();
            }

            EnsureEventSystem();
            CreateBattleUi();
            RefreshUi();
        }

        private void Update()
        {
            if (turnManager == null)
            {
                turnManager = FindObjectOfType<TurnManager>();
                if (turnManager == null)
                {
                    return;
                }
            }
            if (unitBoardView == null)
            {
                unitBoardView = FindObjectOfType<UnitBoardView>();
            }

            RefreshUi();
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

            statusLabel.text = string.Format(
                "Phase: {0}\nTurn: {1}\nSelected: {2}\nPosition: ({3}, {4})\nRemaining Move: {5}",
                turnManager.State,
                turnManager.TurnNumber,
                selectedName,
                selectedUnit != null ? selectedUnit.x : 0,
                selectedUnit != null ? selectedUnit.y : 0,
                selectedUnit != null ? selectedUnit.remainingMovePoints : 0);

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

            RectTransform statusRect = CreateUiRect(
                "Battle Status",
                canvas.transform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(16f, -16f),
                new Vector2(420f, 120f));
            statusLabel = statusRect.gameObject.AddComponent<TextMeshProUGUI>();
            statusLabel.font = font;
            statusLabel.fontSize = 28f;
            statusLabel.color = Color.white;
            statusLabel.alignment = TextAlignmentOptions.TopLeft;
            statusLabel.text = "Phase";

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
    }
}
