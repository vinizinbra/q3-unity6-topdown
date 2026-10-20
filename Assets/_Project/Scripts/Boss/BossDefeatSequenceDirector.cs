using System.Collections;
using System.Collections.Generic;
using Quantum;
using QuantumUser.View;
using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// View side of the boss death cinematic (sim side: BossDefeatSystem, docs/boss-encounter.md). Self-installed at
/// startup like HapticsDirector, so there is nothing to place in a scene. Gameplay is hard-paused by the sim for the
/// whole sequence; this only plays the beats, timed off the event's own Duration/BurstTime:
///
///   kill       - the rig's Dying sprite swaps in (EnemyViewRig.DyingSprite, if authored), hit-stop (boss rig
///                animators/particles frozen), white flash, heavy shake, camera pans onto the boss
///   throes     - Pixie's "Unstable" mark (the ExplodeOnDeath look) swells on the boss while its rig shakes harder and
///                flickers faster - it's about to blow
///   burst      - Pixie's bomb blast, scaled up, + flash + shake; the boss vanishes, "BOSS DEFEATED", and the payout:
///                waves of Rift Shards fly to every hero (real pickup sound, climbing pitch, pickup flash) while a big
///                "+N RIFT SHARDS" tally counts up, slamming on the last one; visual-only Coins/XP orbs fly with them and a
///                spray of tumbling 3D Rift Pink crystals bursts out
///   tail       - camera eases back to the players just before gameplay resumes
///
/// Visuals reuse what already exists (Rift Pink copies of Pixie's Unstable mark + bomb blast in Resources/BossDefeat, the real Rift Shard
/// pickup visual for the shower, a runtime full-screen flash) - swap in authored VFX/audio once they exist. Tuning is in the fields below.
/// </summary>
public class BossDefeatSequenceDirector : MonoBehaviour
{
    private const string LogTag = "BossDefeat";

    private static BossDefeatSequenceDirector instance;

    // The boss intro (BossIntroSequenceDirector) reuses this director's Rift Pink effects and white flash, so the
    // two cinematics read as bookends of the same fight.
    public static BossDefeatSequenceDirector Instance => instance;

    [Header("Kill")]
    [SerializeField] private float hitStopDuration = 0.15f;
    [SerializeField] private float killFlashAlpha = 0.85f;
    [SerializeField] private float killFlashFade = 0.35f;
    [SerializeField] private float killShakeAmplitude = 0.7f;
    [SerializeField] private float killShakeDuration = 0.5f;

    [Header("Death throes")]
    [SerializeField] private float throesStartDelay = 0.3f;
    [SerializeField] private float throesFlickerInterval = 0.2f;
    [SerializeField] private float throesMaxRigJitter = 0.25f;
    [SerializeField] private Color throesFlickerColor = new Color(1f, 0.35f, 0.35f);
    [SerializeField, Tooltip("Unstable mark size relative to the boss's body width, at the start and right before the burst.")]
    private Vector2 unstableMarkScale = new Vector2(1f, 1.6f);
    [SerializeField, Tooltip("Multiplier on every Unstable particle system's own emission rate, at the start of the throes and right before the burst - it builds up like a charge.")]
    private Vector2 unstableRateRamp = new Vector2(1.5f, 5f);
    [SerializeField, Tooltip("Multiplier on every Unstable particle system's own start speed over the throes. The rays' authored speed is slightly negative (pulled inward), so this makes them get sucked into the core faster and faster.")]
    private Vector2 unstableSpeedRamp = new Vector2(1f, 25f);

    [Header("Burst")]
    [SerializeField] private float burstExplosionScale = 8f;
    [SerializeField] private float burstFlashAlpha = 1f;
    [SerializeField] private float burstFlashFade = 0.6f;
    [SerializeField] private float burstShakeAmplitude = 1.1f;
    [SerializeField] private float burstShakeDuration = 0.8f;
    [SerializeField] private string bannerText = "BOSS DEFEATED";
    [SerializeField, Tooltip("Scale of the 3D rift crystal spray at the burst (also scales its speed/spread).")]
    private float crystalSparksScale = 1.2f;

    [Header("Shard shower")]
    [SerializeField, Tooltip("Flying shards are capped at this many - each one is worth total / pieces on the tally.")]
    private int maxShardPieces = 40;
    [SerializeField, Tooltip("Seconds over which the shards leave the body - they arrive in a rolling wave instead of all at once.")]
    private float shardReleaseSpread = 1f;
    [SerializeField, Tooltip("Pickup-sound pitch climb per landed shard, in semitones (capped at shardPitchMaxSteps).")]
    private float shardPitchSemitones = 1f;
    [SerializeField] private int shardPitchMaxSteps = 12;
    [SerializeField] private float shardScatterRadius = 4f;
    [SerializeField] private float shardScatterTime = 0.45f;
    [SerializeField] private float shardFlyTime = 0.6f;
    [SerializeField, Tooltip("Visual-only Coins mixed into the shower - the real Coin pickup look, flying to the heroes like the shards, but they grant nothing (Coins are a per-run currency and the run is ending).")]
    private int decorCoinPieces = 20;
    [SerializeField, Tooltip("Visual-only XP orbs mixed into the shower - same as Coins: the real ExpOrb look, nothing granted.")]
    private int decorOrbPieces = 24;
    [SerializeField, Tooltip("Only used if the Rift Shard pickup's own view can't be resolved.")]
    private float fallbackShardSize = 0.35f;
    [SerializeField] private Color fallbackShardColor = new Color(253f / 255f, 57f / 255f, 113f / 255f); // Rift Pink #FD3971

    [Header("Tally")]
    [SerializeField] private Color tallyColor = new Color(253f / 255f, 57f / 255f, 113f / 255f); // Rift Pink #FD3971
    [SerializeField] private float tallyAmountSize = 120f;
    [SerializeField] private float tallyLabelSize = 42f;
    [SerializeField, Tooltip("Screen height fraction the tally sits at (0 = bottom).")]
    private float tallyScreenY = 0.3f;
    [SerializeField] private float tallyPunchScale = 1.12f;
    [SerializeField] private float tallyFinalPunchScale = 1.5f;
    [SerializeField] private string tallyLabel = "RIFT SHARDS";

    [Header("Tail")]
    [SerializeField] private float cameraReturnLead = 0.6f;

    private Image _flash;
    private TMP_Text _tally;
    private Coroutine _tallyPunch;
    private float _tallyShown;
    private float _tallyTotal;
    private int _shardsLanded;
    private Coroutine _sequence;
    private Coroutine _flashRoutine;

    // State of the boss rig we touched, restored when the sequence ends (the view may be pooled).
    private readonly List<Renderer> _hiddenRenderers = new();
    private Transform _rig;
    private EnemyViewRig _rigComponent;
    private SpriteRenderer[] _bodySprites = System.Array.Empty<SpriteRenderer>();
    private Color[] _bodyColors = System.Array.Empty<Color>();
    private SpriteRenderer _dyingTarget;
    private Sprite _dyingOriginalSprite;
    private ParticleSystem _unstableMark;
    private ParticleSystem[] _unstableSystems = System.Array.Empty<ParticleSystem>();
    private float[] _unstableBaseRates = System.Array.Empty<float>();
    private float[] _unstableBaseSpeeds = System.Array.Empty<float>();
    private Vector3 _bodyCenterOffset;
    private readonly Dictionary<CurrencyType, GameObject> _pickupTemplates = new();
    private GameObject _templateHolder;
    private Vector3 _rigBaseLocalPosition;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (instance != null)
            return;

        var go = new GameObject(nameof(BossDefeatSequenceDirector));
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        instance = go.AddComponent<BossDefeatSequenceDirector>();
    }

    // Rift Pink "corrupted" copies of Pixie's bomb blast (SimpleExplosion) and Unstable mark (ExplosiveMark) - warm
    // particle colors remapped onto a Rift Pink ramp. Loaded from Resources since this director has no scene/prefab
    // to hold serialized references. Missing = falls back to the original orange effects.
    private const string ExplosionResource = "BossDefeat/BossDefeatExplosion";
    private const string UnstableResource = "BossDefeat/BossDefeatUnstable";
    // Tumbling 3D Rift Pink crystals sprayed out of the burst (built by the crystal-sparks editor script - mesh,
    // material and prefab all live next to it).
    private const string CrystalSparksResource = "BossDefeat/BossDefeatCrystalSparks";

    private ParticleSystem _explosionPrefab;
    private ParticleSystem _unstablePrefab;
    private ParticleSystem _crystalSparksPrefab;

    private void Awake()
    {
        _explosionPrefab = Resources.Load<ParticleSystem>(ExplosionResource);
        _unstablePrefab = Resources.Load<ParticleSystem>(UnstableResource);
        _crystalSparksPrefab = Resources.Load<ParticleSystem>(CrystalSparksResource);

        BuildFlashOverlay();

        QuantumEvent.Subscribe<EventBossDefeated>(this, OnBossDefeated);
        QuantumEvent.Subscribe<EventBossDefeatBurst>(this, OnBossDefeatBurst);
    }

    private void OnDestroy()
    {
        QuantumEvent.UnsubscribeListener(this);
    }

    // ------------------------------------------------------------------ events

    private void OnBossDefeated(EventBossDefeated e)
    {
        if (_sequence != null)
            StopCoroutine(_sequence);

        StopUnstableMark();
        RestoreRig();
        _sequence = StartCoroutine(RunSequence(e.Game, e.Boss, e.Position.ToUnityVector3(), e.Duration.AsFloat, e.BurstTime.AsFloat));
    }

    private void OnBossDefeatBurst(EventBossDefeatBurst e)
    {
        // The blast, crystal spray and shard fountain all come out of the body's visible centre, same as the mark.
        Vector3 position = ResolveBossPosition(e.Game, e.Boss, e.Position.ToUnityVector3()) + _bodyCenterOffset;

        Flash(burstFlashAlpha, burstFlashFade);
        FollowCamera.I?.Shake(burstShakeAmplitude, burstShakeDuration, 20f);
        PlayExplosion(position, burstExplosionScale);
        PlayExplosion(position, burstExplosionScale * 0.5f);

        if (_crystalSparksPrefab != null)
            EffectsManager.Instance?.PlayEffect(_crystalSparksPrefab, position, Quaternion.identity, Vector3.one * crystalSparksScale);
        Haptics.Play(HapticsConfig.Instance.bossKilled);

        StopUnstableMark();
        HideRig();
        StartCoroutine(ShardShower(e.Game, position, e.RiftShards.AsFloat));

        AnnouncerManager.Instance?.Announce(bannerText);
        LogHelper.Log(LogTag, $"Burst at {position} - {e.RiftShards} Rift Shards");
    }

    // ------------------------------------------------------------------ timeline

    private IEnumerator RunSequence(QuantumGame game, EntityRef boss, Vector3 fallbackPosition, float duration, float burstTime)
    {
        float start = Time.unscaledTime;
        Transform bossTransform = ResolveBossTransform(game, boss);
        Vector3 position = bossTransform != null ? bossTransform.position : fallbackPosition;

        CaptureRig(bossTransform);
        ApplyDyingSprite();

        // Kill: hit-stop, flash, shake, camera onto the boss (not snapped - the pan is part of the beat).
        Flash(killFlashAlpha, killFlashFade);
        FollowCamera.I?.Shake(killShakeAmplitude, killShakeDuration, 25f);

        if (bossTransform != null)
            FollowCamera.I?.SetFocusOverride(bossTransform, snap: false);

        yield return HitStop(bossTransform);

        // Death throes, up to the burst (fired by the sim's own BossDefeatBurst event).
        while (Time.unscaledTime - start < throesStartDelay)
            yield return null;

        float nextFlicker = 0f;
        bool flickerOn = false;
        float throesEnd = Mathf.Max(throesStartDelay, burstTime - 0.1f);
        // Sized off the collider diameter (like StatusEffectsManager sizes this mark on any primed enemy), but centred on
        // the boss's VISIBLE body - the sprite is much taller than its collider, so the collider centre sits low.
        // Measured once, before the shake, as an offset from the view so it follows the view's own interpolation.
        Frame frame = game?.Frames.Predicted;
        float diameter = 2f;

        if (frame != null && frame.Exists(boss) == true)
            diameter = EnemyMovementUtility.ResolveEntityRadius(frame, boss).AsFloat * 2f;

        _bodyCenterOffset = ResolveBodyCenterOffset(game, boss, bossTransform);
        StartUnstableMark(bossTransform != null);

        while (Time.unscaledTime - start < throesEnd)
        {
            float t = Mathf.InverseLerp(throesStartDelay, throesEnd, Time.unscaledTime - start);

            if (_rig != null)
                _rig.localPosition = _rigBaseLocalPosition + (Vector3)(Random.insideUnitCircle * (throesMaxRigJitter * t));

            if (_unstableMark != null && bossTransform != null)
            {
                float scale = diameter * Mathf.Lerp(unstableMarkScale.x, unstableMarkScale.y, t);
                Vector3 rigShake = _rig != null && _rig.parent != null ? _rig.position - _rig.parent.TransformPoint(_rigBaseLocalPosition) : Vector3.zero;

                _unstableMark.transform.SetPositionAndRotation(bossTransform.position + _bodyCenterOffset + rigShake, Quaternion.identity);
                _unstableMark.transform.localScale = Vector3.one * scale;
                RampUnstableMark(t);
            }

            if (Time.unscaledTime - start >= nextFlicker)
            {
                // Flicker and rumble come faster as the burst approaches.
                nextFlicker = Time.unscaledTime - start + Mathf.Lerp(throesFlickerInterval, throesFlickerInterval * 0.3f, t);
                FollowCamera.I?.Shake(Mathf.Lerp(0.1f, 0.35f, t), 0.18f, 25f);

                flickerOn = !flickerOn;
                TintRig(flickerOn ? throesFlickerColor : Color.white);
            }

            yield return null;
        }

        TintRig(Color.white);

        if (_rig != null)
            _rig.localPosition = _rigBaseLocalPosition;

        // Tail: ease the camera back to the players just before gameplay resumes.
        while (Time.unscaledTime - start < duration - cameraReturnLead)
            yield return null;

        FollowCamera.I?.ClearFocusOverride(snap: false);
        StartCoroutine(HideTally(0.4f));

        while (Time.unscaledTime - start < duration + 0.5f)
            yield return null;

        StopUnstableMark();
        RestoreRig();
        _sequence = null;
    }

    private IEnumerator HitStop(Transform bossTransform)
    {
        if (bossTransform == null || hitStopDuration <= 0f)
            yield break;

        var animators = bossTransform.GetComponentsInChildren<Animator>();
        var particles = bossTransform.GetComponentsInChildren<ParticleSystem>();
        var speeds = new float[animators.Length];

        for (int i = 0; i < animators.Length; i++)
        {
            speeds[i] = animators[i].speed;
            animators[i].speed = 0f;
        }

        foreach (var ps in particles)
            ps.Pause(false);

        yield return new WaitForSecondsRealtime(hitStopDuration);

        for (int i = 0; i < animators.Length; i++)
        {
            if (animators[i] != null)
                animators[i].speed = speeds[i];
        }

        foreach (var ps in particles)
        {
            if (ps != null)
                ps.Play(false);
        }
    }

    private IEnumerator ShardShower(QuantumGame game, Vector3 origin, float amount)
    {
        List<(EntityRef entity, Transform transform)> heroes = ResolveHeroes(game);

        if (heroes.Count == 0 || amount <= 0f)
            yield break;

        int count = Mathf.Clamp(Mathf.CeilToInt(amount), heroes.Count, Mathf.Max(heroes.Count, maxShardPieces));
        GameObject shardTemplate = ResolvePickupTemplate(game, CurrencyType.RiftShard);

        _shardsLanded = 0;
        ShowTally(amount);

        for (int i = 0; i < count; i++)
        {
            var hero = heroes[i % heroes.Count];
            float delay = count > 1 ? shardReleaseSpread * i / (count - 1) : 0f;
            StartCoroutine(FlyPickup(CurrencyType.RiftShard, shardTemplate, origin, hero.entity, hero.transform, delay, count));
        }

        // Visual-only Coins and XP orbs, spread across the same release window so the whole payout reads as one
        // fountain of loot. No sound (keeps the shards' climbing pickup run clean), no tally - just the pickup flash.
        LaunchDecor(game, CurrencyType.Coin, decorCoinPieces, origin, heroes);
        LaunchDecor(game, CurrencyType.Experience, decorOrbPieces, origin, heroes);
    }

    private void LaunchDecor(QuantumGame game, CurrencyType type, int pieces, Vector3 origin, List<(EntityRef entity, Transform transform)> heroes)
    {
        GameObject template = ResolvePickupTemplate(game, type);

        if (template == null || pieces <= 0)
            return;

        for (int i = 0; i < pieces; i++)
        {
            var hero = heroes[Random.Range(0, heroes.Count)];
            StartCoroutine(FlyPickup(type, template, origin, hero.entity, hero.transform, Random.Range(0f, shardReleaseSpread), 0));
        }
    }

    // One pickup flying out of the boss and homing onto a hero. Only RiftShard pieces count toward the tally/sound;
    // Coin/Experience are decoration.
    private IEnumerator FlyPickup(CurrencyType type, GameObject template, Vector3 origin, EntityRef heroEntity, Transform hero, float delay, int pieceCount)
    {
        GameObject shard;
        Material ownedMaterial = null;

        if (template != null)
        {
            shard = Instantiate(template, origin, template.transform.rotation);
            shard.SetActive(true);
        }
        else if (type != CurrencyType.RiftShard)
        {
            yield break;
        }
        else
        {
            // No shard view to copy (e.g. lazy view loading left the prefab unloaded) - plain pink sphere instead.
            shard = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(shard.GetComponent<Collider>());
            shard.transform.localScale = Vector3.one * fallbackShardSize;
            var r = shard.GetComponent<Renderer>();
            ownedMaterial = r.material;
            ownedMaterial.color = fallbackShardColor;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        shard.name = "BossDefeat" + type;
        shard.transform.position = origin;
        Vector3 baseScale = shard.transform.localScale;

        Vector2 scatter2D = Random.insideUnitCircle.normalized * Random.Range(shardScatterRadius * 0.5f, shardScatterRadius);
        Vector3 scatterPoint = origin + new Vector3(scatter2D.x, Random.Range(1.5f, 3f), scatter2D.y);

        float t = -delay;

        // Pop out of the body...
        while (t < shardScatterTime)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / shardScatterTime);
            shard.transform.position = Vector3.Lerp(origin, scatterPoint, 1f - (1f - k) * (1f - k));
            yield return null;
        }

        // ...then home in on a hero, accelerating.
        Vector3 from = shard.transform.position;
        t = 0f;

        while (t < shardFlyTime)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / shardFlyTime);
            Vector3 to = hero != null ? hero.position + Vector3.up * 0.5f : from;
            Vector3 arc = Vector3.up * (Mathf.Sin(k * Mathf.PI) * 1.5f);
            shard.transform.position = Vector3.Lerp(from, to, k * k) + arc;
            shard.transform.localScale = baseScale * Mathf.Lerp(1f, 0.6f, k);
            yield return null;
        }

        if (type == CurrencyType.RiftShard)
        {
            if (hero != null)
                PlayExplosion(hero.position + Vector3.up * 0.5f, 0.4f);

            OnShardLanded(heroEntity, pieceCount);
        }
        else if (hero != null)
        {
            hero.GetComponentInChildren<HitFeedback>()?.FlashPickup(type);
        }

        if (ownedMaterial != null)
            Destroy(ownedMaterial);

        Destroy(shard);
    }

    // A real pickup's look: GameConfig.Prefabs.<Type>Prototype -> its ViewPrototype -> EntityView.Prefab (same chain
    // ProjectileGhostTrailManager.ResolvePrefabView walks). Copied once per type under an inactive holder, so none of
    // its components ever wake up, and stripped of everything Quantum (prototype, entity view, QPrototype*) - only the
    // renderers and visual helpers (Billboard, HasLight) are kept. Kept inactive; FlyPickup clones and activates it.
    private GameObject ResolvePickupTemplate(QuantumGame game, CurrencyType type)
    {
        if (_pickupTemplates.TryGetValue(type, out GameObject cached) && cached != null)
            return cached;

        GameObject prefab = ResolvePickupPrefab(game, type);

        if (prefab == null)
            return null;

        if (_templateHolder == null)
        {
            _templateHolder = new GameObject("BossDefeatPickupTemplates");
            _templateHolder.SetActive(false);
            _templateHolder.transform.SetParent(transform, false);
        }

        GameObject copy = Instantiate(prefab, _templateHolder.transform);

        // Dependents first (QPrototype* require QuantumEntityPrototype), so every DestroyImmediate is allowed.
        foreach (var component in copy.GetComponentsInChildren<QuantumUnityComponentPrototype>(true))
            DestroyImmediate(component);
        foreach (var component in copy.GetComponentsInChildren<QuantumEntityView>(true))
            DestroyImmediate(component);
        foreach (var component in copy.GetComponentsInChildren<QuantumEntityPrototype>(true))
            DestroyImmediate(component);
        foreach (var component in copy.GetComponentsInChildren<Collider>(true))
            DestroyImmediate(component);

        copy.SetActive(false);
        copy.transform.SetParent(transform, false);
        _pickupTemplates[type] = copy;
        return copy;
    }

    private static GameObject ResolvePickupPrefab(QuantumGame game, CurrencyType type)
    {
        Frame frame = game?.Frames.Predicted;

        if (frame == null)
            return null;

        AssetRef<EntityPrototype> prototypeRef = type switch
        {
            CurrencyType.RiftShard => frame.GameConfig.Prefabs.RiftShardPrototype,
            CurrencyType.Coin => frame.GameConfig.Prefabs.CoinPrototype,
            CurrencyType.Experience => frame.GameConfig.Prefabs.ExpOrbPrototype,
            _ => default,
        };

        if (prototypeRef.IsValid == false)
            return null;

        EntityPrototype prototype = QuantumUnityDB.GetGlobalAsset(prototypeRef);

        if (prototype == null || prototype.Container.Components == null)
            return null;

        foreach (ComponentPrototype component in prototype.Container.Components)
        {
            if (component is not Quantum.Prototypes.ViewPrototype viewPrototype)
                continue;

            EntityView entityView = QuantumUnityDB.GetGlobalAsset(viewPrototype.Current);
            return entityView != null ? entityView.Prefab : null;
        }

        return null;
    }

    // ------------------------------------------------------------------ boss rig

    private void CaptureRig(Transform bossTransform)
    {
        _rig = null;
        _rigComponent = null;
        _bodySprites = System.Array.Empty<SpriteRenderer>();
        _bodyColors = System.Array.Empty<Color>();

        if (bossTransform == null)
            return;

        _rigComponent = bossTransform.GetComponentInChildren<EnemyViewRig>();

        if (_rigComponent == null)
            return;

        _rig = _rigComponent.transform;
        _rigBaseLocalPosition = _rig.localPosition;

        // Body sprites only (EnemyViewRig.Sprites skips props like the Leviathan's sand), with their authored colors
        // so the flicker can return to them instead of plain white.
        _bodySprites = _rigComponent.Sprites;
        _bodyColors = new Color[_bodySprites.Length];
        for (int i = 0; i < _bodySprites.Length; i++)
            _bodyColors[i] = _bodySprites[i].color;
    }

    // EnemyViewRig.DyingSprite: one "dying" pose instead of a death animation. No-op until a rig authors one.
    private void ApplyDyingSprite()
    {
        if (_rigComponent == null || _rigComponent.DyingSprite == null)
            return;

        SpriteRenderer target = _rigComponent.DyingSpriteTarget;

        if (target == null)
            return;

        _dyingTarget = target;
        _dyingOriginalSprite = target.sprite;
        target.sprite = _rigComponent.DyingSprite;

        if (_rigComponent.DyingHidesOtherSprites == false)
            return;

        foreach (var sprite in _bodySprites)
        {
            if (sprite != target && sprite.enabled)
            {
                sprite.enabled = false;
                _hiddenRenderers.Add(sprite);
            }
        }
    }

    // white = back to the authored colors; anything else multiplies over them.
    private void TintRig(Color color)
    {
        for (int i = 0; i < _bodySprites.Length; i++)
        {
            if (_bodySprites[i] != null)
                _bodySprites[i].color = _bodyColors[i] * color;
        }
    }

    private void HideRig()
    {
        if (_rig == null)
            return;

        TintRig(Color.white);
        _rig.localPosition = _rigBaseLocalPosition;

        foreach (var r in _rig.GetComponentsInChildren<Renderer>())
        {
            if (r.enabled)
            {
                r.enabled = false;
                _hiddenRenderers.Add(r);
            }
        }
    }

    // The view may be pooled and reused, so everything the sequence changed goes back.
    private void RestoreRig()
    {
        foreach (var r in _hiddenRenderers)
        {
            if (r != null)
                r.enabled = true;
        }

        _hiddenRenderers.Clear();

        if (_dyingTarget != null)
            _dyingTarget.sprite = _dyingOriginalSprite;

        _dyingTarget = null;
        _dyingOriginalSprite = null;

        TintRig(Color.white);

        if (_rig != null)
            _rig.localPosition = _rigBaseLocalPosition;

        _rig = null;
        _rigComponent = null;
        _bodySprites = System.Array.Empty<SpriteRenderer>();
        _bodyColors = System.Array.Empty<Color>();
    }

    // Pixie's "Unstable" (ExplodeOnDeath) mark - its own unparented instance (StatusEffectsManager's tracker only
    // shows on live ExplodeOnDeath enemies); RunSequence positions and scales it every frame.
    private void StartUnstableMark(bool hasBoss)
    {
        StopUnstableMark();

        ParticleSystem prefab = _unstablePrefab != null
            ? _unstablePrefab
            : StatusEffectsManager.Instance != null ? StatusEffectsManager.Instance.ExplodeMarkParticlePrefab : null;

        if (prefab == null || hasBoss == false)
            return;

        _unstableMark = Instantiate(prefab, transform);

        _unstableSystems = _unstableMark.GetComponentsInChildren<ParticleSystem>(true);
        _unstableBaseRates = new float[_unstableSystems.Length];
        _unstableBaseSpeeds = new float[_unstableSystems.Length];

        for (int i = 0; i < _unstableSystems.Length; i++)
        {
            _unstableBaseRates[i] = _unstableSystems[i].emission.rateOverTimeMultiplier;
            _unstableBaseSpeeds[i] = _unstableSystems[i].main.startSpeedMultiplier;
        }

        RampUnstableMark(0f);
        _unstableMark.Play(true);
    }

    // Charge-up: more particles, pulled in faster, as t goes 0 -> 1 toward the burst.
    private void RampUnstableMark(float t)
    {
        float rate = Mathf.Lerp(unstableRateRamp.x, unstableRateRamp.y, t);
        float speed = Mathf.Lerp(unstableSpeedRamp.x, unstableSpeedRamp.y, t);

        for (int i = 0; i < _unstableSystems.Length; i++)
        {
            if (_unstableSystems[i] == null)
                continue;

            var emission = _unstableSystems[i].emission;
            emission.rateOverTimeMultiplier = _unstableBaseRates[i] * rate;

            var main = _unstableSystems[i].main;
            main.startSpeedMultiplier = _unstableBaseSpeeds[i] * speed;
        }
    }

    // Centre of the boss's visible body sprites, as an offset from its view root; falls back to the collider centre.
    private Vector3 ResolveBodyCenterOffset(QuantumGame game, EntityRef boss, Transform bossTransform)
    {
        if (bossTransform == null)
            return Vector3.up;

        bool any = false;
        Bounds bounds = default;

        foreach (var sprite in _bodySprites)
        {
            if (sprite == null || sprite.enabled == false || sprite.sprite == null)
                continue;

            if (any == false)
            {
                bounds = sprite.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(sprite.bounds);
            }
        }

        if (any == true)
            return bounds.center - bossTransform.position;

        Frame frame = game?.Frames.Predicted;
        return frame != null && frame.Exists(boss) == true
            ? EnemyMovementUtility.ResolveEntityCenter(frame, boss).ToUnityVector3() - bossTransform.position
            : Vector3.up;
    }

    private void StopUnstableMark()
    {
        if (_unstableMark != null)
            Destroy(_unstableMark.gameObject);

        _unstableMark = null;
        _unstableSystems = System.Array.Empty<ParticleSystem>();
    }

    public void FlashScreen(float alpha, float fade) => Flash(alpha, fade);

    public void PlayPinkExplosion(Vector3 position, float scale) => PlayExplosion(position, scale);

    public void PlayCrystalSparks(Vector3 position, float scale)
    {
        if (_crystalSparksPrefab != null)
            EffectsManager.Instance?.PlayEffect(_crystalSparksPrefab, position, Quaternion.identity, Vector3.one * scale);
    }

    // Pooled through EffectsManager like every other one-shot blast.
    private void PlayExplosion(Vector3 position, float scale)
    {
        if (EffectsManager.Instance == null)
            return;

        if (_explosionPrefab != null)
            EffectsManager.Instance.PlayEffect(_explosionPrefab, position, Quaternion.identity, Vector3.one * scale);
        else
            EffectsManager.Instance.PlayBossDefeatExplosion(position, scale);
    }

    // ------------------------------------------------------------------ lookups

    private static Transform ResolveBossTransform(QuantumGame game, EntityRef boss)
    {
        var updater = FindFirstObjectByType<QuantumEntityViewUpdater>();
        QuantumEntityView view = updater != null ? updater.GetView(boss) : null;
        return view != null ? view.transform : null;
    }

    private static Vector3 ResolveBossPosition(QuantumGame game, EntityRef boss, Vector3 fallback)
    {
        Transform t = ResolveBossTransform(game, boss);
        return t != null ? t.position : fallback;
    }

    private static List<(EntityRef entity, Transform transform)> ResolveHeroes(QuantumGame game)
    {
        var result = new List<(EntityRef, Transform)>();
        var updater = FindFirstObjectByType<QuantumEntityViewUpdater>();
        Frame frame = game?.Frames.Predicted;

        if (updater == null || frame == null)
            return result;

        var players = frame.Filter<PlayerLink>();
        while (players.Next(out EntityRef entity, out PlayerLink _))
        {
            QuantumEntityView view = updater.GetView(entity);
            if (view != null)
                result.Add((entity, view.transform));
        }

        return result;
    }

    // ------------------------------------------------------------------ tally

    private void OnShardLanded(EntityRef hero, int pieceCount)
    {
        _shardsLanded++;
        bool last = _shardsLanded >= pieceCount;

        FlyingCurrencyManager.Instance?.PlayRiftShardShowerArrival(hero, Mathf.Min(_shardsLanded - 1, shardPitchMaxSteps), shardPitchSemitones);

        // Each piece is worth an equal share; the last one lands the exact total.
        _tallyShown = last ? _tallyTotal : Mathf.Min(_tallyTotal, Mathf.Round(_tallyTotal * _shardsLanded / pieceCount));
        SetTallyText(_tallyShown);
        PunchTally(last ? tallyFinalPunchScale : tallyPunchScale, last ? 0.35f : 0.12f);

        if (last)
        {
            Flash(0.35f, 0.3f);
            FollowCamera.I?.Shake(0.35f, 0.25f, 25f);
            Haptics.Play(HapticsConfig.Instance.bossKilled);
        }
    }

    private void ShowTally(float total)
    {
        if (_tally == null)
            return;

        // Same face as the announcer banner, so the payout reads as part of the same moment.
        TMP_Text bannerText = AnnouncerManager.Instance != null ? AnnouncerManager.Instance.GetComponentInChildren<TMP_Text>(true) : null;
        if (bannerText != null && bannerText.font != null)
            _tally.font = bannerText.font;

        _tallyTotal = total;
        _tallyShown = 0f;
        SetTallyText(0f);
        _tally.alpha = 1f;
        _tally.enabled = true;
        PunchTally(tallyPunchScale, 0.12f);
    }

    private void SetTallyText(float value)
    {
        if (_tally != null)
            _tally.text = $"<size={tallyAmountSize}>+{Mathf.RoundToInt(value)}</size>\n<size={tallyLabelSize}>{tallyLabel}</size>";
    }

    private void PunchTally(float scale, float duration)
    {
        if (_tally == null)
            return;

        if (_tallyPunch != null)
            StopCoroutine(_tallyPunch);

        _tallyPunch = StartCoroutine(PunchRoutine(scale, duration));
    }

    private IEnumerator PunchRoutine(float scale, float duration)
    {
        float t = 0f;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            _tally.rectTransform.localScale = Vector3.one * Mathf.Lerp(scale, 1f, 1f - (1f - k) * (1f - k));
            yield return null;
        }

        _tally.rectTransform.localScale = Vector3.one;
        _tallyPunch = null;
    }

    // Gone before the run result popup takes over (this overlay sorts above it).
    private IEnumerator HideTally(float fade)
    {
        if (_tally == null || _tally.enabled == false)
            yield break;

        float t = 0f;

        while (t < fade)
        {
            t += Time.unscaledDeltaTime;
            _tally.alpha = 1f - Mathf.Clamp01(t / fade);
            yield return null;
        }

        _tally.enabled = false;
    }

    // ------------------------------------------------------------------ flash overlay

    private void BuildFlashOverlay()
    {
        var canvasGo = new GameObject("BossDefeatFlash");
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var imageGo = new GameObject("Flash");
        imageGo.transform.SetParent(canvasGo.transform, false);

        _flash = imageGo.AddComponent<Image>();
        _flash.raycastTarget = false;
        _flash.color = new Color(1f, 1f, 1f, 0f);

        var rect = _flash.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        _flash.enabled = false;

        var tallyGo = new GameObject("Tally");
        tallyGo.transform.SetParent(canvasGo.transform, false);

        _tally = tallyGo.AddComponent<TextMeshProUGUI>();
        _tally.raycastTarget = false;
        _tally.alignment = TextAlignmentOptions.Center;
        _tally.fontStyle = FontStyles.Bold;
        _tally.color = tallyColor;
        _tally.enableWordWrapping = false;

        var tallyRect = _tally.rectTransform;
        tallyRect.anchorMin = tallyRect.anchorMax = new Vector2(0.5f, tallyScreenY);
        tallyRect.sizeDelta = new Vector2(900f, 300f);

        _tally.enabled = false;
    }

    private void Flash(float alpha, float fade)
    {
        if (_flash == null)
            return;

        if (_flashRoutine != null)
            StopCoroutine(_flashRoutine);

        _flashRoutine = StartCoroutine(FlashRoutine(alpha, fade));
    }

    private IEnumerator FlashRoutine(float alpha, float fade)
    {
        _flash.enabled = true;
        float t = 0f;

        while (t < fade)
        {
            t += Time.unscaledDeltaTime;
            _flash.color = new Color(1f, 1f, 1f, Mathf.Lerp(alpha, 0f, t / fade));
            yield return null;
        }

        _flash.enabled = false;
        _flashRoutine = null;
    }
}
