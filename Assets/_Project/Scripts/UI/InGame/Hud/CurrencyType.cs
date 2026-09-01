// Which currency something is dealing with - shared by CurrencyUiWidget (HUD totals) and
// FlyingCurrencyManager (pickup flight/flash), so both stay driven by one enum instead of each
// having its own. Experience stays a shared Frame.Global total (see Experience.qtn,
// ExperienceUtility.Grant) - Coin/RiftShard are PER-PLAYER wallets on CharacterStats (see
// CharacterStats.qtn, CoinUtility/RiftShardUtility.Grant/GrantAll, docs/breathing-poi.md) since
// 2026-08-14's per-player currency conversion.
//
// Scrap is deliberately in here even though it is not a wallet currency at all - it is Lux's own
// per-passive pickup resource (LuxScrapCollector.ScrapStacks, see ScrapUtility). It joins the enum
// purely so a Scrap pickup reuses the ONE FlyingCurrencyManager/HitFeedback.FlashPickup pipeline
// every other pickup already goes through, rather than growing a parallel FlyingScrapManager that
// would then have to be kept in sync by hand. Its HUD readout is NOT CurrencyUiWidget's job -
// LuxHudWidget already owns that (stacks against the free-cast threshold, not a running total), so
// CurrencyUiWidget.TryResolveTotal deliberately has no Scrap case.
public enum CurrencyType
{
    Experience,
    Coin,
    RiftShard,
    Scrap
}
