using NUnit.Framework;
using YARG.Core.Song.Recommendations;
using static YARG.Core.UnitTests.Song.Recommendations.RecommendationTestData;

namespace YARG.Core.UnitTests.Song.Recommendations;

public class RecommenderTests
{
    private static (Dictionary<string, SongFacts> Library, List<PlayFact> Plays) MetalFan(int seed)
    {
        var library = BigLibrary(3000, seed);
        var metal = library.Values.Where(s => s.Genre == "metal" && s.ChartDifficulty.HasValue).Take(15).ToList();
        var plays = new List<PlayFact>();
        for (int i = 0; i < metal.Count; i++)
        {
            float accuracy = Math.Clamp(1.05f - 0.07f * metal[i].ChartDifficulty!.Value, 0.5f, 1f);
            plays.Add(Play(metal[i], 20 - i, accuracy));
            if (i % 3 == 0) plays.Add(Play(metal[i], 10 - i / 3, accuracy + 0.02f));
        }

        return (library, plays);
    }

    [Test]
    public void Recommend_WorksWithNoHistory()
    {
        Assert.That(Recommend(BigLibrary(3000, 1), History(), new Random(1)), Is.Not.Empty);
    }

    [Test]
    public void Recommend_RowsRespectTheirDifficultyBands()
    {
        var (library, plays) = MetalFan(1);
        var result = Recommend(library, History(plays), new Random(1));
        List<float> Row(RecommendationKind kind) =>
            result.Where(s => s.Kind == kind).Select(s => s.PredictedAccuracy).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Row(RecommendationKind.AtYourLevel), Is.Not.Empty.And.All.GreaterThanOrEqualTo(Recommender.AT_LEVEL));
            Assert.That(Row(RecommendationKind.NextStepUp), Is.Not.Empty
                .And.All.GreaterThanOrEqualTo(Recommender.STRETCH).And.All.LessThan(Recommender.AT_LEVEL));
            Assert.That(Row(RecommendationKind.Challenge), Is.Not.Empty
                .And.All.GreaterThanOrEqualTo(Recommender.CHALLENGE).And.All.LessThan(Recommender.STRETCH));
        }
    }

    [Test]
    public void Recommend_ForYouLeansTowardTheLikedGenreWithinTheCap()
    {
        var (library, plays) = MetalFan(2);
        var result = Recommend(library, History(plays), new Random(2));
        var forYou = result.Where(s => s.Kind == RecommendationKind.ForYou).ToList();
        Assert.That(forYou.Count(s => s.Song.Genre == "metal"), Is.EqualTo(3));
    }

    [Test]
    public void Recommend_IsVariedAndSkipsRecentPlays()
    {
        var (library, plays) = MetalFan(3);
        var recent = plays.Take(5).Select(p => library[p.Key]).ToList();

        // Another chart of a recent song is skipped too
        var otherChart = MakeSong("otherchart", "unused", "Metal", title: "unused");
        otherChart.Identity = recent[0].Identity;
        otherChart.Canonical = false;
        recent[0].Canonical = true;
        library[otherChart.Key] = otherChart;

        // The first song's recent play is on its other chart
        var history = History(plays.Concat(recent.Skip(1).Select(s => Play(s, 0, 0.95f))).Append(Play(otherChart, 0, 0.95f)));
        var songs = Enumerable.Range(0, 20).SelectMany(seed => Recommend(library, history, new Random(seed))).ToList();
        var single = Recommend(library, history, new Random(3));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(single.Select(s => s.Song.Identity), Is.Unique);
            Assert.That(single.Select(s => s.Song.Artist), Is.Unique);
            Assert.That(songs.Select(s => s.Song.Identity).Intersect(recent.Select(s => s.Identity)), Is.Empty);
            Assert.That(songs.Select(s => s.Song.Key).Intersect(plays.Skip(5).Select(p => p.Key)), Is.Not.Empty,
                "songs played a while ago still come back");
        }
    }

    [Test]
    public void Recommend_OffersOnlyOneVersionOfASong()
    {
        var library = BigLibrary(300, 4);
        for (int v = 0; v < 3; v++)
        {
            var version = MakeSong($"tmo{v}", "Franz", "Rock", title: "Take Me Out");
            version.Canonical = v == 0;
            library[version.Key] = version;
        }

        var plays = new[] { Play(library["tmo1"], 30, 0.9f), Play(library["tmo1"], 10, 0.9f) };
        var versions = Enumerable.Range(0, 20)
            .SelectMany(seed => Recommend(library, History(plays), new Random(seed)))
            .Where(s => s.Song.Identity == SongNormalizer.Identity("Franz", "Take Me Out"))
            .Select(s => s.Song.Key)
            .Distinct()
            .ToList();
        Assert.That(versions, Is.EqualTo(new[] { "tmo0" }), "only the canonical chart is ever offered");
    }

    [Test]
    public void Recommend_PassedSongsReturnOnlyAfterBeingPlayed()
    {
        var (library, plays) = MetalFan(5);
        var target = library[plays[0].Key];
        var pass = new[] { Swipe(target, false, -60000) };
        var others = plays.Where(p => p.Key != target.Key).ToList();
        var replayed = others.Concat(Enumerable.Range(0, 4).Select(i => Play(target, 5 + 3 * i, 0.97f))).ToList();

        bool hiddenWhileUnplayed = Enumerable.Range(0, 20).All(seed =>
            Recommend(library, History(others, pass), new Random(seed)).All(s => s.Song.Key != target.Key));
        bool backAfterPlays = Enumerable.Range(0, 20).Any(seed =>
            Recommend(library, History(replayed, pass), new Random(seed)).Any(s => s.Song.Key == target.Key));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hiddenWhileUnplayed, Is.True);
            Assert.That(backAfterPlays, Is.True);
        }
    }

    [Test]
    public void Recommend_HandlesTinyAndUnplayableLibraries()
    {
        var unplayable = MakeSong("u", "A", "Rock");
        unplayable.ChartDifficulty = null;
        var single = MakeSong("one", "B", "Rock");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Recommend(Library(), History(), new Random(1)), Is.Empty);
            Assert.That(Recommend(Library(unplayable), History(), new Random(1)), Is.Empty);
            Assert.That(Recommend(Library(single), History(), new Random(1)).Select(s => s.Song.Key), Is.EqualTo(new[] { "one" }));
            Assert.That(Recommender.PickSwipeCards(Library(single), TasteModel.Build(Library(single), History()),
                new HashSet<string>(), 5, new Random(1)).Select(s => s.Key), Is.EqualTo(new[] { "one" }));
            Assert.That(Recommender.PickSwipeCards(Library(single), TasteModel.Build(Library(single), History()),
                new HashSet<string> { "one" }, 5, new Random(1)), Is.Empty);
            Assert.That(Recommender.PickSwipeCards(Library(), TasteModel.Build(Library(), History()), new HashSet<string>(), 5, new Random(1)), Is.Empty);
            Assert.That(Recommender.RankLikelyMistakes(Library(single), History()), Is.Empty);
        }
    }

    [Test]
    public void Recommend_APassAfterPlayingHidesTheSongAgain()
    {
        var (library, plays) = MetalFan(11);
        var target = library[plays[0].Key];
        var pass = new[] { Swipe(target, false) };
        bool hidden = Enumerable.Range(0, 20).All(seed =>
            Recommend(library, History(plays, pass), new Random(seed)).All(s => s.Song.Key != target.Key));
        Assert.That(hidden, Is.True);
    }

    [Test]
    public void Recommend_ALaterLikeOnAnotherChartUndoesAPass()
    {
        var library = BigLibrary(300, 8);
        for (int v = 0; v < 2; v++)
        {
            var version = MakeSong($"tmo{v}", "Franz", "Rock", title: "Take Me Out");
            version.Canonical = v == 0;
            library[version.Key] = version;
        }

        var swipes = new[] { Swipe(library["tmo0"], false, -10), Swipe(library["tmo1"], true) };
        bool offered = Enumerable.Range(0, 20).Any(seed =>
            Recommend(library, History(feedback: swipes), new Random(seed)).Any(s => s.Song.Key == "tmo0"));
        Assert.That(offered, Is.True);
    }

    [Test]
    public void RankLikelyMistakes_ListsEachSongOnceWithItsLatestAnswer()
    {
        var library = BigLibrary(300, 10);
        var first = MakeSong("chart1", "Franz", "Rock", title: "Take Me Out");
        var second = MakeSong("chart2", "Franz", "Rock", title: "Take Me Out");
        second.Canonical = false;
        library[first.Key] = first;
        library[second.Key] = second;
        var others = library.Values.Where(s => s.Canonical && s.Key != "chart1").Take(12).Select((s, i) => Swipe(s, i % 2 == 0, i));
        var history = History(feedback: others.Append(Swipe(first, false, -100)).Append(Swipe(second, true, 100)));

        var reviewed = Recommender.RankLikelyMistakes(library, history).Where(r => r.Key is "chart1" or "chart2").ToList();
        Assert.That(reviewed.Select(r => (r.Key, r.Liked)), Is.EqualTo(new[] { ("chart2", true) }));
    }

    [Test]
    public void PickSwipeCards_FillsTheBatchWhenOnlyAFewArtistsRemain()
    {
        var library = Library(Enumerable.Range(0, 4).Select(i => MakeSong($"s{i}", $"artist{i}", "Rock")).ToArray());
        var taste = TasteModel.Build(library, History());
        for (int seed = 0; seed < 50; seed++)
        {
            var cards = Recommender.PickSwipeCards(library, taste, new HashSet<string>(), 4, new Random(seed));
            Assert.That(cards, Has.Count.EqualTo(4), $"seed {seed}");
        }
    }

    [Test]
    public void Recommend_SkipsTheGivenSongs()
    {
        var (library, plays) = MetalFan(6);
        var first = Recommend(library, History(plays), new Random(6));
        var skip = first.Select(s => s.Song.Key).ToHashSet();
        var second = Recommend(library, History(plays), new Random(6), skip);
        Assert.That(second.Select(s => s.Song.Key).Intersect(skip), Is.Empty);
    }

    [Test]
    public void PickSwipeCards_MixesConfidenceBandsAndGenres()
    {
        var (library, plays) = MetalFan(7);
        var taste = TasteModel.Build(library, History(plays));
        var cards = Recommender.PickSwipeCards(library, taste, new HashSet<string>(), 12, new Random(7));
        var ranked = library.Values.Where(s => s.Canonical && s.ChartDifficulty.HasValue && !taste.Evidence.ContainsKey(s.Key))
            .OrderByDescending(taste.Score).Select(s => s.Key).ToList();
        var positions = cards.Select(c => ranked.IndexOf(c.Key) / (double) ranked.Count).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cards, Has.Count.EqualTo(12));
            Assert.That(cards.Select(c => c.Artist), Is.Unique);
            Assert.That(cards.Select(c => c.Genre).Distinct().Count(), Is.GreaterThanOrEqualTo(5));
            Assert.That(positions.Count(p => p < 0.15), Is.GreaterThanOrEqualTo(3), "some likely likes");
            Assert.That(positions.Count(p => p >= 0.6), Is.GreaterThanOrEqualTo(3), "some likely passes");
            Assert.That(cards.Select(c => c.Key).Intersect(plays.Select(p => p.Key)), Is.Empty);
        }
    }

    [Test]
    public void RankLikelyMistakes_FlagsTheOutOfCharacterSwipeFirst()
    {
        var library = BigLibrary(400, 8);
        for (int i = 0; i < 20; i++) library[$"pop{i}"] = MakeSong($"pop{i}", $"pa{i}", "Pop");
        for (int i = 0; i < 20; i++) library[$"met{i}"] = MakeSong($"met{i}", $"ma{i}", "Metal");
        var plays = Enumerable.Range(0, 8).SelectMany(i => new[] { Play(library[$"pop{i}"], 20, 0.9f), Play(library[$"pop{i}"], 5, 0.9f) });
        var feedback = Enumerable.Range(8, 6).Select(i => Swipe(library[$"pop{i}"], true, i))
            .Concat(Enumerable.Range(0, 6).Select(i => Swipe(library[$"met{i}"], false, 20 + i)))
            .Append(Swipe(library["met10"], true, 40));
        var ranked = Recommender.RankLikelyMistakes(library, History(plays, feedback));
        Assert.That(ranked.First().Key, Is.EqualTo("met10"));
    }
}
