using System.Collections.Generic;
using System.IO;
using System.Linq;
using Quantum;
using QuantumUser.View.Util;
using UnityEditor;
using UnityEngine;

// Authors the Voice Line Trigger System's assets: one VoiceLineConfig with a tuned rule per working
// trigger, plus one HeroVoiceBank per hero with its CharacterData already linked.
//
// Exists because the alternative is hand-entering seventeen rules and six banks in the Inspector,
// which is both tedious and the kind of thing that ends up half-done. Same
// Tools/RiftRaiders/Generate... convention as every other content generator here.
//
// Safe to re-run: existing assets are UPDATED in place rather than replaced, so nothing already
// authored on a bank (its lines, its pair overrides) is lost. Rules ARE overwritten - they're pacing
// defaults, and the point of re-running is usually to get them back.
internal static class VoiceContentGenerator
{
    private const string LogTag = "VoiceContentGenerator";
    private const string Folder = "Assets/_Project/Audio/Voice";

    // Probability / cooldown are starting points for playtesting, not final values. Priorities and
    // audiences follow the design brief's own tables.
    private static readonly (VoiceLineTrigger Trigger, float Probability, float Cooldown, VoicePriority Priority, VoiceAudience Audience)[] Rules =
    {
        // Run flow - once per transition, so no probability roll. Announcements, heard flat by all.
        (VoiceLineTrigger.RunStarted,             1.00f,   0f, VoicePriority.Normal,   VoiceAudience.AllPlayers),
        (VoiceLineTrigger.SurvivalStarted,        0.50f,  30f, VoicePriority.Normal,   VoiceAudience.LocalOnly),
        (VoiceLineTrigger.BreathingTimeStarted,   0.60f,  30f, VoicePriority.Normal,   VoiceAudience.LocalOnly),
        (VoiceLineTrigger.BossStarted,            1.00f,   0f, VoicePriority.High,     VoiceAudience.AllPlayers),
        (VoiceLineTrigger.BossDefeated,           1.00f,   0f, VoicePriority.High,     VoiceAudience.AllPlayers),

        // Frequent action - low probability is what stops a line becoming a catchphrase.
        (VoiceLineTrigger.HeroSkillUsed,          0.20f,  25f, VoicePriority.Normal,   VoiceAudience.NearbyPlayers),

        // Personal progression - about you, so only you hear it.
        // Every level-up pick. Modest probability - it happens many times a run, and a line on every
        // single one would wear out fast.
        (VoiceLineTrigger.UpgradeChosen,          0.30f,  20f, VoicePriority.Normal,   VoiceAudience.LocalOnly),
        // Rare and earned - a whole Ascension line completed - so it always plays.
        (VoiceLineTrigger.UpgradeMaxed,           1.00f,   0f, VoicePriority.High,     VoiceAudience.LocalOnly),
        // Spending at the Store. Only happens during a Break, so it needs no heavy rate limiting.
        (VoiceLineTrigger.ItemPurchased,          0.50f,   6f, VoicePriority.Normal,   VoiceAudience.LocalOnly),

        (VoiceLineTrigger.HeavyHitReceived,       0.35f,  15f, VoicePriority.Normal,   VoiceAudience.LocalOnly),

        // Combat meter topping out (Max's Overdrive Rage, Brute's Juggernaut charge). A personal

        // Failed presses can happen several times a second - lowest priority, longest cooldown.
        (VoiceLineTrigger.AbilityNotReady,        0.25f,  12f, VoicePriority.Low,      VoiceAudience.LocalOnly),

        // Life state - always heard, and able to cut through whatever chatter is playing.
        (VoiceLineTrigger.PlayerDowned,           1.00f,   0f, VoicePriority.Critical, VoiceAudience.AllPlayers),
        (VoiceLineTrigger.PlayerKO,               1.00f,   0f, VoicePriority.Critical, VoiceAudience.AllPlayers),
        (VoiceLineTrigger.TeammateDowned,         0.80f,   8f, VoicePriority.High,     VoiceAudience.LocalOnly),
        // Both halves of a revive are two-player moments, so both are heard by both participants.
        (VoiceLineTrigger.RevivingTeammate,      0.60f,   8f, VoicePriority.Normal,   VoiceAudience.RelevantPlayers),
        (VoiceLineTrigger.RevivedTeammate,        1.00f,   0f, VoicePriority.High,     VoiceAudience.RelevantPlayers),
        // Nobody else involved, so nobody else needs to hear it.
        (VoiceLineTrigger.SelfRevive,             1.00f,   0f, VoicePriority.High,     VoiceAudience.LocalOnly),

        // Accessory. RecoveredByAlly is the pair-dialogue moment, so it always plays and is heard by
        // both players involved - a half-delivered exchange is worse than none.
        (VoiceLineTrigger.AccessoryDropped,       0.70f,   8f, VoicePriority.Normal,   VoiceAudience.LocalOnly),
        (VoiceLineTrigger.AccessorySelfRecovered, 0.35f,  15f, VoicePriority.Low,      VoiceAudience.LocalOnly),
        (VoiceLineTrigger.AccessoryReturnedToAlly, 1.00f, 0f, VoicePriority.High,     VoiceAudience.RelevantPlayers),
        (VoiceLineTrigger.AccessoryBroken,        1.00f,   0f, VoicePriority.High,     VoiceAudience.LocalOnly),
        (VoiceLineTrigger.AccessoryRestored,      0.60f,   5f, VoicePriority.Normal,   VoiceAudience.LocalOnly),

        (VoiceLineTrigger.HeroExceptionalEvent,   1.00f,  20f, VoicePriority.High,     VoiceAudience.NearbyPlayers),
    };

    [MenuItem("Tools/RiftRaiders/Generate Voice Content")]
    private static void Generate()
    {
        EnsureFolder();

        GenerateConfig();
        GenerateBanks();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void GenerateConfig()
    {
        string path = $"{Folder}/VoiceLineConfig.asset";
        var config = AssetDatabase.LoadAssetAtPath<VoiceLineConfig>(path);
        bool created = config == null;

        if (created)
        {
            config = ScriptableObject.CreateInstance<VoiceLineConfig>();
            AssetDatabase.CreateAsset(config, path);
        }

        config.rules = Rules.Select(r => new VoiceLineConfig.Rule
        {
            // Stamped here as well as in OnValidate - a programmatic edit doesn't trigger OnValidate,
            // so a freshly generated config would otherwise show unlabelled rows until touched.
            name = r.Trigger.ToString(),
            Trigger = r.Trigger,
            Probability = r.Probability,
            Cooldown = r.Cooldown,
            Priority = r.Priority,
            Audience = r.Audience,
        }).ToList();

        EditorUtility.SetDirty(config);

        LogHelper.Log(LogTag, $"{(created ? "Created" : "Updated")} {path} with {config.rules.Count} rules.", config);
    }

    // One bank per HeroId, with its CharacterData resolved by name so the hero -> HeroId link is
    // pre-wired. A hero whose asset can't be found still gets a bank - it just needs the reference
    // assigning by hand, which is better than skipping the hero silently.
    private static void GenerateBanks()
    {
        Dictionary<string, CharacterData> byName = AssetDatabase
            .FindAssets("t:CharacterData")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<CharacterData>)
            .Where(d => d != null)
            .ToDictionary(d => d.name, d => d, System.StringComparer.OrdinalIgnoreCase);

        foreach (HeroId hero in System.Enum.GetValues(typeof(HeroId)))
        {
            if (hero == HeroId.None)
                continue;

            string path = $"{Folder}/{hero}VoiceBank.asset";
            var bank = AssetDatabase.LoadAssetAtPath<HeroVoiceBank>(path);
            bool created = bank == null;

            if (created)
            {
                bank = ScriptableObject.CreateInstance<HeroVoiceBank>();
                AssetDatabase.CreateAsset(bank, path);
            }

            bank.hero = hero;

            // Only fill it in when empty - never overwrite a link someone corrected by hand.
            if (bank.characterData == null)
            {
                byName.TryGetValue($"{hero}CharacterData", out CharacterData data);
                bank.characterData = data;
            }

            EditorUtility.SetDirty(bank);

            string linked = bank.characterData != null ? bank.characterData.name : "<CharacterData NOT FOUND - assign by hand>";
            LogHelper.Log(LogTag, $"{(created ? "Created" : "Updated")} {Path.GetFileName(path)} -> {linked}", bank);
        }
    }

    private static void EnsureFolder()
    {
        if (AssetDatabase.IsValidFolder(Folder))
            return;

        if (AssetDatabase.IsValidFolder("Assets/_Project/Audio") == false)
            AssetDatabase.CreateFolder("Assets/_Project", "Audio");

        AssetDatabase.CreateFolder("Assets/_Project/Audio", "Voice");
    }
}
