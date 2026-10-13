using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Starting meta-progression levels for balancing playtests. Only read when the <c>META_PLAYTEST</c> scripting
/// define is on (main Editor toolbar dropdown "Meta: Normal / Playtest"): every listed line then
/// counts as at least that level - MetaProgressionSave.GetLevel returns max(saved, playtest) - so the menu,
/// the unlock requirements and the match (TalentSaveMapper) all see it. Never written to the save: turn the
/// define off and the real progression is back untouched. Referenced from <see cref="GameDB"/>.
/// </summary>
[CreateAssetMenu(fileName = "MetaProgressionPlaytestConfig", menuName = "RiftRaiders/Meta Progression Playtest Config")]
public class MetaProgressionPlaytestConfig : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public PermanentUpgradeId id;
        [Min(0)] public int level;
    }

    [TextArea(2, 4)] public string notes;

    public List<Entry> levels = new List<Entry>();

    public int GetLevel(PermanentUpgradeId id)
    {
        int level = 0;
        foreach (Entry entry in levels)
        {
            if (entry.id == id && entry.level > level)
                level = entry.level;
        }

        return level;
    }
}
