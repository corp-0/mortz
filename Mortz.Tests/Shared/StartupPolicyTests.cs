using Mortz.Shared;
using Xunit;

namespace Mortz.Tests.Shared;

public class StartupPolicyTests
{
    [Fact]
    public void DebugCarve_IsEnabledOnlyByExplicitFlag()
    {
        Assert.False(CmdArgs.HasFlag([], "--enable-debug-carve"));
        Assert.False(CmdArgs.HasFlag(["--server", "--debug"], "--enable-debug-carve"));
        Assert.True(CmdArgs.HasFlag(["--server", "--enable-debug-carve"], "--enable-debug-carve"));
    }

    [Fact]
    public void ContentRootValue_IsReadFromExplicitArguments()
    {
        Assert.Equal("D:/portable/content",
            CmdArgs.GetValue(["--server", "--content-root", "D:/portable/content"], "--content-root"));
        Assert.Null(CmdArgs.GetValue(["--server", "--content-root"], "--content-root"));
    }
}
