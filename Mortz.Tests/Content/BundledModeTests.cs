using Godot;
using Mortz.Content;
using Mortz.Core.Match;
using Xunit;

namespace Mortz.Tests.Content;

[Collection(nameof(MortzGodotCollection))]
public class BundledModeTests
{
    [Fact]
    public void BundledModesLoadAndDeathmatchIsTheDefaultConfig()
    {
        string contentRoot = ProjectSettings.GlobalizePath("res://content");
        ContentCatalogResult result = ContentCatalog.Load(contentRoot);
        ContentCatalog catalog = Assert.IsType<ContentCatalog>(result.Catalog);
        Assert.DoesNotContain(result.Diagnostics,
            diagnostic => diagnostic.Severity == ContentDiagnosticSeverity.ERROR);

        Assert.True(catalog.TryGetMode("deathmatch", out ResolvedContent<GameModeManifest>? deathmatch));
        // A fresh server boots on defaults and must advertise as Deathmatch.
        Assert.Equal(new MatchConfig().ToBytes(), deathmatch!.Winner.Manifest.Rules.ToBytes());

        Assert.True(catalog.TryGetMode("teamdeathmatch", out ResolvedContent<GameModeManifest>? teams));
        MatchConfig rules = teams!.Winner.Manifest.Rules;
        Assert.True(rules.Teams);
        Assert.Equal(WinCondition.TEAM_KILLS, rules.WinCondition);
    }
}
