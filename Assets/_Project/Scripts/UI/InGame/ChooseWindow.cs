using System;
using NaughtyAttributes;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using QuantumUser.View.Util;
using UnityEngine.UI;

// Generic choice screen - Level-Up/Weapon-Upgrade/Chest, Cursed Rift's own Sacrifice/Mutation
// stages, Store, and Blacksmith all drive the SAME window instance per local slot (see
// GameplayUiController), not separate copies - "type" is just which CardData/WeaponCardWidget.
// CardData gets pushed into Refresh/RefreshWeaponChoice/RefreshStore, this class has no idea which
// flow is currently using it. Cursed Rift's own flow is simply two back-to-back uses of this same
// window (open showing Sacrifice cards, pick one, it refreshes in place showing Mutation cards)
// rather than a separate confirm sub-step - clicking a card commits immediately, same "one click =
// one irreversible pick" idiom every other screen here already uses (Store is the one exception -
// see RefreshStore's own comment).
//
// Level-Up goes through WindowManager.ShowWindow<ChooseWindow>() + a Time.timeScale ramp (a
// whole-party pause, see GameplayUiController.UpdateUpgradeScreen); Cursed Rift/Store/Blacksmith
// show/hide the SAME instance directly per slot with neither (see GameplayUiController.
// UpdatePoiWindow) - a real Level-Up happening for a DIFFERENT player can therefore visually
// pre-empt a player's own in-progress POI screen (WindowManager's sweep hides every registered
// window, this one included). This is an accepted tradeoff, not a bug: nothing about the
// interacting player's own interaction component is touched by it, so their screen picks back up
// automatically, right where they left off, the moment the other player's Level-Up closes.
//
// Just orchestrates an array of UpgradeCardWidget children, one per LevelUpChoice.Options slot (or
// CursedRiftInteraction.SacrificeChoices/MutationChoices/BlacksmithInteraction.PerkChoices/Store's
// own FoodOffers - same fixed-slot shape) - this class itself has no Quantum dependency either.
//
// Also owns a parallel array of WeaponCardWidget children for a Choose-Weapon screen or Store's own
// weapon offers (see Refresh/RefreshWeaponChoice/RefreshStore) - every screen except Store is
// always homogeneous (every rolled option is ChooseWeapon, or none are), so only one of the two
// card families is ever shown at a time there; Store is the one place both are live together.
public class ChooseWindow : UiWindow
{
    // A hidden template, not a live card itself - Awake clones it cardCount times under the same
    // parent (so the scene only needs one hand-authored card) then disables the template, leaving
    // only the clones live. cardCount must match the largest fixed-size options array this window
    // is ever driven from (3 today, shared by LevelUpChoice.Options/CursedRiftInteraction.
    // SacrificeChoices/MutationChoices - see LevelUp.qtn/CursedRift.qtn).
    [SerializeField] private UpgradeCardWidget cardPrefab;
    [SerializeField] private int cardCount = 3;

    // Same clone-then-disable-the-template shape as cardPrefab/cardCount above, for a
    // Choose-Weapon screen - see WeaponCardWidget.
    [SerializeField] private WeaponCardWidget weaponCardPrefab;
    [SerializeField] private int weaponCardCount = 3;

    [SerializeField] private TMP_Text countdownText;

    // "Level Up!" for a plain level-up, the rolled category's display name (e.g. "Weapon Perk")
    // for a Chest, "CURSED RIFT"/"RIFT AWAKENED" for Cursed Rift's own two stages, or "STORE"/
    // "BLACKSMITH" - see GameplayUiController.BuildTitle/RefreshStoreWindow/RefreshBlacksmithWindow,
    // which own the actual wording.
    [SerializeField] private TMP_Text titleText;

    // Optional - unused by the existing Level-Up/Weapon-Upgrade/Chest call sites (Refresh/
    // RefreshWeaponChoice's own `subtitle` param defaults to null there, which leaves this
    // untouched). Cursed Rift's two stages populate it (e.g. "CHOOSE A SACRIFICE").
    [SerializeField] private TMP_Text subtitleText;

    // Shared by both card families (same button regardless of whether cards or weaponCards is
    // currently active) - redraws whichever is showing, see GameplayUiController.OnRerollClicked /
    // LevelUpUtility.RerollOptionsFor. rerollChargesText shows the player's own remaining
    // CharacterStats.RerollQuantity; the button itself is disabled at 0 (same "interactable, not
    // hidden" convention as the cards once a pick is confirmed). Hidden entirely (SetRerollButtonActive,
    // via Refresh's allowReroll param) on Cursed Rift's own screens - reroll has no meaning there.
    [Header("Reroll")]
    [SerializeField] private Button rerollButton;
    [SerializeField] private TMP_Text rerollChargesText;

    // ONE button, reused for two mutually-exclusive "decline this screen" actions that never both
    // apply at once (RefreshWeaponChoice vs. Refresh's allowCancel are two different card families
    // - only one is ever active): "KEEP CURRENT" on a Choose-Weapon screen (declining picks
    // nothing, still counts as confirmed - see LevelUpUtility.ConfirmKeepCurrent/
    // KeepCurrentWeaponCommand) or "CANCEL" on Cursed Rift's Sacrifice stage (the one place walking
    // away without picking anything needs to be possible - see CancelCursedRiftCommand). Field kept
    // named secondaryButton (was keepCurrentButton before Cursed Rift needed the same button for a
    // second purpose - FormerlySerializedAs preserves the scene's existing wiring). Hidden entirely
    // for a plain Level-Up (neither call site applies there).
    [Header("Secondary Action (Keep Current / Cancel)")]
    [SerializeField, FormerlySerializedAs("keepCurrentButton")] private Button secondaryButton;
    private TMP_Text _secondaryButtonText;

    // This window only orchestrates WHEN each piece plays (all timing/delay knobs below) - HOW each
    // piece animates (shake/grow/impact) lives on that piece's own ShakeGrowImpactAnimation
    // component instead (one on LevelUpTitle, one on cardPrefab, one on weaponCardPrefab - each tuned
    // independently in its own Inspector, e.g. the title flattens on X while cards flatten on Y).
    [Header("Intro Animation Timing")]
    [SerializeField, Tooltip("Delay after Show() before the dim even starts fading in - a plain pacing knob, no target object involved.")]
    private float introStartDelay = 0f;
    [SerializeField] private float dimFadeDuration = 0.25f;
    [SerializeField, Tooltip("Extra gap after the dim finishes fading in before the title starts its own entrance.")]
    private float titleRevealDelay = 0f;
    [SerializeField, Tooltip("Delay after the title starts before the first card begins its own entrance.")]
    private float cardIntroStartDelay = 0.15f;
    [SerializeField, Tooltip("Extra delay stacked per card index, so cards open one after another instead of all at once.")]
    private float cardIntroStagger = 0.1f;

    [SerializeField, Tooltip("The dark overlay behind the whole popup - fades in first, then everything else follows.")]
    private Image dimImage;

    [SerializeField, Tooltip("The title's own ShakeGrowImpactAnimation, on the whole title block (background panel + heading + description) - see LevelUpTitle in the hierarchy, not just the text.")]
    private ShakeGrowImpactAnimation titleIntro;

    // Any particle (or other trigger-only effect, e.g. a ScaleTween-driven UIParticle) that should
    // stay dormant while the window is closed and only fire from an explicit trigger elsewhere - e.g.
    // titleIntro's own onImpact UnityEvent calling GameObject.SetActive(true) on one of these, which
    // then plays itself via its own OnEnable (UiTween.playOnEnable / ParticleSystem.playOnAwake).
    // Forced inactive here, before base.Show() activates the whole popup, so none of them fire early
    // just because their parent turned on - only reactivating one explicitly (a trigger) does.
    [Header("Sound")]
    [SerializeField, SoundDataPicker, Tooltip("Played once per CARD as it animates in, on the same stagger as the intro - so three cards read as three beats rather than one lump. Covers both card families (upgrade and weapon). Only fires for cards actually being shown, so a screen with fewer cards doesn't play phantom ones.")]
    private SoundData cardAppearSound;

    [SerializeField, SoundDataPicker, Tooltip("Played when a card is clicked - either family, and the Buy button too, since that routes through the same onClicked. Fires before the choice is sent, so it is heard even though the window closes immediately after.")]
    private SoundData cardChooseSound;

    [SerializeField, Tooltip("Particles that must NOT play just because the window opened - reset to inactive every Show(), and only meant to be reactivated by an explicit trigger (e.g. titleIntro.onImpact -> SetActive(true)).")]
    private GameObject[] introParticles;

    // Raised with a card's index (0-based) when a `cards[]` (UpgradeCardWidget) entry is clicked -
    // GameplayUiController forwards this into whichever command matches the currently-active flow
    // (SelectLevelUpUpgradeCommand/SelectSacrificeCommand/SelectMutationCommand/BuyStoreFoodCommand/
    // SelectBlacksmithPerkCommand - see OnCardClicked).
    public Action<int> onCardClicked;

    // Raised with a card's index (0-based) when a `weaponCards[]` (WeaponCardWidget) entry is
    // clicked - split from onCardClicked so Store's own screen (the one place both card families
    // are ever live at once, see RefreshStore) can tell a food-card click from a weapon-offer click.
    // Every other screen only ever has one family active, so this and onCardClicked never both fire
    // for the same screen except on Store's own.
    public Action<int> onWeaponCardClicked;

    // Raised when rerollButton is clicked - GameplayUiController forwards this into a
    // RerollLevelUpOptionsCommand. No index needed, unlike onCardClicked - a reroll redraws every
    // option at once, not one slot.
    public Action onRerollClicked;

    // Raised when secondaryButton is clicked - GameplayUiController forwards this into whichever
    // command matches the currently-active flow (KeepCurrentWeaponCommand on a Choose-Weapon
    // screen, CancelCursedRiftCommand on Cursed Rift's Sacrifice stage - see
    // GameplayUiController.OnSecondaryButtonClicked). No index needed either way.
    public Action onSecondaryButtonClicked;

    private UpgradeCardWidget[] cards;
    private WeaponCardWidget[] weaponCards;

    // Parallel to cards/weaponCards - each clone's own ShakeGrowImpactAnimation (cloned along with
    // it from cardPrefab/weaponCardPrefab, so every clone gets its own independent copy of whatever
    // shake/grow/impact tuning is authored on the prefab). Null if a given prefab has none.
    private ShakeGrowImpactAnimation[] cardIntros;
    private ShakeGrowImpactAnimation[] weaponCardIntros;

    private float _dimFullAlpha = 1f;
    private Tween _dimTween;

    private void Awake()
    {
        if (dimImage != null)
            _dimFullAlpha = dimImage.color.a;

        cards = new UpgradeCardWidget[cardCount];
        cardIntros = new ShakeGrowImpactAnimation[cardCount];

        for (int i = 0; i < cardCount; i++)
        {
            cards[i] = Instantiate(cardPrefab, cardPrefab.transform.parent);
            cardIntros[i] = cards[i].GetComponent<ShakeGrowImpactAnimation>();

            // Instantiate clones cardPrefab's CURRENT active state (which is active, so the
            // template shows correctly in the Editor) - without this, every clone briefly shows
            // the template's own placeholder content (whatever's authored on it) until the next
            // QUpdate's Refresh() actually calls Setup with real data.
            cards[i].gameObject.SetActive(false);
        }

        cardPrefab.gameObject.SetActive(false);

        for (int i = 0; i < cards.Length; i++)
        {
            int index = i; // capture by value, not by the loop variable
            cards[i].onClicked += _ =>
            {
                PlayCardChoose();
                onCardClicked?.Invoke(index);
            };
        }

        weaponCards = new WeaponCardWidget[weaponCardCount];
        weaponCardIntros = new ShakeGrowImpactAnimation[weaponCardCount];

        for (int i = 0; i < weaponCardCount; i++)
        {
            weaponCards[i] = Instantiate(weaponCardPrefab, weaponCardPrefab.transform.parent);
            weaponCardIntros[i] = weaponCards[i].GetComponent<ShakeGrowImpactAnimation>();

            // Same reasoning as the cards[] loop above - avoid a frame of the template's own
            // placeholder content before the first RefreshWeaponChoice() call.
            weaponCards[i].gameObject.SetActive(false);
        }

        weaponCardPrefab.gameObject.SetActive(false);

        for (int i = 0; i < weaponCards.Length; i++)
        {
            int index = i; // capture by value, not by the loop variable
            weaponCards[i].onClicked += _ =>
            {
                PlayCardChoose();
                onWeaponCardClicked?.Invoke(index);
            };
        }

        if (rerollButton != null)
            rerollButton.onClick.AddListener(() => onRerollClicked?.Invoke());

        if (secondaryButton != null)
        {
            _secondaryButtonText = secondaryButton.GetComponentInChildren<TMP_Text>(true);
            secondaryButton.onClick.AddListener(() => onSecondaryButtonClicked?.Invoke());
        }
    }

    // Fires once per screen-open (WindowManager.ShowWindow<ChooseWindow>() for Level-Up, or a
    // direct call from GameplayUiController.UpdatePoiWindow for Cursed Rift/Store/Blacksmith, calls
    // this exactly once per reveal) - Refresh/RefreshWeaponChoice/RefreshStore run every QUpdate tick afterwards
    // purely to push data/countdown, so the intro has to live here instead of there. introParticles
    // are force-deactivated BEFORE base.Show() activates the whole popup - see their own field
    // comment for why the ordering matters.
    public override void Show()
    {
        ResetIntroParticles();
        base.Show();
        PlayIntroAnimation();
    }

    private void ResetIntroParticles()
    {
        if (introParticles == null)
            return;

        foreach (GameObject particle in introParticles)
        {
            if (particle != null)
                particle.SetActive(false);
        }
    }

    // Dim fades in first (black overlay behind the popup), then the title plays its own
    // ShakeGrowImpactAnimation (which fires introParticles itself, via its onImpact UnityEvent) while
    // every card - both families, whichever ends up active is decided by the next
    // Refresh/RefreshWeaponChoice call - plays its own, staggered one after another. This method only
    // computes WHEN each one starts (plain float math) and calls Play(delay) on it - none of the
    // shake/grow/impact tweening (or particle triggering) happens here, see ShakeGrowImpactAnimation.
    // Every delay below is unscaled-time-relative because GameplayUiController ramps Time.timeScale
    // down while a real Level-Up shows this window (see UpdateUpgradeScreen), which would otherwise
    // slow the intro to a crawl - Cursed Rift never touches Time.timeScale at all, so this is a no-op
    // distinction for that flow, but the unscaled basis is correct either way.
    private void PlayIntroAnimation()
    {
        _dimTween.Stop();

        float t = introStartDelay;

        if (dimImage != null)
        {
            Color color = dimImage.color;
            color.a = 0f;
            dimImage.color = color;

            _dimTween = Tween.Alpha(dimImage, 0f, _dimFullAlpha, dimFadeDuration, startDelay: t, useUnscaledTime: true);
            t += dimFadeDuration;
        }

        t += titleRevealDelay;
        float titleStart = t;

        if (titleIntro != null)
            titleIntro.Play(titleStart);

        float cardsStart = titleStart + cardIntroStartDelay;

        for (int i = 0; i < cards.Length; i++)
        {
            float at = cardsStart + i * cardIntroStagger;
            cardIntros[i]?.Play(at);
            PlayCardAppear(cards[i] != null && cards[i].gameObject.activeSelf, at);
        }

        for (int i = 0; i < weaponCards.Length; i++)
        {
            float at = cardsStart + i * cardIntroStagger;
            weaponCardIntros[i]?.Play(at);
            PlayCardAppear(weaponCards[i] != null && weaponCards[i].gameObject.activeSelf, at);
        }
    }

    // Scheduled through the audio system's own delay rather than a coroutine, so it shares the
    // voice's unscaled clock - a Level-Up screen ramps Time.timeScale down match-wide, and a
    // coroutine-timed sound would drift away from the card it is supposed to accompany.
    //
    // Gated on the card actually being visible: these arrays are fixed-size (cardCount /
    // weaponCardCount) and a given screen may show fewer, or none of one family - Store is the only
    // screen that shows both. Without this, a 3-slot array would fire three sounds for one card.
    private void PlayCardAppear(bool visible, float delay)
    {
        if (visible == false || cardAppearSound == null)
            return;

        AudioManager.Play(cardAppearSound, 1f, delay);
    }

    private void PlayCardChoose()
    {
        if (cardChooseSound != null)
            AudioManager.Play(cardChooseSound);
    }

    // Replays the intro exactly the way GameplayUiController's real trigger does (Show() is what
    // fires on every reveal, see the override above) - lets you tune timings in Play Mode without
    // needing to actually trigger a real Level-Up/Cursed Rift. Select this component while playing,
    // then use the Inspector's context menu / NaughtyAttributes button.
    [Button]
    public void TestIntroAnimation()
    {
        Show();
    }

    // confirmedIndex is null while this client hasn't picked yet; once set, every card is locked
    // out (no more clicks can change the pick - see LevelUpUtility.ConfirmSelection on the sim side,
    // which already rejects a second click, but disabling the buttons here avoids a dead click in
    // the first place).
    // allowReroll defaults true (reproduces the original Level-Up behavior with zero call-site
    // changes at UpdateUpgradeScreen) - Cursed Rift passes false for BOTH its own stages
    // (RefreshCursedRiftWindow), since redrawing options makes no sense for a Sacrifice/Mutation
    // pick and RerollLevelUpOptionsCommand has no meaning outside a real LevelUpChoice anyway.
    public void Refresh(string title, float timeRemaining, UpgradeCardWidget.CardData[] cardData, int? confirmedIndex, string subtitle = null, bool allowCancel = false, bool allowReroll = true)
    {
        SetCardFamilyActive(showCards: true, showWeaponCards: false);
        RefreshTitle(title);
        RefreshSubtitle(subtitle);
        RefreshCountdown(timeRemaining);
        SetSecondaryButtonActive(allowCancel, "CANCEL");
        SetRerollButtonActive(allowReroll);

        bool interactable = confirmedIndex.HasValue == false;

        for (int i = 0; i < cards.Length; i++)
        {
            UpgradeCardWidget.CardData data = i < cardData.Length ? cardData[i] : default;
            cards[i].Setup(data, interactable);
        }
    }

    // Same shape as Refresh above, for a screen whose options are all LevelUpPoolKind.ChooseWeapon
    // (see GameplayUiController.UpdateUpgradeScreen). secondaryButton always shows here ("KEEP
    // CURRENT") - all 3 weaponCards stay real rolled weapons (see
    // LevelUpUtility.RollChooseWeaponOptionsFor), the button is the sole way to decline them.
    // Reroll always shows too - only Cursed Rift's own Refresh calls ever hide it.
    public void RefreshWeaponChoice(string title, float timeRemaining, WeaponCardWidget.CardData[] cardData, int? confirmedIndex, string subtitle = null)
    {
        SetCardFamilyActive(showCards: false, showWeaponCards: true);
        RefreshTitle(title);
        RefreshSubtitle(subtitle);
        RefreshCountdown(timeRemaining);
        SetSecondaryButtonActive(true, "KEEP CURRENT");
        SetRerollButtonActive(true);

        bool interactable = confirmedIndex.HasValue == false;

        for (int i = 0; i < weaponCards.Length; i++)
        {
            WeaponCardWidget.CardData data = i < cardData.Length ? cardData[i] : default;
            weaponCards[i].Setup(data, interactable);
        }

        if (secondaryButton != null)
            secondaryButton.interactable = interactable;
    }

    private void SetSecondaryButtonActive(bool active, string label)
    {
        if (secondaryButton == null)
            return;

        secondaryButton.gameObject.SetActive(active);

        if (active && _secondaryButtonText != null)
            _secondaryButtonText.text = label;
    }

    // UpdateRerollButton (below) only ever sets .interactable/.text, never visibility - that's
    // owned here instead, since it has to flip per-screen (Level-Up/Choose-Weapon: on; Cursed
    // Rift: off) rather than every tick alongside charge count.
    private void SetRerollButtonActive(bool active)
    {
        if (rerollButton != null)
            rerollButton.gameObject.SetActive(active);

        if (rerollChargesText != null)
            rerollChargesText.gameObject.SetActive(active);
    }

    // Called every QUpdate tick alongside Refresh/RefreshWeaponChoice (see
    // GameplayUiController.UpdateUpgradeScreen) - independent of which card family is showing, and
    // independent of confirmedIndex's own interactable gating on the cards themselves, since
    // charges and confirmation are two separate reasons the button can be disabled.
    public void UpdateRerollButton(int charges, bool confirmed)
    {
        if (rerollChargesText != null)
            rerollChargesText.text = charges.ToString();

        if (rerollButton != null)
            rerollButton.interactable = charges > 0 && confirmed == false;
    }

    private void RefreshTitle(string title)
    {
        if (titleText != null)
            titleText.text = title;
    }

    // subtitle == null (every existing Level-Up/Weapon-Upgrade/Chest call site) leaves whatever
    // is already authored on the prefab untouched rather than blanking it - only an explicit
    // empty string clears it. Cursed Rift always passes a real subtitle.
    private void RefreshSubtitle(string subtitle)
    {
        if (subtitleText != null && subtitle != null)
            subtitleText.text = subtitle;
    }

    private void RefreshCountdown(float timeRemaining)
    {
        if (countdownText != null)
            countdownText.text = Mathf.CeilToInt(Mathf.Max(timeRemaining, 0f)).ToString();
    }

    // showCards/showWeaponCards are independently settable (not one boolean, unlike the old
    // showWeaponCards-only signature) - Store's own screen (RefreshStore below) is the one place
    // both families are ever live together, food/utility cards above the weapon offers row (per
    // the user's own layout decision - see docs/store-blacksmith.md).
    private void SetCardFamilyActive(bool showCards, bool showWeaponCards)
    {
        for (int i = 0; i < cards.Length; i++)
            cards[i].gameObject.SetActive(showCards);

        for (int i = 0; i < weaponCards.Length; i++)
            weaponCards[i].gameObject.SetActive(showWeaponCards);
    }

    // Store's own screen - food/utility offers (cards[]) AND weapon offers (weaponCards[]) shown
    // at once, food/utility listed first per the user's own layout decision. Unlike every other
    // screen, cards here are never "confirmed/locked out" the way a one-shot pick is - any number
    // of purchases can land while this window stays open, so `interactable` is unconditionally true
    // here; per-offer affordability/sold-out gating lives entirely in each CardData's own
    // PurchasableCardState (see PurchasableCardUi.Apply).
    // One-shot, so a mis-sized window reports once rather than every frame the Store is open.
    private bool _warnedCardShortfall;

    public void RefreshStore(string title, UpgradeCardWidget.CardData[] foodData, WeaponCardWidget.CardData[] weaponData, string subtitle = null)
    {
        SetCardFamilyActive(showCards: true, showWeaponCards: true);
        RefreshTitle(title);
        RefreshSubtitle(subtitle);
        RefreshCountdown(0f);
        SetSecondaryButtonActive(true, "CLOSE");
        SetRerollButtonActive(false);

        // Store is the one screen that can be handed MORE card data than this window was authored
        // with (its rolled food offers plus two guaranteed, never-rolled offers - Increase Weapon
        // Level, and the Accessory Repair/Replacement service). Silently dropping the overflow is
        // exactly how the accessory card went missing once already, so say so instead: the fix is
        // always to raise cardCount on this instance.
        if (foodData.Length > cards.Length && _warnedCardShortfall == false)
        {
            _warnedCardShortfall = true;
            LogHelper.Warn("ChooseWindow", $"{name} has cardCount {cards.Length} but the Store needs " +
                $"{foodData.Length} card slots - the last {foodData.Length - cards.Length} offer(s) " +
                "(e.g. the Accessory Repair/Replacement service) will never be shown. Raise cardCount.", this);
        }

        for (int i = 0; i < cards.Length; i++)
        {
            UpgradeCardWidget.CardData data = i < foodData.Length ? foodData[i] : default;
            cards[i].Setup(data, interactable: true);
        }

        for (int i = 0; i < weaponCards.Length; i++)
        {
            WeaponCardWidget.CardData data = i < weaponData.Length ? weaponData[i] : default;
            weaponCards[i].Setup(data, interactable: true);
        }
    }
}
