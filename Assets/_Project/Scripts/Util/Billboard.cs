using UnityEngine;

public class Billboard : MonoBehaviour
{
    // Every billboard faces the same camera, so the facing rotation is computed once per frame and
    // shared - ~90 instances each re-reading Camera.main's transform and re-running LookRotation
    // was measurable on device.
    private static Camera _camera;
    private static Transform _cameraTransform;
    private static int _cachedFrame = -1;
    private static Quaternion _cachedRotation;

    private Transform _transform;

    private void Awake()
    {
        _transform = transform;
    }

    private void LateUpdate()
    {
        if (_cachedFrame != Time.frameCount)
        {
            if (_camera == null)
            {
                _camera = Camera.main;
                _cameraTransform = _camera != null ? _camera.transform : null;
            }

            if (_camera == null)
                return;

            _cachedRotation = Quaternion.LookRotation(_cameraTransform.forward, Vector3.up);
            _cachedFrame = Time.frameCount;
        }

        _transform.rotation = _cachedRotation;
    }
}
