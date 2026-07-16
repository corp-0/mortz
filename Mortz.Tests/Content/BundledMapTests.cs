using Godot;
using Mortz.Content;
using Mortz.Core;
using Mortz.Core.Match;
using Mortz.Core.Sim;
using Mortz.Shared;
using twodog.xunit;
using Xunit;

namespace Mortz.Tests.Content;

[Collection(nameof(GodotHeadlessCollection))]
public class BundledMapTests
{
    [Fact]
    public void EveryBundledMapLoads_AndItsSpawnPointsHold()
    {
        string contentRoot = ProjectSettings.GlobalizePath("res://content");
        ContentCatalogResult result = ContentCatalog.Load(contentRoot);
        ContentCatalog catalog = Assert.IsType<ContentCatalog>(result.Catalog);
        Assert.DoesNotContain(result.Diagnostics,
            diagnostic => diagnostic.Severity == ContentDiagnosticSeverity.Error);
        Assert.NotEmpty(catalog.Maps);

        foreach (string mapId in catalog.Maps.Keys.Order(StringComparer.Ordinal))
        {
            MapPackage package = Assert.IsType<MapPackage>(MapPackage.Load(mapId, contentRoot));
            Assert.Equal(mapId, package.MapId);
            Assert.True(package.Width > 0);
            Assert.True(package.Height > 0);
            Assert.Equal(4, package.Hash.Split(':').Length);
            Assert.True(package.SpawnPoints.Length >= package.SuggestedPlayers,
                $"{mapId} needs at least {package.SuggestedPlayers} spawn points");
            Assert.Equal(package.SpawnPoints.Length, package.SpawnPoints.Distinct().Count());
            Assert.Empty(SpawnPointValidator.Validate(package.BuildMask(), package.SpawnPoints));

            SimWorld world = new(package.BuildMask(), new MatchConfig(), seed: 1, package.SpawnPoints);
            for (int slot = 1; slot <= package.SuggestedPlayers; slot++)
            {
                int peerId = 1000 + slot;
                world.AddPlayer(peerId);
                Assert.Equal(package.SpawnPoints[slot - 1], world.Players[peerId].Position);
                Assert.True(world.Players[peerId].Grounded);
            }
        }
    }
}
