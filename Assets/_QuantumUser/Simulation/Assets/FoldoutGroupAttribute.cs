namespace Quantum
{
    // Tags a contiguous run of fields as one collapsible section in the Inspector - every field in
    // the run must carry the same group name; a field without this attribute (or a different name)
    // always ends the run and draws normally. Rendering is handled by FoldoutGroupEditor (Editor/),
    // an exact-type Editor any AssetObject subclass can opt into with a one-line subclass - see
    // EnemyActionDataEditor.
#if QUANTUM_UNITY
    public class FoldoutGroupAttribute : UnityEngine.PropertyAttribute
    {
        public readonly string Name;

        public FoldoutGroupAttribute(string name)
        {
            Name = name;
        }
    }
#else
    public class FoldoutGroupAttribute : System.Attribute
    {
        public readonly string Name;

        public FoldoutGroupAttribute(string name)
        {
            Name = name;
        }
    }
#endif
}
