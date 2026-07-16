using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Mortz.Net.Gen;

/// <summary>
/// Generates ordered, reflection-free UI metadata from inline [UiCategory]
/// section markers and [UiProperty] members.
/// </summary>
[Generator]
public sealed class UiMetadataGenerator : IIncrementalGenerator
{
    private const string CATEGORY_ATTRIBUTE = "Mortz.Core.Ui.UiCategoryAttribute";
    private const string PROPERTY_ATTRIBUTE = "Mortz.Core.Ui.UiPropertyAttribute";

    private static readonly DiagnosticDescriptor _duplicateCategory = new(
        "MZ1001", "Duplicate UI category",
        "UI category '{0}' is declared more than once on type '{1}'",
        "Mortz.UI", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _missingCategory = new(
        "MZ1002", "UI property has no category",
        "[UiProperty] property '{0}' must follow a [UiCategory] marker in the same declaration file",
        "Mortz.UI", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _emptyCategory = new(
        "MZ1003", "UI category does not start with a property",
        "[UiCategory] on '{0}' must be paired with [UiProperty] on the same property",
        "Mortz.UI", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _invalidProperty = new(
        "MZ1004", "UI property cannot be bound",
        "Property '{0}' must be a public, non-static property with public get and set accessors",
        "Mortz.UI", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private sealed record CategoryModel(int Order, string DisplayName, Location Location);

    private sealed record PropertyModel(
        string Name,
        string DisplayName,
        int CategoryOrder,
        string Type);

    private sealed record ConfigModel(
        string Namespace,
        string TypeName,
        string FullyQualifiedType,
        ImmutableArray<CategoryModel> Categories,
        ImmutableArray<PropertyModel> Properties,
        ImmutableArray<Diagnostic> Diagnostics);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<ImmutableArray<ConfigModel>> configs = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                PROPERTY_ATTRIBUTE,
                static (node, _) => node is PropertyDeclarationSyntax,
                static (ctx, _) => Extract(((IPropertySymbol)ctx.TargetSymbol).ContainingType))
            .Collect();

        context.RegisterSourceOutput(configs, static (spc, models) => Emit(spc, models));
    }

    private static ConfigModel Extract(INamedTypeSymbol type)
    {
        ImmutableArray<Diagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        ImmutableArray<CategoryModel>.Builder categories = ImmutableArray.CreateBuilder<CategoryModel>();
        ImmutableArray<PropertyModel>.Builder properties = ImmutableArray.CreateBuilder<PropertyModel>();
        var categoryNames = new HashSet<string>(StringComparer.Ordinal);

        string? sourceFile = null;
        CategoryModel? currentCategory = null;
        IPropertySymbol[] orderedProperties = type.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(property => property.Locations.Any(location => location.IsInSource))
            .OrderBy(property => SourceFile(property), StringComparer.Ordinal)
            .ThenBy(property => SourceLocation(property).SourceSpan.Start)
            .ToArray();

        foreach (IPropertySymbol property in orderedProperties)
        {
            Location location = SourceLocation(property);
            string propertyFile = location.SourceTree?.FilePath ?? string.Empty;
            if (!StringComparer.Ordinal.Equals(sourceFile, propertyFile))
            {
                sourceFile = propertyFile;
                currentCategory = null;
            }

            AttributeData? categoryAttribute = FindAttribute(property, CATEGORY_ATTRIBUTE);
            AttributeData? propertyAttribute = FindAttribute(property, PROPERTY_ATTRIBUTE);
            if (categoryAttribute != null)
            {
                string displayName = StringArgument(categoryAttribute, 0);
                Location categoryLocation = AttributeLocation(categoryAttribute, location);
                currentCategory = new CategoryModel(categories.Count, displayName, categoryLocation);
                categories.Add(currentCategory);
                if (!categoryNames.Add(displayName))
                {
                    diagnostics.Add(Diagnostic.Create(
                        _duplicateCategory, categoryLocation, displayName, type.Name));
                }
                if (propertyAttribute == null)
                    diagnostics.Add(Diagnostic.Create(_emptyCategory, categoryLocation, property.Name));
            }

            if (propertyAttribute == null)
                continue;

            Location propertyLocation = AttributeLocation(propertyAttribute, location);
            if (currentCategory == null)
            {
                diagnostics.Add(Diagnostic.Create(_missingCategory, propertyLocation, property.Name));
                continue;
            }
            if (property.IsStatic
                || property.DeclaredAccessibility != Accessibility.Public
                || property.GetMethod?.DeclaredAccessibility != Accessibility.Public
                || property.SetMethod?.DeclaredAccessibility != Accessibility.Public)
            {
                diagnostics.Add(Diagnostic.Create(_invalidProperty, propertyLocation, property.Name));
                continue;
            }

            properties.Add(new PropertyModel(
                property.Name,
                StringArgument(propertyAttribute, 0),
                currentCategory.Order,
                property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }

        return new ConfigModel(
            type.ContainingNamespace.ToDisplayString(),
            type.Name,
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            categories.ToImmutable(),
            properties.ToImmutable(),
            diagnostics.ToImmutable());
    }

    private static void Emit(SourceProductionContext spc, ImmutableArray<ConfigModel> models)
    {
        foreach (IGrouping<string, ConfigModel> group in models.GroupBy(
                     model => model.FullyQualifiedType, StringComparer.Ordinal))
        {
            ConfigModel model = group.First();
            foreach (Diagnostic diagnostic in model.Diagnostics)
                spc.ReportDiagnostic(diagnostic);
            if (!model.Diagnostics.IsEmpty)
                continue;

            EmitModel(spc, model);
        }
    }

    private static void EmitModel(SourceProductionContext spc, ConfigModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated by Mortz.Net.Gen/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        if (model.Namespace.Length > 0)
        {
            sb.AppendLine($"namespace {model.Namespace};");
            sb.AppendLine();
        }
        sb.AppendLine($"public static class {model.TypeName}UiMetadata");
        sb.AppendLine("{");
        sb.AppendLine($"    public static global::System.Collections.Generic.IReadOnlyList<global::Mortz.Core.Ui.UiCategoryDescriptor<{model.FullyQualifiedType}>> Categories {{ get; }} =");
        sb.AppendLine($"        global::System.Array.AsReadOnly(new global::Mortz.Core.Ui.UiCategoryDescriptor<{model.FullyQualifiedType}>[]");
        sb.AppendLine("        {");
        foreach (CategoryModel category in model.Categories)
        {
            sb.AppendLine($"            new global::Mortz.Core.Ui.UiCategoryDescriptor<{model.FullyQualifiedType}>(");
            sb.AppendLine($"                {Literal(category.DisplayName)},");
            sb.AppendLine($"                global::System.Array.AsReadOnly<global::Mortz.Core.Ui.IUiPropertyDescriptor<{model.FullyQualifiedType}>>(new global::Mortz.Core.Ui.IUiPropertyDescriptor<{model.FullyQualifiedType}>[]");
            sb.AppendLine("                {");
            foreach (PropertyModel property in model.Properties.Where(
                         property => property.CategoryOrder == category.Order))
            {
                sb.AppendLine($"                    new global::Mortz.Core.Ui.UiPropertyDescriptor<{model.FullyQualifiedType}, {property.Type}>(");
                sb.AppendLine($"                        {Literal(property.Name)},");
                sb.AppendLine($"                        {Literal(property.DisplayName)},");
                sb.AppendLine($"                        static model => model.{property.Name},");
                sb.AppendLine($"                        static (model, value) => model.{property.Name} = value),");
            }
            sb.AppendLine("                })),");
        }
        sb.AppendLine("        });");
        sb.AppendLine("}");

        spc.AddSource(
            $"{HintName(model.FullyQualifiedType)}UiMetadata.g.cs",
            SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static AttributeData? FindAttribute(IPropertySymbol property, string metadataName) =>
        property.GetAttributes().FirstOrDefault(attribute =>
            attribute.AttributeClass?.ToDisplayString() == metadataName);

    private static string StringArgument(AttributeData attribute, int index) =>
        (string?)attribute.ConstructorArguments[index].Value ?? string.Empty;

    private static Location SourceLocation(IPropertySymbol property) =>
        property.Locations.First(location => location.IsInSource);

    private static string SourceFile(IPropertySymbol property) =>
        SourceLocation(property).SourceTree?.FilePath ?? string.Empty;

    private static Location AttributeLocation(AttributeData attribute, Location fallback) =>
        attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? fallback;

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    private static string HintName(string typeName)
    {
        var sb = new StringBuilder(typeName.Length);
        foreach (char character in typeName)
            sb.Append(char.IsLetterOrDigit(character) ? character : '_');
        return sb.ToString();
    }
}
