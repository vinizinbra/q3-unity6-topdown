using TMPro;
using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public class ToastWidget : MonoBehaviour
{
    public CanvasGroup canvasGroup;
    public TMP_Text message;
    public float displaySeconds = 2.5f;
    public float fadeSpeed = 4f;

    private float _hideAtTime;
    private bool _hiding;

    public bool CanUse => !gameObject.activeSelf;

    private void Reset()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    public void Show(string text)
    {
        message.text = text;
        canvasGroup.alpha = 1f;
        _hiding = false;
        _hideAtTime = Time.unscaledTime + displaySeconds;
        gameObject.SetActive(true);
    }

    private void Update()
    {
        if (!_hiding && Time.unscaledTime >= _hideAtTime)
            _hiding = true;

        if (_hiding)
        {
            canvasGroup.alpha -= Time.unscaledDeltaTime * fadeSpeed;
            if (canvasGroup.alpha <= 0f)
                gameObject.SetActive(false);
        }
    }
}
