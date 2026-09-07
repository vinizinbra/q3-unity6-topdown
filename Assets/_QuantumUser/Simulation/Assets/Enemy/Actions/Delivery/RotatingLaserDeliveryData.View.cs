namespace Quantum
{
    using UnityEngine;

    // View-only half of RotatingLaserDeliveryData (see the partial declaration in
    // RotatingLaserDeliveryData.cs).
    public partial class RotatingLaserDeliveryData
    {
        [Tooltip("LineRenderer prefab RotatingLaserVisualManager instantiates per beam - positionCount/width are overwritten every frame, so only material/color/texture need to be authored on it.")]
        public LineRenderer LineRendererPrefab;
    }
}
