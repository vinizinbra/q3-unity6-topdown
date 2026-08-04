namespace QuantumUser.Editor.Tests
{
    using NUnit.Framework;
    using Photon.Deterministic;
    using Quantum;
    // Quantum ships its own Assert class (deterministic-simulation assertions) which collides with
    // NUnit.Framework.Assert now that both namespaces are in scope - alias pins every Assert.* call
    // below to NUnit's.
    using Assert = NUnit.Framework.Assert;

    // Covers the pure, Frame-free half of the Rift Mark stacking mechanic -
    // StatusEffectUtility.ClampStacks/IsValidAffinityProc - see docs/elemental-reactions.md. The rest
    // of the mechanic (actual StatusEffects component mutation, reaction dispatch, multiplayer
    // determinism) needs a live Quantum Frame this project has no test harness for yet; that half is
    // verified manually in-Editor instead - see the doc's "Current status" section.
    //
    // No asmdef backs this file deliberately - StatusEffectUtility itself has no asmdef either (it
    // compiles into the implicit Assembly-CSharp), and an asmdef-based test assembly can't reference
    // that implicit assembly. Living under this Editor/ folder is what makes Unity's Test Runner
    // pick these up without one.
    [TestFixture]
    public class RiftMarkStackTests
    {
        private const byte MaxStacksMvp = 2;

        // Acceptance case 1: applying to an unmarked (0-stack) target produces 1 stack.
        [Test]
        public void ClampStacks_ApplyToUnmarked_ProducesOneStack()
        {
            Assert.AreEqual(1, StatusEffectUtility.ClampStacks(current: 0, delta: 1, maxStacks: MaxStacksMvp));
        }

        // Acceptance case 2: applying again produces 2 stacks.
        [Test]
        public void ClampStacks_ApplyAgain_ProducesTwoStacks()
        {
            Assert.AreEqual(2, StatusEffectUtility.ClampStacks(current: 1, delta: 1, maxStacks: MaxStacksMvp));
        }

        // Acceptance case 3: applying again at max stacks does not exceed the configured maximum.
        [Test]
        public void ClampStacks_ApplyAtMax_StaysAtMax()
        {
            Assert.AreEqual(2, StatusEffectUtility.ClampStacks(current: 2, delta: 1, maxStacks: MaxStacksMvp));
        }

        // Acceptance case 4: changing MaxStacks to 3 through configuration allows 3 stacks without
        // code changes - same ClampStacks call, just a different maxStacks argument.
        [Test]
        public void ClampStacks_HigherConfiguredMax_AllowsMoreStacks()
        {
            Assert.AreEqual(3, StatusEffectUtility.ClampStacks(current: 2, delta: 1, maxStacks: 3));
        }

        // Never goes negative even if delta overshoots - guards ConsumeRiftMarkStack's own call
        // shape (current stacks minus more than are actually present).
        [Test]
        public void ClampStacks_NegativeDeltaPastZero_ClampsToZero()
        {
            Assert.AreEqual(0, StatusEffectUtility.ClampStacks(current: 0, delta: -1, maxStacks: MaxStacksMvp));
            Assert.AreEqual(0, StatusEffectUtility.ClampStacks(current: 1, delta: -5, maxStacks: MaxStacksMvp));
        }

        // Acceptance case 5: a valid Affinity Proc against 2 stacks consumes 1, leaving 1.
        [Test]
        public void ClampStacks_ConsumeOneOfTwo_LeavesOne()
        {
            Assert.AreEqual(1, StatusEffectUtility.ClampStacks(current: 2, delta: -1, maxStacks: MaxStacksMvp));
        }

        // Acceptance case 6: a second valid proc consumes the final stack, reaching 0.
        [Test]
        public void ClampStacks_ConsumeFinalStack_ReachesZero()
        {
            Assert.AreEqual(0, StatusEffectUtility.ClampStacks(current: 1, delta: -1, maxStacks: MaxStacksMvp));
        }

        // A huge stray `current` (should never happen in practice - RiftMarkStacks is itself clamped
        // on every write - but this is the boundary the int-then-cast-to-byte shape has to hold) still
        // clamps correctly rather than wrapping through byte overflow.
        [Test]
        public void ClampStacks_LargeCurrentValue_StillClampsToMax()
        {
            Assert.AreEqual(MaxStacksMvp, StatusEffectUtility.ClampStacks(current: 255, delta: 1, maxStacks: MaxStacksMvp));
        }

        // Acceptance case 8: a hit that applies Rift Mark to an unmarked target must not trigger a
        // reaction immediately - preHitStacks is captured BEFORE that same hit's own application, so
        // it's still 0 at the point IsValidAffinityProc is checked.
        [Test]
        public void IsValidAffinityProc_ZeroPreHitStacks_IsInvalid()
        {
            Assert.IsFalse(StatusEffectUtility.IsValidAffinityProc(preHitStacks: 0, reactionLockoutRemaining: FP._0));
        }

        // Acceptance case 7/9: a pre-existing stack with no active lockout is a valid proc.
        [Test]
        public void IsValidAffinityProc_ExistingStackNoLockout_IsValid()
        {
            Assert.IsTrue(StatusEffectUtility.IsValidAffinityProc(preHitStacks: 1, reactionLockoutRemaining: FP._0));
        }

        // Acceptance case 10/11: an active reaction lockout blocks consumption even with stacks
        // present - this is what stops multiple damage callbacks from one proc (or two procs in
        // consecutive frames) from consuming more than once.
        [Test]
        public void IsValidAffinityProc_ActiveLockout_IsInvalidEvenWithStacks()
        {
            Assert.IsFalse(StatusEffectUtility.IsValidAffinityProc(preHitStacks: 2, reactionLockoutRemaining: FP._0_50));
        }

        // Once the lockout has ticked down to exactly zero, consumption is valid again.
        [Test]
        public void IsValidAffinityProc_LockoutExpired_IsValidAgain()
        {
            Assert.IsTrue(StatusEffectUtility.IsValidAffinityProc(preHitStacks: 2, reactionLockoutRemaining: FP._0));
        }
    }
}
