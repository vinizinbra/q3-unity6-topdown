namespace Quantum
{
    using System;
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Instant area effect - unlike LeapDeliveryData, no movement first; hits everyone within
    // DamageRange the instant the windup ends. Always instant (Begin() returns true).
    //
    // action.Origin and ConeShaped are independent knobs, not tied together - Origin (see
    // EnemyActionData.Origin's own comment - lives there, not here, so the paired Circle/Cone
    // telegraph can read the exact same choice) picks WHERE the effect is centered. ConeShaped
    // picks WHETHER an angular restriction applies on top of that origin (a wedge instead of a
    // full circle/blast) - pairs with a Cone telegraph, which is always anchored at the enemy (see
    // EnemyAttackVisualsView.ComputeTelegraphPose), so Origin = Self is the natural pairing for
    // ConeShaped = true, but neither setting requires the other. The cone always points from
    // wherever Origin resolved to toward SkillTargetPosition - if Origin is already
    // TargetAnchor (making that direction zero-length), it falls back to world-forward rather
    // than producing a degenerate wedge; that combination doesn't have a sensible pointing
    // direction to begin with, so treat it as unsupported/only use ConeShaped with Origin = Self.
    public unsafe class GroundAreaDeliveryData : EnemyDeliveryData
    {
        // False (default): a full circle. True: restricted to a forward-facing wedge instead -
        // pairs with a Cone telegraph.
        public bool ConeShaped = false;

        // Cone mode only - total angular width of the wedge, centered on the direction toward the
        // locked anchor (e.g. 90 hits anyone within 45 degrees either side of dead-center).
        public FP ConeAngleDegrees = 90;

        // For a creeper-style suicide exploder: kills the enemy itself once it's done applying
        // damage to whoever it hit, via the exact same overkill-DamageUtility.ApplyDamage pattern
        // EnemySystem.CheckFallDeath already uses for its own instant-death case (owner =
        // EntityRef.None so it doesn't get misattributed as a player kill, bypassOutgoingResolution
        // = true since there's no real attacker to resolve modifiers for). Goes through the real
        // death pipeline (EntityDied event, Dead phase + DeathLingerTime, or immediate destroy for
        // a Filler-tier enemy) rather than a shortcut - see DamageUtility.ApplyDamage. EnemySystem.
        // EnterRecovering already guards against clobbering the Dead phase this sets.
        public bool SelfDestructs = false;

        // SelfDestructs only - killed by damage before its own windup could trigger it, the enemy
        // still goes off (see TryDetonateOnKilled, called from DamageUtility.ResolveDeath) instead
        // of just dying quietly. Same Damage/DamageRange/Effects as its own attack, but that blast
        // catches whoever's in range on EITHER side (KilledDetonationTargets) - shooting a
        // Suicider next to its pack is the point. Kept separate from the self-triggered blast in
        // Begin() below, which still only ever hits players.
        public bool DetonateWhenKilled = true;
        public DamageTargetMask KilledDetonationTargets = DamageTargetMask.Both;

        // 0 (the default) keeps every existing asset's exact prior behavior - FindPlayersInRadius
        // below mimics a volumetric 3D sphere/distance check (see PlayerQueryUtility.Scan), so a
        // player standing on an elevated ledge/platform above this slam (or down in a pit near it)
        // can still get caught. Above zero, a hit additionally requires the ACTUAL FLOOR under the
        // target (a real ground raycast, not raw Transform3D.Y) to be within this many units of the
        // floor under the slam's own origin - see EnemyMovementUtility.IsWithinFlatGroundArea/
        // ResolveGroundY.
        public FP MaxHeightDifference = FP._0;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            FPVector3 targetAnchor = filter.Enemy->SkillTargetPosition;
            FPVector3 origin = action.Origin == EnemyActionOrigin.Self ? filter.Transform3D->Position : targetAnchor;

            Span<EntityRef> hits = stackalloc EntityRef[PlayerQueryUtility.MaxPlayerLayerCandidates];

            int hitsCount = EnemyMovementUtility.FindPlayersInRadius(f, origin, action.DamageRange, hits);

            // Resolved once per slam, not per candidate - see EnemyMovementUtility.ResolveGroundY.
            FP originGroundY = MaxHeightDifference > FP._0 ? EnemyMovementUtility.ResolveGroundY(f, origin) : default;
            // Same Dot-vs-Cos(half-arc) idiom DamageUtility's own frontal-arc check already uses
            // (see DamageUtility.cs) - cheaper than an Acos per candidate, and keeps this consistent
            // with that established pattern instead of introducing a different one.
            FPVector3 coneDirection = default;
            FP coneArcCos = default;

            if (ConeShaped == true)
            {
                // Flattened onto the XZ ground plane (Y zeroed) - this is a top-down wedge, and the
                // rest of the system (FlatSqrDistance, the Cone telegraph in
                // EnemyAttackVisualsView.ComputeTelegraphPose) already works flat. Doing the
                // angle-check in full 3D let any Y gap between origin (the enemy's elevated capsule
                // pivot) and the players/target anchor tilt both vectors, so the actual hit wedge no
                // longer matched the telegraph on screen.
                FPVector3 delta = targetAnchor - origin;
                delta.Y = FP._0;
                coneDirection = delta.SqrMagnitude > FP._0 ? delta.Normalized : FPVector3.Forward;
                coneArcCos = FPMath.Cos(ConeAngleDegrees * FP._0_50 * FP.Deg2Rad);
            }

            for (int i = 0; i < hitsCount; i++)
            {
                EntityRef hitEntity = hits[i];

                if (f.Unsafe.TryGetPointer<Transform3D>(hitEntity, out var hitTransform) == false)
                    continue;

                FPVector3 hitPosition = hitTransform->Position;

                if (MaxHeightDifference > FP._0 &&
                    EnemyMovementUtility.IsWithinFlatGroundArea(f, origin, originGroundY, hitPosition, action.DamageRange, MaxHeightDifference) == false)
                    continue;

                if (ConeShaped == true)
                {
                    FPVector3 toHit = hitPosition - origin;
                    toHit.Y = FP._0; // flat wedge on the XZ plane - see coneDirection above

                    if (toHit.SqrMagnitude <= FP._0)
                        continue; // standing exactly on the apex - no meaningful direction to angle-check

                    if (FPVector3.Dot(coneDirection, toHit.Normalized) < coneArcCos)
                        continue; // outside the wedge
                }

                // Radially outward from the origin/apex, not toward wherever each player happens to
                // be facing/moving - a ground slam (or cone sweep) pushes everyone away from it.
                HitEffectContext context = new HitEffectContext
                {
                    Owner = filter.Entity,
                    Target = hitEntity,
                    Position = hitPosition,
                    PushDirection = hitPosition - origin,
                    Damage = action.Damage,
                    Source = DamageSource.None,
                    Element = ElementType.Neutral,
                };

                // multiTarget: once-per-blast effects (a lingering hazard) are applied once at the
                // origin below, not once per player caught at each player's feet.
                HitEffectUtility.ApplyToTarget(f, action.Effects, ref context, multiTarget: true);
            }

            // Once per slam/blast at its origin, even when it caught nobody - e.g. the Fuel Runner's
            // sticky oil splash. No-op for an Effects list without AppliesOncePerBlast entries.
            HitEffectUtility.ApplyBlastLevelEffects(f, action.Effects, origin, filter.Entity, action.Damage,
                DamageSource.None, ElementType.Neutral, SelfDestructs, action.DamageRange, 0);

            if (SelfDestructs == true && f.Unsafe.TryGetPointer<Health>(filter.Entity, out var health) == true)
            {
                // Filler/Normal tier enemies get destroyed the same tick by ApplyDamage's own
                // death branch, with Phase never touched - EnemyAttackVisualsView's usual
                // Phase-edge watching never gets a chance to observe this attack's Begin and its
                // BeginStep particle (the explosion itself) silently never plays. Raised BEFORE
                // ApplyDamage, while filter.Transform3D/Aim are still guaranteed valid, and gated
                // on the exact same tier check ApplyDamage uses so a Heavy+ tier self-destruct
                // (which DOES get a lingering Phase = Dead) doesn't also raise this and double-play
                // the visual through both paths.
                if (data.Tier == EnemyTier.Filler || data.Tier == EnemyTier.Normal)
                {
                    AssetRef<EnemyActionData> actionRef = EnemyDecisionUtility.ResolveActionRef(data, filter.Enemy->CurrentActionSlot);
                    f.Events.EnemySelfDestructBeginVisual(filter.Entity, filter.Transform3D->Position, filter.Aim->Angle, actionRef);
                }

                DamageUtility.ApplyDamage(f, filter.Entity, health->MaxHealth * 1000, EntityRef.None, bypassOutgoingResolution: true);
            }

            return true;
        }

        // Called from DamageUtility.ResolveDeath for every dying enemy, before it's destroyed (the
        // Transform3D is still valid). Finds the first action slot whose delivery is a
        // SelfDestructs + DetonateWhenKilled GroundArea and detonates it at the corpse - no new
        // authoring on the enemy asset, the Suicider's own attack IS the death blast.
        //
        // owner == EntityRef.None is the self-destruct's own overkill ApplyDamage in Begin() above
        // (it already dealt its blast), and also a fall/void death (CheckFallDeath) - neither
        // should detonate a second time. Chains are safe: a Suicider caught by another's blast
        // dies and goes off in turn, and ApplyDamage's own CurrentHealth <= 0 guard stops it
        // re-killing a corpse that's already resolved.
        public static void TryDetonateOnKilled(Frame f, EntityRef enemy, EntityRef killer, EnemyDataAsset data)
        {
            if (killer == EntityRef.None)
                return;

            int slotCount = 1 + (data.Actions.SkillActions != null ? data.Actions.SkillActions.Count : 0);

            for (int slot = 0; slot < slotCount; slot++)
            {
                EnemyActionData action = EnemyDecisionUtility.ResolveAction(f, data, slot);

                if (action == null || action.Delivery.IsValid == false)
                    continue;

                if (f.FindAsset(action.Delivery) is not GroundAreaDeliveryData delivery ||
                    delivery.SelfDestructs == false || delivery.DetonateWhenKilled == false)
                    continue;

                delivery.DetonateAtCorpse(f, enemy, action, EnemyDecisionUtility.ResolveActionRef(data, slot));
                return;
            }
        }

        private void DetonateAtCorpse(Frame f, EntityRef enemy, EnemyActionData action, AssetRef<EnemyActionData> actionRef)
        {
            if (f.Unsafe.TryGetPointer<Transform3D>(enemy, out var transform) == false)
                return;

            FPVector3 origin = transform->Position;

            // Player mask WITHOUT the dashing layer, same as Begin()'s FindPlayersInRadius - a dash
            // still dodges this. Enemies layer added for the friendly-fire half.
            int layerMask = 0;
            if (KilledDetonationTargets != DamageTargetMask.Enemies)
                layerMask |= EnemyMovementUtility.GetPlayerLayerMask(f);
            if (KilledDetonationTargets != DamageTargetMask.Players)
                layerMask |= EnemyMovementUtility.GetEnemyLayerMask(f);

            var hits = f.Physics3D.OverlapShape(origin, FPQuaternion.Identity, Shape3D.CreateSphere(action.DamageRange), layerMask, QueryOptions.HitAll);

            FP originGroundY = MaxHeightDifference > FP._0 ? EnemyMovementUtility.ResolveGroundY(f, origin) : default;

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef hitEntity = hits[i].Entity;

                if (hitEntity == enemy || hitEntity == EntityRef.None)
                    continue;

                if (f.Has<PlayerLink>(hitEntity) == false && f.Has<Enemy>(hitEntity) == false)
                    continue;

                if (f.Unsafe.TryGetPointer<Transform3D>(hitEntity, out var hitTransform) == false)
                    continue;

                FPVector3 hitPosition = hitTransform->Position;

                if (MaxHeightDifference > FP._0 &&
                    EnemyMovementUtility.IsWithinFlatGroundArea(f, origin, originGroundY, hitPosition, action.DamageRange, MaxHeightDifference) == false)
                    continue;

                // Owner stays the dying enemy (not the killer) so the enemy damage scaling applies
                // exactly as it would to its own self-triggered blast, and so a player never ends up
                // "owning" damage dealt to another player.
                HitEffectContext context = new HitEffectContext
                {
                    Owner = enemy,
                    Target = hitEntity,
                    Position = hitPosition,
                    PushDirection = hitPosition - origin,
                    Damage = action.Damage,
                    Source = DamageSource.None,
                    Element = ElementType.Neutral,
                };

                HitEffectUtility.ApplyToTarget(f, action.Effects, ref context, multiTarget: true);
            }

            // Same once-per-blast hazard as the self-triggered blast in Begin() - dying to damage
            // still leaves it behind.
            HitEffectUtility.ApplyBlastLevelEffects(f, action.Effects, origin, enemy, action.Damage,
                DamageSource.None, ElementType.Neutral, true, action.DamageRange, 0);

            // Always raised here, whatever the tier - a damage death never passes through Begin()'s
            // Phase edge, so EnemyAttackVisualsView never plays BeginStep (the explosion) on its own.
            FP facing = f.Unsafe.TryGetPointer<Aim>(enemy, out var aim) ? aim->Angle : FP._0;
            f.Events.EnemySelfDestructBeginVisual(enemy, origin, facing, actionRef);
        }
    }
}
