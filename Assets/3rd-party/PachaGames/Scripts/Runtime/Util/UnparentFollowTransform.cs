using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

public class UnparentFollowTransform : MonoBehaviour
{
    
    public Transform followTransform;
    public Transform followTransformParent;
    private Vector3 localPositionOffset;
    public Vector3 manualOffset;
    public bool isUI;
    private void Awake()
    {
        
        if(followTransform == null)
            followTransform = transform.parent;
        localPositionOffset = transform.localPosition;
        transform.SetParent(followTransformParent);
        
    }

    private void Update()
    {
        if(followTransform == null) return;
            
        if (isUI)
        {
            (transform as RectTransform).anchoredPosition = (followTransform as RectTransform).anchoredPosition + new Vector2(manualOffset.x, manualOffset.y);
        }
        else
        {
            transform.position = followTransform.position + localPositionOffset;
        }
    }
}
