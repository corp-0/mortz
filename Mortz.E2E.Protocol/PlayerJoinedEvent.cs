using Mortz.Core.Identity;

namespace Mortz.E2E.Protocol;

public record PlayerJoinedEvent(
    int PeerId,
    string Name,
    E2EPhase Phase,
    VerifiedAccount? Account = null) : E2EEvent;
