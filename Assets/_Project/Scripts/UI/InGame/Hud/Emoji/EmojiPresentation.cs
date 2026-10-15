using Quantum;
using UnityEngine;

// Single place that turns (hero entity, EmojiKind) into a sprite - see docs/emoji.md. Sprites live on the
// hero's CharacterData.Emojis (indexed by EmojiKind), so no view code ever branches on which hero it is.
public static class EmojiPresentation
{
    public const int Count = 4;

    public static CharacterData GetData(EntityRef entity)
    {
        Frame frame = QuantumRunner.Default != null ? QuantumRunner.Default.Game?.Frames.Predicted : null;

        if (frame == null || frame.TryGet<CharacterStats>(entity, out var stats) == false || stats.CharacterData.IsValid == false)
            return null;

        return frame.FindAsset(stats.CharacterData);
    }

    public static Sprite GetSprite(EntityRef entity, EmojiKind kind)
    {
        CharacterData data = GetData(entity);
        int index = (int)kind;
        if (data == null || data.Emojis == null || index < 0 || index >= data.Emojis.Length)
            return null;

        return data.Emojis[index];
    }
}
