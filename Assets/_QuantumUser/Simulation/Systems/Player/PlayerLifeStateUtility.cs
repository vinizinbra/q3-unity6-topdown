namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    // The Alive -> Downed -> KO transitions themselves (see docs/revive.md) - separate from
    // ReviveUtility, which owns the interaction/channel half (starting/ticking/cancelling a hold).
    // EnterDowned replaces the old DamageUtility.RespawnPlayer entirely: a lethal hit on a player no
    // longer instantly heals/teleports them, it downs them in place.
    public static unsafe class PlayerLifeStateUtility
    {
        // False (a no-op) for anything without PlayerLifeState (e.g. an enemy) - safe to call from
        // any generic gate that also runs for non-player entities.
        public static bool IsIncapacitated(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<PlayerLifeState>(entity, out var lifeState) == false)
                return false;

            return lifeState->State != PlayerLifeStateKind.Alive;
        }

        // Called from DamageUtility.ApplyDamage's own player-death branch, replacing the old
        // RespawnPlayer call there. Deliberately does NOT teleport or reset position - the whole
        // point of hold-to-revive-in-place is that the player stays exactly where they fell, unlike
        // the old instant-respawn-to-spawn-point behavior.
        public static void EnterDowned(Frame f, EntityRef target)
        {
            if (f.Unsafe.TryGetPointer<PlayerLifeState>(target, out var lifeState) == false)
                return;

            ReviveConfig config = GetConfig(f);

            lifeState->State = PlayerLifeStateKind.Downed;
            lifeState->BleedOutRemaining = config != null ? config.DownedBleedOutDuration : 20;
            lifeState->ReviveProgress = FP._0;
            lifeState->ReviveHolder = EntityRef.None;

            // Downed is damage-immune - the bleed-out timer is the ONLY way a Downed player becomes
            // KO, never another hit. Reuses the exact same tag/gate DamageUtility.ApplyDamage
            // already checks first for every other Invulnerable use (Burrow, Cheat Death) - also
            // harmlessly excludes a Downed player from enemy-seeking targeting logic that already
            // skips Invulnerable (EnemyMovementUtility.TryFindNearestEnemy), though
            // TryFindNearestPlayer does NOT skip it, so enemies still path toward/attack a Downed
            // player - the hits just do nothing (see docs/revive.md's own "known simplifications").
            f.Add<Invulnerable>(target);

            // Marks this player's own entity as a valid Revive candidate for ContextInteractionSystem's
            // existing generic closest-in-radius scan - zero changes needed to that scan itself.
            f.AddOrGet<Interactable>(target, out var interactable);
            interactable->Kind = InteractableKind.Revive;
            interactable->Radius = config != null ? config.ReviveInteractionRange : 3;
            interactable->Priority = 0;

            // A player entity is never destroyed by going Downed/KO (unlike an enemy's own death),
            // so nothing else would ever clean up whatever shots this player already had in flight -
            // see ProjectileSystem.DestroyOwnedBy's own comment.
            ProjectileSystem.DestroyOwnedBy(f, target);

            f.Events.PlayerDowned(target);

            Log.Debug($"[Revive] {target} went Downed");
        }

        // Called by PlayerLifeStateSystem once a Downed player's own BleedOutRemaining runs out
        // unrevived. KO is a dead end - confirmed with the user: no teammate hold, no self-revive,
        // nothing brings a KO'd player back except Global.BreathingAreaSecured auto-reviving
        // everyone still incapacitated (ReviveAllIncapacitated below).
        public static void EnterKO(Frame f, EntityRef target)
        {
            if (f.Unsafe.TryGetPointer<PlayerLifeState>(target, out var lifeState) == false)
                return;

            lifeState->State = PlayerLifeStateKind.KO;

            // Removed (was added by EnterDowned) - KO is no longer a valid Revive candidate at all,
            // so ContextInteractionSystem's scan should never surface it. Invulnerable/untargetable
            // stay untouched - a KO'd player is still incapacitated, just with no path back of
            // their own anymore.
            f.Remove<Interactable>(target);

            f.Events.PlayerKO(target);

            Log.Debug($"[Revive] {target} bled out - now KO (no revive path until the area is secured)");
        }

        // Called by ReviveChannelSystem once a channel's own Progress reaches its Kind's configured
        // duration - shared by a teammate revive AND a self-revive, Reviver == Target for the latter.
        public static void Revive(Frame f, EntityRef target, EntityRef reviver)
        {
            if (f.Unsafe.TryGetPointer<PlayerLifeState>(target, out var lifeState) == false)
                return;

            ReviveConfig config = GetConfig(f);

            // Direct field write, not HealUtility.ApplyHeal - ApplyFlatHeal no-ops on
            // CurrentHealth <= 0 ("dead or never seeded - nothing to heal"), which a Downed/KO
            // player's Health always is. Same idiom the old RespawnPlayer already used. Shield is
            // left untouched, same as HealingShrineUtility's own heal.
            if (f.Unsafe.TryGetPointer<Health>(target, out var health) == true)
            {
                FP healthPercent = config != null ? config.ReviveHealthPercent : FP.FromString("0.40");
                health->CurrentHealth = health->MaxHealth * healthPercent;
            }

            lifeState->State = PlayerLifeStateKind.Alive;
            lifeState->BleedOutRemaining = FP._0;
            lifeState->ReviveProgress = FP._0;
            lifeState->ReviveHolder = EntityRef.None;

            f.Remove<Interactable>(target);

            // Invulnerable is left exactly as-is (already present since EnterDowned, zero add/remove
            // gap) and re-justified by a fresh timer - the same "own dedicated StatusEffects field,
            // reusing the Invulnerable tag" shape CheatDeathUtility already established.
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == true)
            {
                FP invulnDuration = config != null ? config.ReviveInvulnerabilityDuration : 2;
                status->ReviveImmunityRemaining = invulnDuration;
            }

            f.Events.PlayerRevived(target, reviver);

            Log.Debug($"[Revive] {target} revived by {reviver}");
        }

        // Called once, edge-detected the tick Global.BreathingAreaSecured flips false -> true
        // (SurvivalProgressionUtility.Tick) - confirmed with the user: once the team has genuinely
        // cleared the area for a Breathing Break, every still-Downed/KO player is fully revived
        // automatically rather than requiring a manual hold or a spent self-revive charge. Reuses
        // the same Revive() every other completion path funnels through (full heal-to-percent + a
        // fresh invulnerability window), not a bespoke "just flip State back to Alive". Reviver is
        // EntityRef.None (nobody specific did this) - HitFeedback, the only other
        // EventPlayerRevived consumer, only reads Target, so this is safe. A teammate still
        // mid-hold on one of these entities is left alone here - ReviveChannelSystem's own very
        // first validity check (target no longer incapacitated) cancels their now-moot channel
        // cleanly the next tick, same as it already does for "target died/disconnected mid-hold".
        public static void ReviveAllIncapacitated(Frame f)
        {
            var filtered = f.Filter<PlayerLifeState, PlayerLink>();
            List<EntityRef> toRevive = null;

            while (filtered.Next(out EntityRef entity, out PlayerLifeState lifeState, out PlayerLink _))
            {
                if (lifeState.State != PlayerLifeStateKind.Alive)
                {
                    toRevive ??= new List<EntityRef>();
                    toRevive.Add(entity);
                }
            }

            if (toRevive == null)
                return;

            for (int i = 0; i < toRevive.Count; i++)
            {
                Revive(f, toRevive[i], EntityRef.None);
            }

            Log.Debug($"[Revive] Breathing area secured - auto-revived {toRevive.Count} incapacitated player(s)");
        }

        public static ReviveConfig GetConfig(Frame f)
        {
            return f.RuntimeConfig.ReviveConfig.IsValid ? f.FindAsset(f.RuntimeConfig.ReviveConfig) : null;
        }
    }
}
