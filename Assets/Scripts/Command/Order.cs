using System;
using EdoStrategy.Core;

namespace EdoStrategy.Command
{
    [Serializable]
    public class Order
    {
        public OrderType orderType;
        public int targetX;
        public int targetY;
        public string targetUnitId;
        public int remainingDelayTurns;
    }
}
