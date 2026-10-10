using UnityEngine;

/// <summary>
/// Which haptic each event HapticsDirector listens to plays - the one place to tune or switch off a gameplay haptic
/// (preset None = off). Loaded from Resources/HapticsConfig; without the asset the defaults below are used.
///
/// Sound-driven haptics are NOT here: a moment that already plays a SoundData gets its haptic on that asset instead
/// (SoundData.haptic), so it can never drift out of sync with its sound.
/// </summary>
[CreateAssetMenu(fileName = "HapticsConfig", menuName = "RiftRaiders/Haptics Config")]
public class HapticsConfig : ScriptableObject
{
    public const string ResourcePath = "HapticsConfig";

    [Header("UI")]
    [Tooltip("Pressing (or submitting) any interactable Selectable - buttons, toggles, tabs, cards.")]
    public HapticCue uiPress = new(HapticPreset.Selection, 0.03f);

    [Header("Taken (local player)")]
    public HapticCue damageTaken = new(HapticPreset.Medium, 0.25f);
    public HapticCue shieldBroken = new(HapticPreset.Heavy, 0.3f);
    [Tooltip("The Accessory blocked a hit.")]
    public HapticCue accessoryBlocked = new(HapticPreset.Rigid, 0.2f);
    [Tooltip("The Accessory popped off.")]
    public HapticCue accessoryBroken = new(HapticPreset.Heavy, 0.3f);
    public HapticCue downed = new(HapticPreset.Heavy);
    public HapticCue knockedOut = new(HapticPreset.Failure);
    [Tooltip("Revived, or reviving a teammate.")]
    public HapticCue revived = new(HapticPreset.Success);

    [Header("Dealt (local player)")]
    public HapticCue skillUsed = new(HapticPreset.Soft, 0.1f);
    public HapticCue criticalHit = new(HapticPreset.Light, 0.12f);
    public HapticCue eliteKilled = new(HapticPreset.Heavy, 0.2f);
    [Tooltip("Groundbreaker landing / wall slam - the moments that already shake the camera.")]
    public HapticCue impactSlam = new(HapticPreset.Rigid, 0.15f);

    [Header("Run (whole team)")]
    [Tooltip("A level-up or chest choice screen opens.")]
    public HapticCue upgradeScreen = new(HapticPreset.Success);
    public HapticCue bossReveal = new(HapticPreset.Heavy);
    public HapticCue bossKilled = new(HapticPreset.Heavy);
    public HapticCue victory = new(HapticPreset.Success);
    public HapticCue runFailed = new(HapticPreset.Failure);

    [Header("Android / gamepad strength")]
    [Tooltip("How each single-impact preset is played on Android and gamepads (iOS uses its native presets and ignores these). Nice Vibrations' own versions are too short/weak to feel there. ~0.05s is about the shortest a motor reliably makes felt.")]
    public HapticPulse selectionPulse = new(0.6f, 0.06f);
    public HapticPulse lightPulse = new(0.75f, 0.06f);
    public HapticPulse softPulse = new(0.6f, 0.1f);
    public HapticPulse mediumPulse = new(0.9f, 0.09f);
    public HapticPulse rigidPulse = new(1f, 0.06f);
    public HapticPulse heavyPulse = new(1f, 0.2f);

    // Success/Failure/Warning have no pulse: their own patterns are long enough everywhere.
    public HapticPulse GetPulse(HapticPreset preset) => preset switch
    {
        HapticPreset.Selection => selectionPulse,
        HapticPreset.Light => lightPulse,
        HapticPreset.Soft => softPulse,
        HapticPreset.Medium => mediumPulse,
        HapticPreset.Rigid => rigidPulse,
        HapticPreset.Heavy => heavyPulse,
        _ => null,
    };

    private static HapticsConfig instance;

    public static HapticsConfig Instance
    {
        get
        {
            if (instance == null)
            {
                instance = Resources.Load<HapticsConfig>(ResourcePath);
                if (instance == null)
                    instance = CreateInstance<HapticsConfig>();
            }

            return instance;
        }
    }
}
