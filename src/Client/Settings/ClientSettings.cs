using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using Mortz.Client.Servers;
using Mortz.Core.Net;
using FileAccess = Godot.FileAccess;

namespace Mortz.Client.Settings;

/// <summary>What a player should only have to say once: their name and their
/// favorite servers. Thin on purpose, Steam will own identity later.</summary>
public sealed class ClientSettings
{
    private const string PATH = "user://settings.json";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string PlayerName { get; set; } = "";
    public List<FavoriteServer> Favorites { get; set; } = [];

    public static ClientSettings Load()
    {
        if (!FileAccess.FileExists(PATH))
            return new ClientSettings();
        try
        {
            using FileAccess file = FileAccess.Open(PATH, FileAccess.ModeFlags.Read);
            return JsonSerializer.Deserialize<ClientSettings>(file.GetAsText(), _jsonOptions)
                   ?? new ClientSettings();
        }
        catch (Exception exception)
        {
            GD.PrintErr($"[client] unreadable {PATH}, starting fresh: {exception.Message}");
            return new ClientSettings();
        }
    }

    public void Save()
    {
        using FileAccess file = FileAccess.Open(PATH, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            GD.PrintErr($"[client] failed to write {PATH}: {FileAccess.GetOpenError()}");
            return;
        }
        file.StoreString(JsonSerializer.Serialize(this, _jsonOptions));
    }

    public void SetPlayerName(string name)
    {
        PlayerName = PlayerNameSanitizer.Sanitize(name);
        Save();
    }

    public void SetFavorites(IEnumerable<FavoriteServer> favorites)
    {
        Favorites = favorites.ToList();
        Save();
    }
}
