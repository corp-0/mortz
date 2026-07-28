using Godot;
using Mortz.Core.Net;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Syntax;

namespace Mortz.Server.Hosting;

/// <summary>
/// Dedicated-box settings, read from server.toml in the working directory so
/// a rented box works without CLI args; a CLI flag overrides its file
/// counterpart. Machine settings only: match rules come from --ruleset or
/// --mode and are replicated to clients, while nothing in here ever leaves
/// the server.
/// </summary>
public sealed class ServerConfig
{
    private const string FILE_NAME = "server.toml";

    public const string DEFAULT_NAME = "Mortz Server";

    /// <summary>Players who send /admin with this password get live control
    /// over the server settings in the lobby. Empty = no admin access.</summary>
    public string AdminPassword { get; set; } = "";

    /// <summary>What the server calls itself in server browsers.</summary>
    public string Name { get; set; } = DEFAULT_NAME;

    /// <summary>Blank or all-invisible names fall back to the default.</summary>
    public static string SanitizeName(string? value)
    {
        string name = SafeName.Sanitize(value, NetConfig.MAX_SERVER_NAME_LENGTH);
        return name.Length == 0 ? DEFAULT_NAME : name;
    }

    /// <summary>Defaults when the file doesn't exist; null when it exists but
    /// is unusable, so a host never silently runs without their settings.</summary>
    public static ServerConfig? Load()
    {
        if (!File.Exists(FILE_NAME))
            return new ServerConfig();
        try
        {
            ServerConfig? config = Parse(File.ReadAllText(FILE_NAME));
            if (config != null)
                GD.Print($"[server] {FILE_NAME} loaded");
            return config;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PrintErr($"[server] failed to load {FILE_NAME}: {e.Message}");
            return null;
        }
    }

    private static ServerConfig? Parse(string text)
    {
        DocumentSyntax syntax = Toml.Parse(text, FILE_NAME);
        if (syntax.HasErrors)
        {
            foreach (DiagnosticMessage diagnostic in syntax.Diagnostics)
            {
                GD.PrintErr($"[server] {FILE_NAME}: {diagnostic.Message}");
            }
            return null;
        }

        TomlTable table = Toml.ToModel(syntax);
        ServerConfig config = new();
        bool valid = true;
        foreach (string key in table.Keys)
        {
            switch (key)
            {
                case "name" when table[key] is string name:
                    config.Name = name;
                    break;
                case "admin_password" when table[key] is string password:
                    config.AdminPassword = password;
                    break;
                case "name" or "admin_password":
                    GD.PrintErr($"[server] {FILE_NAME}: '{key}' must be a string");
                    valid = false;
                    break;
                default:
                    GD.PushWarning($"[server] {FILE_NAME}: unknown key '{key}'");
                    break;
            }
        }
        return valid ? config : null;
    }
}
