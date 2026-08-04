namespace QuantumUser.Editor.Tests
{
    using Photon.Deterministic;
    using Quantum;
    using Assert = NUnit.Framework.Assert;
    using NUnit.Framework;

    // Covers the pure, Frame-free half of the Rift Mark content pool (Weapon Perks + Rift
    // Mutations) - RiftMutationMarkUtility.IsHeavyHit/IsBelowExecutionThreshold. See
    // docs/rift-mutations.md/docs/weapon-perks.md for the full roster. Everything else in that
    // pool (cooldown-key gating, priority-ordered dispatch, Rift Dash's overlap sweep, Fractured
    // Presence's exposure accumulator) needs a live StatusEffects*/Frame this project has no
    // simulation test harness for yet - verified manually in-Editor instead, same gap
    // RiftMarkStackTests.cs's own header comment already documents for the core mechanic.
    //
    // No asmdef backs this file, same reasoning as RiftMarkStackTests.cs - the code under test
    // compiles into the implicit Assembly-CSharp, which an asmdef-based test assembly can't
    // reference.
    [TestFixture]
    public class RiftMarkApplicationTests
    {
        // Acceptance case 19 (Heavy Fracture triggers only from qualifying single-hit events):
        // clears the flat damage threshold alone.
        [Test]
        public void IsHeavyHit_ClearsFlatThreshold_IsHeavy()
        {
            Assert.IsTrue(RiftMutationMarkUtility.IsHeavyHit(damage: 40, maxHealth: 1000, flatThreshold: 40, percentThreshold: FP._0_50));
        }

        // Clears the percent-of-max-health threshold alone (a small enemy where 40 flat damage is a
        // huge fraction of its own health, even below the flat threshold).
        [Test]
        public void IsHeavyHit_ClearsPercentThreshold_IsHeavy()
        {
            Assert.IsTrue(RiftMutationMarkUtility.IsHeavyHit(damage: 30, maxHealth: 100, flatThreshold: 40, percentThreshold: FP._0_25));
        }

        [Test]
        public void IsHeavyHit_ClearsNeither_IsNotHeavy()
        {
            Assert.IsFalse(RiftMutationMarkUtility.IsHeavyHit(damage: 10, maxHealth: 1000, flatThreshold: 40, percentThreshold: FP._0_50));
        }

        // maxHealth <= 0 (an unseeded Health) never qualifies via the percent path, even if damage
        // alone would divide to something huge - only the flat path can still fire.
        [Test]
        public void IsHeavyHit_ZeroMaxHealth_OnlyFlatPathCanQualify()
        {
            Assert.IsFalse(RiftMutationMarkUtility.IsHeavyHit(damage: 10, maxHealth: FP._0, flatThreshold: 40, percentThreshold: FP._0_25));
            Assert.IsTrue(RiftMutationMarkUtility.IsHeavyHit(damage: 40, maxHealth: FP._0, flatThreshold: 40, percentThreshold: FP._0_25));
        }

        // Acceptance case 22 (Execution Fracture checks health before damage): below threshold.
        [Test]
        public void IsBelowExecutionThreshold_HealthBelowThreshold_IsTrue()
        {
            Assert.IsTrue(RiftMutationMarkUtility.IsBelowExecutionThreshold(preHealth: 20, maxHealth: 100, threshold: FP._0_25));
        }

        [Test]
        public void IsBelowExecutionThreshold_HealthAtOrAboveThreshold_IsFalse()
        {
            Assert.IsFalse(RiftMutationMarkUtility.IsBelowExecutionThreshold(preHealth: 25, maxHealth: 100, threshold: FP._0_25));
            Assert.IsFalse(RiftMutationMarkUtility.IsBelowExecutionThreshold(preHealth: 50, maxHealth: 100, threshold: FP._0_25));
        }

        [Test]
        public void IsBelowExecutionThreshold_ZeroMaxHealth_IsFalse()
        {
            Assert.IsFalse(RiftMutationMarkUtility.IsBelowExecutionThreshold(preHealth: 0, maxHealth: FP._0, threshold: FP._0_25));
        }
    }
}
