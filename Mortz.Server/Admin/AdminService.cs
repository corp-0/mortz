using Mortz.Core.Admin;
using Mortz.Core.Net;
using Mortz.Core.Net.Admin;
using Mortz.Core.Net.Lobby;
using Mortz.Core.Net.Match;
using Mortz.Server.Phases;
using Mortz.Server.Players;
using Serilog;
using CryptoRandom = System.Security.Cryptography.RandomNumberGenerator;

namespace Mortz.Server.Admin;

/// <summary>Admin challenge, grant and privileged-command authorization.</summary>
public sealed class AdminService(
    ServerStateKeys keys,
    IServerLink link,
    ServerClock clock,
    ICurrentPhase phase,
    ILogger log,
    string password)
    :
        IHandle<Player, AdminAuthRequestMsg>,
        IHandle<Player, AdminProofMsg>,
        IDisposable
{
    private const string LOBBY_ONLY = "Admin authentication is only available in the lobby.";

    private readonly ServerStateKey<AdminSession> _session = keys.Claim<AdminSession>();
    private readonly AdminAuthenticator _crypto = new(password);

    public void Handle(Player sender, in AdminAuthRequestMsg message)
    {
        if (phase.Kind != ServerPhaseKind.LOBBY)
        {
            link.Send(sender.PeerId, new AdminStateMsg(IsAdmin(sender), LOBBY_ONLY));
            return;
        }

        byte[] nonce = CryptoRandom.GetBytes(AdminCrypto.NONCE_BYTES);
        AdminChallengeResult result = _crypto.Begin(sender.State(_session), clock.Ms, nonce,
            out byte[] challenge);
        if (result == AdminChallengeResult.STARTED)
        {
            link.Send(sender.PeerId, new AdminChallengeMsg(challenge));
            return;
        }

        string status = result switch
        {
            AdminChallengeResult.DISABLED => "Admin authentication is disabled on this server.",
            AdminChallengeResult.RATE_LIMITED => "Too many admin attempts. Try again later.",
            _ => "Admin authentication failed.",
        };
        link.Send(sender.PeerId, new AdminStateMsg(false, status));
    }

    public void Handle(Player sender, in AdminProofMsg message)
    {
        if (phase.Kind != ServerPhaseKind.LOBBY)
        {
            link.Send(sender.PeerId, new AdminStateMsg(IsAdmin(sender), LOBBY_ONLY));
            return;
        }

        AdminProofResult result = _crypto.Verify(sender.State(_session), sender.PeerId, clock.Ms,
            message.Proof);
        bool accepted = result == AdminProofResult.ACCEPTED;
        string status = accepted
            ? "Admin access granted."
            : result switch
            {
                AdminProofResult.EXPIRED => "Admin challenge expired. Run /admin again.",
                AdminProofResult.NO_CHALLENGE => "No admin challenge is active. Run /admin again.",
                AdminProofResult.DISABLED => "Admin authentication is disabled on this server.",
                _ => "Admin authentication failed.",
            };
        link.Send(sender.PeerId, new AdminStateMsg(accepted, status));
        if (accepted)
            log.Information("player {PeerId} authenticated as admin", sender.PeerId);
    }

    public bool IsAdmin(Player player) => _crypto.IsAdmin(player.State(_session));

    public bool Authorize(Player player, in LobbyMapUpdateMsg message) =>
        Authorize(player, message.Sequence, SetLobbyMapAction.ACTION,
            SetLobbyMapAction.SignablePayload(message.MapId), message.Tag);

    public bool Authorize(Player player, in LobbyModeUpdateMsg message) =>
        Authorize(player, message.Sequence, SetLobbyModeAction.ACTION,
            SetLobbyModeAction.SignablePayload(message.ModeId), message.Tag);

    public bool Authorize(Player player, in LobbyRulesUpdateMsg message) =>
        Authorize(player, message.Sequence, ReplaceLobbyRulesAction.ACTION,
            ReplaceLobbyRulesAction.SignablePayload(message.Config), message.Tag);

    public bool Authorize(Player player, in EndMatchRequestMsg message) =>
        Authorize(player, message.Sequence, EndMatchAction.ACTION,
            EndMatchAction.SignablePayload(), message.Tag);

    private bool Authorize(Player player, ulong sequence, byte action,
        ReadOnlySpan<byte> payload, ReadOnlySpan<byte> tag) =>
        _crypto.VerifyCommand(player.State(_session), player.PeerId, sequence, action, payload, tag);

    /// <summary>Zeroes the password; per-player key material is disposed with each AdminSession.</summary>
    public void Dispose() => _crypto.Dispose();
}
