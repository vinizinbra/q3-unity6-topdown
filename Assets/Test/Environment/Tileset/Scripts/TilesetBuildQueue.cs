using System.Collections.Generic;
using UnityEngine;

// Runtime batching for TilesetPlatformBuilder's auto-generate: cubes queue here instead of building
// on their own Start/OnEnable, and the queue flushes once nothing new has queued for SettleFrames
// (or MaxWaitFrames passed). Chunks of one level often spawn over several frames and their floors
// merge into one cluster, so building per cube would rebuild that whole floor once per chunk.
// Created on demand as a hidden object in the active scene - it dies with the scene.
public class TilesetBuildQueue : MonoBehaviour
{
    private const int SettleFrames = 2;
    private const int MaxWaitFrames = 60;

    private static TilesetBuildQueue instance;
    private readonly HashSet<TilesetPlatformBuilder> pending = new();
    private int lastQueuedFrame;
    private int firstQueuedFrame = -1;

    public static void Enqueue(TilesetPlatformBuilder cube)
    {
        if (instance == null)
        {
            var go = new GameObject(nameof(TilesetBuildQueue)) { hideFlags = HideFlags.HideInHierarchy };
            instance = go.AddComponent<TilesetBuildQueue>();
        }

        if (instance.pending.Add(cube))
        {
            instance.lastQueuedFrame = Time.frameCount;
            if (instance.firstQueuedFrame < 0)
                instance.firstQueuedFrame = Time.frameCount;
        }
    }

    private void LateUpdate()
    {
        if (pending.Count == 0)
            return;

        var frame = Time.frameCount;
        if (frame - lastQueuedFrame < SettleFrames && frame - firstQueuedFrame < MaxWaitFrames)
            return;

        Flush();
    }

    private void Flush()
    {
        firstQueuedFrame = -1;
        var batch = new List<TilesetPlatformBuilder>(pending);
        pending.Clear();

        // One scan per scene per flush; a cube built as part of an earlier cluster in this batch is skipped.
        var candidatesByScene = new Dictionary<UnityEngine.SceneManagement.Scene, List<TilesetPlatformBuilder>>();
        var built = new HashSet<TilesetPlatformBuilder>();
        foreach (var cube in batch)
        {
            if (cube == null || !cube.isActiveAndEnabled || built.Contains(cube))
                continue;

            var scene = cube.gameObject.scene;
            if (!candidatesByScene.TryGetValue(scene, out var candidates))
                candidatesByScene[scene] = candidates = TilesetPlatformBuilder.FindCandidates(scene);

            cube.Generate(candidates);
            foreach (var member in cube.Members)
                built.Add(member);
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}
