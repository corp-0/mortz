using Godot;
using Mortz.Client.Menus;
using Mortz.Client.Ui;
using Mortz.Core.Match.Configuration;
using Xunit;

namespace Mortz.Tests.Client.Menus;

[Collection(nameof(MortzGodotCollection))]
public class ConfigVariantSheetTests
{
    [Fact]
    public void LobbySceneBindsAllThreeFamiliesAndOnlyTheirActiveSettings()
    {
        LobbySettingsPanel panel = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/UI/Menus/LobbySettingsPanel.tscn").Instantiate<LobbySettingsPanel>();
        try
        {
            panel.OnReady();
            AssertSheet(panel, "_victoryRulesSheet", ConfigVariantSheet.RuleFamily.END_CONDITION,
                typeof(ScoreTargetRules), 1);
            AssertSheet(panel, "_objectiveRulesSheet", ConfigVariantSheet.RuleFamily.OBJECTIVE,
                typeof(NoObjectiveRules), 0);
            AssertSheet(panel, "_respawnRulesSheet", ConfigVariantSheet.RuleFamily.RESPAWN,
                typeof(FixedRespawnRules), 1);
        }
        finally
        {
            panel.OnExitTree();
            panel.Free();
        }
    }

    [Fact]
    public void SelectingAVariantReplacesItsSettingsAndRebindingUsesTheTypedModel()
    {
        ConfigVariantSheet sheet = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/UI/Controls/ConfigVariantSheet.tscn").Instantiate<ConfigVariantSheet>();
        try
        {
            sheet._Ready();
            EndConditionRules? changed = null;
            sheet.Build<EndConditionRules>(new ScoreTargetRules { Target = 12 }, rules => changed = rules);
            OptionButton picker = sheet.Get("_variant").AsGodotObject() as OptionButton ?? throw new InvalidOperationException();
            UiPropertySheet properties = sheet.Get("_properties").AsGodotObject() as UiPropertySheet ?? throw new InvalidOperationException();
            int timeLimit = Enumerable.Range(0, EndConditionRulesMetadata.Variants.Count)
                .Single(index => EndConditionRulesMetadata.Variants[index].Id == "time_limit");
            picker.EmitSignal(OptionButton.SignalName.ItemSelected, timeLimit);
            Assert.IsType<TimeLimitRules>(changed);
            Assert.Equal(typeof(TimeLimitRules), properties.BoundModelType);
            Assert.Equal(1, properties.ControlCount);
            sheet.SetEditable(false);
            Assert.True(picker.Disabled);

            sheet.Family = ConfigVariantSheet.RuleFamily.RESPAWN;
            FixedRespawnRules respawn = new() { Delay = 4.25f };
            sheet.Build<RespawnRules>(respawn, _ => { });
            Assert.Same(respawn, sheet.Rules);
            Assert.Equal(typeof(FixedRespawnRules), properties.BoundModelType);
            Assert.Equal(1, picker.ItemCount);
        }
        finally
        {
            sheet._ExitTree();
            sheet.Free();
        }
    }

    private static void AssertSheet(LobbySettingsPanel panel, string property,
        ConfigVariantSheet.RuleFamily family, Type model, int controls)
    {
        ConfigVariantSheet sheet = Assert.IsType<ConfigVariantSheet>(panel.Get(property).AsGodotObject());
        Assert.Equal(family, sheet.Family);
        Assert.Equal(model, sheet.Rules.GetType());
        UiPropertySheet properties = Assert.IsType<UiPropertySheet>(sheet.Get("_properties").AsGodotObject());
        Assert.Equal(model, properties.BoundModelType);
        Assert.Equal(controls, properties.ControlCount);
        Label heading = Assert.Single(sheet.GetChildren().OfType<Label>());
        Assert.Equal(family switch
        {
            ConfigVariantSheet.RuleFamily.END_CONDITION => "Victory",
            ConfigVariantSheet.RuleFamily.OBJECTIVE => "Objective",
            ConfigVariantSheet.RuleFamily.RESPAWN => "Respawn",
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        }, heading.Text);
    }
}
