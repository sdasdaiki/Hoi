using System;
using System.IO;
using UnityEngine;

namespace EdoStrategy.Map
{
    [Serializable]
    public class MapSaveData
    {
        public string mapName;
        public int width;
        public int height;
        public HexCell[] cells;
    }

    public static class MapSerializer
    {
        public static string GetMapDirectory()
        {
#if UNITY_EDITOR
            return Path.Combine(Application.dataPath, "Maps");
#else
            return Path.Combine(Application.persistentDataPath, "Maps");
#endif
        }

        public static string GetMapPath(string mapName)
        {
            if (string.IsNullOrWhiteSpace(mapName))
            {
                throw new ArgumentException("Map name cannot be empty.");
            }

            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                mapName = mapName.Replace(invalidChar.ToString(), "_");
            }
            return Path.Combine(GetMapDirectory(), mapName + ".json");
        }

        public static string Save(MapSaveData saveData)
        {
            if (saveData == null)
            {
                throw new ArgumentNullException(nameof(saveData));
            }

            string directory = GetMapDirectory();
            Directory.CreateDirectory(directory);

            string path = GetMapPath(saveData.mapName);
            string json = JsonUtility.ToJson(saveData, true);
            File.WriteAllText(path, json);
            return path;
        }

        public static MapSaveData Load(string mapName)
        {
            string path = GetMapPath(mapName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Map JSON was not found.", path);
            }

            string json = File.ReadAllText(path);
            MapSaveData saveData = JsonUtility.FromJson<MapSaveData>(json);
            if (saveData == null)
            {
                throw new InvalidDataException("Map JSON could not be parsed.");
            }
            return saveData;
        }
    }
}
