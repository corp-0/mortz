namespace Mortz.Core.Chat.Commands;

/// <summary>
/// A chat command: one instance is created per invocation, binds its arguments,
/// then executes against the context. Concrete commands are declared with
/// <see cref="ChatCommandAttribute"/> and registered by generated code.
/// </summary>
public abstract class ChatCommand<TContext>
{
    /// <summary>Validates arguments and captures them in fields. On rejection the
    /// error (usually the usage line) is echoed privately to the sender.</summary>
    public abstract bool TryBind(IReadOnlyList<string> arguments, out string error);

    public abstract void Execute(TContext context);
}
