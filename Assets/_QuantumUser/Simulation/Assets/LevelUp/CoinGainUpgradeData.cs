namespace Quantum
{
    using Photon.Deterministic;

    // Mirror of ExperienceGainUpgradeData, but PERSONAL rather than shared - and that difference
    // comes entirely from the currency, not from anything here.
    //
    // A Coin pickup broadcasts to every connected player's own wallet, each scaled by THEIR OWN
    // CharacterStats.CoinGainMultiplier (CoinUtility.GrantAll), so this upgrade only ever affects
    // the wallet of whoever picked it - two players can hold different amounts of it and each simply
    // earns at their own rate. Experience, by contrast, is one shared Frame.Global total, so the XP
    // version of this benefits the whole team. See docs/global-upgrades.md.
    public unsafe class CoinGainUpgradeData : CharacterStatMultiplierUpgradeData
    {
        protected override FP* GetStat(CharacterStats* stats) => &stats->CoinGainMultiplier;
    }
}
