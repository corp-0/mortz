using Mortz.Protocol.Net;
using Xunit;

namespace Mortz.Runtime.Tests.Core.Net;

public class ProtocolContractTests
{
    [Fact]
    public void AdmissionHandshakeBreakIsVersion47()
    {
        Assert.Equal(48, NetConfig.PROTOCOL_VERSION);
    }

    [Fact]
    public void GeneratedMessageSchemaMatchesVersion45()
    {
        Assert.Equal(0xA3F539E47FD5609AUL, NetRegistry.SCHEMA_HASH);
    }
}
