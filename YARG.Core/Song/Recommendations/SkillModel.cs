using System;
using System.Collections.Generic;
using System.Linq;

namespace YARG.Core.Song.Recommendations
{
    /// <summary>
    /// Estimates how accurately a profile will play a chart on its current instrument.
    /// </summary>
    /// <remarks>
    /// Each chart gets a difficulty number: the song's intensity tier for the instrument (0 to 6), shifted
    /// by the difficulty level (Expert 0, Hard -1.5 and so on) and by song speed. The player gets a skill
    /// number on the same scale, fitted to their results. A chart at the player's skill predicts about 77%
    /// accuracy, two points easier about 95%, two points harder about 60%.
    /// </remarks>
    public sealed class SkillModel
    {
        public const float UNKNOWN_INTENSITY = 3f;

        private const float SLOPE = 1.1f;
        private const float MAX_LOSS = 0.45f;
        private const double RECENCY_HALF_LIFE_DAYS = 60;
        private const float PRIOR_STRENGTH = 0.01f;
        private const float OWN_RESULT_WEIGHT = 0.6f;

        private readonly Dictionary<string, float> _lastAccuracy = new();
        private readonly Dictionary<string, float> _bestAccuracy = new();

        public float Skill { get; private set; }

        public static float ChartDifficulty(int intensity, Difficulty difficulty, float songSpeed = 1f)
        {
            float tier = intensity < 0 ? UNKNOWN_INTENSITY : intensity;
            float speed = songSpeed > 0f ? songSpeed : 1f;
            return tier + DifficultyOffset(difficulty) + 2f * (float) Math.Log(speed, 2);
        }

        public static float Predict(float skill, float chartDifficulty)
        {
            return 1f - MAX_LOSS / (1f + (float) Math.Exp(-SLOPE * (chartDifficulty - skill)));
        }

        public static SkillModel Fit(ProfileHistory history)
        {
            var model = new SkillModel();
            var plays = history.Plays
                .Where(p => p.OnCurrentInstrument && p.ChartDifficulty.HasValue)
                .OrderBy(p => p.Date)
                .ToList();

            // A result is reused as the song's own prediction only if it was played at normal speed;
            // other speeds still inform the fit through their speed-adjusted difficulty
            foreach (var play in plays.Where(p => p.OnCurrentDifficulty && Math.Abs(p.SongSpeed - 1f) < 0.01f))
            {
                model._lastAccuracy[play.Key] = play.Accuracy;
                model._bestAccuracy.TryGetValue(play.Key, out float best);
                model._bestAccuracy[play.Key] = Math.Max(best, play.Accuracy);
            }

            // Before any plays, assume a player handles a mid-tier chart on their chosen difficulty
            float prior = 4f + DifficultyOffset(history.CurrentDifficulty);
            var weights = plays
                .Select(p => 0.1 + Math.Pow(0.5, Math.Max(0, (history.Now - p.Date).TotalDays) / RECENCY_HALF_LIFE_DAYS))
                .ToArray();

            // One-dimensional least squares, so a fine grid search is simple and exact enough
            float bestSkill = prior;
            double bestError = double.MaxValue;
            for (float skill = -8f; skill <= 12f; skill += 0.05f)
            {
                double error = PRIOR_STRENGTH * (skill - prior) * (skill - prior);
                for (int i = 0; i < plays.Count; i++)
                {
                    double diff = plays[i].Accuracy - Predict(skill, plays[i].ChartDifficulty!.Value);
                    error += weights[i] * diff * diff;
                }

                if (error < bestError)
                {
                    bestError = error;
                    bestSkill = skill;
                }
            }

            model.Skill = bestSkill;
            return model;
        }

        /// <summary>
        /// Predicted accuracy on the current instrument and difficulty, leaning on the player's own last
        /// result for songs they have already played that way. Zero if the song has no such chart.
        /// </summary>
        public float PredictSong(SongFacts song)
        {
            if (!song.ChartDifficulty.HasValue) return 0f;

            float predicted = Predict(Skill, song.ChartDifficulty.Value);
            return _lastAccuracy.TryGetValue(song.Key, out float last)
                ? OWN_RESULT_WEIGHT * last + (1f - OWN_RESULT_WEIGHT) * predicted
                : predicted;
        }

        public float? BestAccuracy(string key) => _bestAccuracy.TryGetValue(key, out float best) ? best : null;

        private static float DifficultyOffset(Difficulty difficulty) => difficulty switch
        {
            Difficulty.Beginner   => -6f,
            Difficulty.Easy       => -4.5f,
            Difficulty.Medium     => -3f,
            Difficulty.Hard       => -1.5f,
            Difficulty.ExpertPlus => 0.5f,
            _                     => 0f,
        };
    }
}
