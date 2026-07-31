namespace Quantum
{
    using UnityEngine;

    // View-only half of SentryAddWeaponSkillAction (see the partial declaration in
    // SentryAddWeaponSkillAction.cs). Resolved by SentryGunView off SentryBarrel.Source - see
    // EffectsManager-style asset-authored VFX convention this session, just for a persistent sprite
    // swap instead of a one-shot particle effect.
    public partial class SentryAddWeaponSkillAction
    {
        [Tooltip("Shown on the barrel's own gun sprite (SentryGunView) while this weapon is equipped - lets different weapon tiers/types look different on the sentry. Leave empty to keep whatever sprite the barrel prefab already has.")]
        public Sprite WeaponSprite;
    }
}
