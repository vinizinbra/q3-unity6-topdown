namespace Quantum
{
    using UnityEngine.Scripting;

    // Always-on - detects the final Boss phase's boss actually dying and transitions CurrentState
    // to Victory (see GameState.qtn's own Victory comment). Nothing else advances state out of
    // Boss today - CombatDirectorSystem's own Update gate excludes GameState.Boss entirely once
    // entered (only Survival/Breathing tick SurvivalProgressionUtility), so this is the one place
    // that closes that loop. Boss death has no dedicated signal - the same "BossRuntimeState
    // filter went empty" edge VoiceDirector.PollBoss/BossWidget already poll for their own
    // purposes (voice line, HP bar) - mirrored here for the sim's own state machine.
    //
    // Registered AFTER GameplaySystemGroup in SystemSetup.User.cs (not before, unlike BossPause
    // System/CheatSystem/TutorialSystem) specifically so it observes the SAME tick's boss
    // destruction instead of lagging one tick behind - it has nothing to re-enable, so it doesn't
    // need the "can't live inside the group it's responsible for" placement those systems do.
    [Preserve]
    public unsafe class VictorySystem : SystemMainThread
    {
        public override void Update(Frame f)
        {
            if (f.Global->CurrentState != GameState.Boss)
                return;

            if (IsLastBossPhase(f) == false)
                return;

            if (f.Filter<BossRuntimeState>().Next(out EntityRef _, out BossRuntimeState _))
                return; // still alive

            GameStateUtility.SetState(f, GameState.Victory);
            Log.Debug("[RunPhase] GameState.Victory - final boss defeated");
        }

        private static bool IsLastBossPhase(Frame f)
        {
            SurvivalConfig config = f.FindAsset(f.RuntimeConfig.SurvivalConfig);
            if (config == null || config.Phases == null || config.Phases.Length == 0)
                return false;

            int lastIndex = config.Phases.Length - 1;
            return f.Global->CurrentPhaseIndex == lastIndex
                && config.Phases[lastIndex].Kind == SurvivalPhaseKind.Boss;
        }
    }
}
