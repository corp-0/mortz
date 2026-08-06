using System.Globalization;
using Mortz.Core.Match;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Ui;

namespace Mortz.Server.Settings;

public static class LobbySettingsDiff
{
    public static LobbySettingDelta[] Between(MatchConfig before, MatchConfig after)
    {
        List<LobbySettingDelta> deltas = [];

        AddDeltas(
            deltas,
            ModeRulesUiMetadata.Categories,
            before.Rules,
            after.Rules);

        AddDeltas(
            deltas,
            PhysicsUiMetadata.Categories,
            before.Physics,
            after.Physics);

        AddDeltas(
            deltas,
            CombatUiMetadata.Categories,
            before.Combat,
            after.Combat);

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
