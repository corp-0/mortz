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
    LobbySession lobby,
    Action<LobbyUpdate?> applyLobbyUpdate,
    ILogger log)
    :
        IHandle<Player, LobbyRulesUpdateMsg>,
        IHandle<Player, LobbyModeUpdateMsg>,
        IHandle<Player, LobbyMapUpdateMsg>
{
    public void Handle(Player sender, in LobbyRulesUpdateMsg message)
    {
        if (!admin.Authorize(sender, message))
        {
            Commit(sender,
                new SettingsMutationResult.Rejected(SettingsRejectReason.UNAUTHORIZED));
            return;
        }

        Commit(sender, settings.SetRules(message.Config));
    }

    public void Handle(Player sender, in LobbyModeUpdateMsg message)
    {
        if (!admin.Authorize(sender, message))
        {
            Commit(sender,
                new SettingsMutationResult.Rejected(SettingsRejectReason.UNAUTHORIZED));
            return;
        }

        Commit(sender, settings.SetMode(message.ModeId));
    }

    /// <summary>No teams reseat here: a map change cannot move the Teams rule.</summary>
    public void Handle(Player sender, in LobbyMapUpdateMsg message)
    {
        if (!admin.Authorize(sender, message))
        {
            Commit(sender,
                new SettingsMutationResult.Rejected(SettingsRejectReason.UNAUTHORIZED));
            return;
        }

        Commit(sender, settings.SetMap(message.MapId));
    }

    private void Commit(Player sender, SettingsMutationResult result)
    {
        if (result is SettingsMutationResult.Rejected rejected)
        {
            log.Information("lobby settings mutation rejected for admin {PeerId}: {Reason}",
                sender.PeerId, rejected.Reason);
            settings.SendTo(sender.PeerId);
            return;
        }

        SettingsChange change = ((SettingsMutationResult.Applied)result).Change;
        settings.Broadcast();
        log.Information("lobby settings updated by admin {PeerId}: {@Change}", sender.PeerId,
            change);
        chat.AnnounceSettings(sender.Name, change.Deltas);

        if (change.TeamsRuleChanged)
            applyLobbyUpdate(lobby.SetTeamsEnabled(change.After.Config.Rules.Teams));
    }
}
