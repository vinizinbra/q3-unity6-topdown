using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One reusable icon+text row - a single (Sprite, string) pair rendered as an Image/TMP_Text pair.
// Backs InteractionPromptWidget's own Optional Team Challenge "rules" list (a pooled row per
// ChallengeDefinition.Rules entry, see docs/optional-team-challenge.md) and its single reward
// preview slot (also reused by Traversal Challenge, see docs/traversal-challenge.md). Deliberately
// dumb - just fills in whatever it's given, no state of its own beyond the last Setup call.
public class IconTextRowWidget : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text text;

    public void Setup(Sprite sprite, string label)
    {
        if (icon != null)
        {
            icon.sprite = sprite;
            icon.gameObject.SetActive(sprite != null);
        }

        if (text != null)
            text.text = label;
    }
}
