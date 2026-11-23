using System;
using System.Collections.Generic;
using EdoStrategy.Command;
using EdoStrategy.Core;

namespace EdoStrategy.Units
{
    [Serializable]
    public class UnitData
    {
        public string unitId;
        public string unitName;
        public Faction faction = Faction.Player;
        public int x;
        public int y;

        public List<BattalionData> battalions = new List<BattalionData>();

        public int organization = 1000;
        public int maxOrganization = 1000;
        public int supplyStatus = 1000;
        public int remainingMovePoints;
        public bool hasPlannedMove;
        public int plannedMoveX;
        public int plannedMoveY;

        public List<Order> pendingOrders = new List<Order>();

        public int CurrentTotalManpower
        {
            get
            {
                int total = 0;
                if (battalions != null)
                {
                    for (int i = 0; i < battalions.Count; i++)
                    {
                        BattalionData battalion = battalions[i];
                        if (battalion != null && battalion.currentManpower > 0)
                        {
                            total += battalion.currentManpower;
                        }
                    }
                }
                return total;
            }
        }

        public bool IsAlive
        {
            get { return CurrentTotalManpower > 0 && organization > 0; }
        }
    }
}
