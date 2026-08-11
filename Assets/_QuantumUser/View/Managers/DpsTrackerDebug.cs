using System.Text;
using NaughtyAttributes;
using Photon.Deterministic;
using Quantum;
using QuantumUser.View.Util;
using TMPro;
using UnityEngine;

namespace QuantumUser.View
{
    // Debug-only DPS meter: appends one line every `bucketSeconds` of Global.SurvivalTime (the
    // same deterministic run clock the Survival Director paces off - see
    // SurvivalProgressionUtility.AdvanceTimers), e.g. "0:30 - 42.3 dps", building a full log across
    // the run instead of a single rolling average. Uses SurvivalTime rather than wall-clock/frame
    // time so the log stays aligned with the actual run timeline and pauses along with it during a
    // level-up/Chest Upgrade screen (GameplaySystemGroup disabled - see docs/game-state.md).
    // Tracks all EventEntityDamaged hits owned by a locally-controlled player entity (MyLocalPlayer,
    // both couch-coop slots combined), regardless of target - i.e. this client's own damage output,
    // handy for checking a weapon/hero's actual DPS against the ~50 DPS balance baseline (see
    // docs/hero-balance-pass, CLAUDE.md's "Hero balance pass" memory).
    public unsafe class DpsTrackerDebug : QuantumGlobalMonoBehaviour
    {
        [SerializeField, Tooltip("Assign a TMP_Text here to have each 30s-bucket line appended to it live. Optional - lines always go to the console via LogHelper regardless.")]
        private TMP_Text outputText;

        [SerializeField, Tooltip("Bucket width in seconds - one log line per this many seconds of Global.SurvivalTime.")]
        private int bucketSeconds = 30;

        private readonly StringBuilder _log = new StringBuilder();
        private FP _accumulatedDamage;
        private FP _nextBucketThreshold;

        private void Awake()
        {
            QuantumEvent.Subscribe<EventEntityDamaged>(this, OnEntityDamaged);
            _nextBucketThreshold = (FP)bucketSeconds;
        }

        private void OnDestroy()
        {
            QuantumEvent.UnsubscribeListener(this);
        }

        private void OnEntityDamaged(EventEntityDamaged e)
        {
            if (MyLocalPlayer.Instance == null || MyLocalPlayer.Instance.IsLocalEntity(e.Owner) == false)
                return;

            _accumulatedDamage += e.Damage;
        }

        public override void QUpdate(QuantumGame game)
        {
            Frame f = game.Frames.Verified;
            if (f == null)
                return;

            // A while (not if) in case a single frame's delta somehow spans more than one bucket -
            // e.g. a long stall/catch-up tick - so no bucket is silently skipped.
            while (f.Global->SurvivalTime >= _nextBucketThreshold)
            {
                AppendLine(_nextBucketThreshold, _accumulatedDamage.AsFloat / bucketSeconds);
                _accumulatedDamage = FP._0;
                _nextBucketThreshold += (FP)bucketSeconds;
            }
        }

        private void AppendLine(FP bucketEnd, float dps)
        {
            int totalSeconds = Mathf.RoundToInt(bucketEnd.AsFloat);
            string timestamp = $"{totalSeconds / 60}:{totalSeconds % 60:00}";
            string line = $"{timestamp} - {dps:0.0} dps";

            _log.AppendLine(line);
            if (outputText != null)
                outputText.text = _log.ToString();

            LogHelper.Log("Dps", line);
        }

        [Button]
        public void ResetLog()
        {
            _log.Clear();
            _accumulatedDamage = FP._0;
            _nextBucketThreshold = (FP)bucketSeconds;
            if (outputText != null)
                outputText.text = string.Empty;
        }
    }
}
