namespace Quantum
{
    // Hero Skill Upgrade - "turns the bomb into a birthday cake": while equipped, the thrown bomb
    // is also a Decoy for the rest of its flight, pulling enemy aggro toward it before it eventually
    // detonates (see ProjectileSkillData.Fire, which adds Decoy onto the freshly spawned projectile
    // when the owner holds DecoyOnThrowUpgrade).
    //
    // Begin-only, deliberately not paired with End: this configures what the skill produces, not a
    // temporary buff that should only apply while the skill is actively resolving. Revoking on End
    // would race against Fire() actually reading it - Begin/End brackets the throw itself, and
    // nothing guarantees Fire() runs before End does on every activation. Re-granting fresh
    // (idempotent) every activation and never removing it sidesteps that race entirely.
    public unsafe partial class BirthdayCakeSkillAction : SkillActionData
    {
        public BirthdayCakeSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<DecoyOnThrowUpgrade>(filter.Entity, out _);
        }
    }
}
