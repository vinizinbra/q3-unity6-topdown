using System;
using UnityEngine;

/// <summary>
/// Maps a UI background sprite to its pre-generated "grown" focus sprite (same shape, expanded by
/// <see cref="Entry.padding"/> px, 9-slice border included). Authored by the Editor tool
/// Tools/RiftRaiders/UI/Generate Focus Outlines; read at runtime by <see cref="FocusOutlineWidget"/>.
/// Lives in Resources so widgets can find it without a per-prefab reference.
/// </summary>
[CreateAssetMenu(fileName = "FocusOutlineSet", menuName = "RiftRaiders/UI/Focus Outline Set")]
public class FocusOutlineSet : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public Sprite source;
        public Sprite focus;
        [Tooltip("How many px the focus sprite grows past the source on every side.")]
        public int padding;
    }

    public Entry[] entries = new Entry[0];

    private static FocusOutlineSet instance;

    public static FocusOutlineSet Instance
    {
        get
        {
            if (instance == null)
                instance = Resources.Load<FocusOutlineSet>("FocusOutlineSet");
            return instance;
        }
    }

    public bool TryGet(Sprite source, out Entry entry)
    {
        if (source != null)
        {
            foreach (Entry candidate in entries)
            {
                if (candidate.source == source)
                {
                    entry = candidate;
                    return true;
                }
            }
        }

        entry = default;
        return false;
    }
}
