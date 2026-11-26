using System;

namespace EdoStrategy.Core
{
    public enum Faction
    {
        None = 0,
        Player = 1,
        Enemy = 2
    }

    public enum OrderType
    {
        Move = 0,
        Attack = 1,
        Assault = 2,
        Defend = 3,
        Entrench = 4,
        Ambush = 5
    }

    public enum BattleState
    {
        DeploymentPhase,
        PlayerCommandPhase,
        ExecutionPhase,
        ReconAndReportPhase,
        CheckVictoryPhase,
        BattleEnded
    }
}
