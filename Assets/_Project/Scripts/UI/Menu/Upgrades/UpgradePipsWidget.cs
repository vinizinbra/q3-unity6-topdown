using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Row of level pips (filled up to the current level). The template child is cloned per level.</summary>
public class UpgradePipsWidget : MonoBehaviour
{
    [SerializeField] private Image pipTemplate;
    [SerializeField] private Color emptyColor = new Color(0.85f, 0.86f, 0.89f);

    private readonly List<Image> pips = new List<Image>();

    public void Set(int maxLevel, int level, Color filledColor)
    {
        pipTemplate.gameObject.SetActive(false);

        while (pips.Count < maxLevel)
            pips.Add(Instantiate(pipTemplate, pipTemplate.transform.parent));

        for (int i = 0; i < pips.Count; i++)
        {
            bool used = i < maxLevel;
            pips[i].gameObject.SetActive(used);
            if (used)
                pips[i].color = i < level ? filledColor : emptyColor;
        }
    }
}
