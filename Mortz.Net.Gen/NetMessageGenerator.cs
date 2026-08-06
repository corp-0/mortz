using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Mortz.Net.Gen;

/// <summary>Generates the serializer, SendToServer, NetRegistry, and both routers for a [NetMessage] record struct.</summary>
[Generator]
public sealed class NetMessageGenerator : IIncrementalGenerator
{
    private const string ATTRIBUTE_NAME = "Mortz.Core.Net.NetMessageAttribute";
    private const string ROW_ATTRIBUTE_NAME = "Mortz.Core.Net.NetRowAttribute";

    private static readonly DiagnosticDescriptor _unsupportedFieldType = new(
        "MZ0001", "Unsupported net message field type",
        "Field '{0}' of type '{1}' is not serializable; supported types: byte-backed enums and their nullable form, bool, byte, short, int, long, ulong, float, string, byte[], int[], long[], string[], Vec2, [NetRow][]",
        "Mortz.Net", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _notPartialRecordStruct = new(
        "MZ0002", "Net type must be a partial record struct",
        "Type '{0}' must be declared as a partial record struct",
        "Mortz.Net", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _duplicateName = new(
        "MZ0003", "Duplicate net message name",
        "Two net messages share the short name '{0}'; registry ids are keyed by it, rename one",
        "Mortz.Net", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _zeroValuedNullableEnum = new(
        "MZ0004", "Nullable byte-enum has a zero member",
        "Enum '{0}' is used as a nullable wire field but declares a member with value 0, which the wire spends on null",
        "Mortz.Net", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _badRow = new(
        "MZ0005", "Unusable net row",
        "[NetRow] type '{0}' cannot be serialized: {1}",
        "Mortz.Net", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>RowType is set only for row arrays, EnumType and EnumMembers only
    /// for nullable byte-enums.</summary>
    private sealed record FieldModel(
        string Name, string Type, string WireType, string? RowType, string? EnumType,
        ImmutableArray<string> EnumMembers);

    private sealed record MessageModel(
        string Namespace,
        string Name,
        int Channel,
        int Direction,
        ImmutableArray<FieldModel> Fields,
        ImmutableArray<Diagnostic> Diagnostics);

    private sealed record RowModel(
        string Namespace,
        string Name,
        ImmutableArray<FieldModel> Fields,
        ImmutableArray<Diagnostic> Diagnostics);

    private static readonly ImmutableHashSet<string> _supportedTypes = ImmutableHashSet.Create(
        "bool", "byte", "short", "int", "long", "ulong", "float", "string",
        "byte[]", "int[]", "long[]", "string[]", "Mortz.Core.Sim.Vec2");

    private static readonly ImmutableHashSet<string> _supportedRowTypes = ImmutableHashSet.Create(
        "bool", "byte", "short", "int", "long", "ulong", "float", "string",
        "Mortz.Core.Sim.Vec2");

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<ImmutableArray<MessageModel>> messages = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ATTRIBUTE_NAME,
                static (node, _) => node is RecordDeclarationSyntax or StructDeclarationSyntax,
                static (ctx, _) => Extract(ctx))
            .Collect();

        IncrementalValueProvider<ImmutableArray<RowModel>> rows = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ROW_ATTRIBUTE_NAME,
                static (node, _) => node is RecordDeclarationSyntax or StructDeclarationSyntax,
                static (ctx, _) => ExtractRow(ctx))
            .Collect();

        context.RegisterSourceOutput(messages, static (spc, models) => Emit(spc, models));
        context.RegisterSourceOutput(rows, static (spc, models) => EmitRows(spc, models));
    }

    private static MessageModel Extract(GeneratorAttributeSyntaxContext ctx)
    {
        var symbol = (INamedTypeSymbol)ctx.TargetSymbol;
        string ns = symbol.ContainingNamespace.ToDisplayString();
        string name = symbol.Name;
        ImmutableArray<Diagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        bool isPartialRecordStruct =
            ctx.TargetNode is RecordDeclarationSyntax rec
            && rec.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword)
            && rec.Modifiers.Any(SyntaxKind.PartialKeyword);
        if (!isPartialRecordStruct)
        {
            diagnostics.Add(Diagnostic.Create(_notPartialRecordStruct, ctx.TargetNode.GetLocation(), name));
            return new MessageModel(ns, name, 0, 0, ImmutableArray<FieldModel>.Empty, diagnostics.ToImmutable());
        }

        AttributeData attr = ctx.Attributes[0];
        int channel = (int)(attr.ConstructorArguments[0].Value ?? 0);
        int direction = (int)(attr.ConstructorArguments[1].Value ?? 0);

        ImmutableArray<FieldModel>.Builder fields = ImmutableArray.CreateBuilder<FieldModel>();
        ParameterListSyntax? parameters = ((RecordDeclarationSyntax)ctx.TargetNode).ParameterList;
        if (parameters != null)
        {
            foreach (ParameterSyntax p in parameters.Parameters)
            {
                if (ctx.SemanticModel.GetDeclaredSymbol(p) is not IParameterSymbol ps)
                    continue;
                FieldModel? field = ResolveField(
                    ps.Name, ps.Type, allowRows: true, p.GetLocation(), diagnostics);
                if (field != null)
                    fields.Add(field);
            }
        }

        return new MessageModel(ns, name, channel, direction, fields.ToImmutable(), diagnostics.ToImmutable());
    }

    private static RowModel ExtractRow(GeneratorAttributeSyntaxContext ctx)
    {
        var symbol = (INamedTypeSymbol)ctx.TargetSymbol;
        string ns = symbol.ContainingNamespace.ToDisplayString();
        string name = symbol.Name;
        ImmutableArray<Diagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        bool isPartialRecordStruct =
            ctx.TargetNode is RecordDeclarationSyntax rec
            && rec.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword)
            && rec.Modifiers.Any(SyntaxKind.PartialKeyword);
        if (!isPartialRecordStruct)
        {
            diagnostics.Add(Diagnostic.Create(_notPartialRecordStruct, ctx.TargetNode.GetLocation(), name));
            return new RowModel(ns, name, ImmutableArray<FieldModel>.Empty, diagnostics.ToImmutable());
        }

        TryRowFields(symbol, diagnostics, ctx.TargetNode.GetLocation(),
            out ImmutableArray<FieldModel> fields);
        return new RowModel(ns, name, fields, diagnostics.ToImmutable());
    }

    /// <summary>Rows are depth 1: a row's own fields come through with allowRows
    /// false, so arrays and nested rows are out.</summary>
    private static FieldModel? ResolveField(
        string name, ITypeSymbol type, bool allowRows, Location location,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        string display = type.ToDisplayString();
        ImmutableHashSet<string> supported = allowRows ? _supportedTypes : _supportedRowTypes;
        if (supported.Contains(display))
            return new FieldModel(name, display, display, null, null, ImmutableArray<string>.Empty);

        if (type is INamedTypeSymbol
            {
                TypeKind: TypeKind.Enum,
                EnumUnderlyingType.SpecialType: SpecialType.System_Byte
            })
        {
            return new FieldModel(name, display, "byte", null, null, ImmutableArray<string>.Empty);
        }

        if (TryNullableByteEnum(type, out INamedTypeSymbol? underlying))
        {
            if (underlying!.GetMembers().OfType<IFieldSymbol>()
                .Any(f => f.HasConstantValue && Convert.ToInt64(f.ConstantValue) == 0))
            {
                diagnostics.Add(Diagnostic.Create(
                    _zeroValuedNullableEnum, location, underlying.ToDisplayString()));
                return null;
            }
            return new FieldModel(
                name, display, "byte", null, $"global::{underlying.ToDisplayString()}",
                DefinedMembers(underlying));
        }

        if (allowRows
            && type is IArrayTypeSymbol { Rank: 1, ElementType: INamedTypeSymbol element }
            && IsRow(element))
        {
            if (!TryRowFields(element, diagnostics, location, out ImmutableArray<FieldModel> rowFields))
                return null;
            string signature =
                $"{element.ToDisplayString()}({string.Join(",", rowFields.Select(Signature))})[]";
            return new FieldModel(
                name, display, signature, element.ToDisplayString(), null,
                ImmutableArray<string>.Empty);
        }

        diagnostics.Add(Diagnostic.Create(_unsupportedFieldType, location, name, display));
        return null;
    }

    private static bool IsRow(INamedTypeSymbol type) => type.GetAttributes()
        .Any(a => a.AttributeClass?.ToDisplayString() == ROW_ATTRIBUTE_NAME);

    private static bool TryNullableByteEnum(ITypeSymbol type, out INamedTypeSymbol? underlying)
    {
        underlying = null;
        if (type is not INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n)
            return false;
        if (n.TypeArguments[0] is not INamedTypeSymbol
            {
                TypeKind: TypeKind.Enum,
                EnumUnderlyingType.SpecialType: SpecialType.System_Byte
            } e)
        {
            return false;
        }
        underlying = e;
        return true;
    }

    /// <summary>One name per distinct value: two names for one value would emit
    /// duplicate switch labels.</summary>
    private static ImmutableArray<string> DefinedMembers(INamedTypeSymbol enumType)
    {
        var seen = new HashSet<long>();
        ImmutableArray<string>.Builder names = ImmutableArray.CreateBuilder<string>();
        foreach (IFieldSymbol member in enumType.GetMembers().OfType<IFieldSymbol>())
        {
            if (member.HasConstantValue && seen.Add(Convert.ToInt64(member.ConstantValue)))
                names.Add(member.Name);
        }
        return names.ToImmutable();
    }

    /// <summary>Names are in the hash, or swapping two same-typed fields would be
    /// a silent wire break.</summary>
    private static string Signature(FieldModel field) => $"{field.WireType} {field.Name}";

    /// <summary>Wire order is the public constructor's parameter order; each
    /// parameter reads back from the property of the same name.</summary>
    private static bool TryRowFields(
        INamedTypeSymbol row, ImmutableArray<Diagnostic>.Builder diagnostics, Location location,
        out ImmutableArray<FieldModel> fields)
    {
        fields = ImmutableArray<FieldModel>.Empty;
        IMethodSymbol? ctor = row.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length > 0)
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();
        if (ctor == null)
        {
            diagnostics.Add(Diagnostic.Create(_badRow, location, row.ToDisplayString(),
                "it has no public constructor taking parameters"));
            return false;
        }

        ImmutableArray<FieldModel>.Builder builder = ImmutableArray.CreateBuilder<FieldModel>();
        foreach (IParameterSymbol p in ctor.Parameters)
        {
            IPropertySymbol? property = row.GetMembers().OfType<IPropertySymbol>().FirstOrDefault(
                m => m.DeclaredAccessibility == Accessibility.Public
                    && string.Equals(m.Name, p.Name, StringComparison.OrdinalIgnoreCase));
            if (property == null)
            {
                diagnostics.Add(Diagnostic.Create(_badRow, location, row.ToDisplayString(),
                    $"constructor parameter '{p.Name}' has no public property of the same name"));
                return false;
            }
            FieldModel? field = ResolveField(
                property.Name, p.Type, allowRows: false, location, diagnostics);
            if (field == null)
                return false;
            builder.Add(field);
        }
        fields = builder.ToImmutable();
        return true;
    }

    private static void EmitRows(SourceProductionContext spc, ImmutableArray<RowModel> models)
    {
        foreach (Diagnostic d in models.SelectMany(model => model.Diagnostics))
        {
            spc.ReportDiagnostic(d);
        }

        foreach (RowModel m in models.Where(model => model.Diagnostics.IsEmpty))
        {
            spc.AddSource($"{m.Namespace}.{m.Name}.Row.g.cs",
                SourceText.From(EmitRow(m), Encoding.UTF8));
        }
    }

    private static string EmitRow(RowModel m)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated by Mortz.Net.Gen/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using Mortz.Core.Net;");
        sb.AppendLine();
        sb.AppendLine($"namespace {m.Namespace};");
        sb.AppendLine();
        sb.AppendLine($"partial record struct {m.Name} : global::Mortz.Core.Net.INetRow<{m.Name}>");
        sb.AppendLine("{");
        sb.AppendLine($"    public static void WriteTo(global::System.IO.BinaryWriter w, in {m.Name} row)");
        sb.AppendLine("    {");
        foreach (FieldModel f in m.Fields)
        {
            sb.AppendLine($"        {WriteCall(f, "row")}");
        }
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    public static {m.Name} ReadFrom(global::System.IO.BinaryReader r) => new(");
        for (int i = 0; i < m.Fields.Length; i++)
        {
            sb.AppendLine($"        {ReadCall(m.Fields[i])}{(i < m.Fields.Length - 1 ? "," : ");")}");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void Emit(SourceProductionContext spc, ImmutableArray<MessageModel> models)
    {
        foreach (Diagnostic d in models.SelectMany(model => model.Diagnostics))
        {
            spc.ReportDiagnostic(d);
        }

        MessageModel[] valid = models
            .Where(m => m.Diagnostics.IsEmpty)
            .OrderBy(m => $"{m.Namespace}.{m.Name}", StringComparer.Ordinal)
            .ToArray();
        if (valid.Length == 0)
            return;

        var seen = new HashSet<string>();
        foreach (MessageModel m in valid)
        {
            if (!seen.Add(m.Name))
            {
                spc.ReportDiagnostic(Diagnostic.Create(_duplicateName, Location.None, m.Name));
                return;
            }
        }

        for (int id = 0; id < valid.Length; id++)
        {
            spc.AddSource($"{valid[id].Name}.g.cs", SourceText.From(EmitMessage(valid[id]), Encoding.UTF8));
        }

        spc.AddSource("NetRegistry.g.cs", SourceText.From(EmitRegistry(valid), Encoding.UTF8));
        spc.AddSource("NetRouter.g.cs", SourceText.From(EmitRouter(valid), Encoding.UTF8));
        spc.AddSource("ClientNetRouter.g.cs", SourceText.From(EmitClientRouter(valid), Encoding.UTF8));
    }

    /// <summary>Field name for a message's handler list: ChatSendMsg -> _chatSendMsg.</summary>
    private static string HandlerField(MessageModel m) =>
        $"_{char.ToLowerInvariant(m.Name[0])}{m.Name.Substring(1)}";

    private static string EmitRouter(MessageModel[] models)
    {
        MessageModel[] inbound = models.Where(m => m.Direction == 1).ToArray();
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated by Mortz.Net.Gen/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Mortz.Core.Net;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Routes an inbound client-to-server message to every handler that");
        sb.AppendLine("/// declares IHandle for it. Decodes once, then invokes in add order.</summary>");
        sb.AppendLine("public sealed class NetRouter<TSender>");
        sb.AppendLine("{");
        foreach (MessageModel m in inbound)
        {
            string fqn = $"global::{m.Namespace}.{m.Name}";
            sb.AppendLine($"    private readonly global::System.Collections.Generic.List<IHandle<TSender, {fqn}>> {HandlerField(m)} = new();");
        }
        sb.AppendLine();
        sb.AppendLine("    public void Clear()");
        sb.AppendLine("    {");
        foreach (MessageModel m in inbound)
        {
            sb.AppendLine($"        {HandlerField(m)}.Clear();");
        }
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Adds the handler to every message list it implements.</summary>");
        sb.AppendLine("    public void Add(object handler)");
        sb.AppendLine("    {");
        foreach (MessageModel m in inbound)
        {
            string fqn = $"global::{m.Namespace}.{m.Name}";
            sb.AppendLine($"        if (handler is IHandle<TSender, {fqn}> h{m.Name})");
            sb.AppendLine($"            {HandlerField(m)}.Add(h{m.Name});");
        }
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>False = unknown or wrong-direction id, malformed payload, or no");
        sb.AppendLine("    /// handler. Only decoding is caught here, what a handler throws travels on out.</summary>");
        sb.AppendLine("    public bool Dispatch(ushort msgId, TSender sender, byte[] payload)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (payload.Length > global::Mortz.Core.Net.NetConfig.MAX_ENVELOPE_BYTES)");
        sb.AppendLine("            return false;");
        sb.AppendLine("        switch (msgId)");
        sb.AppendLine("        {");
        foreach (MessageModel m in inbound)
        {
            string fqn = $"global::{m.Namespace}.{m.Name}";
            string field = HandlerField(m);
            sb.AppendLine($"            case NetRegistry.ID_{m.Name}:");
            sb.AppendLine("            {");
            sb.AppendLine($"                if ({field}.Count == 0)");
            sb.AppendLine("                    return false;");
            sb.AppendLine($"                if (!TryDecode<{fqn}>(payload, {fqn}.Deserialize, out {fqn} message))");
            sb.AppendLine("                    return false;");
            sb.AppendLine($"                for (int i = 0; i < {field}.Count; i++)");
            sb.AppendLine($"                    {field}[i].Handle(sender, in message);");
            sb.AppendLine("                return true;");
            sb.AppendLine("            }");
        }
        sb.AppendLine("            default:");
        sb.AppendLine("                return false;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>One line naming every message and its handlers, for boot logging.</summary>");
        sb.AppendLine("    public string Describe()");
        sb.AppendLine("    {");
        sb.AppendLine("        global::System.Collections.Generic.List<string> entries = new();");
        foreach (MessageModel m in inbound)
        {
            sb.AppendLine($"        Describe(entries, \"{m.Name}\", {HandlerField(m)});");
        }
        sb.AppendLine("        if (entries.Count == 0)");
        sb.AppendLine("            return \"router: nothing routed\";");
        sb.AppendLine("        return \"router: \" + string.Join(\"; \", entries);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static void Describe<TMsg>(");
        sb.AppendLine("        global::System.Collections.Generic.List<string> entries,");
        sb.AppendLine("        string name,");
        sb.AppendLine("        global::System.Collections.Generic.List<IHandle<TSender, TMsg>> handlers)");
        sb.AppendLine("        where TMsg : struct");
        sb.AppendLine("    {");
        sb.AppendLine("        if (handlers.Count == 0)");
        sb.AppendLine("            return;");
        sb.AppendLine("        string[] names = new string[handlers.Count];");
        sb.AppendLine("        for (int i = 0; i < handlers.Count; i++)");
        sb.AppendLine("            names[i] = handlers[i].GetType().Name;");
        sb.AppendLine("        entries.Add(name + \" -> \" + string.Join(\", \", names));");
        sb.AppendLine("    }");
        sb.AppendLine();
        AppendTryDecode(sb);
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>The client-side twin of NetRouter&lt;TSender&gt;: same vocabulary
    /// (IHandle + Add/Dispatch/Describe), minus the sender, plus Remove because
    /// client handlers are scene-tree nodes that leave individually rather than
    /// recomposing wholesale on a phase change.</summary>
    private static string EmitClientRouter(MessageModel[] models)
    {
        MessageModel[] inbound = models.Where(m => m.Direction == 0).ToArray();
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated by Mortz.Net.Gen/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Mortz.Core.Net;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Routes an inbound server-to-client message to every handler that");
        sb.AppendLine("/// declares IHandle for it. Decodes once, then invokes in add order.</summary>");
        sb.AppendLine("public sealed class NetRouter");
        sb.AppendLine("{");
        foreach (MessageModel m in inbound)
        {
            string fqn = $"global::{m.Namespace}.{m.Name}";
            sb.AppendLine($"    private readonly global::System.Collections.Generic.List<IHandle<{fqn}>> {HandlerField(m)} = new();");
        }
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Adds the handler to every message list it implements.</summary>");
        sb.AppendLine("    public void Add(object handler)");
        sb.AppendLine("    {");
        foreach (MessageModel m in inbound)
        {
            string fqn = $"global::{m.Namespace}.{m.Name}";
            sb.AppendLine($"        if (handler is IHandle<{fqn}> h{m.Name})");
            sb.AppendLine($"            {HandlerField(m)}.Add(h{m.Name});");
        }
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Removes the handler from every message list it implements.</summary>");
        sb.AppendLine("    public void Remove(object handler)");
        sb.AppendLine("    {");
        foreach (MessageModel m in inbound)
        {
            string fqn = $"global::{m.Namespace}.{m.Name}";
            sb.AppendLine($"        if (handler is IHandle<{fqn}> h{m.Name})");
            sb.AppendLine($"            {HandlerField(m)}.Remove(h{m.Name});");
        }
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>False = unknown or wrong-direction id, malformed payload, or no");
        sb.AppendLine("    /// handler. Only decoding is caught here, what a handler throws travels on out.</summary>");
        sb.AppendLine("    public bool Dispatch(ushort msgId, byte[] payload)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (payload.Length > global::Mortz.Core.Net.NetConfig.MAX_ENVELOPE_BYTES)");
        sb.AppendLine("            return false;");
        sb.AppendLine("        switch (msgId)");
        sb.AppendLine("        {");
        foreach (MessageModel m in inbound)
        {
            string fqn = $"global::{m.Namespace}.{m.Name}";
            string field = HandlerField(m);
            sb.AppendLine($"            case NetRegistry.ID_{m.Name}:");
            sb.AppendLine("            {");
            sb.AppendLine($"                if ({field}.Count == 0)");
            sb.AppendLine("                    return false;");
            sb.AppendLine($"                if (!TryDecode<{fqn}>(payload, {fqn}.Deserialize, out {fqn} message))");
            sb.AppendLine("                    return false;");
            sb.AppendLine($"                for (int i = 0; i < {field}.Count; i++)");
            sb.AppendLine($"                    {field}[i].Handle(in message);");
            sb.AppendLine("                return true;");
            sb.AppendLine("            }");
        }
        sb.AppendLine("            default:");
        sb.AppendLine("                return false;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>One line naming every message and its handlers, for boot logging.</summary>");
        sb.AppendLine("    public string Describe()");
        sb.AppendLine("    {");
        sb.AppendLine("        global::System.Collections.Generic.List<string> entries = new();");
        foreach (MessageModel m in inbound)
        {
            sb.AppendLine($"        Describe(entries, \"{m.Name}\", {HandlerField(m)});");
        }
        sb.AppendLine("        if (entries.Count == 0)");
        sb.AppendLine("            return \"router: nothing routed\";");
        sb.AppendLine("        return \"router: \" + string.Join(\"; \", entries);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static void Describe<TMsg>(");
        sb.AppendLine("        global::System.Collections.Generic.List<string> entries,");
        sb.AppendLine("        string name,");
        sb.AppendLine("        global::System.Collections.Generic.List<IHandle<TMsg>> handlers)");
        sb.AppendLine("        where TMsg : struct");
        sb.AppendLine("    {");
        sb.AppendLine("        if (handlers.Count == 0)");
        sb.AppendLine("            return;");
        sb.AppendLine("        string[] names = new string[handlers.Count];");
        sb.AppendLine("        for (int i = 0; i < handlers.Count; i++)");
        sb.AppendLine("            names[i] = handlers[i].GetType().Name;");
        sb.AppendLine("        entries.Add(name + \" -> \" + string.Join(\", \", names));");
        sb.AppendLine("    }");
        sb.AppendLine();
        AppendTryDecode(sb);
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void AppendTryDecode(StringBuilder sb)
    {
        sb.AppendLine("    /// <summary>False = the payload is not a whole well-formed T.</summary>");
        sb.AppendLine("    private static bool TryDecode<T>(");
        sb.AppendLine("        byte[] payload,");
        sb.AppendLine("        global::System.Func<global::System.IO.BinaryReader, T> deserialize,");
        sb.AppendLine("        out T message)");
        sb.AppendLine("    {");
        sb.AppendLine("        message = default!;");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            using global::System.IO.MemoryStream ms = new global::System.IO.MemoryStream(payload, writable: false);");
        sb.AppendLine("            using global::System.IO.BinaryReader r = new global::System.IO.BinaryReader(ms);");
        sb.AppendLine("            message = deserialize(r);");
        sb.AppendLine("            return ms.Position == ms.Length;");
        sb.AppendLine("        }");
        sb.AppendLine("        catch (global::System.IO.IOException)");
        sb.AppendLine("        {");
        sb.AppendLine("            return false;");
        sb.AppendLine("        }");
        sb.AppendLine("        catch (global::System.IO.InvalidDataException)");
        sb.AppendLine("        {");
        sb.AppendLine("            return false;");
        sb.AppendLine("        }");
        sb.AppendLine("        // A row constructor rejecting its arguments means a malformed message.");
        sb.AppendLine("        catch (global::System.ArgumentException)");
        sb.AppendLine("        {");
        sb.AppendLine("            return false;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }

    private static string EmitMessage(MessageModel m)
    {
        string channel = m.Channel == 0 ? "RELIABLE" : "UNRELIABLE";
        bool toClient = m.Direction == 0;
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated by Mortz.Net.Gen/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using Mortz.Core.Net;");
        sb.AppendLine();
        sb.AppendLine($"namespace {m.Namespace};");
        sb.AppendLine();
        sb.AppendLine($"partial record struct {m.Name} : global::Mortz.Core.Net.INetMessage<{m.Name}>");
        sb.AppendLine("{");
        sb.AppendLine($"    public static ushort MsgId => NetRegistry.ID_{m.Name};");
        sb.AppendLine();
        sb.AppendLine($"    public static NetChannel MsgChannel => NetChannel.{channel};");
        sb.AppendLine();

        // Only client-to-server messages get a SendToServer convenience method.
        if (!toClient)
        {
            sb.AppendLine($"    public void SendToServer() => NetTransport.Send(NetRegistry.ID_{m.Name}, Serialize(in this), NetTransport.TO_SERVER, NetChannel.{channel});");
            sb.AppendLine();
        }
        sb.AppendLine($"    public static byte[] Serialize(in {m.Name} m)");
        sb.AppendLine("    {");
        sb.AppendLine("        using global::System.IO.MemoryStream ms = new global::System.IO.MemoryStream();");
        sb.AppendLine("        using global::System.IO.BinaryWriter w = new global::System.IO.BinaryWriter(ms);");
        foreach (FieldModel f in m.Fields)
        {
            sb.AppendLine($"        {WriteCall(f, "m")}");
        }
        sb.AppendLine("        return ms.ToArray();");
        sb.AppendLine("    }");
        sb.AppendLine();

        if (m.Fields.Length == 0)
            sb.AppendLine($"    internal static {m.Name} Deserialize(global::System.IO.BinaryReader r) => new();");
        else
        {
            sb.AppendLine($"    internal static {m.Name} Deserialize(global::System.IO.BinaryReader r) => new(");
            for (int i = 0; i < m.Fields.Length; i++)
            {
                sb.AppendLine($"        {ReadCall(m.Fields[i])}{(i < m.Fields.Length - 1 ? "," : ");")}");
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string WriteCall(FieldModel f, string receiver)
    {
        if (f.RowType != null)
            return $"w.WriteRows({receiver}.{f.Name});";
        if (f.EnumType != null)
            return $"w.Write((byte)({receiver}.{f.Name} ?? 0));";
        return f.WireType switch
        {
            "byte[]" or "int[]" or "long[]" or "string[]" => $"w.WriteArray({receiver}.{f.Name});",
            "byte" when f.Type != "byte" => $"w.Write((byte){receiver}.{f.Name});",
            _ => $"w.Write({receiver}.{f.Name});",
        };
    }

    private static string ReadCall(FieldModel f)
    {
        if (f.RowType != null)
            return $"r.ReadRows<{f.RowType}>()";
        if (f.EnumType != null)
        {
            // 0 is null, and so is any byte the enum does not name.
            IEnumerable<string> arms = f.EnumMembers
                .Select(m => $"(byte){f.EnumType}.{m} => {f.EnumType}.{m}")
                .Append($"_ => ({f.EnumType}?)null");
            return $"r.ReadNullableByte() switch {{ {string.Join(", ", arms)} }}";
        }
        string read = f.WireType switch
        {
            "bool" => "r.ReadBoolean()",
            "byte" => "r.ReadByte()",
            "short" => "r.ReadInt16()",
            "int" => "r.ReadInt32()",
            "long" => "r.ReadInt64()",
            "ulong" => "r.ReadUInt64()",
            "float" => "r.ReadSingle()",
            "string" => "global::Mortz.Core.Net.NetIo.ReadString(r)",
            "byte[]" => "r.ReadByteArray()",
            "int[]" => "r.ReadInt32Array()",
            "long[]" => "r.ReadInt64Array()",
            "string[]" => "r.ReadStringArray()",
            "Mortz.Core.Sim.Vec2" => "r.ReadVec2()",
            _ => throw new InvalidOperationException($"unmapped type {f.WireType}"),
        };
        return f.Type == f.WireType ? read : $"({f.Type}){read}";
    }

    private static string EmitRegistry(MessageModel[] models)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated by Mortz.Net.Gen/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Mortz.Core.Net;");
        sb.AppendLine();
        sb.AppendLine("public static class NetRegistry");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>FNV-1a over every message's shape; carried in Hello so any");
        sb.AppendLine("    /// wire-incompatible build is rejected at connect, no manual bump needed.</summary>");
        sb.AppendLine($"    public const ulong SCHEMA_HASH = 0x{SchemaHash(models):X16}UL;");
        sb.AppendLine();
        for (int id = 0; id < models.Length; id++)
        {
            sb.AppendLine($"    public const ushort ID_{models[id].Name} = {id};");
        }
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The generated wire id's source-level message name.</summary>");
        sb.AppendLine("    public static string NameOf(ushort msgId) => msgId switch");
        sb.AppendLine("    {");
        for (int id = 0; id < models.Length; id++)
        {
            sb.AppendLine($"        ID_{models[id].Name} => \"{models[id].Name}\",");
        }
        sb.AppendLine("        _ => \"unknown message \" + msgId,");
        sb.AppendLine("    };");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static ulong SchemaHash(MessageModel[] models)
    {
        // FNV-1a 64. Field names are in the signature: a rename bumping the hash
        // costs nothing, a reorder slipping past it corrupts every field.
        ulong hash = 14695981039346656037UL;
        foreach (MessageModel m in models)
        {
            string signature =
                $"{m.Namespace}.{m.Name}({string.Join(",", m.Fields.Select(Signature))})|{m.Channel}|{m.Direction}";
            foreach (byte b in Encoding.UTF8.GetBytes(signature))
            {
                hash ^= b;
                hash *= 1099511628211UL;
            }
        }
        return hash;
    }
}
