using System;
using Quantum;
using UnityEngine;

public class FollowCamera : PgSingleton<FollowCamera>
{
    public Transform target;
    public Vector3 offset;
    public float speed;

    public void AssignCamera(CharView charView)
    {
        target = charView.viewTransform;
    }
    private void Update()
    {
        if(target == null)
            return;
        var vec = new Vector3(target.position.x+offset.x, target.position.y+offset.y, target.position.z+offset.z);
        transform.position = Vector3.Lerp(transform.position,vec,Time.deltaTime*speed);
    }
}
