namespace Quantum
{
    using UnityEngine;

    // View-only half of RingSlamDeliveryData (see the partial declaration in RingSlamDeliveryData.cs).
    public partial class RingSlamDeliveryData
    {
        [Tooltip("LineRenderer prefab RingWaveVisualManager instantiates per ring - positionCount/points are overwritten every frame, so only material/color/texture need to be authored on it. Loop must be checked here too, so the circle actually closes.")]
        public LineRenderer LineRendererPrefab;
    }
}
