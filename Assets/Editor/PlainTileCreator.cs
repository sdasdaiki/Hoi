#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace EdoStrategy.Map
{
    public static class PlainTileCreator
    {
        private const string TileDirectory = "Assets/Tiles";
        private const string TileAssetPath = TileDirectory + "/PlainTile.asset";
        private const string HexSpritePath = TileDirectory + "/HexagonPointedTop.png";

        [MenuItem("EdoStrategy/Create Plain Tile")]
        public static void CreatePlainTile()
        {
            if (!AssetDatabase.IsValidFolder(TileDirectory))
            {
                CreateFolderIfNeeded(TileDirectory);
            }

            Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(TileAssetPath);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                tile.name = "PlainTile";

                AssetDatabase.CreateAsset(tile, TileAssetPath);
            }

            Sprite hexSprite =
                AssetDatabase.LoadAssetAtPath<Sprite>(HexSpritePath);
            if (hexSprite != null)
            {
                tile.sprite = hexSprite;
            }
            tile.color = new Color(168f / 255f, 230f / 255f, 207f / 255f, 1f);

            EditorUtility.SetDirty(tile);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = tile;
            EditorGUIUtility.PingObject(tile);
        }

        private static void CreateFolderIfNeeded(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string parent = "Assets";
            foreach (string segment in folderPath.Substring("Assets/".Length).Split('/'))
            {
                string next = parent + "/" + segment;
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(parent, segment);
                }
                parent = next;
            }
        }
    }
}
#endif
