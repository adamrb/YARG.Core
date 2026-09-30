using System;
using System.Collections.Generic;
using System.Linq;

namespace YARG.Core.Song.Recommendations
{
    /// <summary>
    /// What the recommender knows about one song in the library.
    /// </summary>
    public sealed class SongFacts
    {
        /// <summary>
        /// The song's checksum as a string; every other input refers to songs by this key.
        /// </summary>
        public string Key = string.Empty;

        public SongFeature[] Features = Array.Empty<SongFeature>();

        /// <summary>
        /// Chart difficulty on the profile's current instrument and difficulty (see
        /// <see cref="SkillModel.ChartDifficulty"/>), or null if the song cannot be played that way.
        /// </summary>
        public float? ChartDifficulty;

        private string _identity = string.Empty;

        /// <summary>
        /// Shared by every chart of the same song (<see cref="SongNormalizer.Identity"/>), so only one
        /// version is ever offered. Defaults to <see cref="Key"/>.
        /// </summary>
        public string Identity
        {
            get => _identity.Length > 0 ? _identity : Key;
            set => _identity = value;
        }

        /// <summary>
        /// False for the other versions of a song with several charts.
        /// </summary>
        public bool Canonical = true;

        /// <summary>
        /// Where the song's artist sits on the <see cref="ArtistMap"/>, or null.
        /// </summary>
        public float[]? ArtistPosition;

        public string? Artist => First(FeatureType.Artist);
        public string? Genre => First(FeatureType.Genre);

        private string? First(FeatureType type)
        {
            foreach (var feature in Features)
            {
                if (feature.Type == type) return feature.Value;
            }

            return null;
        }
    }

    public sealed class PlayFact
    {
        public string Key = string.Empty;
        public DateTime Date;

        /// <summary>
        /// Notes hit divided by total notes, from 0 to 1.
        /// </summary>
        public float Accuracy;

        public bool OnCurrentInstrument;
        public bool OnCurrentDifficulty;
        public float SongSpeed = 1f;

        /// <summary>
        /// Difficulty of the part that was played, adjusted for song speed, or null if unknown.
        /// </summary>
        public float? ChartDifficulty;
    }

    /// <summary>
    /// A like or pass given outside gameplay, for example in Song Swipe.
    /// </summary>
    public sealed class FeedbackFact
    {
        public string Key = string.Empty;
        public bool Liked;
        public DateTime Date;
    }

    /// <summary>
    /// A song left before the end.
    /// </summary>
    public sealed class QuitFact
    {
        public string Key = string.Empty;

        /// <summary>
        /// How far into the song the player got, from 0 to 1.
        /// </summary>
        public float Progress;

        public DateTime Date;
    }

    /// <summary>
    /// Everything one profile has done that the recommender learns from.
    /// </summary>
    public sealed class ProfileHistory
    {
        public IReadOnlyList<PlayFact> Plays = Array.Empty<PlayFact>();
        public IReadOnlyList<FeedbackFact> Feedback = Array.Empty<FeedbackFact>();
        public IReadOnlyList<QuitFact> Quits = Array.Empty<QuitFact>();
        public IReadOnlyCollection<string> Favorites = Array.Empty<string>();

        /// <summary>
        /// The profile's current difficulty, as <c>(int) Difficulty</c>.
        /// </summary>
        public int CurrentDifficulty = 4;

        public DateTime Now = DateTime.Now;

        /// <summary>
        /// The latest swipe on each song; a later swipe replaces an earlier one.
        /// </summary>
        public IEnumerable<FeedbackFact> LatestFeedback() =>
            Feedback.GroupBy(f => f.Key).Select(g => g.OrderBy(f => f.Date).Last());

        /// <summary>
        /// A copy without the swipes on the given songs, for judging those swipes by everything else.
        /// </summary>
        public ProfileHistory WithoutFeedbackOn(ISet<string> keys) => new()
        {
            Plays = Plays,
            Feedback = Feedback.Where(f => !keys.Contains(f.Key)).ToList(),
            Quits = Quits,
            Favorites = Favorites,
            CurrentDifficulty = CurrentDifficulty,
            Now = Now,
        };
    }
}
