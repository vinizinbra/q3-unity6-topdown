using System.Collections.Generic;
using Quantum;
using QuantumUser.View.Managers;
using UnityEngine;

/// <summary>
/// View side of the boss hazards first built for Dono do Céu (sim: BossTornadoSystem, BossCrowdBarrageSystem). Self-installed
/// like BossSpeakerStageFxDirector - nothing to place in a scene; prefabs come from Resources/DonoDoCeu (built by
/// DonoDoCeuBossBuilder). Only presents simulation state:
///
///   tornado pulse      - the expiry pulse burst (BossTornadoPulse)
///   tornado dissipate  - a harmless wind puff when a tornado is cancelled (BossTornadoDissipated)
///   crowd mark         - a pulsing ring under every player that currently has a CrowdMark, plus a trash-can icon over
///                        its head that pops in and bobs - "the crowd is throwing at YOU" (polled each frame, so both
///                        appear/disappear exactly with the sim state; released when the mark or the player goes)
/// </summary>
public class BossHazardFxDirector : MonoBehaviour
{
    private const string PulseResource = "DonoDoCeu/DonoDoCeu_TornadoPulse";
    private const string DissipateResource = "DonoDoCeu/DonoDoCeu_TornadoDissipate";
    private const string MarkResource = "DonoDoCeu/DonoDoCeu_CrowdMark";
    private const string MarkHeadResource = "DonoDoCeu/DonoDoCeu_CrowdMarkHead";

    private static BossHazardFxDirector instance;

    [SerializeField] private float pulseScalePerRadius = 1f;
    [SerializeField] private float dissipateScale = 1.6f;
    [SerializeField] private float pulseShakeImpact = 1f;
    [SerializeField] private float markHeight = 0.08f;
    [SerializeField] private float markHeadHeight = 2.7f;
    [SerializeField] private float markHeadBob = 0.12f;
    [SerializeField] private float markHeadPopTime = 0.25f;

    private ParticleSystem _pulsePrefab;
    private ParticleSystem _dissipatePrefab;
    private GameObject _markPrefab;
    private GameObject _markHeadPrefab;
    private QuantumEntityViewUpdater _updater;

    private readonly Dictionary<EntityRef, GameObject> _marks = new();
    private readonly Dictionary<EntityRef, (GameObject marker, float since)> _markHeads = new();
    private readonly List<EntityRef> _stale = new();
    private readonly HashSet<EntityRef> _seen = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (instance != null)
            return;

        var go = new GameObject(nameof(BossHazardFxDirector));
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        instance = go.AddComponent<BossHazardFxDirector>();
    }

    private void Awake()
    {
        _pulsePrefab = Resources.Load<ParticleSystem>(PulseResource);
        _dissipatePrefab = Resources.Load<ParticleSystem>(DissipateResource);
        _markPrefab = Resources.Load<GameObject>(MarkResource);
        _markHeadPrefab = Resources.Load<GameObject>(MarkHeadResource);

        QuantumEvent.Subscribe<EventBossTornadoPulse>(this, OnTornadoPulse);
        QuantumEvent.Subscribe<EventBossTornadoDissipated>(this, OnTornadoDissipated);
    }

    private void OnDestroy()
    {
        QuantumEvent.UnsubscribeListener(this);
    }

    private void OnTornadoPulse(EventBossTornadoPulse e)
    {
        Vector3 position = e.Position.ToUnityVector3();

        if (_pulsePrefab != null)
            EffectsManager.Instance?.PlayEffect(_pulsePrefab, position, Quaternion.identity, Vector3.one * (e.Radius.AsFloat * pulseScalePerRadius));

        FollowCamera.I?.ShakeAtPosition(position, pulseShakeImpact);
    }

    private void OnTornadoDissipated(EventBossTornadoDissipated e)
    {
        if (_dissipatePrefab != null)
            EffectsManager.Instance?.PlayEffect(_dissipatePrefab, e.Position.ToUnityVector3(), Quaternion.identity, Vector3.one * dissipateScale);
    }

    // Crowd marks follow the simulation: one marker per marked player, gone the frame the mark is.
    private void LateUpdate()
    {
        QuantumGame game = QuantumRunner.Default != null ? QuantumRunner.Default.Game : null;
        Frame frame = game?.Frames.Predicted;

        _seen.Clear();

        if (frame != null && _markPrefab != null)
        {
            var marks = frame.Filter<CrowdMark>();
            while (marks.Next(out EntityRef player, out CrowdMark _))
            {
                Transform view = ResolveView(player);
                if (view == null)
                    continue;

                _seen.Add(player);

                if (_marks.TryGetValue(player, out GameObject marker) == false || marker == null)
                {
                    marker = Instantiate(_markPrefab);
                    _marks[player] = marker;
                }

                marker.transform.position = view.position + Vector3.up * markHeight;
                PlaceHead(player, view.position);
            }
        }

        _stale.Clear();
        foreach (KeyValuePair<EntityRef, GameObject> pair in _marks)
        {
            if (_seen.Contains(pair.Key) == false)
                _stale.Add(pair.Key);
        }

        for (int i = 0; i < _stale.Count; i++)
        {
            if (_marks[_stale[i]] != null)
                Destroy(_marks[_stale[i]]);

            _marks.Remove(_stale[i]);

            if (_markHeads.TryGetValue(_stale[i], out var head) == true)
            {
                if (head.marker != null)
                    Destroy(head.marker);

                _markHeads.Remove(_stale[i]);
            }
        }
    }

    // Over the head: pops in (overshoot) when the mark lands, then bobs until it goes.
    private void PlaceHead(EntityRef player, Vector3 feet)
    {
        if (_markHeadPrefab == null)
            return;

        if (_markHeads.TryGetValue(player, out var head) == false || head.marker == null)
        {
            head = (Instantiate(_markHeadPrefab), Time.time);
            _markHeads[player] = head;
        }

        float age = Time.time - head.since;
        float pop = age < markHeadPopTime ? Mathf.Sin(Mathf.Clamp01(age / markHeadPopTime) * Mathf.PI * 0.75f) / 0.7071f : 1f;
        head.marker.transform.localScale = Vector3.one * pop;
        head.marker.transform.position = feet + Vector3.up * (markHeadHeight + Mathf.Sin(age * 7f) * markHeadBob);
    }

    private Transform ResolveView(EntityRef entity)
    {
        if (_updater == null)
            _updater = FindFirstObjectByType<QuantumEntityViewUpdater>();

        QuantumEntityView view = _updater != null ? _updater.GetView(entity) : null;
        return view != null ? view.transform : null;
    }
}
