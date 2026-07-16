using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Mortz.Net.Gen;

/// <summary>
/// Companion to ChatCommandGenerator: a concrete ChatCommand&lt;TContext&gt;
/// subclass without [ChatCommand] would silently never register, so make it a
/// build error instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ChatCommandAnalyzer : DiagnosticAnalyzer
{
    private const string ATTRIBUTE_NAME = "Mortz.Core.Chat.Commands.ChatCommandAttribute";
    private const string BASE_TYPE = "Mortz.Core.Chat.Commands.ChatCommand<TContext>";

    private static readonly DiagnosticDescriptor _missingAttribute = new(
        "MZ2004", "Chat command class is missing [ChatCommand]",
        "'{0}' derives from ChatCommand<TContext> but has no [ChatCommand] attribute, so it will never be registered",
        "Mortz.Chat", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(_missingAttribute);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var symbol = (INamedTypeSymbol)context.Symbol;
        if (symbol.TypeKind != TypeKind.Class || symbol.IsAbstract)
            return;

        bool isCommand = false;
        for (INamedTypeSymbol? baseType = symbol.BaseType; baseType != null; baseType = baseType.BaseType)
        {
            if (baseType.IsGenericType && baseType.OriginalDefinition.ToDisplayString() == BASE_TYPE)
            {
                isCommand = true;
                break;
            }
        }
        if (!isCommand)
            return;

        bool hasAttribute = symbol.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString() == ATTRIBUTE_NAME);
        if (!hasAttribute)
        {
            context.ReportDiagnostic(Diagnostic.Create(_missingAttribute,
                symbol.Locations.FirstOrDefault() ?? Location.None, symbol.Name));
        }
    }
}
