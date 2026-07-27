using System;
using UnityEngine;

public class RotatingUI : MonoBehaviour
{
    private Vector3 rotation = new Vector3(0, 0, -1000);

    private void Update()
    {
        transform.Rotate(rotation*Time.deltaTime);
    }
}
