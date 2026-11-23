#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EdoStrategy.Map
{
    public static class BorderedHexSpriteGenerator
    {
        private const string TileDirectory = "Assets/Tiles";
        private const string SpritePath = TileDirectory + "/HexagonBordered.png";
        private const int Width = 256;
        private const int Height = 296;
        private const int BorderWidth = 5;

        [MenuItem("EdoStrategy/Generate Bordered Hex Sprite")]
        public static void Generate()
        {
            if (!AssetDatabase.IsValidFolder(TileDirectory))
            {
                CreateFolderIfNeeded(TileDirectory);
            }

            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            texture.name = "HexagonBordered";

            var pixels = new Color32[Width * Height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(0, 0, 0, 0);
            }

            Vector2[] outerVertices = GetOuterVertices();
            Vector2[] innerVertices = GetInnerVertices();

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
                    if (IsPointInPolygon(point, outerVertices))
                    {
                        bool isInner = IsPointInPolygon(point, innerVertices);
                        pixels[y * Width + x] = isInner
                            ? new Color32(255, 255, 255, 255)
                            : new Color32(20, 20, 20, 255);
                    }
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            byte[] pngData = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);

            string fullPath =
                Path.Combine(Directory.GetCurrentDirectory(), SpritePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllBytes(fullPath, pngData);

            AssetDatabase.ImportAsset(SpritePath, ImportAssetOptions.ForceUpdate);
            ConfigureTextureImporter();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Sprite sprite =
                AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            Selection.activeObject = sprite;
            EditorGUIUtility.PingObject(sprite);

            Debug.Log("Generated bordered pointy-top hex sprite: " + SpritePath);
        }

        private static Vector2[] GetOuterVertices()
        {
            float left = 3f;
            float right = Width - 4f;
            float top = 3f;
            float bottom = Height - 4f;
            float yUpper = 76f;
            float yLower = 219f;
            float centerX = Width * 0.5f;

            return new[]
            {
                new Vector2(centerX, top),
                new Vector2(right, yUpper),
                new Vector2(right, yLower),
                new Vector2(centerX, bottom),
                new Vector2(left, yLower),
                new Vector2(left, yUpper)
            };
        }

        private static Vector2[] GetInnerVertices()
        {
            float left = 3f + BorderWidth;
            float right = Width - 4f - BorderWidth;
            float top = 3f + BorderWidth;
            float bottom = Height - 4f - BorderWidth;
            float yUpper = 76f + BorderWidth;
            float yLower = 219f - BorderWidth;
            float centerX = Width * 0.5f;

            return new[]
            {
                new Vector2(centerX, top),
                new Vector2(right, yUpper),
                new Vector2(right, yLower),
                new Vector2(centerX, bottom),
                new Vector2(left, yLower),
                new Vector2(left, yUpper)
            };
        }

        private static bool IsPointInPolygon(Vector2 point, Vector2[] vertices)
        {
            bool inside = false;
            for (int i = 0, j = vertices.Length - 1;
                 i < vertices.Length;
                 j = i++)
            {
                Vector2 a = vertices[i];
                Vector2 b = vertices[j];

                bool crosses =
                    (a.y > point.y) != (b.y > point.y) &&
                    point.x <
                    (b.x - a.x) * (point.y - a.y) /
                    (b.y - a.y) + a.x;

                if (crosses)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static void ConfigureTextureImporter()
        {
            var importer =
                AssetImporter.GetAtPath(SpritePath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 256;
            importer.spritePivot = new Vector2(0.5f, 0.5f);
            importer.alphaIsTransparency = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression =
                TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static void CreateFolderIfNeeded(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string parent = "Assets";
            foreach (string segment in
                     folderPath.Substring("Assets/".Length).Split('/'))
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
