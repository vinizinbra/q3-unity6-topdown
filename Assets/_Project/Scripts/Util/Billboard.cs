using UnityEngine;

public class Billboard : MonoBehaviour
{
    private Camera _camera;

    private void LateUpdate()
    {
        if (_camera == null)
            _camera = Camera.main;

        if (_camera == null)
            return;

        transform.rotation = Quaternion.LookRotation(_camera.transform.forward, Vector3.up);
    }
}
