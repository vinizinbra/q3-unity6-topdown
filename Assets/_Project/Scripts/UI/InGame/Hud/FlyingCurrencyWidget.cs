using System;
using UnityEngine;

// One pooled flying-pickup sprite, driven by FlyingCurrencyManager - world-space (SpriteRenderer),
// not UI. Opens with a brief random-direction "pop" (scatterDuration) so it visibly detaches from
// the spawn point instead of just creeping in place, then homes toward its target's LIVE position
// every frame (Vector3.Lerp with an exponential ease-toward-target factor, same idiom
// WeaponViewController/FollowCamera already use elsewhere in this codebase) rather than tweening
// between two fixed points - the target is the collecting character, which can be moving throughout
// the flight, unlike the old UI-space FlyingXpWidget (which flew to a static exp-bar RectTransform
// and could get away with a fixed-endpoint tween). resolveTarget is a Func, not a captured
// Transform, so a despawned/respawning character mid-flight re-resolves cleanly instead of chasing
// a stale/destroyed reference. A plain fixed exponential factor only ever settles into a steady lag
// distance behind a moving target rather than truly closing the gap, so homingSpeed itself ramps up
// via lateAggressionMultiplier as _elapsed approaches maxLifetime - otherwise a player running fast
// enough can outrun the base closing speed indefinitely and the pickup only ever finishes via the
// maxLifetime safety cutoff, well off the character instead of visibly landing on them.
public class FlyingCurrencyWidget : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Header("Initial pop")]
    [SerializeField, Tooltip("Random-direction straight-line 'pop' before homing kicks in - without it the pickup barely seems to move off its spawn point.")]
    private float scatterDuration = 0.15f;
    [SerializeField] private float scatterSpeedMin = 2f;
    [SerializeField] private float scatterSpeedMax = 4f;

    [Header("Homing")]
    [SerializeField, Tooltip("How quickly this eases toward the live target position each frame - higher = snappier/more direct, lower = more of a lagging chase.")]
    private float homingSpeed = 8f;
    [SerializeField, Tooltip("An exponential ease-toward-target settles into a steady lag distance behind a MOVING target (roughly targetSpeed / homingSpeed) rather than actually closing to zero - if the player outruns that base closing speed, this multiplies homingSpeed up to this factor by the time maxLifetime is reached, so it always reels itself in well before the safety cutoff instead of trailing indefinitely and then finishing off-target.")]
    private float lateAggressionMultiplier = 4f;
    [SerializeField, Tooltip("Finishes once within this distance of the target.")]
    private float arrivalDistance = 0.3f;
    [SerializeField, Tooltip("Safety cap in case the target never resolves (entity despawned before this arrived) - finishes anyway instead of flying forever.")]
    private float maxLifetime = 2f;
    [SerializeField, Tooltip("Added to the target's Y so this homes toward roughly chest height instead of the character root, which sits at its feet.")]
    private float targetHeightOffset = 0.5f;

    private Func<Transform> _resolveTarget;
    private Action<FlyingCurrencyWidget> _onArrived;
    private Vector3 _scatterVelocity;
    private float _elapsed;
    private bool _isPlaying;

    public void Play(Sprite sprite, Vector3 worldPosition, Func<Transform> resolveTarget, Action<FlyingCurrencyWidget> onArrived)
    {
        if (spriteRenderer != null)
            spriteRenderer.sprite = sprite;

        transform.position = worldPosition;
        _resolveTarget = resolveTarget;
        _onArrived = onArrived;
        _elapsed = 0f;
        _isPlaying = true;

        // Ground plane is XZ (see EnemyMovementUtility.RandomPositionInRing) - scatter sideways,
        // not vertically, so it reads as a pop on the top-down plane instead of a hop in place.
        float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        float speed = UnityEngine.Random.Range(scatterSpeedMin, scatterSpeedMax);
        _scatterVelocity = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * speed;
    }

    private void Update()
    {
        if (_isPlaying == false)
            return;

        _elapsed += Time.unscaledDeltaTime;

        if (_elapsed < scatterDuration)
        {
            transform.position += _scatterVelocity * Time.unscaledDeltaTime;
            return;
        }

        Transform target = _resolveTarget?.Invoke();

        if (target == null)
        {
            if (_elapsed >= maxLifetime)
                Finish();
            return;
        }

        Vector3 targetPosition = target.position + new Vector3(0f, targetHeightOffset, 0f);

        float lifetimeFraction = Mathf.Clamp01(_elapsed / maxLifetime);
        float effectiveHomingSpeed = homingSpeed * Mathf.Lerp(1f, lateAggressionMultiplier, lifetimeFraction);
        float t = 1f - Mathf.Exp(-effectiveHomingSpeed * Time.unscaledDeltaTime);
        transform.position = Vector3.Lerp(transform.position, targetPosition, t);

        bool arrived = (transform.position - targetPosition).sqrMagnitude <= arrivalDistance * arrivalDistance;

        if (arrived || _elapsed >= maxLifetime)
            Finish();
    }

    private void Finish()
    {
        _isPlaying = false;

        Action<FlyingCurrencyWidget> onArrived = _onArrived;
        _onArrived = null;
        _resolveTarget = null;

        onArrived?.Invoke(this);
    }
}
