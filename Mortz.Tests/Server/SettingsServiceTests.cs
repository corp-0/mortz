using System.Text;
using Mortz.Content;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server;
using Mortz.Server.Content;
using Mortz.Server.Settings;
using Serilog.Core;
using Xunit;
using ModeRules = Mortz.Core.Match.Configuration.ModeRules;

namespace Mortz.Tests.Server;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mortz-settings-{Guid.NewGuid():N}");
    private readonly RecordingTransport _transport = new();
    private readonly IServerLink _link;

    public SettingsServiceTests() => _link = new ReadyLink(_transport);
    private readonly FakeMapSource _maps = new();

    [Fact]
    public void RulesUpdateAppliesAndReportsTheChange()
    {
        SettingsService settings = Build();
        MatchConfig next = new()
        {
            Rules = new ModeRules
            {
                Victory = new KillsVictoryRules { Target = 42 },
            },
        };

        bool applied = settings.TrySetRules(next.ToBytes(), out LobbySettingDelta[] deltas);

        Assert.True(applied);
        Assert.Equal(42,
            Assert.IsType<KillsVictoryRules>(settings.Config.Rules.Victory).Target);
        Assert.Contains(deltas, delta => delta.After == "42");
    }

    [Fact]
    public void MalformedRulesChangeNothing()
    {
        SettingsService settings = Build();
        MatchConfig before = settings.Config;

        bool applied = settings.TrySetRules([1, 2, 3], out LobbySettingDelta[] deltas);

        Assert.False(applied);
        Assert.Same(before, settings.Config);
        Assert.Empty(deltas);
        Assert.Empty(_transport.Messages);
    }

    [Fact]
    public void ModeUpdateAppliesItsRulesAndNamesTheMode()
    {
        SettingsService settings = Build();

        bool applied = settings.TrySetMode("teamdeathmatch", out LobbySettingDelta[] deltas);

        Assert.True(applied);
        Assert.True(settings.Config.Rules.Teams);
        Assert.Equal("Team Deathmatch", settings.ModeName);
        LobbySettingDelta delta = Assert.Single(deltas);
        Assert.Equal("Mode", delta.Name);
        Assert.Equal("Team Deathmatch", delta.After);
    }

    [Fact]
    public void UnknownModeChangesNothing()
    {
        SettingsService settings = Build();
        MatchConfig before = settings.Config;
        string beforeName = settings.ModeName;

        bool applied = settings.TrySetMode("brawl", out LobbySettingDelta[] deltas);

        Assert.False(applied);
        Assert.Same(before, settings.Config);
        Assert.Equal(beforeName, settings.ModeName);
        Assert.Empty(deltas);
    }

    [Fact]
    public void MapUpdateSelectsTheLoadedSnapshot()
    {
        _maps.Add(Snapshot("duel", "Duel"));
        SettingsService settings = Build();

        bool applied = settings.TrySetMap("duel", out LobbySettingDelta[] deltas);

        Assert.True(applied);
        Assert.Equal("duel", settings.Map.MapId);
        LobbySettingDelta delta = Assert.Single(deltas);
        Assert.Equal("Map", delta.Name);
        Assert.Equal("Arena", delta.Before);
        Assert.Equal("Duel", delta.After);
    }

    [Fact]
    public void MapOutsideTheCatalogChangesNothing()
    {
        _maps.Add(Snapshot("elsewhere", "Elsewhere"));
        SettingsService settings = Build();
        MapSnapshot before = settings.Map;

        bool applied = settings.TrySetMap("elsewhere", out LobbySettingDelta[] deltas);

        Assert.False(applied);
        Assert.Same(before, settings.Map);
        Assert.Empty(deltas);
    }

    [Fact]
    public void MapThatFailsToLoadChangesNothing()
    {
        SettingsService settings = Build();
        MapSnapshot before = settings.Map;

        bool applied = settings.TrySetMap("duel", out LobbySettingDelta[] deltas);

        Assert.False(applied);
        Assert.Same(before, settings.Map);
        Assert.Empty(deltas);
    }

    [Fact]
    public void StateGoesOutOverTheLink()
    {
        SettingsService settings = Build();

        settings.SendTo(7);
        settings.Broadcast();

        Assert.Equal([7, 0], _transport.Messages.Select(sent => sent.Target).ToArray());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private SettingsService Build()
    {
        string pack = AddPack("Base", "base");
        AddMap(pack, "arena", "Arena");
        AddMap(pack, "duel", "Duel");
        AddMode(pack, "deathmatch", "Deathmatch",
            "[rules.victory]\ntype = \"kills\"\ntarget = 5");
        AddMode(pack, "teamdeathmatch", "Team Deathmatch", "teams = true");
        ContentCatalog catalog = Assert.IsType<ContentCatalog>(ContentCatalog.Load(_root).Catalog);
        ServerBoot boot = new()
        {
            Map = Snapshot("arena", "Arena"),
            Rules = new MatchConfig(),
            Catalog = catalog,
            AdminPassword = "",
            Name = "test",
            GamePort = 7777,
            QueryPort = 7778,
            Seed = 1,
            NetStats = false,
            AllowJoinInProgress = true,
        };
        return new SettingsService(boot, _maps, _link, Logger.None);
    }

    private static MapSnapshot Snapshot(string id, string name) => new()
    {
        MapId = id,
        DisplayName = name,
        Hash = $"{id}-hash",
        Width = 4,
        Height = 4,
        SpawnPoints = [new SpawnPoint(new Vec2(1, 1))],
        InitialTerrain = new TerrainMask(4, 4, (_, _) => false, (_, _) => false),
    };

    private string AddPack(string directoryName, string id)
    {
        string directory = Path.Combine(_root, directoryName);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "content_pack.toml"),
            $"id = \"org.mortz.{id}\"\nname = \"{id}\"\nversion = \"1.0.0\"\nload_order = 0\n");
        return directory;
    }

    private static void AddMap(string packDirectory, string id, string name)
    {
        string directory = Path.Combine(packDirectory, "maps", id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "map.toml"),
            TomlModel.Write(new MapManifest { Name = name, SuggestedPlayers = 4 }));
        foreach (string layer in new[] { "background.png", "solid.png", "destructible.png" })
        {
            File.WriteAllBytes(Path.Combine(directory, layer), Encoding.UTF8.GetBytes(layer));
        }
    }

    private static void AddMode(string packDirectory, string id, string name, string rule)
    {
        string directory = Path.Combine(packDirectory, "modes", id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "mode.toml"),
            $"format_version = 1\nname = \"{name}\"\n\n[rules]\n{rule}\n");
    }
}
