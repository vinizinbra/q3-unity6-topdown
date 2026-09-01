namespace Quantum
{
    using System;
    using Photon.Deterministic;

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
                FPVector3 delta = targetAnchor - origin;
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

                HitEffectUtility.ApplyToTarget(f, action.Effects, ref context);
            }

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
    }
}
