using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Mortz.Net.Gen;

/// <summary>
/// Registers [ChatCommand] classes at compile time: for each context type an
/// assembly declares commands against, emits a RegisterAssemblyCommands
/// extension on ChatCommandRegistry&lt;TContext&gt; carrying the metadata from
/// the attribute. Names, aliases, and duplicates are validated here so a bad
/// command fails the build, not the session.
/// </summary>
[Generator]
public sealed class ChatCommandGenerator : IIncrementalGenerator
{
    private const string ATTRIBUTE_NAME = "Mortz.Core.Chat.Commands.ChatCommandAttribute";
    private const string BASE_TYPE = "Mortz.Core.Chat.Commands.ChatCommand<TContext>";

    private static readonly DiagnosticDescriptor _invalidCommandClass = new(
        "MZ2001", "Invalid chat command class",
        "[ChatCommand] type '{0}' cannot be registered: {1}",
        "Mortz.Chat", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _invalidName = new(
        "MZ2002", "Invalid chat command name",
        "Chat command name or alias '{0}' on '{1}' may contain only ASCII letters, digits, '-' and '_'",
        "Mortz.Chat", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _duplicateName = new(
        "MZ2003", "Duplicate chat command name",
        "Chat command name or alias '{0}' is declared more than once for context '{1}'",
        "Mortz.Chat", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private sealed record CommandModel(
        string TypeFqn,
        string ContextFqn,
        string Name,
        ImmutableArray<string> Aliases,
        string Usage,
        string Description,
        bool Sensitive,
        ImmutableArray<Diagnostic> Diagnostics);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<ImmutableArray<CommandModel>> commands = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ATTRIBUTE_NAME,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => Extract(ctx))
            .Collect();

        context.RegisterSourceOutput(commands, static (spc, models) => Emit(spc, models));
    }

    private static CommandModel Extract(GeneratorAttributeSyntaxContext ctx)
    {
        var symbol = (INamedTypeSymbol)ctx.TargetSymbol;
        string typeFqn = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        ImmutableArray<Diagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        string? contextFqn = null;
        for (INamedTypeSymbol? baseType = symbol.BaseType; baseType != null; baseType = baseType.BaseType)
        {
            if (baseType.IsGenericType && baseType.OriginalDefinition.ToDisplayString() == BASE_TYPE)
            {
                contextFqn = baseType.TypeArguments[0]
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                break;
            }
        }

        string? reason = null;
        if (symbol.IsAbstract)
            reason = "it is abstract";
        else if (contextFqn == null)
            reason = "it does not derive from ChatCommand<TContext>";
        else if (!symbol.InstanceConstructors.Any(c =>
                     c.Parameters.IsEmpty && c.DeclaredAccessibility != Accessibility.Private))
            reason = "it has no accessible parameterless constructor";
        if (reason != null)
        {
            diagnostics.Add(Diagnostic.Create(_invalidCommandClass,
                ctx.TargetNode.GetLocation(), symbol.Name, reason));
        }

        AttributeData attr = ctx.Attributes[0];
        string name = Normalize((string?)attr.ConstructorArguments[0].Value);
        ImmutableArray<string> aliases =
            attr.ConstructorArguments.Length > 1 && !attr.ConstructorArguments[1].IsNull
                ? attr.ConstructorArguments[1].Values
                    .Select(v => Normalize((string?)v.Value)).ToImmutableArray()
                : ImmutableArray<string>.Empty;
        foreach (string candidate in aliases.Insert(0, name))
        {
            if (!IsValidName(candidate))
            {
                diagnostics.Add(Diagnostic.Create(_invalidName,
                    ctx.TargetNode.GetLocation(), candidate, symbol.Name));
            }
        }

        string usage = "";
        string description = "";
        bool sensitive = false;
        foreach (KeyValuePair<string, TypedConstant> named in attr.NamedArguments)
        {
            switch (named.Key)
            {
                case "Usage": usage = (string?)named.Value.Value ?? ""; break;
                case "Description": description = (string?)named.Value.Value ?? ""; break;
                case "Sensitive": sensitive = named.Value.Value is true; break;
            }
        }

        return new CommandModel(typeFqn, contextFqn ?? "", name, aliases, usage, description,
            sensitive, diagnostics.ToImmutable());
    }

    private static string Normalize(string? value) => (value ?? "").ToLowerInvariant();

    private static bool IsValidName(string value) =>
        value.Length > 0 && value.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_');

    private static void Emit(SourceProductionContext spc, ImmutableArray<CommandModel> models)
    {
        foreach (CommandModel model in models)
        {
            foreach (Diagnostic d in model.Diagnostics)
            {
                spc.ReportDiagnostic(d);
            }
        }

        CommandModel[] valid = models
            .Where(m => m.Diagnostics.IsEmpty)
            .OrderBy(m => m.ContextFqn, StringComparer.Ordinal)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToArray();
        if (valid.Length == 0)
            return;

        foreach (IGrouping<string, CommandModel> group in valid.GroupBy(m => m.ContextFqn))
        {
            var seen = new HashSet<string>();
            foreach (CommandModel m in group)
            {
                foreach (string candidate in m.Aliases.Insert(0, m.Name))
                {
                    if (!seen.Add(candidate))
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(_duplicateName, Location.None,
                            candidate, group.Key));
                        return;
                    }
                }
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated by Mortz.Net.Gen/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using Mortz.Core.Chat.Commands;");
        sb.AppendLine();
        sb.AppendLine("namespace Mortz.Core.Chat.Commands;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Registration for this assembly's [ChatCommand] classes.</summary>");
        sb.AppendLine("internal static class GeneratedChatCommands");
        sb.AppendLine("{");
        bool firstGroup = true;
        foreach (IGrouping<string, CommandModel> group in valid.GroupBy(m => m.ContextFqn))
        {
            if (!firstGroup)
                sb.AppendLine();
            firstGroup = false;
            sb.AppendLine($"    internal static void RegisterAssemblyCommands(this ChatCommandRegistry<{group.Key}> registry)");
            sb.AppendLine("    {");
            foreach (CommandModel m in group)
            {
                string aliases = m.Aliases.IsEmpty
                    ? "global::System.Array.Empty<ChatCommandName>()"
                    : $"new ChatCommandName[] {{ {string.Join(", ", m.Aliases.Select(a => $"new ChatCommandName({Quote(a)})"))} }}";
                sb.AppendLine("        registry.Register(");
                sb.AppendLine($"            new ChatCommandMetadata(new ChatCommandName({Quote(m.Name)}), {Quote(m.Usage)},");
                sb.AppendLine($"                {Quote(m.Description)}, {aliases}, {(m.Sensitive ? "true" : "false")}),");
                sb.AppendLine($"            static () => new {m.TypeFqn}());");
            }
            sb.AppendLine("    }");
        }
        sb.AppendLine("}");
        spc.AddSource("GeneratedChatCommands.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
