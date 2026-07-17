using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Mortz.Net.Gen;

/// <summary>
/// Generates everything a [NetMessage] record struct needs beyond its own
/// declaration: serializer, direction-appropriate send methods, the static
/// Received event, and the shared NetRegistry (ids, dispatch switch,
/// SCHEMA_HASH). See plans/2026-07-12-net-messages-design.md.
/// </summary>
[Generator]
public sealed class NetMessageGenerator : IIncrementalGenerator
{
    private const string ATTRIBUTE_NAME = "Mortz.Core.Net.NetMessageAttribute";

    private static readonly DiagnosticDescriptor _unsupportedFieldType = new(
        "MZ0001", "Unsupported net message field type",
        "Field '{0}' of type '{1}' is not serializable; supported types: byte-backed enums, bool, byte, short, int, long, ulong, float, string, byte[], int[], long[], string[], Vec2",
        "Mortz.Net", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _notPartialRecordStruct = new(
        "MZ0002", "Net message must be a partial record struct",
        "[NetMessage] type '{0}' must be declared as a partial record struct",
        "Mortz.Net", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _duplicateName = new(
        "MZ0003", "Duplicate net message name",
        "Two net messages share the short name '{0}'; registry ids are keyed by it, rename one",
        "Mortz.Net", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private sealed record FieldModel(string Name, string Type, string WireType);

    private sealed record MessageModel(
        string Namespace,
        string Name,
        int Channel,
        int Direction,
        ImmutableArray<FieldModel> Fields,
        ImmutableArray<Diagnostic> Diagnostics);

    private static readonly ImmutableHashSet<string> _supportedTypes = ImmutableHashSet.Create(
        "bool", "byte", "short", "int", "long", "ulong", "float", "string",
        "byte[]", "int[]", "long[]", "string[]", "Mortz.Core.Sim.Vec2");

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<ImmutableArray<MessageModel>> messages = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ATTRIBUTE_NAME,
                static (node, _) => node is RecordDeclarationSyntax or StructDeclarationSyntax,
                static (ctx, _) => Extract(ctx))
            .Collect();

        context.RegisterSourceOutput(messages, static (spc, models) => Emit(spc, models));
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
                if (ctx.SemanticModel.GetDeclaredSymbol(p) is not { } ps)
                    continue;
                string type = ps.Type.ToDisplayString();
                bool byteEnum = ps.Type is INamedTypeSymbol
                {
                    TypeKind: TypeKind.Enum,
                    EnumUnderlyingType.SpecialType: SpecialType.System_Byte
                };
                string? wireType = null;
                if (_supportedTypes.Contains(type))
                    wireType = type;
                else if (byteEnum)
                    wireType = "byte";
                if (wireType == null)
                {
                    diagnostics.Add(Diagnostic.Create(_unsupportedFieldType, p.GetLocation(), ps.Name, type));
                    continue;
                }
                fields.Add(new FieldModel(ps.Name, type, wireType));
            }
        }

        return new MessageModel(ns, name, channel, direction, fields.ToImmutable(), diagnostics.ToImmutable());
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
            spc.AddSource($"{valid[id].Name}.g.cs", SourceText.From(EmitMessage(valid[id], id), Encoding.UTF8));
        }

        spc.AddSource("NetRegistry.g.cs", SourceText.From(EmitRegistry(valid), Encoding.UTF8));
    }

    private static string EmitMessage(MessageModel m, int id)
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
        sb.AppendLine($"partial record struct {m.Name}");
        sb.AppendLine("{");

        if (toClient)
        {
            sb.AppendLine($"    /// <summary>Raised on the client when this message arrives.</summary>");
            sb.AppendLine($"    public static event global::System.Action<{m.Name}>? Received;");
            sb.AppendLine();
            sb.AppendLine($"    public void Broadcast() => NetTransport.Send(NetRegistry.ID_{m.Name}, Serialize(in this), NetTransport.BROADCAST, NetChannel.{channel});");
            sb.AppendLine();
            sb.AppendLine($"    public void SendTo(long peerId) => NetTransport.Send(NetRegistry.ID_{m.Name}, Serialize(in this), peerId, NetChannel.{channel});");
        }
        else
        {
            sb.AppendLine($"    /// <summary>Raised on the server when this message arrives; sender is the validated peer id.</summary>");
            sb.AppendLine($"    public static event global::System.Action<long, {m.Name}>? Received;");
            sb.AppendLine();
            sb.AppendLine($"    public void SendToServer() => NetTransport.Send(NetRegistry.ID_{m.Name}, Serialize(in this), NetTransport.TO_SERVER, NetChannel.{channel});");
        }

        sb.AppendLine();
        sb.AppendLine($"    internal static byte[] Serialize(in {m.Name} m)");
        sb.AppendLine("    {");
        sb.AppendLine("        using global::System.IO.MemoryStream ms = new global::System.IO.MemoryStream();");
        sb.AppendLine("        using global::System.IO.BinaryWriter w = new global::System.IO.BinaryWriter(ms);");
        foreach (FieldModel f in m.Fields)
        {
            sb.AppendLine($"        {WriteCall(f)}");
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

        sb.AppendLine();
        string invoke = toClient ? "Received?.Invoke(m)" : "Received?.Invoke(sender, m)";
        sb.AppendLine($"    internal static void Raise(long sender, in {m.Name} m) => {invoke};");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string WriteCall(FieldModel f) => f.WireType switch
    {
        "byte[]" or "int[]" or "long[]" or "string[]" => $"w.WriteArray(m.{f.Name});",
        "byte" when f.Type != "byte" => $"w.Write((byte)m.{f.Name});",
        _ => $"w.Write(m.{f.Name});",
    };

    private static string ReadCall(FieldModel f)
    {
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
        sb.AppendLine("    /// <summary>False = malformed, unknown id, or wrong direction for this side.</summary>");
        sb.AppendLine("    public static bool Dispatch(ushort msgId, long sender, byte[] payload, bool isServer)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (payload.Length > global::Mortz.Core.Net.NetConfig.MAX_ENVELOPE_BYTES)");
        sb.AppendLine("            return false;");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            using global::System.IO.MemoryStream ms = new global::System.IO.MemoryStream(payload, writable: false);");
        sb.AppendLine("            using global::System.IO.BinaryReader r = new global::System.IO.BinaryReader(ms);");
        sb.AppendLine("            switch (msgId)");
        sb.AppendLine("            {");
        foreach (MessageModel m in models)
        {
            string fqn = $"global::{m.Namespace}.{m.Name}";
            string wrongSide = m.Direction == 0 ? "isServer" : "!isServer";
            sb.AppendLine($"                case ID_{m.Name}:");
            sb.AppendLine("                {");
            sb.AppendLine($"                    if ({wrongSide}) return false;");
            sb.AppendLine($"                    {fqn} message = {fqn}.Deserialize(r);");
            sb.AppendLine("                    if (ms.Position != ms.Length) return false;");
            sb.AppendLine($"                    {fqn}.Raise(sender, message);");
            sb.AppendLine("                    return true;");
            sb.AppendLine("                }");
        }
        sb.AppendLine("                default:");
        sb.AppendLine("                    return false;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("        catch (global::System.IO.IOException)");
        sb.AppendLine("        {");
        sb.AppendLine("            return false;");
        sb.AppendLine("        }");
        sb.AppendLine("        catch (global::System.IO.InvalidDataException)");
        sb.AppendLine("        {");
        sb.AppendLine("            return false;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static ulong SchemaHash(MessageModel[] models)
    {
        // FNV-1a 64. Field names deliberately excluded: renames don't change the wire.
        ulong hash = 14695981039346656037UL;
        foreach (MessageModel m in models)
        {
            string signature =
                $"{m.Namespace}.{m.Name}({string.Join(",", m.Fields.Select(f => f.WireType))})|{m.Channel}|{m.Direction}";
            foreach (byte b in Encoding.UTF8.GetBytes(signature))
            {
                hash ^= b;
                hash *= 1099511628211UL;
            }
        }
        return hash;
    }
}
