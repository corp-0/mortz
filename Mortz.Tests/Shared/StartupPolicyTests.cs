using Mortz.Shared;
using Xunit;

namespace Mortz.Tests.Shared;

public class StartupPolicyTests
{
    [Fact]
    public void ContentRootValue_IsReadFromExplicitArguments()
    {
        Assert.Equal("D:/portable/content",
            CmdArgs.GetValue(["--server", "--content-root", "D:/portable/content"], "--content-root"));
        Assert.Null(CmdArgs.GetValue(["--server", "--content-root"], "--content-root"));
    }
}
