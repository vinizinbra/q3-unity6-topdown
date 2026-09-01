namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors Zara's Flow State base passive and her 9 Ascension lines (Amplifier/Sound Boost/
    // Double Time/Main Stage on the Totem Hero Skill; Faster Tempo/Second Wind/Headliner as Passives;
    // Afterbeat/Portable Speaker as Dash Ascensions - see docs/zara-ascensions.md), then
    // wires all of it into ZaraCharacterData.asset and ZaraBaseSkill.asset. Replaces
    // ZaraResonanceAssetGenerator.cs - same "one generator fully replaces every list it touches end to
    // end" fix Brute/Max/Pixie/Kai's own refactors already applied for the identical append-vs-replace
    // drift bug (the old generator's own WireCharacterData only appended-and-deduped DashSkillUpgrades,
    // which is exactly why the old, broken PortableSpeaker.asset survived every prior regeneration).
    //
    // Amplifier/Healing Chorus/Double Time/Main Stage are SkillActionData living on
    // ZaraBaseSkill.Actions (Activated = false), NOT PassiveUpgradeData - same "Hero Skill Ascension"
    // shape every other hero's own refactor already established.
    //
    // Per-rank tuned values ARE explicitly set here on every run, even though every ranked ascension
    // class already carries a matching C# field-initializer default - that default only applies to a
    // BRAND NEW object, not one that already existed before a field's TYPE changed shape (a plain FP
    // becoming FP[]). Explicitly setting every array here every run is what makes CreateOrUpdate
    // idempotent and correct for pre-existing assets, not just newly created ones.
    public static class ZaraAscensionAssetGenerator
    {
        private const string PassivesFolderPath = "Assets/_QuantumUser/Resources/Passives/Zara";
        private const string PassiveUpgradesFolderPath = "Assets/_QuantumUser/Resources/Passives/Zara/Zara_PassiveSkillUpgrades";
        private const string HeroSkillUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Zara/Zara_HeroSkill/Zara_HeroSkillUpgrades";
        private const string DashUpgradesFolderPath = "Assets/_QuantumUser/Resources/Skills/Zara/Zara_DashSkillUpgrades";
        // The ONE place Portable Speaker's "reduced effectiveness" number lives. Mobile Stage is
        // deliberately "reuse the Totem Beat architecture with a different DATA profile", so the
        // Speaker's own buff/cooldown assets are authored here at this fraction rather than being
        // multiplied down at runtime - which keeps the Speaker's real numbers readable/tunable in the
        // Inspector instead of hidden behind a multiplier applied somewhere else.
        private static readonly FP SpeakerEffectFraction = FP._0_50;

        private const string SharedEffectsFolderPath = "Assets/_QuantumUser/Resources/Skills/Zara/Zara_SharedEffects";
        private const string CharacterDataPath = "Assets/_QuantumUser/Resources/Characters/ZaraCharacterData.asset";
        private const string ZaraBaseSkillPath = "Assets/_QuantumUser/Resources/Skills/Zara/Zara_HeroSkill/ZaraBaseSkill.asset";
        private const string HitEffectsFolderPath = "Assets/_QuantumUser/Resources/HitEffects";
        // ZaraSpeaker.prefab is the Totem's own real placed entity (already has the correct
        // AreaDamage+AlternatingArea setup) - reused directly as Portable Speaker's own spawn
        // prototype too, rather than a dedicated one, same "Dash mini-version reuses the Hero
        // Skill's own entity" precedent Kai's Warp Wake already established (cosmetic follow-up, not
        // a functional gap). ZaraDeviceSpeaker.prefab is NOT this - it's the THROWN PROJECTILE visual
        // that flies from Zara to the Totem's landing spot (ZaraThrowProjectileSpeaker.Prototype),
        // confirmed by the user - do not point anything at it for Portable Speaker.
        private const string PortableSpeakerPrototypePath = "Assets/_QuantumUser/Resources/Skills/Zara/ZaraEntities/ZaraSpeaker.prefab";

        [MenuItem("Tools/RiftRaiders/Zara/Generate Ascension Assets")]
        internal static void Generate()
        {
            CreateFolderRecursive(PassivesFolderPath);
            CreateFolderRecursive(PassiveUpgradesFolderPath);
            CreateFolderRecursive(HeroSkillUpgradesFolderPath);
            CreateFolderRecursive(DashUpgradesFolderPath);
            CreateFolderRecursive(SharedEffectsFolderPath);

            // Shared generic effect assets - authored once, referenced by whichever ranks/lines need
            // them. Everything Zara grants an ally now routes through ONE generic effect class
            // (AllyBuffEffectData) rather than a per-stat effect asset each, so a designer tunes a
            // whole buff profile in one place regardless of whether a Totem, a Speaker or Lux's Sentry
            // aura is emitting it.
            var scaledHeal = CreateOrUpdate<ScaledHealEffectData>($"{SharedEffectsFolderPath}/ZaraScaledHealPulse.asset", a => a.HealMultiplier = FP._1);

            // Baseline Support Beat buff - what a Totem grants with NO Sound Boost rank at all.
            var baseSupportBuff = CreateOrUpdate<AllyBuffEffectData>($"{SharedEffectsFolderPath}/ZaraSupportBeatBuff.asset", a =>
            {
                a.Duration = FP._2;
                a.MoveSpeedBonus = FP._0_10;
                a.FireRateBonus = FP._0_10;
            });

            // Sound Boost's own per-rank profiles. Rank 3 "Power Chord" adds the outgoing-damage
            // window; ranks 1-2 share the same stronger Move Speed / Fire Rate values.
            var soundBoostBuffR1 = CreateOrUpdate<AllyBuffEffectData>($"{SharedEffectsFolderPath}/ZaraSoundBoostBuff_R1.asset", a =>
            {
                a.Duration = FP._2;
                a.MoveSpeedBonus = FP.FromString("0.15");
                a.FireRateBonus = FP.FromString("0.15");
            });
            var soundBoostBuffR3 = CreateOrUpdate<AllyBuffEffectData>($"{SharedEffectsFolderPath}/ZaraSoundBoostBuff_R3.asset", a =>
            {
                a.Duration = FP._2;
                a.MoveSpeedBonus = FP.FromString("0.15");
                a.FireRateBonus = FP.FromString("0.15");
                a.OutgoingDamageBonus = FP.FromString("0.15");
            });

            // Portable Speaker's reduced-effect counterparts ("Mobile Stage" = a different DATA
            // PROFILE, not different code). SpeakerEffectFraction is the one place that halving lives.
            var speakerSupportBuff = CreateOrUpdate<AllyBuffEffectData>($"{SharedEffectsFolderPath}/ZaraSpeakerSupportBuff.asset", a =>
            {
                a.Duration = FP._2;
                a.MoveSpeedBonus = FP._0_10 * SpeakerEffectFraction;
                a.FireRateBonus = FP._0_10 * SpeakerEffectFraction;
            });
            var speakerSoundBoostBuffR1 = CreateOrUpdate<AllyBuffEffectData>($"{SharedEffectsFolderPath}/ZaraSpeakerSoundBoostBuff_R1.asset", a =>
            {
                a.Duration = FP._2;
                a.MoveSpeedBonus = FP.FromString("0.15") * SpeakerEffectFraction;
                a.FireRateBonus = FP.FromString("0.15") * SpeakerEffectFraction;
            });
            var speakerSoundBoostBuffR3 = CreateOrUpdate<AllyBuffEffectData>($"{SharedEffectsFolderPath}/ZaraSpeakerSoundBoostBuff_R3.asset", a =>
            {
                a.Duration = FP._2;
                a.MoveSpeedBonus = FP.FromString("0.15") * SpeakerEffectFraction;
                a.FireRateBonus = FP.FromString("0.15") * SpeakerEffectFraction;
                a.OutgoingDamageBonus = FP.FromString("0.15") * SpeakerEffectFraction;
            });

            // Sound Boost rank 2+ - the generic Hero-Skill-cooldown-reduction effect, budget-capped
            // per Totem per ally by AreaAllyBudget. Not a Zara-specific mechanism.
            var cooldownEffect = CreateOrUpdate<ModifyRemainingCooldownEffectData>($"{SharedEffectsFolderPath}/ZaraSupportCooldownEffect.asset", a =>
            {
                a.Slot = SkillSlotId.HeroSkill;
                a.Amount = FP._0_50;
                a.RespectAreaBudget = true;
            });
            var speakerCooldownEffect = CreateOrUpdate<ModifyRemainingCooldownEffectData>($"{SharedEffectsFolderPath}/ZaraSpeakerCooldownEffect.asset", a =>
            {
                a.Slot = SkillSlotId.HeroSkill;
                a.Amount = FP._0_50 * SpeakerEffectFraction;

                // A Speaker carries no AreaAllyBudget of its own (nothing to cap), so this one is
                // deliberately uncapped per application - its own short lifetime is the limit.
                a.RespectAreaBudget = false;
            });

            var amplifierKnockback = CreateOrUpdate<KnockbackEffectData>($"{SharedEffectsFolderPath}/AmplifierKnockback.asset", a => a.Tier = KnockbackTier.Small);
            var bassDropStun = LoadHitEffect("StunEffectData");

            // Passive base - Flow State, replacing Resonance entirely (see Flow.qtn).
            FlowStatePassiveData passive = CreateOrUpdate<FlowStatePassiveData>($"{PassivesFolderPath}/FlowStatePassiveData.asset", a =>
            {
                a.Description = "Keep moving to fill Flow. At full Flow, gain Move Speed and Fire Rate.";

                a.BuildDuration = FP.FromString("2.5");
                a.MoveSpeedBonus = FP.FromString("0.15");
                a.FireRateBonus = FP.FromString("0.15");
                a.MovementInputThreshold = FP._0_10;
                a.StationaryGrace = FP.FromString("1.25");
                a.DecayDuration = FP.FromString("4.5");
            });

            // 4 Hero Skill lines (ranked SkillActionData, wired into ZaraBaseSkill.Actions below)
            AmplifierSkillAction amplifier = CreateOrUpdate<AmplifierSkillAction>($"{HeroSkillUpgradesFolderPath}/AmplifierSkillAction.asset", a =>
            {
                a.DisplayName = "Amplifier";
                a.Activated = false;
                a.MaxRank = 3;
                a.Description = "Totem Damage Beats deal more damage.";
                a.RankDescriptions = new[]
                {
                    "Totem Damage Beats deal 30% more damage.",
                    "Totem Damage Beats deal 60% more damage and knock enemies back.",
                    "Totem Damage Beats deal double damage, and every third one Stuns enemies.",
                };
                a.DamageBonus = new[] { FP.FromString("0.30"), FP.FromString("0.60"), FP._1 };
                a.KnockbackEffect = amplifierKnockback;
                a.StunInterval = new byte[] { 0, 0, 3 };
                a.StunEffect = bassDropStun;
            });

            SoundBoostSkillAction soundBoost = CreateOrUpdate<SoundBoostSkillAction>($"{HeroSkillUpgradesFolderPath}/SoundBoostSkillAction.asset", a =>
            {
                a.DisplayName = "Sound Boost";
                a.Activated = false;
                a.MaxRank = 3;
                a.Description = "Support Beats push the whole team's tempo - stronger buffs, then Hero Skill cooldown reduction, then an outgoing-damage window.";
                a.RankDescriptions = new[]
                {
                    "Support Beats heal 2% Max Health and grant +15% Move Speed and +15% Fire Rate.",
                    "Support Beats heal 2% Max Health, grant +15% Move Speed and Fire Rate, and cut 0.5s off allies' skill cooldown.",
                    "Support Beats heal 5% Max Health, grant +15% Move Speed and Fire Rate, cut 0.5s off cooldowns and +15% damage.",
                };
                a.HealPercent = new[] { FP.FromString("0.02"), FP.FromString("0.02"), FP._0_05 };
                a.SupportBuffEffect = new[]
                {
                    new AssetRef<HitEffectData>(soundBoostBuffR1.Guid),
                    new AssetRef<HitEffectData>(soundBoostBuffR1.Guid),
                    new AssetRef<HitEffectData>(soundBoostBuffR3.Guid),
                };

                // Rank 1's entry is deliberately left invalid - that is what keeps cooldown reduction
                // off until rank 2, with no extra flag to keep in sync.
                a.CooldownEffect = new[]
                {
                    default(AssetRef<HitEffectData>),
                    new AssetRef<HitEffectData>(cooldownEffect.Guid),
                    new AssetRef<HitEffectData>(cooldownEffect.Guid),
                };

                // Exposed exactly as the brief asks. Left generous for the first playtest; expected
                // tuning range is 3-4s. This is the single knob that keeps Sound Boost + Double Time
                // (many more beats per Totem) from collapsing the whole team's Hero Skill cooldowns.
                a.MaxCooldownReductionPerTotem = 6;

                a.SpeakerSupportBuffEffect = new[]
                {
                    new AssetRef<HitEffectData>(speakerSoundBoostBuffR1.Guid),
                    new AssetRef<HitEffectData>(speakerSoundBoostBuffR1.Guid),
                    new AssetRef<HitEffectData>(speakerSoundBoostBuffR3.Guid),
                };
                a.SpeakerCooldownEffect = new[]
                {
                    default(AssetRef<HitEffectData>),
                    new AssetRef<HitEffectData>(speakerCooldownEffect.Guid),
                    new AssetRef<HitEffectData>(speakerCooldownEffect.Guid),
                };
            });

            DoubleTimeSkillAction doubleTime = CreateOrUpdate<DoubleTimeSkillAction>($"{HeroSkillUpgradesFolderPath}/DoubleTimeSkillAction.asset", a =>
            {
                a.DisplayName = "Double Time";
                a.Activated = false;
                a.MaxRank = 3;
                a.Description = "Totem Beats occur more often.";
                a.RankDescriptions = new[]
                {
                    "Totem Beats occur every 0.85s.",
                    "Totem Beats occur every 0.70s.",
                    "Totem Beats occur every 0.5s.",
                };
                a.BeatInterval = new[] { FP.FromString("0.85"), FP.FromString("0.70"), FP._0_50 };
            });

            MainStageSkillAction mainStage = CreateOrUpdate<MainStageSkillAction>($"{HeroSkillUpgradesFolderPath}/MainStageSkillAction.asset", a =>
            {
                a.DisplayName = "Main Stage";
                a.Activated = false;
                a.MaxRank = 3;
                a.Description = "Increase Totem Beat radius.";
                a.RankDescriptions = new[]
                {
                    "Increase Totem Beat radius by 30%.",
                    "Totem Beat radius +50%, and the Totem lasts 2s longer.",
                    "Totem Beat radius +75% and it lasts 2s longer, opening with a Damage Beat and ending with a Healing Beat.",
                };
                a.RadiusBonus = new[] { FP.FromString("0.30"), FP._0_50, FP.FromString("0.75") };
                a.DurationBonus = new[] { FP._0, FP._2, FP._2 };
            });

            // 3 Passive lines (ranked PassiveUpgradeData) - all three rebuilt on Flow.
            FasterTempoPassiveUpgradeData fasterTempo = CreateOrUpdate<FasterTempoPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/FasterTempo.asset", a =>
            {
                a.DisplayName = "Faster Tempo";
                a.MaxRank = 3;
                a.Description = "Reach Flow faster, and make every stack of it worth more.";
                a.RankDescriptions = new[]
                {
                    "Flow builds 25% faster.",
                    "Flow builds 50% faster, and active Flow grants +18% Move Speed and +18% Fire Rate.",
                    "Flow builds 75% faster, and active Flow grants a further +10% Fire Rate.",
                };

                a.BuildRateMultiplier = new[] { FP.FromString("1.25"), FP.FromString("1.50"), FP.FromString("1.75") };

                // Rank 1 restates the 15% baseline rather than leaving it alone, so a re-pick at any
                // rank always writes a complete, correct value into ZaraFlow.
                a.MoveSpeedBonus = new[] { FP.FromString("0.15"), FP.FromString("0.18"), FP.FromString("0.18") };
                a.FireRateBonus = new[] { FP.FromString("0.15"), FP.FromString("0.18"), FP.FromString("0.18") };
                a.ActiveFireRateBonus = new[] { FP._0, FP._0, FP._0_10 };
            });

            SecondWindPassiveUpgradeData secondWind = CreateOrUpdate<SecondWindPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/SecondWind.asset", a =>
            {
                a.DisplayName = "Second Wind";
                a.MaxRank = 3;
                a.Description = "Recover faster when your rhythm is broken.";
                a.RankDescriptions = new[]
                {
                    "When a hit breaks your Flow, gain +20% Move Speed for 1.5s.",
                    "A hit no longer empties your Flow - it drops to a third instead, and you still gain +20% Move Speed for 1.5s.",
                    "A hit taken while Flow is active deals 30% less damage (6s cooldown).",
                };

                a.MoveSpeedBonus = new[] { FP._0_20, FP._0_20, FP._0_20 };
                a.Duration = new[] { FP.FromString("1.5"), FP.FromString("1.5"), FP.FromString("1.5") };
                a.ProgressRetainedOnHit = new[] { FP._0, FP.FromString("0.33"), FP.FromString("0.33") };
                a.DamageReduction = new[] { FP._0, FP._0, FP.FromString("0.30") };
                a.Cooldown = new[] { FP._0, FP._0, FP._6 };
            });

            HeadlinerPassiveUpgradeData headliner = CreateOrUpdate<HeadlinerPassiveUpgradeData>($"{PassiveUpgradesFolderPath}/Headliner.asset", a =>
            {
                a.DisplayName = "Headliner";
                a.MaxRank = 3;
                a.Description = "Max Flow becomes a payoff - for your damage, your Totem, and your whole team.";
                a.RankDescriptions = new[]
                {
                    "While Flow is active, you deal +10% damage.",
                    "While Flow is active, you deal +10% damage and your Totem's Beats are 15% more effective.",
                    "Activating Flow grants you and allies within 6m +10% Move Speed and Fire Rate for 3s (8s cooldown).",
                };

                a.ActiveDamageBonus = new[] { FP._0_10, FP._0_10, FP._0_10 };
                a.ActiveBeatEffectiveness = new[] { FP._0, FP.FromString("0.15"), FP.FromString("0.15") };
                a.HypeRadius = new[] { FP._0, FP._0, FP._6 };
                a.HypeDuration = new[] { FP._0, FP._0, FP._3 };
                a.HypeMoveSpeedBonus = new[] { FP._0, FP._0, FP._0_10 };
                a.HypeFireRateBonus = new[] { FP._0, FP._0, FP._0_10 };
                a.HypeCooldown = new[] { FP._0, FP._0, FP._8 };
            });

            // 2 Dash lines
            AfterbeatSkillAction afterbeat = CreateOrUpdate<AfterbeatSkillAction>($"{DashUpgradesFolderPath}/AfterbeatSkillAction.asset", a =>
            {
                a.DisplayName = "Afterbeat";
                a.MaxRank = 3;
                // Explicit, not just the class's own constructor default - CreateOrUpdate re-uses a
                // pre-existing asset instance rather than constructing a fresh one, so a stale
                // serialized Phase (e.g. an accidental Inspector edit, exactly what happened to this
                // asset once already - see docs/zara-ascensions.md's "Corrections" section) would
                // otherwise silently survive every future regeneration.
                a.Phase = SkillActionPhase.Begin | SkillActionPhase.End;
                a.Phase = SkillActionPhase.Begin | SkillActionPhase.OnGoing | SkillActionPhase.End;
                a.Interval = FP._0;
                a.Description = "Dashing feeds your Flow - and eventually leaves damaging beats behind you.";
                a.RankDescriptions = new[]
                {
                    "Dashing fills a third of your Flow bar, plus more for each enemy you pass through.",
                    "Dashing fills your Flow bar, and 1s later a beat erupts at your start point, damaging and knocking enemies back.",
                    "Beats erupt at both ends of the dash - landing either on an enemy fills your Flow bar again (once per dash).",
                };
                a.FlowProgressOnDash = FP.FromString("0.35");
                a.SweepRadius = FP._1_50;

                // One shared per-dash allowance covers BOTH rank 1's dash sweep and rank 3's pulse
                // hits, so the two can never compound past the cap.
                a.ProgressPerEnemyHit = FP._0_10;
                a.MaxProgressPerDash = FP.FromString("0.40");

                a.Delay = FP._1;
                a.DamagePercentOfSkill = new[] { FP._0, FP.FromString("0.75"), FP.FromString("0.75") };
                a.Radius = new[] { FP._0, FP._4, FP._4 };
                a.KnockbackForce = new[] { FP._0, FP._6, FP._6 };
            });

            AssetRef<HitEffectData> damageEffect = ConfigureTotemThrow(new AssetRef<HitEffectData>(scaledHeal.Guid), new AssetRef<HitEffectData>(baseSupportBuff.Guid));
            AssetRef<EntityPrototype> speakerPrototype = ResolvePortableSpeakerPrototype();

            PortableSpeakerSkillAction portableSpeaker = CreateOrUpdate<PortableSpeakerSkillAction>($"{DashUpgradesFolderPath}/PortableSpeakerSkillAction.asset", a =>
            {
                a.DisplayName = "Portable Speaker";
                a.MaxRank = 3;
                // Explicit, not just the class's own constructor default - see AfterbeatSkillAction's
                // identical comment above for why this matters on every future regeneration, not just
                // the first.
                a.Phase = SkillActionPhase.Begin | SkillActionPhase.End;
                a.Description = "Dashing leaves behind a Portable Speaker running the same Damage/Support rhythm at half strength.";
                a.RankDescriptions = new[]
                {
                    "Dashing leaves a Portable Speaker alternating Damage and Support Beats at half strength.",
                    "Dashing leaves a Portable Speaker that lasts longer and covers a wider area; the dash also buffs nearby allies.",
                    "The Portable Speaker inherits your Beat interval, radius and Sound Boost profile at reduced effectiveness.",
                };
                a.Prototype = speakerPrototype;
                a.Duration = new[] { FP._3, FP._4, FP._4 };
                a.BaseRadius = FP._3;
                a.RadiusMultiplier = new[] { FP._1, FP.FromString("1.30"), FP.FromString("1.30") };
                a.BeatInterval = FP._1;

                // At most one live Speaker per Zara at ranks 1-2; rank 3 optionally allows a second.
                // A new one past the cap silently retires her oldest.
                a.MaxActiveSpeakers = new byte[] { 1, 1, 2 };

                a.TotemBaseDamage = 10;
                a.DamagePercentOfTotem = SpeakerEffectFraction;
                a.DamageEffect = damageEffect;

                // Support Beat now heals as well as buffing, at half the Totem's live value. Reuses the
                // Totem's OWN ScaledHealEffectData asset rather than a Speaker-specific copy - that
                // effect takes its percentage from AlternatingArea.HealAmount, which SpawnSpeaker
                // halves, so one asset covers both and there is no second heal number to drift.
                //
                // These two values are the Totem's, mirrored (see PortableSpeakerSkillAction's own
                // comment on TotemBaseDamage) - keep them in step with spawnArea.HealAmount /
                // MaxHealFractionPerAlly in ConfigureTotemThrow below, which is why both live in this
                // one file.
                a.HealEffect = new AssetRef<HitEffectData>(scaledHeal.Guid);
                a.TotemBaseHeal = FP.FromString("0.01");
                a.HealPercentOfTotem = SpeakerEffectFraction;
                a.MaxHealFractionPerAlly = FP._0_10;

                a.SupportBuffEffect = new AssetRef<HitEffectData>(speakerSupportBuff.Guid);
                a.DashEndBuffEffect = new AssetRef<HitEffectData>(speakerSupportBuff.Guid);
                a.DashEndBuffRadius = FP._5;
                a.MobileStageInheritanceFraction = SpeakerEffectFraction;
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            WireCharacterData(passive,
                new List<PassiveUpgradeData> { fasterTempo, secondWind, headliner },
                new List<SkillActionData> { afterbeat, portableSpeaker });

            WireTotemActions(new List<SkillActionData> { amplifier, soundBoost, doubleTime, mainStage });

            LogHelper.Log("ZaraAscensionAssetGenerator", "Flow State passive + 9 Ascension lines authored and wired (3 Passive Upgrades " +
                      "into ZaraCharacterData.PassiveUpgrades, Afterbeat/Portable Speaker into ZaraCharacterData.DashSkillUpgrades, " +
                      "Amplifier/Sound Boost/Double Time/Main Stage into ZaraBaseSkill.Actions as Hero Skill Ascensions - every list " +
                      "fully replaced, not appended; every per-rank value is re-set explicitly on every run). Portable Speaker's Prototype " +
                      "is ZaraSpeaker.prefab (the Totem's own placed entity, reused directly).");
        }

        // Configures the embedded projectile-chain sub-objects inside ZaraBaseSkill.asset
        // (SpawnAlternatingAreaEffectData's own base Damage/Heal amounts, and the shared
        // ZaraScaledHealPulse asset in its HealEffects) - these aren't separate .asset files, so
        // CreateOrUpdate<T> doesn't apply; they're found by scanning the file's own sub-objects, same
        // as KaiAscensionAssetGenerator.ResolveKaiVortexPrototype. Returns the embedded
        // ZaraVoidPulseDamage's own AssetRef so Portable Speaker's DamageEffect can reuse the exact
        // same asset rather than duplicating it.
        private static AssetRef<HitEffectData> ConfigureTotemThrow(AssetRef<HitEffectData> scaledHealEffect, AssetRef<HitEffectData> supportBuffEffect)
        {
            var subObjects = AssetDatabase.LoadAllAssetsAtPath(ZaraBaseSkillPath);

            SpawnAlternatingAreaEffectData spawnArea = null;
            DamageEffectData voidPulseDamage = null;

            foreach (var obj in subObjects)
            {
                if (obj is SpawnAlternatingAreaEffectData area)
                {
                    spawnArea = area;
                }
                else if (obj is DamageEffectData damage && obj.name == "ZaraVoidPulseDamage")
                {
                    voidPulseDamage = damage;
                }
            }

            if (voidPulseDamage == null)
            {
                LogHelper.Error("ZaraAscensionAssetGenerator", $"No embedded ZaraVoidPulseDamage found in {ZaraBaseSkillPath} - Totem/Portable Speaker Damage Beats will deal 0 damage until DamageEffect is assigned by hand.");
                return default;
            }

            AssetRef<HitEffectData> damageEffect = new AssetRef<HitEffectData>(voidPulseDamage.Guid);

            if (spawnArea == null)
            {
                LogHelper.Error("ZaraAscensionAssetGenerator", $"No embedded SpawnAlternatingAreaEffectData found in {ZaraBaseSkillPath} - Totem's own base Damage/Heal amounts were not (re)configured.");
                return damageEffect;
            }

            spawnArea.TickInterval = FP._1;
            spawnArea.HealTargetMask = DamageTargetMask.Players;

            // SLOT ORDER IS A CONTRACT Sound Boost relies on (see
            // SpawnAlternatingAreaEffectData.SupportHealSlot/SupportBuffSlot/SupportCooldownSlot):
            // [0] the heal, [1] the ally buff bundle, [2] reserved for Sound Boost rank 2+'s cooldown
            // reduction. Slot 2 is authored empty here on purpose.
            spawnArea.HealEffects = new List<AssetRef<HitEffectData>> { scaledHealEffect, supportBuffEffect, default };

            // Baseline Support Beat trickle - 1% Max HP. Sound Boost SETS this higher per rank.
            spawnArea.HealAmount = FP.FromString("0.01");

            // The GLOBAL per-Totem healing cap, applied at every Sound Boost rank and regardless of
            // Beat frequency - what stops Double Time from letting a lower Sound Boost rank out-heal a
            // higher one. Once spent, Support Beats still deliver Move Speed / Fire Rate / cooldown
            // reduction; only the HP half switches off.
            spawnArea.MaxHealFractionPerAlly = FP._0_20;

            spawnArea.DamageAmount = 10;
            spawnArea.DamageMask = DamageTargetMask.Enemies;
            spawnArea.DamageEffects = new List<AssetRef<HitEffectData>> { damageEffect };
            EditorUtility.SetDirty(spawnArea);
            AssetDatabase.SaveAssets();

            return damageEffect;
        }

        // Portable Speaker's spawn prototype - reuses ZaraSpeaker.prefab (the Totem's own real placed
        // entity) directly rather than a dedicated prefab - see PortableSpeakerPrototypePath's own
        // comment for why ZaraDeviceSpeaker.prefab is NOT this. Loaded directly from the prefab path
        // (same AssetDatabase.LoadAssetAtPath<EntityPrototype> pattern MaxAscensionAssetGenerator/
        // HeroQuickPlayToolbar already use) rather than reading a stale numeric AssetGuid off the old,
        // deleted PortableSpeaker.asset.
        private static AssetRef<EntityPrototype> ResolvePortableSpeakerPrototype()
        {
            var prototype = AssetDatabase.LoadAssetAtPath<EntityPrototype>(PortableSpeakerPrototypePath);

            if (prototype == null)
            {
                LogHelper.Error("ZaraAscensionAssetGenerator", $"No EntityPrototype at {PortableSpeakerPrototypePath} - Portable Speaker won't spawn anything until Prototype is assigned by hand.");
                return default;
            }

            return new AssetRef<EntityPrototype>(prototype.Guid);
        }

        // Looks up an already-authored, shared HitEffectData instance under Resources/HitEffects
        // (BurnEffectData.asset, RiftMarkEffectData.asset, etc. - all zero-config, reading their own
        // magnitudes from RuntimeConfig.EffectConfig) rather than creating a bespoke copy.
        private static AssetRef<HitEffectData> LoadHitEffect(string name)
        {
            var asset = AssetDatabase.LoadAssetAtPath<HitEffectData>($"{HitEffectsFolderPath}/{name}.asset");

            if (asset == null)
            {
                LogHelper.Error("ZaraAscensionAssetGenerator", $"No HitEffectData asset at {HitEffectsFolderPath}/{name}.asset - Bass Drop's Stun is missing.");
                return default;
            }

            return new AssetRef<HitEffectData>(asset.Guid);
        }

        private static T CreateOrUpdate<T>(string path, System.Action<T> configure) where T : AssetObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            bool isNew = existing == null;
            T asset = isNew ? (T)ScriptableObject.CreateInstance(typeof(T)) : existing;

            configure(asset);

            if (isNew)
            {
                AssetDatabase.CreateAsset(asset, path);
            }
            else
            {
                EditorUtility.SetDirty(asset);
            }

            return asset;
        }

        private static void WireCharacterData(FlowStatePassiveData passive, List<PassiveUpgradeData> passiveUpgrades, List<SkillActionData> dashUpgrades)
        {
            var characterData = AssetDatabase.LoadAssetAtPath<CharacterData>(CharacterDataPath);

            if (characterData == null)
            {
                LogHelper.Error("ZaraAscensionAssetGenerator", $"No CharacterData asset at {CharacterDataPath} - assets were created/updated, but nothing was wired.");
                return;
            }

            characterData.Passive = new AssetRef<PassiveData>(passive.Guid);
            characterData.PassiveUpgrades = passiveUpgrades.Select(a => new AssetRef<PassiveUpgradeData>(a.Guid)).ToList();
            characterData.DashSkillUpgrades = dashUpgrades.Select(a => new AssetRef<SkillActionData>(a.Guid)).ToList();

            EditorUtility.SetDirty(characterData);
            AssetDatabase.SaveAssets();
        }

        // Wires the 4 Hero Skill Ascensions into ZaraBaseSkill.Actions - CheckActions stays 0 either
        // way (same "the CheckActions bug" reasoning docs/brute-ascensions.md already documents for
        // Juggernaut), since these execute via SkillSlot.Upgrades once picked. Also sweeps and removes
        // any stray sub-object embedded directly in the asset file that ISN'T the main ZaraBaseSkill
        // asset (or its own embedded ProjectileData/DirectHitData/SpawnAlternatingAreaEffectData/the
        // Damage Beat's own DamageEffectData/ThrownProjectileMovementData, which this generator doesn't
        // touch beyond ConfigureTotemThrow above) - a safety net against the 8 old dead pre-refactor
        // sub-actions (Bigger Totem/Void Pulse/Haste Pulse/Rapid Pulse/Stunning Pulse/Knockback Pulse/
        // Amplified Healing/Amplified Damage) this asset used to carry embedded, plus the old
        // ZaraHealPulse (replaced by the shared ZaraScaledHealPulse top-level asset).
        private static void WireTotemActions(List<SkillActionData> actions)
        {
            var mainAsset = AssetDatabase.LoadAssetAtPath<ProjectileSkillData>(ZaraBaseSkillPath);

            if (mainAsset == null)
            {
                LogHelper.Error("ZaraAscensionAssetGenerator", $"No ProjectileSkillData asset at {ZaraBaseSkillPath} - Amplifier/Healing Chorus/Double Time/Main Stage were created, but Actions was not wired.");
                return;
            }

            mainAsset.Actions = actions.Select(a => new AssetRef<SkillActionData>(a.Guid)).ToList();
            EditorUtility.SetDirty(mainAsset);
            AssetDatabase.SaveAssets();

            var allObjects = AssetDatabase.LoadAllAssetsAtPath(ZaraBaseSkillPath);

            foreach (var obj in allObjects)
            {
                if (obj == null || obj == mainAsset)
                    continue;

                if (obj is ProjectileDataAsset || obj is DirectHitData || obj is SpawnAlternatingAreaEffectData
                    || obj is ThrownProjectileMovementData || (obj is DamageEffectData && obj.name == "ZaraVoidPulseDamage"))
                    continue;

                AssetDatabase.RemoveObjectFromAsset(obj);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void CreateFolderRecursive(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";

                if (AssetDatabase.IsValidFolder(next) == false)
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
