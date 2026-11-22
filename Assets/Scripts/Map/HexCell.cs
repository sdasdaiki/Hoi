using System;

namespace EdoStrategy.Map
{
    [Serializable]
    public class HexCell
    {
        public int x;
        public int y;
        public string terrainId = "plain";
        public int deployZoneFaction = 0;
        public int infrastructureLevel = 1;
        public int currentSupply = 1000;

        [NonSerialized] public bool isVisibleToPlayer;

        public HexCell()
        {
        }

        public HexCell(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }
}
