namespace Quantum
{
    using Photon.Deterministic;

    // When a bot presses Dash and Hero Skill. Replaces the old random countdowns: a skill is only
    // pressed when its slot is actually ready AND the situation fits that skill.
    //
    // Hero Skill rules are keyed off the equipped SkillData TYPE rather than a hero id - each hero's
    // base Hero Skill is its own type, so this is per-hero in practice, and an unknown future skill
    // still gets the generic fallback instead of never being cast:
    //   Brute  (JuggernautSkillData)  - 3+ enemies within knockback reach, an Elite in reach, or low
    //                                    Health with anything close (it's also his damage reduction).
    //   Max    (BerserkSkillData)     - 3+ enemies within 8, or an Elite within 8.
    //   Pixie/Zara/Kai (ProjectileSkillData) - thrown at Aim.Target: within the skill's Range and
    //                                    either 3+ enemies clustered around it or it's an Elite.
    //   Lux    (Sentry spawn)         - any enemy within 10 and none of her sentries are out.
    //   other                          - 2+ enemies within 8.
    //
    // Dash is never fired blind any more (it moves the hero 6 units with the KCC off and no ground
    // check, which is how bots used to land in water): only to escape an enemy on top of a hurt or
    // swarmed bot, or to catch up with a far leader, and only when the landing is solid ground.
    public static unsafe class BotSkillUtility
    {
        // Minimum gap between two presses of the same button, so a press SkillSystem refused isn't
        // retried every tick.
        private static readonly FP HeroSkillPressGuard = FP._1;
        private static readonly FP DashPressGuard = FP._2;

        private static readonly FP DefaultDashDistance = 6;
        private static readonly FP DashEscapeEnemyDistance = FP._2 + FP._0_50;
        private static readonly FP DashCatchUpDistance = 12;

        // Escape candidates, degrees off straight-away-from-the-threat - the first safe landing wins.
        private static readonly FP[] DashEscapeAngles = { 0, 30, -30, 60, -60, 90, -90 };

        public static void Update(Frame f, ref BotInputSystem.Filter filter, in BotPerception perception, RuntimeConfig.BotSettings settings, bool catchUpToLeader)
        {
            BotBrain* brain = filter.Brain;
            brain->HeroSkillGuardTimer -= f.DeltaTime;
            brain->DashGuardTimer -= f.DeltaTime;

            if (f.Unsafe.TryGetPointer<CharacterSkills>(filter.Entity, out var skills) == false)
                return;

            // Reviving or about to use a POI - the Hero Skill button is the interact button there,
            // and BotInputSystem drives it itself.
            if (brain->Goal == BotGoal.Revive || brain->Goal == BotGoal.Interact || IsHeroButtonClaimedByInteraction(f, filter.Entity) == true)
                return;

            if (brain->HeroSkillGuardTimer <= FP._0 && IsReady(&skills->HeroSkill) == true
                && ShouldCastHeroSkill(f, filter.Entity, &skills->HeroSkill, in perception) == true)
            {
                brain->Data.HeroSkill = true;
                brain->HeroSkillGuardTimer = HeroSkillPressGuard;
            }

            if (brain->DashGuardTimer <= FP._0 && IsReady(&skills->DashSkill) == true)
            {
                TryDash(f, ref filter, &skills->DashSkill, in perception, settings, catchUpToLeader);
            }
        }

        // Ready, not mid-activation (a press while Active cuts the activation short and recasts -
        // see SkillSystem.CanCancelAndRecast - which would e.g. end Juggernaut early).
        public static bool IsReady(SkillSlot* slot)
        {
            return slot->Skill.IsValid == true
                && slot->State == SkillState.Ready
                && (slot->CurrentStacks > 0 || slot->FreeCastPending == true);
        }

        public static bool IsHeroSkillActive<T>(Frame f, EntityRef entity) where T : SkillData
        {
            if (f.Unsafe.TryGetPointer<CharacterSkills>(entity, out var skills) == false || skills->HeroSkill.State != SkillState.Active)
                return false;

            return f.FindAsset(skills->HeroSkill.Skill) is T;
        }

        // Standing on an Available/NotNeeded POI means SkillSystem would turn a Hero Skill press into
        // an interaction - never cast there by accident.
        private static bool IsHeroButtonClaimedByInteraction(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<ContextInteraction>(entity, out var context) == true
                && (context->State == ContextInteractionState.Available || context->State == ContextInteractionState.NotNeeded);
        }

        private static bool ShouldCastHeroSkill(Frame f, EntityRef self, SkillSlot* slot, in BotPerception perception)
        {
            if (perception.HasEnemies == false)
                return false;

            SkillData skill = f.FindAsset(slot->Skill);

            switch (skill)
            {
                case JuggernautSkillData juggernaut:
                {
                    FP reach = juggernaut.AftershockRadius + FP._1;

                    return perception.CountWithin(reach) >= 3
                        || perception.EliteWithin(reach) == true
                        || (perception.HealthFraction < FP._0_50 && perception.CountWithin(3) >= 1);
                }

                case BerserkSkillData _:
                    return perception.CountWithin(8) >= 3 || perception.EliteWithin(8) == true;

                case ProjectileSkillData projectile:
                {
                    if (f.Unsafe.TryGetPointer<Aim>(self, out var aim) == false
                        || aim->Target == EntityRef.None
                        || f.Has<Enemy>(aim->Target) == false
                        || f.Unsafe.TryGetPointer<Transform3D>(aim->Target, out var targetTransform) == false)
                    {
                        return false;
                    }

                    if (BotNavigation.FlatDistance(perception.Position, targetTransform->Position) > projectile.Range)
                        return false;

                    return perception.CountNear(targetTransform->Position, 4) >= 3 || perception.IsElite(aim->Target) == true;
                }

                case InstantSkillData instant when SpawnsSentry(f, instant) == true:
                    return perception.CountWithin(10) >= 1 && OwnsSentry(f, self) == false;

                default:
                    return perception.CountWithin(8) >= 2;
            }
        }

        private static bool SpawnsSentry(Frame f, SkillData skill)
        {
            for (int i = 0; i < skill.Actions.Count; i++)
            {
                if (f.FindAsset(skill.Actions[i]) is SpawnSentrySkillAction)
                    return true;
            }

            return false;
        }

        private static bool OwnsSentry(Frame f, EntityRef self)
        {
            var sentries = f.Filter<Sentry>();

            while (sentries.Next(out EntityRef _, out Sentry sentry))
            {
                if (sentry.Owner == self)
                    return true;
            }

            return false;
        }

        // DashSkillData dashes along Input.Direction, so the chosen direction is written into this
        // tick's Data.Direction alongside the press.
        private static void TryDash(Frame f, ref BotInputSystem.Filter filter, SkillSlot* slot, in BotPerception perception, RuntimeConfig.BotSettings settings, bool catchUpToLeader)
        {
            if (filter.KCC->Data.IsGrounded == false)
                return;

            FP distance = f.FindAsset(slot->Skill) is DashSkillData dash ? dash.DashDistance : DefaultDashDistance;
            FPVector3 position = filter.Transform->Position;
            BotBrain* brain = filter.Brain;

            bool threatened = perception.NearestEnemyDistance <= DashEscapeEnemyDistance
                && perception.ThreatDirection != default
                && (perception.HealthFraction < BotInputSystem.RetreatFraction(settings) + FP._0_25 || perception.CountWithin(3) >= 3);

            if (threatened == true)
            {
                for (int i = 0; i < DashEscapeAngles.Length; i++)
                {
                    FPVector2 candidate = BotNavigation.Rotate(perception.ThreatDirection, DashEscapeAngles[i]);

                    if (BotNavigation.IsDashLandingSafe(f, position, candidate, distance) == false)
                        continue;

                    Press(brain, candidate);
                    return;
                }

                return;
            }

            // Catch-up: only straight along the heading the bot is already safely walking, and never
            // into a fight.
            if (catchUpToLeader == true && brain->Data.Direction != default && perception.CountWithin(6) == 0
                && BotNavigation.IsDashLandingSafe(f, position, brain->Data.Direction.Normalized, distance) == true)
            {
                Press(brain, brain->Data.Direction.Normalized);
            }
        }

        private static void Press(BotBrain* brain, FPVector2 direction)
        {
            brain->Data.Direction = direction;
            brain->Data.DashSkill = true;
            brain->Heading = direction;
            brain->DashGuardTimer = DashPressGuard;
        }

        public static bool ShouldCatchUp(FP leaderDistance) => leaderDistance > DashCatchUpDistance;
    }
}
