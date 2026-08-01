namespace Quantum.Editor
{
    using UnityEditor;

    // See FoldoutGroupEditor - renders EnemyActionData's [FoldoutGroup("Base"/"Animation")] runs
    // (EnemyActionData.cs/.View.cs) as collapsible boxed sections.
    [CustomEditor(typeof(EnemyActionData))]
    public class EnemyActionDataEditor : FoldoutGroupEditor
    {
    }
}
