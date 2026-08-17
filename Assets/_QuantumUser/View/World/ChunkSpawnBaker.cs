using NaughtyAttributes;
using Photon.Deterministic;
using Quantum;
using UnityEngine;
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
#endif

// Bakes every EntityPrototype instance placed under a "SpawnedEntities" object into a
// ChunkSpawnConfig asset's Spawns array (each entry's Offset = the child's position local to this
// chunk root) - so a chunk's talent-gated spawns (see ChunkSpawnConfig/TalentGateSystem,
// docs/talents.md) can be authored by dragging prototype prefabs into the scene where they should
// appear, instead of hand-typing AssetRef/FPVector3 rows on the asset. Same "author in the Scene
// view, then bake" idea as ChunkWaypointBaker (also a plain [Button] component in this assembly).
//
// WHY a bake is needed at all: Quantum's prefab importer only reads a chunk prefab's ROOT GameObject
// and silently ignores nested QuantumEntityPrototypes (see docs/talents.md), so the child prototypes
// under SpawnedEntities never spawn on their own - baking lifts their prototype ref + local position
// into a real ChunkSpawnConfig (TalentGateSystem then f.Create's each at chunk Transform3D.Position
// + Offset) and points this chunk's own Chunk.SpawnConfig at it.
//
// Each child must be an INSTANCE of a standalone EntityPrototype prefab - the bake resolves its
// AssetRef<EntityPrototype> from the source prefab's companion .qprototype. Requirement/Chance are
// baked as None/always; edit them per row on the ChunkSpawnConfig asset afterward for anything that
// should be talent-gated or rare. The bake itself is Editor-only (needs AssetDatabase).
//
// BakeSpawns also converts each baked child into a harmless Scene-view-only preview once its data
// is safely in the asset (ConvertToBakedPreview): strips every Quantum authoring component
// (QuantumEntityPrototype and any QuantumUnityComponentPrototype wrapper, e.g. QPrototypeChunk-
// shaped per-component scripts), leaving only plain view components (renderers, QuantumEntityView,
// etc.) behind; unpacks it from its source prefab entirely, since a prefab instance missing
// components its own source still has is exactly the kind of override Unity's Prefab workflow (
// Revert/Apply, "Missing Prefab" warnings) isn't meant to represent; renames it so the change is
// obvious at a glance in the Hierarchy; and adds BakedSpawnPreview so it self-destroys the instant
// the game actually runs (Awake never fires for an object just sitting in an edit-mode Scene). None
// of this affects the baked data - these child instances never actually spawn via Quantum on their
// own anyway (see the importer note above), so post-bake they're purely a level-design preview.
// ResetSpawns reverses all of it: it clears SpawnedEntities and re-instantiates the original source
// prefab (full Quantum setup, original name, real prefab connection) for every entry already saved
// in targetConfig.Spawns, by resolving each AssetRef<EntityPrototype> guid back to its source
// prefab - the same guid-in-the-.qprototype-file trick EntityPrototypeAssetRefDrawer already uses
// for its own thumbnail preview.
[RequireComponent(typeof(QPrototypeChunk))]
public class ChunkSpawnBaker : MonoBehaviour
{
    [SerializeField, Tooltip("Parent object holding the EntityPrototype instances to bake (the \"SpawnedEntities\" object). If unset, a direct child named \"SpawnedEntities\" is used, else this object itself.")]
    private Transform spawnedEntitiesRoot;

    [SerializeField, Tooltip("The ChunkSpawnConfig asset to write the baked Spawns into. Its Spawns array is fully replaced on each bake, and the chunk's own Chunk.SpawnConfig is pointed at this asset.")]
    private ChunkSpawnConfig targetConfig;

#if UNITY_EDITOR
    // The Quantum importer names every prototype prefab's companion asset
    // "<prefabName>EntityPrototype.qprototype" in the same folder - see
    // QuantumEntityPrototypeAssetObjectImporter (its type isn't referenced here - it lives in an
    // Editor-only assembly this one can't see - so the suffix is inlined instead).
    private const string PrototypeCompanionSuffix = "EntityPrototype.qprototype";

    private const string BakedPreviewNameSuffix = " (Baked Preview)";
#endif

    [Button]
    public void BakeSpawns()
    {
#if UNITY_EDITOR
        if (targetConfig == null)
        {
            Debug.LogError($"[ChunkSpawnBaker] {name}: assign a Target Config (ChunkSpawnConfig asset) before baking.", this);
            return;
        }

        Transform root = ResolveRoot();
        var spawns = new List<SpawnEntityWithRequirement>();

        foreach (Transform child in root)
        {
            if (child.GetComponent<QuantumEntityPrototype>() == null)
            {
                Debug.LogWarning($"[ChunkSpawnBaker] {name}: child '{child.name}' has no QuantumEntityPrototype - skipping (only prototype instances are baked).", child);
                continue;
            }

            if (TryResolvePrototypeGuid(child.gameObject, out AssetGuid guid) == false)
            {
                Debug.LogWarning($"[ChunkSpawnBaker] {name}: could not resolve an EntityPrototype asset for '{child.name}' - it must be an instance of a standalone prototype prefab. Skipping.", child);
                continue;
            }

            spawns.Add(new SpawnEntityWithRequirement
            {
                Prototype = new AssetRef<EntityPrototype> { Id = guid },
                // Local to this chunk root - TalentGateSystem spawns at (chunk Transform3D.Position +
                // Offset), and the chunk root IS that position (chunks are min-corner pivoted, never
                // rotated), so a child's position relative to this root is exactly that offset.
                Offset = transform.InverseTransformPoint(child.position).ToFPVector3(),
                Requirement = SharedTalentRequirement.None,
                Chance = FP._0,
            });

            ConvertToBakedPreview(child.gameObject);
        }

        targetConfig.Spawns = spawns.ToArray();
        EditorUtility.SetDirty(targetConfig);

        // Wire this chunk at the config it just baked, so the two stay connected.
        QPrototypeChunk chunkPrototype = GetComponent<QPrototypeChunk>();
        chunkPrototype.Prototype.SpawnConfig = new AssetRef<ChunkSpawnConfig> { Id = targetConfig.Guid };
        EditorUtility.SetDirty(chunkPrototype);

        AssetDatabase.SaveAssets();

        Debug.Log($"[ChunkSpawnBaker] {name}: baked {spawns.Count} spawn(s) into {targetConfig.name}.", this);
#else
        Debug.LogWarning("[ChunkSpawnBaker] BakeSpawns is Editor-only.");
#endif
    }

    // Reverses BakeSpawns' strip: destroys everything currently under SpawnedEntities and
    // re-instantiates the original source prefab (full Quantum authoring setup included) for every
    // entry already saved in targetConfig.Spawns, positioned back at Offset. targetConfig.Spawns is
    // the source of truth here, not scene state, so this is a full destructive re-sync, not a merge.
    [Button]
    public void ResetSpawns()
    {
#if UNITY_EDITOR
        if (targetConfig == null)
        {
            Debug.LogError($"[ChunkSpawnBaker] {name}: assign a Target Config (ChunkSpawnConfig asset) before resetting.", this);
            return;
        }

        Transform root = ResolveRoot();

        // A dedicated SpawnedEntities container is required here (unlike BakeSpawns, which only
        // reads it) - falling back to this chunk's own transform would mean clearing every child of
        // the whole chunk (walls, waypoint markers, everything), not just the baked spawns.
        if (root == transform)
        {
            Debug.LogError($"[ChunkSpawnBaker] {name}: no dedicated SpawnedEntities container found (assign Spawned Entities Root, or add a child named \"SpawnedEntities\") - refusing to clear this chunk's own children.", this);
            return;
        }

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(root.GetChild(i).gameObject);
        }

        int restored = 0;

        foreach (SpawnEntityWithRequirement spawn in targetConfig.Spawns)
        {
            GameObject prefab = ResolveSourcePrefab(spawn.Prototype.Id);

            if (prefab == null)
            {
                Debug.LogWarning($"[ChunkSpawnBaker] {name}: could not resolve a source prefab for guid {spawn.Prototype.Id} - skipping.", this);
                continue;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(instance, "Reset Chunk Spawns");
            instance.transform.SetParent(root, false);
            // Offset is local to THIS transform (see BakeSpawns), not necessarily root, so it's
            // resolved the same way regardless of root's own position/rotation.
            instance.transform.position = transform.TransformPoint(spawn.Offset.ToUnityVector3());
            restored++;
        }

        Debug.Log($"[ChunkSpawnBaker] {name}: restored {restored} spawn instance(s) from {targetConfig.name}.", this);
#else
        Debug.LogWarning("[ChunkSpawnBaker] ResetSpawns is Editor-only.");
#endif
    }

    private Transform ResolveRoot()
    {
        if (spawnedEntitiesRoot != null)
            return spawnedEntitiesRoot;

        Transform named = transform.Find("SpawnedEntities");
        return named != null ? named : transform;
    }

#if UNITY_EDITOR
    // Resolves a prototype-prefab instance to its EntityPrototype AssetObject's guid, via the
    // companion .qprototype the Quantum importer generates next to every prototype prefab. Falls back
    // to loading an EntityPrototype directly at the prefab path for an embedded setup.
    private static bool TryResolvePrototypeGuid(GameObject childInstance, out AssetGuid guid)
    {
        guid = default;

        Object source = PrefabUtility.GetCorrespondingObjectFromSource(childInstance);
        string prefabPath = source != null ? AssetDatabase.GetAssetPath(source) : null;

        if (string.IsNullOrEmpty(prefabPath))
            return false;

        string directory = Path.GetDirectoryName(prefabPath);
        string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
        string companionPath = Path.Combine(directory, prefabName + PrototypeCompanionSuffix);

        EntityPrototype prototype = AssetDatabase.LoadAssetAtPath<EntityPrototype>(companionPath)
                                    ?? AssetDatabase.LoadAssetAtPath<EntityPrototype>(prefabPath);

        if (prototype == null)
            return false;

        guid = prototype.Guid;
        return guid.IsValid;
    }

    // Reverse of TryResolvePrototypeGuid - resolves an EntityPrototype asset back to the source
    // prefab it was generated from. The .qprototype companion file's raw text content is literally
    // the source prefab's GUID (see QuantumEntityPrototypeAssetObjectImporter.OnImportAsset) - same
    // trick EntityPrototypeAssetRefDrawer already uses for its own thumbnail preview.
    private static GameObject ResolveSourcePrefab(AssetGuid guid)
    {
        if (guid.IsValid == false)
            return null;

        if (QuantumUnityDB.GetGlobalAssetEditorInstance(guid) is not EntityPrototype asset)
            return null;

        string prototypePath = AssetDatabase.GetAssetPath(asset);

        if (string.IsNullOrEmpty(prototypePath) || prototypePath.EndsWith(PrototypeCompanionSuffix) == false)
            return null;

        string prefabGuid = File.ReadAllText(prototypePath);
        string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuid);

        return string.IsNullOrEmpty(prefabPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
    }

    // Strips Quantum authoring components, breaks the prefab connection, renames, and marks the
    // instance to self-destroy at runtime - see the class comment's own ConvertToBakedPreview note.
    private static void ConvertToBakedPreview(GameObject instance)
    {
        StripQuantumComponents(instance);

        // Removed components on a prefab instance are a normal, supported override - unpacking
        // afterward just bakes that state in and drops the connection, so the source prefab (which
        // still has every Quantum component) is never touched.
        if (PrefabUtility.IsPartOfPrefabInstance(instance))
        {
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }

        if (instance.TryGetComponent(out BakedSpawnPreview _) == false)
        {
            instance.AddComponent<BakedSpawnPreview>();
        }

        if (instance.name.EndsWith(BakedPreviewNameSuffix) == false)
        {
            instance.name += BakedPreviewNameSuffix;
        }
    }

    // QuantumUnityComponentPrototype wrappers (e.g. QPrototypeChunk-shaped per-component scripts)
    // all [RequireComponent(typeof(QuantumEntityPrototype))] on the SAME GameObject, so they must be
    // destroyed before QuantumEntityPrototype itself or Unity blocks removing the one they depend on.
    private static void StripQuantumComponents(GameObject instance)
    {
        foreach (QuantumUnityComponentPrototype wrapper in instance.GetComponentsInChildren<QuantumUnityComponentPrototype>(true))
        {
            DestroyImmediate(wrapper);
        }

        foreach (QuantumEntityPrototype prototype in instance.GetComponentsInChildren<QuantumEntityPrototype>(true))
        {
            DestroyImmediate(prototype);
        }
    }
#endif
}
