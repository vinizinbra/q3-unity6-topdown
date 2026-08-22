namespace Quantum
{
    using UnityEngine;

    // View-only half of PassiveData (see the partial declaration in PassiveData.cs) - mirrors
    // SkillData.View.cs one-for-one, on the shared abstract base rather than per hero passive, since
    // every passive wants the same "which icon/name represents this" concept. Description itself
    // already lives on PassiveData proper, so this only adds the two fields it was missing.
    //
    // First consumer: HeroInfoWidget's Passive Skill row (the Tab-hold hero info popup), which shows
    // a passive exactly like it shows the Base Skill - icon, name, description.
    public partial class PassiveData
    {
        [PreviewSprite]
        public Sprite Icon;

        [Tooltip("Player-facing passive name. Left empty, whatever displays it falls back to this asset's own file name (see HeroInfoWidget.ResolveName).")]
        public string DisplayName;
    }
}
