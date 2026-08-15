using System.Globalization;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Ui;

namespace Mortz.Server.Settings;

public readonly record struct LobbySettingDelta(string Name, string Before, string After);

public static class LobbySettingsDiff
{
    public static LobbySettingDelta[] Between(MatchConfig before, MatchConfig after)
        => Between(before.ToSnapshot(), after.ToSnapshot());

    public static LobbySettingDelta[] Between(
        MatchConfigSnapshot before,
        MatchConfigSnapshot after)
    {
        MatchConfig beforeDraft = before.ToMutable();
        MatchConfig afterDraft = after.ToMutable();
        List<LobbySettingDelta> deltas = [];

        AddDeltas(
            deltas,
            ModeRulesUiMetadata.Categories,
            beforeDraft.Rules,
            afterDraft.Rules);

        VictoryRuleDescriptor beforeVictory = VictoryRulesMetadata.For(beforeDraft.Rules.Victory);
        VictoryRuleDescriptor afterVictory = VictoryRulesMetadata.For(afterDraft.Rules.Victory);
        if (beforeVictory.RulesType == afterVictory.RulesType)
        {
            AddDeltas(
                deltas,
                afterVictory.Categories,
                beforeDraft.Rules.Victory,
                afterDraft.Rules.Victory);
        }
        else
        {
            deltas.Add(new LobbySettingDelta(
                "Victory Rule",
                beforeVictory.DisplayName,
                afterVictory.DisplayName));
        }

        AddDeltas(
            deltas,
            PhysicsUiMetadata.Categories,
            beforeDraft.Physics,
            afterDraft.Physics);

        AddDeltas(
            deltas,
            CombatUiMetadata.Categories,
            beforeDraft.Combat,
            afterDraft.Combat);

        return [.. deltas];
    }

    private static void AddDeltas(
        List<LobbySettingDelta> deltas,
        IReadOnlyList<UiCategoryDescriptor> categories,
        object before,
        object after)
    {
        IEnumerable<IUiPropertyDescriptor> descriptors =
            categories.SelectMany(category => category.Properties);

        foreach (IUiPropertyDescriptor descriptor in descriptors)
        {
            object? oldValue = descriptor.GetValue(before);
            object? newValue = descriptor.GetValue(after);

            if (Equals(oldValue, newValue))
                continue;

            deltas.Add(new LobbySettingDelta(
                descriptor.DisplayName,
                FormatValue(oldValue),
                FormatValue(newValue)));
        }
    }

    private static string FormatValue(object? value) => value switch
    {
        bool enabled => enabled ? "On" : "Off",
        float number => number.ToString(
            "0.###",
            CultureInfo.InvariantCulture),
        double number => number.ToString(
            "0.###",
            CultureInfo.InvariantCulture),
        Enum item => item.ToString().Replace('_', ' '),
        IFormattable item => item.ToString(
            null,
            CultureInfo.InvariantCulture),
        null => "",
        _ => value.ToString() ?? "",
    };
}
