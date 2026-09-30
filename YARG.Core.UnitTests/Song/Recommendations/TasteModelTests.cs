using NUnit.Framework;
using YARG.Core.Song.Recommendations;
using static YARG.Core.UnitTests.Song.Recommendations.RecommendationTestData;

namespace YARG.Core.UnitTests.Song.Recommendations;

public class TasteModelTests
{
    [Test]
    public void Evidence_LowScoreIsNeverADislike()
    {
        var song = MakeSong("bad", "A", "Metal", tier: 5);
        var taste = TasteModel.Build(Library(song), History(new[] { Play(song, 1, 0.55f) }));
        Assert.That(taste.Evidence["bad"], Is.GreaterThan(0));
    }

    [Test]
    public void Evidence_ReturningAfterABadScoreCountsMore()
    {
        var hard = MakeSong("hard", "A", "Metal", tier: 5);
        var easy = MakeSong("easy", "B", "Pop", tier: 1);
        var library = Library(hard, easy);
        var afterBad = TasteModel.Build(library, History(new[] { Play(hard, 10, 0.6f), Play(hard, 3, 0.7f) }));
        var afterGood = TasteModel.Build(library, History(new[] { Play(easy, 10, 0.99f), Play(easy, 3, 0.99f) }));
        Assert.That(afterBad.Evidence["hard"], Is.GreaterThan(afterGood.Evidence["easy"]));
    }

    [Test]
    public void Evidence_AReturnVisitBeatsReplaysInOneSitting()
    {
        var song = MakeSong("s", "A", "Pop");
        var library = Library(song);
        var sameDay = TasteModel.Build(library, History(new[] { Play(song, 3, 0.9f), Play(song, 3, 0.9f), Play(song, 3, 0.9f) }));
        var twoDays = TasteModel.Build(library, History(new[] { Play(song, 5, 0.9f), Play(song, 3, 0.9f) }));
        Assert.That(twoDays.Evidence["s"], Is.GreaterThan(sameDay.Evidence["s"]));
    }

    [Test]
    public void Evidence_SwipesCountOnlyUntilASongIsPlayed()
    {
        var song = MakeSong("s", "A", "Pop");
        var library = Library(song);
        var passedThenPlayed = TasteModel.Build(library, History(new[] { Play(song, 1, 0.9f) }, new[] { Swipe(song, false, -9000) }));
        var liked = TasteModel.Build(library, History(feedback: new[] { Swipe(song, true) }));
        var passed = TasteModel.Build(library, History(feedback: new[] { Swipe(song, false) }));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(passedThenPlayed.Evidence["s"], Is.GreaterThan(0));
            Assert.That(liked.Evidence["s"], Is.GreaterThan(0));
            Assert.That(passed.Evidence["s"], Is.LessThan(0));
        }
    }

    [Test]
    public void Evidence_EarlyQuitCountsAgainst()
    {
        var song = MakeSong("s", "A", "Pop");
        var taste = TasteModel.Build(Library(song), History(quits: new[] { new QuitFact { Key = "s", Progress = 0.2f, Date = Now } }));
        Assert.That(taste.Evidence["s"], Is.LessThan(0));
    }

    [Test]
    public void Evidence_SwipesKeepTheirSignWhateverElseTheHistoryHolds()
    {
        var quitter = MakeSong("quit", "A", "Pop");
        var passed = MakeSong("passed", "B", "Pop");
        var loved = MakeSong("loved", "C", "Rock");
        var liked = MakeSong("liked", "D", "Rock");
        var library = Library(quitter, passed, loved, liked);
        var quits = Enumerable.Range(0, 10).Select(_ => new QuitFact { Key = "quit", Progress = 0.1f, Date = Now });
        var manyQuits = TasteModel.Build(library, History(quits: quits, feedback: new[] { Swipe(passed, false) }));
        var manyReturns = TasteModel.Build(library, History(
            Enumerable.Range(0, 5).Select(day => Play(loved, day + 1, 0.9f)), new[] { Swipe(liked, true) }));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(manyQuits.Evidence["passed"], Is.LessThan(0));
            Assert.That(manyReturns.Evidence["liked"], Is.GreaterThan(0));
        }
    }

    [Test]
    public void Evidence_EveryChartOfASongCountsAsOneSong()
    {
        var harmonix = MakeSong("hmx", "A", "Rock", title: "Song");
        var neversoft = MakeSong("ns", "A", "Rock", title: "Song");
        neversoft.Canonical = false;
        var library = Library(harmonix, neversoft);
        var passThenLike = TasteModel.Build(library, History(feedback: new[] { Swipe(harmonix, false, -10), Swipe(neversoft, true) }));
        var passThenPlay = TasteModel.Build(library, History(new[] { Play(neversoft, 1, 0.9f) }, new[] { Swipe(harmonix, false, -9000) }));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(passThenLike.Evidence.Keys, Is.EqualTo(new[] { "hmx" }));
            Assert.That(passThenLike.Evidence["hmx"], Is.GreaterThan(0), "the later like on the other chart wins");
            Assert.That(passThenPlay.Evidence["hmx"], Is.GreaterThan(0), "playing any chart ends the pass");
        }
    }

    [Test]
    public void Score_GeneralizesThroughSharedFeatures()
    {
        var liked = MakeSong("liked", "B", "Pop");
        var sameArtist = MakeSong("same", "B", "Pop");
        var other = MakeSong("other", "Z", "Jazz", year: 1950);
        var library = BigLibrary(300, seed: 1);
        foreach (var s in new[] { liked, sameArtist, other }) library[s.Key] = s;
        var taste = TasteModel.Build(library, History(new[] { Play(liked, 9, 0.9f), Play(liked, 2, 0.9f) }));
        Assert.That(taste.Score(sameArtist), Is.GreaterThan(taste.Score(other)));
    }

    [Test]
    public void Score_TriedOnceGenreRanksBelowLovedAndUntried()
    {
        var loved1 = MakeSong("l1", "L1", "Metal");
        var loved2 = MakeSong("l2", "L2", "Metal");
        var once = MakeSong("o1", "O1", "Jazz");
        var lovedNew = MakeSong("l3", "L3", "Metal");
        var onceNew = MakeSong("o2", "O2", "Jazz");
        var untried = MakeSong("u1", "U1", "Country");
        var library = Library(loved1, loved2, once, lovedNew, onceNew, untried);
        var taste = TasteModel.Build(library, History(new[]
        {
            Play(loved1, 20, 0.9f), Play(loved1, 10, 0.8f), Play(loved1, 3, 0.9f),
            Play(loved2, 15, 0.7f), Play(loved2, 4, 0.8f),
            Play(once, 12, 0.95f),
        }));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(taste.Score(lovedNew), Is.GreaterThan(taste.Score(untried)));
            Assert.That(taste.Score(onceNew), Is.LessThan(taste.Score(untried)));
        }
    }

    [Test]
    public void Score_ArtistMapCarriesTasteToNeighboringArtists()
    {
        float[] near = { 1f, 0f };
        float[] far = { 0f, 1f };
        var library = BigLibrary(300, seed: 2);
        var liked = MakeSong("liked", "A", "Rock");
        var neighbor = MakeSong("neighbor", "B", "Rock");
        var stranger = MakeSong("stranger", "C", "Rock");
        liked.ArtistPosition = near;
        neighbor.ArtistPosition = near;
        stranger.ArtistPosition = far;
        foreach (var s in new[] { liked, neighbor, stranger }) library[s.Key] = s;
        var taste = TasteModel.Build(library, History(new[] { Play(liked, 9, 0.9f), Play(liked, 2, 0.9f) }));
        Assert.That(taste.Score(neighbor), Is.GreaterThan(taste.Score(stranger)));
    }

    [Test]
    public void Uncertainty_DropsAsFeaturesAreObserved()
    {
        var seen = MakeSong("seen", "A", "Rock");
        var similar = MakeSong("similar", "A", "Rock");
        var unexplored = MakeSong("new", "Z", "Jazz", year: 1950);
        var taste = TasteModel.Build(Library(seen, similar, unexplored), History(new[] { Play(seen, 1, 0.9f) }));
        Assert.That(taste.Uncertainty(similar), Is.LessThan(taste.Uncertainty(unexplored)));
    }
}
