namespace Quantum
{
    using UnityEngine.Scripting;

    // Handles SetTutorialPauseCommand for the solo-only tutorial popups (HowToPlayPopup/
    // FirstBreakPopup) - always-on and OUTSIDE GameplaySystemGroup, same "can't live inside the
    // group it's the one responsible for re-enabling" reasoning LevelUpSystem/ChestSystem/
    // BossPauseSystem/CheatSystem's own header comments already document for why each of them sits
    // outside it too. The sim has no notion of "tutorial" beyond this - deciding WHEN to pause is a
    // pure local/View concern (see InMatchTutorialManager, which reacts to the already-existing
    // EventGameStateChanged), this system just carries out whatever it's told.
    [Preserve]
    public unsafe class TutorialSystem : SystemMainThreadFilter<TutorialSystem.Filter>
    {
        public struct Filter
        {
            public EntityRef Entity;
            public PlayerLink* PlayerLink;
        }

        public override void Update(Frame f, ref Filter filter)
        {
            if (f.GetPlayerCommand(filter.PlayerLink->Player) is not SetTutorialPauseCommand cmd)
                return;

            if (cmd.Paused)
            {
                f.SystemDisable<GameplaySystemGroup>();
            }
            else if (f.Global->LevelUpScreenOpen == false)
            {
                // A level-up screen can legitimately still be open (or have reopened for the next
                // screen in a chained multi-level grant) by the time this popup closes - don't
                // clobber its own pause; LevelUpUtility.Resolve re-enables the group itself once
                // every chained screen is actually done.
                f.SystemEnable<GameplaySystemGroup>();
            }
        }
    }
}
