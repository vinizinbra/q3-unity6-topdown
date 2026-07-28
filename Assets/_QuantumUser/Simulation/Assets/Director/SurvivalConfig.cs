namespace Quantum
{
    using System;
    using System.Collections.Generic;
    using Photon.Deterministic;

    // One phase of the survival curve - capped at exactly the values SurvivalProgressionUtility/
    // CombatDirectorUtility need, so balancing this system stays a matter of tuning six named
    // numbers instead of hunting through a larger sheet. AllowedGroups doubles as this phase's own
    // "unlock list" - a group is unlocked exactly when it's in here, so there's no separate
    // unlock flag/system anywhere else.
    [Serializable]
    public struct SurvivalPhase
    {
        public FP Duration;
        public FP BudgetPerPulse;
        public FP PulseInterval;
        public FP TargetPressure;
        public Int32 MaxAliveEnemies;
        public List<AssetRef<EnemyGroupConfig>> AllowedGroups;
    }

    // Drives SurvivalProgressionUtility.Tick. The last entry never expires - once
    // CurrentPhaseIndex reaches Phases.Length - 1, its Duration is ignored and the match holds at
    // that phase's budget/pressure/cap/groups forever, so an escalating run doesn't need a
    // separate "loop the last phase" flag.
    public class SurvivalConfig : AssetObject
    {
        public SurvivalPhase[] Phases;
    }
}
