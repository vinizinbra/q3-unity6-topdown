using System;
using System.Collections;
using TMPro;
using UnityEngine;

public class TypingEffect : MonoBehaviour
{
    public TMP_Text textMeshPro; // Reference to your TextMeshPro component
    public float typingSpeed = 0.025f;  // Time between each letter

    private string fullText;  // Store the full text
    private string currentText = ""; // Text being revealed
    public Action onFinish;
    public bool Typing => currentText != "";
    public bool acceptInput = false;
    public Coroutine TypingCoroutine;
    private void Reset()
    {
        textMeshPro = GetComponent<TMP_Text>();
    }

    private void Awake()
    {
        fullText = textMeshPro.text; // Save the complete text
        textMeshPro.text = ""; 
    }

    public void SetNewText(string newText, int fontSize = -1)
    {
        if(fontSize > 0)
            textMeshPro.fontSize = fontSize;
        fullText = newText;
        textMeshPro.text = "";
        currentText = "";
        if (gameObject.activeInHierarchy)
        {
            StopAllCoroutines();
            TypingCoroutine = StartCoroutine(RevealText());
        }
    }
    void OnEnable()
    {
        if (!string.IsNullOrEmpty(fullText))
        {
            currentText = "";
            textMeshPro.text = "";
            StopAllCoroutines();
            TypingCoroutine = StartCoroutine(RevealText());
        }
    }

    private void OnDisable()
    {
        StopAllCoroutines();
    }

    private void Update()
    {
        if (acceptInput && (Input.anyKeyDown || Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)) && TypingCoroutine != null)
        {
            SkipToEnd();
        }
    }

    public void SkipToEnd()
    {
        if (TypingCoroutine == null) return;
        StopAllCoroutines();
        TypingCoroutine = null;
        currentText = "";
        textMeshPro.text = fullText;
        onFinish?.Invoke();
    }

    private IEnumerator RevealText()
    {
        bool open = false;
        for (int i = 0; i < fullText.Length; i++)
        {
            if (fullText[i] == '<')
            {
                open = true;
            }

            if (open)
            {
                if(fullText[i] == '>')
                    open = false;
            }
            
            currentText += fullText[i];
            if(open)
                continue;
            textMeshPro.text = currentText;
            yield return new WaitForSeconds(typingSpeed);
        }
        currentText = "";

        onFinish?.Invoke();
    }
}