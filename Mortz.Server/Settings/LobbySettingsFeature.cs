using System.Text;
using Mortz.Core.Admin;
using Mortz.Core.Net;
using Mortz.Core.Net.Lobby;
using Mortz.Server.Admin;
using Mortz.Server.Chat;
using Mortz.Server.Lobby;
using Mortz.Server.Players;
using Serilog;

namespace Mortz.Server.Settings;

/// <summary>The lobby's settings mutations. Lobby lifetime, so no handler asks
/// what phase it is in; the state itself lives on SettingsFeature.</summary>
public sealed class LobbySettingsFeature :
    IHandle<Player, LobbyRulesUpdateMsg>,
    IHandle<Player, LobbyModeUpdateMsg>,
    IHandle<Player, LobbyMapUpdateMsg>
{
    private readonly SettingsFeature _settings;
    private readonly AdminFeature _admin;
    private readonly ChatFeature _chat;
    private readonly LobbyFeature _lobby;
    private readonly ILogger _log;

    public LobbySettingsFeature(SettingsFeature settings, AdminFeature admin, ChatFeature chat,
        LobbyFeature lobby, ILogger log)
    {
        _settings = settings;
        _admin = admin;
        _chat = chat;
        _lobby = lobby;
        _log = log;
    }

    public void Handle(Player sender, in LobbyRulesUpdateMsg message)
    {
        if (!_admin.Authorize(sender, message.Sequence, AdminAction.SET_LOBBY_RULES,
                message.Config, message.Tag) ||
            !_settings.TrySetRules(message.Config, out LobbySettingDelta[] deltas))
        {
            _settings.SendTo(sender.PeerId);
            return;
        }

        _settings.Broadcast();
        _log.Information("lobby rules updated by admin {PeerId}", sender.PeerId);
        _chat.AnnounceSettings(sender.Name, deltas);
        _lobby.ApplyTeamsRule(_settings.Config.Rules.Teams);
    }

    public void Handle(Player sender, in LobbyModeUpdateMsg message)
    {
        byte[] payload = Encoding.UTF8.GetBytes(message.ModeId);
        if (!_admin.Authorize(sender, message.Sequence, AdminAction.SET_LOBBY_MODE,
                payload, message.Tag) ||
            !_settings.TrySetMode(message.ModeId, out LobbySettingDelta[] deltas))
        {
            _settings.SendTo(sender.PeerId);
            return;
        }

        _settings.Broadcast();
        _log.Information("lobby mode set to '{Mode}' by admin {PeerId}", _settings.ModeName,
            sender.PeerId);
        _chat.AnnounceSettings(sender.Name, deltas);
        _lobby.ApplyTeamsRule(_settings.Config.Rules.Teams);
    }

    /// <summary>No teams reseat here: a map change cannot move the Teams rule.</summary>
    public void Handle(Player sender, in LobbyMapUpdateMsg message)
    {
        byte[] payload = Encoding.UTF8.GetBytes(message.MapId);
        if (!_admin.Authorize(sender, message.Sequence, AdminAction.SET_LOBBY_MAP,
                payload, message.Tag) ||
            !_settings.TrySetMap(message.MapId, out LobbySettingDelta[] deltas))
        {
            _settings.SendTo(sender.PeerId);
            return;
        }

        _settings.Broadcast();
        _log.Information("lobby map changed to '{MapId}' by admin {PeerId}", _settings.Map.MapId,
            sender.PeerId);
        _chat.AnnounceSettings(sender.Name, deltas);
    }
}
