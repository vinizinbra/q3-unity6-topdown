namespace Quantum
{
    // Marks a GameObject field for PreviewPrefabDrawer (Editor) to render Unity's own generated
    // prefab thumbnail beneath the object field - unlike ExpandableAssetAttribute this is never
    // referenced from headless sim code, so no QUANTUM_UNITY fallback is needed.
    public class PreviewPrefabAttribute : UnityEngine.PropertyAttribute
    {
    }
}
