using System.Collections.Generic;
using Quantum;
using UnityEngine;

/// <summary>
/// View of BreathConeDeliveryData (the Frost Patriarch's Polar Breath) while it channels: the delivery's looping
/// BreathEffect at the mouth and its AreaDecal (the standard red cone, real range and angle) on the floor, both turned
/// every frame by the simulation's live Aim.Angle - the same direction the hit cone uses, so what players see always
/// matches where the breath hurts. Shown only while the breather is in the Active phase of a breath action; the effect
/// is stopped (lets its particles finish) and the decal removed the moment it isn't (end, stagger, death).
/// Self-installed, polls the predicted frame; nothing to place in a scene.
/// </summary>
public class BreathConeFxDirector : MonoBehaviour
{
    private static BreathConeFxDirector instance;

    [SerializeField] private float decalAlpha = 0.3f;
    [SerializeField] private float groundProbeHeight = 20f;
    [SerializeField] private float floorLift = 0.05f;

    private sealed class Live
    {
        public ParticleSystem Effect;
        public GameObject Decal;
    }

    private readonly Dictionary<EntityRef, Live> _live = new();
    private readonly HashSet<EntityRef> _seen = new();
    private readonly List<EntityRef> _stale = new();
    private int _groundMask;
    private QuantumEntityViewUpdater _updater;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (instance != null)
            return;

        var go = new GameObject(nameof(BreathConeFxDirector));
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        instance = go.AddComponent<BreathConeFxDirector>();
    }

    private void Awake()
    {
        _groundMask = UnityEngine.LayerMask.GetMask("Ground");
    }

    private void LateUpdate()
    {
        QuantumGame game = QuantumRunner.Default != null ? QuantumRunner.Default.Game : null;
        Frame frame = game?.Frames.Predicted;
        _seen.Clear();

        if (frame != null)
        {
            var enemies = frame.Filter<Enemy, Transform3D, Aim>();
            while (enemies.Next(out EntityRef entity, out Enemy enemy, out Transform3D transform, out Aim aim))
            {
                if (enemy.Phase != EnemyActionPhase.Active)
                    continue;

                EnemyActionData action = EnemyDecisionUtility.ResolveAction(frame, frame.FindAsset(enemy.EnemyData), enemy.CurrentActionSlot);
                if (action == null || frame.FindAsset(action.Delivery) is not BreathConeDeliveryData config)
                    continue;

                _seen.Add(entity);
                Draw(entity, transform.Position.ToUnityVector3(), aim.Angle.AsFloat, config, action.DamageRange.AsFloat);
            }
        }

        _stale.Clear();
        foreach (var pair in _live)
        {
            if (_seen.Contains(pair.Key) == false)
                _stale.Add(pair.Key);
        }

        for (int i = 0; i < _stale.Count; i++)
        {
            Release(_live[_stale[i]]);
            _live.Remove(_stale[i]);
        }
    }

    private void Draw(EntityRef entity, Vector3 position, float angle, BreathConeDeliveryData config, float range)
    {
        if (_live.TryGetValue(entity, out Live live) == false)
        {
            live = new Live();

            if (config.BreathEffect != null)
            {
                live.Effect = Instantiate(config.BreathEffect, transform);
                live.Effect.transform.localScale = Vector3.one * config.EffectScale;
                live.Effect.Play(true);
            }

            if (config.AreaDecal != null)
                live.Decal = CreateDecal(config.AreaDecal);

            _live[entity] = live;
        }

        Quaternion facing = Quaternion.Euler(0f, angle, 0f);
        Vector3 forward = facing * Vector3.forward;

        if (live.Effect != null)
            live.Effect.transform.SetPositionAndRotation(ResolveMouth(entity, position, facing, config), facing);

        if (live.Decal != null)
        {
            float groundY = Physics.Raycast(position + Vector3.up * groundProbeHeight, Vector3.down, out RaycastHit hit, groundProbeHeight * 2f, _groundMask)
                ? hit.point.y
                : position.y;

            // Same placement as the Cone telegraph (EnemyAttackVisualsView): apex at the breather, flat, scaled to the range.
            live.Decal.transform.SetPositionAndRotation(new Vector3(position.x, groundY + floorLift, position.z), Quaternion.LookRotation(Vector3.up, forward));
            live.Decal.transform.localScale = new Vector3(range * 2f, range * 2f, 1f);
        }
    }

    // The drawn mouth: MouthOnSprite through the rendered rig's transform (flip, billboard tilt and fit scale included);
    // the aim-frame MouthOffset if the rig isn't there.
    private Vector3 ResolveMouth(EntityRef entity, Vector3 position, Quaternion facing, BreathConeDeliveryData config)
    {
        if (config.MouthFromSprite == true)
        {
            if (_updater == null)
                _updater = FindFirstObjectByType<QuantumEntityViewUpdater>();

            QuantumEntityView view = _updater != null ? _updater.GetView(entity) : null;
            EnemyViewRig rig = view != null ? view.GetComponentInChildren<EnemyViewRig>() : null;

            if (rig != null)
                return rig.transform.TransformPoint(new Vector3(config.MouthOnSprite.x, config.MouthOnSprite.y, 0f));
        }

        return position + facing * config.MouthOffset;
    }

    // The standard cone telegraph prefab, used as a plain static decal: its fade/grow drivers (TelegraphManager-owned)
    // are switched off and every sprite is held at decalAlpha.
    private GameObject CreateDecal(GameObject prefab)
    {
        GameObject decal = Instantiate(prefab, transform);

        foreach (MonoBehaviour behaviour in decal.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour is TelegraphFade || behaviour is TelegraphGrow)
                behaviour.enabled = false;
        }

        foreach (SpriteRenderer sprite in decal.GetComponentsInChildren<SpriteRenderer>(true))
        {
            sprite.transform.localScale = sprite.transform == decal.transform ? sprite.transform.localScale : Vector3.one;
            Color color = sprite.color;
            color.a = decalAlpha;
            sprite.color = color;
        }

        return decal;
    }

    private static void Release(Live live)
    {
        if (live.Effect != null)
        {
            live.Effect.transform.SetParent(null, true);
            live.Effect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(live.Effect.gameObject, 2f);
        }

        if (live.Decal != null)
            Destroy(live.Decal);
    }
}
