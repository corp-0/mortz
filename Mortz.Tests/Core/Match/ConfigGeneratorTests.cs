using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mortz.Net.Gen;
using Xunit;

namespace Mortz.Tests.Core.Match;

public sealed class ConfigGeneratorTests
{
    [Fact]
    public void UnprojectedMatchConfigSectionFailsGeneration()
    {
        const string SOURCE = """
            namespace Mortz.Core.Match.Configuration;

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class ConfigSectionAttribute : System.Attribute;

            public sealed class Known;
            public sealed class Missing;

            public sealed partial class MatchConfig
            {
                [ConfigSection]
                public Known Known { get; init; } = new();

                public Missing Missing { get; init; } = new();
            }
            """;

        GeneratorDriverRunResult result = Run(SOURCE);

        Diagnostic diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "MZ3007");
        Assert.Contains("MatchConfig.Missing", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void UnprojectedSectionFieldFailsGeneration()
    {
        const string SOURCE = """
            namespace Mortz.Core.Match.Configuration;

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class MatchRuleAttribute(
                float min = float.NaN,
                float max = float.NaN) : System.Attribute;

            public sealed partial class Rules
            {
                [MatchRule]
                public int Known { get; set; }

                public int Missing { get; set; }
            }
            """;

        GeneratorDriverRunResult result = Run(SOURCE);

        Diagnostic diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "MZ3007");
        Assert.Contains("Rules.Missing", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void NestedValueWithoutACompleteProjectionFailsGeneration()
    {
        const string SOURCE = """
            namespace Mortz.Core.Match.Configuration;

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class ConfigValueAttribute(
                System.Type snapshotType,
                System.Type projectionType) : System.Attribute;

            public sealed class Nested;
            public sealed record NestedSnapshot;
            public static class BrokenProjection;

            public sealed partial class Rules
            {
                [ConfigValue(typeof(NestedSnapshot), typeof(BrokenProjection))]
                public Nested Nested { get; set; } = new();
            }
            """;

        GeneratorDriverRunResult result = Run(SOURCE);

        Assert.Contains(result.Diagnostics, item => item.Id == "MZ3008");
    }

    [Fact]
    public void ConstructedGenericNestedValueUsesItsExplicitProjection()
    {
        const string SOURCE = """
            namespace Mortz.Core.Match.Configuration;

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class ConfigValueAttribute(
                System.Type snapshotType,
                System.Type projectionType) : System.Attribute;

            public sealed class Box<T>;
            public sealed record BoxSnapshot<T>(T Value);
            public static class BoxIntProjection
            {
                public static BoxSnapshot<int> ToSnapshot(Box<int> value) => new(0);
                public static Box<int> ToMutable(BoxSnapshot<int> snapshot) => new();
                public static void Clamp(Box<int> value) { }
                public static byte[] ToBytes(Box<int> value) => [];
                public static Box<int> FromBytes(byte[] data) => new();
            }

            public sealed partial class Rules
            {
                [ConfigValue(typeof(BoxSnapshot<int>), typeof(BoxIntProjection))]
                public Box<int> Box { get; set; } = new();
            }
            """;

        GeneratorDriverRunResult result = Run(SOURCE);
        string generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, item => item.Id == "MZ3008");
        Assert.Contains("global::Mortz.Core.Match.Configuration.BoxSnapshot<int> Box",
            generated, StringComparison.Ordinal);
        Assert.Contains("BoxIntProjection.ToSnapshot(Box)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void SectionDeclarationDrivesClampAndWireProjection()
    {
        const string SOURCE = """
            namespace Mortz.Core.Match.Configuration;

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class ConfigSectionAttribute : System.Attribute;

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class MatchRuleAttribute(
                float min = float.NaN,
                float max = float.NaN) : System.Attribute;

            public sealed partial class Extra
            {
                [MatchRule]
                public int Count { get; set; }
            }

            public sealed partial class MatchConfig
            {
                [ConfigSection]
                public Extra Extra { get; init; } = new();
            }
            """;

        GeneratorDriverRunResult result = Run(SOURCE);
        string generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, item => item.Severity == DiagnosticSeverity.Error);
        Assert.Contains("Extra.Clamp();", generated, StringComparison.Ordinal);
        Assert.Contains("Extra.ToBytes(),", generated, StringComparison.Ordinal);
        Assert.Contains("Extra = global::Mortz.Core.Match.Configuration.Extra.FromBytes(",
            generated, StringComparison.Ordinal);
    }

    private static GeneratorDriverRunResult Run(string source)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            typeof(object).Assembly.Location,
        };
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string platform)
        {
            foreach (string path in platform.Split(Path.PathSeparator))
            {
                if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    references.Add(path);
            }
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            $"ConfigGen_{Guid.NewGuid():N}",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            references.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ConfigGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }
}
