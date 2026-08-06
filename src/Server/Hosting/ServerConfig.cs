using Mortz.Content;
using Mortz.Core.Net;
using Mortz.Core.Net.Names;
using Mortz.Shared.Logging;
using Serilog;

namespace Mortz.Server.Hosting;

/// <summary>Machine-local dedicated server settings.</summary>
[TomlModel]
public sealed class ServerConfig
{
    private static readonly ILogger _log = MortzLog.For("server");

    private const string FILE_NAME = "server.toml";

    public const string DEFAULT_NAME = "Mortz Server";

    /// <summary>Players who send /admin with this password get live control
    /// over the server settings in the lobby. Empty = no admin access.</summary>
    public string AdminPassword { get; set; } = "";

    public string Name { get; set; } = DEFAULT_NAME;

    public bool AllowJoinInProgress { get; set; } = true;

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
                _log.Information("{File} loaded", FILE_NAME);
            return config;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Error(e, "failed to load {File}", FILE_NAME);
            return null;
        }
    }

    private static ServerConfig? Parse(string text)
    {
        ContentReadResult<ServerConfig> result = TomlModel.Read<ServerConfig>(text, FILE_NAME);
        foreach (ContentDiagnostic diagnostic in result.Diagnostics)
        {
            if (diagnostic.Severity == ContentDiagnosticSeverity.ERROR)
                _log.Error("{File}: {Message}", FILE_NAME, diagnostic.Message);
            else
                _log.Warning("{File}: {Message}", FILE_NAME, diagnostic.Message);
        }
        return result.Value;
    }
}
