using System;
using System.Collections.Generic;
using System.Linq;

namespace YARG.Core.Song.Recommendations
{
    public enum RecommendationKind
    {
        ForYou,
        AtYourLevel,
        NextStepUp,
        Challenge,
    }

    public sealed class RecommendedSong
    {
        public SongFacts Song = null!;
        public RecommendationKind Kind;
        public float Taste;
        public float PredictedAccuracy;
    }

    /// <summary>
    /// Builds the recommendation rows, picks Song Swipe cards, and finds likely mistaken swipes.
    /// </summary>
    public static class Recommender
    {
        public const float AT_LEVEL = 0.95f;
        public const float STRETCH = 0.85f;
        public const float CHALLENGE = 0.65f;

        private static readonly IReadOnlyDictionary<RecommendationKind, int> RowSizes =
            new Dictionary<RecommendationKind, int>
            {
                { RecommendationKind.ForYou, 8 },
                { RecommendationKind.AtYourLevel, 6 },
                { RecommendationKind.NextStepUp, 6 },
                { RecommendationKind.Challenge, 4 },
            };

        private const double RECENTLY_PLAYED_DAYS = 2;
        private const double FRESHNESS_NOISE = 0.05;
        private const double DISCOVERY_NOISE = 0.5;
        private const int ARTIST_PER_ROW = 1;
        private const int ARTIST_TOTAL = 1;
        private const int GENRE_PER_ROW = 3;
        private const int GENRE_TOTAL = 6;

        private const double SWIPE_TOP_BAND = 0.15;
        private const double SWIPE_MIDDLE_BAND = 0.6;
        private const int SWIPE_BAND_SAMPLE = 400;
        private const double SWIPE_NOISE = 0.25;
        private const int MISTAKE_FOLDS = 5;

        /// <summary>
        /// Picks the recommendation rows. The models are passed in so a caller can reuse them when only
        /// the random picks change, as on a refresh.
        /// </summary>
        /// <param name="skip">Song keys to leave out, for example the ones shown before a refresh.</param>
        public static List<RecommendedSong> Recommend(IReadOnlyDictionary<string, SongFacts> library,
            ProfileHistory history, TasteModel taste, SkillModel skill, Random random, ISet<string>? skip = null)
        {
            string IdOf(string key) => SongFacts.IdentityOf(library, key);

            // Plays and passes apply to every chart of a song. A pass hides a song only until it is played.
            var played = new HashSet<string>(history.Plays.Select(p => IdOf(p.Key)));
            var recent = new HashSet<string>(history.Plays
                .Where(p => (history.Now - p.Date).TotalDays < RECENTLY_PLAYED_DAYS)
                .Select(p => IdOf(p.Key)));
            var passed = new HashSet<string>(history.LatestFeedback(library)
                .Where(f => !f.Liked)
                .Select(f => IdOf(f.Key))
                .Where(id => !played.Contains(id)));

            // A little randomness keeps lists fresh. Discovery adds a larger bonus scaled by how unsure the
            // model is, for one exploratory For You slot.
            var discovery = new Dictionary<SongFacts, double>();
            var candidates = library.Values
                .Where(s => s.Canonical && s.ChartDifficulty.HasValue && !recent.Contains(s.Identity) &&
                    !passed.Contains(s.Identity) && (skip == null || !skip.Contains(s.Key)))
                .Select(s =>
                {
                    float score = taste.Score(s) + (float) (NextGaussian(random) * FRESHNESS_NOISE);
                    discovery[s] = score + Math.Abs(NextGaussian(random)) * DISCOVERY_NOISE * taste.Uncertainty(s);
                    return new RecommendedSong { Song = s, Taste = score, PredictedAccuracy = skill.PredictSong(s) };
                })
                .OrderByDescending(s => s.Taste)
                .ToList();

            var result = new List<RecommendedSong>();
            var variety = new VarietyRules(ARTIST_PER_ROW, ARTIST_TOTAL, GENRE_PER_ROW, GENRE_TOTAL);

            void Take(RecommendationKind kind, IEnumerable<RecommendedSong> ordered, int count)
            {
                foreach (var song in ordered)
                {
                    if (count <= 0) break;
                    if (!variety.TryAdd(song.Song, (int) kind)) continue;

                    song.Kind = kind;
                    result.Add(song);
                    count--;
                }
            }

            int RemainingIn(RecommendationKind kind) => RowSizes[kind] - result.Count(s => s.Kind == kind);

            // For You: one known favorite, the best matches not played yet, and one discovery
            var forYou = candidates.Where(s => s.PredictedAccuracy >= STRETCH).ToList();
            Take(RecommendationKind.ForYou, forYou.Where(s => played.Contains(s.Song.Identity)), 1);
            Take(RecommendationKind.ForYou, forYou.Where(s => !played.Contains(s.Song.Identity)),
                RemainingIn(RecommendationKind.ForYou) - 1);
            Take(RecommendationKind.ForYou, forYou.OrderByDescending(s => discovery[s.Song]), 1);
            Take(RecommendationKind.ForYou, forYou, RemainingIn(RecommendationKind.ForYou));

            Take(RecommendationKind.AtYourLevel, candidates.Where(s => s.PredictedAccuracy >= AT_LEVEL),
                RowSizes[RecommendationKind.AtYourLevel]);

            // The ladder: the best-liked songs just above the comfort zone, not mastered yet, easiest first
            Take(RecommendationKind.NextStepUp, candidates.Where(s =>
                    s.PredictedAccuracy >= STRETCH && s.PredictedAccuracy < AT_LEVEL &&
                    (skill.BestAccuracy(s.Song.Key) ?? 0f) < AT_LEVEL),
                RowSizes[RecommendationKind.NextStepUp]);
            var ladder = result.Where(s => s.Kind == RecommendationKind.NextStepUp)
                .OrderByDescending(s => s.PredictedAccuracy).ToList();
            result.RemoveAll(s => s.Kind == RecommendationKind.NextStepUp);
            result.AddRange(ladder);

            Take(RecommendationKind.Challenge, candidates.Where(s =>
                    s.PredictedAccuracy >= CHALLENGE && s.PredictedAccuracy < STRETCH),
                RowSizes[RecommendationKind.Challenge]);

            return result.OrderBy(s => s.Kind).ToList();
        }

        /// <summary>
        /// Picks Song Swipe cards. A model learns little from answers it could predict, so each batch
        /// rotates through songs the profile will probably like (top of the ranking), songs the model is
        /// unsure about (the middle) and songs it expects a pass on (the bottom). Within each band it
        /// favors unexplored genres and artists, and never repeats an artist in a batch.
        /// </summary>
        public static List<SongFacts> PickSwipeCards(IReadOnlyDictionary<string, SongFacts> library, TasteModel taste,
            ISet<string> exclude, int count, Random random)
        {
            var seen = new HashSet<string>(exclude.Concat(taste.Evidence.Keys)
                .Select(key => SongFacts.IdentityOf(library, key)));
            var pool = library.Values.Where(s => s.Canonical && !seen.Contains(s.Identity)).ToList();
            var playable = pool.Where(s => s.ChartDifficulty.HasValue).ToList();
            var ranked = (playable.Count > 0 ? playable : pool).OrderByDescending(taste.Score).ToList();
            if (ranked.Count == 0) return new List<SongFacts>();

            int topEnd = Math.Max(1, (int) (ranked.Count * SWIPE_TOP_BAND));
            int middleEnd = Math.Min(ranked.Count, Math.Max(topEnd + 1, (int) (ranked.Count * SWIPE_MIDDLE_BAND)));
            var bands = new[] { ranked.GetRange(0, topEnd), Slice(ranked, topEnd, middleEnd), Slice(ranked, middleEnd, ranked.Count) };

            var picked = new List<SongFacts>();
            var variety = new VarietyRules(artistPerGroup: 1, artistTotal: 1);
            var explored = new Dictionary<SongFeature, int>();
            int band = random.Next(bands.Length);
            for (int misses = 0; picked.Count < count && misses < bands.Length; band = (band + 1) % bands.Length)
            {
                // Drop what the variety rules now rule out, so sampling only sees songs it could pick
                var candidates = bands[band];
                candidates.RemoveAll(song => !variety.Allows(song));
                SongFacts? best = null;
                double bestScore = double.MinValue;
                for (int tries = Math.Min(candidates.Count, SWIPE_BAND_SAMPLE); tries > 0; tries--)
                {
                    var song = candidates[random.Next(candidates.Count)];
                    double score = taste.Uncertainty(song, explored) + SWIPE_NOISE * NextGumbel(random);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = song;
                    }
                }

                if (best == null)
                {
                    misses++;
                    continue;
                }

                misses = 0;
                variety.TryAdd(best);
                picked.Add(best);
                foreach (var feature in best.Features)
                {
                    explored[feature] = (explored.TryGetValue(feature, out int n) ? n : 0) + 1;
                }
            }

            return picked;
        }

        /// <summary>
        /// Orders the profile's swipes by how much each one disagrees with everything else it has done,
        /// most surprising first, so accidental likes and passes can be reviewed. Uses cross-validation:
        /// each fifth of the swipes is judged by a model trained without it.
        /// </summary>
        public static List<(string Key, bool Liked, float Surprise)> RankLikelyMistakes(
            IReadOnlyDictionary<string, SongFacts> library, ProfileHistory history)
        {
            var latest = history.LatestFeedback(library).Where(f => library.ContainsKey(f.Key)).ToList();
            var result = new List<(string, bool, float)>();
            for (int fold = 0; fold < MISTAKE_FOLDS; fold++)
            {
                var held = latest.Where((_, i) => i % MISTAKE_FOLDS == fold).ToList();
                if (held.Count == 0) continue;

                var keys = new HashSet<string>(held.Select(f => f.Key));
                var taste = TasteModel.Build(library, history.WithoutFeedbackOn(library, keys), keys);
                foreach (var swipe in held)
                {
                    float score = taste.Score(library[swipe.Key]);
                    result.Add((swipe.Key, swipe.Liked, swipe.Liked ? -score : score));
                }
            }

            return result.OrderByDescending(r => r.Item3).ToList();
        }

        private static List<SongFacts> Slice(List<SongFacts> list, int start, int end) =>
            list.GetRange(start, Math.Max(0, end - start));

        private static double NextGaussian(Random random)
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = random.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        private static double NextGumbel(Random random) => -Math.Log(-Math.Log(1.0 - random.NextDouble() * 0.999999));
    }
}
