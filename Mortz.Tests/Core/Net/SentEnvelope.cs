namespace Mortz.Tests.Core.Net;

/// <summary>The id and bytes one send put on the wire.</summary>
public readonly record struct SentEnvelope(ushort Id, byte[] Payload);
