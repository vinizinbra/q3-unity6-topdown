using System.Collections;
using System.Collections.Generic;
using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;

// Training Zone guide (on the TrainingZone root, added by TrainingZoneBuilder) - pure View, see
// docs/training-zone.md. Every station's ground text starts hidden and pops in (scaled up with a
// little overshoot, one label after another) the first time the local player walks into that
// station. Stations are the
// root's Chunk children in course order; "inside" is the same X/Z footprint test LobbyBoundarySystem
// uses (min corner = the station transform, size = its baked Chunk size). Interacting with the Finish
// POI (Interactable{Kind = TrainingFinish}, see TrainingZone.qtn) fires EventTrainingFinished; for a
// local player this waits TrainingFinish.LeaveDelay, then goes back to the menu through the same
// MatchMakingConfig.LeaveMatch path the Skip button uses.
public class TrainingZoneGuideWidget : QuantumGlobalMonoBehaviour
{
    // Reveal a station's text this far BEFORE the player reaches it, so it pops in ahead of them.
    [SerializeField] private float revealLookahead = 3f;
    [SerializeField] private float popDuration = 0.22f;
    [SerializeField] private float settleDuration = 0.12f;
    [SerializeField] private float popOvershoot = 1.15f;
    [SerializeField] private float labelStagger = 0.08f;

    private class Station
    {
        public Vector3 Min;
        public Vector2 Size;
        public TextMeshPro[] Labels;
        public Vector3[] LabelScales;
    }

    private readonly List<Station> _stations = new List<Station>();
    private int _reached = -1;
    private bool _finished;

    private void Awake()
    {
        QuantumEvent.Subscribe<EventTrainingFinished>(this, OnTrainingFinished);

        foreach (Transform child in transform)
        {
            var chunk = child.GetComponent<QPrototypeChunk>();

            if (chunk == null)
                continue;

            var station = new Station
            {
                Min = child.position,
                Size = new Vector2(chunk.Prototype.ChunkSizeWidth, chunk.Prototype.ChunkSizeDepth),
                Labels = child.GetComponentsInChildren<TextMeshPro>(true),
            };

            station.LabelScales = new Vector3[station.Labels.Length];

            for (int i = 0; i < station.Labels.Length; i++)
            {
                station.LabelScales[i] = station.Labels[i].transform.localScale;
                station.Labels[i].transform.localScale = Vector3.zero;
            }

            _stations.Add(station);
        }
    }

    private void OnDestroy()
    {
        QuantumEvent.UnsubscribeListener(this);
    }

    public override void QUpdate(QuantumGame game)
    {
        if (MyLocalPlayer.Instance == null || MyLocalPlayer.Instance.IsLocalPlayerSetup == false)
            return;

        if (game.Frames.Predicted.TryGet(MyLocalPlayer.Instance.EntityRef, out Transform3D transform3D) == false)
            return;

        int index = FindStation(transform3D.Position.ToUnityVector3());

        if (index <= _reached)
            return;

        // Skipping ahead (e.g. a dash over a whole station) still reveals everything passed on the way.
        for (int i = _reached + 1; i <= index; i++)
            StartCoroutine(PopLabels(_stations[i]));

        _reached = index;
    }

    // The furthest station whose footprint (widened by revealLookahead on its entry side) contains
    // the player.
    private int FindStation(Vector3 position)
    {
        for (int i = _stations.Count - 1; i >= 0; i--)
        {
            Station s = _stations[i];

            if (position.x >= s.Min.x - revealLookahead && position.x <= s.Min.x + s.Size.x
                && position.z >= s.Min.z && position.z <= s.Min.z + s.Size.y)
                return i;
        }

        return -1;
    }

    private void OnTrainingFinished(EventTrainingFinished e)
    {
        if (_finished || IsLocalPlayer(e.Player) == false)
            return;

        _finished = true;

        Frame frame = QuantumRunner.Default != null ? QuantumRunner.Default.Game.Frames.Predicted : null;
        float delay = frame != null && frame.TryGet(e.Target, out TrainingFinish finish) ? finish.LeaveDelay.AsFloat : 0f;
        StartCoroutine(LeaveAfterDelay(delay));
    }

    private static bool IsLocalPlayer(EntityRef entity)
    {
        if (MyLocalPlayer.Instance == null)
            return false;

        foreach (LocalPlayerSlot slot in MyLocalPlayer.Instance.Slots)
        {
            if (slot.IsSet && slot.EntityRef == entity)
                return true;
        }

        return false;
    }

    private IEnumerator LeaveAfterDelay(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);

        if (MatchMakingConfig.Instance != null)
            MatchMakingConfig.Instance.LeaveMatch();
    }

    // Unscaled - a tutorial/level-up pause shouldn't freeze a half-popped label.
    private IEnumerator PopLabels(Station station)
    {
        for (int i = 0; i < station.Labels.Length; i++)
        {
            StartCoroutine(Pop(station.Labels[i].transform, station.LabelScales[i]));
            yield return new WaitForSecondsRealtime(labelStagger);
        }
    }

    private IEnumerator Pop(Transform label, Vector3 targetScale)
    {
        Vector3 overshoot = targetScale * popOvershoot;

        for (float t = 0f; t < popDuration; t += Time.unscaledDeltaTime)
        {
            float k = 1f - Mathf.Pow(1f - t / popDuration, 3f); // ease-out cubic
            label.localScale = Vector3.LerpUnclamped(Vector3.zero, overshoot, k);
            yield return null;
        }

        for (float t = 0f; t < settleDuration; t += Time.unscaledDeltaTime)
        {
            label.localScale = Vector3.Lerp(overshoot, targetScale, t / settleDuration);
            yield return null;
        }

        label.localScale = targetScale;
    }
}
