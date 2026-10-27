namespace Quantum
{
    using UnityEngine;

    // View-only half of SentryWeaponSystemsSkillAction (see the partial declaration in
    // SentryWeaponSystemsSkillAction.cs). One sprite per weapon SLOT rather than one per asset, since
    // a single ranked asset now arms all three Ascension slots - SentryGunView resolves the right one
    // off SentryBarrel.SlotIndex.
    public partial class SentryWeaponSystemsSkillAction
    {
        [Tooltip("Gun art per barrel slot (index 1 = Minigun, 2 = Rocket Pod, 3 = Laser). Index 0 is the baseline Cannon, which this line never arms - leave it empty. An unset entry keeps whatever the barrel prefab already shows.")]
        public Sprite[] WeaponSpriteBySlot = new Sprite[4];

        // Tolerates a short/unauthored array rather than throwing - an unset slot simply means "keep
        // whatever the barrel prefab already shows", which is SentryGunView's own fallback.
        public Sprite GetWeaponSprite(int slotIndex)
        {
            return WeaponSpriteBySlot != null && slotIndex >= 0 && slotIndex < WeaponSpriteBySlot.Length
                ? WeaponSpriteBySlot[slotIndex]
                : null;
        }
    }
}
