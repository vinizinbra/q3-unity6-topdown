namespace Quantum
{
    using Photon.Deterministic;

    // Schedule-side half of the generic Damage Echo behavior - see DamageEcho.qtn's own comment for
    // the full shape. Called once per REAL pellet/projectile WeaponSystem's own fire pipeline actually
    // launches on the first shot of a fresh magazine (FireProjectile/FireHitscan's own pellet loops,
    // plus FireDoubleTapShot for Double Tap's free extra shot) - NOT off a landed hit, so this counts
    // every shot fired, miss included. DamageEchoSystem owns the tick/execute half.
    public static unsafe class DamageEchoUtility
    {
        // damage is the shot's own pre-hit resolved damage (WeaponSystem.ResolveLiveDamage's return
        // value - post Weapon/Element Mastery, Phantom Strike, magazine-position bonuses; NOT
        // post-crit, since crit is only rolled once a shot actually connects, which this no longer
        // waits for). direction is that specific pellet's own resolved fired heading (spread rotation
        // already applied) - see PendingDamageEcho.Direction's own comment. No-op for any owner
        // without the upgrade, or whose currently-equipped weapon's Element doesn't match - reads the
        // SAME Weapon/WeaponDataAsset HeroMastery already resolves elsewhere, no new weapon-lookup
        // plumbing.
        public static void TryScheduleEcho(Frame f, EntityRef owner, FP damage, FPVector3 direction)
        {
            if (f.Unsafe.TryGetPointer<DamageEchoUpgrade>(owner, out var echo) == false)
                return;

            if (f.Unsafe.TryGetPointer<Weapon>(owner, out var weapon) == false || weapon->WeaponData.IsValid == false)
                return;

            if (f.FindAsset(weapon->WeaponData).Element != echo->RequiredElement)
                return;

            ScheduleEcho(f, owner, damage * echo->DamageMultiplier, echo->Delay, echo->Visual, echo->EchoHit, direction);
        }

        private static void ScheduleEcho(Frame f, EntityRef owner, FP damage, FP delay,
            AssetRef<DamageEchoVisualData> visual, AssetRef<ProjectileHitData> echoHit, FPVector3 direction)
        {
            if (delay <= FP._0 || damage <= FP._0)
                return;

            f.AddOrGet<PendingDamageEcho>(owner, out var pending);

            for (int i = 0; i < 16; i++)
            {
                if (pending->DelayRemaining[i] > FP._0)
                    continue;

                pending->DelayRemaining[i] = delay;
                pending->Damage[i] = damage;
                pending->Visual[i] = visual;
                pending->EchoHit[i] = echoHit;
                pending->Direction[i] = direction;
                return;
            }

            Log.Debug($"[DamageEcho] {owner} had no free echo slot (16 already pending) - dropped an echo");
        }
    }
}
