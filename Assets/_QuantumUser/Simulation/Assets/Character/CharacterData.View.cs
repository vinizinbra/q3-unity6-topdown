namespace Quantum
{
    using System;
    using UnityEngine;

    // Per-hero PRESENTATION for that hero's Signature Accessory (see docs/accessory-guard.md) -
    // Max's cap, Zara's headset, Brute's mask. Deliberately the only place in the whole feature
    // that knows an accessory is a specific THING: no simulation system, and no View system, ever
    // branches on which hero this is - they resolve this struct generically through the owner's
    // CharacterStats.CharacterData and render whatever it holds.
    //
    // Only covers the WORLD PICKUP half of the presentation. The EQUIPPED half deliberately does
    // NOT live here: a worn accessory is a swap between two hand-placed GameObjects on the hero's
    // own view prefab (AccessoryView's equippedVisual/unequippedVisual), because these rigs are
    // sprite-based - "wearing the cap" and "not wearing the cap" are usually two different authored
    // head sprites, not one prop parented onto a bare head. Same split BlobAnimationView already
    // uses: per-hero rig references belong on the hero's prefab, per-hero ASSETS belong here.
    [Serializable]
    public struct HeroAccessoryPresentation
    {
        [Tooltip("Player-facing accessory name (\"Lucky Cap\", \"Studio Headset\") - shown on the Merchant's repair/replacement card. Left empty, the card falls back to a generic \"Accessory\" label, so this is optional.")]
        public string DisplayName;

        [PreviewSprite]
        [Tooltip("How this hero's accessory looks as a world collectible after it pops off. Assigned onto the SHARED DroppedAccessory prototype's SpriteRenderer at spawn by DroppedAccessoryView, resolved through the collectible's own Owner - which is why one prototype serves every hero. Unassigned leaves whatever placeholder sprite the prototype itself carries.")]
        public Sprite CollectibleSprite;

        [Tooltip("Uniform scale multiplier applied to CollectibleSprite on the SHARED dropped-accessory prototype (see DroppedAccessoryView). Needed because one prototype serves every hero: a cap, a headset and a mask are rarely drawn at the same source size, so without this the prefab's own scale would have to suit all of them at once. Multiplies the prototype's authored scale rather than replacing it, so 1 (or 0/unset) leaves it exactly as authored.\n\nThe EQUIPPED visual needs no equivalent - that one is a hand-placed GameObject on the hero's own view prefab, so it is scaled directly in the Editor.")]
        public float CollectibleScale;

        [Tooltip("OPTIONAL per-hero override for the particle played where this accessory's debris lands when it breaks (0 durability). Left unassigned, the shared generic one on EffectsManager.accessoryBrokenEffectPrefab is used instead - so a generic \"it shattered\" puff can cover every hero, and only a hero whose accessory deserves something distinctive (a cap bursting into feathers vs. a mask cracking into shards) needs its own.\n\nSame default-with-override shape EnemyDataAsset.ViewPrefab/FactionSkins already uses: assign nothing and everything shares one effect.")]
        public ParticleSystem BrokenEffectPrefab;
    }

    // View-only half of CharacterData (see the partial declaration in CharacterData.cs) - mirrors
    // PassiveData.View.cs/EnemyDataAsset.View.cs one-for-one. Note CharacterData.cs itself still
    // carries an older [Header("View")] block (RingColor/PawnSprite) from before this split existed;
    // new presentation fields belong here.
    public partial class CharacterData
    {
        [Header("Signature Accessory")]
        [Tooltip("Presentation for this hero's Signature Accessory (Recoverable Accessory Guard - see docs/accessory-guard.md). Purely cosmetic: durability, blocking, dropping, recovery and Merchant pricing are all hero-agnostic and live in AccessoryGuardConfig instead.")]
        public HeroAccessoryPresentation Accessory;
    }
}
