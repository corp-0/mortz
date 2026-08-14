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
/// what phase it is in; the state itself lives on SettingsService.</summary>
public sealed class LobbySettingsService(
    SettingsService settings,
    AdminService admin,
    ChatService chat,
    LobbyService lobby,
    ILogger log)
    :
        IHandle<Player, LobbyRulesUpdateMsg>,
        IHandle<Player, LobbyModeUpdateMsg>,
        IHandle<Player, LobbyMapUpdateMsg>
{
    public void Handle(Player sender, in LobbyRulesUpdateMsg message)
    {
        if (!admin.Authorize(sender, message.Sequence, AdminAction.SET_LOBBY_RULES,
                message.Config, message.Tag) ||
            !settings.TrySetRules(message.Config, out LobbySettingDelta[] deltas))
        {
            settings.SendTo(sender.PeerId);
            return;
        }

        settings.Broadcast();
        log.Information("lobby rules updated by admin {PeerId}", sender.PeerId);
        chat.AnnounceSettings(sender.Name, deltas);
        lobby.ApplyTeamsRule(settings.Config.Rules.Teams);
    }

    public void Handle(Player sender, in LobbyModeUpdateMsg message)
    {
        byte[] payload = Encoding.UTF8.GetBytes(message.ModeId);
        if (!admin.Authorize(sender, message.Sequence, AdminAction.SET_LOBBY_MODE,
                payload, message.Tag) ||
            !settings.TrySetMode(message.ModeId, out LobbySettingDelta[] deltas))
        {
            settings.SendTo(sender.PeerId);
            return;
        }

        settings.Broadcast();
        log.Information("lobby mode set to '{Mode}' by admin {PeerId}", settings.ModeName,
            sender.PeerId);
        chat.AnnounceSettings(sender.Name, deltas);
        lobby.ApplyTeamsRule(settings.Config.Rules.Teams);
    }

    /// <summary>No teams reseat here: a map change cannot move the Teams rule.</summary>
    public void Handle(Player sender, in LobbyMapUpdateMsg message)
    {
        byte[] payload = Encoding.UTF8.GetBytes(message.MapId);
        if (!admin.Authorize(sender, message.Sequence, AdminAction.SET_LOBBY_MAP,
                payload, message.Tag) ||
            !settings.TrySetMap(message.MapId, out LobbySettingDelta[] deltas))
        {
            settings.SendTo(sender.PeerId);
            return;
        }

        settings.Broadcast();
        log.Information("lobby map changed to '{MapId}' by admin {PeerId}", settings.Map.MapId,
            sender.PeerId);
        chat.AnnounceSettings(sender.Name, deltas);
    }
}
