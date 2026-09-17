namespace Quantum
{
    using Photon.Deterministic;

    // Shared resolution point for Hero Mastery (see docs/hero-mastery.md) - every hero's Weapon Weight
    // + Element Mastery damage bonuses, and every R3 Special that is itself a conditional damage
    // multiplier, read off the owner's currently-equipped Weapon in ONE place, so DamageUtility.
    // ResolveOutgoingDamage only ever needs a single call rather than one TryGetPointer block per
    // hero. No Hero == X branch anywhere here - every check is "does the owner hold this generic
    // component, and does the currently-equipped weapon's Weight/Element match".
    public static unsafe class HeroMasteryUtility
    {
        // Called once per weapon-sourced hit from DamageUtility.ResolveOutgoingDamage, AFTER the
        // CharacterStats gate (a Sentry barrel has none, and Mastery only ever lives on a real hero
        // entity) - see GetTargetingLinkMultiplier below for the one Mastery bonus that has to run
        // BEFORE that gate instead, since it pays out on Sentry-attributed damage.
        public static FP ResolveDamageMultiplier(Frame f, EntityRef owner, EntityRef target)
        {
            if (f.Unsafe.TryGetPointer<Weapon>(owner, out var weapon) == false || weapon->WeaponData.IsValid == false)
                return FP._1;

            WeaponDataAsset weaponData = f.FindAsset(weapon->WeaponData);
            FP multiplier = ResolveWeightElementMultiplier(f, owner, weaponData);

            multiplier *= ResolvePointBlank(f, owner, target, weaponData);
            multiplier *= ResolveVendettaBonus(f, owner, target, weaponData);
            multiplier *= ResolveInfernalRage(f, owner, weaponData);
            multiplier *= ResolveDeadeye(f, owner, target, weaponData);

            ApplyTargetingLinkMark(f, owner, target, weaponData);

            return multiplier;
        }

        // The flat Weapon Weight + Element Mastery term only - shared by ResolveDamageMultiplier
        // above and the no-target baseline preview below, so the two never drift apart.
        private static FP ResolveWeightElementMultiplier(Frame f, EntityRef owner, WeaponDataAsset weaponData)
        {
            FP multiplier = FP._1;

            if (f.Unsafe.TryGetPointer<WeaponWeightMastery>(owner, out var weightMastery) == true
                && weightMastery->Weight == weaponData.Weight)
            {
                multiplier *= FP._1 + weightMastery->DamageMultiplier;
            }

            if (f.Unsafe.TryGetPointer<ElementMastery>(owner, out var elementMastery) == true
                && (elementMastery->Element == weaponData.Element
                    || (elementMastery->Element == ElementType.Fire && HasGuaranteedBurn(f, owner) == true)))
            {
                multiplier *= FP._1 + elementMastery->DamageMultiplier;
            }

            return multiplier;
        }

        // No-target preview of ResolveDamageMultiplier's own flat Weapon Weight/Element Mastery term
        // - skips every target-conditional R3 special (Point Blank, Vendetta, Infernal Rage, Deadeye)
        // since there's no target to evaluate them against, and never mutates TargetingLinkMark.
        // Public so CurrentWeaponUiWidget (View) can preview a "Current Damage" number instead of
        // re-deriving this from WeaponWeightMastery/ElementMastery separately - see
        // DamageUtility.ResolveBaselineDamageMultiplier, which folds this in for the Weapon source.
        public static FP ResolveBaselineDamageMultiplier(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<Weapon>(owner, out var weapon) == false || weapon->WeaponData.IsValid == false)
                return FP._1;

            return ResolveWeightElementMultiplier(f, owner, f.FindAsset(weapon->WeaponData));
        }

        // Brute's Heavy Mastery R3 "Point Blank" - additional damage to a target within Range of
        // Brute himself. Pure attacker-target distance, no dependency on pellet count/fire rate/
        // magazine size/a specific weapon implementation - works for every weapon tagged Heavy.
        private static FP ResolvePointBlank(Frame f, EntityRef owner, EntityRef target, WeaponDataAsset weaponData)
        {
            if (weaponData.Weight != WeaponWeight.Heavy || f.Unsafe.TryGetPointer<PointBlankUpgrade>(owner, out var pointBlank) == false)
                return FP._1;

            if (f.Unsafe.TryGetPointer<Transform3D>(owner, out var ownerTransform) == false
                || f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                return FP._1;

            FP distance = FPVector3.Distance(ownerTransform->Position, targetTransform->Position);

            return distance <= pointBlank->Range ? FP._1 + pointBlank->DamageBonus : FP._1;
        }

        // Max's Light Mastery R3 "Vendetta" - bonus Light-weapon damage against whichever enemy
        // currently carries THIS owner's own RevengeMark (the existing Vendetta mark, Heroes/Max/
        // Vendetta.qtn) - no duplicate mark, no dependency on fire rate/magazine size/shot count/
        // semi-auto vs automatic. Works for Pistol, SMG, or any future Light weapon.
        private static FP ResolveVendettaBonus(Frame f, EntityRef owner, EntityRef target, WeaponDataAsset weaponData)
        {
            if (weaponData.Weight != WeaponWeight.Light || f.Unsafe.TryGetPointer<VendettaUpgrade>(owner, out var vendetta) == false)
                return FP._1;

            if (f.Unsafe.TryGetPointer<RevengeMark>(target, out var mark) == false || mark->MarkedBy != owner)
                return FP._1;

            return FP._1 + vendetta->DamageBonus;
        }

        // Max's Fire Mastery R3 "Infernal Rage" - bonus Fire-weapon damage while Max is in his existing
        // Overdrive activation. Component PRESENCE of RageOverdrive is what every other Overdrive
        // Ascension already reads as "an activation is running right now" - see RageOverdrive.qtn.
        // Deliberately does not touch Rage generation - Fire Mastery only reads the state, never grants it.
        // A Neutral weapon also qualifies while Ignition's guaranteed Burn is active (HasGuaranteedBurn) -
        // that hit lands a Burn same as a Fire weapon's would, so it is eligible for Fire Mastery too.
        private static FP ResolveInfernalRage(Frame f, EntityRef owner, WeaponDataAsset weaponData)
        {
            if (f.Unsafe.TryGetPointer<InfernalRageUpgrade>(owner, out var infernalRage) == false)
                return FP._1;

            if (weaponData.Element != ElementType.Fire && HasGuaranteedBurn(f, owner) == false)
                return FP._1;

            return f.Has<RageOverdrive>(owner) == true ? FP._1 + infernalRage->DamageBonus : FP._1;
        }

        // Ignition (Max's Overdrive Ascension) latches CharacterStats.BurnOnHitStacks for as long as
        // its guaranteed-Burn window is open (see StatusEffectUtility.TryApplyGuaranteedBurn's own
        // comment - "fires even on a Neutral weapon"). Read generically off CharacterStats rather than
        // Max/RageOverdrive directly, so any future always-Burn effect gets the same Fire Mastery
        // eligibility for free, with no Hero == X branch.
        private static bool HasGuaranteedBurn(Frame f, EntityRef owner)
        {
            return f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == true && stats->BurnOnHitStacks != 0;
        }

        // Kai's Heavy Mastery R3 "Deadeye" - bonus damage on the first Heavy-weapon hit THIS owner
        // lands against each enemy, via the generic WeaponWeightFirstHitTracker rather than a bespoke
        // mark - naturally coexists with Kai's own First Strike Ascension without merging their state.
        private static FP ResolveDeadeye(Frame f, EntityRef owner, EntityRef target, WeaponDataAsset weaponData)
        {
            if (weaponData.Weight != WeaponWeight.Heavy || f.Unsafe.TryGetPointer<DeadeyeUpgrade>(owner, out var deadeye) == false)
                return FP._1;

            return WeaponWeightFirstHitUtility.TryConsumeFirstHit(f, target, owner, WeaponWeight.Heavy)
                ? FP._1 + deadeye->DamageBonus
                : FP._1;
        }

        // Lux's Medium Mastery R3 "Targeting Link" - a Medium-weapon hit from a TargetingLinkUpgrade
        // holder refreshes (never stacks) a duration mark on the target; the actual bonus damage is
        // read separately by GetTargetingLinkMultiplier below, since it has to reach Sentry-attributed
        // damage (owner = a SentryBarrel with no CharacterStats), not just Lux's own hits.
        private static void ApplyTargetingLinkMark(Frame f, EntityRef owner, EntityRef target, WeaponDataAsset weaponData)
        {
            if (weaponData.Weight != WeaponWeight.Medium || f.Unsafe.TryGetPointer<TargetingLinkUpgrade>(owner, out var targetingLink) == false)
                return;

            f.AddOrGet<TargetingLinkMark>(target, out var mark);
            mark->MarkedBy = owner;
            mark->Remaining = targetingLink->MarkDuration;
        }

        // Targeting Link's own damage-bonus read - MUST run before DamageUtility.ResolveOutgoingDamage's
        // CharacterStats gate, since the damage this boosts is dealt by a SentryBarrel (owner here),
        // which never carries CharacterStats (see WeaponSystem.Equip's own comment on that). Resolves
        // the barrel back to its chassis, then to the Lux who deployed it, so only THAT Lux's own
        // Targeting Link (and only a mark SHE placed) ever pays out - never another player's Sentry,
        // never a global incoming-damage buff.
        public static FP GetTargetingLinkMultiplier(Frame f, EntityRef owner, EntityRef target)
        {
            if (f.Unsafe.TryGetPointer<SentryBarrel>(owner, out var barrel) == false)
                return FP._1;

            if (f.Unsafe.TryGetPointer<Sentry>(barrel->Sentry, out var sentry) == false)
                return FP._1;

            if (f.Unsafe.TryGetPointer<TargetingLinkUpgrade>(sentry->Owner, out var targetingLink) == false)
                return FP._1;

            if (f.Unsafe.TryGetPointer<TargetingLinkMark>(target, out var mark) == false || mark->MarkedBy != sentry->Owner)
                return FP._1;

            return FP._1 + targetingLink->SentryDamageBonus;
        }
    }

    // Generic reusable "has (owner, weight) already landed a hit on this target" ledger - Kai's
    // Deadeye is the first consumer, but any future Weapon Weight Mastery needing the same "first hit
    // of weight X from owner Y" shape reuses this instead of a bespoke mark.
    public static unsafe class WeaponWeightFirstHitUtility
    {
        // True only the FIRST time this exact (owner, weight) pair lands on target; false every time
        // after, until target itself is destroyed (and the tracker along with it). All 4 slots taken
        // (the co-op player cap) is treated as "already recorded" rather than overflowing.
        public static bool TryConsumeFirstHit(Frame f, EntityRef target, EntityRef owner, WeaponWeight weight)
        {
            f.AddOrGet<WeaponWeightFirstHitTracker>(target, out var tracker);

            for (int i = 0; i < 4; i++)
            {
                if (tracker->Owner[i] == owner && (WeaponWeight)tracker->Weight[i] == weight)
                    return false;
            }

            for (int i = 0; i < 4; i++)
            {
                if (tracker->Owner[i] == EntityRef.None)
                {
                    tracker->Owner[i] = owner;
                    tracker->Weight[i] = (byte)weight;
                    return true;
                }
            }

            return false;
        }
    }
}
