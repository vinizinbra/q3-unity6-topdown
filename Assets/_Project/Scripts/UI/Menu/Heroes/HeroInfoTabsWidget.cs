using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Skill / Passive / Mastery tab strip of the hero info panel; one page visible at a time.</summary>
public class HeroInfoTabsWidget : MonoBehaviour
{
    [Serializable]
    private class Entry
    {
        public Button button;
        public Image background;
        public TMP_Text label;
        public GameObject page;
    }

    [SerializeField] private Entry[] tabs;
    [SerializeField] private int startIndex;

    [Header("Colors")]
    [SerializeField] private Color activeBackground = Color.white;
    [SerializeField] private Color idleBackground = new Color(0.80f, 0.82f, 0.87f);
    [SerializeField] private Color activeLabel = new Color(0.10f, 0.11f, 0.15f);
    [SerializeField] private Color idleLabel = new Color(0.45f, 0.47f, 0.53f);

    public int SelectedIndex { get; private set; } = -1;

    private void Awake()
    {
        for (int i = 0; i < tabs.Length; i++)
        {
            int index = i;
            tabs[i].button.onClick.AddListener(() => Select(index));
        }
    }

    private void Start()
    {
        Select(startIndex);
    }

    public void Select(int index)
    {
        if (index < 0 || index >= tabs.Length)
            return;

        SelectedIndex = index;
        for (int i = 0; i < tabs.Length; i++)
        {
            bool active = i == index;
            tabs[i].background.color = active ? activeBackground : idleBackground;
            tabs[i].label.color = active ? activeLabel : idleLabel;
            if (tabs[i].page != null)
                tabs[i].page.SetActive(active);
        }
    }
}
