using Godot;
using Mortz.Content;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Scoring;
using Xunit;
using ModeRules = Mortz.Core.Match.Configuration.ModeRules;

namespace Mortz.Tests.Content;

[Collection(nameof(MortzGodotCollection))]
public class BundledModeTests
{
    [Fact]
    public void BundledModesLoadWithTheirAuthoredRules()
    {
        string contentRoot = ProjectSettings.GlobalizePath("res://content");
        ContentCatalogResult result = ContentCatalog.Load(contentRoot);
        ContentCatalog catalog = Assert.IsType<ContentCatalog>(result.Catalog);
        Assert.DoesNotContain(result.Diagnostics,
            diagnostic => diagnostic.Severity == ContentDiagnosticSeverity.ERROR);

        Assert.True(catalog.TryGetMode("deathmatch", out ResolvedContent<GameModeManifest>? deathmatch));
        ModeRules dmRules = deathmatch!.Winner.Manifest.Rules;
        Assert.False(dmRules.Teams);
        Assert.Equal(5, Assert.IsType<KillsVictoryRules>(dmRules.Victory).Target);
        Assert.Equal(SuicidePenalty.KILL_NO_NEGATIVE, dmRules.SuicidePenalty);

        Assert.True(catalog.TryGetMode("teamdeathmatch", out ResolvedContent<GameModeManifest>? teams));
        ModeRules rules = teams!.Winner.Manifest.Rules;
        Assert.True(rules.Teams);
        Assert.IsType<KillsVictoryRules>(rules.Victory);
        Assert.Equal(SuicidePenalty.REWARD_CLOSEST_ENEMY, rules.SuicidePenalty);

        Assert.True(catalog.TryGetMode("killlead", out ResolvedContent<GameModeManifest>? killLead));
        ModeRules killLeadRules = killLead!.Winner.Manifest.Rules;
        Assert.False(killLeadRules.Teams);
        Assert.Equal(3,
            Assert.IsType<KillLeadVictoryRules>(killLeadRules.Victory).Target);
        Assert.Equal(SuicidePenalty.KILL_NO_NEGATIVE, killLeadRules.SuicidePenalty);

        Assert.True(catalog.TryGetMode(
            "teamkilllead", out ResolvedContent<GameModeManifest>? teamKillLead));
        ModeRules teamKillLeadRules = teamKillLead!.Winner.Manifest.Rules;
        Assert.True(teamKillLeadRules.Teams);
        Assert.Equal(5,
            Assert.IsType<KillLeadVictoryRules>(teamKillLeadRules.Victory).Target);
        Assert.Equal(SuicidePenalty.REWARD_CLOSEST_ENEMY, teamKillLeadRules.SuicidePenalty);

        MatchConfig customizedDeathmatch = new()
        {
            Rules = deathmatch.Winner.Manifest.Rules.ToSnapshot().ToMutable(),
        };
        Assert.IsType<KillsVictoryRules>(customizedDeathmatch.Rules.Victory).Target = 100;
        Assert.True(deathmatch.Winner.Manifest.Matches(customizedDeathmatch));
        Assert.False(teams.Winner.Manifest.Matches(customizedDeathmatch));
    }
}
