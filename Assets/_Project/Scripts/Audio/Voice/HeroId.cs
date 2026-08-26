// Stable authoring key for anything that varies PER HERO on the presentation side - voice banks, and
// the pair-specific dialogue an interaction between two heroes resolves to.
//
// An AssetRef<CharacterData> already identifies a hero uniquely, and that is still what the
// simulation uses. This exists purely because a pair table (Max->Pixie vs Max->Brute) is miserable
// to author against asset references, and because it keeps this whole feature out of the Quantum
// simulation assembly: the hero -> HeroId link is authored on HeroVoiceBank, so no simulation type
// gains a field for the sake of voice acting.
//
// Append new heroes at the END - these values are lookup keys in authored assets, so reordering
// silently reassigns every hero's lines.
public enum HeroId
{
    None = 0,
    Max = 1,
    Pixie = 2,
    Brute = 3,
    Kai = 4,
    Zara = 5,
    Lux = 6,
}
