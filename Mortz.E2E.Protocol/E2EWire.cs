using System.Text.Json;

namespace Mortz.E2E.Protocol;

public static class E2EWire
{
    public const string STDOUT_PREFIX = "MORTZ_E2E ";

    public static string Serialize(E2ERequest request) =>
        JsonSerializer.Serialize(request, E2EJsonContext.Default.E2ERequest);

    public static E2ERequest DeserializeRequest(string line) =>
        JsonSerializer.Deserialize(line, E2EJsonContext.Default.E2ERequest)
        ?? throw new JsonException("The E2E request was null.");

    public static string Serialize(E2EMessage message) =>
        JsonSerializer.Serialize(message, E2EJsonContext.Default.E2EMessage);

    public static E2EMessage DeserializeMessage(string line) =>
        JsonSerializer.Deserialize(line, E2EJsonContext.Default.E2EMessage)
        ?? throw new JsonException("The E2E message was null.");
}
