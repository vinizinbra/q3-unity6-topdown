using System.Collections.Generic;
using Lofelt.NiceVibrations;
using UnityEngine;

// The project's own preset vocabulary, so nothing outside this folder references Nice Vibrations
// directly (SoundData, HapticsConfig and every call site only ever see this enum). None = 0 on purpose:
// every existing SoundData deserializes to "no haptic" without being touched.
public enum HapticPreset
{
    None = 0,
    Selection,
    Light,
    Medium,
    Heavy,
    Rigid,
    Soft,
    Success,
    Warning,
    Failure,
}

// A preset plus its own minimum re-trigger gap - what HapticsConfig authors per event. A class (not a
// struct) so its identity can key the cooldown table.
[System.Serializable]
public class HapticCue
{
    public HapticPreset preset;

    [Min(0f), Tooltip("Minimum seconds between two plays of this cue. Stops a burst (a shotgun crit, a DoT on you) from turning into a constant buzz.")]
    public float cooldown;

    public HapticCue(HapticPreset preset, float cooldown = 0f)
    {
        this.preset = preset;
        this.cooldown = cooldown;
    }
}

// A constant vibration: what the single-impact presets become on Android/gamepads (see HapticsConfig).
[System.Serializable]
public class HapticPulse
{
    [Range(0f, 1f)] public float amplitude;
    [Min(0f), Tooltip("Seconds.")] public float duration;

    public HapticPulse(float amplitude, float duration)
    {
        this.amplitude = amplitude;
        this.duration = duration;
    }
}

/// <summary>
/// The single entry point to device haptics (phone vibration, gamepad rumble on desktop) - nothing else in the
/// project calls Nice Vibrations. Owns the player's on/off setting (persisted) and one arbitration rule: a weaker
/// preset never interrupts a stronger one still playing, so a UI tap can't cut short a Downed thud.
///
/// Who triggers it: SoundData.haptic (fired by AudioManager when that sound actually plays), HapticsDirector (UI
/// taps + gameplay events, tuned in HapticsConfig). See docs/haptics.md.
/// </summary>
public static class Haptics
{
    private const string PrefKey = "haptics_enabled";

    private static bool? enabled;
    private static readonly Dictionary<HapticCue, float> lastCueTime = new();
    private static int playingStrength;
    private static float playingUntil;

    public static bool Enabled
    {
        get
        {
            enabled ??= PlayerPrefs.GetInt(PrefKey, 1) == 1;
            return enabled.Value;
        }
        set
        {
            enabled = value;
            PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
            PlayerPrefs.Save();

            if (value == false)
                HapticController.Stop();
        }
    }

    public static void Play(HapticCue cue)
    {
        if (cue == null || cue.preset == HapticPreset.None)
            return;

        if (cue.cooldown > 0f)
        {
            float now = Time.unscaledTime;
            if (lastCueTime.TryGetValue(cue, out float last) && now - last < cue.cooldown)
                return;

            lastCueTime[cue] = now;
        }

        Play(cue.preset);
    }

    public static void Play(HapticPreset preset)
    {
        if (preset == HapticPreset.None || Enabled == false)
            return;

        float now = Time.unscaledTime;
        int strength = Strength(preset);
        if (now < playingUntil && strength < playingStrength)
            return;

        playingStrength = strength;

#if !UNITY_IOS || UNITY_EDITOR
        // Android and gamepads play presets as raw amplitude pulses, and the single-impact ones are too short/weak
        // to feel there (Selection = 40 ms at 47 %, Light = 20 ms at 1 %: a pad's motor doesn't even spin up). They
        // get a tuned constant pulse instead (HapticsConfig). iOS keeps the native Taptic presets, which are fine.
        HapticPulse pulse = HapticsConfig.Instance.GetPulse(preset);
        if (pulse != null && pulse.duration > 0f)
        {
            playingUntil = now + pulse.duration;
            HapticPatterns.PlayConstant(pulse.amplitude, 0.5f, pulse.duration);
            return;
        }

        // PlayConstant leaves its amplitude on the shared clipLevel, which would scale this preset too.
        HapticController.clipLevel = 1f;
#endif

        HapticPatterns.PresetType type = ToPresetType(preset);
        playingUntil = now + HapticPatterns.GetPresetDuration(type);
        HapticPatterns.PlayPreset(type);
    }

    // Arbitration order only - how hard a preset reads, not its exact amplitude.
    private static int Strength(HapticPreset preset) => preset switch
    {
        HapticPreset.Selection => 0,
        HapticPreset.Soft => 1,
        HapticPreset.Light => 1,
        HapticPreset.Medium => 2,
        HapticPreset.Rigid => 2,
        HapticPreset.Success => 3,
        HapticPreset.Warning => 3,
        HapticPreset.Heavy => 4,
        HapticPreset.Failure => 4,
        _ => 0,
    };

    private static HapticPatterns.PresetType ToPresetType(HapticPreset preset) => preset switch
    {
        HapticPreset.Selection => HapticPatterns.PresetType.Selection,
        HapticPreset.Light => HapticPatterns.PresetType.LightImpact,
        HapticPreset.Medium => HapticPatterns.PresetType.MediumImpact,
        HapticPreset.Heavy => HapticPatterns.PresetType.HeavyImpact,
        HapticPreset.Rigid => HapticPatterns.PresetType.RigidImpact,
        HapticPreset.Soft => HapticPatterns.PresetType.SoftImpact,
        HapticPreset.Success => HapticPatterns.PresetType.Success,
        HapticPreset.Warning => HapticPatterns.PresetType.Warning,
        HapticPreset.Failure => HapticPatterns.PresetType.Failure,
        _ => HapticPatterns.PresetType.None,
    };
}
