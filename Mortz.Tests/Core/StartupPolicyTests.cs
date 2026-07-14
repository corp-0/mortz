using Mortz.Shared;
using Xunit;

namespace Mortz.Tests.Core;

public class StartupPolicyTests
{
    [Fact]
    public void DebugCarve_IsEnabledOnlyByExplicitFlag()
    {
        Assert.False(CmdArgs.HasFlag([], "--enable-debug-carve"));
        Assert.False(CmdArgs.HasFlag(["--server", "--debug"], "--enable-debug-carve"));
        Assert.True(CmdArgs.HasFlag(["--server", "--enable-debug-carve"], "--enable-debug-carve"));
    }
}
