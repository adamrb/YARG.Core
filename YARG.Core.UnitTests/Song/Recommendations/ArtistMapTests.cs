using NUnit.Framework;
using YARG.Core.Song.Recommendations;

namespace YARG.Core.UnitTests.Song.Recommendations;

public class ArtistMapTests
{
    [Test]
    public void Parse_ReadsPositionsSkipsCommentsAndNormalizesLookups()
    {
        var map = ArtistMap.Parse(new[]
        {
            "# comment",
            "presidents of the united states of america\t0.6\t0.8",
            "broken line",
            "bjork\t1\t0",
        });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(map.Count, Is.EqualTo(2));
            Assert.That(map.Find("The Presidents of the United States of America (Harmonix)"), Is.EqualTo(new[] { 0.6f, 0.8f }));
            Assert.That(map.Find("Nobody"), Is.Null);
            Assert.That(ArtistMap.Similarity(map.Find("bjork"), new[] { 1f, 0f }), Is.EqualTo(1f));
        }
    }

    [Test]
    public void Parse_SkipsNonFiniteValuesAndMismatchedLengthsAndScalesToUnitLength()
    {
        var map = ArtistMap.Parse(new[]
        {
            "a\t3\t4",
            "b\tNaN\t1",
            "c\tInfinity\t1",
            "d\t1\t0\t0",
            "e\t0\t0",
        });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(map.Count, Is.EqualTo(1));
            Assert.That(map.Find("a"), Is.EqualTo(new[] { 0.6f, 0.8f }).Within(1e-6f));
        }
    }
}
