namespace QuantumUser.Editor.BalanceSimulator
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using Photon.Deterministic;
    using Quantum;
    using static BalanceSimAssets;

    // Expected damage per activation of a hero's base skill, read off the authored skill assets.
    // Known skill shapes get a dedicated model; everything else is summed generically by walking the
    // Activated actions/effects for Damage-like fields (see docs/balance-simulator.md).
    public static class BalanceSimSkillModel
    {
        public static SkillEstimate Evaluate(CharacterData hero, BalanceSimAssets assets, BalanceSimScenario scenario)
        {
            var est = new SkillEstimate();
            SkillData skill = assets.Resolve(hero.HeroSkill);

            if (skill == null)
                return est;

            est.Cooldown = Math.Max(0.1, D(skill.Cooldown));
            est.ActiveDuration = D(skill.GetActiveDuration());
            double area = Math.Max(1, scenario.AreaTargets);

            switch (skill)
            {
                case BerserkSkillData berserk:
                {
                    double duration = D(berserk.Duration);
                    double uptime = Math.Clamp(duration / Math.Max(duration, est.Cooldown), 0, 1);
                    est.BuffUptime = uptime;
                    est.WeaponFireRateBonus = D(berserk.FireRateBonus) * uptime;
                    est.WeaponReloadBonus = D(berserk.ReloadSpeedBonus) * uptime;
                    est.Model = $"Berserk buff (+{D(berserk.FireRateBonus):P0} fire rate, {uptime:P0} uptime)";
                    AddGenericActions(skill, assets, scenario, est, area, 1);
                    return est;
                }
                case JuggernautSkillData jug:
                {
                    double discharges = area * D(jug.Duration) / Math.Max(0.5, D(jug.DischargeCooldownPerEnemy)) * scenario.ChannelContactUptime;
                    est.Basis = D(jug.Damage);
                    est.DamagePerActivation = D(jug.Damage) * discharges;
                    est.UsesArea = true;
                    est.Model = $"Juggernaut ({discharges:0.#} discharge hits x {D(jug.Damage)} @ {scenario.ChannelContactUptime:P0} contact uptime)";
                    AddGenericActions(skill, assets, scenario, est, area, discharges);
                    return est;
                }
                case ProjectileSkillData projectile:
                {
                    ProjectileDataAsset projectileData = assets.Resolve(projectile.ProjectileData);
                    ProjectileHitData hit = projectileData != null ? assets.Resolve(projectileData.Hit) : null;
                    bool isArea = hit is AreaHitData;
                    double effectMultiplier = 0;
                    var effects = new List<HitEffectData>();

                    if (hit != null)
                    {
                        foreach (AssetRef<HitEffectData> effectRef in hit.Effects)
                        {
                            HitEffectData effect = assets.Resolve(effectRef);
                            if (effect == null)
                                continue;

                            effects.Add(effect);
                            if (effect is DamageEffectData damageEffect)
                                effectMultiplier += D(damageEffect.DamageMultiplier);
                        }
                    }

                    if (effectMultiplier <= 0)
                        effectMultiplier = 1;

                    double targets = isArea ? area : 1;
                    est.Basis = D(projectile.Damage);
                    est.ImpactDamage = D(projectile.Damage) * effectMultiplier * targets;
                    est.DamagePerActivation = est.ImpactDamage;
                    est.UsesArea = isArea;
                    string model = isArea ? "Projectile+Area" : "Projectile";

                    foreach (HitEffectData effect in effects)
                    {
                        switch (effect)
                        {
                            case SpawnAlternatingAreaEffectData alt:
                            {
                                double duration = D(alt.Duration);
                                double ticks = duration / Math.Max(0.1, D(alt.TickInterval));
                                est.TickDamage = D(alt.DamageAmount) * ticks * area;
                                est.TickInterval = D(alt.TickInterval);
                                est.TickWindow = duration;
                                est.DamagePerActivation += est.TickDamage;
                                est.ActiveDuration = Math.Max(est.ActiveDuration, duration);
                                model += "+AlternatingArea";
                                break;
                            }
                            case SpawnEntityEffectData spawn:
                                est.ActiveDuration = Math.Max(est.ActiveDuration, D(spawn.Duration));
                                model += spawn is SpawnVortexEffectData ? "+Vortex" : "+Entity";
                                break;
                        }
                    }

                    est.Model = model;
                    AddGenericActions(skill, assets, scenario, est, area, 1);
                    return est;
                }
                default:
                    est.Model = skill.GetType().Name + " (generic)";
                    AddGenericActions(skill, assets, scenario, est, area, 1);
                    return est;
            }
        }

        // Baseline actions are the Activated ones (non-Activated actions are Ascension picks - see
        // LevelUpUtility.AddHeroSkillUpgradeCandidates). Each contributes: any mounted weapon's DPS
        // over its Duration, plus any Damage/DamageAmount field x ticks x area targets.
        private static void AddGenericActions(SkillData skill, BalanceSimAssets assets, BalanceSimScenario scenario, SkillEstimate est, double area, double perActivationHits)
        {
            var extras = new List<string>();

            foreach (AssetRef<SkillActionData> actionRef in skill.Actions)
            {
                SkillActionData action = assets.Resolve(actionRef);

                if (action == null || action.Activated == false)
                    continue;

                double actionDuration = GetFP(action, "Duration") ?? est.ActiveDuration;

                foreach (WeaponDataAsset weapon in GetWeapons(action, assets))
                {
                    double duration = actionDuration > 0 ? actionDuration : est.ActiveDuration;
                    if (duration <= 0)
                        continue;

                    double mounted = SimWeapon.BaseDps(weapon) * duration;
                    est.DamagePerActivation += mounted;
                    est.MountedWeaponDamage += mounted;
                    est.ActiveDuration = Math.Max(est.ActiveDuration, duration);
                    extras.Add($"{weapon.name} x{duration:0.#}s");
                }

                double skillDamageBasis = GetFP(action, "SkillDamage") ?? 0;
                if (skillDamageBasis > 0 && est.Basis <= 0)
                    est.Basis = skillDamageBasis;

                double damage = GetFP(action, "Damage") ?? GetFP(action, "DamageAmount") ?? 0;

                if (damage <= 0)
                    continue;

                double interval = GetFP(action, "TickInterval") ?? (D(action.Interval) > 0 ? D(action.Interval) : 0);
                double window = Math.Max(actionDuration, est.ActiveDuration);
                double ticks = interval > 0 && window > 0 ? Math.Max(1, window / interval) : 1;
                bool perDischarge = action.GetType().Name.Contains("Discharge");
                double hits = perDischarge ? perActivationHits : ticks * area;

                est.DamagePerActivation += damage * hits;
                est.UsesArea = true;
                extras.Add($"{action.name} {damage}x{hits:0.#}");
            }

            if (extras.Count > 0)
                est.Model += " [" + string.Join(", ", extras) + "]";
        }

        // Cumulative damage an Ascension line adds per activation at `rank` (1-based), read off its
        // rank-indexed fields against the skill's basis. Unknown actions fall back to a generic
        // DamagePercent rule; anything still unquantifiable returns default (caller applies the
        // flat SkillUpgradeDpsValue knob instead). Matched by type name so a renamed/removed
        // Ascension class degrades to the generic rule instead of breaking the tool.
        public static UpgradeValue EvaluateUpgrade(SkillActionData action, int rank, SkillEstimate est, BalanceSimScenario scenario, BalanceSimAssets assets)
        {
            var value = new UpgradeValue();
            if (action == null || rank < 1)
                return value;

            double area = Math.Max(1, scenario.AreaTargets);
            double basis = est.Basis;
            double window = est.ActiveDuration;
            double F(string name) => Field(action, name, rank);

            switch (action.GetType().Name)
            {
                // -- Kai
                case "CompressionSkillAction":
                {
                    double pulses = window / Math.Max(0.1, F("PulseTickInterval"));
                    double crowd = 1 + F("CrowdPerEnemyBonus") * Math.Min(area - 1, F("CrowdMaxCount"));
                    value.SkillDamage = basis * F("PulseDamagePercent") * pulses * area * crowd;
                    double nth = F("ImplosionEveryNthPulse");
                    if (nth > 0)
                        value.SkillDamage += basis * F("ImplosionDamagePercent") * (pulses / nth) * area * crowd;
                    break;
                }
                case "VoidShardsSkillAction":
                {
                    double ticks = window / Math.Max(0.1, F("TickInterval"));
                    double targets = Math.Max(1, F("ShardCount")) * Math.Min(area, Math.Max(1, F("PierceCount")));
                    value.SkillDamage = basis * F("DamagePercent") * ticks * targets;
                    break;
                }
                case "VortexCollapseSkillAction":
                    value.SkillDamage = basis * F("DamagePercent") * area;
                    break;

                // -- Pixie
                case "ClusterBombSkillAction":
                    value.SkillDamage = basis * F("DamagePercent") * Math.Max(1, F("Count")) * area;
                    break;
                case "DirectHitSkillAction":
                    value.SkillDamage = est.ImpactDamage * F("DamageMultiplierBonus");
                    break;
                case "BirthdayCakeSkillAction":
                    value.SkillDamage = est.ImpactDamage * F("BonusDamageMultiplier");
                    break;

                // -- Brute
                case "BoneBreakerSkillAction":
                    value.SkillDamage = est.DamagePerActivation * (F("DamageMultiplierBonus") + F("TierDamageBonus") * 0.3);
                    break;
                case "AftershockSkillAction":
                {
                    double stacks = Math.Min(area, Math.Max(1, F("MaxStacks")));
                    value.SkillDamage = basis * area * (1 + F("StackDamagePercent") * stacks);
                    double threshold = F("EarthquakeStackThreshold");
                    if (threshold > 0 && stacks >= threshold)
                        value.SkillDamage += basis * F("EarthquakeDamagePercent") * area;
                    break;
                }
                case "ConcussiveImpactSkillAction":
                    value.SkillDamage = basis * (F("LandingDamagePercent") + F("ShockwaveDamagePercent")) * area;
                    break;

                // -- Max
                case "FullThrottleSkillAction":
                    value.WeaponBonus = F("WeaponDamageBonus") * est.BuffUptime;
                    break;

                // -- Zara
                case "AmplifierSkillAction":
                    value.SkillDamage = est.DamagePerActivation * F("DamageBonus");
                    break;
                case "DoubleTimeSkillAction":
                    if (est.TickInterval > 0)
                        value.SkillDamage = est.TickDamage * (est.TickInterval / Math.Max(0.1, F("BeatInterval")) - 1);
                    break;
                case "MainStageSkillAction":
                    if (est.TickWindow > 0)
                        value.SkillDamage = est.TickDamage * F("DurationBonus") / est.TickWindow;
                    break;

                // -- Lux
                case "SentryWeaponSystemsSkillAction":
                {
                    int slot = 0;
                    foreach (WeaponDataAsset weapon in GetWeapons(action, assets))
                    {
                        if (slot++ >= rank)
                            break;
                        value.SkillDamage += SimWeapon.BaseDps(weapon) * window;
                    }
                    break;
                }
                case "SentryOverclockSkillAction":
                    value.SkillDamage = est.MountedWeaponDamage * (F("FireRateMultiplier") - 1);
                    if (window > 0)
                        value.SkillDamage += est.MountedWeaponDamage * F("DurationBonus") / window;
                    break;
                case "SentryOverloadCoreSkillAction":
                    value.SkillDamage = basis * F("DamagePercent") * area;
                    break;

                default:
                {
                    double percent = F("DamagePercent");
                    if (percent > 0)
                    {
                        double interval = F("TickInterval");
                        double ticks = interval > 0 && window > 0 ? window / interval : 1;
                        value.SkillDamage = basis * percent * ticks * area;
                    }
                    break;
                }
            }

            value.SkillDamage = Math.Max(0, value.SkillDamage);
            value.WeaponBonus = Math.Max(0, value.WeaponBonus);
            return value;
        }

        // Reads FP / FP[] / byte / int / byte[] / int[] fields; arrays are rank-indexed (rank-1, clamped).
        private static double Field(object target, string name, int rank)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field == null)
                return 0;

            object raw = field.GetValue(target);
            switch (raw)
            {
                case FP fp: return fp.AsDouble;
                case int i: return i;
                case byte b: return b;
                case FP[] fps: return fps.Length == 0 ? 0 : fps[Math.Clamp(rank - 1, 0, fps.Length - 1)].AsDouble;
                case int[] ints: return ints.Length == 0 ? 0 : ints[Math.Clamp(rank - 1, 0, ints.Length - 1)];
                case byte[] bytes: return bytes.Length == 0 ? 0 : bytes[Math.Clamp(rank - 1, 0, bytes.Length - 1)];
                default: return 0;
            }
        }

        private static IEnumerable<WeaponDataAsset> GetWeapons(SkillActionData action, BalanceSimAssets assets)
        {
            foreach (FieldInfo field in action.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.FieldType != typeof(AssetRef<WeaponDataAsset>))
                    continue;

                WeaponDataAsset weapon = assets.Resolve((AssetRef<WeaponDataAsset>)field.GetValue(action));
                if (weapon != null)
                    yield return weapon;
            }
        }

        private static double? GetFP(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);

            if (field == null || field.FieldType != typeof(FP))
                return null;

            return ((FP)field.GetValue(target)).AsDouble;
        }
    }
}
