using Photon.Deterministic;

namespace Quantum
{
    public partial class RuntimePlayer
    {
        public FP test;

        // Player's own meta-progression weapon-talent level, carried in from OUTSIDE this match
        // (see MatchMakingConfig.StartRunner, which reads it from local PlayerPrefs before
        // AddPlayer) - seeds CharacterStats.WeaponTalentLevel once at spawn (PlayerSpawnUtility.
        // Spawn). Distinct from CharacterStats.WeaponTalentLevel itself, which then keeps
        // incrementing live for the rest of THIS match on every LevelUpCategory.ChooseWeapon pick -
        // this field is only ever read once, never written by the simulation.
        public byte WeaponLevel;
    }
}