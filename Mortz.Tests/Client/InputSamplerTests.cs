using Mortz.Client;
using Mortz.Client.Chat;
using Mortz.Client.Match;
using Mortz.Core.Sim;
using Xunit;

namespace Mortz.Tests.Client;

public class InputSamplerTests
{
    [Fact]
    public void TypingSuppressesEveryGameplayButton()
    {
        object owner = new();
        ChatInputGuard.SetTyping(owner, true);
        try
        {
            Assert.Equal(InputButtons.NONE, InputSampler.Sample());
        }
        finally
        {
            ChatInputGuard.SetTyping(owner, false);
        }
    }
}
