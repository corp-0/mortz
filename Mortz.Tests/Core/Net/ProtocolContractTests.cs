using Mortz.Core.Net;
using Xunit;

namespace Mortz.Tests.Core.Net;

public class ProtocolContractTests
{
    [Fact]
    public void IntentionalLiveSnapshotAndTypedRowBreakIsVersion43()
    {
        Assert.Equal(43, NetConfig.PROTOCOL_VERSION);
    }

    [Fact]
    public void GeneratedMessageSchemaMatchesVersion43()
    {
        Assert.Equal(0x0AFA6A230A157A23UL, NetRegistry.SCHEMA_HASH);
    }
}
