using UnityEngine;

namespace Project.Audio.EditorTools
{
    // Estimates tempo from a decoded clip so loop markers can be snapped onto a beat/bar grid instead
    // of landing mid-transient, which is what actually causes an audible "seam" on an otherwise clean
    // loop - the ear is far more forgiving of a small timing error than of a loop point that doesn't
    // land on the beat the rest of the track implies.
    //
    // Standard onset-autocorrelation tempo estimate: build an energy-flux novelty curve (onsets show
    // up as positive spikes in how fast the signal is getting LOUDER, which a plain RMS envelope
    // doesn't isolate on its own), autocorrelate it across the plausible tempo range, and take the lag
    // with the strongest self-similarity as the beat period. This is a well-known, "good enough for
    // authoring" technique - not a beat tracker - so the result is a starting point the window lets
    // the user nudge or override, never treated as ground truth.
    //
    // Meter (2/4 vs 3/4 vs 4/4) is deliberately NOT guessed - real downbeat detection needs a proper
    // accent/ML model to be trustworthy, and the overwhelming majority of what this tool sees is 4/4
    // anyway. The window just defaults beats-per-bar to 4 and leaves it a plain editable field for the
    // rare waltz or march.
    internal static class AudioBpmEstimator
    {
        private const float MinBpm = 60f;
        private const float MaxBpm = 200f;
        private const float WindowSeconds = 0.023f; // ~1024 samples at 44.1kHz - fine enough to resolve 200bpm, coarse enough to stay fast.

        internal struct Result
        {
            public float Bpm;
            public float BeatOffset; // Seconds - where "beat 0" of the estimated grid falls (see EstimatePhase for how this is found).
        }

        // False when the clip is too short/quiet to say anything meaningful - callers should leave
        // whatever BPM field they have untouched rather than overwrite it with noise.
        internal static bool Estimate(AudioSilenceSplitter.ClipData data, out Result result)
        {
            result = default;
            if (data?.Samples == null || data.Frequency <= 0)
                return false;

            int windowSize = Mathf.Max(256, Mathf.RoundToInt(data.Frequency * WindowSeconds));
            int windowCount = data.Frames / windowSize;
            if (windowCount < 16)
                return false;

            float[] flux = BuildOnsetFlux(data, windowSize, windowCount);
            float windowsPerSecond = (float)data.Frequency / windowSize;

            // Bias the TEMPO read toward the middle of the track rather than trusting the whole thing
            // equally: an intro is exactly where a song is most likely to be sparse, rubato, or not
            // have landed on the groove yet, and if it's LOUD (a swell, a non-metrical riff) it can
            // pull a whole-track correlation toward the wrong lag even though it's a small fraction of
            // the track. Skip a chunk off each end when there's enough left to still say something
            // meaningful; a short clip just uses everything it's got. Phase (EstimatePhase, below)
            // deliberately does NOT get this same treatment - it needs to find where the real beat
            // starts, which the trimmed-off edges might still be part of.
            const float EdgeSkipFraction = 0.15f;
            const float MinCoreSeconds = 8f;
            int edgeSkip = Mathf.RoundToInt(windowCount * EdgeSkipFraction);
            int minCoreWindows = Mathf.RoundToInt(MinCoreSeconds * windowsPerSecond);

            int coreStart = 0;
            int coreEnd = windowCount;
            if (windowCount - 2 * edgeSkip >= minCoreWindows)
            {
                coreStart = edgeSkip;
                coreEnd = windowCount - edgeSkip;
            }

            int minLag = Mathf.Max(1, Mathf.RoundToInt(windowsPerSecond * 60f / MaxBpm));
            int maxLag = Mathf.Min(coreEnd - coreStart - 1, Mathf.RoundToInt(windowsPerSecond * 60f / MinBpm));
            if (maxLag <= minLag)
                return false;

            int bestLag = AutocorrelationPeak(flux, minLag, maxLag, coreStart, coreEnd);
            if (bestLag <= 0)
                return false;

            float bpm = 60f * windowsPerSecond / bestLag;

            // Autocorrelation happily locks onto a half- or double-tempo harmonic (a strong backbeat
            // reads as "the beat" just as easily as the actual beat does). Folding into one octave
            // around typical song tempo is a cheap, usually-correct fix - and if it guesses wrong, the
            // BPM field in the window is a plain editable float, not a locked result.
            while (bpm < 70f)
                bpm *= 2f;
            while (bpm > 180f)
                bpm /= 2f;

            float beatPeriod = 60f / bpm;
            float beatOffset = EstimatePhase(flux, windowsPerSecond, beatPeriod);

            result = new Result { Bpm = bpm, BeatOffset = beatOffset };
            return true;
        }

        // Where "beat 0" of the grid falls, mod one beat period. Deliberately searched across the
        // WHOLE track rather than just its first couple of seconds: a song can open with a rubato or
        // ambient intro before the groove actually starts, and anchoring to whatever's loudest near
        // t=0 would misalign the entire grid against a track like that. Instead this tries every
        // possible phase (there are only `beatPeriod` seconds worth, at window resolution) and picks
        // whichever one lines up best with onsets EVERYWHERE in the track - the real groove, wherever
        // it starts, dominates the sum; a quiet/irregular intro barely votes for any phase in
        // particular. O(windowCount) total despite the nested loop: the inner loop's step means every
        // window is visited exactly once across all phases combined.
        private static float EstimatePhase(float[] flux, float windowsPerSecond, float beatPeriod)
        {
            int periodWindows = Mathf.Max(1, Mathf.RoundToInt(beatPeriod * windowsPerSecond));
            int bestPhase = 0;
            double bestScore = double.MinValue;

            for (int phase = 0; phase < periodWindows; phase++)
            {
                double sum = 0;
                for (int w = phase; w < flux.Length; w += periodWindows)
                    sum += flux[w];

                if (sum > bestScore)
                {
                    bestScore = sum;
                    bestPhase = phase;
                }
            }

            return bestPhase / windowsPerSecond;
        }

        // Half-wave rectified energy flux: how much LOUDER this window is than the last one, floored
        // at zero. Loud-to-quiet transitions (a note decaying) produce no novelty, which is exactly
        // right for onset detection - only attacks should vote for the beat grid.
        private static float[] BuildOnsetFlux(AudioSilenceSplitter.ClipData data, int windowSize, int windowCount)
        {
            float[] energy = new float[windowCount];

            for (int w = 0; w < windowCount; w++)
            {
                int start = w * windowSize;
                double sum = 0;
                for (int f = 0; f < windowSize; f++)
                {
                    int baseIndex = (start + f) * data.Channels;
                    float s = 0f;
                    for (int c = 0; c < data.Channels; c++)
                        s += data.Samples[baseIndex + c];

                    s /= data.Channels;
                    sum += s * s;
                }

                energy[w] = Mathf.Sqrt((float)(sum / windowSize));
            }

            float[] flux = new float[windowCount];
            for (int w = 1; w < windowCount; w++)
                flux[w] = Mathf.Max(0f, energy[w] - energy[w - 1]);

            return flux;
        }

        // Sums only over [rangeStart, rangeEnd) - the "core" of the track (see Estimate) - though
        // flux[w - lag] can still legally reach a few windows before rangeStart, which is fine and
        // wanted: it's still real onset data, just from just outside the nominal core boundary.
        private static int AutocorrelationPeak(float[] flux, int minLag, int maxLag, int rangeStart, int rangeEnd)
        {
            int bestLag = -1;
            float bestScore = float.MinValue;

            for (int lag = minLag; lag <= maxLag; lag++)
            {
                double sum = 0;
                int from = Mathf.Max(lag, rangeStart);
                int count = rangeEnd - from;
                for (int w = from; w < rangeEnd; w++)
                    sum += flux[w] * flux[w - lag];

                float score = count > 0 ? (float)(sum / count) : 0f;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestLag = lag;
                }
            }

            return bestLag;
        }

        // Nearest grid line to `time`, where the grid is `beatsPerStep` beats wide (1 = every beat,
        // 4 = every bar in 4/4) starting from the estimated (or hand-authored) beat offset.
        internal static float SnapToGrid(float time, float bpm, float beatOffset, int beatsPerStep)
        {
            if (bpm <= 0.01f)
                return time;

            float step = 60f / bpm * Mathf.Max(1, beatsPerStep);
            float n = Mathf.Round((time - beatOffset) / step);
            return Mathf.Max(0f, beatOffset + n * step);
        }
    }
}
