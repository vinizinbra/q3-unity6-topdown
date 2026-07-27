using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SafeArea : MonoBehaviour
{
    private RectTransform _rectTransform;
    private Rect safeArea;
    private Vector2 minAnchor;
    private Vector2 maxAnchor;

    void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        safeArea = Screen.safeArea;
        minAnchor = safeArea.position;
        maxAnchor = minAnchor + safeArea.size;
        minAnchor.x /= Screen.width;
        minAnchor.y /= Screen.height;
        maxAnchor.x /= Screen.width;
        maxAnchor.y /= Screen.height;
        
        _rectTransform.anchorMin = minAnchor;
        _rectTransform.anchorMax = maxAnchor;
    }

}
