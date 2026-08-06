using Mortz.Content;
using Mortz.Server.Hosting;
using Xunit;

namespace Mortz.Tests.Server;

public sealed class ServerConfigTests
{
    [Fact]
    public void TomlModelReadsServerConfigAndKeepsDefaults()
    {
        ContentReadResult<ServerConfig> result = TomlModel.Read<ServerConfig>("""
            name = "Coffee box"
            unknown = true
            """, "server.toml");

        Assert.NotNull(result.Value);
        Assert.Equal("Coffee box", result.Value.Name);
        Assert.Equal("", result.Value.AdminPassword);
        Assert.True(result.Value.AllowJoinInProgress);
        Assert.Contains(result.Diagnostics, x => x.Message == "unknown key 'unknown'");
    }

    [Fact]
    public void InvalidServerConfigHasNoValue()
    {
        ContentReadResult<ServerConfig> result = TomlModel.Read<ServerConfig>(
            "allow_join_in_progress = \"yes\"", "server.toml");

        Assert.Null(result.Value);
        Assert.Contains(result.Diagnostics,
            x => x.Message == "'allow_join_in_progress' must be a boolean");
    }
}
