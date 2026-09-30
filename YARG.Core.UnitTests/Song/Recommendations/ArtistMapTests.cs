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
}
