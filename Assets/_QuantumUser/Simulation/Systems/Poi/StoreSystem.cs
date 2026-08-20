namespace Quantum
{
    using UnityEngine.Scripting;

    // Command-processing driver for StoreInteraction - same manual f.Filter<PlayerLink>() +
    // f.GetPlayerCommand shape CursedRiftSystem already uses, deliberately NOT gated on any global
    // flag - Store must keep processing a player's Buy commands regardless of GameState, so a
    // Breathing Break ending mid-Browse doesn't strand a half-sent purchase (though in practice
    // every purchase here is a single atomic command, so there's no multi-tick "paid, pending"
    // window to strand - unlike Cursed Rift's own Situation B). Registered inside
    // GameplaySystemGroup (not outside it like LevelUpSystem) - nothing here ever disables that
    // group, so there's no re-entrancy hazard to guard against.
    [Preserve]
    public unsafe class StoreSystem : SystemMainThread
    {
        public override void Update(Frame f)
        {
            var filtered = f.Filter<PlayerLink>();

            while (filtered.Next(out EntityRef entity, out PlayerLink playerLink))
            {
                var command = f.GetPlayerCommand(playerLink.Player);

                if (command == null)
                    continue;

                if (f.Unsafe.TryGetPointer<StoreInteraction>(entity, out var interaction) == false)
                    continue;

                switch (command)
                {
                    case BuyStoreWeaponCommand buyWeapon:
                        StoreUtility.BuyWeapon(f, entity, interaction, buyWeapon.OfferIndex);
                        break;

                    case BuyStoreFoodCommand buyFood:
                        StoreUtility.BuyFood(f, entity, interaction, buyFood.OfferIndex);
                        break;

                    case BuyStoreWeaponLevelCommand:
                        StoreUtility.BuyWeaponLevelUp(f, entity);
                        break;

                    case CloseStoreCommand:
                        StoreUtility.Close(f, entity);
                        break;
                }
            }
        }
    }
}
