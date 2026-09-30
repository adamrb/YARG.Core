using NUnit.Framework;
using YARG.Core.Song.Recommendations;
using static YARG.Core.UnitTests.Song.Recommendations.RecommendationTestData;

namespace YARG.Core.UnitTests.Song.Recommendations;

public class VarietyRulesTests
{
    [Test]
    public void TryAdd_EnforcesSongArtistAndGenreCaps()
    {
        var rules = new VarietyRules(genrePerGroup: 2, genreTotal: 3);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rules.TryAdd(MakeSong("a1", "A", "Rock", title: "One"), 0), Is.True);
            Assert.That(rules.TryAdd(MakeSong("a1b", "A", "Rock", title: "One"), 1), Is.False, "same song, other chart");
            Assert.That(rules.TryAdd(MakeSong("a2", "A", "Pop", title: "Two"), 1), Is.False, "artist already picked");
            Assert.That(rules.TryAdd(MakeSong("b1", "B", "Rock"), 0), Is.True);
            Assert.That(rules.TryAdd(MakeSong("c1", "C", "Rock"), 0), Is.False, "genre already twice in group");
            Assert.That(rules.TryAdd(MakeSong("d1", "D", "Rock"), 1), Is.True);
            Assert.That(rules.TryAdd(MakeSong("e1", "E", "Rock"), 2), Is.False, "genre total reached");
        }
    }
}
