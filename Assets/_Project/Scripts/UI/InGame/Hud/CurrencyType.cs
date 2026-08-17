// Which currency something is dealing with - shared by CurrencyUiWidget (HUD totals) and
// FlyingCurrencyManager (pickup flight/flash), so both stay driven by one enum instead of each
// having its own. Experience stays a shared Frame.Global total (see Experience.qtn,
// ExperienceUtility.Grant) - Coin/RiftShard are PER-PLAYER wallets on CharacterStats (see
// CharacterStats.qtn, CoinUtility/RiftShardUtility.Grant/GrantAll, docs/breathing-poi.md) since
// 2026-08-14's per-player currency conversion.
public enum CurrencyType
{
    Experience,
    Coin,
    RiftShard
}
