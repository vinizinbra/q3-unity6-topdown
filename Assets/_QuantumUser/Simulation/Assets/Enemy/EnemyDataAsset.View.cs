namespace Quantum
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    // One alternate visual for this archetype, tagged with which world faction it represents (see
    // Enemy.Faction, Enemy.qtn) - purely cosmetic, same EnemyDataAsset stats/AI regardless of which
    // skin actually renders. Not every archetype needs any of these; an empty FactionSkins list
    // just always uses the default ViewPrefab below.
    [Serializable]
    public struct EnemyFactionSkin
    {
        public EnemyFaction Faction;
        public GameObject ViewPrefab;

        // Multiplies EnemyView's resolved fit scale for this skin only (see EnemyView.SpawnSprite/
        // ResolveFitScale) - purely cosmetic, doesn't touch the collider/collision radius that
        // scale is otherwise fit to. Lets a reskin whose source art reads bigger/smaller than the
        // default ViewPrefab at the same Radius match it visually without re-authoring Radius
        // itself (which would also move the collider). <= 0 (every skin entry authored before this
        // field existed included) reads as 1 - no change - same "unset multiplier defaults safely"
        // convention EnemyDataAsset.EnemyHeightData.TraversalJumpSpeedMultiplier already uses.
        public float ScaleMultiplier;
    }

    // View-only half of EnemyDataAsset (see the partial declaration in EnemyDataAsset.cs).
    public partial class EnemyDataAsset
    {
        [Tooltip("Tint applied to EffectsManager's shared death explosion VFX when this enemy type explodes on death (Tier == Filler - see DamageUtility.ApplyDamage). Unused for other tiers, which still play the lingering die animation instead.")]
        public Color ExplosionColor = Color.white;

        [PreviewPrefab]
        [Tooltip("Default/fallback visual rig prefab (EnemyViewRig + SpriteRenderers + optional weapon) instantiated as a child on spawn (EnemyView) - swap this per enemy type to change appearance without touching the shared generic sim prototype. Used as-is when FactionSkins below is empty, and as the fallback when this entity's Faction (Enemy.qtn) has no matching entry. EnemyBlobAnimationView/EnemyArmAimView/EnemyAttackVisualsView live on the generic prototype itself, not in here - they get this rig handed to them once it's instantiated. Scale is fit automatically off EnemyViewRig.ReferenceSprite's bounds, so it's PPU-independent - no need to hand-author at a specific scale.")]
        public GameObject ViewPrefab;

        [Tooltip("Optional faction-specific alternate skins for this archetype (e.g. a Ranged enemy could be a Faction2 robot or a Faction3 hyena) - EnemyView picks the one matching this entity's Enemy.Faction, which is authored explicitly per-slot on the EnemyGroupConfig.GroupMemberEntry that spawned it, not randomized. Leave empty for archetypes that don't need faction variety - they always use ViewPrefab above.")]
        public List<EnemyFactionSkin> FactionSkins;
    }
}
