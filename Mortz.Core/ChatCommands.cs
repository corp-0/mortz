using System.Text;

namespace Mortz.Core;

/// <summary>Validated, case-insensitive command name. Raw strings stop at this boundary.</summary>
public readonly record struct ChatCommandName
{
    public ChatCommandName(string value)
    {
        if (!TryNormalize(value, out string normalized))
            throw new ArgumentException("Command names may contain only ASCII letters, digits, '-' and '_'.",
                nameof(value));
        Value = normalized;
    }

    public string Value { get; }
    public override string ToString() => Value;

    internal static bool TryCreate(string value, out ChatCommandName name)
    {
        if (!TryNormalize(value, out string normalized))
        {
            name = default;
            return false;
        }
        name = new ChatCommandName(normalized);
        return true;
    }

    private static bool TryNormalize(string value, out string normalized)
    {
        normalized = value.ToLowerInvariant();
        return normalized.Length > 0 &&
            normalized.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    }
}

public readonly record struct ChatCommandMetadata(
    ChatCommandName Name,
    string Usage,
    string Description,
    IReadOnlyList<ChatCommandName> Aliases);

/// <summary>A typed command value produced after parsing user text.</summary>
public interface IChatCommand;

/// <summary>Commands whose arguments must never take the ordinary chat wire
/// path or appear in history.</summary>
public interface ISensitiveChatCommand : IChatCommand;

public interface IChatCommandDefinition<TCommandBase>
    where TCommandBase : class, IChatCommand
{
    ChatCommandMetadata Metadata { get; }
    internal bool TryBind(IReadOnlyList<string> arguments, out TCommandBase? command,
        out string error);
}

/// <summary>Typed argument binder for one command.</summary>
public abstract class ChatCommandDefinition<TCommandBase, TCommand> :
    IChatCommandDefinition<TCommandBase>
    where TCommandBase : class, IChatCommand
    where TCommand : class, TCommandBase
{
    protected ChatCommandDefinition(ChatCommandName name, string usage, string description,
        params ChatCommandName[] aliases) =>
        Metadata = new ChatCommandMetadata(name, usage, description, aliases);

    public ChatCommandMetadata Metadata { get; }

    public abstract bool TryBind(IReadOnlyList<string> arguments, out TCommand? command,
        out string error);

    bool IChatCommandDefinition<TCommandBase>.TryBind(IReadOnlyList<string> arguments,
        out TCommandBase? command, out string error)
    {
        bool bound = TryBind(arguments, out TCommand? typed, out error);
        command = typed;
        return bound;
    }
}

/// <summary>Lexes text and binds it to a typed command record.</summary>
public sealed class ChatCommandRegistry<TCommand>
    where TCommand : class, IChatCommand
{
    private readonly Dictionary<ChatCommandName, IChatCommandDefinition<TCommand>>
        _definitionsByName = [];
    private readonly List<IChatCommandDefinition<TCommand>> _definitions = [];

    public IReadOnlyList<IChatCommandDefinition<TCommand>> Definitions => _definitions;

    public void Register(IChatCommandDefinition<TCommand> definition)
    {
        AddName(definition.Metadata.Name, definition);
        foreach (ChatCommandName alias in definition.Metadata.Aliases)
            AddName(alias, definition);
        _definitions.Add(definition);
    }

    public bool TryParse(string? input, out TCommand? command, out string error)
    {
        command = null;
        if (!TryTokenize(input, out ChatCommandName name, out string[] arguments, out error))
            return false;
        if (!_definitionsByName.TryGetValue(name,
                out IChatCommandDefinition<TCommand>? definition))
        {
            error = $"Unknown command '/{name}'. Try /help.";
            return false;
        }
        return definition.TryBind(arguments, out command, out error);
    }

    private void AddName(ChatCommandName name, IChatCommandDefinition<TCommand> definition)
    {
        if (!_definitionsByName.TryAdd(name, definition))
            throw new InvalidOperationException($"Chat command or alias '{name}' is already registered.");
    }

    private static bool TryTokenize(string? input, out ChatCommandName name,
        out string[] arguments, out string error)
    {
        name = default;
        arguments = [];
        error = "";
        if (string.IsNullOrWhiteSpace(input) || input[0] != '/')
        {
            error = "Not a command.";
            return false;
        }

        ReadOnlySpan<char> source = input.AsSpan(1).Trim();
        if (source.IsEmpty)
        {
            error = "Enter a command after '/'.";
            return false;
        }

        var tokens = new List<string>();
        var token = new StringBuilder();
        bool quoted = false;
        bool escaping = false;
        bool tokenStarted = false;
        foreach (char c in source)
        {
            if (escaping)
            {
                token.Append(c);
                escaping = false;
                tokenStarted = true;
                continue;
            }
            if (c == '\\')
            {
                escaping = true;
                tokenStarted = true;
                continue;
            }
            if (c == '"')
            {
                quoted = !quoted;
                tokenStarted = true;
                continue;
            }
            if (!quoted && char.IsWhiteSpace(c))
            {
                if (tokenStarted)
                {
                    tokens.Add(token.ToString());
                    token.Clear();
                    tokenStarted = false;
                    if (tokens.Count > NetConfig.MAX_CHAT_COMMAND_ARGS + 1)
                    {
                        error = "Too many command arguments.";
                        return false;
                    }
                }
                continue;
            }
            token.Append(c);
            tokenStarted = true;
        }

        if (escaping || quoted)
        {
            error = escaping ? "Command ends with an escape character." :
                "Unterminated quoted argument.";
            return false;
        }
        if (tokenStarted)
            tokens.Add(token.ToString());
        if (tokens.Count == 0)
        {
            error = "Enter a command after '/'.";
            return false;
        }
        if (tokens.Count > NetConfig.MAX_CHAT_COMMAND_ARGS + 1)
        {
            error = "Too many command arguments.";
            return false;
        }
        if (!ChatCommandName.TryCreate(tokens[0], out name))
        {
            error = "Invalid command name.";
            return false;
        }
        arguments = tokens.Skip(1).ToArray();
        return true;
    }
}
