namespace Mortz.Core.Match.Configuration;

public interface IConfigSection
{
    ConfigKeyResult TryApplyKey(string key, object? value, out string error);

    void Clamp();
}
