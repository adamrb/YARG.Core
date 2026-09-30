using NUnit.Framework;
using YARG.Core.Song.Recommendations;
using static YARG.Core.UnitTests.Song.Recommendations.RecommendationTestData;

namespace YARG.Core.UnitTests.Song.Recommendations;

public class SkillModelTests
{
    [Test]
    public void Fit_LandsBetweenTheEasiestAndHardestChartsPlayed()
    {
        var easy = MakeSong("easy", "A", "Rock", tier: 1);
        var mid = MakeSong("mid", "A", "Rock", tier: 3);
        var hard = MakeSong("hard", "A", "Rock", tier: 6);
        var skill = SkillModel.Fit(History(new[]
        {
            Play(easy, 3, 0.99f), Play(easy, 2, 1f), Play(mid, 3, 0.9f), Play(mid, 1, 0.92f), Play(hard, 1, 0.68f),
        }));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(skill.Skill, Is.InRange(2f, 6f));
            Assert.That(skill.PredictSong(MakeSong("x", "B", "Pop", tier: 2)), Is.GreaterThan(skill.PredictSong(MakeSong("y", "B", "Pop", tier: 6))));
        }
    }

    [Test]
    public void Fit_WithoutPlaysUsesTheDifficultyPrior()
    {
        Assert.That(SkillModel.Fit(History()).Skill, Is.EqualTo(4f).Within(0.1f));
    }

    [Test]
    public void ChartDifficulty_SlowerPlaysAreEasier()
    {
        Assert.That(SkillModel.ChartDifficulty(4, 4, 0.5f), Is.LessThan(SkillModel.ChartDifficulty(4, 4)));
    }

    [Test]
    public void Predict_HasTheDocumentedShape()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(SkillModel.Predict(0, -2), Is.GreaterThan(0.94f));
            Assert.That(SkillModel.Predict(0, 2), Is.LessThan(0.62f));
        }
    }

    [Test]
    public void PredictSong_IgnoresSpedUpResultsForTheSongsOwnPrediction()
    {
        var song = MakeSong("s", "A", "Rock", tier: 2);
        var easy = MakeSong("easy", "B", "Rock", tier: 1);
        var mid = MakeSong("mid", "B", "Rock", tier: 3);
        var skill = SkillModel.Fit(History(new[]
        {
            Play(easy, 3, 0.99f), Play(mid, 2, 0.92f), Play(song, 1, 0.55f, speed: 1.5f),
        }));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(skill.BestAccuracy("s"), Is.Null);
            Assert.That(skill.PredictSong(song), Is.GreaterThan(0.85f));
        }
    }
}
