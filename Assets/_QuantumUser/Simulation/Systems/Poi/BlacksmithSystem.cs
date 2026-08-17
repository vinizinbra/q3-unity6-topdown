namespace Quantum
{
    using UnityEngine.Scripting;

    // Command-processing driver for BlacksmithInteraction - same shape as CursedRiftSystem/
    // StoreSystem. Registered inside GameplaySystemGroup - nothing here ever disables that group,
    // so there's no re-entrancy hazard to guard against.
    [Preserve]
    public unsafe class BlacksmithSystem : SystemMainThread
    {
        public override void Update(Frame f)
        {
            var filtered = f.Filter<PlayerLink>();

            while (filtered.Next(out EntityRef entity, out PlayerLink playerLink))
            {
                var command = f.GetPlayerCommand(playerLink.Player);

                if (command == null)
                    continue;

                if (f.Unsafe.TryGetPointer<BlacksmithInteraction>(entity, out var interaction) == false)
                    continue;

                switch (command)
                {
                    case SelectBlacksmithPerkCommand select:
                        BlacksmithUtility.SelectPerk(f, entity, interaction, select.OptionIndex);
                        break;

                    case CancelBlacksmithCommand:
                        BlacksmithUtility.Cancel(f, entity, interaction);
                        break;
                }
            }
        }
    }
}
