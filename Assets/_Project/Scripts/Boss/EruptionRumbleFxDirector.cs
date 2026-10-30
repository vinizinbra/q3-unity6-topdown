using System.Collections.Generic;
using Quantum;
using UnityEngine;

/// <summary>
/// Anticipation for EruptionLineDeliveryData (the Frost Patriarch's Icebreaker): on every eruption warning
/// (EventProjectileLandingWarning owned by an enemy currently running that delivery) plays the delivery's WarningEffect
/// on the point - ice chips jumping off the cracking floor - for exactly the warning's countdown, then lets it fade. Like
/// the owner-bound warning decal (GroundWarningTelegraphManager), it stops early the moment the owner can no longer
/// detonate (staggered out of the action, dead, gone). The delivery may also be the running step of a
/// SequenceDeliveryData (the jump slam's frost rings). Self-installed; nothing to place in a scene.
/// </summary>
public class EruptionRumbleFxDirector : MonoBehaviour
{
    private static EruptionRumbleFxDirector instance;

    private sealed class Rumble
    {
        public ParticleSystem Effect;
        public EntityRef Owner;
        public float EndTime;
    }

    private readonly List<Rumble> _live = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (instance != null)
            return;

        var go = new GameObject(nameof(EruptionRumbleFxDirector));
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        instance = go.AddComponent<EruptionRumbleFxDirector>();
    }

    private void Awake()
    {
        QuantumEvent.Subscribe<EventProjectileLandingWarning>(this, OnWarning);
    }

    private void OnDestroy()
    {
        QuantumEvent.UnsubscribeListener(this);
    }

    private void OnWarning(EventProjectileLandingWarning e)
    {
        Frame frame = e.Game.Frames.Predicted;
        if (frame == null || e.Owner == EntityRef.None || frame.TryGet(e.Owner, out Enemy enemy) == false)
            return;

        EruptionLineDeliveryData config = ResolveConfig(frame, e.Owner, enemy);
        if (config == null || config.WarningEffect == null)
            return;

        ParticleSystem effect = Instantiate(config.WarningEffect, e.Position.ToUnityVector3(), Quaternion.identity, transform);
        effect.transform.localScale = Vector3.one * e.Radius.AsFloat;
        effect.Play(true);

        _live.Add(new Rumble { Effect = effect, Owner = e.Owner, EndTime = Time.time + e.Duration.AsFloat });
    }

    private void LateUpdate()
    {
        if (_live.Count == 0)
            return;

        QuantumGame game = QuantumRunner.Default != null ? QuantumRunner.Default.Game : null;
        Frame frame = game?.Frames.Predicted;

        for (int i = _live.Count - 1; i >= 0; i--)
        {
            Rumble rumble = _live[i];
            bool cancelled = frame == null || frame.TryGet(rumble.Owner, out Enemy enemy) == false
                || enemy.Phase != EnemyActionPhase.Active || ResolveConfig(frame, rumble.Owner, enemy) == null;

            if (Time.time < rumble.EndTime && cancelled == false)
                continue;

            if (rumble.Effect != null)
            {
                rumble.Effect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(rumble.Effect.gameObject, 1.5f);
            }

            _live.RemoveAt(i);
        }
    }

    private static EruptionLineDeliveryData ResolveConfig(Frame frame, EntityRef entity, Enemy enemy)
    {
        EnemyActionData action = EnemyDecisionUtility.ResolveAction(frame, frame.FindAsset(enemy.EnemyData), enemy.CurrentActionSlot);
        EnemyDeliveryData delivery = action != null ? frame.FindAsset(action.Delivery) : null;

        if (delivery is SequenceDeliveryData sequence && frame.TryGet(entity, out EnemySequenceState state) == true &&
            state.CurrentStepIndex >= 0 && state.CurrentStepIndex < sequence.Steps.Count)
        {
            EnemyActionData step = frame.FindAsset(sequence.Steps[state.CurrentStepIndex]);
            delivery = step != null ? frame.FindAsset(step.Delivery) : null;
        }

        return delivery as EruptionLineDeliveryData;
    }
}
