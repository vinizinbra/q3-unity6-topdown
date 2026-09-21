using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Lives in the bootstrap/splash scene: waits a fixed delay, then loads the menu scene.
public class BootstrapLoader : MonoBehaviour
{
    [SerializeField] private float _delaySeconds = 2f;
    [SerializeField] private string _sceneName = "MenuScene";

    private IEnumerator Start()
    {
        yield return new WaitForSecondsRealtime(_delaySeconds);
        SceneManager.LoadScene(_sceneName);
    }
}
