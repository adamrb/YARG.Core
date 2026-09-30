using System;
using System.Collections.Generic;
using System.Linq;

namespace YARG.Core.Song.Recommendations
{
    /// <summary>
    /// What a profile enjoys, learned from how it has actually behaved.
    /// </summary>
    /// <remarks>
    /// Every song the profile has touched gets an enjoyment "evidence" number. Coming back to a song on
    /// another day is the strongest signal, especially after a poor score. Finishing a song once is a weak
    /// signal, and a low score never counts against a song (that is the <see cref="SkillModel"/>'s job).
    /// Favorites count strongly; a swipe counts only until the song is actually played; an early quit
    /// counts against. A <see cref="PreferenceModel"/> is then trained on that evidence.
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

        // Evidence above this share of the profile's average counts as a like when training, so a song
        // played once and never revisited ranks below the songs the profile keeps returning to
        private const float BASELINE_SHARE = 0.5f;

        private readonly Dictionary<string, float> _evidence = new();
        private readonly Dictionary<SongFeature, int> _observations = new();
        private PreferenceModel _preferences = PreferenceModel.Empty;

        /// <summary>
        /// Enjoyment evidence per song key, for songs the profile has touched.
        /// </summary>
        public IReadOnlyDictionary<string, float> Evidence => _evidence;

        public PreferenceModel Preferences => _preferences;

        /// <param name="heldOut">Songs to leave out of the weak-negative sample (for cross-validation).</param>
        public static TasteModel Build(IReadOnlyDictionary<string, SongFacts> library, ProfileHistory history,
            ISet<string>? heldOut = null)
        {
            var model = new TasteModel();
            var played = new HashSet<string>();

            foreach (var songPlays in history.Plays.GroupBy(p => p.Key))
            {
                played.Add(songPlays.Key);
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
                evidence *= (float) (0.5 + 0.5 * Math.Pow(0.5, age / RECENCY_HALF_LIFE_DAYS));
                model.Add(songPlays.Key, evidence);
            }

            foreach (var quit in history.Quits)
            {
                model.Add(quit.Key, quit.Progress < 0.5f ? EARLY_QUIT : LATE_QUIT);
            }

            foreach (string key in history.Favorites)
            {
                model.Add(key, FAVORITE);
            }

            foreach (var swipe in history.LatestFeedback().Where(f => !played.Contains(f.Key)))
            {
                model.Add(swipe.Key, swipe.Liked ? SWIPE_LIKE : SWIPE_PASS);
            }

            var known = model._evidence.Where(e => library.ContainsKey(e.Key)).ToList();
            foreach (var (key, _) in known)
            {
                foreach (var feature in library[key].Features)
                {
                    model._observations.TryGetValue(feature, out int count);
                    model._observations[feature] = count + 1;
                }
            }

            if (known.Count > 0)
            {
                float baseline = BASELINE_SHARE * known.Average(e => e.Value);
                model._preferences = PreferenceModel.Train(library, model._evidence, baseline, heldOut);
            }

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
