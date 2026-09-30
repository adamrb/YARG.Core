using YARG.Core.Song.Recommendations;

namespace YARG.Core.UnitTests.Song.Recommendations;

/// <summary>
/// Small builders for recommendation tests: songs, plays and swipes against a fixed "now".
/// </summary>
internal static class RecommendationTestData
{
    public static readonly DateTime Now = new(2026, 9, 29, 20, 0, 0);

    public static SongFacts MakeSong(string key, string artist, string genre, int year = 2000, int tier = 2,
        Difficulty difficulty = Difficulty.Expert, string? title = null)
    {
        return new SongFacts
        {
            Key = key,
            Features = SongNormalizer.Features(artist, genre, null, "charter", "source", year, 200),
            ChartDifficulty = SkillModel.ChartDifficulty(tier, difficulty),
            Identity = SongNormalizer.Identity(artist, title ?? key),
        };
    }

    public static Dictionary<string, SongFacts> Library(params SongFacts[] songs) => songs.ToDictionary(s => s.Key);

    public static List<RecommendedSong> Recommend(Dictionary<string, SongFacts> library, ProfileHistory history,
        Random random, ISet<string>? skip = null) =>
        Recommender.Recommend(library, history, TasteModel.Build(library, history), SkillModel.Fit(history), random, skip);

    public static PlayFact Play(SongFacts song, int daysAgo, float accuracy, float speed = 1f) => new()
    {
        Key = song.Key,
        Date = Now.AddDays(-daysAgo),
        Accuracy = accuracy,
        OnCurrentInstrument = true,
        OnCurrentDifficulty = true,
        SongSpeed = speed,
        // The same shift a real play at this speed gets
        ChartDifficulty = song.ChartDifficulty +
            (SkillModel.ChartDifficulty(0, Difficulty.Expert, speed) - SkillModel.ChartDifficulty(0, Difficulty.Expert)),
    };

    public static FeedbackFact Swipe(SongFacts song, bool liked, int minutes = 0) => new()
    {
        Key = song.Key,
        Liked = liked,
        Date = Now.AddMinutes(minutes),
    };

    public static ProfileHistory History(IEnumerable<PlayFact>? plays = null, IEnumerable<FeedbackFact>? feedback = null,
        IEnumerable<QuitFact>? quits = null, IEnumerable<string>? favorites = null) => new()
    {
        Plays = plays?.ToList() ?? new List<PlayFact>(),
        Feedback = feedback?.ToList() ?? new List<FeedbackFact>(),
        Quits = quits?.ToList() ?? new List<QuitFact>(),
        Favorites = favorites?.ToList() ?? new List<string>(),
        Now = Now,
    };

    /// <summary>
    /// A varied library: many artists across genres, decades and difficulty tiers.
    /// </summary>
    public static Dictionary<string, SongFacts> BigLibrary(int count, int seed)
    {
        string[] genres = { "Rock", "Metal", "Pop", "Punk", "Country", "Jazz", "Electronic", "Indie", "Alternative", "Hip Hop" };
        var random = new Random(seed);
        var library = new Dictionary<string, SongFacts>();
        for (int i = 0; i < count; i++)
        {
            var song = MakeSong($"s{i}", $"artist{random.Next(count / 8)}", genres[random.Next(genres.Length)],
                1960 + 10 * random.Next(7), random.Next(-1, 7));
            if (random.NextDouble() < 0.05) song.ChartDifficulty = null;
            library[song.Key] = song;
        }

        return library;
    }
}
