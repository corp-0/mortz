using Mortz.Client.Announcements;
using Mortz.Core.Match;
using Xunit;

namespace Mortz.Tests.Client.Announcements;

public class VocabTests
{
    [Theory]
    [InlineData(2, "just got a", "DOUBLE KILL", 0)]
    [InlineData(3, "racks up a", "TRIPLE KILL", 1)]
    [InlineData(4, "goes for", "OVERKILL", 2)]
    [InlineData(5, "is on an", "ULTRA KILL", 3)]
    [InlineData(6, "unleashes a", "MASSACRE", 4)]
    [InlineData(7, "is pure", "CARNAGE", 5)]
    [InlineData(12, "is pure", "CARNAGE", 5)]
    public void MultiKillLadder(byte magnitude, string link, string loud, int heat) =>
        Assert.Equal(new MultiKillWording(link, loud, heat), Vocab.MultiKillTier(magnitude));

    [Theory]
    [InlineData(5, "has a", "taste for it", "BLOODLUST", "has", 0)]
    [InlineData(7, "demands", "obedience", "PUNISHMENT", "serves", 1)]
    [InlineData(9, "is", "dominating", "DOMINATING", "is", 2)]
    [InlineData(11, "is a", "MACHINE GOD", "MACHINE GOD", "is a", 3)]
    [InlineData(13, "is a", "fucking psycho", "FUCKING PSYCHO", "is a", 4)]
    [InlineData(23, "is a", "fucking psycho", "FUCKING PSYCHO", "is a", 4)]
    public void StreakLadder(
        byte magnitude, string weak, string strong, string streakName, string streakVerb, int heat) =>
        Assert.Equal(
            new StreakWording(weak, strong, streakName, streakVerb, heat),
            Vocab.StreakTier(magnitude));

    [Fact]
    public void SuicideQuipsComeFromTheCausePool()
    {
        VariantPicker picker = new(new Random(1));

        string[] blastPool =
        [
            "couldn't take it anymore",
            "chose the easy way out",
            "went out with a bang",
        ];
        string[] fallPool =
        [
            "thought they could fly",
            "found the exit",
            "left without saying goodbye",
        ];
        for (int i = 0; i < 10; i++)
        {
            Assert.Contains(Vocab.SuicideQuip(SuicideCause.BLAST, picker), blastPool);
            Assert.Contains(Vocab.SuicideQuip(SuicideCause.FALL, picker), fallPool);
        }
    }
}
