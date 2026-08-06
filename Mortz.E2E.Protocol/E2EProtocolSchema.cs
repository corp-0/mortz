using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Mortz.E2E.Protocol;

public static class E2EProtocolSchema
{
    public static IReadOnlyList<ProtocolRegistration> Registrations { get; } =
        BuildRegistrations();

    public static string Hash { get; } = BuildHash();

    private static string BuildHash()
    {
        StringBuilder schema = new();
        foreach (ProtocolRegistration registration in Registrations)
        {
            schema.Append(registration.Family).Append(':')
                .Append(registration.Discriminator).Append(':')
                .Append(Describe(registration.Type)).Append('\n');
            if (registration.ResponseType != null)
                schema.Append("response:").Append(Describe(registration.ResponseType)).Append('\n');
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(schema.ToString()));
        return Convert.ToHexStringLower(hash);
    }

    private static IReadOnlyList<ProtocolRegistration> BuildRegistrations()
    {
        List<ProtocolRegistration> registrations = [];
        AppendFamily(typeof(E2ERequest), "request", registrations);
        AppendFamily(typeof(E2EResponse), "response", registrations);
        AppendFamily(typeof(E2EEvent), "event", registrations);
        AppendFamily(typeof(E2EMessage), "message", registrations);
        return registrations
            .OrderBy(value => value.Family, StringComparer.Ordinal)
            .ThenBy(value => value.Discriminator, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AppendFamily(
        Type root,
        string family,
        List<ProtocolRegistration> registrations)
    {
        HashSet<string> discriminators = new(StringComparer.Ordinal);
        foreach (JsonDerivedTypeAttribute attribute in
                 root.GetCustomAttributes<JsonDerivedTypeAttribute>())
        {
            string discriminator = attribute.TypeDiscriminator?.ToString()
                ?? throw new InvalidOperationException($"{attribute.DerivedType.Name} has no discriminator.");
            if (!discriminators.Add(discriminator))
                throw new InvalidOperationException($"Duplicate {family} discriminator '{discriminator}'.");

            Type? response = null;
            if (family == "request")
            {
                Type[] pairs = attribute.DerivedType.GetInterfaces()
                    .Where(value => value.IsGenericType &&
                                    value.GetGenericTypeDefinition() == typeof(IE2ERequest<>))
                    .ToArray();
                if (pairs.Length != 1)
                {
                    throw new InvalidOperationException(
                        $"{attribute.DerivedType.Name} must declare exactly one response type.");
                }
                response = pairs[0].GetGenericArguments()[0];
            }

            registrations.Add(new ProtocolRegistration(
                family, discriminator, attribute.DerivedType, response));
        }
    }

    private static string Describe(Type type)
    {
        if (type.IsArray)
            return $"{Describe(type.GetElementType()!)}[]";
        if (type.IsGenericType)
        {
            string name = type.GetGenericTypeDefinition().FullName!;
            return $"{name}[{string.Join(",", type.GetGenericArguments().Select(Describe))}]";
        }
        if (type.IsEnum)
        {
            string values = string.Join(",", Enum.GetNames(type).Zip(
                Enum.GetValues(type).Cast<object>(),
                (name, value) => $"{name}={Convert.ToUInt64(value)}"));
            return $"{type.FullName}{{{values}}}";
        }
        if (type.IsPrimitive || type == typeof(string) || type == typeof(Guid) ||
            type == typeof(decimal))
            return type.FullName!;

        PropertyInfo[] properties = type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(value => value.Name, StringComparer.Ordinal)
            .ToArray();
        if (properties.Length == 0)
            return type.FullName!;
        return $"{type.FullName}{{{string.Join(",", properties.Select(
            value => $"{value.Name}:{Describe(value.PropertyType)}"))}}}";
    }
}
