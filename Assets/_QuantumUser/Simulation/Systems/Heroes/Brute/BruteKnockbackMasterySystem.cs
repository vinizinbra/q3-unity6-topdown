namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Brute's Knockback Mastery trait that reacts to a signal rather than a per-hit hook (see
    // DamageUtility.ResolveOutgoingDamage for Crushing Blow, StatusEffectUtility.ApplyStun for
    // Lasting Impact, and CharacterStats.KnockbackMultiplier for Overwhelming Force instead) - Ground
    // Pound, a knockback pulse the instant Brute lands from a genuine fall (see PlayerMovement.qtn's
    // OnPlayerLanded, fired from AutoJumpSystem's own generic landing edge - gated here on
    // MinFallDistance so a flat auto-hop/mantle or a manual ground-level jump doesn't trigger it).
    //
    // Applies its own small knockback loop instead of HitEffectUtility.ApplyShockwave - that helper
    // always fires the generic ShockwaveReleased event too, which would stack a second, redundant
    // VFX alongside GroundPoundTriggered below (ShockwaveReleased.Effect is a "skip, a dedicated view
    // already handles this" flag, not a way to resolve GroundPoundPassiveUpgradeData's own
    // BlastEffectPrefab - wrong tool for an asset-authored prefab). Same "resolve own asset, fire own
    // event, don't go through the generic shared blast" shape VortexSystem.TryExplodeOnDestroy uses.
    [Preserve]
    public unsafe class BruteKnockbackMasterySystem : SystemMainThread, ISignalOnPlayerLanded
    {
        public override void Update(Frame f)
        {
        }

        public void OnPlayerLanded(Frame f, EntityRef entity, FP fallDistance)
        {
            if (f.Unsafe.TryGetPointer<GroundPoundUpgrade>(entity, out var groundPound) == false)
                return;

            if (fallDistance < groundPound->MinFallDistance)
                return;

            if (f.Unsafe.TryGetPointer<Transform3D>(entity, out var transform) == false)
                return;

            FPVector3 position = transform->Position;
            FP radius = groundPound->Radius;

            Shape3D sphere = Shape3D.CreateSphere(radius);
            var hits = f.Physics3D.OverlapShape(position, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef target = hits[i].Entity;

                if (target == entity || f.Has<Enemy>(target) == false)
                    continue;

                if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                    continue;

                DamageUtility.ApplyKnockback(f, target, targetTransform->Position - position, groundPound->Force, groundPound->UpwardForce, entity);
            }

            f.Events.GroundPoundTriggered(entity, position, radius, groundPound->Source);
        }
    }
}
