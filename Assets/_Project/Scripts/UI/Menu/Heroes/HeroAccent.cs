using UnityEngine;

/// <summary>Derives the colours a hero's UI uses from its signature colour (CharacterData.RingColor).</summary>
public static class HeroAccent
{
    /// <summary>For glows and backdrops: the hero colour, kept saturated enough to read on the light menu background.</summary>
    public static Color Vivid(Color ring)
    {
        Color.RGBToHSV(ring, out float h, out float s, out float v);
        return Color.HSVToRGB(h, Mathf.Max(s, 0.55f), Mathf.Max(v, 0.85f));
    }

    /// <summary>For a solid fill that carries text (the selected list row): darker so white text stays readable.</summary>
    public static Color Fill(Color ring)
    {
        Color.RGBToHSV(ring, out float h, out float s, out float v);
        return Color.HSVToRGB(h, Mathf.Max(s, 0.6f), Mathf.Min(v, 0.86f));
    }
}
