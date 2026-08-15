using System.Reflection;
using Chickensoft.AutoInject;
using Godot;
using Mortz.Client.Audio;
using Mortz.Client.Effects;
using Mortz.Client.Match;
using Mortz.Client.Players;
using Mortz.Client.Replay;
using Mortz.Client.Views;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Net.Match;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Terrain;
using Mortz.Net;
using Mortz.Shared;
using Mortz.Tests.Net;
using Xunit;

namespace Mortz.Tests.Client;

[Collection(nameof(MortzGodotCollection))]
public class FinalKillReplayTests : NodeServiceTest
{
    private const int LOCAL_ID = 7;
    private const int VICTIM_ID = 8;
    private const int IMPACT_X = 16;
    private const int IMPACT_Y = 16;

    [Fact]
    public void InsufficientHistoryPlaysFallbackWithoutEnteringReplayMode()
    {
        GameView shell = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/Scenes/Match/GameView.tscn").Instantiate<GameView>();
        GameMap map = CreateMap();
        try
        {
            EffectsSpawner effects = shell.GetNode<EffectsSpawner>("Effects");
            effects.FakeDependency(map);
            effects.FakeDependency<ISfx>(new NullSfx());
            effects.OnReady();
            Node2D liveEffects = effects.GetNode<Node2D>("LiveEffects");

            FinalKillReplay replay = shell.GetNode<FinalKillReplay>("FinalKillReplay");
            Camera2D replayCamera = shell.GetNode<Camera2D>("ReplayCamera");
            Sprite2D replayTerrain = map.GetNode<Sprite2D>("ReplayTerrain");
            Router.Add(effects);
            Router.Add(replay);
            try
            {
                new FinalKillMsg(
                    10, 7, 8, FinalKillFlags.FALL,
                    12, 14, 12, 14, 0).Broadcast(Router);
                Assert.True(replay.ConsumeFrame(0));

                Node2D fallbackEffects = effects.GetChildren().OfType<Node2D>()
                    .Single(child => child != liveEffects && child.Visible);
                Assert.IsType<GibBurst>(Assert.Single(fallbackEffects.GetChildren()));
                Assert.Same(liveEffects, effects.GetNode<Node2D>("LiveEffects"));
                Assert.True(liveEffects.Visible);
                Assert.False(replayTerrain.Visible);
                Assert.False(replayCamera.Enabled);
                Assert.Equal(1f, ClientClock.TimeScale);
            }
            finally
            {
                Router.Remove(replay);
                Router.Remove(effects);
                ClientClock.Reset();
            }
        }
        finally
        {
            shell.Free();
            map.Free();
        }
    }

    [Fact]
    public void SuccessfulReplayRendersRecordedFrameAndTakesCamera()
    {
        SuccessfulReplay fixture = CreateSuccessfulReplay();
        try
        {
            PresentedMatchFrame first = Frame(0, offset: 0);
            fixture.Replay.Record(first);
            fixture.Replay.Record(Frame(45, offset: 45));
            fixture.Map.PredictCarve(99, new Vector2(IMPACT_X, IMPACT_Y));

            fixture.FinalKill.Broadcast(Router);
            Assert.True(fixture.Replay.ConsumeFrame(0));

            PlayerView local = fixture.Players.ViewForTest(LOCAL_ID);
            Assert.Equal(
                first.Players[0].State.Feet - new Vector2(0, SimConfig.PLAYER_HALF_HEIGHT),
                local.Position);
            MortarView mortar = Assert.Single(
                fixture.Mortars.GetChildren().OfType<MortarView>());
            Assert.Equal(first.Mortars[0].Position, mortar.Position);
            Assert.Equal(first.Ropes[0], Assert.Single(fixture.Ropes.Segments));

            Assert.False(fixture.MatchCamera.Enabled);
            Assert.True(fixture.ReplayCamera.Enabled);
            Assert.Equal(
                fixture.ReplayCamera.GetInstanceId(),
                fixture.Replay.GetViewport().GetCamera2D()?.GetInstanceId());
        }
        finally
        {
            fixture.Shell.Free();
            ClientClock.Reset();
        }
    }

    [Fact]
    public void SuccessfulReplayTransitionsImpactOnceAndHoldsForOneHundredTwentyMilliseconds()
    {
        SuccessfulReplay fixture = CreateSuccessfulReplay();
        try
        {
            fixture.Replay.Record(Frame(0, offset: 0));
            fixture.Replay.Record(Frame(45, offset: 45));
            fixture.Map.PredictCarve(99, new Vector2(IMPACT_X, IMPACT_Y));
            Node2D originalLiveEffects = fixture.Effects.GetNode<Node2D>("LiveEffects");
            int liveSoundCount = fixture.Sfx.PlayAtPositions.Count;

            fixture.FinalKill.Broadcast(Router);
            fixture.Replay.ConsumeFrame(0);

            Sprite2D terrain = fixture.Map.GetNode<Sprite2D>("ReplayTerrain");
            Assert.True(terrain.Visible);
            Assert.True(ReplayTerrainPixel(fixture.Map).A > 0);
            Assert.False(originalLiveEffects.Visible);
            Assert.Empty(VisibleEffectNodes(fixture.Effects));
            Assert.Equal(liveSoundCount, fixture.Sfx.PlayAtPositions.Count);

            fixture.Replay.ConsumeFrame(2.5f);

            Assert.False(terrain.Visible);
            Assert.True(ReplayTerrainPixel(fixture.Map).A > 0);
            Assert.Equal(liveSoundCount + 2, fixture.Sfx.PlayAtPositions.Count);
            Assert.Single(VisibleEffectNodes(fixture.Effects).OfType<GibBurst>());
            int replayEffectCount = VisibleEffectNodes(fixture.Effects).Count;
            MortarView mortar = Assert.Single(
                fixture.Mortars.GetChildren().OfType<MortarView>());

            fixture.Replay.ConsumeFrame(0.119f);

            Assert.True(mortar.Visible);
            Assert.False(mortar.IsQueuedForDeletion());
            Assert.True(ReplayTerrainPixel(fixture.Map).A > 0);
            Assert.Equal(replayEffectCount, VisibleEffectNodes(fixture.Effects).Count);
            Assert.Equal(liveSoundCount + 2, fixture.Sfx.PlayAtPositions.Count);

            fixture.Replay.ConsumeFrame(0.002f);

            Assert.False(mortar.Visible);
            Assert.True(mortar.IsQueuedForDeletion());
            Assert.Equal(0, ReplayTerrainPixel(fixture.Map).A);
            Assert.Equal(replayEffectCount, VisibleEffectNodes(fixture.Effects).Count);
            Assert.Equal(liveSoundCount + 2, fixture.Sfx.PlayAtPositions.Count);

            fixture.Map.PredictCarve(100, new Vector2(IMPACT_X, IMPACT_Y));
            Assert.Equal(replayEffectCount + 1, VisibleEffectNodes(fixture.Effects).Count);
            Assert.Equal(liveSoundCount + 3, fixture.Sfx.PlayAtPositions.Count);
        }
        finally
        {
            fixture.Shell.Free();
            ClientClock.Reset();
        }
    }

    private SuccessfulReplay CreateSuccessfulReplay()
    {
        GameView shell = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/Scenes/Match/GameView.tscn").Instantiate<GameView>();
        GameMap map = CreateMap(withDestructibleTerrain: true);
        FakeNetwork network = new() { LocalPeerId = LOCAL_ID };
        RecordingSfx sfx = new();
        ClientPlayers clientPlayers = new();
        clientPlayers.FakeDependency(Router);
        clientPlayers.OpenMatch(new MatchConfig());

        EffectsSpawner effects = Take<EffectsSpawner>(shell, "Effects");
        PlayerViewManager players = Take<PlayerViewManager>(shell, "Players");
        MortarViewManager mortars = Take<MortarViewManager>(shell, "Mortars");
        Camera2D replayCamera = Take<Camera2D>(shell, "ReplayCamera");
        Camera2D matchCamera = Take<Camera2D>(shell, "MatchCamera");
        FinalKillReplay replay = Take<FinalKillReplay>(shell, "FinalKillReplay");
        RopeOverlay ropes = shell.GetNode<RopeOverlay>("RopeOverlay");

        effects.FakeDependency(map);
        effects.FakeDependency<ISfx>(sfx);
        effects.FakeDependency(Router);
        players.FakeDependency<INetwork>(network);
        players.FakeDependency<ISfx>(sfx);
        players.FakeDependency(clientPlayers);
        mortars.FakeDependency<ISfx>(sfx);
        replay.FakeDependency<INetwork>(network);
        replay.FakeDependency(map);
        replay.FakeDependency(Router);
        map.FakeDependency<INetwork>(network);
        map.FakeDependency(Router);

        MatchSceneRenderer renderer = new(players, mortars, ropes);
        replay.Initialize(renderer);

        // Host owners before their dependencies so teardown releases subscriptions first.
        Host(replay);
        Host(effects);
        Host(players);
        Host(mortars);
        Host(matchCamera);
        Host(replayCamera);
        Host(clientPlayers);
        Host(map);

        FinalKillMsg final = new(
            45,
            LOCAL_ID,
            VICTIM_ID,
            FinalKillFlags.EXPLOSION,
            DeathX: 18,
            DeathY: 18,
            ImpactX: IMPACT_X,
            ImpactY: IMPACT_Y,
            BlastRadius: 2);
        return new SuccessfulReplay(
            shell, map, effects, players, mortars, ropes, replay,
            replayCamera, matchCamera, sfx, final);
    }

    private static PresentedMatchFrame Frame(float tick, float offset) => new(
        tick,
        [
            new PresentedPlayer(LOCAL_ID, PlayerStateAt(new Vector2(5 + offset, 24))),
            new PresentedPlayer(VICTIM_ID, PlayerStateAt(new Vector2(20 + offset, 24))),
        ],
        [
            new PresentedMortar(
                PresentedMortarKey.Authoritative(4),
                new Vector2(9 + offset, 10),
                new Vec2(1, 2)),
        ],
        [new RopeSegment(new Vector2(5 + offset, 16), new Vector2(12 + offset, 8))]);

    private static PlayerViewState PlayerStateAt(Vector2 feet) => new(
        feet,
        Aim: 1,
        Skin: 2,
        Ammo: 3,
        ReloadTicks: 0,
        Health: 100,
        RespawnTicks: 0,
        ParryTicks: 0,
        DashCooldown: 0,
        SpawnImmunityTicks: 0,
        new PlayerPresentationState(KillingSpreeMagnitude: 5, IsBleeding: true));

    private static List<Node> VisibleEffectNodes(EffectsSpawner effects) =>
        effects.GetChildren()
            .OfType<Node2D>()
            .Where(container => container.Visible)
            .SelectMany(container => container.GetChildren())
            .ToList();

    private static Color ReplayTerrainPixel(GameMap map)
    {
        FieldInfo imageField = typeof(GameMap).GetField(
                                   "_replayTerrainImage",
                                   BindingFlags.Instance | BindingFlags.NonPublic) ??
                               throw new InvalidOperationException("Replay terrain image field was not found.");
        Image image = Assert.IsType<Image>(imageField.GetValue(map));
        return image.GetPixel(IMPACT_X, IMPACT_Y);
    }

    private static T Take<T>(Node parent, string path) where T : Node
    {
        T node = parent.GetNode<T>(path);
        parent.RemoveChild(node);
        return node;
    }

    private static GameMap CreateMap(bool withDestructibleTerrain = false)
    {
        const int SIZE = 32;
        GameMap map = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/Scenes/Match/GameMap.tscn").Instantiate<GameMap>();
        Image destructible = Image.CreateEmpty(SIZE, SIZE, false, Image.Format.Rgba8);
        if (withDestructibleTerrain)
            destructible.Fill(Colors.White);
        MapPackage package = new()
        {
            MapId = "test",
            DisplayName = "Test",
            SuggestedPlayers = 2,
            Hash = "hash",
            Width = SIZE,
            Height = SIZE,
            SpawnPoints = [],
            Zones = new MapZones([]),
            Background = Image.CreateEmpty(SIZE, SIZE, false, Image.Format.Rgba8),
            Solid = Image.CreateEmpty(SIZE, SIZE, false, Image.Format.Rgba8),
            Destructible = destructible,
            InitialTerrain = new TerrainMask(
                SIZE,
                SIZE,
                (_, _) => false,
                (_, _) => withDestructibleTerrain),
        };
        map.Initialize(package, new MatchConfig().Combat,
            TerrainSyncEncoding.CARVE_LOG, TerrainSync.SerializeCarves([]));
        return map;
    }

    private sealed record SuccessfulReplay(
        GameView Shell,
        GameMap Map,
        EffectsSpawner Effects,
        PlayerViewManager Players,
        MortarViewManager Mortars,
        RopeOverlay Ropes,
        FinalKillReplay Replay,
        Camera2D ReplayCamera,
        Camera2D MatchCamera,
        RecordingSfx Sfx,
        FinalKillMsg FinalKill);

    private sealed class RecordingSfx : ISfx
    {
        private readonly NullSfx _inner = new();

        public SoundRegistry Sounds => _inner.Sounds;
        public List<Vector2> PlayAtPositions { get; } = [];

        public SfxHandle Play(SoundEffect? sound, float pitch = 1, float gainDb = 0) => default;

        public SfxHandle PlayAt(
            SoundEffect? sound,
            Vector2 position,
            float pitch = 1,
            float gainDb = 0)
        {
            PlayAtPositions.Add(position);
            return default;
        }

        public SfxHandle PlayAttached(
            SoundEffect? sound,
            Node2D target,
            float pitch = 1,
            float gainDb = 0) => default;
    }
}
