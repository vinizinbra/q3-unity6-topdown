using UnityEditor;
using UnityEngine;

// Scripted entries in the project-wide EnemyActionSoundTable (Resources/EnemyActionSounds), for builders that own an
// enemy's sounds. Set replaces the action+phase entry (or adds it), so a rebuild never duplicates.
internal static class EnemyActionSoundTableAuthoring
{
    private const string AssetPath = "Assets/_Project/Resources/" + EnemyActionSoundTable.ResourcePath + ".asset";

    internal static void Set(Quantum.EnemyActionData action, Quantum.AttackPhase phase, SoundData sound)
    {
        var table = AssetDatabase.LoadAssetAtPath<EnemyActionSoundTable>(AssetPath);
        if (table == null)
        {
            table = ScriptableObject.CreateInstance<EnemyActionSoundTable>();
            AssetDatabase.CreateAsset(table, AssetPath);
        }

        table.Entries.RemoveAll(e => e.Action == null || (e.Action == action && e.Phase == phase));
        table.Entries.Add(new EnemyActionSoundTable.Entry { Action = action, Phase = phase, Sound = sound });
        EditorUtility.SetDirty(table);
    }
}
