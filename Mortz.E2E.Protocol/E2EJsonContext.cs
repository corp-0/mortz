using System.Text.Json.Serialization;

namespace Mortz.E2E.Protocol;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(E2ERequest))]
[JsonSerializable(typeof(E2EResponse))]
[JsonSerializable(typeof(E2EEvent))]
[JsonSerializable(typeof(E2EMessage))]
public sealed partial class E2EJsonContext : JsonSerializerContext;
