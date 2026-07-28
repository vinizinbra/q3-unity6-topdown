namespace Quantum
{
    // Empty subclass purely to get a distinct, semantically-named Type for
    // f.SystemDisable<GameplaySystemGroup>()/SystemEnable<GameplaySystemGroup>() - see
    // LevelUpUtility.BeginLevelUpScreen/Resolve. SystemGroup.Schedule() is already a no-op, so
    // disabling this just skips scheduling every child below for as long as an upgrade screen is
    // open - see SystemSetup.User.cs and docs/level-up-upgrades.md.
    public sealed class GameplaySystemGroup : SystemGroup
    {
        public GameplaySystemGroup(params SystemBase[] children) : base(children)
        {
        }
    }
}
