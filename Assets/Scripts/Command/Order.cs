using System;
using System.Collections.Generic;
using EdoStrategy.Core;
using UnityEngine;

namespace EdoStrategy.Command
{
    [Serializable]
    public class Order
    {
        public OrderType orderType;
        public int targetX;
        public int targetY;
        public int secondaryTargetX = -1;
        public int secondaryTargetY = -1;
        public string targetUnitId;
        public int remainingDelayTurns;
        public List<Vector2Int> path = new List<Vector2Int>();
    }
}
