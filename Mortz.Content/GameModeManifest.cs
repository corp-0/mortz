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
    public ModeRules Rules { get; init; } = new();
    public Physics Physics { get; init; } = new();
    public Combat Combat { get; init; } = new();

    /// <summary>The bundled modes are identified by the two rules that define their scoring.</summary>
    public bool Matches(MatchConfig current) =>
        Rules.Teams == current.Rules.Teams && Rules.WinCondition == current.Rules.WinCondition;
}

[TomlModel]
public sealed record RulesetManifest
{
    public ModeRules Rules { get; init; } = new();
    public Physics Physics { get; init; } = new();
    public Combat Combat { get; init; } = new();
}
