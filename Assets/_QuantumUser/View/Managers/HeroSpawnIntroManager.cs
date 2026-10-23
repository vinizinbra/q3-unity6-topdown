namespace QuantumUser.View.Managers
{
    using System.Collections.Generic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEngine;

    // Match-start intro: a looping portal effect on the LobbyStart chunk (Global.PlayerSpawnPosition)
    // while the heroes arrive, and a one-shot burst per hero as it "appears" through it.
    //
    // View-only on purpose - the simulation still spawns heroes exactly when it always did
    // (PlayerSpawnUtility.IsReadyToSpawn). What this owns is WHEN each hero becomes visible on THIS
    // client: a hero spawned while the loading screen is still covering the game (common on a slow
    // device, where the shore bake outlasts the 1s spawn settle) stays hidden until the screen has
    // lifted, so its burst always plays on screen instead of behind the LoadingWindow. Heroes are
    // launched one at a time, revealStagger apart, in the order their views were created: an optional
    // travel effect flies portal -> hero (homing on its live position) and the hero is revealed on landing.
    //
    // Only heroes whose view is created while still in GameState.Lobby and before the intro has
    // finished go through this - a client reconnecting mid-run recreates views for heroes that have
    // existed for minutes, and those must just be there, not "arrive" again.
    //
    // Hidden = BlobAnimationView/WeaponViewController/CharacterUiWidget treat it like a pending
    // fall-respawn (see IsHidden). Without an instance in the scene IsHidden is always false, so the
    // whole intro is opt-in by placing this component (next to EffectsManager is the natural spot).
    public class HeroSpawnIntroManager : QuantumGlobalMonoBehaviour
    {
        private const string LogTag = "SpawnIntro";

        public static HeroSpawnIntroManager Instance;

        // Raised the moment a hero becomes visible on this client (after the portal/travel effect lands).
        // InMatchTutorialManager uses it to open the Hero Intro popup right after the local hero arrives.
        public static event System.Action<EntityRef> HeroRevealed;

        [Header("Portal")]
        [SerializeField, Tooltip("Looping effect played at the LobbyStart chunk (Global.PlayerSpawnPosition) from the moment the level is generated until every hero has arrived. Instantiated once per match, NOT pooled - EffectsManager's pool only takes non-looping one-shots. Leave empty to skip the portal (heroes still reveal with their burst).")]
        private ParticleSystem portalPrefab;
        [SerializeField, Tooltip("World offset added to Global.PlayerSpawnPosition (which sits at floor level, Y=0, at the chunk's footprint center).")]
        private Vector3 portalOffset;
        [SerializeField] private float portalScale = 1f;
        [SerializeField, Tooltip("How long the portal keeps running after the last hero is revealed before it stops emitting.")]
        private float portalLingerAfterLastHero = 0.6f;
        [SerializeField, Tooltip("Failsafe, counted only once the loading screen has lifted: closes the portal (and reveals anyone still hidden) even if an expected hero never showed up, e.g. a player that dropped mid-join.")]
        private float portalMaxDuration = 12f;
        [SerializeField, Tooltip("Seconds the portal instance is kept alive after Stop() so its live particles can finish. Should cover the prefab's longest particle lifetime.")]
        private float portalDestroyDelay = 3f;
        [SerializeField, SoundDataPicker, Tooltip("Looping hum at the portal while it's open. Starts when the loading screen lifts (not behind it) and is stopped with the SoundData's own Fade Out when the portal closes - so author Loop on, and a Fade In/Out there. Leave empty for a silent portal.")]
        private SoundData portalLoopSound;

        [Header("Travel (portal -> hero)")]
        [SerializeField, Tooltip("Flies from the portal to each hero before it appears - the hero is revealed (burst + punch) the moment this lands. Moved by this manager every frame, so author it in LOCAL simulation space with a Trail/emission over distance in WORLD space for a streak. Instantiated per hero, not pooled. Leave empty to reveal heroes in place with no flight.")]
        private ParticleSystem travelEffectPrefab;
        [SerializeField, Tooltip("Seconds from leaving the portal to landing on the hero.")]
        private float travelDuration = 0.45f;
        [SerializeField, Tooltip("Extra height at the middle of the flight, on top of the straight line - 0 = straight line.")]
        private float travelArcHeight = 1.5f;
        [SerializeField] private AnimationCurve travelEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField, Tooltip("Seconds the travel instance is kept after landing (emission stopped) so its trail/particles can fade out.")]
        private float travelDestroyDelay = 1f;

        [Header("Camera")]
        [SerializeField, Tooltip("The camera holds on the portal while the local hero is still hidden, then glides to it over this many seconds, starting when that hero's travel effect leaves the portal - so camera and lightning arrive together. <= 0 = match travelDuration.")]
        private float cameraBlendDuration = -1f;

        [Header("Hero reveal")]
        [SerializeField, Tooltip("One-shot burst at each hero the moment it's revealed - played through EffectsManager's pool, so it must not loop. Its local UP is aimed at the portal (author it pointing up, +Y, and it will fire toward the portal). Leave empty to skip the particle (the body punch-scale still plays).")]
        private ParticleSystem heroSpawnEffectPrefab;
        [SerializeField] private float heroSpawnEffectScale = 1f;
        [SerializeField, SoundDataPicker, Tooltip("One-shot played at each hero the moment it's revealed - the travel lightning landing. Several heroes land revealStagger apart, so give it pitch variance on the SoundData. Must not loop.")]
        private SoundData heroLandSound;
        [SerializeField, Tooltip("Aims the burst's UP at the portal along the ground only (Y difference ignored), so it lies flat pointing at it. Off = aim straight at the portal itself - with a portal floating portalOffset.y above heroes standing ~1.5m from its center, that is nearly vertical and barely tilts the effect.")]
        private bool aimHorizontally = true;
        [SerializeField, Tooltip("Delay after the loading screen has fully lifted before the first hero is revealed - lets the player register the portal first.")]
        private float firstRevealDelay = 0.35f;
        [SerializeField, Tooltip("Gap between consecutive heroes being revealed.")]
        private float revealStagger = 0.2f;

        private readonly List<EntityRef> _pending = new List<EntityRef>();
        private readonly HashSet<EntityRef> _hidden = new HashSet<EntityRef>();

        // Play On Awake particles a held hero's prefab started the moment its view was instantiated
        // (an arrival flash, an aura...). Most sit OUTSIDE the body roots BlobAnimationView hides, so
        // nothing would hide them - they'd play at an invisible hero. Stopped + cleared on register,
        // replayed on reveal.
        private readonly Dictionary<EntityRef, List<ParticleSystem>> _deferredEffects = new Dictionary<EntityRef, List<ParticleSystem>>();

        private struct Flight
        {
            public EntityRef Entity;
            public ParticleSystem Instance;
            public Vector3 Start;
            public float Elapsed;
        }

        private readonly List<Flight> _flights = new List<Flight>();

        private ParticleSystem _portal;
        private SoundHandle _portalLoop = SoundHandle.None;
        private Vector3 _portalPosition;
        private bool _cameraFocused;
        private bool _cameraReleased;
        private bool _finished;
        private float _nextRevealTime;
        private float _lastRevealTime;
        private float _uncoveredTime;

        public static bool IsHidden(EntityRef entity)
        {
            return Instance != null && Instance._hidden.Contains(entity);
        }

        private void Awake()
        {
            Instance = this;

            // The sim lets a hero move the moment it spawns; hidden here, it would walk off unseen and
            // pop in somewhere else. Empty input until our own hero has landed - same gate the UI uses,
            // so a button held through the arrival stays muted until released (movement resumes at once).
            UiInputGate.Register(this, IsLocalHeroHidden);
        }

        private bool IsLocalHeroHidden()
        {
            if (MyLocalPlayer.Instance == null)
                return false;

            foreach (EntityRef entity in _hidden)
                if (MyLocalPlayer.Instance.IsLocalEntity(entity) == true)
                    return true;

            return false;
        }

        private void OnDestroy()
        {
            _portalLoop.Stop(0f);

            if (Instance == this)
                Instance = null;
        }

        // From CharView.Initialize - every hero view, local, remote or bot. Same frame the view was
        // instantiated, so its Play On Awake particles can still be stopped before they ever render.
        public unsafe void RegisterHero(Frame frame, EntityRef entity, Transform viewRoot)
        {
            if (_finished == true || frame == null || frame.Global->CurrentState != GameState.Lobby)
                return;

            if (_hidden.Add(entity) == false)
                return;

            _pending.Add(entity);
            DeferAwakeEffects(entity, viewRoot);
        }

        public void UnregisterHero(EntityRef entity)
        {
            _hidden.Remove(entity);
            _pending.Remove(entity);
            _deferredEffects.Remove(entity);

            for (int i = _flights.Count - 1; i >= 0; i--)
                if (_flights[i].Entity == entity)
                    EndFlight(i);
        }

        public override unsafe void QUpdate(QuantumGame game)
        {
            if (_finished == true)
                return;

            Frame frame = game.Frames.Predicted;
            if (frame == null || frame.Global->LevelGenerated == false)
                return;

            Vector3 spawnPoint = frame.Global->PlayerSpawnPosition.ToUnityVector3();
            _portalPosition = spawnPoint + portalOffset;

            // Behind the loading screen, so the snap is never seen - the world is revealed already
            // framed on the portal rather than wherever the scene's camera happened to be authored.
            if (_cameraFocused == false && FollowCamera.I != null)
            {
                FollowCamera.I.SetIdleFocus(spawnPoint, hold: true);
                _cameraFocused = true;
            }

            if (_portal == null && portalPrefab != null)
            {
                _portal = Instantiate(portalPrefab, _portalPosition, portalPrefab.transform.rotation, transform);
                _portal.transform.localScale = Vector3.one * portalScale;
                _portal.Play(true);
            }

            // Nothing is revealed (and the failsafe doesn't tick) while the screen still covers the game.
            if (WaterShoreBaker.IsLoadingScreenUp == true)
            {
                _nextRevealTime = Time.time + firstRevealDelay;
                _uncoveredTime = 0f;
                return;
            }

            if (_portalLoop.IsValid == false && portalLoopSound != null && (portalPrefab == null || _portal != null))
                _portalLoop = AudioManager.PlayAt(portalLoopSound, _portalPosition);

            _uncoveredTime += Time.deltaTime;

            if (_pending.Count > 0 && Time.time >= _nextRevealTime)
            {
                Launch(frame, _pending[0]);
                _nextRevealTime = Time.time + revealStagger;
            }

            UpdateFlights(frame);

            bool timedOut = portalMaxDuration > 0f && _uncoveredTime >= portalMaxDuration;
            bool leftLobby = frame.Global->CurrentState != GameState.Lobby;
            bool allArrived = _pending.Count == 0
                && _flights.Count == 0
                && AllPlayersSpawned(frame) == true
                && Time.time - _lastRevealTime >= portalLingerAfterLastHero;

            if (allArrived == true || timedOut == true || leftLobby == true)
                Finish(frame, timedOut ? "timed out" : leftLobby ? "left Lobby" : "all heroes arrived");
        }

        // Takes the hero off the queue; with a travel effect it stays hidden until the flight lands.
        private void Launch(Frame frame, EntityRef entity)
        {
            _pending.Remove(entity);
            ReleaseCameraIfLocalLaunched(entity);

            if (travelEffectPrefab == null || travelDuration <= 0f || frame.Has<Transform3D>(entity) == false)
            {
                Reveal(frame, entity);
                return;
            }

            ParticleSystem instance = Instantiate(travelEffectPrefab, _portalPosition, travelEffectPrefab.transform.rotation, transform);
            instance.Play(true);
            _flights.Add(new Flight { Entity = entity, Instance = instance, Start = _portalPosition });
        }

        // Only top-level systems (Play/Stop already cascade to children). Body-root ones are included:
        // they're hidden anyway, and re-enabling the root replays them by itself.
        private void DeferAwakeEffects(EntityRef entity, Transform viewRoot)
        {
            if (viewRoot == null)
                return;

            List<ParticleSystem> deferred = null;
            foreach (ParticleSystem ps in viewRoot.GetComponentsInChildren<ParticleSystem>(false))
            {
                // Not gated on isPlaying - straight after Instantiate it can still read false for a
                // system that will start this frame; an active Play On Awake one is playing either way.
                if (ps.main.playOnAwake == false)
                    continue;

                Transform parent = ps.transform.parent;
                if (parent != null && parent != viewRoot && parent.GetComponentInParent<ParticleSystem>() != null)
                    continue;

                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                (deferred ??= new List<ParticleSystem>()).Add(ps);
            }

            if (deferred != null)
                _deferredEffects[entity] = deferred;
        }

        // A system whose object is inactive right now (under a body root that BlobAnimationView hasn't
        // re-enabled yet) is skipped - its own Play On Awake fires when that root comes back on.
        private void PlayDeferredEffects(EntityRef entity)
        {
            if (_deferredEffects.Remove(entity, out List<ParticleSystem> deferred) == false)
                return;

            foreach (ParticleSystem ps in deferred)
                if (ps != null && ps.gameObject.activeInHierarchy == true)
                    ps.Play(true);
        }

        // Homes on the hero's LIVE position every frame - our own hero is input-gated while hidden, but
        // a remote teammate can already be walking (their client reveals on its own schedule), and the
        // burst must still land on them.
        private void UpdateFlights(Frame frame)
        {
            for (int i = _flights.Count - 1; i >= 0; i--)
            {
                Flight flight = _flights[i];
                flight.Elapsed += Time.deltaTime;
                _flights[i] = flight;

                if (frame.Has<Transform3D>(flight.Entity) == false)
                {
                    EndFlight(i);
                    continue;
                }

                Vector3 target = frame.Get<Transform3D>(flight.Entity).Position.ToUnityVector3();
                float t = Mathf.Clamp01(flight.Elapsed / travelDuration);
                float eased = travelEase != null ? travelEase.Evaluate(t) : t;

                if (flight.Instance != null)
                {
                    Vector3 position = Vector3.LerpUnclamped(flight.Start, target, eased)
                        + Vector3.up * (travelArcHeight * 4f * t * (1f - t));
                    flight.Instance.transform.position = position;
                }

                if (t >= 1f)
                {
                    EndFlight(i);
                    Reveal(frame, flight.Entity);
                }
            }
        }

        // Hands the camera from the portal to the heroes once the LAST local hero (couch co-op can
        // have several) leaves the portal - a remote teammate's arrival never moves our camera.
        private void ReleaseCameraIfLocalLaunched(EntityRef entity)
        {
            if (_cameraFocused == false || _cameraReleased == true || MyLocalPlayer.Instance == null || MyLocalPlayer.Instance.IsLocalEntity(entity) == false)
                return;

            foreach (EntityRef pending in _pending)
                if (MyLocalPlayer.Instance.IsLocalEntity(pending) == true)
                    return;

            FollowCamera.I?.ClearIdleFocus(ResolveCameraBlendDuration());
            _cameraReleased = true;
        }

        private float ResolveCameraBlendDuration()
        {
            if (cameraBlendDuration > 0f)
                return cameraBlendDuration;

            return travelEffectPrefab != null && travelDuration > 0f ? travelDuration : 0.6f;
        }

        private void EndFlight(int index)
        {
            ParticleSystem instance = _flights[index].Instance;
            _flights.RemoveAt(index);

            if (instance == null)
                return;

            instance.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(instance.gameObject, travelDestroyDelay);
        }

        private void Reveal(Frame frame, EntityRef entity)
        {
            _hidden.Remove(entity);
            _lastRevealTime = Time.time;
            PlayDeferredEffects(entity);
            HeroRevealed?.Invoke(entity);

            if (frame.Has<Transform3D>(entity) == false)
                return;

            Vector3 position = frame.Get<Transform3D>(entity).Position.ToUnityVector3();

            if (heroLandSound != null)
                AudioManager.PlayAt(heroLandSound, position);

            if (heroSpawnEffectPrefab == null || EffectsManager.Instance == null)
                return;

            // The prefab's UP points at the portal - the hero reads as having been flung out of it.
            // On top of the authored rotation, so a prefab tilted on purpose keeps that tilt. A hero
            // sitting right on the portal has no direction to aim along, so it keeps the authored one.
            Quaternion rotation = heroSpawnEffectPrefab.transform.rotation;
            Vector3 toPortal = _portalPosition - position;
            if (aimHorizontally == true)
                toPortal.y = 0f;
            if (toPortal.sqrMagnitude > 0.0001f)
                rotation = Quaternion.FromToRotation(Vector3.up, toPortal.normalized) * rotation;

            EffectsManager.Instance.PlayEffect(heroSpawnEffectPrefab, position, rotation, Vector3.one * heroSpawnEffectScale);
        }

        // Every slot with RuntimePlayer data must have its hero in the sim - so the portal stays open
        // for an online teammate whose join lands a moment after ours.
        private static bool AllPlayersSpawned(Frame frame)
        {
            for (int i = 0; i < frame.MaxPlayerCount; i++)
            {
                PlayerRef player = i;
                if (frame.GetPlayerData(player) != null && PlayerSpawnUtility.HasSpawned(frame, player) == false)
                    return false;
            }

            return true;
        }

        private void Finish(Frame frame, string reason)
        {
            _finished = true;

            // Only reached early (timeout / left Lobby) with anything still queued or mid-flight -
            // everyone left appears at once rather than staying hidden.
            while (_flights.Count > 0)
            {
                EntityRef entity = _flights[_flights.Count - 1].Entity;
                EndFlight(_flights.Count - 1);
                Reveal(frame, entity);
            }

            while (_pending.Count > 0)
            {
                EntityRef entity = _pending[0];
                _pending.RemoveAt(0);
                Reveal(frame, entity);
            }

            _hidden.Clear();

            // Every hero above went through Reveal, but never leave a hero's effects switched off.
            foreach (EntityRef entity in new List<EntityRef>(_deferredEffects.Keys))
                PlayDeferredEffects(entity);
            if (_cameraFocused == true && _cameraReleased == false)
                FollowCamera.I?.ClearIdleFocus(ResolveCameraBlendDuration());

            _portalLoop.Stop();
            _portalLoop = SoundHandle.None;

            if (_portal != null)
            {
                _portal.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(_portal.gameObject, portalDestroyDelay);
                _portal = null;
            }

            LogHelper.Log(LogTag, $"intro finished ({reason}) {_uncoveredTime:0.0}s after the screen lifted.", this);
        }
    }
}
