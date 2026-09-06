using Mortz.Content;
using Mortz.Core.Match.Configuration;
using Xunit;

namespace Mortz.Runtime.Tests.Core.Match;

public class ModeDefinitionTests
{
    [Theory]
    [InlineData("score")]
    [InlineData("winner")]
    [InlineData("evaluation")]
    [InlineData("replay")]
    public void UnknownComponentsRejectTheDefinition(string component)
    {
        ContentReadResult<RulesetManifest> read = TomlModel.Read<RulesetManifest>(
            $"[rules]\n{component} = \"missing_component\"\n");

        Assert.Null(read.Value);
        Assert.Contains(read.Diagnostics, diagnostic => diagnostic.Severity == ContentDiagnosticSeverity.ERROR &&
            diagnostic.Message.Contains($"rules.{component}", StringComparison.Ordinal));
    }

    [Fact]
    public void DefaultIdentityDistinguishesModeComponentsButAllowsTargetCustomization()
    {
        GameModeManifest mode = new() { Name = "Kills", FormatVersion = 1 };
        MatchConfig draft = mode.ToMatchConfigSnapshot().ToMutable();
        ((ScoreTargetRules)draft.Rules.EndCondition).Target = 80;
        Assert.True(mode.Matches(draft));
        draft.Rules.Score = ScoreMetric.DEATHS;
        Assert.False(mode.Matches(draft));
        draft.Rules.Score = ScoreMetric.KILLS;
        draft.Rules.Replay = ReplayRule.NONE;
        Assert.False(mode.Matches(draft));
    }
}
