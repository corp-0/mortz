namespace Mortz.Core.Match.Configuration;

public sealed partial class MatchConfig
{
    [ConfigSection]
    public ModeRules Rules { get; init; } = new();

    [ConfigSection]
    public Physics Physics { get; init; } = new();

    [ConfigSection]
    public Combat Combat { get; init; } = new();

}
