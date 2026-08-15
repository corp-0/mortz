using Microsoft.CodeAnalysis;
using Xunit;

namespace Mortz.Tests.Core.Net;

/// <summary>What the schema hash must notice, plus the error for a message the
/// generator cannot encode. Both read the generated text instead of running it.</summary>
public class NetGeneratorSchemaTests
{
    private const string ROW_MESSAGE = """
        using Mortz.Core.Net;

        namespace GenTest;

        [NetRow]
        public readonly partial record struct Pair({0});

        [NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
        public readonly partial record struct PairMsg(Pair[] Pairs);
        """;

    private const string SCALAR_MESSAGE = """
        using Mortz.Core.Net;

        namespace GenTest;

        [NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
        public readonly partial record struct ScalarMsg({0});
        """;

    private const string STANDALONE_ROW = """
        using Mortz.Core.Net;

        namespace GenTest;

        [NetRow]
        public readonly partial record struct Pair({0});

        [NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
        public readonly partial record struct ScalarMsg(int Sequence);
        """;

    private const string ZERO_VALUED_ENUM = """
        using Mortz.Core.Net;

        namespace GenTest;

        public enum Tint : byte
        {
            NONE = 0,
            RED = 1,
        }

        [NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
        public readonly partial record struct TintMsg(Tint? Colour);
        """;

    private const string CLIENT_MESSAGE = """
        using Mortz.Core.Net;

        namespace GenTest;

        [NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
        public readonly partial record struct ReadyMsg(bool Ready);
        """;

    [Fact]
    public void ReorderingARowsFieldsChangesTheSchemaHash()
    {
        string before = Hash(ROW_MESSAGE, "int PeerId, int PingMs");
        string after = Hash(ROW_MESSAGE, "int PingMs, int PeerId");
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void ReorderingFieldsInAStandaloneRowChangesTheSchemaHash()
    {
        string before = Hash(STANDALONE_ROW, "int PeerId, int PingMs");
        string after = Hash(STANDALONE_ROW, "int PingMs, int PeerId");
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void ReorderingAMessagesFieldsChangesTheSchemaHash()
    {
        string before = Hash(SCALAR_MESSAGE, "int Kills, int Deaths");
        string after = Hash(SCALAR_MESSAGE, "int Deaths, int Kills");
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void RenamingAFieldChangesTheSchemaHash()
    {
        string before = Hash(SCALAR_MESSAGE, "int Kills, int Deaths");
        string after = Hash(SCALAR_MESSAGE, "int Kills, int Losses");
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void RetypingAFieldChangesTheSchemaHash()
    {
        string before = Hash(SCALAR_MESSAGE, "int Kills, int Deaths");
        string after = Hash(SCALAR_MESSAGE, "int Kills, long Deaths");
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void ANullableEnumWithAZeroMemberIsRejected()
    {
        NetGenResult result = NetGenHarness.Run(ZERO_VALUED_ENUM);
        Diagnostic reported = Assert.Single(result.Diagnostics, d => d.Id == "MZ0004");
        Assert.Equal(DiagnosticSeverity.Error, reported.Severity);
        Assert.Contains("GenTest.Tint", reported.GetMessage());
    }

    [Fact]
    public void ClientConvenienceSendRequiresAnExplicitSender()
    {
        string generated = NetGenHarness.Run(CLIENT_MESSAGE).Sources;

        Assert.Contains("SendToServer(IClientSender sender)", generated,
            StringComparison.Ordinal);
        Assert.DoesNotContain("NetTransport", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("SendToServer()", generated, StringComparison.Ordinal);
    }

    private static string Hash(string template, string fields) =>
        NetGenHarness.SchemaHash(NetGenHarness.Run(string.Format(template, fields)).Sources);
}
