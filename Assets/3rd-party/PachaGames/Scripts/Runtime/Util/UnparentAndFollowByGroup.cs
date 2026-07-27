using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

public class UnparentAndFollowByGroup : MonoBehaviour
{
    public string group;
    public Transform followTransform;
    private Vector3 localPositionOffset;
    private void Awake()
    {
        Transform followTransformParent = null;
        var rootGameObjects = SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (var go in rootGameObjects)
        {
            if (go.name == group)
            {
                followTransformParent = go.transform;
                break;
            }
        }

        if (followTransformParent == null)
        {
            followTransformParent = new GameObject(group).transform;
        }

        localPositionOffset = transform.localPosition;
        followTransform = transform.parent;
        transform.parent = followTransformParent;
    }

    private void Update()
    {
        if(followTransform != null)
            transform.position = followTransform.position + localPositionOffset;
    }
}
