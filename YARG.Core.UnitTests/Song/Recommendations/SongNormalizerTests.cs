using System.Runtime.CompilerServices;
using NUnit.Framework;
using YARG.Core.Song;
using YARG.Core.Song.Recommendations;

namespace YARG.Core.UnitTests.Song.Recommendations;

public class SongNormalizerTests
{
    [TestCase("Take Me Out (Sep 3, 2005 Prototype)", "take me out")]
    [TestCase("Voodoo Child (Slight Return) (Live)", "voodoo child slight return")]
    [TestCase("Song [Remastered]", "song")]
    [TestCase("(I Just) Died in Your Arms", "i just died in your arms")]
    [TestCase("Hotel California - 2013 Remaster", "hotel california")]
    [TestCase("Numb - Live", "numb")]
    [TestCase("Rock - Paper - Scissors", "rock paper scissors")]
    public void Title_DropsVersionNotesButKeepsNameBrackets(string title, string expected)
    {
        Assert.That(SongNormalizer.Title(title), Is.EqualTo(expected));
    }

    [Test]
    public void Title_KeepsDistinctPartsDistinct()
    {
        Assert.That(SongNormalizer.Title("Shine On You Crazy Diamond (Parts I-V)"),
            Is.Not.EqualTo(SongNormalizer.Title("Shine On You Crazy Diamond (Parts VI-IX)")));
    }

    [TestCase("Franz Ferdinand (WaveGroup)", "franz ferdinand")]
    [TestCase("The Presidents of the United States of America", "presidents of the united states of america")]
    [TestCase("Blink-182", "blink 182")]
    [TestCase("Bob Marley & The Wailers", "bob marley the wailers")]
    [TestCase("Bob Marley and the Wailers", "bob marley the wailers")]
    [TestCase("The The", "the")]
    public void Artist_DropsChartersAndLeadingThe(string artist, string expected)
    {
        Assert.That(SongNormalizer.Artist(artist), Is.EqualTo(expected));
    }

    private static IEnumerable<TestCaseData> SongKeyCases()
    {
        return File.ReadLines(Path.Combine(Path.GetDirectoryName(ThisFile())!, "song-keys.tsv"))
            .Where(line => line.Length > 0 && !line.StartsWith("#"))
            .Select(line => line.Split('\t'))
            .Select(parts => new TestCaseData(parts[0], parts[1], parts.Length > 2 ? parts[2] : string.Empty));
    }

    private static string ThisFile([CallerFilePath] string path = "") => path;

    // The song map builder checks itself against the same file, so the game and the map key songs alike
    [TestCaseSource(nameof(SongKeyCases))]
    public void Identity_OfSearchStringsMatchesTheSharedSongKeys(string artist, string title, string expected)
    {
        Assert.That(SongNormalizer.Identity(new SortString(artist).SearchStr, new SortString(title).SearchStr),
            Is.EqualTo(expected));
    }

    [Test]
    public void IsAlternateVersion_SpotsDemosButNotSongNames()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(SongNormalizer.IsAlternateVersion("Trogdor (Late 2006 Retail Demo)"), Is.True);
            Assert.That(SongNormalizer.IsAlternateVersion("Live and Let Die"), Is.False);
            Assert.That(SongNormalizer.IsAlternateVersion("Live and Let Die (Live)"), Is.True);
            Assert.That(SongNormalizer.IsAlternateVersion("Numb - Live - 2011 Remaster"), Is.True);
        }
    }

    [Test]
    public void Features_NormalizeGenreSpellingsAndSplitWords()
    {
        var a = SongNormalizer.Features("A", "Pop/Rock", null, null, null, 1995, 200);
        var b = SongNormalizer.Features("B", "pop-rock", null, null, null, 1995, 200);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(a.Single(f => f.Type == FeatureType.Genre), Is.EqualTo(b.Single(f => f.Type == FeatureType.Genre)));
            Assert.That(a.Where(f => f.Type == FeatureType.GenreWord).Select(f => f.Value), Is.EquivalentTo(new[] { "pop", "rock" }));
            Assert.That(a.Single(f => f.Type == FeatureType.Decade).Value, Is.EqualTo("1990"));
        }
    }

    [Test]
    public void AssignCanonical_PrefersThePlayableNonDemoChart()
    {
        var demo = new SongFacts { Key = "demo", Identity = "x|song", ChartDifficulty = 1 };
        var main = new SongFacts { Key = "main", Identity = "x|song", ChartDifficulty = 1 };
        var unplayable = new SongFacts { Key = "none", Identity = "x|song" };
        SongNormalizer.AssignCanonical(new[] { (demo, "Song (Demo)"), (main, "Song (Remastered)"), (unplayable, "Song") });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(main.Canonical, Is.True);
            Assert.That(demo.Canonical, Is.False);
            Assert.That(unplayable.Canonical, Is.False);
        }
    }
}
