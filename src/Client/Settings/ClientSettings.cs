using Mortz.Client.Servers;
using Mortz.Content;
using Mortz.Core.Net.Names;
using Mortz.Core.Sim;
using Mortz.Shared;
using Mortz.Shared.Logging;
using Serilog;

namespace Mortz.Client.Settings;

/// <summary>Player identity and saved servers.</summary>
public sealed class ClientSettings(string userDataDirectory)
{
    private static readonly ILogger _log = MortzLog.For("client");

    private const string FILE_NAME = "profile.toml";

    private readonly string _path = Path.Combine(userDataDirectory, FILE_NAME);

    public string PlayerName { get; set; } = "";
    public int? SelectedSkin { get; set; }
    public List<FavoriteServer> Favorites { get; private set; } = [];

    public bool HasIdentity =>
        PlayerNameSanitizer.Sanitize(PlayerName).Length > 0 && IsValidSkin(SelectedSkin);

    public int Skin => IsValidSkin(SelectedSkin) ? SelectedSkin!.Value : 0;

    public ClientSettings() : this(MortzUserData.Resolve()) { }

    public static ClientSettings Load() => Load(MortzUserData.Resolve());

    public static ClientSettings Load(string userDataDirectory)
    {
        ClientSettings settings = new(userDataDirectory);
        if (!File.Exists(settings._path))
            return settings;
        try
        {
            settings.Read(File.ReadAllText(settings._path));
            return settings;
        }
        catch (Exception exception)
        {
            _log.Error(exception, "unreadable {Path}, starting fresh", settings._path);
            return new ClientSettings(userDataDirectory);
        }
    }

    public void Save()
    {
        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (directory != null)
                Directory.CreateDirectory(directory);
            string temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, Write());
            File.Move(temporaryPath, _path, true);
        }
        catch (Exception exception)
        {
            _log.Error(exception, "failed to write {Path}", _path);
        }
    }

    public void SetIdentity(string name, int skin)
    {
        PlayerName = PlayerNameSanitizer.Sanitize(name);
        SelectedSkin = IsValidSkin(skin) ? skin : null;
        Save();
    }

    public static bool IsValidSkin(int? skin) =>
        skin is >= 0 and < SimConfig.SKIN_COUNT;

    public void SetFavorites(IEnumerable<FavoriteServer> favorites)
    {
        Favorites = favorites.ToList();
        Save();
    }

    private void Read(string text)
    {
        ContentReadResult<ClientProfile> result = TomlModel.Read<ClientProfile>(text, _path);
        if (result.Value is not ClientProfile profile)
            throw new InvalidDataException(string.Join("; ", result.Diagnostics.Select(x => x.Message)));

        foreach (ContentDiagnostic diagnostic in result.Diagnostics)
        {
            _log.Warning("{Diagnostic}", diagnostic);
        }

        PlayerName = profile.PlayerName;
        SelectedSkin = profile.SelectedSkin;
        Favorites = [.. profile.Favorites];
    }

    private string Write() => TomlModel.Write(new ClientProfile
    {
        PlayerName = PlayerName,
        SelectedSkin = SelectedSkin,
        Favorites = [.. Favorites],
    });
}

[TomlModel]
internal sealed class ClientProfile
{
    public string PlayerName { get; set; } = "";
    public int? SelectedSkin { get; set; }
    public FavoriteServer[] Favorites { get; set; } = [];
}
