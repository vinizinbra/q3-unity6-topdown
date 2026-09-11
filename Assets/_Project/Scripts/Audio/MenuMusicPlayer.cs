using UnityEngine;
using UnityEngine.SceneManagement;

// Starts the main-menu theme once MenuScene is up, and again whenever the gameplay scene goes away
// (match end, disconnect, a reconnect's own teardown). MusicDirector can't do either job itself - it
// drives its track choice off Global.CurrentState, which doesn't exist until a Quantum session is
// running, so the menu needs its own trivial trigger.
//
// AudioManager.PlayMusic() from here is an ordinary call into the same singleton that survives into
// the gameplay scene (see AudioManager.persistAcrossScenes) - so the moment MusicDirector resolves
// its own first track there it crossfades this one out and takes over, with no extra code needed on
// either side. The reverse direction (gameplay -> menu) needs this component to actively replay
// itself, since MusicDirector is scene-local and is simply gone once that scene unloads - nothing is
// left to pick a track at all otherwise.
public class MenuMusicPlayer : MonoBehaviour
{
    [SerializeField, SoundDataPicker, Tooltip("Theme played on Start, and again whenever the gameplay scene unloads. Leave empty for silence in the menu.")]
    private SoundData music;

    private void Start()
    {
        AudioManager.PlayMusic(music);
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }

    // This component lives in MenuScene, which never unloads - so any OTHER scene unloading is
    // necessarily the gameplay scene going away, whatever triggered it. That's the one event every
    // "back to menu" path funnels through, so it's simpler and more robust than trying to catch each
    // path (disconnect button, connection drop, a future leave-match flow) individually.
    private void OnSceneUnloaded(Scene scene)
    {
        if (scene != gameObject.scene)
            AudioManager.PlayMusic(music);
    }
}
