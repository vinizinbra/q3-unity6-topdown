namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Always-on driver for the level-up upgrade-choice screen - deliberately outside
    // GameplaySystemGroup (see SystemSetup.User.cs) since it's the thing that disables/enables that
    // group and must keep ticking the countdown while everything else is paused. Unfiltered, manual
    // loop over f.Filter<PlayerLink>() rather than a SystemMainThreadFilter, matching
    // LevelGenerationSystem's own style - see LevelUpUtility for the actual roll/grant/resolve logic
    // this only sequences. See docs/level-up-upgrades.md.
    [Preserve]
    public unsafe class LevelUpSystem : SystemMainThread, ISignalOnPlayerDisconnected
    {
        public override void Update(Frame f)
        {
            // Captured before anything below (or ChestSystem, which runs right after this system
            // every tick) can change LevelUpScreenOpen this same tick - see
            // Global.LevelUpScreenOpenLastTick's own comment.
            f.Global->LevelUpScreenOpenLastTick = f.Global->LevelUpScreenOpen;

            // A level-up screen got blocked (another screen already had LevelUpScreenOpen claimed
            // the instant Grant raised Level) and is still owed - retry every tick until it actually
            // gets through, same "keep retrying while blocked" idiom ChestSystem's own top-of-Update
            // guard already uses. See Global.PendingLevelUpScreen's own comment.
            if (f.Global->LevelUpScreenOpen == false && f.Global->PendingLevelUpScreen == true)
                LevelUpUtility.BeginLevelUpScreen(f);

            if (f.Global->LevelUpScreenOpen == false)
                return;

            AutoPickForBots(f);
            ProcessSelectCommands(f);
            ProcessRerollCommands(f);
            ProcessKeepCurrentCommands(f);

            f.Global->LevelUpTimeRemaining -= f.DeltaTime;

            if (f.Global->LevelUpTimeRemaining <= FP._0 || AllConfirmed(f) == true)
            {
                LevelUpUtility.Resolve(f);
            }
        }

        private static void ProcessSelectCommands(Frame f)
        {
            var filtered = f.Filter<PlayerLink>();

            while (filtered.Next(out EntityRef entity, out PlayerLink playerLink))
            {
                if (f.GetPlayerCommand(playerLink.Player) is not SelectLevelUpUpgradeCommand command)
                    continue;

                if (f.Unsafe.TryGetPointer<LevelUpChoice>(entity, out var choice) == false)
                    continue;

                LevelUpUtility.ConfirmSelection(f, entity, choice, command.OptionIndex);
            }
        }

        // Same lookup/gating shape as ProcessSelectCommands above - only ever acts on the sender's
        // own already-rolled LevelUpChoice, found via PlayerLink, never another player's.
        private static void ProcessRerollCommands(Frame f)
        {
            if (f.RuntimeConfig.LevelUpConfig.IsValid == false)
                return;

            LevelUpConfig config = f.FindAsset(f.RuntimeConfig.LevelUpConfig);
            var filtered = f.Filter<PlayerLink>();

            while (filtered.Next(out EntityRef entity, out PlayerLink playerLink))
            {
                if (f.GetPlayerCommand(playerLink.Player) is not RerollLevelUpOptionsCommand)
                    continue;

                if (f.Unsafe.TryGetPointer<LevelUpChoice>(entity, out var choice) == false)
                    continue;

                LevelUpUtility.RerollOptionsFor(f, entity, choice, config);
            }
        }

        // Same lookup/gating shape as ProcessSelectCommands above - only ever acts on the sender's
        // own already-rolled LevelUpChoice, never another player's.
        private static void ProcessKeepCurrentCommands(Frame f)
        {
            var filtered = f.Filter<PlayerLink>();

            while (filtered.Next(out EntityRef entity, out PlayerLink playerLink))
            {
                if (f.GetPlayerCommand(playerLink.Player) is not KeepCurrentWeaponCommand)
                    continue;

                if (f.Unsafe.TryGetPointer<LevelUpChoice>(entity, out var choice) == false)
                    continue;

                LevelUpUtility.ConfirmKeepCurrent(f, entity, choice);
            }
        }

        // A bot has nobody at the keyboard, so it takes itself out of the "waiting for all players"
        // gate the tick the screen opens instead of making the human sit through the full
        // countdown for a pick that would have been random anyway. AutoConfirm is the exact same
        // random draw Resolve's own timeout fallback makes - see LevelUpUtility.AutoConfirm - so
        // this changes WHEN a bot picks, never HOW. Opt-out via RuntimeConfig.Bots.
        private static void AutoPickForBots(Frame f)
        {
            if (f.RuntimeConfig.Bots.DisableAutoLevelUpPick == true)
                return;

            var filtered = f.Filter<LevelUpChoice, BotBrain>();

            while (filtered.Next(out EntityRef entity, out LevelUpChoice _, out BotBrain _))
            {
                LevelUpUtility.AutoConfirm(f, entity);
            }
        }

        // True once every entity that still has a LevelUpChoice (i.e. was actually offered
        // something this screen) has confirmed - an entity nobody rolled anything for never gets
        // the component at all (see LevelUpUtility.RollOptionsFor), so it can't hold the screen open.
        private static bool AllConfirmed(Frame f)
        {
            var filtered = f.Filter<LevelUpChoice>();

            while (filtered.Next(out EntityRef entity, out LevelUpChoice choice))
            {
                if (choice.Confirmed == false)
                    return false;
            }

            return true;
        }

        // A player leaving mid-screen shouldn't hold the rest of the group waiting out the full
        // countdown for a pick that will never come - auto-confirm immediately instead. Harmless if
        // the screen isn't open or this player never had a LevelUpChoice to begin with (both are
        // no-ops inside AutoConfirm/TryGetPointer).
        public void OnPlayerDisconnected(Frame f, PlayerRef player)
        {
            if (f.Global->LevelUpScreenOpen == false)
                return;

            var filtered = f.Filter<PlayerLink>();

            while (filtered.Next(out EntityRef entity, out PlayerLink playerLink))
            {
                if (playerLink.Player != player)
                    continue;

                LevelUpUtility.AutoConfirm(f, entity);
                break;
            }
        }
    }
}
