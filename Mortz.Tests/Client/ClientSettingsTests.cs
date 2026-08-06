using Mortz.Client.Servers;
using Mortz.Client.Settings;
using Xunit;

namespace Mortz.Tests.Client;

public class ClientSettingsTests
{
    [Fact]
    public void IdentityRequiresBothANameAndAValidSkin()
    {
        ClientSettings settings = new() { PlayerName = "Alice" };
        Assert.False(settings.HasIdentity);

        settings.SelectedSkin = 24;
        Assert.True(settings.HasIdentity);
        Assert.Equal(24, settings.Skin);

        settings.PlayerName = "   ";
        Assert.False(settings.HasIdentity);

        settings.PlayerName = "Alice";
        settings.SelectedSkin = 25;
        Assert.False(settings.HasIdentity);
        Assert.Equal(0, settings.Skin);
    }

    [Fact]
    public void ProfileRoundTripsAsToml()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"mortz-profile-{Guid.NewGuid():N}");
        try
        {
            ClientSettings settings = new(directory);
            settings.SetIdentity("Alice", 4);
            settings.SetFavorites([new FavoriteServer("arena.example", 42069, "Arena")]);

            ClientSettings loaded = ClientSettings.Load(directory);

            Assert.Equal("Alice", loaded.PlayerName);
            Assert.Equal(4, loaded.SelectedSkin);
            FavoriteServer favorite = Assert.Single(loaded.Favorites);
            Assert.Equal(new FavoriteServer("arena.example", 42069, "Arena"), favorite);
            string text = File.ReadAllText(Path.Combine(directory, "profile.toml"));
            Assert.Contains("player_name = \"Alice\"", text);
            Assert.Contains("[[favorites]]", text);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void MissingOptionalProfileValuesUseDefaults()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"mortz-profile-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "profile.toml"), "player_name = \"Alice\"\n");

            ClientSettings loaded = ClientSettings.Load(directory);

            Assert.Equal("Alice", loaded.PlayerName);
            Assert.Null(loaded.SelectedSkin);
            Assert.Empty(loaded.Favorites);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

}
