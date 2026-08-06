namespace Mortz.E2E.Protocol;

public sealed record ProtocolRegistration(
    string Family,
    string Discriminator,
    Type Type,
    Type? ResponseType);
