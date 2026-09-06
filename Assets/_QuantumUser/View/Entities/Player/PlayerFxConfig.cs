using UnityEngine;

namespace Quantum
{
    // Shared home for player-wide particle prefabs so views (DashFxView, JumpFxView, and future
    // additions) reference one config asset instead of each holding its own scattered
    // ParticleSystem field. Prefabs here are played through EffectsManager.PlayEffect (pooled),
    // not parented as permanent children.
    [CreateAssetMenu(fileName = "PlayerFxConfig", menuName = "Quantum/View/Player Fx Config")]
    public class PlayerFxConfig : ScriptableObject
    {
        // One entry per burst below - bundles the prefab with a per-effect rotation/scale tweak so
        // a single shared particle can be reused across bursts with different authored rest
        // orientations/sizes without needing a separate prefab per hero or per use.
        [System.Serializable]
        public class ParticleFx
        {
            public ParticleSystem Prefab;
            [Tooltip("Extra local rotation (degrees), applied on top of whatever world rotation the caller resolves (e.g. DashFxView's dash-direction alignment) - compensates for this prefab's own authored rest orientation.")]
            public Vector3 RotationOffset;
            [Tooltip("Multiplies the effect's default Vector3.one scale.")]
            public float ScaleMultiplier = 1f;
            [Tooltip("Spawn position offset in PLAYER-LOCAL space: X = player's right, Y = world up (not rotated), Z = player's forward. Resolved to world space via ResolveWorldPositionOffset using the player's current facing, so the burst can sit e.g. in front of or beside the player regardless of which way they're facing.")]
            public Vector3 PositionOffset;

            // Shared by DashFxView/JumpFxView/BlobAnimationView so each doesn't reimplement the same
            // right/up/forward composition. Y is deliberately NOT rotated by playerTransform - it's a
            // flat vertical offset, since PositionOffset's X/Z only ever need to track the player's
            // facing on the ground plane.
            public Vector3 ResolveWorldPositionOffset(Transform playerTransform)
            {
                return playerTransform.right * PositionOffset.x
                     + Vector3.up * PositionOffset.y
                     + playerTransform.forward * PositionOffset.z;
            }
        }

        [Header("Dash")]
        [Tooltip("One-shot burst played when a dash begins (see DashFxView), tinted with the hero's CharacterData.RingColor.")]
        public ParticleFx DashBurst;

        [Header("Jump")]
        [Tooltip("One-shot burst played on EventPlayerJumped/EventPlayerAutoJumpedDown (see JumpFxView).")]
        public ParticleFx JumpBurst;

        [Header("Land")]
        [Tooltip("One-shot burst played the frame the character regains ground after being airborne (see GroundedFxView) - same justLanded moment BlobAnimationView's landSound already reacts to.")]
        public ParticleFx LandBurst;
        [Tooltip("Downward impact speed below which a landing is skipped - stops constant micro-landings from walking over uneven geometry from firing a burst every few steps. Matches BlobAnimationView.landSoundMinImpactSpeed's default.")]
        public float LandMinImpactSpeed = 2f;

        // The fields below mirror HitFeedback's own local flash-color fields 1:1 - when a
        // HitFeedback has this config assigned, it copies these over its locals once in Awake
        // (see HitFeedback.ApplyFxConfig), so every other hero reads the same values instead of
        // each prefab carrying its own independently-authored (and, before this config existed,
        // already-drifted) copy. Enemies/objects that also use HitFeedback leave this config
        // unassigned and keep their own local values untouched.
        [Header("Hit Flash")]
        [Tooltip("Used for a Neutral-element hit (plain weapon/skill damage) - everything without a more specific color below.")]
        public Color FlashColor = Color.white;
        [Tooltip("Used instead of FlashColor when the hit's ElementType is Fire - i.e. a Burn tick.")]
        public Color BurnFlashColor = new Color(1f, 0.45f, 0.1f);
        [Tooltip("Flash colour when the accessory is put back on - recovered off the ground, or repaired/replaced at the Merchant.")]
        public Color RecoverFlashColor = Color.cyan;
        [Tooltip("Flash colour when the Accessory Guard eats a hit entirely. BLUE, not a damage colour: nothing was actually lost.")]
        public Color BlockFlashColor = new Color(0.25f, 0.6f, 1f);
        [Tooltip("Flash colour when a Free Hit Guard is spent. CYAN: same cool family as the accessory block, distinct hue.")]
        public Color FreeHitGuardFlashColor = new Color(0.4f, 0.95f, 1f);
        [Tooltip("Used instead of FlashColor/BurnFlashColor when the hit is FrontalReduced - takes priority over the element color either way.")]
        public Color FrontalReducedFlashColor = Color.gray;
        [Tooltip("Tween-back target every flash eases toward once its duration ends.")]
        public Color RestColor = Color.clear;
        [Tooltip("Duration of a hit flash (heal/shield/rift-mark/pickup each have their own duration below).")]
        public float FlashDuration = 0.2f;

        [Header("Elemental First-Hit Rest Tint")]
        [Tooltip("While StatusEffects.FirstElementApplied (the FIRST of Fire/Ice/Rock/Lightning to ever land its baseline status on this entity) is Fire AND Burn is still actually active, RestColor is live-overridden to this (see HitFeedback.UpdateElementalRestTint) - reverts to the original RestColor the instant Burn expires. Rock has no entry (not requested yet) - HitFeedback.ResolveElementRestTint returns null for it and simply leaves RestColor untouched.")]
        public Color FireRestTint = new Color(1f, 0.55f, 0.2f);
        [Tooltip("Same as FireRestTint, for Element == Ice.")]
        public Color IceRestTint = new Color(0.4f, 0.9f, 1f);
        [Tooltip("Same as FireRestTint, for Element == Lightning.")]
        public Color LightningRestTint = new Color(1f, 0.9f, 0.3f);

        [Header("Heal / Shield Flash")]
        [Tooltip("Used on EventEntityHealed.")]
        public Color HealFlashColor = new Color(0.4f, 1f, 0.4f);
        [Tooltip("Used on EventEntityShielded.")]
        public Color ShieldFlashColor = new Color(0.4f, 0.75f, 1f);

        [Header("Death")]
        [Tooltip("Applied the instant the entity dies, and held for the rest of the corpse's lingering duration.")]
        public Color DeathColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);

        [Header("Rift Mark")]
        [Tooltip("Brief, subtle tint played only when RiftMarkStacks actually increases. Hot-pink #FD3971 per this project's Rift Mark color rule.")]
        public Color RiftMarkApplicationFlashColor = new Color32(0xFD, 0x39, 0x71, 0xFF);
        public float RiftMarkApplicationFlashDuration = 0.08f;

        [Header("Pickup Flash")]
        [Tooltip("Used on FlashPickup for CurrencyType.Experience. Alpha is the SpriteColor shader's blend strength.")]
        public Color ExpPickupFlashColor = new Color(0.3f, 0.55f, 1f, 0.35f);
        [Tooltip("Used on FlashPickup for CurrencyType.Coin.")]
        public Color CoinPickupFlashColor = new Color(1f, 0.84f, 0.2f, 0.35f);
        [Tooltip("Used on FlashPickup for CurrencyType.RiftShard.")]
        public Color RiftShardPickupFlashColor = new Color(1f, 0.35f, 0.75f, 0.35f);
        [Tooltip("Used on FlashPickup for CurrencyType.Scrap - Lux's own pickup.")]
        public Color ScrapPickupFlashColor = new Color(0.95f, 0.6f, 0.25f, 0.35f);
        public float PickupFlashDuration = 0.35f;
    }
}
