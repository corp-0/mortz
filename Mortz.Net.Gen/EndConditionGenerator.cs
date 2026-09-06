using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Mortz.Net.Gen;

[Generator]
public sealed class EndConditionGenerator : IIncrementalGenerator
{
    private const string ATTRIBUTE =
        "Mortz.Server.Match.Modes.EndConditionForAttribute";
    private const string BASE =
        "Mortz.Server.Match.Modes.EndCondition";
    private const string RULES_BASE = "Mortz.Core.Match.Configuration.EndConditionRules";

    private static readonly DiagnosticDescriptor _invalidHandler = new(
        "MZ5001", "Invalid end condition",
        "End condition '{0}' cannot be registered: {1}",
        "Mortz.Match", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _duplicateRules = new(
        "MZ5002", "Duplicate end condition",
        "End-condition rules '{0}' are handled by more than one handler",
        "Mortz.Match", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private sealed record Handler(
        string Type,
        string RulesType,
        string RulesName,
        Location Location,
        ImmutableArray<Diagnostic> Diagnostics);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<ImmutableArray<Handler>> strategies = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ATTRIBUTE,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => Extract(ctx))
            .Collect();

        context.RegisterSourceOutput(strategies, Emit);
    }

    private static Handler Extract(GeneratorAttributeSyntaxContext context)
    {
        var type = (INamedTypeSymbol)context.TargetSymbol;
        AttributeData attribute = context.Attributes[0];
        var rulesType = attribute.ConstructorArguments[0].Value as INamedTypeSymbol;
        ImmutableArray<Diagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        Location location = context.TargetNode.GetLocation();

        if (type.IsAbstract || !DerivesFrom(type, BASE))
        {
            diagnostics.Add(Diagnostic.Create(
                _invalidHandler, location, type.Name,
                $"it must be a concrete {BASE}"));
        }
        else if (rulesType == null)
        {
            diagnostics.Add(Diagnostic.Create(
                _invalidHandler, location, type.Name,
                "the attribute must name a end-condition rules type"));
        }
        else if (!DerivesFrom(rulesType, RULES_BASE))
        {
            diagnostics.Add(Diagnostic.Create(
                _invalidHandler, location, type.Name,
                $"{rulesType.Name} does not derive from {RULES_BASE}"));
        }
        else if (!type.InstanceConstructors.Any(constructor =>
                     constructor.DeclaredAccessibility != Accessibility.Private &&
                     constructor.Parameters.Length == 1 &&
                     SymbolEqualityComparer.Default.Equals(
                         constructor.Parameters[0].Type, rulesType)))
        {
            diagnostics.Add(Diagnostic.Create(
                _invalidHandler, location, type.Name,
                $"it needs an accessible constructor accepting {rulesType.Name}"));
        }

        return new Handler(
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            rulesType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "",
            rulesType?.ToDisplayString() ?? "",
            location,
            diagnostics.ToImmutable());
    }

    private static bool DerivesFrom(INamedTypeSymbol type, string expected)
    {
        for (INamedTypeSymbol? current = type.BaseType;
             current != null;
             current = current.BaseType)
        {
            if (current.ToDisplayString() == expected)
                return true;
        }
        return false;
    }

    private static void Emit(
        SourceProductionContext context,
        ImmutableArray<Handler> strategies)
    {
        foreach (Diagnostic diagnostic in strategies.SelectMany(handler => handler.Diagnostics))
        {
            context.ReportDiagnostic(diagnostic);
        }

        foreach (IGrouping<string, Handler> duplicate in strategies
                     .Where(handler => handler.RulesType.Length > 0)
                     .GroupBy(handler => handler.RulesType, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            foreach (Handler handler in duplicate)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    _duplicateRules, handler.Location, handler.RulesName));
            }
        }

        if (strategies.IsDefaultOrEmpty ||
            strategies.Any(handler => !handler.Diagnostics.IsEmpty) ||
            strategies.Where(handler => handler.RulesType.Length > 0)
                .GroupBy(handler => handler.RulesType, StringComparer.Ordinal)
                .Any(group => group.Count() > 1))
        {
            return;
        }

        var source = new StringBuilder();
        source.AppendLine("// <auto-generated by Mortz.Net.Gen/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.AppendLine("namespace Mortz.Server.Match.Modes;");
        source.AppendLine();
        source.AppendLine("public abstract partial class EndCondition");
        source.AppendLine("{");
        source.AppendLine("    public static EndCondition Create(global::Mortz.Core.Match.Configuration.EndConditionRules rules) =>");
        source.AppendLine("        rules switch");
        source.AppendLine("        {");
        foreach (Handler handler in strategies.OrderBy(item => item.RulesType, StringComparer.Ordinal))
        {
            source.AppendLine(
                $"            {handler.RulesType} typed => new {handler.Type}(typed),");
        }
        source.AppendLine("            _ => throw new global::System.ArgumentOutOfRangeException(nameof(rules), rules, \"Unsupported end-condition rules.\"),");
        source.AppendLine("        };");
        source.AppendLine("}");

        context.AddSource("EndCondition.Factory.g.cs",
            SourceText.From(source.ToString(), Encoding.UTF8));
    }
}
