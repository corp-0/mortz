using Godot;
using Mortz.Client.Menus;
using Mortz.Core.Sim.Modifiers;
using Mortz.Shared;
using Xunit;

namespace Mortz.Tests.Client.Menus;

[Collection(nameof(MortzGodotCollection))]
public class LobbySettingsPanelTests
{
    [Fact]
    public void MapPreviewBlendsDifferentFormatsWithoutMutatingSharedLayers()
    {
        using Image background = Image.CreateFromData(3, 1, false, Image.Format.Rgb8,
            [0, 0, 255, 0, 0, 255, 0, 0, 255]);
        using Image solid = Image.CreateFromData(3, 1, false, Image.Format.Rgba8,
            [255, 0, 0, 255, 0, 0, 0, 0, 0, 0, 0, 0]);
        using Image destructible = Image.CreateFromData(3, 1, false, Image.Format.La8,
            [0, 0, 255, 255, 0, 0]);
        byte[] originalBackground = background.GetData();
        byte[] originalSolid = solid.GetData();
        byte[] originalDestructible = destructible.GetData();
        MapPackage map = new()
        {
            MapId = "preview",
            DisplayName = "Preview",
            SuggestedPlayers = 1,
            Hash = "preview",
            Width = 3,
            Height = 1,
            SpawnPoints = [],
            Zones = MapZones.None,
            Background = background,
            Solid = solid,
            Destructible = destructible,
        };

        using Image preview = LobbySettingsPanel.ComposePreview(map);

        Assert.Equal(Colors.Red, preview.GetPixel(0, 0));
        Assert.Equal(Colors.White, preview.GetPixel(1, 0));
        Assert.Equal(Colors.Blue, preview.GetPixel(2, 0));
        Assert.Equal(Image.Format.Rgb8, background.GetFormat());
        Assert.Equal(Image.Format.Rgba8, solid.GetFormat());
        Assert.Equal(Image.Format.La8, destructible.GetFormat());
        Assert.Equal(originalBackground, background.GetData());
        Assert.Equal(originalSolid, solid.GetData());
        Assert.Equal(originalDestructible, destructible.GetData());
    }
}
