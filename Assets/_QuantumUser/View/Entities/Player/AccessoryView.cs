using NaughtyAttributes;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // The EQUIPPED half of the Recoverable Accessory Guard's presentation (see
    // docs/accessory-guard.md) - a two-way SWITCH between the hero's "wearing it" and "not wearing
    // it" visuals, driven purely off the simulation's own AccessoryGuard.State:
    //
    //     Equipped                       -> equippedVisual ON,  unequippedVisual OFF
    //     Airborne / Dropped / Broken    -> equippedVisual OFF, unequippedVisual ON
    //
    // Deliberately a swap between two hand-placed GameObjects on the hero's own view prefab rather
    // than instantiating a prop from hero data: these rigs are sprite-based (head_0 / Torso_0 /
    // CharBody), so "with the accessory" and "without it" are usually two different authored
    // sprites, not one prop parented onto a bare head. This is the exact same active-object-swap
    // idiom BlobAnimationView already uses for Alive/Downed/KO and PoiView for Inactive/Active/
    // Expired.
    //
    // Both fields are optional and independent. Assigning only equippedVisual degrades to a plain
    // single toggle (the hero simply shows nothing extra while unequipped); assigning only
    // unequippedVisual works for a rig whose default state already includes the accessory.
    //
    // Per-hero by construction and with no hero-specific code: each hero's own view prefab carries
    // its own AccessoryView pointing at its own two GameObjects, the same way each hero's
    // BlobAnimationView points at its own rig transforms. Nothing here names a hero, and nothing
    // here writes back to simulation state.
    //
    // Polls AccessoryGuard.State every QUpdate rather than subscribing to AccessoryBlocked/
    // Recovered/Broken/Restored: state is authoritative and self-healing, so a late-joining view, a
    // rollback, a resimulated tick or a missed event can never leave a hero visibly wearing
    // something the simulation says they dropped. Same reasoning BlobAnimationView/
    // WeaponViewController already document for their own Downed/KO swaps. The events exist for
    // one-shot FX (an impact spark, a sound), which is a different job.
    public class AccessoryView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Shown ONLY while the accessory is equipped - e.g. this hero's head sprite wearing the cap. Hidden the instant it pops off, is lying in the level, or is broken.")]
        private GameObject equippedVisual;

        [SerializeField, Tooltip("Shown ONLY while the accessory is NOT equipped (Airborne/Dropped/Broken) - e.g. the same head sprite without the cap. Optional: leave unassigned for a hero whose accessory is a pure add-on with no separate bare-headed version.")]
        private GameObject unequippedVisual;

        [SerializeField, SoundDataPicker, Tooltip("Plain SFX played when the accessory goes back ON - covers every way that happens: walking over your own, an ally bringing it to you, or paying the Merchant to repair or replace it. Not a voice line; the spoken reactions are VoiceDirector's job.\n\nRouted through EntitySound, so ticking Quieter When Remote (or Local Player Only) on the SoundData keeps a teammate's re-equip from being as loud as your own.")]
        private SoundData equippedSound;

        // Nullable so the very first resolve always applies, even if it happens to match whatever
        // the prefab was authored with - the authored state is a guess, the frame is the truth.
        private bool? _lastEquipped;

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);

            // Applied here as well as in QUpdate so a hero who spawns (or is re-created after a
            // rollback) already Dropped/Broken never shows a single frame of the wrong visual.
            _lastEquipped = null;
            Refresh(game);
        }

        protected override void QUpdate(QuantumGame game)
        {
            Refresh(game);
        }

        private unsafe void Refresh(QuantumGame game)
        {
            Frame frame = game?.Frames.Predicted;

            if (frame == null)
                return;

            // No AccessoryGuard at all (the mechanic is disabled - RuntimeConfig.AccessoryGuardConfig
            // unassigned, so nothing is ever seeded) reads as "worn", so a hero authored with both
            // visuals still looks right in a build where the guard was never turned on.
            bool equipped = frame.Unsafe.TryGetPointer<AccessoryGuard>(_entityRef, out var guard) == false
                            || guard->State == AccessoryGuardState.Equipped;

            if (_lastEquipped.HasValue && _lastEquipped.Value == equipped)
                return;

            // Whether there WAS a previous state matters as much as what it was: the first resolve
            // happens at spawn (and again after a rollback re-creates the view), and a hero spawns
            // already wearing the thing - firing the sound there would mean an equip noise every
            // time anyone spawned, for something nobody just picked up.
            bool hadPreviousState = _lastEquipped.HasValue;
            bool wasEquipped = hadPreviousState && _lastEquipped.Value;

            _lastEquipped = equipped;
            Apply(equipped);

            if (hadPreviousState && equipped && wasEquipped == false && equippedSound != null)
                EntitySound.PlayAttached(equippedSound, transform, _entityRef);
        }

        private void Apply(bool equipped)
        {
            if (equippedVisual != null && equippedVisual.activeSelf != equipped)
                equippedVisual.SetActive(equipped);

            if (unequippedVisual != null && unequippedVisual.activeSelf == equipped)
                unequippedVisual.SetActive(equipped == false);
        }

        // Editor-only preview, same shape BlobAnimationView's own PreviewAlive/PreviewDowned/
        // PreviewKO buttons use - lets the swap be checked in the scene without a live match. A real
        // AccessoryGuard drives it off actual state every frame once one exists.
        [Button("Preview Equipped")]
        private void PreviewEquipped() => Apply(true);

        [Button("Preview Dropped / Broken")]
        private void PreviewUnequipped() => Apply(false);
    }
}
