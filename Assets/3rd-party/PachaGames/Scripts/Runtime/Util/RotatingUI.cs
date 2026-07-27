using System;
using UnityEngine;

public class RotatingUI : MonoBehaviour
{
    [SerializeField]private Vector3 rotation = new Vector3(0, 0, -1000);

    private void Update()
    {
        transform.Rotate(rotation*Time.deltaTime);
    }

    public void SetRotation(Vector3 rotation)
    {
        this.rotation = rotation;
    }
}
