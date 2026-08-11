using System;
using NaughtyAttributes;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Level-up upgrade-choice screen - shown while Frame.Global.LevelUpScreenOpen is true (see
// GameplayUiController.QUpdate, which drives this window and owns all Quantum-facing reads). Just
// orchestrates an array of UpgradeCardWidget children, one per LevelUpChoice.Options slot - this
// class itself has no Quantum dependency either.
//
// Also owns a parallel array of WeaponCardWidget children for a Choose-Weapon screen (see
// Refresh/RefreshWeaponChoice) - a given LevelUpChoice is always homogeneous (every rolled option
// is ChooseWeapon, or none are, see LevelUpUtility.RollOptionsFor), so only one of the two card
// families is ever shown at a time; the other is deactivated for the duration of that screen.
public class UpgradeWindow : UiWindow
{
    // A hidden template, not a live card itself - Awake clones it cardCount times under the same
    // parent (so the scene only needs one hand-authored card) then disables the template, leaving
    // only the clones live. cardCount must match LevelUpChoice.Options' fixed size (3) - see
    // LevelUp.qtn.
    [SerializeField] private UpgradeCardWidget cardPrefab;
    [SerializeField] private int cardCount = 3;

    // Same clone-then-disable-the-template shape as cardPrefab/cardCount above, for a
    // Choose-Weapon screen - see WeaponCardWidget.
    [SerializeField] private WeaponCardWidget weaponCardPrefab;
    [SerializeField] private int weaponCardCount = 3;

    [SerializeField] private TMP_Text countdownText;

    // "Level Up!" for a plain level-up, or the rolled category's display name (e.g. "Weapon Perk")
    // for a Chest - see GameplayUiController.BuildTitle, which owns the actual wording.
    [SerializeField] private TMP_Text titleText;

    // Shared by both card families (same button regardless of whether cards or weaponCards is
    // currently active) - redraws whichever is showing, see GameplayUiController.OnRerollClicked /
    // LevelUpUtility.RerollOptionsFor. rerollChargesText shows the player's own remaining
    // CharacterStats.RerollQuantity; the button itself is disabled at 0 (same "interactable, not
    // hidden" convention as the cards once a pick is confirmed).
    [Header("Reroll")]
    [SerializeField] private Button rerollButton;
    [SerializeField] private TMP_Text rerollChargesText;

    // Only shown on a Choose-Weapon screen (RefreshWeaponChoice) - hidden entirely, not just
    // disabled, for every other category (Refresh), since "keep current weapon" has no meaning
    // there. Declining picks nothing, still counts as confirmed - see
    // LevelUpUtility.ConfirmKeepCurrent/KeepCurrentWeaponCommand. All 3 weaponCards stay real
    // rolled weapons - this button is the only way to decline, not a 4th card.
    [Header("Keep Current (Choose-Weapon screens only)")]
    [SerializeField] private Button keepCurrentButton;

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
    [SerializeField, Tooltip("Particles that must NOT play just because the window opened - reset to inactive every Show(), and only meant to be reactivated by an explicit trigger (e.g. titleIntro.onImpact -> SetActive(true)).")]
    private GameObject[] introParticles;

    // Raised with a card's index (0-based, matching LevelUpChoice.Options) when clicked -
    // GameplayUiController forwards this into a SelectLevelUpUpgradeCommand. Shared by both card
    // families - a click is just a slot index regardless of which kind of card it came from.
    public Action<int> onCardClicked;

    // Raised when rerollButton is clicked - GameplayUiController forwards this into a
    // RerollLevelUpOptionsCommand. No index needed, unlike onCardClicked - a reroll redraws every
    // option at once, not one slot.
    public Action onRerollClicked;

    // Raised when keepCurrentButton is clicked - GameplayUiController forwards this into a
    // KeepCurrentWeaponCommand. No index needed - it isn't one of the 3 weaponCards.
    public Action onKeepCurrentClicked;

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
            cards[i].onClicked += _ => onCardClicked?.Invoke(index);
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
            weaponCards[i].onClicked += _ => onCardClicked?.Invoke(index);
        }

        if (rerollButton != null)
            rerollButton.onClick.AddListener(() => onRerollClicked?.Invoke());

        if (keepCurrentButton != null)
            keepCurrentButton.onClick.AddListener(() => onKeepCurrentClicked?.Invoke());
    }

    // Fires once per screen-open (WindowManager.ShowWindow<UpgradeWindow>() calls this exactly
    // once per LevelUpScreenOpen transition) - Refresh/RefreshWeaponChoice run every QUpdate tick
    // afterwards purely to push data/countdown, so the intro has to live here instead of there.
    // introParticles are force-deactivated BEFORE base.Show() activates the whole popup - see their
    // own field comment for why the ordering matters.
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
    // down while this window shows (see UpdateUpgradeScreen), which would otherwise slow the intro to
    // a crawl.
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
            cardIntros[i]?.Play(cardsStart + i * cardIntroStagger);

        for (int i = 0; i < weaponCards.Length; i++)
            weaponCardIntros[i]?.Play(cardsStart + i * cardIntroStagger);
    }

    // Replays the intro exactly the way GameplayUiController's real trigger does (Show() is what
    // fires on every LevelUpScreenOpen transition, see the override above) - lets you tune timings
    // in Play Mode without needing to actually level up. Select this component while playing, then
    // use the Inspector's context menu / NaughtyAttributes button.
    [Button]
    public void TestIntroAnimation()
    {
        Show();
    }

    // confirmedIndex is null while this client hasn't picked yet; once set, every card is locked
    // out (no more clicks can change the pick - see LevelUpUtility.ConfirmSelection on the sim side,
    // which already rejects a second click, but disabling the buttons here avoids a dead click in
    // the first place).
    public void Refresh(string title, float timeRemaining, UpgradeCardWidget.CardData[] cardData, int? confirmedIndex)
    {
        SetCardFamilyActive(showWeaponCards: false);
        SetKeepCurrentButtonActive(false);
        RefreshTitle(title);
        RefreshCountdown(timeRemaining);

        bool interactable = confirmedIndex.HasValue == false;

        for (int i = 0; i < cards.Length; i++)
        {
            UpgradeCardWidget.CardData data = i < cardData.Length ? cardData[i] : default;
            cards[i].Setup(data, interactable);
        }
    }

    // Same shape as Refresh above, for a screen whose options are all LevelUpPoolKind.ChooseWeapon
    // (see GameplayUiController.UpdateUpgradeScreen). keepCurrentButton only ever appears here - all
    // 3 weaponCards stay real rolled weapons (see LevelUpUtility.RollChooseWeaponOptionsFor), the
    // button is the sole way to decline them.
    public void RefreshWeaponChoice(string title, float timeRemaining, WeaponCardWidget.CardData[] cardData, int? confirmedIndex)
    {
        SetCardFamilyActive(showWeaponCards: true);
        SetKeepCurrentButtonActive(true);
        RefreshTitle(title);
        RefreshCountdown(timeRemaining);

        bool interactable = confirmedIndex.HasValue == false;

        for (int i = 0; i < weaponCards.Length; i++)
        {
            WeaponCardWidget.CardData data = i < cardData.Length ? cardData[i] : default;
            weaponCards[i].Setup(data, interactable);
        }

        if (keepCurrentButton != null)
            keepCurrentButton.interactable = interactable;
    }

    private void SetKeepCurrentButtonActive(bool active)
    {
        if (keepCurrentButton != null)
            keepCurrentButton.gameObject.SetActive(active);
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

    private void RefreshCountdown(float timeRemaining)
    {
        if (countdownText != null)
            countdownText.text = Mathf.CeilToInt(Mathf.Max(timeRemaining, 0f)).ToString();
    }

    private void SetCardFamilyActive(bool showWeaponCards)
    {
        for (int i = 0; i < cards.Length; i++)
            cards[i].gameObject.SetActive(showWeaponCards == false);

        for (int i = 0; i < weaponCards.Length; i++)
            weaponCards[i].gameObject.SetActive(showWeaponCards);
    }
}
