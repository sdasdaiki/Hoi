using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Tilemaps;

namespace EdoStrategy.Editor
{
    [DefaultExecutionOrder(-50)]
    public class MapEditorController : MonoBehaviour
    {
        [SerializeField] private EdoStrategy.Map.HexTilemapView mapView;
        [SerializeField] private Camera editorCamera;
        [SerializeField] private string currentTerrainId = "plain";
        [SerializeField] private int currentDeployZone = 0;

        public string CurrentTerrainId => currentTerrainId;

        private void Awake()
        {
            if (editorCamera == null)
            {
                editorCamera = Camera.main;
            }
        }

        private void Update()
        {
            if (EventSystem.current != null &&
                EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            if (Input.GetMouseButton(0))
            {
                PaintAtScreenPosition(Input.mousePosition);
            }

            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Began ||
                    touch.phase == TouchPhase.Moved ||
                    touch.phase == TouchPhase.Stationary)
                {
                    if (EventSystem.current != null &&
                        EventSystem.current.IsPointerOverGameObject(touch.fingerId))
                    {
                        continue;
                    }
                    PaintAtScreenPosition(touch.position);
                }
            }
        }

        public void SetTerrainBrush(string terrainId)
        {
            currentTerrainId = string.IsNullOrEmpty(terrainId)
                ? "plain"
                : terrainId;
            currentDeployZone = 0;
        }

        public void SetDeployZoneBrush(int faction)
        {
            currentDeployZone = faction;
        }

        private void PaintAtScreenPosition(Vector2 screenPosition)
        {
            if (mapView == null || mapView.Grid == null || editorCamera == null)
            {
                return;
            }

            Tilemap tilemap = mapView.GetComponent<Tilemap>();
            if (tilemap == null)
            {
                return;
            }

            Vector3 worldPosition =
                editorCamera.ScreenToWorldPoint(new Vector3(
                    screenPosition.x,
                    screenPosition.y,
                    editorCamera.nearClipPlane));
            Vector3Int cellPosition = tilemap.WorldToCell(worldPosition);

            mapView.Grid.SetCellTerrain(
                cellPosition.x,
                cellPosition.y,
                currentTerrainId);

            if (currentDeployZone != 0)
            {
                mapView.Grid.SetCellDeployZone(
                    cellPosition.x,
                    cellPosition.y,
                    currentDeployZone);
            }
        }
    }
}
