using System.Text.Json;
using Godot;

namespace Mortz.Server;

/// <summary>
/// Dedicated-box settings, read from server.json in the working directory so
/// a rented box works without CLI args; a CLI flag overrides its file
/// counterpart. Machine settings only: match rules live in the ruleset file
/// (--ruleset) and are replicated to clients, while nothing in here ever
/// leaves the server.
/// </summary>
public sealed class ServerConfig
{
    private const string FILE_NAME = "server.json";

    /// <summary>Players who send /admin with this password get live control
    /// over the server settings in the lobby. Empty = no admin access.</summary>
    public string AdminPassword { get; set; } = "";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Defaults when the file doesn't exist; null when it exists but
    /// is unusable, so a host never silently runs without their settings.</summary>
    public static ServerConfig? Load()
    {
        if (!File.Exists(FILE_NAME))
            return new ServerConfig();
        try
        {
            ServerConfig config = JsonSerializer.Deserialize<ServerConfig>(File.ReadAllText(FILE_NAME), _jsonOptions)
                                  ?? new ServerConfig();
            GD.Print($"[server] {FILE_NAME} loaded");
            return config;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[server] failed to load {FILE_NAME}: {e.Message}");
            return null;
        }
    }
}
