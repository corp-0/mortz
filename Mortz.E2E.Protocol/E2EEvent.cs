using System.Text.Json.Serialization;

namespace Mortz.E2E.Protocol;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$event")]
[JsonDerivedType(typeof(ProcessReadyEvent), "process_ready")]
[JsonDerivedType(typeof(ServerListeningEvent), "server_listening")]
[JsonDerivedType(typeof(PhaseChangedEvent), "phase_changed")]
[JsonDerivedType(typeof(PlayerJoinedEvent), "player_joined")]
[JsonDerivedType(typeof(PlayerLeftEvent), "player_left")]
[JsonDerivedType(typeof(MatchTickEvent), "match_tick")]
[JsonDerivedType(typeof(EliminationEvent), "elimination")]
[JsonDerivedType(typeof(MatchEndedEvent), "match_ended")]
[JsonDerivedType(typeof(ConnectedEvent), "connected")]
[JsonDerivedType(typeof(ConnectionFailedEvent), "connection_failed")]
[JsonDerivedType(typeof(DisconnectedEvent), "disconnected")]
[JsonDerivedType(typeof(LobbyRosterObservedEvent), "lobby_roster_observed")]
[JsonDerivedType(typeof(MatchEnteredEvent), "match_entered")]
[JsonDerivedType(typeof(SnapshotObservedEvent), "snapshot_observed")]
[JsonDerivedType(typeof(EliminationObservedEvent), "elimination_observed")]
[JsonDerivedType(typeof(MatchEndObservedEvent), "match_end_observed")]
[JsonDerivedType(typeof(InputPlanStartedEvent), "input_plan_started")]
[JsonDerivedType(typeof(InputPlanCompletedEvent), "input_plan_completed")]
[JsonDerivedType(typeof(InputPlanCancelledEvent), "input_plan_cancelled")]
public abstract record E2EEvent;
