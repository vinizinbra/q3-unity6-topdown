using UnityEngine;

// Put this next to an Animator and add Animation Events that call PlaySound, passing an AudioClip as
// the event's Object parameter. Used by the bootstrap logo animation to time its sounds.
[RequireComponent(typeof(AudioSource))]
public class AnimationEventAudio : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] private float _volume = 1f;

    private AudioSource _audioSource;

    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        _audioSource.playOnAwake = false;
    }

    // Animation Event target.
    public void PlaySound(AudioClip clip)
    {
        if (clip != null)
            _audioSource.PlayOneShot(clip, _volume);
    }
}
