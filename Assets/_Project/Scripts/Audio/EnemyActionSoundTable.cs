using System;
using System.Collections.Generic;
using Quantum;
using UnityEngine;

// Sounds for enemy attack phases, keyed by action + phase. EnemyActionData is a simulation asset and can't hold a
// SoundData (Assembly-CSharp), so the mapping lives here, view-side, and EnemyAttackVisualsView plays it on the same
// phase edges that play the step's visuals - no carrier particle needed. One project-wide asset
// (Resources/EnemyActionSounds), filled by the boss builders; an action with no entry stays silent as before.
public class EnemyActionSoundTable : ScriptableObject
{
    public const string ResourcePath = "EnemyActionSounds";

    [Serializable]
    public class Entry
    {
        public EnemyActionData Action;
        public AttackPhase Phase;
        [SoundDataPicker] public SoundData Sound;
    }

    public List<Entry> Entries = new();

    private static EnemyActionSoundTable _instance;
    private static bool _loaded;
    private Dictionary<(AssetGuid, AttackPhase), SoundData> _lookup;

    public static SoundData Find(EnemyActionData action, AttackPhase phase)
    {
        if (_loaded == false)
        {
            _instance = Resources.Load<EnemyActionSoundTable>(ResourcePath);
            _loaded = true;
        }

        if (_instance == null || action == null)
            return null;

        if (_instance._lookup == null)
        {
            _instance._lookup = new Dictionary<(AssetGuid, AttackPhase), SoundData>();
            foreach (Entry entry in _instance.Entries)
            {
                if (entry.Action != null && entry.Sound != null)
                    _instance._lookup[(entry.Action.Guid, entry.Phase)] = entry.Sound;
            }
        }

        return _instance._lookup.TryGetValue((action.Guid, phase), out SoundData sound) ? sound : null;
    }

    // Keyed by guid, not reference: the simulation resolves actions through its own asset DB.
    private void OnValidate() => _lookup = null;
}
