namespace Quantum
{
    // Shared marker for an UpgradeData kind that supports multiple ranks/levels (a "Hero Ascension
    // line") - implemented by both PassiveUpgradeData and SkillActionData, the two pools Hero
    // Ascensions are drawn from (see LevelUpCategory.HeroSkill in LevelUp.qtn). MaxRank == 1 (the
    // default on both) means "classic single-pick", unchanged behavior for every existing upgrade
    // across every hero. Exists purely so generic tooling (GameplayUiController.BuildCardData today)
    // can read rank info without knowing which concrete kind it's looking at - see
    // docs/level-up-upgrades.md for the full rank mechanism this is part of.
    public interface IRankedUpgrade
    {
        byte MaxRank { get; }

        string GetDescription(int rank);
    }
}
