using UnityEngine;

// Simple standalone idle "breathing" animation for a decorative lobby character/mascot - scales
// and bobs a chest, a head, and an arbitrary number of optional leg Transforms, all on the same
// scale-pulse-plus-bob style (just with their own amount/phase per part), rather than legs getting
// a distinct sway/rotation treatment. Deliberately independent of BlobAnimationView/Quantum: this
// is for a static prop sitting in the lobby scene (a mascot, a menu-background character) with no
// gameplay rig or simulation behind it, not the hero preview CharacterPreviewWidget already
// renders (that one gets its own idle breathing for free via BlobAnimationView.TickPreview). The
// leg array (rather than BlobAnimationView's fixed legLeft/legRight pair) exists specifically to
// cover a character like Lux, whose rig has several mechanical floating legs instead of two.
public class IdleBreatheWidget : MonoBehaviour
{
    [SerializeField, Tooltip("Rises/compresses vertically on the breathing wave. Optional - leave unassigned to skip.")]
    private Transform chest;

    [SerializeField, Tooltip("Follows the same wave as chest, scaled down by headInfluence. Optional - leave unassigned to skip.")]
    private Transform head;

    [SerializeField, Tooltip("Breaths per second.")]
    private float breatheFrequency = 1.2f;

    [SerializeField, Tooltip("Peak scale offset from rest (e.g. 0.05 = chest swells to 105% / shrinks to 95%).")]
    private float breatheAmount = 0.05f;

    [SerializeField, Tooltip("Peak vertical bob offset, in local units, applied to both chest and head on the same wave.")]
    private float bobAmount = 0.03f;

    [SerializeField, Range(0f, 1f), Tooltip("How much of chest's breathing amount the head also gets - a head breathing as hard as the chest reads as bobblehead.")]
    private float headInfluence = 0.4f;

    [System.Serializable]
    private class LegConfig
    {
        [Tooltip("Leg Transform this config drives. Left unassigned, the entry is skipped.")]
        public Transform transform;

        [Tooltip("Multiplies the shared breatheFrequency above - 1 = same speed as chest/head, <1 slower, >1 faster.")]
        public float frequencyMultiplier = 1f;

        [Tooltip("Peak scale offset from rest, same meaning as breatheAmount above.")]
        public float breatheAmount = 0.05f;

        [Tooltip("Peak vertical bob offset, in local units.")]
        public float bobAmount = 0.03f;

        [Range(0f, 1f), Tooltip("Phase offset from the shared wave, as a fraction of a full cycle - lets legs move out of sync with each other instead of pulsing together.")]
        public float phaseOffset;
    }

    [Header("Legs (optional)")]
    [SerializeField, Tooltip("Optional legs - any number, individual entries with no Transform assigned are skipped. Each gets the same scale-pulse + bob treatment as chest/head (e.g. Lux's mechanical floating legs), but with its own frequency/amount/phase so legs don't have to move identically.")]
    private LegConfig[] legs = System.Array.Empty<LegConfig>();

    private Vector3 _chestBaseScale, _chestBasePos;
    private Vector3 _headBaseScale, _headBasePos;
    private Vector3[] _legBaseScale;
    private Vector3[] _legBasePos;

    // Randomized per instance so several mascots placed side by side don't breathe in lockstep.
    private float _phaseOffset;

    private void Awake()
    {
        _phaseOffset = Random.value * Mathf.PI * 2f;

        if (chest != null) { _chestBaseScale = chest.localScale; _chestBasePos = chest.localPosition; }
        if (head != null) { _headBaseScale = head.localScale; _headBasePos = head.localPosition; }

        _legBaseScale = new Vector3[legs.Length];
        _legBasePos = new Vector3[legs.Length];
        for (int i = 0; i < legs.Length; i++)
        {
            if (legs[i]?.transform == null) continue;
            _legBaseScale[i] = legs[i].transform.localScale;
            _legBasePos[i] = legs[i].transform.localPosition;
        }
    }

    private void Update()
    {
        float basePhase = Time.time * breatheFrequency * Mathf.PI * 2f + _phaseOffset;
        float wave = Mathf.Sin(basePhase);

        ApplyBreathe(chest, _chestBaseScale, _chestBasePos, wave * breatheAmount, wave * bobAmount);
        ApplyBreathe(head, _headBaseScale, _headBasePos, wave * breatheAmount * headInfluence, wave * bobAmount);

        for (int i = 0; i < legs.Length; i++)
        {
            LegConfig leg = legs[i];
            if (leg?.transform == null) continue;

            float legWave = Mathf.Sin(Time.time * breatheFrequency * leg.frequencyMultiplier * Mathf.PI * 2f + _phaseOffset + leg.phaseOffset * Mathf.PI * 2f);
            ApplyBreathe(leg.transform, _legBaseScale[i], _legBasePos[i], legWave * leg.breatheAmount, legWave * leg.bobAmount);
        }
    }

    // Shared scale-pulse + bob applied to a single part: squashes/stretches vertically (with a
    // matching horizontal counter-scale so volume feels preserved) and bobs on the Y axis, both
    // driven by the same wave value so a part's scale and position always peak together.
    private static void ApplyBreathe(Transform part, Vector3 baseScale, Vector3 basePos, float breathe, float bob)
    {
        if (part == null)
            return;

        part.localScale = Vector3.Scale(baseScale, new Vector3(1f - breathe * 0.5f, 1f + breathe, 1f - breathe * 0.5f));

        var pos = basePos;
        pos.y += bob;
        part.localPosition = pos;
    }
}
