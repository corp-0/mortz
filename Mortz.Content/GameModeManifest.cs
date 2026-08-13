using Mortz.Core.Match.Configuration;
using Combat = Mortz.Core.Match.Configuration.Combat;
using ModeRules = Mortz.Core.Match.Configuration.ModeRules;
using Physics = Mortz.Core.Match.Configuration.Physics;

namespace Mortz.Content;

[TomlModel]
public sealed record GameModeManifest
{
    public required int FormatVersion { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public string[] Identity { get; init; } =
        ["rules.teams", "rules.victory.type"];
    public ModeRules Rules { get; init; } = new();
    public Physics Physics { get; init; } = new();
    public Combat Combat { get; init; } = new();

    public bool Matches(MatchConfig current) => TomlModel.PropertiesMatch(
        this,
        new RulesetManifest
        {
            Rules = current.Rules,
            Physics = current.Physics,
            Combat = current.Combat,
        },
        Identity);
}

[TomlModel]
public sealed record RulesetManifest
{
    public ModeRules Rules { get; init; } = new();
    public Physics Physics { get; init; } = new();
    public Combat Combat { get; init; } = new();
}
