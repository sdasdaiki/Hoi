using UnityEngine;
using UnityEngine.UI;
using EdoStrategy.Map;
using EdoStrategy.Editor;

namespace EdoStrategy.UI
{
    public class MapEditorUI : MonoBehaviour
    {
        [SerializeField] private MapEditorController controller;
        [SerializeField] private HexTilemapView mapView;
        [SerializeField] private InputField mapNameInput;
        [SerializeField] private Text statusText;

        private void Awake()
        {
            if (controller == null)
            {
                controller = FindObjectOfType<MapEditorController>();
            }
            if (mapView == null)
            {
                mapView = FindObjectOfType<HexTilemapView>();
            }
            if (mapNameInput != null && string.IsNullOrEmpty(mapNameInput.text))
            {
                mapNameInput.text = "NewMap";
            }
        }

        public void SetPlainBrush() { SetTerrainBrush("plain"); }
        public void SetForestBrush() { SetTerrainBrush("forest"); }
        public void SetMountainBrush() { SetTerrainBrush("mountain"); }
        public void SetRiverBrush() { SetTerrainBrush("river"); }

        public void SetTerrainBrush(string terrainId)
        {
            if (controller != null)
            {
                controller.SetTerrainBrush(terrainId);
            }
        }

        public void SetFactionOneDeployZone()
        {
            if (controller != null)
            {
                controller.SetDeployZoneBrush(1);
            }
        }

        public void SetFactionTwoDeployZone()
        {
            if (controller != null)
            {
                controller.SetDeployZoneBrush(2);
            }
        }

        public void SaveMap()
        {
            if (mapView == null)
            {
                SetStatus("Map view is not assigned.");
                return;
            }

            string mapName = mapNameInput != null
                ? mapNameInput.text
                : "NewMap";
            MapSaveData saveData = mapView.CreateSaveData(mapName);
            try
            {
                string path = MapSerializer.Save(saveData);
                SetStatus("Saved: " + path);
            }
            catch (System.Exception exception)
            {
                SetStatus("Save failed: " + exception.Message);
            }
        }

        public void LoadMap()
        {
            if (mapView == null)
            {
                SetStatus("Map view is not assigned.");
                return;
            }

            string mapName = mapNameInput != null
                ? mapNameInput.text
                : "NewMap";
            try
            {
                MapSaveData saveData = MapSerializer.Load(mapName);
                mapView.LoadMap(saveData);
                SetStatus("Loaded: " + mapName);
            }
            catch (System.Exception exception)
            {
                SetStatus("Load failed: " + exception.Message);
            }
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
            Debug.Log(message);
        }
    }
}
