using QuantumUser.View;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // The Base Repair Station's "ready" prop: paints a SpriteRenderer with THIS client's own hero accessory
    // (CharacterData.Accessory.CollectibleSprite), so the ship shows the accessory it will repair. Showing/hiding
    // it with the portal particle is PoiView's job (both live under its activeVisual). Presentation only -
    // re-resolves if the local hero changes.
    //
    // Scale is unified with the dropped accessory: the sprite's PARENT carries the shared base (AccessoryOrb's
    // sprite scale x a smaller factor) plus the JuicyEffects blob idle (scale-in + squash wobble), and this
    // component only multiplies the sprite itself by the hero's CollectibleScale - the same per-hero correction
    // DroppedAccessoryView applies, so every hero's accessory keeps the same relative size in both places.
    public class RepairStationAccessoryView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Painted with the local hero's accessory sprite. Its sorting order must stay above the portal particle's.")]
        private SpriteRenderer spriteRenderer;

        private Vector3 _authoredScale = Vector3.one;
        private bool _authoredScaleCaptured;
        private AssetRef<CharacterData> _shownFor;

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game); // sets _game/_entityRef - without it QUpdate never runs
            _shownFor = default;   // pooled/re-instantiated view: resolve the local hero again

            if (spriteRenderer == null)
                spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
            if (spriteRenderer != null && _authoredScaleCaptured == false)
            {
                _authoredScale = spriteRenderer.transform.localScale;
                _authoredScaleCaptured = true;
            }
        }

        protected override unsafe void QUpdate(QuantumGame game)
        {
            if (spriteRenderer == null || MyLocalPlayer.Instance == null)
                return;

            Frame frame = game.Frames.Predicted;
            EntityRef local = MyLocalPlayer.Instance.EntityRef;
            if (local == EntityRef.None || frame.Unsafe.TryGetPointer<CharacterStats>(local, out var stats) == false)
                return;

            if (stats->CharacterData == _shownFor || stats->CharacterData.IsValid == false)
                return;

            _shownFor = stats->CharacterData;
            CharacterData data = frame.FindAsset(stats->CharacterData);
            if (data == null || data.Accessory.CollectibleSprite == null)
                return;

            spriteRenderer.sprite = data.Accessory.CollectibleSprite;
            float scale = data.Accessory.CollectibleScale > 0f ? data.Accessory.CollectibleScale : 1f;
            spriteRenderer.transform.localScale = _authoredScale * scale;
        }
    }
}
