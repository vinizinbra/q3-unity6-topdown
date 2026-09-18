using Quantum;
using UnityEngine;

// Shared, single source of truth for how a LevelUpCategory is presented to the player - display
// name and (via SpriteManager's own generic name-keyed lookup) icon - so any caller previewing or
// labeling a category (a Chest's own title, Optional Team Challenge's reward preview, a future
// Store/Blacksmith category filter, etc.) reads the exact same text/art instead of each caller
// hand-typing its own copy that can silently drift from whatever the real reward actually is.
public static class LevelUpCategoryUtility
{
    // Moved out of GameplayUiController.BuildTitle's own private GetCategoryDisplayName - same
    // switch, now reusable.
    public static string GetDisplayName(LevelUpCategory category)
    {
        switch (category)
        {
            case LevelUpCategory.HeroSkill: return "Hero Skill";
            case LevelUpCategory.GlobalUpgrade: return "Global Upgrade";
            case LevelUpCategory.RiftMutation: return "Rift Mutation";
            case LevelUpCategory.WeaponPerk: return "Weapon Perk";
            case LevelUpCategory.ChooseWeapon: return "Weapon";
            default: return "Chest";
        }
    }

    // Resolved via SpriteManager's own generic name-keyed lookup (SpriteConfigSO entries) - add an
    // entry named e.g. "RiftMutation" (category.ToString()) to any registered SpriteConfigSO to
    // supply this for a given category; returns null (SpriteManager logs its own warning) if none
    // is registered yet.
    public static Sprite GetIcon(LevelUpCategory category)
    {
        return SpriteManager.GetSprite(category.ToString());
    }
}
