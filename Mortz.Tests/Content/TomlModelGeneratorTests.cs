using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mortz.Net.Gen;
using Xunit;

namespace Mortz.Tests.Content;

public sealed class TomlModelGeneratorTests
{
    [Fact]
    public void EmitsDirectReaderAndWriterForValidModel()
    {
        GeneratorDriverRunResult result = Run("""
            namespace Mortz.Content;
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class TomlModelAttribute : System.Attribute { }
            [TomlModel]
            public sealed record Pack(string Id, int Count);
            """);

        Assert.Empty(result.Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error));
        string generated = result.GeneratedTrees.Single().ToString();
        Assert.Contains("new global::Mortz.Content.Pack(v0, v1)", generated);
        Assert.Contains("TomlModel.Scalar<string>", generated);
        Assert.Contains("value.Id", generated);
    }

    [Fact]
    public void ReportsInvalidRootAtCompileTime()
    {
        GeneratorDriverRunResult result = Run("""
            namespace Mortz.Content;
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class TomlModelAttribute : System.Attribute { }
            [TomlModel]
            public sealed record Broken(int[,] Values);
            """);

        Assert.Contains(result.Diagnostics, x => x.Id == "MZ4001");
    }

    private static GeneratorDriverRunResult Run(string source)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { typeof(object).Assembly.Location };
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string platform)
        {
            foreach (string path in platform.Split(Path.PathSeparator))
            {
                if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) references.Add(path);
            }
        }

        CSharpCompilation compilation = CSharpCompilation.Create($"TomlGen_{Guid.NewGuid():N}",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            references.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new TomlModelGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }
}
