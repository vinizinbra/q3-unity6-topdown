using UnityEngine;
using UnityEngine.UI;

// A slider that trails another one - the classic "recent damage" bar. Sits behind the watched
// slider, holds still for drainDelay after it drops, then drains down to meet it. Snaps straight up
// when the watched slider rises, so a heal reads as an immediate gain rather than a slow fill.
//
// Watches by polling rather than by a hook, so whatever drives the watched slider (CharacterUiWidget
// and friends) needs to know nothing about this - drop it on any trailing bar and point it at one.
[DefaultExecutionOrder(10)]
public class DelayedSliderWidget : MonoBehaviour
{
    [SerializeField, Tooltip("The trailing slider this drives - the one that lags behind.")]
    private Slider selfSlider;
    [SerializeField, Tooltip("The slider to follow.")]
    private Slider watchedSlider;

    [SerializeField, Tooltip("Seconds the bar holds still after the watched slider drops before it starts draining.")]
    private float drainDelay = 0.5f;
    [SerializeField, Tooltip("How fast the bar drains toward the watched value, in slider units per second.")]
    private float drainSpeed = 1.5f;

    private float _lastWatchedValue;
    private float _drainTimer;
    private bool _hasSnapped;

    private void OnEnable()
    {
        _hasSnapped = false;
    }

    private void LateUpdate()
    {
        if (selfSlider == null || watchedSlider == null)
            return;

        float watchedValue = watchedSlider.value;

        // Spawned onto an already-damaged entity, the first real value would otherwise read as a
        // fresh hit and drain for it.
        if (_hasSnapped == false)
        {
            Snap(watchedValue);
            return;
        }

        if (watchedValue < _lastWatchedValue)
        {
            _drainTimer = drainDelay;
        }
        else if (watchedValue > selfSlider.value)
        {
            selfSlider.value = watchedValue;
            _drainTimer = 0f;
        }

        if (_drainTimer > 0f)
            _drainTimer -= Time.deltaTime;
        else
            selfSlider.value = Mathf.MoveTowards(selfSlider.value, watchedValue, drainSpeed * Time.deltaTime);

        _lastWatchedValue = watchedValue;
    }

    private void Snap(float value)
    {
        selfSlider.value = value;
        _lastWatchedValue = value;
        _drainTimer = 0f;
        _hasSnapped = true;
    }
}
