using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The player-facing "Quality" setting: the URP render scale (how many pixels the 3D camera renders; UI is not
/// affected), in steps of 10 % from 100 down to 20 %. A cheap way to buy frame rate on weaker phones. Mobile only
/// (<see cref="IsAvailable"/>). Persisted; applied at startup once the player has chosen a step. Without a saved
/// choice the pipeline asset's own scale (the shipped Mobile default) is left alone and its nearest step is shown.
///
/// The Editor never writes the scale: the URP asset is a shared project asset there, not a runtime copy.
/// </summary>
public static class RenderScaleSetting
{
    public static readonly float[] Levels = { 1f, 0.9f, 0.8f, 0.7f, 0.6f, 0.5f, 0.4f, 0.3f, 0.2f };

    private const string PrefKey = "render_scale_index";

    private static int? index;

    /// <summary>Only phones and tablets get the setting.</summary>
    public static bool IsAvailable => Application.isMobilePlatform;

    private static UniversalRenderPipelineAsset Pipeline => GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

    public static int Index
    {
        get
        {
            if (index == null)
                index = PlayerPrefs.HasKey(PrefKey) ? Mathf.Clamp(PlayerPrefs.GetInt(PrefKey), 0, Levels.Length - 1) : NearestStep(Pipeline != null ? Pipeline.renderScale : 1f);

            return index.Value;
        }
        set
        {
            index = Mathf.Clamp(value, 0, Levels.Length - 1);
            PlayerPrefs.SetInt(PrefKey, index.Value);
            PlayerPrefs.Save();
            Apply();
        }
    }

    public static string Label(int step) => Mathf.RoundToInt(Levels[Mathf.Clamp(step, 0, Levels.Length - 1)] * 100f) + "%";

    public static string[] Labels()
    {
        var labels = new string[Levels.Length];
        for (int i = 0; i < labels.Length; i++)
            labels[i] = Label(i);
        return labels;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplySavedAtStartup()
    {
        if (PlayerPrefs.HasKey(PrefKey))
            Apply();
    }

    private static void Apply()
    {
        if (!IsAvailable)
            return;

#if !UNITY_EDITOR
        UniversalRenderPipelineAsset urp = Pipeline;
        if (urp != null)
            urp.renderScale = Levels[Index];
#endif
    }

    private static int NearestStep(float scale)
    {
        int best = 0;
        for (int i = 1; i < Levels.Length; i++)
        {
            if (Mathf.Abs(Levels[i] - scale) < Mathf.Abs(Levels[best] - scale))
                best = i;
        }

        return best;
    }
}
