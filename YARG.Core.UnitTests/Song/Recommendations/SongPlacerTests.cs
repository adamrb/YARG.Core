using NUnit.Framework;
using YARG.Core.Song.Recommendations;
using static YARG.Core.UnitTests.Song.Recommendations.RecommendationTestData;

namespace YARG.Core.UnitTests.Song.Recommendations;

public class SongPlacerTests
{
    private static readonly float[] MetalSide = { 1f, 0f };
    private static readonly float[] PopSide = { 0f, 1f };

    // Known songs: metal sits on one side of the map and pop on the other
    private static List<SongFacts> KnownSongs(int count)
    {
        return Enumerable.Range(0, count).Select(i =>
        {
            bool metal = i % 2 == 0;
            var song = MakeSong($"k{i}", $"artist{i}", metal ? "Metal" : "Pop");
            song.OnMap = true;
            song.Position = metal ? MetalSide : PopSide;
            return song;
        }).ToList();
    }

    private static SongFacts Unknown(string key, string genre) => MakeSong(key, "New Band " + key, genre);

    [Test]
    public void PlaceUnknownSongs_LearnsWhereTheLibrarysGenresSit()
    {
        var metal = Unknown("u1", "Metal");
        var pop = Unknown("u2", "Pop");
        int placed = SongPlacer.PlaceUnknownSongs(KnownSongs(100).Append(metal).Append(pop));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(placed, Is.EqualTo(2));
            Assert.That(SongMap.Similarity(metal.Position, MetalSide), Is.GreaterThan(0.9f));
            Assert.That(SongMap.Similarity(pop.Position, PopSide), Is.GreaterThan(0.9f));
        }
    }

    [Test]
    public void PlaceUnknownSongs_KeepsTheArtistsPositionWhenTheMapKnowsTheArtist()
    {
        var song = Unknown("u", "Pop");
        song.Position = MetalSide;
        SongPlacer.PlaceUnknownSongs(KnownSongs(100).Append(song));
        Assert.That(song.Position, Is.EqualTo(MetalSide));
    }

    [Test]
    public void PlaceUnknownSongs_LeavesSmallLibrariesAlone()
    {
        var song = Unknown("u", "Metal");
        Assert.That(SongPlacer.PlaceUnknownSongs(KnownSongs(10).Append(song)), Is.Zero);
        Assert.That(song.Position, Is.Null);
    }
}
