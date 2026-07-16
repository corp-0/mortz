using System.Text;
using Mortz.Core.Net;

namespace Mortz.Core.Chat.Commands;

/// <summary>Parses "/name args..." into a freshly created, bound command
/// instance. Register calls are generated from [ChatCommand] classes.</summary>
public sealed class ChatCommandRegistry<TContext>
{
    private sealed record Entry(ChatCommandMetadata Metadata, Func<ChatCommand<TContext>> CreateCommand);

    private readonly Dictionary<ChatCommandName, Entry> _entriesByName = [];
    private readonly List<Entry> _entries = [];

    public IEnumerable<ChatCommandMetadata> Commands => _entries.Select(entry => entry.Metadata);

    public void Register(ChatCommandMetadata metadata, Func<ChatCommand<TContext>> createCommand)
    {
        var entry = new Entry(metadata, createCommand);
        AddName(metadata.Name, entry);
        foreach (ChatCommandName alias in metadata.Aliases)
            AddName(alias, entry);
        _entries.Add(entry);
    }

    public bool TryParse(string? input, out ChatCommand<TContext>? command, out string error)
    {
        command = null;
        if (!TryTokenize(input, out ChatCommandName name, out string[] arguments, out error))
            return false;
        if (!_entriesByName.TryGetValue(name, out Entry? entry))
        {
            error = $"Unknown command '/{name}'. Try /help.";
            return false;
        }
        ChatCommand<TContext> bound = entry.CreateCommand();
        if (!bound.TryBind(arguments, out error))
            return false;
        command = bound;
        return true;
    }

    private void AddName(ChatCommandName name, Entry entry)
    {
        if (!_entriesByName.TryAdd(name, entry))
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
