using Quantum;
using UnityEditor;
using UnityEngine;

// WeaponDataAsset's Inspector DPS preview reads other assets too (BaseTraits perks, ProjectileData
// and its Hit), but OnValidate only fires for edits to the weapon asset itself - so tuning a trait
// left every weapon using it showing stale numbers. Listens for any AssetObject edit (Inspector
// change, undo, script) and re-runs the preview on every weapon, dirtying only the ones whose text
// changed. ~40 weapon assets, so a full pass per edit is cheap.
[InitializeOnLoad]
public static class WeaponDpsPreviewRefresher
{
    private static bool _pending;

    static WeaponDpsPreviewRefresher()
    {
        ObjectChangeEvents.changesPublished += OnChangesPublished;
    }

    private static void OnChangesPublished(ref ObjectChangeEventStream stream)
    {
        for (int i = 0; i < stream.length; i++)
        {
            if (stream.GetEventType(i) != ObjectChangeKind.ChangeAssetObjectProperties)
                continue;

            stream.GetChangeAssetObjectPropertiesEvent(i, out var change);

            // Weapon edits are already covered by its own OnValidate.
            if (EditorUtility.InstanceIDToObject(change.instanceId) is AssetObject asset && asset is not WeaponDataAsset)
            {
                ScheduleRefresh();
                return;
            }
        }
    }

    // Deferred a frame so a slider drag's burst of change events costs one pass, not one per event.
    private static void ScheduleRefresh()
    {
        if (_pending)
            return;

        _pending = true;
        EditorApplication.delayCall += () =>
        {
            _pending = false;
            RefreshAll();
        };
    }

    public static void RefreshAll()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Quantum.WeaponDataAsset"))
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponDataAsset>(AssetDatabase.GUIDToAssetPath(guid));

            if (weapon != null && weapon.RefreshDpsPreview())
                EditorUtility.SetDirty(weapon);
        }
    }
}
