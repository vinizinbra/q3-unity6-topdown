namespace Quantum
{
    // The one entry point for giving an enemy a passive - EnemySystem.SeedPassives for authored
    // ones today, a random Elite pack roller later. Records it on EnemyPassives (see that
    // component) before applying, and refuses duplicates/overflow.
    public static unsafe class EnemyPassiveUtility
    {
        public static bool Apply(Frame f, EntityRef enemy, AssetRef<EnemyPassiveData> passiveRef)
        {
            if (passiveRef.IsValid == false || f.Exists(enemy) == false)
                return false;

            if (Has(f, enemy, passiveRef) == true)
                return false;

            EnemyPassiveData passive = f.FindAsset(passiveRef);

            if (passive == null)
                return false;

            f.AddOrGet<EnemyPassives>(enemy, out var passives);

            if (passives->Count >= passives->Applied.Length)
            {
                Log.Error($"[Enemy] {enemy} already has {passives->Count} passives - {passive.name} not applied");
                return false;
            }

            passives->Applied[passives->Count] = passiveRef;
            passives->Count++;

            passive.Apply(f, enemy);
            return true;
        }

        public static bool Has(Frame f, EntityRef enemy, AssetRef<EnemyPassiveData> passiveRef)
        {
            if (f.Unsafe.TryGetPointer<EnemyPassives>(enemy, out var passives) == false)
                return false;

            for (int i = 0; i < passives->Count; i++)
            {
                if (passives->Applied[i] == passiveRef)
                    return true;
            }

            return false;
        }
    }
}
