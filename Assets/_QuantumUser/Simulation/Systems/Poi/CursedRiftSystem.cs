namespace Quantum
{
    using UnityEngine.Scripting;

    // Command-processing driver for CursedRiftInteraction - same manual f.Filter<PlayerLink>()
    // + f.GetPlayerCommand shape LevelUpSystem already uses for its own commands, deliberately
    // NOT gated on any global flag (unlike LevelUpSystem's own Global.LevelUpScreenOpen gate) -
    // Cursed Rift must keep processing a player's commands regardless of GameState, so a
    // Breathing Break ending mid-mutation-selection (State == SelectingMutation, cost already
    // applied) doesn't strand them (see CombatDirectorSystem/docs/breathing-poi.md, "Situation B").
    // Registered inside
    // GameplaySystemGroup (not outside it like LevelUpSystem) since, unlike a whole-party pause,
    // nothing here ever disables that group - there's no re-entrancy hazard to guard against by
    // living outside it.
    [Preserve]
    public unsafe class CursedRiftSystem : SystemMainThread
    {
        public override void Update(Frame f)
        {
            var filtered = f.Filter<PlayerLink>();

            while (filtered.Next(out EntityRef entity, out PlayerLink playerLink))
            {
                var command = f.GetPlayerCommand(playerLink.Player);

                if (command == null)
                    continue;

                if (f.Unsafe.TryGetPointer<CursedRiftInteraction>(entity, out var interaction) == false)
                    continue;

                switch (command)
                {
                    case SelectSacrificeCommand select:
                        CursedRiftUtility.SelectSacrifice(f, entity, interaction, select.OptionIndex);
                        break;

                    case CancelCursedRiftCommand:
                        CursedRiftUtility.Cancel(f, entity, interaction);
                        break;

                    case SelectMutationCommand mutation:
                        CursedRiftUtility.SelectMutation(f, entity, interaction, mutation.OptionIndex);
                        break;
                }
            }
        }
    }
}
