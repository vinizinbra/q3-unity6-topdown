using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Quantum;

/// <summary>
/// The MASTERY page of the Heroes tab: the hero's level-by-level reward track (LV 1..N), each row showing its reward and
/// whether it's claimed (below the hero's level), current or still locked. MOCK for now: the rewards are authored here
/// (<see cref="rewards"/>) and the level is the Heroes tab's placeholder - replace both once hero progression exists.
/// Rows are cloned from an in-scene template (see ListTemplateUtility).
/// </summary>
public class HeroMasteryViewWidget : MonoBehaviour
{
    [Serializable]
    public struct Reward
    {
        [Tooltip("{0} is the hero's name, e.g. \"{0} Portrait Border\".")]
        public string text;
        [Tooltip("Empty = the hero's own portrait.")]
        public Sprite icon;
        [Tooltip("An emote reward: the icon is the viewed hero's own emoji of this kind (CharacterData.Emojis).")]
        public bool isEmote;
        public EmojiKind emote;
    }

    [SerializeField] private MasteryRowWidget rowTemplate;
    [SerializeField] private Transform listRoot;

    [Header("Texts")]
    [SerializeField] private TMP_Text heroNameText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField, TextArea(1, 3)] private string descriptionFormat = "Hero mastery is unlocked by playing runs only. Gain Hero XP to unlock rewards for {0}.";

    [Header("Mock rewards (LV 1 first)")]
    [SerializeField] private List<Reward> rewards = new List<Reward>();

    private readonly List<MasteryRowWidget> rows = new List<MasteryRowWidget>();

    public void Show(string heroName, Sprite heroPortrait, int heroLevel, CharacterData hero)
    {
        if (heroNameText != null)
            heroNameText.text = heroName;
        if (descriptionText != null)
            descriptionText.text = string.Format(descriptionFormat, heroName);

        if (rowTemplate == null || listRoot == null)
            return;

        if (rows.Count != rewards.Count)
        {
            ListTemplateUtility.Hide(rowTemplate);
            ListTemplateUtility.Clear(listRoot, rowTemplate);
            rows.Clear();
            for (int i = 0; i < rewards.Count; i++)
            {
                MasteryRowWidget row = ListTemplateUtility.Spawn(rowTemplate, listRoot);
                row.name = "Mastery_LV" + (i + 1);
                rows.Add(row);
            }
        }

        for (int i = 0; i < rows.Count; i++)
        {
            int level = i + 1;
            MasteryRowState state = level < heroLevel ? MasteryRowState.Claimed : level == heroLevel ? MasteryRowState.Current : MasteryRowState.Locked;
            Reward reward = rewards[i];
            rows[i].Bind(level, string.Format(reward.text ?? string.Empty, heroName), IconOf(reward, hero, heroPortrait), state,
                i == 0, i == rows.Count - 1);
        }
    }

    // An emote is the viewed hero's own emoji (same lookup as EmojiPresentation.GetSprite); then the authored icon; then the portrait.
    private static Sprite IconOf(Reward reward, CharacterData hero, Sprite heroPortrait)
    {
        if (reward.isEmote && hero != null && hero.Emojis != null)
        {
            int index = (int)reward.emote;
            if (index >= 0 && index < hero.Emojis.Length && hero.Emojis[index] != null)
                return hero.Emojis[index];
        }

        return reward.icon != null ? reward.icon : heroPortrait;
    }
}
