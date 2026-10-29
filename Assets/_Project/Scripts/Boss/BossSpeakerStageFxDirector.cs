using Quantum;
using UnityEngine;

/// <summary>
/// View side of Riff Riot's "Som no Talo" (sim: SpeakerStageDeliveryData / BossSpeakerStageSystem). Self-installed
/// like BossDefeatSequenceDirector, so there is nothing to place in a scene. Only the two beats that have no
/// EnemyActionData step of their own to hang particles on:
///
///   spawn  - each speaker bursts in with the Rift Pink blast + a small crystal spray at its feet (no placement pose)
///   broken - both speakers destroyed: flash, Rift Pink blast + crystal spray on the boss, shake - the exposure tell
///            (the boss's forced Kneel pose plays the rest)
///
/// Everything else (each emission's warning/burst, the speaker's death pop via EnemyExploded + its pink
/// ExplosionColor, the boss's own attacks) is authored on the action/enemy assets. Reuses the defeat director's
/// Rift Pink effects so the whole fight speaks the same visual language.
/// </summary>
public class BossSpeakerStageFxDirector : MonoBehaviour
{
    private static BossSpeakerStageFxDirector instance;

    [Header("Speaker spawn")]
    [SerializeField] private float spawnExplosionScale = 2.2f;
    [SerializeField] private float spawnCrystalScale = 0.45f;
    [SerializeField] private float spawnShakeImpact = 0.6f;

    [Header("Both speakers destroyed")]
    [SerializeField] private float brokenFlashAlpha = 0.35f;
    [SerializeField] private float brokenFlashFade = 0.3f;
    [SerializeField] private float brokenExplosionScale = 4f;
    [SerializeField] private float brokenCrystalScale = 0.8f;
    [SerializeField] private float brokenShakeAmplitude = 0.45f;
    [SerializeField] private float brokenShakeDuration = 0.35f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (instance != null)
            return;

        var go = new GameObject(nameof(BossSpeakerStageFxDirector));
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        instance = go.AddComponent<BossSpeakerStageFxDirector>();
    }

    private void Awake()
    {
        QuantumEvent.Subscribe<EventBossSpeakerSpawned>(this, OnSpeakerSpawned);
        QuantumEvent.Subscribe<EventBossSpeakerStageBroken>(this, OnStageBroken);
    }

    private void OnDestroy()
    {
        QuantumEvent.UnsubscribeListener(this);
    }

    private void OnSpeakerSpawned(EventBossSpeakerSpawned e)
    {
        BossDefeatSequenceDirector fx = BossDefeatSequenceDirector.Instance;
        Vector3 position = e.Position.ToUnityVector3();

        if (fx != null)
        {
            fx.PlayPinkExplosion(position, spawnExplosionScale);
            fx.PlayCrystalSparks(position, spawnCrystalScale);
        }

        FollowCamera.I?.ShakeAtPosition(position, spawnShakeImpact);
    }

    private void OnStageBroken(EventBossSpeakerStageBroken e)
    {
        BossDefeatSequenceDirector fx = BossDefeatSequenceDirector.Instance;
        Vector3 position = e.Position.ToUnityVector3();

        if (fx != null)
        {
            fx.FlashScreen(brokenFlashAlpha, brokenFlashFade);
            fx.PlayPinkExplosion(position, brokenExplosionScale);
            fx.PlayCrystalSparks(position, brokenCrystalScale);
        }

        FollowCamera.I?.Shake(brokenShakeAmplitude, brokenShakeDuration, 22f);
    }
}
