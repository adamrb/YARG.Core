using System;
using System.Collections.Generic;
using System.Linq;

namespace YARG.Core.Song.Recommendations
{
    /// <summary>
    /// What a profile enjoys, learned from how it has actually behaved.
    /// </summary>
    /// <remarks>
    /// Every song the profile has touched gets an enjoyment "evidence" number: positive for a like,
    /// negative for a dislike. Coming back to a song on another day is the strongest signal, especially
    /// after a poor score, and play history is judged against the profile's typical play, so a song played
    /// once ranks below the ones it keeps returning to. A low score never counts against a song (that is
    /// the <see cref="SkillModel"/>'s job). Favorites, swipes and quits keep their own sign. The newest
    /// signal wins: a swipe or quit counts only if it came after the song was last played, and a swipe
    /// after the last play replaces what the plays said. All charts of a song count as one song. A
    /// <see cref="PreferenceModel"/> is then trained on that evidence.
    /// </remarks>
    public sealed class TasteModel
    {
        private const float FIRST_PLAY = 0.4f;
        private const float RETURN_VISIT = 1.0f;
        private const float RETURN_AFTER_BAD_SCORE = 0.5f;
        private const float BAD_SCORE = 0.85f;
        private const int MAX_COUNTED_RETURNS = 4;
        private const float FAVORITE = 1.5f;
        private const float SWIPE_LIKE = 1.0f;
        private const float SWIPE_PASS = -0.8f;
        private const float EARLY_QUIT = -0.6f;
        private const float LATE_QUIT = -0.2f;
        private const double RECENCY_HALF_LIFE_DAYS = 90;

        // Play evidence above this share of the profile's average play counts as a like
        private const float BASELINE_SHARE = 0.5f;

        private readonly Dictionary<string, float> _evidence = new();
        private readonly Dictionary<SongFeature, int> _observations = new();
        private PreferenceModel _preferences = PreferenceModel.Empty;

        /// <summary>
        /// Enjoyment evidence for the songs the profile has touched, positive for a like, keyed by one chart
        /// of each song (the canonical one when the song is in the library).
        /// </summary>
        public IReadOnlyDictionary<string, float> Evidence => _evidence;

        /// <param name="heldOut">Songs to leave out of the weak-negative sample (for cross-validation).</param>
        public static TasteModel Build(IReadOnlyDictionary<string, SongFacts> library, ProfileHistory history,
            ISet<string>? heldOut = null)
        {
            var model = new TasteModel();
            var songKey = SongFacts.CanonicalKeyLookup(library);
            var swipes = history.CurrentFeedback(library).ToDictionary(f => songKey(f.Key));

            var played = new Dictionary<string, float>();
            foreach (var songPlays in history.Plays.GroupBy(p => songKey(p.Key)))
            {
                var days = songPlays
                    .GroupBy(p => p.Date.Date)
                    .OrderBy(g => g.Key)
                    .Select(g => g.Max(p => p.Accuracy))
                    .ToList();

                int returns = Math.Min(days.Count - 1, MAX_COUNTED_RETURNS);
                float evidence = FIRST_PLAY + returns * RETURN_VISIT;
                for (int i = 1; i <= returns; i++)
                {
                    if (days[i - 1] < BAD_SCORE) evidence += RETURN_AFTER_BAD_SCORE;
                }

                double age = Math.Max(0, (history.Now - songPlays.Max(p => p.Date)).TotalDays);
                played[songPlays.Key] = evidence * (float) (0.5 + 0.5 * Math.Pow(0.5, age / RECENCY_HALF_LIFE_DAYS));
            }

            float baseline = played.Count > 0 ? BASELINE_SHARE * played.Values.Average() : 0f;
            foreach (var (key, evidence) in played.Where(p => !swipes.ContainsKey(p.Key)))
            {
                model.Add(key, evidence - baseline);
            }

            foreach (var (key, swipe) in swipes)
            {
                model.Add(key, swipe.Liked ? SWIPE_LIKE : SWIPE_PASS);
            }

            foreach (var quit in history.CurrentQuits(library))
            {
                model.Add(songKey(quit.Key), quit.Progress < 0.5f ? EARLY_QUIT : LATE_QUIT);
            }

            foreach (string key in history.Favorites.Select(songKey).Distinct())
            {
                model.Add(key, FAVORITE);
            }

            foreach (string key in model._evidence.Keys.Where(library.ContainsKey))
            {
                foreach (var feature in library[key].Features)
                {
                    model._observations.TryGetValue(feature, out int count);
                    model._observations[feature] = count + 1;
                }
            }

            model._preferences = PreferenceModel.Train(library, model._evidence, heldOut);
            return model;
        }

        private void Add(string key, float amount)
        {
            _evidence.TryGetValue(key, out float current);
            _evidence[key] = current + amount;
        }

        /// <summary>
        /// How much the profile is expected to enjoy a song, as a log-odds score.
        /// </summary>
        public float Score(SongFacts song) => _preferences.Score(song);

        /// <summary>
        /// The learned weight of one feature, such as how much this profile likes an artist.
        /// </summary>
        public float Weight(SongFeature feature) => _preferences.Weight(feature);

        public int Observations(SongFeature feature) => _observations.TryGetValue(feature, out int n) ? n : 0;

        /// <summary>
        /// How little the model knows about songs like this one, from 0 to 1: high for untried genres,
        /// artists and decades. <paramref name="extra"/> adds observations not yet in the model.
        /// </summary>
        public float Uncertainty(SongFacts song, IReadOnlyDictionary<SongFeature, int>? extra = null)
        {
            if (song.Features.Length == 0) return 1f;

            float total = 0f;
            foreach (var feature in song.Features)
            {
                int count = Observations(feature);
                if (extra != null && extra.TryGetValue(feature, out int more)) count += more;
                total += 1f / (float) Math.Sqrt(1 + count);
            }

            return total / song.Features.Length;
        }
    }
}
