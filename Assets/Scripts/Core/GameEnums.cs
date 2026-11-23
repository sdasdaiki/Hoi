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
        Attack = 1
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
