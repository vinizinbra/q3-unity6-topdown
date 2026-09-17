namespace Quantum
{
    using UnityEngine;

    // Magnitude bucket for the camera shake fired on PlayerFired - same "one shared enum, several
    // tiers of values" idea as EffectConfig.KnockbackTier. Lives here (not WeaponDataAsset.cs) since
    // shake is purely presentational - the actual amplitude/duration numbers live on the Unity-only
    // CameraShakeConfig, resolved by WeaponCameraShakeListener.
    public enum WeaponShakeTier
    {
        Small,
        Medium,
        Strong
    }

    // Generic per-weapon PROJECTILE look, read by ProjectileDataVisualsView off the live Projectile.
    // WeaponData asset ref (seeded at fire time - ProjectileSpawner.Spawn's weaponData param, same as
    // Element) rather than through ViewPrefab - a weapon's own ViewPrefab is what sits in the player's
    // hands, this is what the FIRED SHOT looks like, and the two are authored on completely different
    // prefabs (ViewPrefab vs. WeaponDataAsset.ProjectileData's own EntityPrototype/view). Lets one
    // generic sprite-based projectile prefab serve every weapon that wants a 2D bullet - only these
    // fields change per weapon, not the prefab. Grouped into its own class purely for Inspector
    // organization (one foldout instead of a long flat field list) - WeaponDataAsset.ProjectileVisuals.
    [System.Serializable]
    public class ProjectileVisualsConfig
    {
        [Tooltip("Sprite shown on the projectile's billboard SpriteRenderer (ProjectileDataVisualsView.sprite). Leave empty to keep whatever sprite is already authored on the prefab.")]
        public Sprite ProjectileSprite;

        [Tooltip("Degrees added on top of the computed billboard angle (BillboardVelocityAlignedSprite.AngleOffset) - 0 if the sprite art is drawn facing right, -90 if drawn facing up.")]
        public float ProjectileSpriteRotationOffset;

        [Tooltip("Tint applied to the projectile sprite's SpriteRenderer.color - HDR (a channel above 1) only actually glows/blooms if the sprite's shader reads it off vertex color at full precision, e.g. SpriteGrayscaleColorizeHDR - see that shader's own caveat comment on SpriteRenderer.color's usual Color32 vertex clamping. White leaves the sprite's own authored art untouched.")]
        [ColorUsage(true, true)]
        public Color ProjectileColor = Color.white;

        [Tooltip("Scale applied to the projectile sprite's transform.localScale. (1,1,1) leaves the sprite's own authored size untouched.")]
        public Vector3 ProjectileScale = Vector3.one;

        [Tooltip("Enables ProjectileDataVisualsView's spark trail particle slot and tints it. Off (no spark trail) by default.")]
        public bool EnableProjectileSparkTrail;
        [ColorUsage(true, true)]
        public Color ProjectileSparkTrailColor = Color.white;
        [Tooltip("Scale applied to the spark trail particle's transform.localScale. (1,1,1) leaves its own authored size untouched.")]
        public Vector3 ProjectileSparkTrailScale = Vector3.one;
        [Tooltip("Overrides the spark trail particle's Main.Start Lifetime (seconds a spawned particle lives before fading). 0 (the default) leaves its own authored lifetime untouched.")]
        public float ProjectileSparkTrailLifetimeOverride;

        [Tooltip("Enables ProjectileDataVisualsView's glow particle slot and tints it. Off (no glow) by default.")]
        public bool EnableProjectileGlow;
        [ColorUsage(true, true)]
        public Color ProjectileGlowColor = Color.white;
        [Tooltip("Scale applied to the glow particle's transform.localScale. (1,1,1) leaves its own authored size untouched.")]
        public Vector3 ProjectileGlowScale = Vector3.one;

        [Tooltip("Enables ProjectileDataVisualsView's HasLight ground blob and tints it. Off (no ground light) by default.")]
        public bool EnableProjectileLight;
        [ColorUsage(true, true)]
        public Color ProjectileLightColor = Color.white;

        // Fed (alpha forced to 1) into the shared destroyEffectPrefab's own ROOT/CHILD particle tint
        // by RegisterWithProjectileView (ProjectileDataVisualsView) - see that method's own comment.
        // Dedicated fields rather than reusing ProjectileGlowColor/ProjectileSparkTrailColor - the
        // impact burst is its own moment, independently tunable from how the live projectile looks in
        // flight.
        [Header("Destroy Effect (generic destroyEffectPrefab, e.g. GenericProjectileDestroy)")]
        [Tooltip("Tint applied to destroyEffectPrefab's own ROOT ParticleSystem. White leaves it exactly as authored on the prefab.")]
        [ColorUsage(true, true)]
        public Color ProjectileDestroyColor = Color.white;
        [Tooltip("Tint applied to every CHILD ParticleSystem under destroyEffectPrefab (e.g. GenericProjectileDestroy's Sparks/Glow). White leaves them exactly as authored on the prefab.")]
        [ColorUsage(true, true)]
        public Color ProjectileDestroyGlowColor = Color.white;
        [Tooltip("Multiplier on destroyEffectPrefab's own authored transform.localScale. (1,1,1) leaves it exactly as authored on the prefab.")]
        public Vector3 ProjectileDestroyScale = Vector3.one;

        // Optional, on top of the generic sparkTrail/glow slots ProjectileDataVisualsView already
        // drives - instantiated and parented under the visual root at spawn (ProjectileView.
        // AttachWeaponExtraParticle), for a one-off weapon-specific effect that doesn't belong baked
        // into every projectile prefab. Its own color/behavior is whatever the prefab itself is
        // authored with - unlike sparkTrail/glow this isn't a shared generic slot, so there's no
        // runtime tint override, only a scale.
        [Header("Extra Particle (optional, parented onto the visual root)")]
        [Tooltip("Instantiated under the projectile's visual root at spawn if assigned. Leave empty for no extra particle.")]
        public ParticleSystem ProjectileExtraParticle;
        [Tooltip("Scale applied to the instantiated extra particle's transform.localScale. (1,1,1) uses its own authored prefab size.")]
        public Vector3 ProjectileExtraParticleScale = Vector3.one;
    }

    // View-only half of WeaponDataAsset (see the partial declaration in WeaponDataAsset.cs).
    public partial class WeaponDataAsset
    {
        [Tooltip("Prefab instantiated under the player's weapon socket to represent this weapon - must have a WeaponView component. WeaponViewController resolves this directly, no separate catalog/lookup needed.")]
        public GameObject ViewPrefab;

        // A Choose-Weapon level-up/Chest card (WeaponCardWidget) needs an icon, but every weapon
        // already has a real world sprite authored on ViewPrefab's own root SpriteRenderer (see
        // e.g. BasicWeapon.prefab) - reusing that instead of a second hand-authored Icon field
        // means zero extra per-weapon authoring. Safe to read directly off the prefab ASSET (no
        // Instantiate needed) since the SpriteRenderer is a plain sibling component wired in the
        // Inspector, not something WeaponView itself builds up at runtime.
        public Sprite GetIcon()
        {
            return ViewPrefab != null ? ViewPrefab.GetComponent<SpriteRenderer>()?.sprite : null;
        }

        [Tooltip("Camera shake tier applied (to the local player only) each time this weapon fires - see WeaponCameraShakeListener/CameraShakeConfig.")]
        public WeaponShakeTier ShakeTier = WeaponShakeTier.Small;

        [Header("Projectile Visuals")]
        public ProjectileVisualsConfig ProjectileVisuals = new ProjectileVisualsConfig();

        // Editor-only preview, not read by simulation or any other View code - just a quick sanity
        // check while tuning Damage/FireRate/MagazineSize/ReloadDuration together in the Inspector.
        // Recomputed on every value change (OnValidate) and on load (OnEnable) rather than being an
        // editable field, so it can never drift out of sync with the stats above it.
        [Header("Preview")]
        [Tooltip("Recomputed automatically - not an input. Burst ignores reload (Damage x Pellets x FireRate); Sustained folds in how long a full magazine + reload actually takes, so a small mag/long reload weapon reads lower here than its burst number alone would suggest. Both fold in this weapon's own CriticalChance/CriticalDamageBonus and every quantifiable BaseTraits perk (ramp, burst-fire, split shot, ...) - deliberately weapon-only (no hero CharacterStats blended in, since a weapon asset alone has no equipped hero to read), so CriticalDamageBonus is read directly as the crit multiplier (Bonus 2.5 on 100 Damage = 250 on crit) rather than stacked onto a hero baseline. A perk this preview can't quantify (Crit Stun, ...) is silently skipped, same as the Balance Simulator's own UnquantifiedBonus fallback. A weapon that hits more than one enemy per shot (Pierce, Ricochet, Split Shot, an Area blast radius) also gets an 'Effective DPS (est. N targets/shot)' line - the single-target numbers above stay a fair apples-to-apples baseline, this second line is what actually judges whether a multi-hit weapon's own Damage should be lower for hitting a crowd instead of one enemy.")]
        [TextArea(4, 10)]
        [SerializeField]
        private string _dpsPreview;

        private void OnEnable()
        {
            _dpsPreview = BuildDpsPreview();
        }

        private void OnValidate()
        {
            _dpsPreview = BuildDpsPreview();
        }

        private string BuildDpsPreview()
        {
            float damage = Damage.AsFloat;
            float fireRate = FireRate.AsFloat;
            int magazineSize = MagazineSize;
            float reloadDuration = Mathf.Max(0f, ReloadDuration.AsFloat);
            float criticalChance = Mathf.Clamp01(CriticalChance.AsFloat);
            float criticalMultiplier = Mathf.Max(1f, CriticalDamageBonus.AsFloat);
            float perShotFactor = 1f;
            float estimatedTargets = 1f;

#if UNITY_EDITOR
            // QuantumUnityDB.GetGlobalAssetEditorInstance only exists in-Editor (see its own "use in
            // OnValidate etc." doc comment - Quantum.Simulation must still compile for a headless
            // dedicated-server build, which has no Unity asset database to resolve BaseTraits against).
            ApplyBaseTraits(ref damage, ref fireRate, ref magazineSize, ref reloadDuration,
                ref criticalChance, ref criticalMultiplier, ref perShotFactor, ref estimatedTargets);
            ApplyProjectileHitTargets(ref estimatedTargets);

            // The weapon's OWN authored baseline (WeaponDataAsset.BonusBounces - "Ricochet bounces
            // this weapon starts with, before any Ricochet perk's own BonusBounces" per its own
            // comment) stacks additively on top of a Ricochet BaseTraits perk for either fire type
            // (WeaponSystem.cs applies both `weaponData.BonusBounces` and `mods->BonusBounces` onto
            // the same RemainingBounces/bounces count) - Arcshot's is 0 today (all its bounce comes
            // from its Ricochet BaseTraits pick instead), but nothing stops a weapon authoring this
            // directly with no perk involved at all, so it has to be counted here too, not just
            // ApplyBaseTraits' BaseTraits loop. No equivalent baseline exists for pierce - a hitscan
            // shot's own pierce count is always exactly 1 before perks (WeaponSystem.FireHitscanPellet
            // - "int pierces = 1"), and a Projectile weapon's baseline pierce already comes from its
            // own DirectHitData.PierceCount, handled in ApplyProjectileHitTargets above.
            estimatedTargets += BonusBounces * EstimatedExtraTargetConnectChance;

            estimatedTargets = Mathf.Clamp(estimatedTargets, 1f, 8f);
#endif

            if (fireRate <= 0f)
                return "DPS: n/a (FireRate is 0)";

            // Weapon-only (no hero CharacterStats blended in - see class-level tooltip). Multiplier
            // is floored at 1 same as DamageUtility.ResolveDamage, so an unset/0 CriticalDamageBonus
            // reads as "no crit bonus" (crit = normal damage) rather than a zero-damage crit.
            float expectedHitMultiplier = 1f + criticalChance * (criticalMultiplier - 1f);

            float normalDamagePerShot = damage * Mathf.Max(1, PelletCount) * perShotFactor;
            float criticalDamagePerShot = normalDamagePerShot * criticalMultiplier;
            float damagePerShot = normalDamagePerShot * expectedHitMultiplier;
            float burstDps = damagePerShot * fireRate;

            if (magazineSize <= 0)
                return $"Burst DPS: {burstDps:0.#} (MagazineSize is 0 - can never fire)";

            float magazineDuration = magazineSize / fireRate;
            float cycleDuration = magazineDuration + reloadDuration;
            float sustainedDps = damagePerShot * magazineSize / cycleDuration;

            string preview = $"Burst DPS: {burstDps:0.#}\nSustained DPS (incl. reload): {sustainedDps:0.#}\nDamage/min: {sustainedDps * 60f:0}\nCrit dmg/shot: {criticalDamagePerShot:0.#} (vs {normalDamagePerShot:0.#} normal) @ {criticalChance:P0} chance, x{criticalMultiplier:0.##}";

            // Only shown for a weapon that can actually hit more than one enemy per shot - the single-
            // target numbers above stay the fair baseline to compare every weapon against; this is the
            // number that says whether THIS weapon's own Damage should be lower for reaching a crowd.
            if (estimatedTargets > 1.01f)
            {
                preview += $"\nEst. targets/shot: {estimatedTargets:0.#}\nEffective Burst DPS (all targets): {burstDps * estimatedTargets:0.#}\nEffective Sustained DPS (all targets): {sustainedDps * estimatedTargets:0.#}";
            }

            return preview;
        }

#if UNITY_EDITOR
        // Folds every BaseTraits perk this preview can quantify into the same locals BuildDpsPreview
        // already works with, mirroring BalanceSimModel.SimWeapon's Quantify/Dps formulas (the Balance
        // Simulator's own, more thorough DPS math) - just re-derived here with plain floats since that
        // class lives in the Editor-only Quantum.Unity.Editor assembly and can't be referenced from
        // here. Ramp (Relentless Fire/Suppressive Cycle/Overcharge Cycle) uses the same "half of max
        // stacks" decisive placeholder the simulator documents (a sustained-fire figure can't just
        // assume everyone sits at max stacks the whole time); Burst Fire blends BurstDelay into the
        // effective cadence the same way. Ricochet/Piercing Rounds/Split Shot don't touch the
        // single-target numbers at all - they only feed estimatedTargets (see its own comment below),
        // since a bounce/pierce/fragment redirects at or spawns toward a DIFFERENT enemy, not extra
        // damage on the one already being hit. A perk with nothing quantifiable here at all (Crit
        // Stun, ...) falls through untouched - this preview has no hero-facing "unquantified" flat
        // bonus to credit it with, unlike the Simulator's own UnquantifiedBonus knob.
        // A bounce/pierce/split-shot fragment only pays off if another enemy is actually there to
        // catch it (DirectHitData.TryRicochet's own search comes up empty plenty of the time, a
        // pierce's straight-line path may have nothing standing behind the first target) - so each
        // extra hit OPPORTUNITY beyond the guaranteed primary target is credited at this fraction
        // instead of a full extra target (1 bounce reads as 1.5 targets/shot, not 2). A decisive
        // placeholder, not a measured connect rate - same spirit as the Balance Simulator's own
        // AreaTargets/ChannelContactUptime knobs; retune once checked against real crowd density.
        private const float EstimatedExtraTargetConnectChance = 0.5f;

        private void ApplyBaseTraits(ref float damage, ref float fireRate, ref int magazineSize, ref float reloadDuration,
            ref float criticalChance, ref float criticalMultiplier, ref float perShotFactor, ref float estimatedTargets)
        {
            int burstCount = 1;
            float burstDelay = 0f;
            float rampMaxStacks = 0f;
            float rampDamageBonusPerStack = 0f;
            float rampFireRateBonusPerStack = 0f;
            int extraHitOpportunities = 0;

            for (int i = 0; i < BaseTraits.Count; i++)
            {
                if (BaseTraits[i].IsValid == false)
                    continue;

                switch (QuantumUnityDB.GetGlobalAssetEditorInstance(BaseTraits[i]))
                {
                    case DamageMultiplierWeaponPerkData p: damage *= p.Multiplier.AsFloat; break;
                    case FireRateWeaponPerkData p: fireRate *= Mathf.Max(0.01f, p.Multiplier.AsFloat); break;
                    case CooldownMultiplierWeaponPerkData p: fireRate /= Mathf.Max(0.01f, p.Multiplier.AsFloat); break;
                    case HeavyCaliberWeaponPerkData p:
                        damage *= p.DamageMultiplier.AsFloat;
                        fireRate *= Mathf.Max(0.01f, p.FireRateMultiplier.AsFloat);
                        break;
                    case CriticalChanceWeaponPerkData p: criticalChance = Mathf.Clamp01(criticalChance + p.Chance.AsFloat); break;
                    case CriticalDamageWeaponPerkData p: criticalMultiplier += p.Bonus.AsFloat; break;
                    case MagazineMultiplierWeaponPerkData p: magazineSize = Mathf.Max(1, Mathf.RoundToInt(magazineSize * p.Multiplier.AsFloat)); break;
                    case ReloadSpeedWeaponPerkData p: reloadDuration /= Mathf.Max(0.01f, p.Multiplier.AsFloat); break;
                    case FinalRoundWeaponPerkData p: perShotFactor *= 1f + p.DamageBonus.AsFloat / Mathf.Max(1, magazineSize); break;
                    case SplitShotWeaponPerkData p:
                        perShotFactor *= 1f + p.Count * p.DamageMultiplier.AsFloat;
                        extraHitOpportunities += p.Count;
                        break;
                    case PiercingRoundsWeaponPerkData p: extraHitOpportunities += p.BonusPierce; break;
                    case RicochetWeaponPerkData p: extraHitOpportunities += p.BonusBounces; break;
                    case ExplosiveCritWeaponPerkData p: perShotFactor *= 1f + criticalChance * p.DamageMultiplier.AsFloat; break;
                    case ExplosiveSequenceWeaponPerkData p: perShotFactor *= 1f + p.DamageMultiplier.AsFloat / Mathf.Max(1, p.Interval); break;
                    case RelentlessFireWeaponPerkData p:
                        rampMaxStacks = Mathf.Max(rampMaxStacks, p.MaxStacks);
                        rampDamageBonusPerStack += p.DamageBonusPerStack.AsFloat;
                        break;
                    case SuppressiveCycleWeaponPerkData p:
                        rampMaxStacks = Mathf.Max(rampMaxStacks, p.MaxStacks);
                        rampFireRateBonusPerStack += p.FireRateBonusPerStack.AsFloat;
                        break;
                    case OverchargeCycleWeaponPerkData p:
                        rampMaxStacks = Mathf.Max(rampMaxStacks, p.MaxStacks);
                        rampDamageBonusPerStack += p.DamageBonusPerStack.AsFloat;
                        rampFireRateBonusPerStack += p.FireRateBonusPerStack.AsFloat;
                        break;
                    case BurstFireWeaponPerkData p:
                        burstCount = p.BurstCount;
                        burstDelay = p.BurstDelay.AsFloat;
                        break;
                }
            }

            if (rampMaxStacks > 0f)
            {
                perShotFactor *= 1f + rampMaxStacks * rampDamageBonusPerStack * 0.5f;
                fireRate *= 1f + rampMaxStacks * rampFireRateBonusPerStack * 0.5f;
            }

            // Double Barrel/Burst Rifle's own baseline BurstFireWeaponPerkData - see WeaponSystem's
            // burst-start block/SimWeapon.Dps's own copy of this same blend.
            if (burstCount > 1 && fireRate > 0f)
            {
                fireRate = burstCount / ((burstCount - 1) * burstDelay + 1f / fireRate);
            }

            estimatedTargets += extraHitOpportunities * EstimatedExtraTargetConnectChance;
        }

        // The weapon's OWN authored Hit data (ProjectileData.Hit, not a BaseTraits perk) - a baseline
        // DirectHitData.PierceCount above 1, or an AreaHitData's own BlastRadius, means this weapon
        // hits more than one enemy per shot before any perk is even involved (see Arcshot/Napalm).
        // Hitscan has no equivalent asset to check here (its own baseline is always single-target;
        // WeaponFireTimeMods.BonusPierce/BonusBounces above already covers anything a perk adds on top
        // of that for either fire type).
        private void ApplyProjectileHitTargets(ref float estimatedTargets)
        {
            if (FireType != WeaponFireType.Projectile || ProjectileData.IsValid == false)
                return;

            ProjectileDataAsset projectileData = QuantumUnityDB.GetGlobalAssetEditorInstance(ProjectileData);

            if (projectileData == null)
                return;

            switch (QuantumUnityDB.GetGlobalAssetEditorInstance(projectileData.Hit))
            {
                case DirectHitData d:
                    estimatedTargets += Mathf.Max(0, d.PierceCount - 1) * EstimatedExtraTargetConnectChance;
                    break;

                case AreaHitData a:
                    estimatedTargets += a.BlastRadius.AsFloat * EstimatedExtraTargetConnectChance;
                    break;
            }
        }
#endif
    }
}
