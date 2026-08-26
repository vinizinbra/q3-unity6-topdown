using System.Collections.Generic;
using Quantum;
using QuantumUser.View;
using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

// The single place gameplay moments become voice lines.
//
// It SUBSCRIBES TO THE EXISTING simulation events rather than introducing a parallel "voice event"
// that every system would also have to raise. Most of the triggers this feature needs already exist
// as deterministic events carrying exactly the right context - PlayerDowned, AccessoryRecovered
// (which already distinguishes Owner from Recoverer), AccessoryRestored (WasReplacement), and
// GameStateChanged. Duplicating those into a second event stream would mean two events per moment
// saying the same thing, and two places to keep in sync.
//
// Consequence, and the point of the design: the simulation needs NO changes for this. It already
// emits the gameplay facts; deciding whether a fact becomes audible - probability, cooldown,
// priority, audience, which hero says what - happens entirely here, on the View side, where a random
// roll can never influence gameplay.
public class VoiceDirector : QuantumGlobalMonoBehaviour
{
    private const string LogTag = "VO";

    // VoiceLineRequest.ContextValue values for SelfRevive - see VoiceLineTrigger.SelfRevive.
    public const int SelfReviveByCharge = 0;
    public const int SelfReviveOnAreaSecured = 1;

    public static VoiceDirector Instance;

    [SerializeField, Tooltip("Playback rules per trigger - probability, cooldown, priority, audience. Unassigned, nothing ever speaks.")]
    private VoiceLineConfig config;

    [SerializeField, Tooltip("One bank per hero. Looked up by CharacterData.Hero, so order doesn't matter and a missing hero simply stays silent.")]
    private List<HeroVoiceBank> banks = new List<HeroVoiceBank>();

    [Header("Debug")]
    [SerializeField, Tooltip("Logs every trigger that fires, whether or not a line is authored for it - the point being to verify WHEN triggers happen while there is still no audio attached. Also logs why a request was rejected (no rule, cooldown, probability), which is otherwise invisible.")]
    private bool logTriggers = true;

    [SerializeField, Min(0f), Tooltip("Extra silence held after a line finishes before either participant may speak again. Stops one bark treading on the tail of another.")]
    private float crossTalkGap = 0.35f;

    [SerializeField, Tooltip("Fraction of the victim's MaxHealth a single damage event must exceed to count as a heavy hit. 0.2 = 20%. Data-driven rather than a constant, since it's a feel value.")]
    [Range(0.05f, 1f)] private float heavyHitThreshold = 0.22f;

    private readonly Dictionary<HeroId, HeroVoiceBank> _banks = new();
    // CharacterData asset GUID -> HeroId. Keyed by GUID rather than by object reference because
    // reference identity relies on Quantum returning the very same managed instance the Inspector
    // points at, which is not something to depend on; the GUID is the asset's real identity.
    private readonly Dictionary<AssetGuid, HeroId> _heroByData = new();

    // Assets already reported as unregistered, so a missing bank warns once instead of every tick.
    private readonly HashSet<AssetGuid> _warnedUnmapped = new();
    // Keyed per speaker AND per trigger, which is what "per-trigger cooldown" has to mean in co-op -
    // one player's line must not put another player's identical line on cooldown.
    private readonly Dictionary<(EntityRef, VoiceLineTrigger), float> _triggerCooldowns = new();
    private readonly Dictionary<EntityRef, float> _speakerCooldowns = new();

    // Entity -> when it may speak again. Set for BOTH participants of a line, so a two-hero moment
    // cannot produce two overlapping takes of itself.
    private readonly Dictionary<EntityRef, float> _busyUntil = new();

    private float _globalCooldownUntil;
    private SoundHandle _current;
    private VoicePriority _currentPriority;

    private GameState _lastState = GameState.Lobby;
    private bool _stateSeeded;
    private bool _wasAreaSecured;
    private bool _wasBossAlive;
    private readonly HashSet<EntityRef> _knownRevivers = new();
    private readonly HashSet<EntityRef> _activeRevivers = new();

    private void Awake()
    {
        Instance = this;

        foreach (HeroVoiceBank bank in banks)
        {
            if (bank == null)
                continue;

            _banks[bank.hero] = bank;

            if (bank.characterData != null)
                _heroByData[bank.characterData.Guid] = bank.hero;
        }

        ValidateSetup();

        QuantumEvent.Subscribe<EventPlayerDowned>(this, OnPlayerDowned);
        QuantumEvent.Subscribe<EventPlayerKO>(this, OnPlayerKO);
        QuantumEvent.Subscribe<EventPlayerRevived>(this, OnPlayerRevived);
        QuantumEvent.Subscribe<EventEntityDamaged>(this, OnEntityDamaged);
        QuantumEvent.Subscribe<EventAccessoryBlocked>(this, OnAccessoryBlocked);
        QuantumEvent.Subscribe<EventAccessoryRecovered>(this, OnAccessoryRecovered);
        QuantumEvent.Subscribe<EventAccessoryBroken>(this, OnAccessoryBroken);
        QuantumEvent.Subscribe<EventAccessoryRestored>(this, OnAccessoryRestored);
    }

    // Everything here is a silent no-op when unassigned, so say so once at startup rather than
    // leaving "nothing happened" to be diagnosed from an empty Console.
    private void ValidateSetup()
    {
        if (config == null)
            LogHelper.Error(LogTag, "No VoiceLineConfig assigned - NOTHING will ever play. Run Tools > RiftRaiders > Generate Voice Content and assign it.", this);

        if (banks.Count == 0)
            LogHelper.Error(LogTag, "No Hero Voice Banks assigned - nothing can resolve a line.", this);

        foreach (HeroVoiceBank bank in banks)
        {
            if (bank == null)
                continue;

            if (bank.hero == HeroId.None)
                LogHelper.Error(LogTag, $"'{bank.name}' has Hero = None - it can never be matched to a player.", bank);

            // The single most likely misconfiguration: without this link a live player entity can
            // never be resolved to a HeroId, so every trigger silently resolves to nobody.
            if (bank.characterData == null)
                LogHelper.Error(LogTag, $"'{bank.name}' has no CharacterData assigned - {bank.hero} will never be recognised in a match.", bank);
            else
                LogHelper.Log(LogTag, $"  {bank.hero} <- {bank.characterData.name}", bank);
        }

        LogHelper.Log(LogTag, $"Ready. config={(config != null ? config.name : "<none>")}, banks={_banks.Count}, heroLinks={_heroByData.Count}.", this);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        QuantumEvent.UnsubscribeListener(this);
    }

    // ------------------------------------------------------------------ polled triggers
    //
    // These have no event of their own, and none is added: a state edge the View can see directly is
    // strictly less machinery than a new .qtn event plus a codegen pass, and VO is cosmetic so the
    // View is allowed to derive it.

    public override void QStart(QuantumGame game) { }

    public override void QLateUpdate(QuantumGame game) { }

    public override unsafe void QUpdate(QuantumGame game)
    {
        Frame frame = game.Frames.Predicted;

        PollRunFlow(frame);
        PollBoss(frame);
        PollReviveChannels(game, frame);
    }

    private unsafe void PollRunFlow(Frame frame)
    {
        GameState state = frame.Global->CurrentState;

        // Seeded rather than assumed, so joining a run in progress doesn't announce a transition
        // that already happened before this client was watching.
        if (_stateSeeded == false)
        {
            _stateSeeded = true;
            _lastState = state;
            _wasAreaSecured = frame.Global->BreathingAreaSecured;
            return;
        }

        // Upgrade is a PAUSE, not a phase: LevelUpUtility captures whatever state it interrupted in
        // Global.PreUpgradeState and restores it on close (see docs/game-state.md). Treating it as a
        // real transition meant closing an upgrade window looked like Upgrade -> Survival and
        // announced the assault starting all over again - and, less visibly, Upgrade -> Breathing
        // re-announced a Break that was already underway, because leaving Breathing had reset the
        // secured latch below.
        //
        // Returning early leaves BOTH _lastState and _wasAreaSecured untouched, so the pause is
        // invisible to the state machine and the transition that eventually matters is the real one
        // either side of it. Every future pause-like state gets this for free by being listed here.
        if (state == GameState.Upgrade)
            return;

        if (state != _lastState)
        {
            GameState previous = _lastState;
            _lastState = state;

            switch (state)
            {
                // RunStarted is the match beginning in the Lobby, kept distinct from SurvivalStarted
                // (walking out of it) - confirmed with the user. Guarded on coming FROM nothing so a
                // return to Lobby, if that ever exists, doesn't re-announce the run.
                case GameState.Lobby when previous == GameState.Lobby:
                    break;

                // Only the real starts: the first assault out of the Lobby, and each new assault
                // after a Break. With Upgrade skipped above, nothing else can reach Survival.
                case GameState.Survival when previous == GameState.Breathing || previous == GameState.Lobby:
                    RaiseForEveryLocalPlayer(frame, VoiceLineTrigger.SurvivalStarted);
                    break;

                case GameState.Boss:
                    RaiseForEveryLocalPlayer(frame, VoiceLineTrigger.BossStarted);
                    break;
            }
        }

        // Breathing fires on SECURED, not on entering the phase - confirmed with the user, and the
        // same gate the music and the AREA SECURED banner use. Until the field is clear it is still
        // combat, so "we can breathe now" would be a lie.
        bool secured = state == GameState.Breathing && frame.Global->BreathingAreaSecured;

        if (secured && _wasAreaSecured == false)
            RaiseForEveryLocalPlayer(frame, VoiceLineTrigger.BreathingTimeStarted);

        _wasAreaSecured = secured;
    }

    // Boss defeat has no event, but the boss entity's own disappearance is observable - the same
    // edge BossWidget already tracks to hide its HP bar.
    private unsafe void PollBoss(Frame frame)
    {
        bool alive = false;
        var filter = frame.Filter<BossRuntimeState>();
        while (filter.NextUnsafe(out _, out _))
        {
            alive = true;
            break;
        }

        if (alive == false && _wasBossAlive && frame.Global->CurrentState != GameState.RunFailed)
            RaiseForEveryLocalPlayer(frame, VoiceLineTrigger.BossDefeated);

        _wasBossAlive = alive;
    }

    // ------------------------------------------------------------------ event-driven triggers

    private void OnPlayerDowned(EventPlayerDowned e)
    {
        Raise(e.Game, VoiceLineTrigger.PlayerDowned, e.Entity);

        // Observers get their own line. Only the OTHER players speak here - the one who went down
        // already has PlayerDowned, and duplicating it would have them announce their own fall twice.
        ForEachLocalPlayerExcept(e.Game, e.Entity, observer =>
            Raise(e.Game, VoiceLineTrigger.TeammateDowned, observer, e.Entity));
    }

    private void OnPlayerKO(EventPlayerKO e) => Raise(e.Game, VoiceLineTrigger.PlayerKO, e.Entity);

    // Told apart by who the Reviver is:
    //   self   -> spent a charge (ReviveUtility.TryPerformSelfRevive revives the player with THEMSELF)
    //   nobody -> the automatic revive when a Breathing area is secured
    //   an ally -> a real teammate hold completed
    //
    // The first two both mean "I got back up and there is nobody to thank", so they share SelfRevive
    // and differ only by ContextValue.
    private void OnPlayerRevived(EventPlayerRevived e)
    {
        if (e.Reviver == e.Target || e.Reviver == EntityRef.None)
        {
            int cause = e.Reviver == EntityRef.None ? SelfReviveOnAreaSecured : SelfReviveByCharge;
            Raise(e.Game, VoiceLineTrigger.SelfRevive, e.Target, contextValue: cause);
            return;
        }

        // Same perspective as the start: the reviver speaks, about the teammate they just got up.
        Raise(e.Game, VoiceLineTrigger.RevivedTeammate, e.Reviver, e.Target);
    }

    // ReviveChannel exists only while someone is actively holding to revive (its presence IS the
    // in-progress flag - see Revive.qtn), so a channel appearing is the hold starting. No new
    // simulation event needed for something already visible as component state.
    private unsafe void PollReviveChannels(QuantumGame game, Frame frame)
    {
        _activeRevivers.Clear();

        var filter = frame.Filter<ReviveChannel>();
        while (filter.NextUnsafe(out EntityRef reviver, out ReviveChannel* channel))
        {
            _activeRevivers.Add(reviver);

            // Speaker is the REVIVER - the one doing something - with the downed player as Other.
            if (_knownRevivers.Contains(reviver) == false && channel->Target != EntityRef.None)
                Raise(game, VoiceLineTrigger.RevivingTeammate, reviver, channel->Target);
        }

        // Rebuilt rather than trimmed, so a channel that ended is forgotten and the next hold on the
        // same target counts as a fresh start - including after an interrupted attempt.
        _knownRevivers.Clear();
        foreach (EntityRef entity in _activeRevivers)
            _knownRevivers.Add(entity);
    }

    // Heavy hit is derived rather than given its own event: the existing damage event already carries
    // everything needed, and the threshold is a feel value that belongs in presentation config, not
    // baked into the simulation.
    private unsafe void OnEntityDamaged(EventEntityDamaged e)
    {
        if (e.Silent)
            return;

        Frame frame = e.Game.Frames.Predicted;

        if (frame.Unsafe.TryGetPointer<Health>(e.Target, out var health) == false)
            return;

        if (health->MaxHealth <= 0)
            return;

        float fraction = e.Damage.AsFloat / health->MaxHealth.AsFloat;

        if (fraction >= heavyHitThreshold)
            Raise(e.Game, VoiceLineTrigger.HeavyHitReceived, e.Target, contextValue: Mathf.RoundToInt(fraction * 100f));
    }

    private void OnAccessoryBlocked(EventAccessoryBlocked e)
        => Raise(e.Game, VoiceLineTrigger.AccessoryDropped, e.Owner, contextValue: e.RemainingDurability);

    // The pair-dialogue case. Recoverer is already carried on the event, so no gameplay change was
    // needed to tell "I got my own hat back" from "someone brought me my hat".
    private void OnAccessoryRecovered(EventAccessoryRecovered e)
    {
        if (logTriggers)
            LogHelper.Log(LogTag, $"AccessoryRecovered owner={e.Owner} recoverer={e.Recoverer} durability={e.Durability}", this);

        if (e.Recoverer == e.Owner || e.Recoverer == EntityRef.None)
        {
            Raise(e.Game, VoiceLineTrigger.AccessorySelfRecovered, e.Owner, contextValue: e.Durability);
            return;
        }

        // One line, on the COLLECTOR's bank, with the owner as Other - they are the one who did
        // something, so theirs is the bank to look in. The owner does not also speak: the clip is the
        // whole interaction, and the cross-talk lock keeps them quiet while it plays.
        Raise(e.Game, VoiceLineTrigger.AccessoryReturnedToAlly, e.Recoverer, e.Owner, e.Durability);
    }

    private void OnAccessoryBroken(EventAccessoryBroken e)
        => Raise(e.Game, VoiceLineTrigger.AccessoryBroken, e.Owner);

    // One trigger for both repair and replacement - the event still carries WasReplacement, but the
    // player's reaction to "it's fixed" doesn't change with what it cost.
    //
    // This is also the ONLY line for that purchase: GameplayUiController deliberately skips
    // ItemPurchased for the accessory card, so the two can't both fire for one click.
    private void OnAccessoryRestored(EventAccessoryRestored e)
        => Raise(e.Game, VoiceLineTrigger.AccessoryRestored, e.Owner, contextValue: e.Durability);

    // ------------------------------------------------------------------ public entry points
    //
    // For the triggers only a caller can know about. HeroSkillUsed / SkillNotReady / DashNotReady are
    // raised by SkillSoundView-style view code that already watches those slots, and
    // HeroExceptionalEvent is the extension point a future hero mechanic bridges into rather than
    // growing the trigger enum.

    public void Report(QuantumGame game, VoiceLineTrigger trigger, EntityRef speaker, int contextValue = 0)
        => Raise(game, trigger, speaker, contextValue: contextValue);

    public void ReportUpgrade(QuantumGame game, VoiceLineTrigger trigger, EntityRef speaker,
        AssetRef<UpgradeData> upgrade, int rank)
        => Raise(game, trigger, speaker, upgrade: upgrade, contextValue: rank);

    // ------------------------------------------------------------------ pair dialogue

    // ------------------------------------------------------------------ debug
    //
    // The point of this while there is still no recorded audio: play a run and confirm exactly WHEN
    // each trigger fires. logTriggers reports every request plus why it was rejected, which is
    // otherwise completely invisible.

    [NaughtyAttributes.Button("Log Voice State")]
    private void LogVoiceState()
    {
        LogHelper.Log(LogTag, $"config={(config != null ? config.name : "<none>")} banks={_banks.Count} heroes={_heroByData.Count} " +
            $"globalCooldownRemaining={Mathf.Max(0f, _globalCooldownUntil - Time.unscaledTime):0.0}s", this);

        foreach (var pair in _banks)
            LogHelper.Log(LogTag, $"  bank {pair.Key} -> {pair.Value.name}", this);
    }

    [SerializeField, Tooltip("Debug only: the trigger fired by the Test Trigger button below, on this client's first local player.")]
    private VoiceLineTrigger testTrigger = VoiceLineTrigger.HeroSkillUsed;

    [NaughtyAttributes.Button("Test Trigger (Play Mode)")]
    private void TestTrigger()
    {
        if (Application.isPlaying == false || MyLocalPlayer.Instance == null)
            return;

        foreach (var slot in MyLocalPlayer.Instance.Slots)
        {
            if (slot.IsSet == false || slot.EntityRef == EntityRef.None)
                continue;

            Raise(_game, testTrigger, slot.EntityRef);
            return;
        }
    }

    // ------------------------------------------------------------------ resolution

    private void Raise(QuantumGame game, VoiceLineTrigger trigger, EntityRef speaker,
        EntityRef relatedPlayer = default, int contextValue = 0, AssetRef<UpgradeData> upgrade = default)
    {
        if (game == null || speaker == EntityRef.None)
            return;

        Frame frame = game.Frames.Predicted;

        var request = new VoiceLineRequest(trigger, speaker, ResolveHero(frame, speaker),
            relatedPlayer, ResolveHero(frame, relatedPlayer), upgrade, contextValue);

        Play(request);
    }

    private unsafe HeroId ResolveHero(Frame frame, EntityRef entity)
    {
        if (entity == EntityRef.None || frame.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
            return HeroId.None;

        AssetGuid guid = stats->CharacterData.Id;

        if (_heroByData.TryGetValue(guid, out HeroId hero))
            return hero;

        // The single most common cause of a trigger resolving to nobody: that hero has no bank in
        // the director's list, or its bank has no CharacterData assigned. Named explicitly, once,
        // because "Related is missing" is otherwise impossible to trace back to a specific asset.
        if (_warnedUnmapped.Add(guid))
        {
            CharacterData data = frame.FindAsset(stats->CharacterData);
            string assetName = data != null ? data.name : guid.ToString();

            LogHelper.Error(LogTag, $"No voice bank registered for CharacterData '{assetName}'. " +
                $"Add that hero's HeroVoiceBank to VoiceDirector.banks and set its CharacterData field.", this);
        }

        return HeroId.None;
    }

    private void Play(VoiceLineRequest request)
    {
        if (logTriggers)
            LogHelper.Log(LogTag, request.ToString(), this);

        if (config == null)
            return;

        VoiceLineConfig.Rule rule = config.GetRule(request.Trigger);

        // No rule authored = deliberately silent. Opt-in, so adding a trigger to the enum can never
        // make the game chattier on its own.
        if (rule == null)
        {
            if (logTriggers)
                LogHelper.Log(LogTag, $"  -> no rule authored for {request.Trigger}, silent", this);

            return;
        }

        if (ShouldSpeak(request, rule, out string reason) == false)
        {
            if (logTriggers)
                LogHelper.Log(LogTag, $"  -> suppressed: {reason}", this);

            return;
        }

        AudioClip line = ResolveLine(request, out SoundData settings);

        // The cooldowns are stamped even with no clip authored, so pacing behaves identically once
        // audio lands - otherwise an unauthored trigger would silently reset the rhythm of the ones
        // that are authored.
        StampCooldowns(request, rule);

        if (line == null)
            return;

        // A more important line cuts a less important one; equal priority lets the current one
        // finish. The busy lock above already stops the SAME two heroes overlapping - this is the
        // separate case of an unrelated, more urgent moment (someone going down) arriving mid-bark.
        if (_current.IsPlaying)
        {
            if (rule.Priority <= _currentPriority)
                return;

            _current.Stop();
            ClearLocks();
        }

        _currentPriority = rule.Priority;
        _current = PlayForAudience(request, rule, settings, line);

        // Hold BOTH participants silent for the length of the clip. A pair line is the whole
        // interaction baked into one take - Max reacting to Pixie already contains Pixie - so letting
        // her start her own line over it would be two people talking at once about the same moment.
        LockSpeakers(request, line.length);
    }

    // Reports WHICH check rejected a request. A single "suppressed" covering six unrelated reasons
    // makes an authoring mistake (an unmapped hero) indistinguishable from correct pacing behaviour
    // (a cooldown), which is the difference between a two-second fix and an afternoon.
    private bool ShouldSpeak(VoiceLineRequest request, VoiceLineConfig.Rule rule, out string reason)
    {
        if (request.SpeakerHero == HeroId.None)
        {
            reason = "speaker has no HeroId - that hero has no voice bank registered (see the error above)";
            return false;
        }

        float busyNow = Time.unscaledTime;

        if (_busyUntil.TryGetValue(request.Speaker, out float speakerBusy) && busyNow < speakerBusy)
        {
            reason = $"speaker is still in a line ({speakerBusy - busyNow:0.0}s left)";
            return false;
        }

        // Also blocked while the OTHER participant is mid-line about them - otherwise Pixie answers
        // a clip that already contains her answer.
        if (request.RelatedPlayer != EntityRef.None
            && _busyUntil.TryGetValue(request.RelatedPlayer, out float relatedBusy) && busyNow < relatedBusy)
        {
            reason = $"the other participant is still in a line ({relatedBusy - busyNow:0.0}s left)";
            return false;
        }

        // LocalOnly is enforced here rather than left to the caller, so no trigger site has to
        // remember it - a teammate's private reaction is simply never built into a playback.
        if (rule.Audience == VoiceAudience.LocalOnly && IsLocal(request.Speaker) == false)
        {
            reason = "audience is LocalOnly and the speaker is not a local player";
            return false;
        }

        if (rule.Audience == VoiceAudience.RelevantPlayers
            && IsLocal(request.Speaker) == false && IsLocal(request.RelatedPlayer) == false)
        {
            reason = "audience is RelevantPlayers and neither participant is local";
            return false;
        }

        float now = Time.unscaledTime;

        if (now < _globalCooldownUntil)
        {
            reason = $"global cooldown ({_globalCooldownUntil - now:0.0}s left)";
            return false;
        }

        if (_speakerCooldowns.TryGetValue(request.Speaker, out float speakerUntil) && now < speakerUntil)
        {
            reason = $"per-speaker cooldown ({speakerUntil - now:0.0}s left)";
            return false;
        }

        var key = (request.Speaker, request.Trigger);
        if (_triggerCooldowns.TryGetValue(key, out float triggerUntil) && now < triggerUntil)
        {
            reason = $"per-trigger cooldown ({triggerUntil - now:0.0}s left)";
            return false;
        }

        // Rolled last, so a line rejected by chance doesn't consume the cooldown of one that never
        // had a chance to play.
        if (rule.Probability < 1f && Random.value > rule.Probability)
        {
            reason = $"probability roll failed ({rule.Probability:0.00})";
            return false;
        }

        reason = null;
        return true;
    }

    private void StampCooldowns(VoiceLineRequest request, VoiceLineConfig.Rule rule)
    {
        float now = Time.unscaledTime;

        _globalCooldownUntil = now + config.GlobalCooldown;
        _speakerCooldowns[request.Speaker] = now + config.PerSpeakerCooldown;
        _triggerCooldowns[(request.Speaker, request.Trigger)] = now + rule.Cooldown;
    }

    private AudioClip ResolveLine(VoiceLineRequest request, out SoundData settings)
    {
        settings = null;

        if (_banks.TryGetValue(request.SpeakerHero, out HeroVoiceBank bank) == false || bank == null)
            return null;

        settings = bank.voiceSettings;
        return bank.Resolve(request.Trigger, request.RelatedHero);
    }

    // Positioned for NearbyPlayers so a distant teammate's line falls off naturally; flat for
    // everything else, since a line meant for you shouldn't quieten because you walked away.
    private SoundHandle PlayForAudience(VoiceLineRequest request, VoiceLineConfig.Rule rule,
        SoundData settings, AudioClip line)
    {
        if (rule.Audience != VoiceAudience.NearbyPlayers)
            return AudioManager.PlayClip(settings, line);

        Transform speaker = EntityViewManager.Instance != null
            ? EntityViewManager.Instance.GetEntityTransform(request.Speaker)
            : null;

        return speaker != null
            ? AudioManager.PlayClipAttached(settings, line, speaker)
            : AudioManager.PlayClip(settings, line);
    }

    // An interrupted line's participants are released immediately - holding them silent for a take
    // that was cut off would mute them for a moment that no longer exists.
    private void ClearLocks() => _busyUntil.Clear();

    // Both the speaker and whoever the line is ABOUT go quiet until it finishes. Separate from the
    // per-speaker cooldown, which is pacing: this is about two heroes not overlapping on one moment.
    private void LockSpeakers(VoiceLineRequest request, float duration)
    {
        float until = Time.unscaledTime + duration + crossTalkGap;

        _busyUntil[request.Speaker] = until;

        if (request.RelatedPlayer != EntityRef.None)
            _busyUntil[request.RelatedPlayer] = until;
    }

    // ------------------------------------------------------------------ local-player helpers

    private static bool IsLocal(EntityRef entity)
        => entity != EntityRef.None
           && MyLocalPlayer.Instance != null
           && MyLocalPlayer.Instance.IsLocalEntity(entity);

    private void RaiseForEveryLocalPlayer(Frame frame, VoiceLineTrigger trigger)
    {
        if (MyLocalPlayer.Instance == null)
            return;

        foreach (var slot in MyLocalPlayer.Instance.Slots)
        {
            if (slot.IsSet && slot.EntityRef != EntityRef.None)
                Raise(_game, trigger, slot.EntityRef);
        }
    }

    private void ForEachLocalPlayerExcept(QuantumGame game, EntityRef excluded, System.Action<EntityRef> action)
    {
        if (MyLocalPlayer.Instance == null)
            return;

        foreach (var slot in MyLocalPlayer.Instance.Slots)
        {
            if (slot.IsSet && slot.EntityRef != EntityRef.None && slot.EntityRef != excluded)
                action(slot.EntityRef);
        }
    }
}
