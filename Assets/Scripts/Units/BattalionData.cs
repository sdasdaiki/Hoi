using System;

namespace EdoStrategy.Units
{
    [Serializable]
    public class BattalionData
    {
        public string unitTypeId;
        public int maxManpower;
        public int currentManpower;
        public int equipmentRatio = 1000;
    }
}
