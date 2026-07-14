namespace Mortz.Core.Net.Messages;

/// <summary>Low-rate, unreliable packed position/velocity corrections for all
/// live shells. Lifecycle remains reliable, so losing this only delays a
/// cosmetic correction.</summary>
[NetMessage(NetChannel.UNRELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct MortarCorrectionMsg(int Tick, byte[] States);
