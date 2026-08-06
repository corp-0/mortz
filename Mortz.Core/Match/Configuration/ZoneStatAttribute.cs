namespace Mortz.Core.Match.Configuration;

/// <summary>Lets zones modify a match property for the entity currently inside
/// them. The property still needs its normal [MatchRule] contract.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ZoneStatAttribute : Attribute;
