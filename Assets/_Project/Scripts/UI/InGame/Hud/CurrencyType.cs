// Which run-wide currency something is dealing with - shared by CurrencyUiWidget (HUD totals) and
// FlyingCurrencyManager (pickup flight/flash), so both stay driven by one enum instead of each
// having its own. All three back onto Frame.Global fields (shared run-wide, not per-player, same
// convention Frame.Global.TotalExperience uses - see ExpBarUiWidget), not a per-entity stack. See
// Experience.qtn/Coins.qtn/RiftShards.qtn and ExperienceUtility/CoinUtility/RiftShardUtility.Grant
// for how each total is actually written.
public enum CurrencyType
{
    Experience,
    Coin,
    RiftShard
}
