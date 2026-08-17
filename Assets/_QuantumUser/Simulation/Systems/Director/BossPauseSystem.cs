namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Counts down Global.BossPauseTimer (seeded by RunPhaseUtility.BeginBossEncounter right after
    // it disables GameplaySystemGroup) and re-enables that group once it reaches 0 - always-on,
    // can't live inside the group it's the one responsible for re-enabling (same reasoning
    // LevelUpSystem/ChestSystem/DebugCheatSystem's own header comments already document for why
    // each of them sits outside it too). A no-op every tick outside an active Boss pause.
    [Preserve]
    public unsafe class BossPauseSystem : SystemMainThread
    {
        public override void Update(Frame f)
        {
            if (f.Global->BossPauseTimer <= FP._0)
                return;

            f.Global->BossPauseTimer -= f.DeltaTime;

            if (f.Global->BossPauseTimer <= FP._0)
            {
                f.Global->BossPauseTimer = FP._0;
                f.SystemEnable<GameplaySystemGroup>();
                Log.Debug("[RunPhase] Boss encounter pause ended - GameplaySystemGroup re-enabled");
            }
        }
    }
}
