using Mortz.Core.Match.Configuration;
using Mortz.Protocol.Configuration;

[assembly: ConfigCodecs(typeof(MatchConfig))]

namespace Mortz.Protocol.Configuration;

[AttributeUsage(AttributeTargets.Assembly)]
public class ConfigCodecsAttribute(Type root) : Attribute
{
    public Type Root { get; } = root;
}
