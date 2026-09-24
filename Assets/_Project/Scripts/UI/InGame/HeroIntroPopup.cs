using Playtime.Core;
using Quantum;
using QuantumUser.View;
using UnityEngine;

// Solo-only "meet your hero" popup, shown once per hero the instant a run starts (Lobby->Survival -
// same hook HowToPlayPopup uses, see InMatchTutorialManager.OpenHeroIntro) - pauses the sim
// (TutorialPopup) while a read-only HeroInfoWidget snapshot of whatever the local player is running
// is shown. Continue is the base UiPopup.closeButton, wired in the Inspector like every other
// tutorial popup. "Don't Show This Again" additionally persists a per-hero PlayerPrefBool (keyed off
// the equipped CharacterData's own AssetGuid, see HasBeenSeen/MarkSeen) before closing, so it never
// reappears for THAT hero specifically but still shows again if the player switches to one they
// haven't seen it for yet.
public class HeroIntroPopup : TutorialPopup
{
    [SerializeField] private HeroInfoWidget heroInfoWidget;

    // Fully-qualified: Quantum.Button (this file's "using Quantum;") also has this name.
    [SerializeField, Tooltip("Wired to DontShowAgain() instead of the base closeButton, so it also persists the per-hero pref before closing.")]
    private UnityEngine.UI.Button dontShowAgainButton;

    private AssetRef<CharacterData> _characterData;

    public override void Awake()
    {
        base.Awake();

        if (dontShowAgainButton != null)
            dontShowAgainButton.onClick.AddListener(DontShowAgain);
    }

    // Called by InMatchTutorialManager right after opening this popup - it already resolved the
    // local player's entity/CharacterData to run HasBeenSeen, so it hands both straight in instead
    // of this popup re-resolving them itself.
    public void Setup(EntityRef entityRef, AssetRef<CharacterData> characterData)
    {
        _characterData = characterData;

        if (heroInfoWidget != null)
            heroInfoWidget.Initialize(entityRef);
    }

    private void DontShowAgain()
    {
        MarkSeen(_characterData);
        Close();
    }

    public static bool HasBeenSeen(AssetRef<CharacterData> characterData)
    {
        return characterData.IsValid && new PlayerPrefBool(SeenKey(characterData), false).Value;
    }

    private static void MarkSeen(AssetRef<CharacterData> characterData)
    {
        if (characterData.IsValid == false)
            return;

        new PlayerPrefBool(SeenKey(characterData), false).Value = true;
    }

    private static string SeenKey(AssetRef<CharacterData> characterData)
    {
        return $"hero_intro_seen_{characterData.Id.Value}" + LocalClientIdentity.PrefSuffix;
    }
}
