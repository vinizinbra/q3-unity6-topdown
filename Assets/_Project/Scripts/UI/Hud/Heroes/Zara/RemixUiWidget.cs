using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;

// Remix's progress display: how many pulses until the next randomly-chosen HitEffectData proc (see
// ResonanceUtility.ResolveRemixEffect - every 3rd pulse). Same icon+text stack shape as
// AdrenalineUiWidget/ScrapUiWidget, not SkillProgressUiWidget's fill slider - that one's meant to
// wrap HeroSkillUiWidget's own icon (charge-up toward a Hero Skill's bonus state), while Remix is a
// Passive Ascension's own periodic counter. Whole widget self-hides via root when Remix hasn't been
// taken - Resonance itself always exists once the base passive is applied, ascension or not, so
// presence alone can't gate this the way LuxScrapCollector/Adrenaline's own components do; RemixEffects
// being empty (its own "not taken" default, see RemixPassiveUpgradeData.Apply) is what's checked instead.
// By default self-binds to local slot 0 (player 1), same as SkillCooldownUiWidget - see
// autoBindLocalPlayerOne.
public class RemixUiWidget : QuantumGlobalMonoBehaviour
{
    private const int PulsesPerTrigger = 3;

    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text pulsesText;

    [SerializeField, Tooltip("On: binds itself to local slot 0 (player 1) automatically. Off: stays unbound until something else calls Initialize (e.g. the party HUD).")]
    private bool autoBindLocalPlayerOne = true;

    [SerializeField] private EntityRef _entityRef;

    private void Start()
    {
        if (autoBindLocalPlayerOne)
            MyLocalPlayer.Instance.BindToSlot(0, Initialize);
    }

    public void Initialize(EntityRef entityRef)
    {
        _entityRef = entityRef;
    }

    // Called by PartyHudWidget on every widget it owns, so an externally-driven slot
    // never fights its own children's default self-binding - see the class comment above.
    public void DisableAutoBind()
    {
        autoBindLocalPlayerOne = false;
    }

    public override void QStart(QuantumGame game)
    {
    }

    public override void QLateUpdate(QuantumGame game)
    {
    }

    public override void QUpdate(QuantumGame game)
    {
        var frame = game.Frames.Predicted;

        if (frame.Has<Resonance>(_entityRef) == false)
        {
            SetShown(false);
            return;
        }

        Resonance resonance = frame.Get<Resonance>(_entityRef);

        if (resonance.RemixEffects[0].IsValid == false)
        {
            SetShown(false);
            return;
        }

        SetShown(true);

        if (pulsesText != null)
            pulsesText.text = $"{resonance.PulseCount % PulsesPerTrigger}/{PulsesPerTrigger}";
    }

    private void SetShown(bool shown)
    {
        if (root != null && root.activeSelf != shown)
            root.SetActive(shown);
    }
}
