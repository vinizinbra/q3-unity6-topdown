using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The "selected" look of a list card/row/tile: any number of graphics (Images or texts), each with its own
/// colour at rest and while selected, plus objects that are only active while selected. Serialized on the card
/// widget; the widget calls <see cref="Apply"/> from its SetSelected, so what changes on selection is authored
/// in the prefab, not hard-coded.
/// </summary>
[Serializable]
public class SelectionStyle
{
    [Serializable]
    public struct Target
    {
        public Graphic graphic;
        public Color idleColor;
        public Color selectedColor;

        public Target(Graphic graphic, Color idleColor, Color selectedColor)
        {
            this.graphic = graphic;
            this.idleColor = idleColor;
            this.selectedColor = selectedColor;
        }
    }

    [SerializeField] private Target[] targets = new Target[0];

    [SerializeField, Tooltip("Active only while selected (a selection frame, an arrow, a glow...).")]
    private GameObject[] selectedObjects = new GameObject[0];

    public void Apply(bool selected)
    {
        foreach (Target target in targets)
        {
            if (target.graphic != null)
                target.graphic.color = selected ? target.selectedColor : target.idleColor;
        }

        foreach (GameObject go in selectedObjects)
        {
            if (go != null && go.activeSelf != selected)
                go.SetActive(selected);
        }
    }
}
