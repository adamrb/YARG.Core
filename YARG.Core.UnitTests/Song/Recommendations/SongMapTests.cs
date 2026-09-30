using NUnit.Framework;
using YARG.Core.Song.Recommendations;

namespace YARG.Core.UnitTests.Song.Recommendations;

public class SongMapTests
{
    private static readonly SongMap Map = SongMap.Parse(new[]
    {
        "# comment",
        "a\tpresidents of the united states of america\t0.6\t0.8",
        "t\tpresidents of the united states of america|peaches\t4200\t0.00\t1\t0",
        "t\tpresidents of the united states of america|kitty\t300\t0.50\t0\t1",
        "broken line",
    });

    private static SongFacts Placed(string artist, string title, SongMap? map = null)
    {
        var song = new SongFacts { Key = title };
        (map ?? Map).Place(song, artist, title);
        return song;
    }

    [Test]
    public void Place_UsesTheSongsOwnPositionAndPopularity()
    {
        var song = Placed("The Presidents of the United States of America (Harmonix)", "Peaches (Live)");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(song.OnMap, Is.True);
            Assert.That(song.Position, Is.EqualTo(new[] { 1f, 0f }));
            Assert.That(song.ArtistPosition, Is.EqualTo(new[] { 0.6f, 0.8f }));
            Assert.That(song.Features, Is.EquivalentTo(new[]
            {
                new SongFeature(FeatureType.ArtistRank, "hit"),
                new SongFeature(FeatureType.Listeners, "under 5000"),
            }));
            Assert.That(Placed("The Presidents of the United States of America", "Kitty").Features,
                Does.Contain(new SongFeature(FeatureType.ArtistRank, "deep cut")));
        }
    }

    [Test]
    public void Place_FallsBackToTheArtistForSongsTheMapDoesNotKnow()
    {
        using (Assert.EnterMultipleScope())
        {
            var lump = Placed("The Presidents of the United States of America", "Lump");
            Assert.That(lump.OnMap, Is.False);
            Assert.That(lump.Position, Is.EqualTo(new[] { 0.6f, 0.8f }));
            Assert.That(lump.Features, Is.Empty);
            Assert.That(Placed("Nobody", "Anything").Position, Is.Null);
            Assert.That(SongMap.Similarity(Placed("The Presidents of the United States of America", "Peaches").Position,
                new[] { 1f, 0f }), Is.EqualTo(1f));
        }
    }

    [Test]
    public void Parse_ARejectedRowDoesNotFixTheLengthOfTheRest()
    {
        var map = SongMap.Parse(new[] { "t\tbad|song\tnot-a-number\t0.2\t1", "a\tartist\t3\t4" });
        Assert.That(map.ArtistCount, Is.EqualTo(1));
    }

    [Test]
    public void Parse_SkipsNonFiniteValuesAndMismatchedLengthsAndScalesToUnitLength()
    {
        var map = SongMap.Parse(new[]
        {
            "a\ta\t3\t4",
            "a\tb\tNaN\t1",
            "a\tc\tInfinity\t1",
            "a\td\t1\t0\t0",
            "a\te\t0\t0",
            "a\tf\t1e30\t1e30",
            "a\tg\t1e-40\t0",
            "t\tbad|rank\t10\tNaN\t1\t0",
            "t\tbad|listeners\t-5\t0.5\t1\t0",
        });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(map.ArtistCount, Is.EqualTo(3));
            Assert.That(map.TrackCount, Is.Zero);
            Assert.That(Placed("g", "x", map).Position, Is.EqualTo(new[] { 1f, 0f }));
            Assert.That(Placed("a", "x", map).Position, Is.EqualTo(new[] { 0.6f, 0.8f }).Within(1e-6f));
            Assert.That(Placed("f", "x", map).Position, Is.EqualTo(new[] { 0.70710677f, 0.70710677f }).Within(1e-6f));
        }
    }
}
