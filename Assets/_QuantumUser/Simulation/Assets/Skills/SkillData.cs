namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    // Base for every player skill type; each subclass is its own Quantum asset owning its
    // execution logic - direct analog of EnemyDeliveryData, but driven by player input (SkillSystem)
    // instead of an AI state machine (EnemySystem). One asset per level (leveling = swapping which
    // AssetRef a CharacterSkills slot points at) rather than a per-level array/curve on one asset.
    public abstract unsafe partial class SkillData : AssetObject
    {
        public string Name;

        // How many stacks a slot starts with the moment this asset is first assigned to it
        // (PlayerSpawnUtility.InitSkillSlot) - distinct from SkillSlot.MaxStacks (the ceiling,
        // component/prototype-owned, see below): this is asset-owned since it can reasonably differ
        // per skill/level (e.g. a higher-level Dash could start partway banked instead of empty).
        // Clamped to MaxStacks at assignment - never exceeds the slot's actual cap.
        public byte InitStacks = 1;

        // Time to regain one stack, not a whole-skill lockout - see SkillSystem's recharge tick.
        // MaxStacks (how many stacks that recharge counts up to) deliberately isn't here - it lives
        // entirely on SkillSlot, baked on the prototype like Health.MaxHealth, so a runtime upgrade
        // ("+1 charge" perk) can raise a slot's cap independently of which SkillData/level is
        // equipped, and isn't reset by simply reassigning which skill/level a slot points at.
        public FP Cooldown = 1;


        // Skill-level kill switch for this Actions list specifically - false (the default) skips
        // resolving/checking every entry in SkillSystem.InvokeActions entirely, rather than relying
        // on each one's own SkillActionData.Activated. Doesn't touch slot->Upgrades - a granted
        // level-up pick still always runs regardless of this flag, since a player was explicitly
        // told they received it. Flip true only for a skill that actually composes behavior through
        // this list.
        public bool CheckActions = false;
        // Composable behaviors mixed onto this skill with no new C# - see SkillActionData. Each
        // action's own Phase field (data, not which method it overrides) decides which SkillSystem
        // lifecycle point(s) fire it, so e.g. "spawn on Begin" vs "spawn on End" is the same
        // SpawnEntitySkillAction with a different Phase, not a new class. One list (not three separate
        // Begin/OnGoing/End lists) so a paired action (Phase = Begin | End) can't be added to one
        // phase and forgotten in the other.
        [ExpandableAsset] public List<AssetRef<SkillActionData>> Actions = new();

        // Return true if the skill fully resolves this same tick; false if it needs Tick() (Dash
        // always does). SkillSystem captures slot->StartPosition/TargetPosition (both = current
        // position) immediately before calling this - override TargetPosition here for anything
        // that needs a computed destination.
        public abstract bool Begin(Frame f, ref SkillSystem.Filter filter, Input* input, SkillSlot* slot);

        // Called only if Begin() returned false. Return true once finished.
        public virtual bool Tick(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot)
        {
            return true;
        }

        // Called exactly once when the skill finishes (instant Begin, or Tick reporting done) -
        // core per-skill cleanup only (e.g. DashSkillData restoring KCC.SetActive(true)). Actions'
        // OnEnd fires separately, right after this, from SkillSystem's single finish call site
        // (mirrors EnemySystem.EnterRecovering).
        public virtual void End(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot)
        {
        }
    }
}
