using System.Text.Json.Serialization;

namespace Mortz.E2E.Protocol;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$response")]
[JsonDerivedType(typeof(PongResponse), "pong")]
[JsonDerivedType(typeof(ShutdownStartedResponse), "shutdown_started")]
[JsonDerivedType(typeof(ServerStateResponse), "server_state")]
[JsonDerivedType(typeof(MatchSetupResponse), "match_setup")]
[JsonDerivedType(typeof(PlayerPlacedResponse), "player_placed")]
[JsonDerivedType(typeof(PlayerDamagedResponse), "player_damaged")]
[JsonDerivedType(typeof(ReadySentResponse), "ready_sent")]
[JsonDerivedType(typeof(InputPlanAcceptedResponse), "input_plan_accepted")]
[JsonDerivedType(typeof(ClientStateResponse), "client_state")]
[JsonDerivedType(typeof(CommandFailedResponse), "command_failed")]
public abstract record E2EResponse(Guid Id);
