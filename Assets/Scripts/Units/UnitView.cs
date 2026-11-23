using System.Collections.Generic;
using EdoStrategy.Core;
using EdoStrategy.Map;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace EdoStrategy.Units
{
    public class UnitView : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 8f;

        private UnitData unitData;
        private UnitDefinition unitDefinition;
        private Tilemap tilemap;
        private SpriteRenderer spriteRenderer;
        private SpriteRenderer factionBadge;
        private TextMesh label;
        private SpriteRenderer plannedMoveMarker;
        private LineRenderer plannedArrowLine;
        private SpriteRenderer plannedArrowHead;
        private Vector3 targetWorldPosition;

        public UnitData UnitData
        {
            get { return unitData; }
        }

        public void Initialize(
            UnitData data,
            UnitDefinition definition,
            Tilemap mapTilemap)
        {
            unitData = data;
            unitDefinition = definition;
            tilemap = mapTilemap;

            spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer == null)
            {
                spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            }
            spriteRenderer.sortingOrder = 10;

            if (unitDefinition != null && unitDefinition.unitIcon != null)
            {
                spriteRenderer.sprite = unitDefinition.unitIcon;
                spriteRenderer.color = Color.white;
                FitSpriteToTile(spriteRenderer, 0.7f);
                EnsureFactionBadge();
                factionBadge.color = data.faction == Faction.Player
                    ? new Color(0.18f, 0.48f, 1f, 0.95f)
                    : data.faction == Faction.Enemy
                        ? new Color(1f, 0.22f, 0.18f, 0.95f)
                        : new Color(0.7f, 0.7f, 0.7f, 0.95f);
            }
            else
            {
                spriteRenderer.sprite = CreateCircleSprite();
                spriteRenderer.color = data.faction == Faction.Player
                    ? new Color(0.18f, 0.48f, 1f, 1f)
                    : data.faction == Faction.Enemy
                        ? new Color(1f, 0.22f, 0.18f, 1f)
                        : new Color(0.7f, 0.7f, 0.7f, 1f);
                transform.localScale = Vector3.one;
                RemoveFactionBadge();
            }

            Transform labelTransform = transform.Find("UnitLabel");
            if (labelTransform != null)
            {
                label = labelTransform.GetComponent<TextMesh>();
            }
            if (label == null)
            {
                GameObject labelObject = new GameObject("UnitLabel");
                labelObject.transform.SetParent(transform, false);
                labelObject.transform.localPosition = new Vector3(0f, -0.15f, -0.1f);
                label = labelObject.AddComponent<TextMesh>();
                label.anchor = TextAnchor.MiddleCenter;
                label.alignment = TextAlignment.Center;
                label.fontSize = 24;
                label.characterSize = 0.05f;
                label.color = Color.white;
            }

            RefreshLabel();
            MoveToTileImmediately(data.x, data.y);
        }

        private void EnsurePlannedMoveMarker()
        {
            if (plannedMoveMarker == null)
            {
                Transform markerTransform = transform.Find("PlannedMoveMarker");
                if (markerTransform != null)
                {
                    plannedMoveMarker =
                        markerTransform.GetComponent<SpriteRenderer>();
                }
            }

            if (plannedMoveMarker == null)
            {
                GameObject markerObject =
                    new GameObject("PlannedMoveMarker");
                markerObject.transform.SetParent(tilemap != null
                    ? tilemap.transform
                    : transform, false);
                plannedMoveMarker =
                    markerObject.AddComponent<SpriteRenderer>();
                plannedMoveMarker.sprite = CreateCircleSprite();
                plannedMoveMarker.sortingOrder = 8;
                markerObject.transform.localScale =
                    new Vector3(0.55f, 0.55f, 1f);
            }

            plannedMoveMarker.enabled = true;
            plannedMoveMarker.color =
                new Color(1f, 0.95f, 0.25f, 0.45f);
            Vector3 worldPosition = tilemap != null
                ? tilemap.GetCellCenterWorld(
                    new Vector3Int(
                        unitData.plannedMoveX,
                        unitData.plannedMoveY,
                        0))
                : new Vector3(
                    unitData.plannedMoveX,
                    unitData.plannedMoveY,
                    0f);
            plannedMoveMarker.transform.position = worldPosition;
        }

        private void ClearPlannedMoveMarker()
        {
            if (plannedMoveMarker != null)
            {
                plannedMoveMarker.enabled = false;
            }
            SetArrowVisible(false);
        }

        public void SetArrowVisible(bool visible)
        {
            if (visible && unitData != null && unitData.hasPlannedMove)
            {
                EnsurePlannedArrow();
                if (plannedArrowLine != null)
                {
                    plannedArrowLine.enabled = true;
                }
                if (plannedArrowHead != null)
                {
                    plannedArrowHead.enabled = true;
                }
                UpdatePlannedArrow();
            }
            else
            {
                if (plannedArrowLine != null)
                {
                    plannedArrowLine.enabled = false;
                }
                if (plannedArrowHead != null)
                {
                    plannedArrowHead.enabled = false;
                }
            }
        }

        private void EnsurePlannedArrow()
        {
            if (plannedArrowLine == null)
            {
                GameObject lineObject =
                    new GameObject("PlannedArrowLine");
                lineObject.transform.SetParent(
                    tilemap != null ? tilemap.transform : transform,
                    false);
                plannedArrowLine =
                    lineObject.AddComponent<LineRenderer>();
                plannedArrowLine.positionCount = 0;
                plannedArrowLine.startWidth = 0.08f;
                plannedArrowLine.endWidth = 0.08f;
                plannedArrowLine.material =
                    new Material(Shader.Find("Sprites/Default"));
                plannedArrowLine.startColor =
                    new Color(1f, 0.85f, 0.15f, 0.9f);
                plannedArrowLine.endColor =
                    new Color(1f, 0.85f, 0.15f, 0.9f);
                plannedArrowLine.sortingOrder = 12;
            }

            if (plannedArrowHead == null)
            {
                GameObject headObject =
                    new GameObject("PlannedArrowHead");
                headObject.transform.SetParent(
                    tilemap != null ? tilemap.transform : transform,
                    false);
                plannedArrowHead =
                    headObject.AddComponent<SpriteRenderer>();
                plannedArrowHead.sprite = CreateTriangleSprite();
                plannedArrowHead.color =
                    new Color(1f, 0.85f, 0.15f, 0.95f);
                plannedArrowHead.sortingOrder = 13;
                headObject.transform.localScale =
                    new Vector3(0.25f, 0.25f, 1f);
            }
        }

        private void UpdatePlannedArrow()
        {
            if (plannedArrowLine == null ||
                unitData == null ||
                unitData.plannedPath == null ||
                unitData.plannedPath.Count < 1)
            {
                return;
            }

            System.Collections.Generic.List<Vector2Int> path =
                unitData.plannedPath;
            Vector3[] points = new Vector3[path.Count];
            for (int i = 0; i < path.Count; i++)
            {
                points[i] = tilemap != null
                    ? tilemap.GetCellCenterWorld(
                        new Vector3Int(path[i].x, path[i].y, 0))
                    : new Vector3(path[i].x, path[i].y, 0f);
                points[i].z = -0.2f;
            }

            plannedArrowLine.positionCount = points.Length;
            plannedArrowLine.SetPositions(points);

            if (plannedArrowHead == null || points.Length < 2)
            {
                return;
            }

            Vector3 direction =
                points[points.Length - 1] - points[points.Length - 2];
            plannedArrowHead.transform.position =
                points[points.Length - 1];
            if (direction.sqrMagnitude > 0.0001f)
            {
                float angle = Mathf.Atan2(direction.y, direction.x) *
                              Mathf.Rad2Deg - 90f;
                plannedArrowHead.transform.rotation =
                    Quaternion.Euler(0f, 0f, angle);
            }
        }

        private static Sprite CreateTriangleSprite()
        {
            const int size = 32;
            var texture =
                new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float upper = size - y - 1;
                    float halfWidth =
                        Mathf.Clamp((upper / (size - 1f)) * size * 0.5f,
                            0f,
                            size * 0.5f);
                    float center = (size - 1) * 0.5f;
                    bool inside =
                        y < size - 2 &&
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

        private void EnsureFactionBadge()
        {
            if (factionBadge == null)
            {
                Transform badgeTransform = transform.Find("FactionBadge");
                if (badgeTransform != null)
                {
                    factionBadge = badgeTransform.GetComponent<SpriteRenderer>();
                }
            }

            if (factionBadge == null)
            {
                GameObject badgeObject = new GameObject("FactionBadge");
                badgeObject.transform.SetParent(transform, false);
                factionBadge = badgeObject.AddComponent<SpriteRenderer>();
                factionBadge.sprite = CreateCircleSprite();
                factionBadge.sortingOrder = 9;

                Vector3 parentScale = transform.localScale;
                float compensationX =
                    Mathf.Abs(parentScale.x) > 0.0001f
                        ? 0.85f / Mathf.Abs(parentScale.x)
                        : 0.85f;
                float compensationY =
                    Mathf.Abs(parentScale.y) > 0.0001f
                        ? 0.85f / Mathf.Abs(parentScale.y)
                        : 0.85f;
                badgeObject.transform.localScale =
                    new Vector3(compensationX, compensationY, 1f);
            }
            else
            {
                Vector3 parentScale = transform.localScale;
                float compensationX =
                    Mathf.Abs(parentScale.x) > 0.0001f
                        ? 0.85f / Mathf.Abs(parentScale.x)
                        : 0.85f;
                float compensationY =
                    Mathf.Abs(parentScale.y) > 0.0001f
                        ? 0.85f / Mathf.Abs(parentScale.y)
                        : 0.85f;
                factionBadge.transform.localScale =
                    new Vector3(compensationX, compensationY, 1f);
            }
        }

        private void RemoveFactionBadge()
        {
            if (factionBadge != null)
            {
                Destroy(factionBadge.gameObject);
                factionBadge = null;
            }
        }

        public void MoveToTile(int x, int y)
        {
            if (tilemap == null)
            {
                targetWorldPosition = new Vector3(x, y, 0f);
                return;
            }

            targetWorldPosition = tilemap.GetCellCenterWorld(new Vector3Int(x, y, 0));
        }

        public void MoveToTileImmediately(int x, int y)
        {
            MoveToTile(x, y);
            transform.position = targetWorldPosition;
        }

        public void Refresh()
        {
            RefreshLabel();
            MoveToTile(unitData.x, unitData.y);
        }

        private void RefreshLabel()
        {
            if (label == null || unitData == null)
            {
                return;
            }

            string name = unitDefinition != null && !string.IsNullOrEmpty(unitDefinition.displayName)
                ? unitDefinition.displayName
                : unitData.unitName;
            label.text = string.Format("{0}\n{1}", name, unitData.CurrentTotalManpower);
        }

        private void FitSpriteToTile(SpriteRenderer renderer, float targetSize)
        {
            if (renderer == null || renderer.sprite == null)
            {
                return;
            }

            Vector2 spriteSize = renderer.sprite.bounds.size;
            float longestSide = Mathf.Max(spriteSize.x, spriteSize.y);
            if (longestSide <= 0f)
            {
                return;
            }

            float scale = targetSize / longestSide;
            transform.localScale = new Vector3(scale, scale, 1f);
        }

        private void Update()
        {
            transform.position = Vector3.Lerp(
                transform.position,
                targetWorldPosition,
                1f - Mathf.Exp(-moveSpeed * Time.deltaTime));

            if (unitData != null && unitData.hasPlannedMove)
            {
                EnsurePlannedMoveMarker();
            }
            else
            {
                ClearPlannedMoveMarker();
            }
        }

        private static Sprite CreateCircleSprite()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float radius = (size - 2) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    pixels[y * size + x] = distance <= radius
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
    }
}
