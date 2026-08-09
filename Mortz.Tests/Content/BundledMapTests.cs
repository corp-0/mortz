using Godot;
using Mortz.Content;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Sim;
using Mortz.Shared;
using Xunit;

namespace Mortz.Tests.Content;

[Collection(nameof(MortzGodotCollection))]
public class BundledMapTests
{
    [Fact]
    public void EveryBundledMapLoadsAndPreservesItsSpawnPoints()
    {
        string contentRoot = ProjectSettings.GlobalizePath("res://content");
        ContentCatalogResult result = ContentCatalog.Load(contentRoot);
        ContentCatalog catalog = Assert.IsType<ContentCatalog>(result.Catalog);
        Assert.DoesNotContain(result.Diagnostics,
            diagnostic => diagnostic.Severity == ContentDiagnosticSeverity.ERROR);
        Assert.NotEmpty(catalog.Maps);

        foreach ((string mapId, ResolvedContent<MapManifest> resolved) in catalog.Maps
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            MapPackageLoadResult load = MapPackageLoader.Load(resolved.Winner);
            Assert.False(load.HasErrors);
            MapPackage package = Assert.IsType<MapPackage>(load.Package);
            Assert.Equal(mapId, package.MapId);
            Assert.True(package.Width > 0);
            Assert.True(package.Height > 0);
            Assert.Equal(4, package.Hash.Split(':').Length);
            Assert.True(package.SpawnPoints.Length >= package.SuggestedPlayers,
                $"{mapId} needs at least {package.SuggestedPlayers} spawn points");
            Assert.Equal(package.SpawnPoints.Length, package.SpawnPoints.Distinct().Count());

            SimWorld world = new(package.BuildMask(), new MatchConfig(), package.SpawnPoints);
            for (int slot = 1; slot <= package.SuggestedPlayers; slot++)
            {
                int peerId = 1000 + slot;
                world.AddPlayer(peerId);
                Assert.Equal(package.SpawnPoints[slot - 1].Position,
                    world.Players[peerId].Position);
            }
        }
    }
}
