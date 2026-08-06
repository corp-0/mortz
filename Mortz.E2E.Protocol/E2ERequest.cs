using System.Text.Json.Serialization;

namespace Mortz.E2E.Protocol;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$request")]
[JsonDerivedType(typeof(PingRequest), "ping")]
[JsonDerivedType(typeof(ShutdownRequest), "shutdown")]
[JsonDerivedType(typeof(ServerStateRequest), "server_state")]
[JsonDerivedType(typeof(MatchSetupRequest), "match_setup")]
[JsonDerivedType(typeof(PlacePlayerRequest), "place_player")]
[JsonDerivedType(typeof(DamagePlayerRequest), "damage_player")]
[JsonDerivedType(typeof(SetReadyRequest), "set_ready")]
[JsonDerivedType(typeof(RunInputPlanRequest), "run_input_plan")]
[JsonDerivedType(typeof(ClientStateRequest), "client_state")]
public abstract record E2ERequest(Guid Id);
