namespace EdoStrategy.Combat
{
    [System.Serializable]
    public class UnitModifier
    {
        public string sourceId;
        public string displayName;
        public int attackMultiplier = 1000;
        public int antiStructureMultiplier = 1000;
        public int defenseMultiplier = 1000;
        public int breakthroughMultiplier = 1000;
        public int orgDamageMultiplier = 1000;
        public int movePointBonus = 0;
        public int visionRangeBonus = 0;
        public int commandDelayBonus = 0;
    }
}
