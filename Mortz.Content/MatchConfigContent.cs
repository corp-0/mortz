using Mortz.Core.Match.Configuration;

namespace Mortz.Content;

public static class MatchConfigContent
{
    public static void Validate(ModeRules modeRules, string source,
        List<ContentDiagnostic> diagnostics)
    {
        foreach (string error in ModeComposition.Errors(modeRules))
        {
            TomlModel.Error(diagnostics, source, error);
        }
    }
}
