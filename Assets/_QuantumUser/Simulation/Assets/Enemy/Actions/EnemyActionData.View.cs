namespace Quantum
{
    // View-only half of EnemyActionData (see EnemyActionData.cs) - never read by simulation logic,
    // only by EnemyAttackVisualsView. UnityEngine types compile fine here since Quantum.Simulation
    // may reference core Unity types, just not the View project's own classes (that would be a
    // circular assembly reference).
    public partial class EnemyActionData
    {
        // Windup telegraph, matching AnticipationTime's duration.
        public AttackVisualStep AnticipationStep;

        // Fires the instant Begin() is called (Preparation/Telegraph -> Recovery or -> Active alike).
        public AttackVisualStep BeginStep;

        // EnemyActionPhase.Active only; skipped entirely by instant actions (no Active phase).
        public AttackVisualStep OnGoingStep;

        // Active -> Recovery, or the same tick as BeginStep for instant actions (swing and hit are
        // simultaneous there, so both correctly fire together).
        public AttackVisualStep EndStep;

        // Optional ground indicator spanning two phase edges (see TelegraphData). Unset, or a
        // TelegraphData with no TelegraphPrefab, means no telegraph.
        [ExpandableAsset] public AssetRef<TelegraphData> Telegraph;
    }
}
