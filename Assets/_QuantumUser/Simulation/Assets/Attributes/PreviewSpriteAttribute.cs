namespace Quantum
{
    // Marks a Sprite field for PreviewSpriteDrawer (Editor) to render the sprite's thumbnail beneath
    // the object field - same reasoning as PreviewPrefabAttribute, just for Sprite instead of
    // GameObject (e.g. SkillData.Icon). Never referenced from headless sim code, so no
    // QUANTUM_UNITY fallback is needed.
    public class PreviewSpriteAttribute : UnityEngine.PropertyAttribute
    {
    }
}
