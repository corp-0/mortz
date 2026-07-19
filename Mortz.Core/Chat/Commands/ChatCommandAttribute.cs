using JetBrains.Annotations;

namespace Mortz.Core.Chat.Commands;

/// <summary>
/// Declares a chat command. The source generator finds every decorated
/// <see cref="ChatCommand{TContext}"/> subclass and emits a
/// RegisterAssemblyCommands extension per context; names, aliases, and
/// duplicates are validated at compile time.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ChatCommandAttribute(string name, params string[] aliases) : Attribute
{
    public string Name { get; } = name;
    public string[] Aliases { get; } = aliases;
    [UsedImplicitly] public string Usage { get; set; } = "";
    [UsedImplicitly] public string Description { get; set; } = "";

    /// <summary>Arguments must never take the ordinary chat wire path or appear
    /// in history (passwords and similar).</summary>
    [UsedImplicitly] public bool Sensitive { get; set; }
}
