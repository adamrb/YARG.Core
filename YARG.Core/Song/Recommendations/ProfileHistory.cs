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

        /// <summary>
        /// The <see cref="Identity"/> of the song with this key, or the key itself if it is not in the library.
        /// </summary>
        public static string IdentityOf(IReadOnlyDictionary<string, SongFacts> library, string key) =>
            library.TryGetValue(key, out var song) ? song.Identity : key;

        /// <summary>
        /// A lookup from any chart's key to the key of its song's canonical chart (or the key itself if the
        /// song is not in the library), so everything said about a song lands on one chart.
        /// </summary>
        public static Func<string, string> CanonicalKeyLookup(IReadOnlyDictionary<string, SongFacts> library)
        {
            var canonical = library.Values.Where(s => s.Canonical).GroupBy(s => s.Identity)
                .ToDictionary(g => g.Key, g => g.First().Key);
            return key => canonical.TryGetValue(IdentityOf(library, key), out string song) ? song : key;
        }

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

        public Difficulty CurrentDifficulty = Difficulty.Expert;

        public DateTime Now = DateTime.Now;

        /// <summary>
        /// The latest swipe on each song, counting every chart of a song as one song: a later swipe on any
        /// chart replaces an earlier one.
        /// </summary>
        public IEnumerable<FeedbackFact> LatestFeedback(IReadOnlyDictionary<string, SongFacts> library) =>
            Feedback.GroupBy(f => SongFacts.IdentityOf(library, f.Key)).Select(g => g.OrderBy(f => f.Date).Last());

        /// <summary>
        /// The latest swipe on each song, if it came after the song was last played: playing a song
        /// replaces an earlier swipe, and a later swipe replaces what the plays said.
        /// </summary>
        public IEnumerable<FeedbackFact> CurrentFeedback(IReadOnlyDictionary<string, SongFacts> library) =>
            NewerThanLastPlay(library, LatestFeedback(library), f => f.Key, f => f.Date);

        /// <summary>
        /// The latest quit on each song, if it came after the song was last finished.
        /// </summary>
        public IEnumerable<QuitFact> CurrentQuits(IReadOnlyDictionary<string, SongFacts> library) =>
            NewerThanLastPlay(library,
                Quits.GroupBy(q => SongFacts.IdentityOf(library, q.Key)).Select(g => g.OrderBy(q => q.Date).Last()),
                q => q.Key, q => q.Date);

        private IEnumerable<T> NewerThanLastPlay<T>(IReadOnlyDictionary<string, SongFacts> library, IEnumerable<T> facts,
            Func<T, string> keyOf, Func<T, DateTime> dateOf)
        {
            var lastPlayed = new Dictionary<string, DateTime>();
            foreach (var play in Plays)
            {
                string song = SongFacts.IdentityOf(library, play.Key);
                if (!lastPlayed.TryGetValue(song, out var date) || play.Date > date)
                {
                    lastPlayed[song] = play.Date;
                }
            }

            return facts.Where(fact =>
                !lastPlayed.TryGetValue(SongFacts.IdentityOf(library, keyOf(fact)), out var played) || dateOf(fact) > played);
        }

        /// <summary>
        /// A copy without the swipes on the given songs (on any of their charts), for judging those swipes
        /// by everything else.
        /// </summary>
        public ProfileHistory WithoutFeedbackOn(IReadOnlyDictionary<string, SongFacts> library, ISet<string> keys)
        {
            var songs = new HashSet<string>(keys.Select(key => SongFacts.IdentityOf(library, key)));
            return new ProfileHistory
            {
                Plays = Plays,
                Feedback = Feedback.Where(f => !songs.Contains(SongFacts.IdentityOf(library, f.Key))).ToList(),
                Quits = Quits,
                Favorites = Favorites,
                CurrentDifficulty = CurrentDifficulty,
                Now = Now,
            };
        }
    }
}
