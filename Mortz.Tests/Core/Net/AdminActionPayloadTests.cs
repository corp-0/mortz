using Mortz.Core.Net.Lobby;
using Mortz.Core.Net.Match;
using Xunit;

namespace Mortz.Tests.Core.Net;

public class AdminActionPayloadTests
{
    [Fact]
    public void SetLobbyMapPayloadHasFixedEncoding()
    {
        Assert.Equal(2, SetLobbyMapAction.ACTION);
        Assert.Equal("6D6170732F6361C3B1C3B36E",
            Convert.ToHexString(SetLobbyMapAction.SignablePayload("maps/cañón")));
    }

    [Fact]
    public void SetLobbyModePayloadHasFixedEncoding()
    {
        Assert.Equal(4, SetLobbyModeAction.ACTION);
        Assert.Equal("636C61737369632F6475656C",
            Convert.ToHexString(SetLobbyModeAction.SignablePayload("classic/duel")));
    }

    [Fact]
    public void ReplaceLobbyRulesPayloadHasFixedEncoding()
    {
        byte[] config = [0x00, 0x7F, 0x80, 0xFF];

        byte[] payload = ReplaceLobbyRulesAction.SignablePayload(config);

        Assert.Equal(1, ReplaceLobbyRulesAction.ACTION);
        Assert.Equal("007F80FF", Convert.ToHexString(payload));
        Assert.NotSame(config, payload);
    }

    [Fact]
    public void EndMatchPayloadHasFixedEncoding()
    {
        Assert.Equal(3, EndMatchAction.ACTION);
        Assert.Empty(EndMatchAction.SignablePayload());
    }
}
