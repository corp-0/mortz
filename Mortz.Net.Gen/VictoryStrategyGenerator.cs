using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Mortz.Net.Gen;

[Generator]
public sealed class VictoryStrategyGenerator : IIncrementalGenerator
{
    private const string ATTRIBUTE =
        "Mortz.Server.Match.Scoring.WinConditions.VictoryRuleStrategyAttribute";
    private const string BASE =
        "Mortz.Server.Match.Scoring.WinConditions.WinConditionStrategy";
    private const string RULES_BASE = "Mortz.Core.Match.Configuration.VictoryRules";

    private static readonly DiagnosticDescriptor _invalidStrategy = new(
        "MZ5001", "Invalid victory strategy",
        "Victory strategy '{0}' cannot be registered: {1}",
        "Mortz.Match", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _duplicateRules = new(
        "MZ5002", "Duplicate victory strategy",
        "Victory rules '{0}' are handled by more than one strategy",
        "Mortz.Match", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private sealed record Strategy(
        string Type,
        string RulesType,
        string RulesName,
        Location Location,
        ImmutableArray<Diagnostic> Diagnostics);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<ImmutableArray<Strategy>> strategies = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ATTRIBUTE,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => Extract(ctx))
            .Collect();

        context.RegisterSourceOutput(strategies, Emit);
    }

    private static Strategy Extract(GeneratorAttributeSyntaxContext context)
    {
        var type = (INamedTypeSymbol)context.TargetSymbol;
        AttributeData attribute = context.Attributes[0];
        var rulesType = attribute.ConstructorArguments[0].Value as INamedTypeSymbol;
        ImmutableArray<Diagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        Location location = context.TargetNode.GetLocation();

        if (type.IsAbstract || !DerivesFrom(type, BASE))
        {
            diagnostics.Add(Diagnostic.Create(
                _invalidStrategy, location, type.Name,
                $"it must be a concrete {BASE}"));
        }
        else if (rulesType == null)
        {
            diagnostics.Add(Diagnostic.Create(
                _invalidStrategy, location, type.Name,
                "the attribute must name a victory-rules type"));
        }
        else if (!DerivesFrom(rulesType, RULES_BASE))
        {
            diagnostics.Add(Diagnostic.Create(
                _invalidStrategy, location, type.Name,
                $"{rulesType.Name} does not derive from {RULES_BASE}"));
        }
        else if (!type.InstanceConstructors.Any(constructor =>
                     constructor.DeclaredAccessibility != Accessibility.Private &&
                     constructor.Parameters.Length == 1 &&
                     SymbolEqualityComparer.Default.Equals(
                         constructor.Parameters[0].Type, rulesType)))
        {
            diagnostics.Add(Diagnostic.Create(
                _invalidStrategy, location, type.Name,
                $"it needs an accessible constructor accepting {rulesType.Name}"));
        }

        return new Strategy(
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
        ImmutableArray<Strategy> strategies)
    {
        foreach (Diagnostic diagnostic in strategies.SelectMany(strategy => strategy.Diagnostics))
        {
            context.ReportDiagnostic(diagnostic);
        }

        foreach (IGrouping<string, Strategy> duplicate in strategies
                     .Where(strategy => strategy.RulesType.Length > 0)
                     .GroupBy(strategy => strategy.RulesType, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            foreach (Strategy strategy in duplicate)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    _duplicateRules, strategy.Location, strategy.RulesName));
            }
        }

        if (strategies.IsDefaultOrEmpty ||
            strategies.Any(strategy => !strategy.Diagnostics.IsEmpty) ||
            strategies.Where(strategy => strategy.RulesType.Length > 0)
                .GroupBy(strategy => strategy.RulesType, StringComparer.Ordinal)
                .Any(group => group.Count() > 1))
        {
            return;
        }

        var source = new StringBuilder();
        source.AppendLine("// <auto-generated by Mortz.Net.Gen/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.AppendLine("namespace Mortz.Server.Match.Scoring.WinConditions;");
        source.AppendLine();
        source.AppendLine("public abstract partial class WinConditionStrategy");
        source.AppendLine("{");
        source.AppendLine("    public static WinConditionStrategy Create(global::Mortz.Core.Match.Configuration.VictoryRules rules) =>");
        source.AppendLine("        rules switch");
        source.AppendLine("        {");
        foreach (Strategy strategy in strategies.OrderBy(item => item.RulesType, StringComparer.Ordinal))
        {
            source.AppendLine(
                $"            {strategy.RulesType} typed => new {strategy.Type}(typed),");
        }
        source.AppendLine("            _ => throw new global::System.ArgumentOutOfRangeException(nameof(rules), rules, \"Unsupported victory rules.\"),");
        source.AppendLine("        };");
        source.AppendLine("}");

        context.AddSource("WinConditionStrategy.Factory.g.cs",
            SourceText.From(source.ToString(), Encoding.UTF8));
    }
}
