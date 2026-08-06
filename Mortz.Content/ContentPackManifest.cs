using JetBrains.Annotations;

namespace Mortz.Content;

[TomlModel]
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed record ContentPackManifest(
    string Id,
    string Name,
    string Version,
    string Author = "",
    string Description = "",
    int LoadOrder = 0);
