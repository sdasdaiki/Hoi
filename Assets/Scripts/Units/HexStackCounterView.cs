using System.Collections.Generic;
using EdoStrategy.Core;
using TMPro;
using UnityEngine;

namespace EdoStrategy.Units
{
    public class HexStackCounterView : MonoBehaviour
    {
        private static Sprite rectangleSprite;

        private SpriteRenderer highlightRenderer;
        private SpriteRenderer backgroundRenderer;
        private SpriteRenderer organizationFill;
        private SpriteRenderer manpowerFill;
        private Transform organizationFillTransform;
        private Transform manpowerFillTransform;
        private SpriteRenderer iconRenderer;
        private TextMeshPro label;
        private TextMeshPro stackCount;

        public IReadOnlyList<UnitData> Units { get; private set; }
        public Faction Faction { get; private set; }
        public int X { get; private set; }
        public int Y { get; private set; }

        public void Initialize(
            IReadOnlyList<UnitData> units,
            UnitDefinition definition,
            string unitTypeId,
            Vector3 worldPosition,
            Vector2 size,
            bool selected)
        {
            if (units == null || units.Count == 0)
            {
                return;
            }

            UnitData firstUnit = units[0];
            Units = units;
            Faction = firstUnit.faction;
            X = firstUnit.x;
            Y = firstUnit.y;
            transform.position = worldPosition;
            Faction faction = firstUnit.faction;
            BoxCollider2D boxCollider = GetComponent<BoxCollider2D>();
            if (boxCollider == null)
            {
                boxCollider = gameObject.AddComponent<BoxCollider2D>();
            }
            boxCollider.size = size;
            boxCollider.offset = Vector2.zero;

            Color factionColor = faction == Faction.Player
                ? new Color(0.13f, 0.32f, 0.72f, 1f)
                : faction == Faction.Enemy
                    ? new Color(0.72f, 0.16f, 0.13f, 1f)
                    : new Color(0.35f, 0.35f, 0.35f, 1f);

            highlightRenderer = CreateSpriteRenderer(
                "Counter Border",
                new Vector3(0f, 0f, 0.02f),
                20);
            highlightRenderer.color = selected
                ? new Color(1f, 0.88f, 0.18f, 1f)
                : new Color(0.025f, 0.035f, 0.06f, 1f);
            SetSpriteSize(highlightRenderer.transform, size);

            backgroundRenderer = CreateSpriteRenderer(
                "Counter Background",
                new Vector3(0f, 0f, 0.01f),
                21);
            backgroundRenderer.color = factionColor;
            SetSpriteSize(
                backgroundRenderer.transform,
                new Vector2(size.x * 0.92f, size.y * 0.82f));

            float organizationRatio = 0f;
            float manpowerRatio = 0f;
            float totalOrganization = 0f;
            float totalManpower = 0f;
            float totalMaxManpower = 0f;
            for (int i = 0; i < units.Count; i++)
            {
                UnitData unit = units[i];
                if (unit == null)
                {
                    continue;
                }

                totalOrganization += unit.maxOrganization > 0
                    ? Mathf.Clamp01(
                        unit.organization / (float)unit.maxOrganization)
                    : 0f;
                totalManpower += GetCurrentManpower(unit);
                totalMaxManpower += GetMaxManpower(unit);
            }
            organizationRatio = totalOrganization / units.Count;
            manpowerRatio = totalMaxManpower > 0f
                ? Mathf.Clamp01(totalManpower / totalMaxManpower)
                : 0f;

            float gaugeHeight = size.y * 0.68f;
            float gaugeWidth = Mathf.Max(0.012f, size.x * 0.035f);
            float gaugeX = -size.x * 0.39f;
            CreateGauge(
                "Organization Gauge",
                gaugeX,
                gaugeWidth,
                gaugeHeight,
                GetOrganizationColor(organizationRatio),
                out organizationFill,
                out organizationFillTransform);
            CreateGauge(
                "Manpower Gauge",
                gaugeX + gaugeWidth * 1.8f,
                gaugeWidth,
                gaugeHeight,
                new Color(0.96f, 0.48f, 0.12f, 1f),
                out manpowerFill,
                out manpowerFillTransform);

            if (definition != null && definition.unitIcon != null)
            {
                iconRenderer = CreateSpriteRenderer(
                    "Unit Icon",
                    new Vector3(size.x * 0.04f, 0f, -0.01f),
                    22);
                iconRenderer.sprite = definition.unitIcon;
                iconRenderer.color = Color.white;
                FitIcon(iconRenderer, size);
            }
            else
            {
                label = CreateText(
                    "Unit Type",
                    new Vector3(size.x * 0.04f, 0f, -0.02f),
                    23);
                label.text = GetInitial(
                    definition != null
                        ? definition.displayName
                        : unitTypeId);
                label.fontSize = 3f;
                label.color = Color.white;
                label.alignment = TextAlignmentOptions.Center;
                label.transform.localScale = Vector3.one * 0.06f;
            }

            stackCount = CreateText(
                "Stack Count",
                new Vector3(size.x * 0.34f, 0f, -0.02f),
                23);
            stackCount.text = units.Count.ToString();
            stackCount.fontSize = 3.5f;
            stackCount.color = Color.white;
            stackCount.alignment = TextAlignmentOptions.Center;
            stackCount.transform.localScale = Vector3.one * 0.06f;

            SetFill(
                organizationFill,
                organizationFillTransform,
                gaugeWidth,
                gaugeHeight,
                organizationRatio);
            SetFill(
                manpowerFill,
                manpowerFillTransform,
                gaugeWidth,
                gaugeHeight,
                manpowerRatio);
        }

        private SpriteRenderer CreateSpriteRenderer(
            string objectName,
            Vector3 localPosition,
            int sortingOrder)
        {
            GameObject child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            child.transform.localPosition = localPosition;

            SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = GetRectangleSprite();
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private TextMeshPro CreateText(
            string objectName,
            Vector3 localPosition,
            int sortingOrder)
        {
            GameObject child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            child.transform.localPosition = localPosition;

            TextMeshPro text = child.AddComponent<TextMeshPro>();
            MeshRenderer meshRenderer = child.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.sortingOrder = sortingOrder;
            }
            return text;
        }

        private void CreateGauge(
            string name,
            float x,
            float width,
            float height,
            Color fillColor,
            out SpriteRenderer fillRenderer,
            out Transform fillTransform)
        {
            SpriteRenderer track = CreateSpriteRenderer(
                name + " Track",
                new Vector3(x, 0f, 0f),
                22);
            track.color = new Color(0.035f, 0.035f, 0.035f, 1f);
            SetSpriteSize(track.transform, new Vector2(width, height));

            fillRenderer = CreateSpriteRenderer(
                name + " Fill",
                new Vector3(x, 0f, -0.01f),
                23);
            fillRenderer.color = fillColor;
            fillTransform = fillRenderer.transform;
        }

        private static void SetFill(
            SpriteRenderer renderer,
            Transform fillTransform,
            float width,
            float height,
            float ratio)
        {
            float fillHeight = height * Mathf.Clamp01(ratio);
            fillTransform.localScale =
                new Vector3(width, fillHeight, 1f);
            fillTransform.localPosition = new Vector3(
                fillTransform.localPosition.x,
                -height * 0.5f + fillHeight * 0.5f,
                fillTransform.localPosition.z);
            renderer.enabled = fillHeight > 0f;
        }

        private static void SetSpriteSize(Transform target, Vector2 size)
        {
            target.localScale = new Vector3(size.x, size.y, 1f);
        }

        private static void FitIcon(SpriteRenderer renderer, Vector2 size)
        {
            Vector2 iconSize = renderer.sprite.bounds.size;
            float maxWidth = size.x * 0.27f;
            float maxHeight = size.y * 0.72f;
            float scale = Mathf.Min(
                maxWidth / Mathf.Max(iconSize.x, 0.001f),
                maxHeight / Mathf.Max(iconSize.y, 0.001f));
            renderer.transform.localScale = new Vector3(scale, scale, 1f);
        }

        private static int GetCurrentManpower(UnitData unit)
        {
            return unit != null ? unit.CurrentTotalManpower : 0;
        }

        private static int GetMaxManpower(UnitData unit)
        {
            int total = 0;
            if (unit != null && unit.battalions != null)
            {
                for (int i = 0; i < unit.battalions.Count; i++)
                {
                    if (unit.battalions[i] != null)
                    {
                        total += Mathf.Max(0, unit.battalions[i].maxManpower);
                    }
                }
            }
            return total;
        }

        private static string GetInitial(string value)
        {
            return string.IsNullOrEmpty(value) ? "?" : value.Substring(0, 1);
        }

        private static Color GetOrganizationColor(float ratio)
        {
            Color red = new Color(0.9f, 0.16f, 0.12f, 1f);
            Color yellow = new Color(1f, 0.78f, 0.12f, 1f);
            Color green = new Color(0.18f, 0.86f, 0.28f, 1f);
            return ratio < 0.5f
                ? Color.Lerp(red, yellow, ratio * 2f)
                : Color.Lerp(yellow, green, (ratio - 0.5f) * 2f);
        }

        private static Sprite GetRectangleSprite()
        {
            if (rectangleSprite != null)
            {
                return rectangleSprite;
            }

            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            texture.hideFlags = HideFlags.DontSave;
            rectangleSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f);
            rectangleSprite.hideFlags = HideFlags.DontSave;
            return rectangleSprite;
        }
    }
}
