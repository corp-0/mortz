using System.Collections.Immutable;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Mortz.Core.Net;
using Mortz.Net.Gen;

namespace Mortz.Tests.Core.Net;

internal readonly record struct NetGenResult(
    ImmutableArray<Diagnostic> Diagnostics,
    string Sources,
    Assembly? Assembly);

/// <summary>
/// Compiles a source string with Mortz.Net.Gen attached and loads the result, so
/// the generator can be tested on shapes production does not have yet. Attaching
/// the generator to this assembly instead would emit a second NetRegistry in
/// Mortz.Core.Net and shadow the real one.
/// </summary>
internal static class NetGenHarness
{
    private static readonly MetadataReference[] _references = BuildReferences();

    public static NetGenResult Run(string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            $"NetGen_{Guid.NewGuid():N}",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            _references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver
            .Create(new NetMessageGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out Compilation generated, out _);
        GeneratorDriverRunResult run = driver.GetRunResult();

        using var pe = new MemoryStream();
        EmitResult emit = generated.Emit(pe);
        ImmutableArray<Diagnostic> diagnostics =
        [
            .. run.Diagnostics,
            .. emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
        ];
        Assembly? assembly = null;
        if (emit.Success)
            assembly = Assembly.Load(pe.ToArray());
        return new NetGenResult(
            diagnostics,
            string.Join("\n", run.GeneratedTrees.Select(t => t.ToString())),
            assembly);
    }

    /// <summary>The SCHEMA_HASH literal the run put in NetRegistry.</summary>
    public static string SchemaHash(string sources)
    {
        System.Text.RegularExpressions.Match match =
            Regex.Match(sources, @"SCHEMA_HASH = (0x[0-9A-F]{16}UL);");
        if (!match.Success)
            throw new InvalidOperationException("no SCHEMA_HASH in the generated sources");
        return match.Groups[1].Value;
    }

    private static MetadataReference[] BuildReferences()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            typeof(object).Assembly.Location,
            typeof(NetRegistry).Assembly.Location,
        };
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string platform)
        {
            foreach (string path in platform.Split(Path.PathSeparator))
            {
                if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                    paths.Add(path);
            }
        }
        return [.. paths.Select(p => MetadataReference.CreateFromFile(p))];
    }
}
