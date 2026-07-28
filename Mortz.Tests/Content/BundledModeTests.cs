using Godot;
using Mortz.Content;
using Mortz.Core.Match;
using Xunit;

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
        ModeRules dmRules = deathmatch!.Winner.Manifest.Config.Rules;
        Assert.False(dmRules.Teams);
        Assert.Equal(WinCondition.KILLS, dmRules.WinCondition);
        Assert.Equal(5, dmRules.KillTarget);
        Assert.Equal(SuicidePenalty.KILL_NO_NEGATIVE, dmRules.SuicidePenalty);
        Assert.Equal(2, deathmatch.Winner.Manifest.Identity?.KeyCount);

        Assert.True(catalog.TryGetMode("teamdeathmatch", out ResolvedContent<GameModeManifest>? teams));
        ModeRules rules = teams!.Winner.Manifest.Config.Rules;
        Assert.True(rules.Teams);
        Assert.Equal(WinCondition.KILLS, rules.WinCondition);
        Assert.Equal(SuicidePenalty.REWARD_CLOSEST_ENEMY, rules.SuicidePenalty);
        Assert.Equal(2, teams.Winner.Manifest.Identity?.KeyCount);

        MatchConfig customizedDeathmatch =
            MatchConfig.FromBytes(deathmatch.Winner.Manifest.Config.ToBytes());
        customizedDeathmatch.Rules.KillTarget = 100;
        Assert.True(deathmatch.Winner.Manifest.MatchesIdentity(customizedDeathmatch));
        Assert.False(teams.Winner.Manifest.MatchesIdentity(customizedDeathmatch));
    }
}
