using System;
using System.Collections.Generic;
using System.Linq;

namespace YARG.Core.Song.Recommendations
{
    /// <summary>
    /// A per-profile preference model: L2-regularized logistic regression with one learned weight per song
    /// feature (each artist, genre word, decade, source and so on) plus a learned direction on the
    /// <see cref="ArtistMap"/>. Nothing is hand-weighted; the data decides what matters for the player.
    /// </summary>
    /// <remarks>
    /// Songs with positive evidence are positives and those with negative evidence negatives, each weighted
    /// by the size of the evidence. A stable sample of untouched songs is added as weak negatives, the usual
    /// approach to implicit feedback: in a big library a random song is more likely than not one the player
    /// would skip.
    /// </remarks>
    public sealed class PreferenceModel
    {
        private const float MAX_EXAMPLE_WEIGHT = 3f;
        private const float IMPLICIT_NEGATIVE_WEIGHT = 0.2f;
        private const int IMPLICIT_NEGATIVES_PER_POSITIVE = 4;
        private const int MIN_IMPLICIT_NEGATIVES = 100;
        private const int MAX_IMPLICIT_NEGATIVES = 1000;
        private const float L2 = 1f;
        private const float LEARNING_RATE = 0.5f;
        private const int ITERATIONS = 150;

        // Map positions have unit length; scaling them lets one L2 penalty suit both kinds of input
        private const float POSITION_SCALE = 4f;

        public static readonly PreferenceModel Empty = new();

        private readonly Dictionary<SongFeature, int> _index = new();
        private float[] _weights = Array.Empty<float>();
        private float[] _direction = Array.Empty<float>();
        private float _bias;

        public float Weight(SongFeature feature) => _index.TryGetValue(feature, out int i) ? _weights[i] : 0f;

        public float Score(SongFacts song)
        {
            float score = _bias;
            foreach (var feature in song.Features)
            {
                if (_index.TryGetValue(feature, out int i)) score += _weights[i];
            }

            var position = song.ArtistPosition;
            if (position != null && position.Length == _direction.Length)
            {
                for (int d = 0; d < position.Length; d++) score += _direction[d] * position[d] * POSITION_SCALE;
            }

            return score;
        }

        public static PreferenceModel Train(IReadOnlyDictionary<string, SongFacts> library,
            IReadOnlyDictionary<string, float> evidence, ISet<string>? heldOut = null)
        {
            if (!evidence.Keys.Any(library.ContainsKey))
            {
                return Empty;
            }

            var model = new PreferenceModel();
            var rows = new List<(int[] Features, float[]? Position, float Label, float Weight)>();
            int dims = library.Values.FirstOrDefault(s => s.ArtistPosition != null)?.ArtistPosition!.Length ?? 0;

            int[] Encode(SongFacts song)
            {
                var indices = new HashSet<int>();
                foreach (var feature in song.Features)
                {
                    if (!model._index.TryGetValue(feature, out int i))
                    {
                        i = model._index.Count;
                        model._index[feature] = i;
                    }

                    indices.Add(i);
                }

                return indices.ToArray();
            }

            float[]? PositionOf(SongFacts song) => song.ArtistPosition?.Length == dims ? song.ArtistPosition : null;

            int positives = 0;
            foreach (var (key, value) in evidence)
            {
                if (value == 0f || !library.TryGetValue(key, out var song)) continue;
                if (value > 0f) positives++;
                rows.Add((Encode(song), PositionOf(song), value > 0f ? 1f : 0f, Math.Min(Math.Abs(value), MAX_EXAMPLE_WEIGHT)));
            }

            // Every chart of a song with evidence (or held out) stays out of the implicit negatives
            var touched = new HashSet<string>(evidence.Keys.Concat(heldOut ?? Enumerable.Empty<string>())
                .Select(key => SongFacts.IdentityOf(library, key)));
            int negatives = Math.Clamp(positives * IMPLICIT_NEGATIVES_PER_POSITIVE, MIN_IMPLICIT_NEGATIVES, MAX_IMPLICIT_NEGATIVES);
            foreach (var song in library.Values
                .Where(s => s.Canonical && !touched.Contains(s.Identity))
                .OrderBy(s => StableHash(s.Key))
                .Take(negatives))
            {
                rows.Add((Encode(song), PositionOf(song), 0f, IMPLICIT_NEGATIVE_WEIGHT));
            }

            // Full-batch AdaGrad on weighted log loss plus L2 (the bias is not regularized)
            var weights = new Parameters(model._index.Count);
            var direction = new Parameters(dims);
            var bias = new Parameters(1);
            for (int iteration = 0; iteration < ITERATIONS; iteration++)
            {
                weights.ClearGradient();
                direction.ClearGradient();
                bias.ClearGradient();
                foreach (var (features, position, label, weight) in rows)
                {
                    float z = bias.Values[0];
                    foreach (int i in features) z += weights.Values[i];
                    if (position != null)
                    {
                        for (int d = 0; d < dims; d++) z += direction.Values[d] * position[d] * POSITION_SCALE;
                    }

                    float error = weight * (1f / (1f + (float) Math.Exp(-z)) - label);
                    bias.Gradient[0] += error;
                    foreach (int i in features) weights.Gradient[i] += error;
                    if (position != null)
                    {
                        for (int d = 0; d < dims; d++) direction.Gradient[d] += error * position[d] * POSITION_SCALE;
                    }
                }

                weights.Step(L2);
                direction.Step(L2);
                bias.Step(0f);
            }

            model._weights = weights.Values;
            model._direction = direction.Values;
            model._bias = bias.Values[0];
            return model;
        }

        private sealed class Parameters
        {
            public readonly float[] Values;
            public readonly float[] Gradient;
            private readonly float[] _history;

            public Parameters(int count)
            {
                Values = new float[count];
                Gradient = new float[count];
                _history = new float[count];
            }

            public void ClearGradient() => Array.Clear(Gradient, 0, Gradient.Length);

            public void Step(float l2)
            {
                for (int i = 0; i < Values.Length; i++)
                {
                    float g = Gradient[i] + l2 * Values[i];
                    _history[i] += g * g;
                    Values[i] -= LEARNING_RATE * g / ((float) Math.Sqrt(_history[i]) + 1e-6f);
                }
            }
        }

        // FNV-1a: stable across runs and platforms, unlike string.GetHashCode
        private static uint StableHash(string text)
        {
            uint hash = 2166136261;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 16777619;
            }

            return hash;
        }
    }
}
