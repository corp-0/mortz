namespace Mortz.Content;

public interface ITomlValidatedModel
{
    void ValidateToml(string source, List<ContentDiagnostic> diagnostics);
}
