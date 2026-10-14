using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum MasteryRowState
{
    Claimed,
    Current,
    Locked
}

/// <summary>
/// One level of the Mastery track: a timeline dot + segment, the LV badge, the reward icon and text, and a state mark
/// (check = claimed, dot = current level, padlock = locked). The current level's row is highlighted.
/// </summary>
public class MasteryRowWidget : MonoBehaviour
{
    [SerializeField] private Image background;
    [SerializeField] private Image badge;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text rewardText;

    [Header("State marks")]
    [SerializeField] private GameObject claimedMark;
    [SerializeField] private GameObject currentMark;
    [SerializeField] private GameObject lockedMark;

    [Header("Timeline")]
    [SerializeField] private Image dot;
    [SerializeField, Tooltip("Segment above the dot (hidden on the first row).")] private Image lineUp;
    [SerializeField, Tooltip("Segment below the dot (hidden on the last row).")] private Image lineDown;

    [Header("Colours")]
    [SerializeField] private Color accent = new Color(0.992f, 0.224f, 0.443f);
    [SerializeField] private Color ink = new Color(0.10f, 0.11f, 0.15f);
    [SerializeField] private Color rowColor = Color.white;
    [SerializeField] private Color lockedRowColor = new Color(0.93f, 0.94f, 0.96f);
    [SerializeField] private Color lockedTextColor = new Color(0.30f, 0.32f, 0.38f);

    public void Bind(int level, string reward, Sprite rewardIcon, MasteryRowState state, bool first, bool last)
    {
        bool current = state == MasteryRowState.Current;
        bool reached = state != MasteryRowState.Locked;

        levelText.text = "<size=70%>LV</size> " + level;
        rewardText.text = reward;
        rewardText.color = current ? Color.white : reached ? ink : lockedTextColor;
        icon.sprite = rewardIcon;
        icon.enabled = rewardIcon != null;

        background.color = current ? accent : reached ? rowColor : lockedRowColor;
        badge.color = current ? Color.Lerp(accent, ink, 0.25f) : ink;

        claimedMark.SetActive(state == MasteryRowState.Claimed);
        currentMark.SetActive(current);
        lockedMark.SetActive(state == MasteryRowState.Locked);

        dot.color = reached ? accent : ink;
        lineUp.gameObject.SetActive(!first);
        lineDown.gameObject.SetActive(!last);
        lineUp.color = reached ? accent : ink;
        lineDown.color = state == MasteryRowState.Claimed ? accent : ink;
    }
}
