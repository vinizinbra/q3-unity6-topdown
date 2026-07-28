namespace Quantum
{
    using System;
    using Photon.Deterministic;

    // Only the categories GroupSpawnerUtility actually branches on today - HighGroundRanged/Boss
    // exist as authored values so a designer can mark an enemy's intent now, but the spawner
    // currently treats both exactly like Flying (free vertical placement, no jump/line-of-sight
    // validation yet). See docs/survival-director.md "Group Spawner (Domain 3)" roadmap - wiring
    // real HighGroundRanged validation is a follow-up milestone, not a silent bug.
    public enum EnemySpawnCategory
    {
        GroundMelee,
        GroundRanged,
        HighGroundRanged,
        Flying,
        Boss
    }

    // Domain 3 (Group Spawner) per-enemy-type placement rules - referenced by EnemyDataAsset.
    // SpawnProfile, one asset shared by every EnemyDataAsset that should place the same way (e.g.
    // one "GroundMelee" profile for every grunt/fighter type). Deliberately minimal - only the
    // fields GroupSpawnerUtility's Milestone 1-2 implementation consumes. Ground-probe tuning
    // (start height/distance), ground-continuity accessibility sampling, and high-ground jump/
    // line-of-sight validation are real future fields (see the design doc's Milestone 4/5
    // roadmap) intentionally left off until a system actually reads them - an authored-but-inert
    // field here would be indistinguishable from a bug, unlike e.g. EnemyDataAsset.Traits, which
    // is inert by an explicit, commented design choice.
    public class EnemySpawnProfile : AssetObject
    {
        public EnemySpawnCategory SpawnCategory = EnemySpawnCategory.GroundMelee;

        // Only checked for GroundMelee/GroundRanged (see GroupSpawnerUtility.ValidateHeightRule) -
        // Flying/HighGroundRanged/Boss skip this check entirely for now. HeightDifference =
        // candidateGroundY - anchorGroundY, so Minimum is how far BELOW the anchor is still
        // allowed (typically negative) and Maximum is how far ABOVE (typically small/near zero) -
        // "must not spawn significantly above the player" from the design doc.
        public FP MinimumHeightDifference = -3;
        public FP MaximumHeightDifference = FP._0_50;

        // Overlap-query test volume (a vertical capsule: ClearanceRadius horizontal, ClearanceHeight
        // tall) centered on a candidate ground position - see GroupSpawnerUtility.HasClearance.
        // Author these close to the enemy's real EnemyDataAsset.Radius/visual height; a Boss profile
        // needs a much bigger volume than a Filler.
        public FP ClearanceRadius = 1;
        public FP ClearanceHeight = 2;
    }
}
