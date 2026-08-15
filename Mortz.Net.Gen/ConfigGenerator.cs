using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Mortz.Net.Gen;

[Generator]
public sealed class ConfigGenerator : IIncrementalGenerator
{
    private const string PLAYER_STAT_ATTRIBUTE = "Mortz.Core.Match.Configuration.PlayerStatAttribute";
    private const string MATCH_RULE_ATTRIBUTE = "Mortz.Core.Match.Configuration.MatchRuleAttribute";
    private const string CONFIG_VALUE_ATTRIBUTE = "Mortz.Core.Match.Configuration.ConfigValueAttribute";
    private const string CONFIG_SECTION_ATTRIBUTE = "Mortz.Core.Match.Configuration.ConfigSectionAttribute";
    private const string ZONE_STAT_ATTRIBUTE = "Mortz.Core.Match.Configuration.ZoneStatAttribute";
    private const string VICTORY_CASE_ATTRIBUTE =
        "Mortz.Core.Match.Configuration.VictoryRuleCaseAttribute";
    private const string TICK_RATE_TYPE = "Mortz.Core.Sim.SimConfig";
    // MatchConfig must expose each [PlayerStat] owner type as a property named after that type.
    private const string MATCH_CONFIG = "global::Mortz.Core.Match.Configuration.MatchConfig";

    private static readonly DiagnosticDescriptor _invalidRange = new(
        "MZ3001", "Invalid clamp range",
        "Property '{0}' declares min {1} > max {2}",
        "Mortz.Config", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _defaultOutsideRange = new(
        "MZ3002", "Default outside clamp range",
        "Property '{0}' has default {1}, outside its clamp range [{2}, {3}]",
        "Mortz.Config", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _typeMismatch = new(
        "MZ3003", "Attribute options incompatible with property type",
        "Property '{0}': {1}",
        "Mortz.Config", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _convertOverflow = new(
        "MZ3004", "Converted stat overflows its target type",
        "Property '{0}': max {1} converts to {2} ticks, which does not fit a {3}",
        "Mortz.Config", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _nameCollision = new(
        "MZ3005", "Stat name collision",
        "Two [PlayerStat] properties resolve to the stat name '{0}'; use statsName to disambiguate",
        "Mortz.Config", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _misuse = new(
        "MZ3006", "Attribute misuse",
        "Property '{0}': {1}",
        "Mortz.Config", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _untrackedValue = new(
        "MZ3007", "Configuration value is not projected",
        "Property '{0}.{1}' must declare a configuration field attribute",
        "Mortz.Config", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _unsupportedProjection = new(
        "MZ3008", "Unsupported nested configuration projection",
        "Property '{0}': {1}",
        "Mortz.Config", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _multipleNestedValues = new(
        "MZ3009", "Unsupported nested configuration wire layout",
        "Configuration section '{0}' has more than one nested value; its wire layout must be explicitly designed",
        "Mortz.Config", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private enum FieldKind
    {
        STAT,
        RULE,
        ZONE_STAT,
        VALUE,
    }

    // Mirrors Mortz.Core.Match.Configuration.StatConvert; the generator cannot reference it.
    private enum Convert
    {
        RAW = 0,
        TICKS_INT = 1,
        TICKS_BYTE = 2,
        TICKS_USHORT = 3,
        COUNT_BYTE = 4,
    }

    private sealed record FieldModel(
        FieldKind Kind,
        string Owner,
        string OwnerName,
        string OwnerNamespace,
        string Name,
        string Type,
        bool IsEnum,
        ImmutableArray<string> EnumMembers,
        string StatName,
        float Min,
        float Max,
        Convert Conv,
        double? Default,
        string FilePath,
        int SpanStart,
        string? SnapshotBase,
        string? SnapshotType,
        string? ProjectionType,
        ImmutableArray<string> UntrackedProperties,
        ImmutableArray<Diagnostic> Diagnostics);

    private sealed record SectionModel(
        string Name,
        string Type,
        string Namespace,
        string FilePath,
        int SpanStart,
        ImmutableArray<string> UntrackedProperties);

    private sealed record VictoryCase(string Id, string DisplayName, INamedTypeSymbol Type);

    private sealed record VictoryUnion(
        INamedTypeSymbol Type,
        ImmutableArray<VictoryCase> Cases);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<ImmutableArray<FieldModel>> stats = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                PLAYER_STAT_ATTRIBUTE,
                static (node, _) => node is PropertyDeclarationSyntax,
                static (ctx, _) => Extract(ctx, FieldKind.STAT))
            .Collect();
        IncrementalValueProvider<ImmutableArray<FieldModel>> rules = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                MATCH_RULE_ATTRIBUTE,
                static (node, _) => node is PropertyDeclarationSyntax,
                static (ctx, _) => Extract(ctx, FieldKind.RULE))
            .Collect();
        IncrementalValueProvider<ImmutableArray<FieldModel>> zoneStats = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ZONE_STAT_ATTRIBUTE,
                static (node, _) => node is PropertyDeclarationSyntax,
                static (ctx, _) => Extract(ctx, FieldKind.ZONE_STAT))
            .Collect();
        IncrementalValueProvider<ImmutableArray<FieldModel>> values = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CONFIG_VALUE_ATTRIBUTE,
                static (node, _) => node is PropertyDeclarationSyntax,
                static (ctx, _) => Extract(ctx, FieldKind.VALUE))
            .Collect();
        IncrementalValueProvider<ImmutableArray<SectionModel>> sections = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CONFIG_SECTION_ATTRIBUTE,
                static (node, _) => node is PropertyDeclarationSyntax,
                static (ctx, _) => ExtractSection(ctx))
            .Collect();

        IncrementalValueProvider<((((ImmutableArray<FieldModel> Left, ImmutableArray<FieldModel> Right) Left, ImmutableArray<FieldModel> Right) Left, ImmutableArray<FieldModel> Right) Left, ImmutableArray<SectionModel> Right)> combined = stats.Combine(rules).Combine(zoneStats).Combine(values).Combine(sections);
        context.RegisterSourceOutput(combined, static (spc, models) =>
            Emit(spc,
                models.Left.Left.Left.Left,
                models.Left.Left.Left.Right,
                models.Left.Left.Right,
                models.Left.Right,
                models.Right));

        IncrementalValueProvider<ImmutableArray<VictoryUnion>> victoryUnions =
            context.SyntaxProvider.ForAttributeWithMetadataName(
                    VICTORY_CASE_ATTRIBUTE,
                    static (node, _) => node is TypeDeclarationSyntax,
                    static (ctx, _) => ExtractVictoryUnion((INamedTypeSymbol)ctx.TargetSymbol))
                .Collect();
        context.RegisterSourceOutput(victoryUnions, EmitVictoryUnions);
    }

    private static VictoryUnion ExtractVictoryUnion(INamedTypeSymbol type)
    {
        ImmutableArray<VictoryCase>.Builder cases = ImmutableArray.CreateBuilder<VictoryCase>();
        foreach (AttributeData attribute in type.GetAttributes().Where(candidate =>
                     candidate.AttributeClass?.ToDisplayString() == VICTORY_CASE_ATTRIBUTE))
        {
            if (attribute.ConstructorArguments.Length == 3 &&
                attribute.ConstructorArguments[0].Value is string id &&
                attribute.ConstructorArguments[1].Value is string displayName &&
                attribute.ConstructorArguments[2].Value is INamedTypeSymbol member)
            {
                cases.Add(new VictoryCase(id, displayName, member));
            }
        }
        return new VictoryUnion(type, cases.ToImmutable());
    }

    private static void EmitVictoryUnions(SourceProductionContext spc,
        ImmutableArray<VictoryUnion> unions)
    {
        foreach (IGrouping<string, VictoryUnion> group in unions.GroupBy(
                     union => union.Type.ToDisplayString(), StringComparer.Ordinal))
        {
            VictoryUnion union = group.First();
            string source = EmitVictoryUnion(union);
            spc.AddSource($"{HintName(union.Type.ToDisplayString())}Metadata.g.cs",
                SourceText.From(source, Encoding.UTF8));
        }
    }

    private static string EmitVictoryUnion(VictoryUnion union)
    {
        string ns = union.Type.ContainingNamespace.ToDisplayString();
        string baseType = union.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated by Mortz.Net.Gen/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public static class {union.Type.Name}Metadata");
        sb.AppendLine("{");
        sb.AppendLine("    public static global::System.Collections.Generic.IReadOnlyList<global::Mortz.Core.Match.Configuration.VictoryRuleDescriptor> Variants { get; } =");
        sb.AppendLine("        global::System.Array.AsReadOnly(new global::Mortz.Core.Match.Configuration.VictoryRuleDescriptor[]");
        sb.AppendLine("        {");
        foreach (VictoryCase item in union.Cases)
        {
            string type = item.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            sb.AppendLine("            new global::Mortz.Core.Match.Configuration.VictoryRuleDescriptor(");
            sb.AppendLine($"                {Literal(item.Id)}, {Literal(item.DisplayName)}, typeof({type}),");
            sb.AppendLine($"                {type}UiMetadata.Categories,");
            sb.AppendLine($"                static () => new {type}(),");
            sb.AppendLine($"                static rules => (({type})rules).Clamp(),");
            sb.AppendLine($"                static rules => {type}.Serialize(({type})rules),");
            sb.AppendLine($"                static data => {type}.Deserialize(data)),");
        }
        sb.AppendLine("        });");
        sb.AppendLine();
        sb.AppendLine($"    public static global::Mortz.Core.Match.Configuration.VictoryRuleDescriptor For({baseType} rules)");
        sb.AppendLine("    {");
        sb.AppendLine("        foreach (global::Mortz.Core.Match.Configuration.VictoryRuleDescriptor variant in Variants)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (variant.RulesType == rules.GetType())");
        sb.AppendLine("                return variant;");
        sb.AppendLine("        }");
        sb.AppendLine("        throw new global::System.NotSupportedException($\"Unknown victory rules '{rules.GetType().Name}'.\");");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    internal static void Clamp({baseType} rules) => For(rules).Clamp(rules);");
        sb.AppendLine();
        sb.AppendLine($"    internal static byte[] Serialize({baseType} rules)");
        sb.AppendLine("    {");
        sb.AppendLine("        global::Mortz.Core.Match.Configuration.VictoryRuleDescriptor variant = For(rules);");
        sb.AppendLine("        byte[] payload = variant.Serialize(rules);");
        sb.AppendLine("        using global::System.IO.MemoryStream stream = new();");
        sb.AppendLine("        using global::System.IO.BinaryWriter writer = new(stream);");
        sb.AppendLine("        writer.Write(variant.Id);");
        sb.AppendLine("        writer.Write(payload.Length);");
        sb.AppendLine("        writer.Write(payload);");
        sb.AppendLine("        return stream.ToArray();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    internal static {baseType} Deserialize(byte[] data)");
        sb.AppendLine("    {");
        sb.AppendLine("        using global::System.IO.MemoryStream stream = new(data, writable: false);");
        sb.AppendLine("        using global::System.IO.BinaryReader reader = new(stream);");
        sb.AppendLine("        string id = reader.ReadString();");
        sb.AppendLine("        int length = reader.ReadInt32();");
        sb.AppendLine("        if (length < 0 || length != stream.Length - stream.Position)");
        sb.AppendLine("            throw new global::System.IO.InvalidDataException(\"Invalid victory-rules payload length.\");");
        sb.AppendLine("        byte[] payload = reader.ReadBytes(length);");
        sb.AppendLine("        foreach (global::Mortz.Core.Match.Configuration.VictoryRuleDescriptor variant in Variants)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (global::System.StringComparer.Ordinal.Equals(variant.Id, id))");
        sb.AppendLine("                return variant.Deserialize(payload);");
        sb.AppendLine("        }");
        sb.AppendLine("        throw new global::System.IO.InvalidDataException($\"Unknown victory rules '{id}'.\");");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static FieldModel Extract(GeneratorAttributeSyntaxContext ctx, FieldKind kind)
    {
        var symbol = (IPropertySymbol)ctx.TargetSymbol;
        var node = (PropertyDeclarationSyntax)ctx.TargetNode;
        string name = symbol.Name;
        string type = symbol.Type.ToDisplayString();
        Location location = node.GetLocation();
        ImmutableArray<Diagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        ImmutableArray<string> untracked = symbol.ContainingType.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(property => property is
            {
                IsStatic: false,
                DeclaredAccessibility: Accessibility.Public,
                GetMethod.DeclaredAccessibility: Accessibility.Public,
            } && property.SetMethod != null &&
                !property.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() is
                    PLAYER_STAT_ATTRIBUTE or MATCH_RULE_ATTRIBUTE or CONFIG_VALUE_ATTRIBUTE))
            .Select(property => property.Name)
            .ToImmutableArray();

        bool publicGetSet = symbol is
        {
            IsStatic: false,
            DeclaredAccessibility: Accessibility.Public,
            GetMethod.DeclaredAccessibility: Accessibility.Public,
            SetMethod.DeclaredAccessibility: Accessibility.Public,
        };
        if (!publicGetSet)
            diagnostics.Add(Diagnostic.Create(_misuse, location, name,
                "the property must be public, non-static, with public get and set"));

        AttributeData attr = ctx.Attributes[0];
        INamedTypeSymbol? snapshotType = null;
        INamedTypeSymbol? projectionType = null;
        if (kind == FieldKind.VALUE)
        {
            if (attr.ConstructorArguments.Length != 2 ||
                attr.ConstructorArguments[0].Value is not INamedTypeSymbol snapshot ||
                attr.ConstructorArguments[1].Value is not INamedTypeSymbol projection)
            {
                diagnostics.Add(Diagnostic.Create(_unsupportedProjection, location, name,
                    "[ConfigValue] must name snapshot and projection types"));
            }
            else
            {
                snapshotType = snapshot;
                projectionType = projection;
                ValidateProjection(ctx, symbol, snapshot, projection, location, diagnostics);
            }
        }
        AttributeData? rangeContract = kind switch
        {
            FieldKind.ZONE_STAT => symbol.GetAttributes().FirstOrDefault(candidate =>
                candidate.AttributeClass?.ToDisplayString() == MATCH_RULE_ATTRIBUTE),
            FieldKind.VALUE => null,
            _ => attr,
        };
        if (kind == FieldKind.ZONE_STAT && rangeContract == null)
            diagnostics.Add(Diagnostic.Create(_misuse, location, name,
                "[ZoneStat] requires [MatchRule] on the same property"));
        float min = rangeContract == null
            ? float.NaN
            : (float)(rangeContract.ConstructorArguments[0].Value ?? float.NaN);
        float max = rangeContract == null
            ? float.NaN
            : (float)(rangeContract.ConstructorArguments[1].Value ?? float.NaN);
        Convert conv = kind == FieldKind.STAT
            ? (Convert)(byte)(attr.ConstructorArguments[2].Value ?? (byte)0)
            : Convert.RAW;
        string? statsName = kind == FieldKind.STAT
            ? (string?)attr.ConstructorArguments[3].Value
            : null;

        bool isEnum = symbol.Type.TypeKind == TypeKind.Enum;
        ImmutableArray<string> enumMembers = isEnum
            ? symbol.Type.GetMembers().OfType<IFieldSymbol>()
                .Where(f => f.HasConstantValue)
                .Select(f => f.Name)
                .ToImmutableArray()
            : ImmutableArray<string>.Empty;
        ValidateType(kind, name, type, isEnum, conv, min, max, location, diagnostics);

        if (!float.IsNaN(min) && !float.IsNaN(max) && min > max)
            diagnostics.Add(Diagnostic.Create(_invalidRange, location, name, min, max));

        double? defaultValue = ConstantDefault(ctx, node);
        if (defaultValue is double d && !float.IsNaN(min) && !float.IsNaN(max) && (d < min || d > max))
            diagnostics.Add(Diagnostic.Create(_defaultOutsideRange, location, name, d, min, max));

        if (conv is Convert.TICKS_BYTE or Convert.TICKS_USHORT && !float.IsNaN(max) &&
            TickRate(ctx) is int tickRate)
        {
            double maxTicks = (double)max * tickRate;
            double capacity = conv == Convert.TICKS_BYTE ? byte.MaxValue : ushort.MaxValue;
            if (maxTicks > capacity)
                diagnostics.Add(Diagnostic.Create(_convertOverflow, location, name, max, maxTicks,
                    conv == Convert.TICKS_BYTE ? "byte" : "ushort"));
        }

        return new FieldModel(kind,
            symbol.ContainingType.ToDisplayString(),
            symbol.ContainingType.Name,
            symbol.ContainingType.ContainingNamespace.ToDisplayString(),
            name, type, isEnum, enumMembers, statsName ?? name, min, max,
            conv, defaultValue, node.SyntaxTree.FilePath, node.SpanStart,
            symbol.ContainingType.BaseType?.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == VICTORY_CASE_ATTRIBUTE) == true
                ? symbol.ContainingType.BaseType.ToDisplayString()
                : null,
            snapshotType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            projectionType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            untracked,
            diagnostics.ToImmutable());
    }

    private static void ValidateProjection(
        GeneratorAttributeSyntaxContext ctx,
        IPropertySymbol property,
        INamedTypeSymbol snapshot,
        INamedTypeSymbol projection,
        Location location,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        ITypeSymbol mutable = property.Type;
        IArrayTypeSymbol bytes = ctx.SemanticModel.Compilation.CreateArrayTypeSymbol(
            ctx.SemanticModel.Compilation.GetSpecialType(SpecialType.System_Byte));
        ITypeSymbol voidType = ctx.SemanticModel.Compilation.GetSpecialType(SpecialType.System_Void);
        (string Name, ITypeSymbol Parameter, ITypeSymbol Return)[] required =
        [
            ("ToSnapshot", mutable, snapshot),
            ("ToMutable", snapshot, mutable),
            ("Clamp", mutable, voidType),
            ("ToBytes", mutable, bytes),
            ("FromBytes", bytes, mutable),
        ];
        foreach ((string methodName, ITypeSymbol parameter, ITypeSymbol returnType) in required)
        {
            bool found = projection.GetMembers(methodName).OfType<IMethodSymbol>().Any(method =>
                method.IsStatic &&
                method.DeclaredAccessibility != Accessibility.Private &&
                method.Parameters.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, parameter) &&
                SymbolEqualityComparer.Default.Equals(method.ReturnType, returnType));
            if (!found)
            {
                diagnostics.Add(Diagnostic.Create(_unsupportedProjection, location, property.Name,
                    $"projection '{projection.Name}' needs static {returnType.ToDisplayString()} " +
                    $"{methodName}({parameter.ToDisplayString()})"));
            }
        }
    }

    private static SectionModel ExtractSection(GeneratorAttributeSyntaxContext ctx)
    {
        var symbol = (IPropertySymbol)ctx.TargetSymbol;
        var node = (PropertyDeclarationSyntax)ctx.TargetNode;
        INamedTypeSymbol owner = symbol.ContainingType;
        ImmutableArray<string> tracked = owner.GetMembers().OfType<IPropertySymbol>()
            .Where(property => property is
            {
                IsStatic: false,
                DeclaredAccessibility: Accessibility.Public,
                GetMethod.DeclaredAccessibility: Accessibility.Public,
            } && property.SetMethod != null &&
                !property.GetAttributes().Any(attribute =>
                    attribute.AttributeClass?.ToDisplayString() == CONFIG_SECTION_ATTRIBUTE))
            .Select(property => property.Name)
            .ToImmutableArray();
        return new SectionModel(
            symbol.Name,
            symbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            owner.ContainingNamespace.ToDisplayString(),
            node.SyntaxTree.FilePath,
            node.SpanStart,
            tracked);
    }

    private static void ValidateType(FieldKind kind, string name, string type, bool isEnum,
        Convert conv, float min, float max, Location location,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (kind == FieldKind.VALUE)
            return;

        if (kind == FieldKind.STAT)
        {
            bool ok = conv switch
            {
                Convert.COUNT_BYTE => type == "int",
                _ => type == "float",
            };
            if (!ok)
                diagnostics.Add(Diagnostic.Create(_typeMismatch, location, name,
                    $"convert {conv} requires a {(conv == Convert.COUNT_BYTE ? "int" : "float")} property, not {type}"));
            if (float.IsNaN(min) || float.IsNaN(max))
                diagnostics.Add(Diagnostic.Create(_typeMismatch, location, name,
                    "player stats always clamp; min and max are required"));
            return;
        }

        bool numeric = type is "float" or "int";
        if (kind == FieldKind.ZONE_STAT && !numeric)
        {
            diagnostics.Add(Diagnostic.Create(_typeMismatch, location, name,
                $"zone stats must be float or int, not {type}"));
            return;
        }
        if (!numeric && type != "bool" && !isEnum)
            diagnostics.Add(Diagnostic.Create(_typeMismatch, location, name,
                $"rules must be float, int, bool, or an enum, not {type}"));
        if (!numeric && (!float.IsNaN(min) || !float.IsNaN(max)))
            diagnostics.Add(Diagnostic.Create(_typeMismatch, location, name,
                $"min/max apply to numeric rules only, not {type}"));
    }

    private static double? ConstantDefault(GeneratorAttributeSyntaxContext ctx,
        PropertyDeclarationSyntax node)
    {
        if (node.Initializer is not EqualsValueClauseSyntax initializer)
            return null;
        Optional<object?> constant = ctx.SemanticModel.GetConstantValue(initializer.Value);
        if (!constant.HasValue)
            return null;
        return constant.Value switch
        {
            float f => f,
            int i => i,
            byte b => b,
            double d => d,
            _ => null,
        };
    }

    private static int? TickRate(GeneratorAttributeSyntaxContext ctx)
    {
        INamedTypeSymbol? simConfig =
            ctx.SemanticModel.Compilation.GetTypeByMetadataName(TICK_RATE_TYPE);
        object? value = simConfig?.GetMembers("TICK_RATE")
            .OfType<IFieldSymbol>()
            .FirstOrDefault(f => f.HasConstantValue)?.ConstantValue;
        if (value is byte b)
            return b;
        if (value is int i)
            return i;
        return null;
    }

    private static void Emit(SourceProductionContext spc,
        ImmutableArray<FieldModel> stats, ImmutableArray<FieldModel> rules,
        ImmutableArray<FieldModel> zoneStats, ImmutableArray<FieldModel> values,
        ImmutableArray<SectionModel> sections)
    {
        FieldModel[] allFields = stats.Concat(rules).Concat(zoneStats).Concat(values).ToArray();
        foreach (Diagnostic d in allFields.SelectMany(m => m.Diagnostics))
        {
            spc.ReportDiagnostic(d);
        }
        foreach (IGrouping<string, FieldModel> owner in allFields.GroupBy(m => m.Owner))
        {
            var declared = new HashSet<string>(owner.Select(field => field.Name), StringComparer.Ordinal);
            foreach (string property in owner.SelectMany(field => field.UntrackedProperties)
                         .Distinct(StringComparer.Ordinal))
            {
                if (!declared.Contains(property))
                    spc.ReportDiagnostic(Diagnostic.Create(_untrackedValue, Location.None,
                        owner.First().OwnerName, property));
            }
        }
        foreach (string property in sections.SelectMany(section => section.UntrackedProperties)
                     .Distinct(StringComparer.Ordinal))
        {
            spc.ReportDiagnostic(Diagnostic.Create(_untrackedValue, Location.None,
                "MatchConfig", property));
        }
        IGrouping<string, FieldModel>[] unsupportedNested = allFields
            .Where(field => field.Kind == FieldKind.VALUE)
            .GroupBy(field => field.Owner)
            .Where(owner => owner.Count() > 1)
            .ToArray();
        foreach (IGrouping<string, FieldModel> owner in unsupportedNested)
        {
            spc.ReportDiagnostic(Diagnostic.Create(_multipleNestedValues, Location.None,
                owner.First().OwnerName));
        }
        if (allFields.Any(m => !m.Diagnostics.IsEmpty) ||
            allFields.GroupBy(m => m.Owner).Any(owner =>
                owner.SelectMany(field => field.UntrackedProperties).Distinct(StringComparer.Ordinal)
                    .Except(owner.Select(field => field.Name), StringComparer.Ordinal).Any()) ||
            sections.Any(section => !section.UntrackedProperties.IsEmpty) ||
            unsupportedNested.Length > 0)
            return;

        FieldModel[] valid = stats.Concat(rules).Concat(values)
            .OrderBy(m => m.FilePath, StringComparer.Ordinal)
            .ThenBy(m => m.SpanStart)
            .ToArray();
        if (valid.Length == 0)
            return;

        var seen = new HashSet<string>();
        FieldModel[] modifierFields = stats.Concat(zoneStats)
            .OrderBy(m => m.FilePath, StringComparer.Ordinal)
            .ThenBy(m => m.SpanStart)
            .ToArray();
        foreach (FieldModel m in modifierFields)
        {
            if (!seen.Add(ToScreamingSnake(m.StatName)))
            {
                spc.ReportDiagnostic(Diagnostic.Create(_nameCollision, Location.None, m.StatName));
                return;
            }
        }

        foreach (IGrouping<string, FieldModel> owner in valid.GroupBy(m => m.Owner))
        {
            spc.AddSource($"{HintName(owner.Key)}.g.cs",
                SourceText.From(EmitConfig(owner.ToArray()),
                    Encoding.UTF8));
        }

        if (!sections.IsDefaultOrEmpty)
        {
            spc.AddSource("MatchConfigSnapshot.g.cs",
                SourceText.From(EmitMatchConfig(sections
                        .OrderBy(section => section.FilePath, StringComparer.Ordinal)
                        .ThenBy(section => section.SpanStart)
                        .ToArray()), Encoding.UTF8));
        }

        FieldModel[] statFields = valid.Where(m => m.Kind == FieldKind.STAT).ToArray();
        if (statFields.Length == 0)
            return;
        spc.AddSource("Stat.g.cs",
            SourceText.From(EmitStatEnum(modifierFields),
                Encoding.UTF8));
        spc.AddSource("PlayerStats.g.cs",
            SourceText.From(EmitPlayerStats(statFields),
                Encoding.UTF8));
        spc.AddSource("StatsPipeline.g.cs",
            SourceText.From(EmitPipeline(modifierFields),
                Encoding.UTF8));
    }

    private static string EmitMatchConfig(SectionModel[] sections)
    {
        string ns = sections[0].Namespace;
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated by Mortz.Net.Gen/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("public sealed record MatchConfigSnapshot(");
        for (int i = 0; i < sections.Length; i++)
        {
            SectionModel section = sections[i];
            string suffix = i == sections.Length - 1 ? ");" : ",";
            sb.AppendLine($"    {section.Type}Snapshot {section.Name}{suffix}");
        }
        sb.AppendLine();
        sb.AppendLine("public sealed partial class MatchConfig");
        sb.AppendLine("{");
        sb.AppendLine("    public MatchConfigSnapshot ToSnapshot() => new MatchConfigSnapshot(");
        for (int i = 0; i < sections.Length; i++)
        {
            SectionModel section = sections[i];
            string suffix = i == sections.Length - 1 ? ");" : ",";
            sb.AppendLine($"        {section.Name}.ToSnapshot(){suffix}");
        }
        sb.AppendLine();
        sb.AppendLine("    public static MatchConfig FromSnapshot(MatchConfigSnapshot snapshot) => new MatchConfig");
        sb.AppendLine("    {");
        foreach (SectionModel section in sections)
        {
            sb.AppendLine($"        {section.Name} = snapshot.{section.Name}.ToMutable(),");
        }
        sb.AppendLine("    };");
        sb.AppendLine();
        sb.AppendLine("    public void Clamp()");
        sb.AppendLine("    {");
        foreach (SectionModel section in sections)
        {
            sb.AppendLine($"        {section.Name}.Clamp();");
        }
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    // Changing this layout requires a protocol version bump.");
        sb.AppendLine("    public byte[] ToBytes()");
        sb.AppendLine("    {");
        sb.AppendLine("        byte[][] segments =");
        sb.AppendLine("        [");
        foreach (SectionModel section in sections)
        {
            sb.AppendLine($"            {section.Name}.ToBytes(),");
        }
        sb.AppendLine("        ];");
        sb.AppendLine("        byte[] combined = new byte[global::System.Linq.Enumerable.Sum(segments, segment => 4 + segment.Length)];");
        sb.AppendLine("        int offset = 0;");
        sb.AppendLine("        foreach (byte[] segment in segments)");
        sb.AppendLine("        {");
        sb.AppendLine("            global::System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(");
        sb.AppendLine("                global::System.MemoryExtensions.AsSpan(combined, offset), segment.Length);");
        sb.AppendLine("            segment.CopyTo(combined, offset + 4);");
        sb.AppendLine("            offset += 4 + segment.Length;");
        sb.AppendLine("        }");
        sb.AppendLine("        return combined;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public static MatchConfig FromBytes(byte[] data)");
        sb.AppendLine("    {");
        sb.AppendLine("        int offset = 0;");
        sb.AppendLine("        MatchConfig config = new MatchConfig");
        sb.AppendLine("        {");
        foreach (SectionModel section in sections)
        {
            sb.AppendLine($"            {section.Name} = {section.Type}.FromBytes(ReadSegment(data, ref offset)),");
        }
        sb.AppendLine("        };");
        sb.AppendLine("        if (offset != data.Length)");
        sb.AppendLine("            throw new global::System.IO.InvalidDataException(\"Trailing bytes in match configuration.\");");
        sb.AppendLine("        return config;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static byte[] ReadSegment(byte[] data, ref int offset)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (data.Length - offset < 4)");
        sb.AppendLine("            throw new global::System.IO.InvalidDataException(\"Match configuration too short.\");");
        sb.AppendLine("        int length = global::System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(");
        sb.AppendLine("            global::System.MemoryExtensions.AsSpan(data, offset));");
        sb.AppendLine("        if (length < 0 || length > data.Length - offset - 4)");
        sb.AppendLine("            throw new global::System.IO.InvalidDataException(\"Match configuration segment length out of range.\");");
        sb.AppendLine("        byte[] segment = data[(offset + 4)..(offset + 4 + length)];");
        sb.AppendLine("        offset += 4 + length;");
        sb.AppendLine("        return segment;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("public static class MatchConfigSnapshotConversions");
        sb.AppendLine("{");
        sb.AppendLine("    public static MatchConfig ToMutable(this MatchConfigSnapshot snapshot) =>");
        sb.AppendLine("        MatchConfig.FromSnapshot(snapshot);");
        sb.AppendLine();
        sb.AppendLine("    // This remains the existing protocol layout. Changing it requires a version bump.");
        sb.AppendLine("    public static byte[] ToBytes(this MatchConfigSnapshot snapshot) =>");
        sb.AppendLine("        snapshot.ToMutable().ToBytes();");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string HintName(string typeName)
    {
        var sb = new StringBuilder(typeName.Length);
        foreach (char c in typeName)
        {
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        }
        return sb.ToString();
    }

    private static string StatsFieldName(FieldModel m) =>
        m.Conv is Convert.TICKS_INT or Convert.TICKS_BYTE or Convert.TICKS_USHORT
            ? m.StatName + "Ticks"
            : m.StatName;

    private static string StatsFieldType(FieldModel m) => m.Conv switch
    {
        Convert.TICKS_INT => "int",
        Convert.TICKS_BYTE or Convert.COUNT_BYTE => "byte",
        Convert.TICKS_USHORT => "ushort",
        _ => "float",
    };

    private static string EmitPlayerStats(FieldModel[] stats)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated by Mortz.Net.Gen/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Mortz.Core.Sim;");
        sb.AppendLine();
        sb.AppendLine("public sealed class PlayerStats");
        sb.AppendLine("{");
        foreach (FieldModel m in stats)
        {
            sb.AppendLine($"    public readonly {StatsFieldType(m)} {StatsFieldName(m)};");
        }
        sb.AppendLine();
        sb.AppendLine($"    public static PlayerStats Resolve({MATCH_CONFIG} cfg) =>");
        sb.AppendLine("        new PlayerStats(cfg);");
        sb.AppendLine();
        sb.AppendLine($"    private PlayerStats({MATCH_CONFIG} cfg)");
        sb.AppendLine("    {");
        foreach (FieldModel m in stats)
        {
            string source = $"cfg.{m.OwnerName}.{m.Name}";
            string value = m.Conv switch
            {
                Convert.TICKS_INT => $"Ticks({source})",
                Convert.TICKS_BYTE => $"(byte)Ticks({source})",
                Convert.TICKS_USHORT => $"(ushort)Ticks({source})",
                Convert.COUNT_BYTE => $"(byte){source}",
                _ => source,
            };
            sb.AppendLine($"        {StatsFieldName(m)} = {value};");
        }
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static int Ticks(float seconds) =>");
        sb.AppendLine("        (int)(seconds * global::Mortz.Core.Sim.SimConfig.TICK_RATE);");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string EmitPipeline(FieldModel[] stats)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated by Mortz.Net.Gen/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Mortz.Core.Sim.Modifiers;");
        sb.AppendLine();
        sb.AppendLine("public static partial class StatsPipeline");
        sb.AppendLine("{");
        sb.AppendLine($"    private static float Get({MATCH_CONFIG} c, Stat stat) => stat switch");
        sb.AppendLine("    {");
        foreach (FieldModel m in stats)
        {
            sb.AppendLine($"        Stat.{ToScreamingSnake(m.StatName)} => c.{m.OwnerName}.{m.Name},");
        }
        sb.AppendLine("        _ => throw new global::System.ArgumentOutOfRangeException(nameof(stat)),");
        sb.AppendLine("    };");
        sb.AppendLine();
        sb.AppendLine($"    private static void Set({MATCH_CONFIG} c, Stat stat, float value)");
        sb.AppendLine("    {");
        sb.AppendLine("        int rounded = (int)global::System.MathF.Round(value);");
        sb.AppendLine("        switch (stat)");
        sb.AppendLine("        {");
        foreach (FieldModel m in stats)
        {
            string assigned = m.Type == "int" ? "rounded" : "value";
            sb.AppendLine($"            case Stat.{ToScreamingSnake(m.StatName)}: c.{m.OwnerName}.{m.Name} = {assigned}; break;");
        }
        sb.AppendLine("            default: throw new global::System.ArgumentOutOfRangeException(nameof(stat));");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public static float Clamp(Stat stat, float value) => stat switch");
        sb.AppendLine("    {");
        foreach (FieldModel m in stats)
        {
            if (!float.IsNaN(m.Min) && !float.IsNaN(m.Max))
                sb.AppendLine($"        Stat.{ToScreamingSnake(m.StatName)} => global::System.Math.Clamp(value, {F(m.Min)}, {F(m.Max)}),");
        }
        sb.AppendLine("        _ => value,");
        sb.AppendLine("    };");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string EmitStatEnum(FieldModel[] statFields)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated by Mortz.Net.Gen/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Mortz.Core.Sim.Modifiers;");
        sb.AppendLine();
        sb.AppendLine("public enum Stat : byte");
        sb.AppendLine("{");
        foreach (FieldModel m in statFields)
        {
            sb.AppendLine($"    {ToScreamingSnake(m.StatName)},");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string EmitConfig(FieldModel[] fields)
    {
        string name = fields[0].OwnerName;
        FieldModel[] wireFields = fields.Where(field => field.Kind != FieldKind.VALUE).ToArray();
        string snapshotBase = fields[0].SnapshotBase == null
            ? ""
            : $" : global::{fields[0].SnapshotBase}Snapshot";
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated by Mortz.Net.Gen/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"namespace {fields[0].OwnerNamespace};");
        sb.AppendLine();
        sb.AppendLine($"public sealed record {name}Snapshot(");
        for (int i = 0; i < fields.Length; i++)
        {
            FieldModel field = fields[i];
            string type = field.Kind == FieldKind.VALUE
                ? field.SnapshotType!
                : TypeName(field.Type);
            string suffix = i == fields.Length - 1 ? $")" + snapshotBase : ",";
            sb.AppendLine($"    {type} {field.Name}{suffix}");
        }
        sb.AppendLine("{");
        string mutableOverride = fields[0].SnapshotBase == null ? "" : "override ";
        sb.AppendLine($"    public {mutableOverride}{name} ToMutable() => new {name}");
        sb.AppendLine("    {");
        foreach (FieldModel field in fields)
        {
            string value = field.Kind == FieldKind.VALUE
                ? $"{field.ProjectionType}.ToMutable({field.Name})"
                : field.Name;
            sb.AppendLine($"        {field.Name} = {value},");
        }
        sb.AppendLine("    };");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"public sealed partial class {name} : global::Mortz.Core.Match.Configuration.IConfigSection");
        sb.AppendLine("{");
        string snapshotOverride = fields[0].SnapshotBase == null ? "" : "override ";
        sb.AppendLine($"    public {snapshotOverride}{name}Snapshot ToSnapshot() => new {name}Snapshot(");
        for (int i = 0; i < fields.Length; i++)
        {
            FieldModel field = fields[i];
            string value = field.Kind == FieldKind.VALUE
                ? $"{field.ProjectionType}.ToSnapshot({field.Name})"
                : field.Name;
            string suffix = i == fields.Length - 1 ? ");" : ",";
            sb.AppendLine($"        {value}{suffix}");
        }
        sb.AppendLine();
        sb.AppendLine($"    public static {name} FromSnapshot({name}Snapshot snapshot) => snapshot.ToMutable();");
        sb.AppendLine();
        sb.AppendLine("    public void Clamp()");
        sb.AppendLine("    {");
        foreach (FieldModel m in wireFields)
        {
            if (m.IsEnum)
            {
                sb.AppendLine($"        if (!global::System.Enum.IsDefined({m.Name}))");
                sb.AppendLine($"            {m.Name} = (global::{m.Type})({(int)(m.Default ?? 0)});");
            }
            else if (m.Type == "int" && !float.IsNaN(m.Min))
            {
                sb.AppendLine($"        {m.Name} = global::System.Math.Clamp({m.Name}, {(int)m.Min}, {(int)m.Max});");
            }
            else if (m.Type == "float" && !float.IsNaN(m.Min))
            {
                sb.AppendLine($"        {m.Name} = C({m.Name}, {F(m.Min)}, {F(m.Max)});");
            }
        }
        foreach (FieldModel field in fields.Where(field => field.Kind == FieldKind.VALUE))
        {
            sb.AppendLine($"        {field.ProjectionType}.Clamp({field.Name});");
        }
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static float C(float v, float min, float max) =>");
        sb.AppendLine("        float.IsNaN(v) ? min : global::System.Math.Clamp(v, min, max);");
        sb.AppendLine();
        sb.AppendLine("    // Changing field order requires a protocol version bump.");
        sb.AppendLine($"    internal static byte[] Serialize({name} config)");
        sb.AppendLine("    {");
        sb.AppendLine("        using global::System.IO.MemoryStream stream = new global::System.IO.MemoryStream();");
        sb.AppendLine("        using global::System.IO.BinaryWriter w = new global::System.IO.BinaryWriter(stream);");
        foreach (FieldModel m in wireFields)
        {
            sb.AppendLine(m.IsEnum
                ? $"        w.Write((byte)config.{m.Name});"
                : $"        w.Write(config.{m.Name});");
        }
        sb.AppendLine("        return stream.ToArray();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    internal static {name} Deserialize(byte[] data)");
        sb.AppendLine("    {");
        sb.AppendLine("        using global::System.IO.MemoryStream stream = new global::System.IO.MemoryStream(data, writable: false);");
        sb.AppendLine("        using global::System.IO.BinaryReader r = new global::System.IO.BinaryReader(stream);");
        sb.AppendLine($"        {name} config = new {name}");
        sb.AppendLine("        {");
        foreach (FieldModel m in wireFields)
        {
            sb.AppendLine($"            {m.Name} = {ReadExpr(m)},");
        }
        sb.AppendLine("        };");
        sb.AppendLine("        if (stream.Position != stream.Length)");
        sb.AppendLine("            throw new global::System.IO.InvalidDataException(\"Trailing bytes in match configuration.\");");
        sb.AppendLine("        config.Clamp();");
        sb.AppendLine("        return config;");
        sb.AppendLine("    }");
        sb.AppendLine();
        EmitSectionWire(sb, name, fields);
        sb.AppendLine();
        EmitApplier(sb, wireFields);
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitSectionWire(StringBuilder sb, string name, FieldModel[] fields)
    {
        FieldModel? nested = fields.SingleOrDefault(field => field.Kind == FieldKind.VALUE);
        if (nested == null)
        {
            sb.AppendLine("    public byte[] ToBytes() => Serialize(this);");
            sb.AppendLine();
            sb.AppendLine($"    public static {name} FromBytes(byte[] data) => Deserialize(data);");
            return;
        }

        sb.AppendLine("    public byte[] ToBytes()");
        sb.AppendLine("    {");
        sb.AppendLine("        byte[] shared = Serialize(this);");
        sb.AppendLine($"        byte[] nested = {nested.ProjectionType}.ToBytes({nested.Name});");
        sb.AppendLine("        using global::System.IO.MemoryStream stream = new global::System.IO.MemoryStream();");
        sb.AppendLine("        using global::System.IO.BinaryWriter writer = new global::System.IO.BinaryWriter(stream);");
        sb.AppendLine("        writer.Write(shared.Length);");
        sb.AppendLine("        writer.Write(shared);");
        sb.AppendLine("        writer.Write(nested);");
        sb.AppendLine("        return stream.ToArray();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    public static {name} FromBytes(byte[] data)");
        sb.AppendLine("    {");
        sb.AppendLine("        using global::System.IO.MemoryStream stream = new global::System.IO.MemoryStream(data, writable: false);");
        sb.AppendLine("        using global::System.IO.BinaryReader reader = new global::System.IO.BinaryReader(stream);");
        sb.AppendLine("        int sharedLength = reader.ReadInt32();");
        sb.AppendLine("        if (sharedLength < 0 || sharedLength > stream.Length - stream.Position)");
        sb.AppendLine("            throw new global::System.IO.InvalidDataException(\"Invalid shared configuration length.\");");
        sb.AppendLine($"        {name} config = Deserialize(reader.ReadBytes(sharedLength));");
        sb.AppendLine($"        config.{nested.Name} = {nested.ProjectionType}.FromBytes(");
        sb.AppendLine("            reader.ReadBytes(checked((int)(stream.Length - stream.Position))));");
        sb.AppendLine("        return config;");
        sb.AppendLine("    }");
    }

    private static string TypeName(string type) => type switch
    {
        "bool" or "byte" or "int" or "float" or "double" or "string" => type,
        _ => $"global::{type}",
    };

    private static void EmitApplier(StringBuilder sb, FieldModel[] fields)
    {
        sb.AppendLine("    public global::Mortz.Core.Match.Configuration.ConfigKeyResult TryApplyKey(string key, object? value, out string error)");
        sb.AppendLine("    {");
        sb.AppendLine("        error = \"\";");
        sb.AppendLine("        switch (key)");
        sb.AppendLine("        {");
        foreach (FieldModel m in fields)
        {
            string key = ToScreamingSnake(m.Name).ToLowerInvariant();
            sb.AppendLine($"            case \"{key}\":");
            if (m.IsEnum)
            {
                string values = string.Join(", ",
                    m.EnumMembers.Select(v => v.ToLowerInvariant()));
                sb.AppendLine($"                if (value is string s{m.Name} &&");
                sb.AppendLine($"                    global::System.Enum.TryParse(s{m.Name}, ignoreCase: true, out global::{m.Type} e{m.Name}) &&");
                sb.AppendLine($"                    global::System.Enum.IsDefined(e{m.Name}))");
                sb.AppendLine("                {");
                sb.AppendLine($"                    {m.Name} = e{m.Name};");
                sb.AppendLine("                    return global::Mortz.Core.Match.Configuration.ConfigKeyResult.APPLIED;");
                sb.AppendLine("                }");
                sb.AppendLine($"                error = \"'{key}' must be one of: {values}\";");
            }
            else if (m.Type == "bool")
            {
                sb.AppendLine($"                if (value is bool b{m.Name})");
                sb.AppendLine("                {");
                sb.AppendLine($"                    {m.Name} = b{m.Name};");
                sb.AppendLine("                    return global::Mortz.Core.Match.Configuration.ConfigKeyResult.APPLIED;");
                sb.AppendLine("                }");
                sb.AppendLine($"                error = \"'{key}' must be a boolean\";");
            }
            else if (m.Type == "int")
            {
                sb.AppendLine($"                if (value is long l{m.Name} && l{m.Name} is >= int.MinValue and <= int.MaxValue)");
                sb.AppendLine("                {");
                sb.AppendLine($"                    {m.Name} = (int)l{m.Name};");
                sb.AppendLine("                    return global::Mortz.Core.Match.Configuration.ConfigKeyResult.APPLIED;");
                sb.AppendLine("                }");
                sb.AppendLine($"                error = \"'{key}' must be a 32-bit integer\";");
            }
            else
            {
                sb.AppendLine($"                if (value is double d{m.Name})");
                sb.AppendLine("                {");
                sb.AppendLine($"                    {m.Name} = (float)d{m.Name};");
                sb.AppendLine("                    return global::Mortz.Core.Match.Configuration.ConfigKeyResult.APPLIED;");
                sb.AppendLine("                }");
                sb.AppendLine($"                if (value is long i{m.Name})");
                sb.AppendLine("                {");
                sb.AppendLine($"                    {m.Name} = i{m.Name};");
                sb.AppendLine("                    return global::Mortz.Core.Match.Configuration.ConfigKeyResult.APPLIED;");
                sb.AppendLine("                }");
                sb.AppendLine($"                error = \"'{key}' must be a number\";");
            }
            sb.AppendLine("                return global::Mortz.Core.Match.Configuration.ConfigKeyResult.INVALID_VALUE;");
        }
        sb.AppendLine("            default:");
        sb.AppendLine("                error = $\"unknown key '{key}'\";");
        sb.AppendLine("                return global::Mortz.Core.Match.Configuration.ConfigKeyResult.UNKNOWN_KEY;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }

    private static string ReadExpr(FieldModel m) => m.Type switch
    {
        "float" => "r.ReadSingle()",
        "int" => "r.ReadInt32()",
        "bool" => "r.ReadBoolean()",
        _ => $"(global::{m.Type})r.ReadByte()",
    };

    private static string F(float value) =>
        value.ToString("R", CultureInfo.InvariantCulture) + "f";

    private static string Literal(string value) =>
        SymbolDisplay.FormatLiteral(value, quote: true);

    private static string ToScreamingSnake(string pascal)
    {
        var sb = new StringBuilder(pascal.Length + 8);
        for (int i = 0; i < pascal.Length; i++)
        {
            char c = pascal[i];
            bool wordStart = i > 0 &&
                ((char.IsUpper(c) && (!char.IsUpper(pascal[i - 1]) ||
                     (i + 1 < pascal.Length && char.IsLower(pascal[i + 1])))) ||
                 (char.IsDigit(c) && char.IsLetter(pascal[i - 1])));
            if (wordStart)
                sb.Append('_');
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }
}
