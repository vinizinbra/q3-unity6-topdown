namespace Quantum
{
    using UnityEngine;

    // View-only half of BossDataAsset (see EnemyDataAsset.View.cs for the same split on the base
    // class) - feeds the Boss Window's full-screen reveal (BossWindow.cs). Title/Subtitle are
    // deliberately separate fields from the base EnemyDataAsset.EnemyName (already read by
    // BossWidget's in-combat HUD name) - the reveal card's own text doesn't need to match that 1:1.
    public partial class BossDataAsset
    {
        public string Title;
        [TextArea] public string Subtitle;
        public Sprite UiSprite;
    }
}
